using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AtlasAI.Modules.Downloader.Resolvers
{
    public class ItunesPreviewResolver : ILinkResolver
    {
        private readonly HttpClient _http;

        public ItunesPreviewResolver(HttpClient http)
        {
            _http = http;
        }

        public string Name => "ItunesPreview";
        public bool IsEnabled { get; set; } = true;
        public int Priority => 200;

        public bool CanHandle(Uri input)
        {
            return input.Scheme.Equals("atlastrack", StringComparison.OrdinalIgnoreCase);
        }

        public async Task<ResolvedLink?> ResolveAsync(Uri input, CancellationToken ct)
        {
            var q = GetQueryParam(input, "q");
            var title = GetQueryParam(input, "title");
            var artists = GetQueryParam(input, "artists");
            var album = GetQueryParam(input, "album");
            var yearStr = GetQueryParam(input, "year");
            _ = int.TryParse(yearStr, out var year);

            var term = (q ?? "").Trim();
            if (string.IsNullOrWhiteSpace(term))
            {
                var a = (artists ?? "").Trim();
                var t = (title ?? "").Trim();
                term = string.IsNullOrWhiteSpace(a) ? t : $"{t} {a}";
            }
            term = term.Trim();
            if (string.IsNullOrWhiteSpace(term)) return null;

            var url = "https://itunes.apple.com/search?entity=song&limit=8&term=" + Uri.EscapeDataString(term);
            using var resp = await _http.GetAsync(url, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode) return null;

            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array) return null;

            JsonElement? best = null;
            var bestScore = int.MinValue;
            foreach (var r in results.EnumerateArray())
            {
                var preview = r.TryGetProperty("previewUrl", out var p) ? (p.GetString() ?? "") : "";
                if (string.IsNullOrWhiteSpace(preview)) continue;

                var rTitle = r.TryGetProperty("trackName", out var tn) ? (tn.GetString() ?? "") : "";
                var rArtist = r.TryGetProperty("artistName", out var an) ? (an.GetString() ?? "") : "";
                var rAlbum = r.TryGetProperty("collectionName", out var cn) ? (cn.GetString() ?? "") : "";
                var rYear = 0;
                try
                {
                    if (r.TryGetProperty("releaseDate", out var rd))
                    {
                        var s = rd.GetString() ?? "";
                        if (s.Length >= 4 && int.TryParse(s.AsSpan(0, 4), out var y)) rYear = y;
                    }
                }
                catch
                {
                }

                var score = 0;
                score += ScoreMatch(title, rTitle) * 3;
                score += ScoreMatch(artists, rArtist) * 2;
                score += ScoreMatch(album, rAlbum);
                if (year > 0 && rYear > 0) score += Math.Max(0, 5 - Math.Abs(year - rYear));

                if (score > bestScore)
                {
                    bestScore = score;
                    best = r;
                }
            }

            if (best == null) return null;
            var item = best.Value;

            var previewUrl = item.TryGetProperty("previewUrl", out var pu) ? (pu.GetString() ?? "") : "";
            if (string.IsNullOrWhiteSpace(previewUrl)) return null;

            var foundTitle = item.TryGetProperty("trackName", out var fTitle) ? (fTitle.GetString() ?? "") : "";
            var foundArtist = item.TryGetProperty("artistName", out var fArtist) ? (fArtist.GetString() ?? "") : "";
            var foundAlbum = item.TryGetProperty("collectionName", out var fAlbum) ? (fAlbum.GetString() ?? "") : "";
            var artwork = item.TryGetProperty("artworkUrl100", out var a100) ? (a100.GetString() ?? "") : "";

            var ext = ".m4a";
            try
            {
                if (Uri.TryCreate(previewUrl, UriKind.Absolute, out var puUri))
                {
                    var e = System.IO.Path.GetExtension(puUri.AbsolutePath);
                    if (!string.IsNullOrWhiteSpace(e)) ext = e;
                }
            }
            catch
            {
            }

            var baseName = BuildBaseName(foundArtist, foundTitle, artists, title);
            var suggested = string.IsNullOrWhiteSpace(baseName) ? null : baseName + ext;

            return new ResolvedLink
            {
                DirectUrl = new Uri(previewUrl),
                ResolverName = Name,
                SuggestedFilename = suggested,
                ArtworkUrl = string.IsNullOrWhiteSpace(artwork) ? null : artwork.Replace("100x100bb", "600x600bb", StringComparison.OrdinalIgnoreCase),
                TrackTitle = string.IsNullOrWhiteSpace(foundTitle) ? title : foundTitle,
                TrackArtists = string.IsNullOrWhiteSpace(foundArtist) ? artists : foundArtist,
                Album = string.IsNullOrWhiteSpace(foundAlbum) ? album : foundAlbum,
                Year = year
            };
        }

        private static string? GetQueryParam(Uri uri, string name)
        {
            try
            {
                var q = uri.Query ?? "";
                if (q.StartsWith("?")) q = q.Substring(1);
                var parts = q.Split('&', StringSplitOptions.RemoveEmptyEntries);
                foreach (var p in parts)
                {
                    var kv = p.Split('=', 2);
                    if (kv.Length != 2) continue;
                    if (!kv[0].Equals(name, StringComparison.OrdinalIgnoreCase)) continue;
                    return Uri.UnescapeDataString(kv[1].Replace('+', ' '));
                }
            }
            catch
            {
            }
            return null;
        }

        private static int ScoreMatch(string? expected, string? actual)
        {
            var a = (expected ?? "").Trim();
            var b = (actual ?? "").Trim();
            if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return 0;
            if (b.Equals(a, StringComparison.OrdinalIgnoreCase)) return 10;
            if (b.IndexOf(a, StringComparison.OrdinalIgnoreCase) >= 0) return 7;
            if (a.IndexOf(b, StringComparison.OrdinalIgnoreCase) >= 0) return 5;
            return 1;
        }

        private static string BuildBaseName(string foundArtist, string foundTitle, string? fallbackArtists, string? fallbackTitle)
        {
            var artist = (foundArtist ?? "").Trim();
            var title = (foundTitle ?? "").Trim();
            if (string.IsNullOrWhiteSpace(artist)) artist = (fallbackArtists ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? "";
            if (string.IsNullOrWhiteSpace(title)) title = (fallbackTitle ?? "").Trim();
            if (string.IsNullOrWhiteSpace(artist) && string.IsNullOrWhiteSpace(title)) return "";
            if (string.IsNullOrWhiteSpace(artist)) return title;
            if (string.IsNullOrWhiteSpace(title)) return artist;
            return $"{artist} - {title}";
        }
    }
}
