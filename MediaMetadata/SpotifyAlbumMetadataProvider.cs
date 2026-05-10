using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

namespace AtlasAI.MediaMetadata
{
    public sealed class SpotifyAlbumMetadataProvider : IMusicAlbumMetadataProvider
    {
        private static readonly HttpClient _http = new HttpClient();
        private static readonly object _lock = new object();
        private static string _token = "";
        private static DateTime _tokenExpiryUtc = DateTime.MinValue;

        public string Name => "Spotify";
        public bool IsConfigured
        {
            get
            {
                var id = MusicMetadataHub.GetKey("spotify_client_id");
                var secret = MusicMetadataHub.GetKey("spotify_client_secret");
                return !string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(secret);
            }
        }

        public async Task<MusicAlbumMetadata?> TryGetAlbumAsync(MusicAlbumQuery query, CancellationToken ct)
        {
            if (!IsConfigured) return null;
            var artist = (query?.Artist ?? "").Trim();
            var album = (query?.Album ?? "").Trim();
            if (string.IsNullOrWhiteSpace(album)) return null;

            try
            {
                var token = await GetTokenAsync(ct).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(token)) return null;

                var q = string.IsNullOrWhiteSpace(artist)
                    ? $"album:\"{album}\""
                    : $"album:\"{album}\" artist:\"{artist}\"";

                var url = "https://api.spotify.com/v1/search?type=album&limit=8&q=" + HttpUtility.UrlEncode(q);
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                using var res = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                if (!res.IsSuccessStatusCode) return null;
                var json = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("albums", out var albumsEl) || albumsEl.ValueKind != JsonValueKind.Object) return null;
                if (!albumsEl.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array) return null;

                string bestId = "";
                string bestUrl = "";
                string bestDate = "";
                var bestScore = 0.0;
                var qArtistTokens = Tokenize(artist);
                var qAlbumTokens = Tokenize(album);

                foreach (var it in items.EnumerateArray())
                {
                    var id = it.TryGetProperty("id", out var idEl) ? (idEl.GetString() ?? "").Trim() : "";
                    if (string.IsNullOrWhiteSpace(id)) continue;
                    var name = it.TryGetProperty("name", out var nEl) ? (nEl.GetString() ?? "") : "";
                    var relDate = it.TryGetProperty("release_date", out var rdEl) ? (rdEl.GetString() ?? "") : "";
                    var extUrl = "";
                    if (it.TryGetProperty("external_urls", out var exEl) && exEl.ValueKind == JsonValueKind.Object &&
                        exEl.TryGetProperty("spotify", out var spEl) && spEl.ValueKind == JsonValueKind.String)
                        extUrl = (spEl.GetString() ?? "").Trim();

                    var sAlbum = Similarity(qAlbumTokens, Tokenize(name));
                    if (sAlbum < 0.40) continue;

                    var sArtist = 1.0;
                    if (!string.IsNullOrWhiteSpace(artist) &&
                        it.TryGetProperty("artists", out var ars) && ars.ValueKind == JsonValueKind.Array)
                    {
                        var bestArtist = 0.0;
                        foreach (var a in ars.EnumerateArray())
                        {
                            var an = a.TryGetProperty("name", out var anEl) ? (anEl.GetString() ?? "") : "";
                            bestArtist = Math.Max(bestArtist, Similarity(qArtistTokens, Tokenize(an)));
                        }
                        sArtist = bestArtist;
                        if (sArtist < 0.35) continue;
                    }

                    var score = (sArtist * 0.45) + (sAlbum * 0.55);
                    if (score <= bestScore) continue;
                    bestScore = score;
                    bestId = id;
                    bestUrl = extUrl;
                    bestDate = relDate;
                }

                if (string.IsNullOrWhiteSpace(bestId)) return null;

                var tracksUrl = $"https://api.spotify.com/v1/albums/{bestId}/tracks?limit=50";
                using var trReq = new HttpRequestMessage(HttpMethod.Get, tracksUrl);
                trReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                using var trRes = await _http.SendAsync(trReq, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                if (!trRes.IsSuccessStatusCode) return null;
                var trJson = await trRes.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                using var trDoc = JsonDocument.Parse(trJson);
                if (!trDoc.RootElement.TryGetProperty("items", out var trItems) || trItems.ValueKind != JsonValueKind.Array) return null;

                var list = new List<Views.ViewModels.AlbumDetailTrack>();
                foreach (var t in trItems.EnumerateArray())
                {
                    var title = t.TryGetProperty("name", out var tn) ? (tn.GetString() ?? "").Trim() : "";
                    if (string.IsNullOrWhiteSpace(title)) continue;
                    var trackNo = t.TryGetProperty("track_number", out var tno) && tno.TryGetInt32(out var tnov) ? tnov : 0;
                    var discNo = t.TryGetProperty("disc_number", out var dno) && dno.TryGetInt32(out var dnov) ? dnov : 1;
                    var artistName = "";
                    if (t.TryGetProperty("artists", out var ars) && ars.ValueKind == JsonValueKind.Array)
                    {
                        var names = ars.EnumerateArray()
                            .Select(a => a.TryGetProperty("name", out var nn) ? (nn.GetString() ?? "") : "")
                            .Where(s => !string.IsNullOrWhiteSpace(s))
                            .ToList();
                        if (names.Count > 0) artistName = string.Join(", ", names);
                    }
                    list.Add(new Views.ViewModels.AlbumDetailTrack
                    {
                        DiscNumber = discNo <= 0 ? 1 : discNo,
                        TrackNumber = trackNo,
                        Title = title,
                        Artist = artistName,
                        RecordingId = ""
                    });
                }

                var meta = new MusicAlbumMetadata
                {
                    Provider = Name,
                    ReleaseId = bestId,
                    ReleaseUrl = bestUrl,
                    ReleaseDate = (bestDate ?? "").Trim(),
                    Tracks = list.Where(x => !string.IsNullOrWhiteSpace(x.Title)).OrderBy(x => x.DiscNumber).ThenBy(x => x.TrackNumber).ToList()
                };

                if (!string.IsNullOrWhiteSpace(meta.ReleaseDate) && meta.ReleaseDate.Length >= 4 &&
                    int.TryParse(meta.ReleaseDate.Substring(0, 4), out var y) && y > 0)
                    meta.Year = y;

                return meta;
            }
            catch
            {
                return null;
            }
        }

        public Task<bool> TryDownloadCoverAsync(MusicAlbumQuery query, string destinationPath, CancellationToken ct)
        {
            return Task.FromResult(false);
        }

        private static async Task<string> GetTokenAsync(CancellationToken ct)
        {
            lock (_lock)
            {
                if (!string.IsNullOrWhiteSpace(_token) && DateTime.UtcNow < _tokenExpiryUtc.AddSeconds(-30))
                    return _token;
            }

            var id = MusicMetadataHub.GetKey("spotify_client_id");
            var secret = MusicMetadataHub.GetKey("spotify_client_secret");
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(secret)) return "";

            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Post, "https://accounts.spotify.com/api/token");
                var auth = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{id}:{secret}"));
                req.Headers.Authorization = new AuthenticationHeaderValue("Basic", auth);
                req.Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["grant_type"] = "client_credentials" });
                using var res = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                if (!res.IsSuccessStatusCode) return "";
                var json = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                var token = doc.RootElement.TryGetProperty("access_token", out var tEl) ? (tEl.GetString() ?? "") : "";
                var exp = doc.RootElement.TryGetProperty("expires_in", out var eEl) && eEl.TryGetInt32(out var s) ? s : 0;
                if (string.IsNullOrWhiteSpace(token)) return "";
                lock (_lock)
                {
                    _token = token;
                    _tokenExpiryUtc = DateTime.UtcNow.AddSeconds(Math.Max(60, exp));
                }
                return token;
            }
            catch
            {
                return "";
            }
        }

        private static HashSet<string> Tokenize(string s)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(s)) return set;
            var chars = s.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : ' ').ToArray();
            var parts = new string(chars).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var p in parts)
            {
                if (p.Length < 2) continue;
                set.Add(p);
            }
            return set;
        }

        private static double Similarity(HashSet<string> a, HashSet<string> b)
        {
            if (a.Count == 0 || b.Count == 0) return 0;
            var inter = 0;
            foreach (var t in a)
            {
                if (b.Contains(t)) inter++;
            }
            var denom = Math.Max(a.Count, b.Count);
            return denom <= 0 ? 0 : (double)inter / denom;
        }
    }
}
