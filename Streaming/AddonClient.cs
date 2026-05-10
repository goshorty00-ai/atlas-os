using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AtlasAI.Streaming
{
    public sealed class AddonClient
    {
        private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
        private readonly SemaphoreSlim _throttler;

        public AddonClient(int maxConcurrentRequests = 8)
        {
            _throttler = new SemaphoreSlim(Math.Max(1, maxConcurrentRequests));
            try
            {
                Http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "AtlasAI/1.0");
                Http.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json");
            }
            catch
            {
            }
        }

        public async Task<JsonDocument?> GetJsonAsync(string url, TimeSpan timeout, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(url))
                return null;

            await _throttler.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                if (timeout > TimeSpan.Zero)
                    timeoutCts.CancelAfter(timeout);
                var tct = timeoutCts.Token;

                using var resp = await Http.GetAsync(url, tct).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode)
                    return null;

                await using var stream = await resp.Content.ReadAsStreamAsync(tct).ConfigureAwait(false);
                return await JsonDocument.ParseAsync(stream, cancellationToken: tct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                if (ct.IsCancellationRequested) throw;
                return null;
            }
            catch
            {
                return null;
            }
            finally
            {
                try { _throttler.Release(); } catch { }
            }
        }
    }
}

