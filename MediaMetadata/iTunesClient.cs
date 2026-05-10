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
    public sealed class iTunesClient
    {
        private static readonly HttpClient _http = new HttpClient();

        public sealed class AlbumInfo
        {
            public string CollectionId { get; set; } = "";
            public string Artist { get; set; } = "";
            public string Album { get; set; } = "";
            public string ReleaseDate { get; set; } = "";
            public string PrimaryGenre { get; set; } = "";
            public int TrackCount { get; set; }
            public string ArtworkUrl { get; set; } = "";
        }

        public async Task<AlbumInfo?> SearchAlbumAsync(string artist, string album, CancellationToken ct)
        {
            try
            {
                var cleanArtist = CleanSearchTerm(artist);
                var cleanAlbum = CleanSearchTerm(album);
                var term = $"{cleanArtist} {cleanAlbum}";
                var encoded = HttpUtility.UrlEncode(term);
                var url = $"https://itunes.apple.com/search?term={encoded}&entity=album&limit=8";

                using var res = await _http.GetAsync(url, ct).ConfigureAwait(false);
                if (!res.IsSuccessStatusCode) return null;

                var json = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);

                if (!doc.RootElement.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array || results.GetArrayLength() == 0)
                    return null;

                var bestScore = 0.0;
                AlbumInfo? best = null;

                var qArtist = Tokenize(cleanArtist);
                var qAlbum = Tokenize(cleanAlbum);

                foreach (var item in results.EnumerateArray())
                {
                    var itemArtist = item.TryGetProperty("artistName", out var a) ? (a.GetString() ?? "") : "";
                    var itemAlbum = item.TryGetProperty("collectionName", out var c) ? (c.GetString() ?? "") : "";
                    var collectionId = item.TryGetProperty("collectionId", out var id) ? (id.ToString() ?? "") : "";

                    var sArtist = Similarity(qArtist, Tokenize(CleanSearchTerm(itemArtist)));
                    var sAlbum = Similarity(qAlbum, Tokenize(CleanSearchTerm(itemAlbum)));
                    var score = (sArtist * 0.45) + (sAlbum * 0.55);
                    if (sArtist < 0.45 || sAlbum < 0.45) continue;
                    if (score <= bestScore) continue;

                    var releaseDate = item.TryGetProperty("releaseDate", out var rd) ? (rd.GetString() ?? "") : "";
                    var genre = item.TryGetProperty("primaryGenreName", out var g) ? (g.GetString() ?? "") : "";
                    var trackCount = item.TryGetProperty("trackCount", out var tc) && tc.TryGetInt32(out var tcv) ? tcv : 0;
                    var artwork = item.TryGetProperty("artworkUrl100", out var art) ? (art.GetString() ?? "") : "";

                    bestScore = score;
                    best = new AlbumInfo
                    {
                        CollectionId = (collectionId ?? "").Trim(),
                        Artist = (itemArtist ?? "").Trim(),
                        Album = (itemAlbum ?? "").Trim(),
                        ReleaseDate = (releaseDate ?? "").Trim(),
                        PrimaryGenre = (genre ?? "").Trim(),
                        TrackCount = trackCount,
                        ArtworkUrl = (artwork ?? "").Trim()
                    };
                }

                if (best == null) return null;
                if (!string.IsNullOrWhiteSpace(best.ArtworkUrl))
                    best.ArtworkUrl = best.ArtworkUrl.Replace("100x100bb", "600x600bb");
                return best;
            }
            catch
            {
                return null;
            }
        }

        public async Task<bool> TryDownloadCoverAsync(string artist, string album, string destinationPath, CancellationToken ct)
        {
            try
            {
                var cleanArtist = CleanSearchTerm(artist);
                var cleanAlbum = CleanSearchTerm(album);
                var term = $"{cleanArtist} {cleanAlbum}";
                var encoded = HttpUtility.UrlEncode(term);
                var url = $"https://itunes.apple.com/search?term={encoded}&entity=album&limit=8";

                using var res = await _http.GetAsync(url, ct).ConfigureAwait(false);
                if (!res.IsSuccessStatusCode) return false;

                var json = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                
                if (!doc.RootElement.TryGetProperty("resultCount", out var countProp) || countProp.GetInt32() == 0)
                    return false;

                if (!doc.RootElement.TryGetProperty("results", out var results) || results.GetArrayLength() == 0)
                    return false;

                // Find best match
                string? bestUrl = null;
                var bestScore = 0.0;
                var qArtist = Tokenize(cleanArtist);
                var qAlbum = Tokenize(cleanAlbum);

                foreach (var item in results.EnumerateArray())
                {
                    var itemArtist = item.TryGetProperty("artistName", out var a) ? a.GetString() : "";
                    var itemAlbum = item.TryGetProperty("collectionName", out var c) ? c.GetString() : "";
                    var artwork = item.TryGetProperty("artworkUrl100", out var art) ? art.GetString() : "";
                    if (string.IsNullOrWhiteSpace(artwork)) continue;

                    var sArtist = Similarity(qArtist, Tokenize(CleanSearchTerm(itemArtist ?? "")));
                    var sAlbum = Similarity(qAlbum, Tokenize(CleanSearchTerm(itemAlbum ?? "")));
                    var score = (sArtist * 0.45) + (sAlbum * 0.55);

                    if (sArtist < 0.45 || sAlbum < 0.45) continue;
                    if (score <= bestScore) continue;

                    bestScore = score;
                    bestUrl = artwork.Replace("100x100bb", "600x600bb");
                }

                if (string.IsNullOrEmpty(bestUrl)) return false;

                var imgBytes = await _http.GetByteArrayAsync(bestUrl, ct).ConfigureAwait(false);
                await File.WriteAllBytesAsync(destinationPath, imgBytes, ct).ConfigureAwait(false);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private string CleanSearchTerm(string term)
        {
            if (string.IsNullOrWhiteSpace(term)) return "";
            
            // Remove "feat.", "ft.", "featuring" and content in brackets/parentheses for better search matches
            var cleaned = term;
            cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"\s*[\(\[].*?[\)\]]", ""); // remove (...) and [...]
            cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"\s+(feat\.?|ft\.?|featuring)\s+.*$", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase); // remove feat...
            
            return cleaned.Trim();
        }

        private bool IsMatch(string? query, string? target)
        {
            if (string.IsNullOrWhiteSpace(query) || string.IsNullOrWhiteSpace(target)) return false;
            return target.Contains(query, StringComparison.OrdinalIgnoreCase) || 
                   query.Contains(target, StringComparison.OrdinalIgnoreCase);
        }

        private static HashSet<string> Tokenize(string s)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(s)) return set;
            var chars = s.ToLowerInvariant()
                .Select(c => char.IsLetterOrDigit(c) ? c : ' ')
                .ToArray();
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
