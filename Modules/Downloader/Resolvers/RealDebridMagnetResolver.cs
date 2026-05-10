using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AtlasAI.Modules.Downloader.Resolvers
{
    public sealed class RealDebridMagnetResolver : ILinkResolver
    {
        private readonly HttpClient _http;
        private readonly Func<string> _tokenProvider;

        public RealDebridMagnetResolver(HttpClient http, Func<string> tokenProvider)
        {
            _http = http;
            _tokenProvider = tokenProvider;
        }

        public string Name => "RealDebridMagnet";
        public bool IsEnabled { get; set; } = true;
        public int Priority => 110;

        public bool CanHandle(Uri input)
        {
            return input.Scheme.Equals("magnet", StringComparison.OrdinalIgnoreCase);
        }

        public async Task<ResolvedLink?> ResolveAsync(Uri input, CancellationToken ct)
        {
            var token = (_tokenProvider() ?? "").Trim();
            if (string.IsNullOrWhiteSpace(token)) return null;

            var torrentId = await AddMagnetAsync(token, input.ToString(), ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(torrentId)) return null;

            await SelectAllFilesAsync(token, torrentId, ct).ConfigureAwait(false);

            var link = await TryGetAnyLinkAsync(token, torrentId, ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(link)) return null;

            var (downloadUrl, filename) = await UnrestrictAsync(token, link, ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(downloadUrl)) return null;

            if (!Uri.TryCreate(downloadUrl, UriKind.Absolute, out var direct) ||
                (!direct.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase) && !direct.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase)))
                return null;

            return new ResolvedLink
            {
                DirectUrl = direct,
                ResolverName = "RealDebrid",
                SuggestedFilename = string.IsNullOrWhiteSpace(filename) ? null : filename
            };
        }

        private async Task<string?> AddMagnetAsync(string token, string magnet, CancellationToken ct)
        {
            var req = new HttpRequestMessage(HttpMethod.Post, "https://api.real-debrid.com/rest/1.0/torrents/addMagnet");
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            req.Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["magnet"] = magnet
            });

            using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return null;

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String)
                return (idEl.GetString() ?? "").Trim();
            return null;
        }

        private async Task SelectAllFilesAsync(string token, string torrentId, CancellationToken ct)
        {
            var url = $"https://api.real-debrid.com/rest/1.0/torrents/selectFiles/{Uri.EscapeDataString(torrentId)}";
            var req = new HttpRequestMessage(HttpMethod.Post, url);
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            req.Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["files"] = "all"
            });

            using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            _ = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        }

        private async Task<string?> TryGetAnyLinkAsync(string token, string torrentId, CancellationToken ct)
        {
            var url = $"https://api.real-debrid.com/rest/1.0/torrents/info/{Uri.EscapeDataString(torrentId)}";

            for (var i = 0; i < 24; i++)
            {
                ct.ThrowIfCancellationRequested();

                var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

                using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode) return null;

                try
                {
                    using var doc = JsonDocument.Parse(body);
                    var root = doc.RootElement;
                    if (root.TryGetProperty("links", out var linksEl) && linksEl.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var l in linksEl.EnumerateArray())
                        {
                            if (l.ValueKind != JsonValueKind.String) continue;
                            var s = (l.GetString() ?? "").Trim();
                            if (!string.IsNullOrWhiteSpace(s)) return s;
                        }
                    }
                }
                catch
                {
                }

                await Task.Delay(500, ct).ConfigureAwait(false);
            }

            return null;
        }

        private async Task<(string? url, string? filename)> UnrestrictAsync(string token, string link, CancellationToken ct)
        {
            var req = new HttpRequestMessage(HttpMethod.Post, "https://api.real-debrid.com/rest/1.0/unrestrict/link");
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            req.Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["link"] = link
            });

            using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return (null, null);

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            var download = root.TryGetProperty("download", out var d) ? d.GetString() : null;
            var filename = root.TryGetProperty("filename", out var f) ? f.GetString() : null;
            return ((download ?? "").Trim(), (filename ?? "").Trim());
        }
    }
}
