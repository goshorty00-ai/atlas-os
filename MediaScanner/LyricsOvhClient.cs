using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

namespace AtlasAI.MediaScanner
{
    public sealed class LyricsOvhClient
    {
        private static readonly HttpClient _http = new HttpClient();

        public static LyricsOvhClient Instance { get; } = new LyricsOvhClient();

        private LyricsOvhClient()
        {
        }

        public async Task<string?> TryGetLyricsAsync(string artist, string title, CancellationToken ct)
        {
            var a = (artist ?? "").Trim();
            var t = (title ?? "").Trim();
            if (string.IsNullOrWhiteSpace(t) || string.IsNullOrWhiteSpace(a)) return null;

            try
            {
                var url = "https://api.lyrics.ovh/v1/" + HttpUtility.UrlEncode(a) + "/" + HttpUtility.UrlEncode(t);
                using var res = await _http.GetAsync(url, ct).ConfigureAwait(false);
                if (!res.IsSuccessStatusCode) return null;
                var json = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(json)) return null;
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("lyrics", out var lEl) && lEl.ValueKind == JsonValueKind.String)
                {
                    var lyrics = (lEl.GetString() ?? "").Trim();
                    return string.IsNullOrWhiteSpace(lyrics) ? null : lyrics;
                }
                return null;
            }
            catch
            {
                return null;
            }
        }
    }
}
