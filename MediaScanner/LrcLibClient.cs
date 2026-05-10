using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

namespace AtlasAI.MediaScanner
{
    public sealed class LrcLibClient
    {
        private static readonly HttpClient _http = new HttpClient();

        public static LrcLibClient Instance { get; } = new LrcLibClient();

        private LrcLibClient()
        {
        }

        public async Task<string?> TryGetLyricsAsync(string artist, string title, string? album, CancellationToken ct)
        {
            var a = (artist ?? "").Trim();
            var t = (title ?? "").Trim();
            var al = (album ?? "").Trim();
            if (string.IsNullOrWhiteSpace(t)) return null;

            try
            {
                var url = "https://lrclib.net/api/get?" +
                          "track_name=" + HttpUtility.UrlEncode(t) +
                          (string.IsNullOrWhiteSpace(a) ? "" : "&artist_name=" + HttpUtility.UrlEncode(a)) +
                          (string.IsNullOrWhiteSpace(al) ? "" : "&album_name=" + HttpUtility.UrlEncode(al));

                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.TryAddWithoutValidation("User-Agent", "AtlasAI/1.0");
                using var res = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                if (!res.IsSuccessStatusCode) return null;

                var json = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(json)) return null;

                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                string? synced = null;
                string? plain = null;

                if (root.TryGetProperty("syncedLyrics", out var sEl) && sEl.ValueKind == JsonValueKind.String)
                    synced = (sEl.GetString() ?? "").Trim();
                if (root.TryGetProperty("plainLyrics", out var pEl) && pEl.ValueKind == JsonValueKind.String)
                    plain = (pEl.GetString() ?? "").Trim();

                if (!string.IsNullOrWhiteSpace(synced)) return synced;
                if (!string.IsNullOrWhiteSpace(plain)) return plain;
                return null;
            }
            catch
            {
                return null;
            }
        }
    }
}
