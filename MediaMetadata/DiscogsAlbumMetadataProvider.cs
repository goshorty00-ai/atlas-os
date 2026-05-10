using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

namespace AtlasAI.MediaMetadata
{
    public sealed class DiscogsAlbumMetadataProvider : IMusicAlbumMetadataProvider
    {
        private static readonly HttpClient _http = new HttpClient();

        public string Name => "Discogs";
        public bool IsConfigured => !string.IsNullOrWhiteSpace(MusicMetadataHub.GetKey("discogs_token"));

        public async Task<MusicAlbumMetadata?> TryGetAlbumAsync(MusicAlbumQuery query, CancellationToken ct)
        {
            if (!IsConfigured) return null;
            var artist = (query?.Artist ?? "").Trim();
            var album = (query?.Album ?? "").Trim();
            if (string.IsNullOrWhiteSpace(album)) return null;

            try
            {
                var token = MusicMetadataHub.GetKey("discogs_token");
                if (string.IsNullOrWhiteSpace(token)) return null;
                var q = string.IsNullOrWhiteSpace(artist) ? album : $"{artist} {album}";
                var url = "https://api.discogs.com/database/search?type=release&per_page=8&q=" + HttpUtility.UrlEncode(q) + "&token=" + HttpUtility.UrlEncode(token);

                using var res = await _http.GetAsync(url, ct).ConfigureAwait(false);
                if (!res.IsSuccessStatusCode) return null;
                var json = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array) return null;

                long bestId = 0;
                var bestScore = 0.0;
                var qArtistTokens = Tokenize(artist);
                var qAlbumTokens = Tokenize(album);
                var expectedYear = query?.ExpectedYear ?? 0;

                foreach (var r in results.EnumerateArray())
                {
                    if (r.ValueKind != JsonValueKind.Object) continue;
                    var id = r.TryGetProperty("id", out var idEl) && idEl.TryGetInt64(out var idv) ? idv : 0;
                    if (id <= 0) continue;
                    var title = r.TryGetProperty("title", out var tEl) ? (tEl.GetString() ?? "") : "";
                    var year = r.TryGetProperty("year", out var yEl) && yEl.TryGetInt32(out var yv) ? yv : 0;
                    var sAlbum = Similarity(qAlbumTokens, Tokenize(title));
                    if (sAlbum < 0.35) continue;
                    var sArtist = string.IsNullOrWhiteSpace(artist) ? 1.0 : Similarity(qArtistTokens, Tokenize(title));
                    var score = (sArtist * 0.35) + (sAlbum * 0.65);
                    if (expectedYear > 0 && year > 0)
                    {
                        var ydiff = Math.Abs(expectedYear - year);
                        score += Math.Max(0, 0.25 - (ydiff * 0.05));
                    }
                    if (score <= bestScore) continue;
                    bestScore = score;
                    bestId = id;
                }

                if (bestId <= 0) return null;

                var relUrl = "https://api.discogs.com/releases/" + bestId;
                using var relRes = await _http.GetAsync(relUrl, ct).ConfigureAwait(false);
                if (!relRes.IsSuccessStatusCode) return null;
                var relJson = await relRes.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                using var relDoc = JsonDocument.Parse(relJson);

                var meta = new MusicAlbumMetadata { Provider = Name };
                meta.ReleaseId = bestId.ToString();
                meta.ReleaseUrl = "https://www.discogs.com/release/" + bestId;

                if (relDoc.RootElement.TryGetProperty("released", out var rdEl) && rdEl.ValueKind == JsonValueKind.String)
                {
                    meta.ReleaseDate = (rdEl.GetString() ?? "").Trim();
                    if (meta.ReleaseDate.Length >= 4 && int.TryParse(meta.ReleaseDate.Substring(0, 4), out var y) && y > 0) meta.Year = y;
                }

                if (relDoc.RootElement.TryGetProperty("labels", out var labs) && labs.ValueKind == JsonValueKind.Array)
                {
                    var first = labs.EnumerateArray().FirstOrDefault();
                    if (first.ValueKind == JsonValueKind.Object && first.TryGetProperty("name", out var ln))
                        meta.Label = (ln.GetString() ?? "").Trim();
                }

                if (relDoc.RootElement.TryGetProperty("genres", out var gens) && gens.ValueKind == JsonValueKind.Array)
                {
                    foreach (var g in gens.EnumerateArray())
                    {
                        var s = (g.GetString() ?? "").Trim();
                        if (!string.IsNullOrWhiteSpace(s)) meta.Genres.Add(s);
                    }
                }

                if (relDoc.RootElement.TryGetProperty("tracklist", out var tl) && tl.ValueKind == JsonValueKind.Array)
                {
                    var list = new List<Views.ViewModels.AlbumDetailTrack>();
                    var idx = 0;
                    foreach (var t in tl.EnumerateArray())
                    {
                        idx++;
                        if (t.ValueKind != JsonValueKind.Object) continue;
                        var title2 = t.TryGetProperty("title", out var t2) ? (t2.GetString() ?? "").Trim() : "";
                        if (string.IsNullOrWhiteSpace(title2)) continue;
                        var pos = t.TryGetProperty("position", out var p2) ? (p2.GetString() ?? "") : "";
                        var (disc, track) = ParsePosition(pos, idx);
                        list.Add(new Views.ViewModels.AlbumDetailTrack
                        {
                            DiscNumber = disc,
                            TrackNumber = track,
                            Title = title2,
                            Artist = "",
                            RecordingId = ""
                        });
                    }
                    meta.Tracks = list.OrderBy(x => x.DiscNumber).ThenBy(x => x.TrackNumber).ToList();
                }

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

        private static (int disc, int track) ParsePosition(string? pos, int fallback)
        {
            var p = (pos ?? "").Trim();
            if (string.IsNullOrWhiteSpace(p)) return (1, fallback);
            p = p.Replace("-", ".").Replace(" ", "");
            var parts = p.Split('.');
            if (parts.Length == 2 &&
                int.TryParse(parts[0], out var d) && d > 0 &&
                int.TryParse(parts[1], out var t) && t > 0)
                return (d, t);
            if (int.TryParse(p, out var only) && only > 0) return (1, only);
            return (1, fallback);
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
