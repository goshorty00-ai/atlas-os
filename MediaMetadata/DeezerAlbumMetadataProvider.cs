using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

namespace AtlasAI.MediaMetadata
{
    public sealed class DeezerAlbumMetadataProvider : IMusicAlbumMetadataProvider
    {
        private static readonly HttpClient _http = new HttpClient();

        public string Name => "Deezer";
        public bool IsConfigured => true;

        public async Task<MusicAlbumMetadata?> TryGetAlbumAsync(MusicAlbumQuery query, CancellationToken ct)
        {
            var artist = (query?.Artist ?? "").Trim();
            var album = (query?.Album ?? "").Trim();
            if (string.IsNullOrWhiteSpace(album)) return null;

            try
            {
                var q = $"artist:\"{artist}\" album:\"{album}\"";
                if (string.IsNullOrWhiteSpace(artist)) q = $"album:\"{album}\"";
                var url = "https://api.deezer.com/search/album?q=" + HttpUtility.UrlEncode(q) + "&limit=8";

                using var res = await _http.GetAsync(url, ct).ConfigureAwait(false);
                if (!res.IsSuccessStatusCode) return null;
                var json = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("data", out var dataEl) || dataEl.ValueKind != JsonValueKind.Array) return null;

                long bestId = 0;
                var bestScore = 0.0;
                var qArtistTokens = Tokenize(artist);
                var qAlbumTokens = Tokenize(album);

                foreach (var it in dataEl.EnumerateArray())
                {
                    if (it.ValueKind != JsonValueKind.Object) continue;
                    var id = it.TryGetProperty("id", out var idEl) && idEl.TryGetInt64(out var idv) ? idv : 0;
                    if (id <= 0) continue;
                    var title = it.TryGetProperty("title", out var tEl) ? (tEl.GetString() ?? "") : "";
                    var artistObj = it.TryGetProperty("artist", out var aEl) && aEl.ValueKind == JsonValueKind.Object ? aEl : default;
                    var artistName = artistObj.ValueKind == JsonValueKind.Object && artistObj.TryGetProperty("name", out var nEl) ? (nEl.GetString() ?? "") : "";

                    var sAlbum = Similarity(qAlbumTokens, Tokenize(title));
                    var sArtist = string.IsNullOrWhiteSpace(artist) ? 1.0 : Similarity(qArtistTokens, Tokenize(artistName));
                    var score = (sArtist * 0.45) + (sAlbum * 0.55);
                    if (sAlbum < 0.40) continue;
                    if (!string.IsNullOrWhiteSpace(artist) && sArtist < 0.35) continue;
                    if (score <= bestScore) continue;
                    bestScore = score;
                    bestId = id;
                }

                if (bestId <= 0) return null;

                var albumUrl = $"https://api.deezer.com/album/{bestId}";
                using var albumRes = await _http.GetAsync(albumUrl, ct).ConfigureAwait(false);
                if (!albumRes.IsSuccessStatusCode) return null;
                var albumJson = await albumRes.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                using var albumDoc = JsonDocument.Parse(albumJson);

                var meta = new MusicAlbumMetadata { Provider = Name };
                meta.ReleaseId = bestId.ToString();
                meta.ReleaseUrl = $"https://www.deezer.com/album/{bestId}";

                if (albumDoc.RootElement.TryGetProperty("release_date", out var rdEl) && rdEl.ValueKind == JsonValueKind.String)
                {
                    meta.ReleaseDate = (rdEl.GetString() ?? "").Trim();
                    if (meta.ReleaseDate.Length >= 4 && int.TryParse(meta.ReleaseDate.Substring(0, 4), out var y) && y > 0) meta.Year = y;
                }

                if (albumDoc.RootElement.TryGetProperty("genres", out var gEl) && gEl.ValueKind == JsonValueKind.Object &&
                    gEl.TryGetProperty("data", out var gData) && gData.ValueKind == JsonValueKind.Array)
                {
                    foreach (var g in gData.EnumerateArray())
                    {
                        var name = g.TryGetProperty("name", out var gn) ? (gn.GetString() ?? "").Trim() : "";
                        if (!string.IsNullOrWhiteSpace(name)) meta.Genres.Add(name);
                    }
                }

                if (albumDoc.RootElement.TryGetProperty("tracks", out var trEl) && trEl.ValueKind == JsonValueKind.Object &&
                    trEl.TryGetProperty("data", out var trData) && trData.ValueKind == JsonValueKind.Array)
                {
                    var list = new List<Views.ViewModels.AlbumDetailTrack>();
                    var idx = 0;
                    foreach (var t in trData.EnumerateArray())
                    {
                        idx++;
                        var title = t.TryGetProperty("title", out var tt) ? (tt.GetString() ?? "").Trim() : "";
                        if (string.IsNullOrWhiteSpace(title)) continue;
                        var trackNo = t.TryGetProperty("track_position", out var tp) && tp.TryGetInt32(out var tpn) ? tpn : idx;
                        var artObj = t.TryGetProperty("artist", out var at) && at.ValueKind == JsonValueKind.Object ? at : default;
                        var an = artObj.ValueKind == JsonValueKind.Object && artObj.TryGetProperty("name", out var anEl) ? (anEl.GetString() ?? "").Trim() : "";
                        list.Add(new Views.ViewModels.AlbumDetailTrack
                        {
                            DiscNumber = 1,
                            TrackNumber = trackNo <= 0 ? idx : trackNo,
                            Title = title,
                            Artist = an,
                            RecordingId = ""
                        });
                    }
                    meta.Tracks = list.OrderBy(x => x.TrackNumber).ToList();
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
            return TryDownloadCoverInnerAsync(query, destinationPath, ct);
        }

        private static async Task<bool> TryDownloadCoverInnerAsync(MusicAlbumQuery query, string destinationPath, CancellationToken ct)
        {
            var artist = (query?.Artist ?? "").Trim();
            var album = (query?.Album ?? "").Trim();
            if (string.IsNullOrWhiteSpace(album)) return false;

            try
            {
                var q = $"artist:\"{artist}\" album:\"{album}\"";
                if (string.IsNullOrWhiteSpace(artist)) q = $"album:\"{album}\"";
                var url = "https://api.deezer.com/search/album?q=" + HttpUtility.UrlEncode(q) + "&limit=8";

                using var res = await _http.GetAsync(url, ct).ConfigureAwait(false);
                if (!res.IsSuccessStatusCode) return false;
                var json = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("data", out var dataEl) || dataEl.ValueKind != JsonValueKind.Array) return false;

                long bestId = 0;
                var bestScore = 0.0;
                var qArtistTokens = Tokenize(artist);
                var qAlbumTokens = Tokenize(album);

                foreach (var it in dataEl.EnumerateArray())
                {
                    if (it.ValueKind != JsonValueKind.Object) continue;
                    var id = it.TryGetProperty("id", out var idEl) && idEl.TryGetInt64(out var idv) ? idv : 0;
                    if (id <= 0) continue;
                    var title = it.TryGetProperty("title", out var tEl) ? (tEl.GetString() ?? "") : "";
                    var artistObj = it.TryGetProperty("artist", out var aEl) && aEl.ValueKind == JsonValueKind.Object ? aEl : default;
                    var artistName = artistObj.ValueKind == JsonValueKind.Object && artistObj.TryGetProperty("name", out var nEl) ? (nEl.GetString() ?? "") : "";
                    var sAlbum = Similarity(qAlbumTokens, Tokenize(title));
                    if (sAlbum < 0.40) continue;
                    var sArtist = string.IsNullOrWhiteSpace(artist) ? 1.0 : Similarity(qArtistTokens, Tokenize(artistName));
                    if (!string.IsNullOrWhiteSpace(artist) && sArtist < 0.35) continue;
                    var score = (sArtist * 0.45) + (sAlbum * 0.55);
                    if (score <= bestScore) continue;
                    bestScore = score;
                    bestId = id;
                }

                if (bestId <= 0) return false;

                var albumUrl = $"https://api.deezer.com/album/{bestId}";
                using var albumRes = await _http.GetAsync(albumUrl, ct).ConfigureAwait(false);
                if (!albumRes.IsSuccessStatusCode) return false;
                var albumJson = await albumRes.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                using var albumDoc = JsonDocument.Parse(albumJson);

                var cover = "";
                if (albumDoc.RootElement.TryGetProperty("cover_xl", out var cx) && cx.ValueKind == JsonValueKind.String) cover = (cx.GetString() ?? "").Trim();
                else if (albumDoc.RootElement.TryGetProperty("cover_big", out var cb) && cb.ValueKind == JsonValueKind.String) cover = (cb.GetString() ?? "").Trim();
                else if (albumDoc.RootElement.TryGetProperty("cover_medium", out var cm) && cm.ValueKind == JsonValueKind.String) cover = (cm.GetString() ?? "").Trim();
                if (string.IsNullOrWhiteSpace(cover)) return false;

                var bytes = await _http.GetByteArrayAsync(cover, ct).ConfigureAwait(false);
                await File.WriteAllBytesAsync(destinationPath, bytes, ct).ConfigureAwait(false);
                return File.Exists(destinationPath);
            }
            catch
            {
                return false;
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
