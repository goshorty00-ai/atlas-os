using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AtlasAI.Modules.Downloader.Resolvers
{
    public class RealDebridResolver : ILinkResolver
    {
        private readonly HttpClient _http;
        private readonly Func<string> _tokenProvider;

        public RealDebridResolver(HttpClient http, Func<string> tokenProvider)
        {
            _http = http;
            _tokenProvider = tokenProvider;
        }

        public string Name => "RealDebrid";
        public bool IsEnabled { get; set; } = true;
        public int Priority => 100;

        public bool CanHandle(Uri input)
        {
            return input.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase) ||
                   input.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase);
        }

        public async Task<ResolvedLink?> ResolveAsync(Uri input, CancellationToken ct)
        {
            var token = (_tokenProvider() ?? "").Trim();
            if (string.IsNullOrWhiteSpace(token)) return null;

            var req = new HttpRequestMessage(HttpMethod.Post, "https://api.real-debrid.com/rest/1.0/unrestrict/link");
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            req.Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["link"] = input.ToString()
            });

            using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode) return null;

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            var download = root.TryGetProperty("download", out var d) ? d.GetString() : null;
            if (string.IsNullOrWhiteSpace(download)) return null;

            var filename = root.TryGetProperty("filename", out var f) ? f.GetString() : null;

            return new ResolvedLink
            {
                DirectUrl = new Uri(download),
                ResolverName = Name,
                SuggestedFilename = filename
            };
        }

        public async Task<(bool ok, string message)> TestTokenAsync(CancellationToken ct)
        {
            var token = (_tokenProvider() ?? "").Trim();
            if (string.IsNullOrWhiteSpace(token)) return (false, "Token is empty.");

            var req = new HttpRequestMessage(HttpMethod.Get, "https://api.real-debrid.com/rest/1.0/user");
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

            using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!resp.IsSuccessStatusCode) return (false, $"HTTP {(int)resp.StatusCode}");
            return (true, "OK");
        }
    }
}

