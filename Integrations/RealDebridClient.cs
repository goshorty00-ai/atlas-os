using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AtlasAI.Integrations
{
    public sealed class RealDebridClient
    {
        private readonly HttpClient _httpClient;

        public RealDebridClient(string apiToken, HttpClient? httpClient = null)
        {
            _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            if (_httpClient.BaseAddress == null)
                _httpClient.BaseAddress = new Uri("https://api.real-debrid.com/rest/1.0/");
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiToken);
        }

        public async Task<string?> UnrestrictLinkAsync(string link, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(link)) return null;

            using var content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("link", link)
            });

            using var response = await _httpClient.PostAsync("unrestrict/link", content, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return null;

            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(json)) return null;

            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;
                if (!doc.RootElement.TryGetProperty("download", out var downloadEl)) return null;
                var download = downloadEl.GetString();
                return string.IsNullOrWhiteSpace(download) ? null : download;
            }
            catch
            {
                return null;
            }
        }
    }
}
