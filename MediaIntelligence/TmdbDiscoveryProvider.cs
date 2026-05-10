using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using AtlasAI.MediaMetadata;
using AtlasAI.Services;

namespace AtlasAI.MediaIntelligence
{
    internal sealed class TmdbDiscoveryProvider
    {
        private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };

        private static readonly ConcurrentDictionary<string, (DateTime utc, Dictionary<int, string> map)> GenreCache = new(StringComparer.OrdinalIgnoreCase);

        private static readonly (int id, string name, string[] aliases)[] MovieGenreAliases =
        {
            (28, "Action", new[] { "action" }),
            (12, "Adventure", new[] { "adventure" }),
            (16, "Animation", new[] { "animation" }),
            (35, "Comedy", new[] { "comedy" }),
            (80, "Crime", new[] { "crime" }),
            (99, "Documentary", new[] { "documentary" }),
            (18, "Drama", new[] { "drama" }),
            (10751, "Family", new[] { "family" }),
            (14, "Fantasy", new[] { "fantasy" }),
            (36, "History", new[] { "history" }),
            (27, "Horror", new[] { "horror" }),
            (10402, "Music", new[] { "music" }),
            (9648, "Mystery", new[] { "mystery" }),
            (10749, "Romance", new[] { "romance" }),
            (878, "Science Fiction", new[] { "science fiction", "sci-fi", "scifi" }),
            (53, "Thriller", new[] { "thriller" }),
            (10752, "War", new[] { "war" }),
            (37, "Western", new[] { "western" })
        };

        public static List<(int id, string name)> ResolveMovieGenresFromText(string instruction)
        {
            var normalized = (instruction ?? "").Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(normalized))
                return new List<(int id, string name)>();

            var matches = new List<(int id, string name)>();
            foreach (var entry in MovieGenreAliases)
            {
                if (entry.aliases.Any(alias => ContainsGenreAlias(normalized, alias)))
                    matches.Add((entry.id, entry.name));
            }

            return matches
                .GroupBy(match => match.id)
                .Select(group => group.First())
                .ToList();
        }

        private static bool ContainsGenreAlias(string instruction, string alias)
        {
            instruction = (instruction ?? "").Trim();
            alias = (alias ?? "").Trim();
            if (string.IsNullOrWhiteSpace(instruction) || string.IsNullOrWhiteSpace(alias))
                return false;

            var aliasPattern = Regex.Escape(alias).Replace("\\ ", @"\s+");
            return Regex.IsMatch(instruction, $@"(?<![a-z0-9]){aliasPattern}(?![a-z0-9])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        public async Task<Dictionary<int, string>> GetGenreMapAsync(string apiKey, MediaKind kind, CancellationToken ct)
        {
            apiKey = (apiKey ?? "").Trim();
            if (string.IsNullOrWhiteSpace(apiKey)) return new Dictionary<int, string>();

            var cacheKey = $"{kind}";
            if (GenreCache.TryGetValue(cacheKey, out var cached) && (DateTime.UtcNow - cached.utc).TotalHours < 24)
                return cached.map;

            var type = kind == MediaKind.Series ? "tv" : "movie";
            var url = $"https://api.themoviedb.org/3/genre/{type}/list?api_key={Uri.EscapeDataString(apiKey)}&language=en-US";

            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.TryAddWithoutValidation("Accept", "application/json");
                req.Headers.TryAddWithoutValidation("Cache-Control", "no-cache");
                using var res = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                if (!res.IsSuccessStatusCode)
                {
                    try { SelfHealingMonitor.Instance.ReportApiFailure("TMDB", url, (int)res.StatusCode, "genre/list"); } catch { }
                    return GenreCache.TryGetValue(cacheKey, out cached) ? cached.map : new Dictionary<int, string>();
                }

                var json = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("genres", out var genresEl) || genresEl.ValueKind != JsonValueKind.Array)
                    return new Dictionary<int, string>();

                var map = new Dictionary<int, string>();
                foreach (var g in genresEl.EnumerateArray())
                {
                    if (g.ValueKind != JsonValueKind.Object) continue;
                    if (!g.TryGetProperty("id", out var idEl) || idEl.ValueKind != JsonValueKind.Number) continue;
                    if (!idEl.TryGetInt32(out var id)) continue;
                    if (!g.TryGetProperty("name", out var nameEl) || nameEl.ValueKind != JsonValueKind.String) continue;
                    var name = (nameEl.GetString() ?? "").Trim();
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    map[id] = name;
                }

                GenreCache[cacheKey] = (DateTime.UtcNow, map);
                return map;
            }
            catch
            {
                try { SelfHealingMonitor.Instance.ReportApiFailure("TMDB", url, 0, "genre/list"); } catch { }
                return GenreCache.TryGetValue(cacheKey, out cached) ? cached.map : new Dictionary<int, string>();
            }
        }

        private static string KindToEndpoint(MediaKind kind)
            => kind == MediaKind.Series ? "tv" : "movie";

        private static string BuildListUrl(string apiKey, string endpoint, int page)
        {
            var separator = endpoint.Contains('?') ? "&" : "?";
            var langFilter = "";
            if (endpoint.StartsWith("discover/", StringComparison.OrdinalIgnoreCase) &&
                !endpoint.Contains("with_original_language", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var pref = (AtlasAI.Settings.SettingsStore.Current.PreferredContentLanguage ?? "").Trim();
                    if (!string.IsNullOrWhiteSpace(pref) && !string.Equals(pref, "any", StringComparison.OrdinalIgnoreCase))
                        langFilter = $"&with_original_language={Uri.EscapeDataString(pref)}";
                }
                catch { }
            }
            return $"https://api.themoviedb.org/3/{endpoint}{separator}api_key={Uri.EscapeDataString(apiKey)}&language=en-US&page={page}{langFilter}";
        }

        public async Task<List<EnrichedMediaObject>> FetchListAsync(string apiKey, string endpoint, MediaKind kind, CancellationToken ct)
        {
            return await FetchListPagesAsync(apiKey, endpoint, kind, 1, ct).ConfigureAwait(false);
        }

        public async Task<List<EnrichedMediaObject>> FetchListPagesAsync(string apiKey, string endpoint, MediaKind kind, int pages, CancellationToken ct)
        {
            return await FetchListInternalAsync(apiKey, endpoint, kind, pages, ct).ConfigureAwait(false);
        }

        public async Task<List<EnrichedMediaObject>> FetchMixedListPagesAsync(string apiKey, string endpoint, int pages, CancellationToken ct)
        {
            return await FetchListInternalAsync(apiKey, endpoint, MediaKind.Unknown, pages, ct).ConfigureAwait(false);
        }

        private async Task<List<EnrichedMediaObject>> FetchListInternalAsync(string apiKey, string endpoint, MediaKind kind, int pages, CancellationToken ct)
        {
            apiKey = (apiKey ?? "").Trim();
            endpoint = (endpoint ?? "").Trim().TrimStart('/');
            pages = Math.Max(1, Math.Min(10, pages));
            if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(endpoint))
                return new List<EnrichedMediaObject>();

            try
            {
                var list = new List<EnrichedMediaObject>();
                for (var page = 1; page <= pages; page++)
                {
                    ct.ThrowIfCancellationRequested();
                    var url = BuildListUrl(apiKey, endpoint, page);
                    using var req = new HttpRequestMessage(HttpMethod.Get, url);
                    req.Headers.TryAddWithoutValidation("Accept", "application/json");
                    req.Headers.TryAddWithoutValidation("Cache-Control", "no-cache");
                    using var res = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                    if (!res.IsSuccessStatusCode)
                    {
                        try { SelfHealingMonitor.Instance.ReportApiFailure("TMDB", url, (int)res.StatusCode, endpoint); } catch { }
                        continue;
                    }

                    var json = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                    using var doc = JsonDocument.Parse(json);
                    if (!doc.RootElement.TryGetProperty("results", out var resultsEl) || resultsEl.ValueKind != JsonValueKind.Array)
                        continue;

                    foreach (var r in resultsEl.EnumerateArray())
                    {
                        if (r.ValueKind != JsonValueKind.Object) continue;
                        if (!r.TryGetProperty("id", out var idEl) || idEl.ValueKind != JsonValueKind.Number || !idEl.TryGetInt32(out var tmdbId))
                            continue;

                        var itemKind = kind;
                        if (itemKind == MediaKind.Unknown)
                        {
                            if (!r.TryGetProperty("media_type", out var mtEl) || mtEl.ValueKind != JsonValueKind.String)
                                continue;

                            var mediaType = (mtEl.GetString() ?? "").Trim();
                            if (string.Equals(mediaType, "movie", StringComparison.OrdinalIgnoreCase))
                                itemKind = MediaKind.Movie;
                            else if (string.Equals(mediaType, "tv", StringComparison.OrdinalIgnoreCase))
                                itemKind = MediaKind.Series;
                            else
                                continue;
                        }

                        var title = "";
                        if (itemKind == MediaKind.Series)
                        {
                            if (r.TryGetProperty("name", out var nameEl) && nameEl.ValueKind == JsonValueKind.String)
                                title = (nameEl.GetString() ?? "").Trim();
                        }
                        else
                        {
                            if (r.TryGetProperty("title", out var tEl) && tEl.ValueKind == JsonValueKind.String)
                                title = (tEl.GetString() ?? "").Trim();
                        }

                        if (string.IsNullOrWhiteSpace(title))
                            continue;

                        var overview = r.TryGetProperty("overview", out var ovEl) && ovEl.ValueKind == JsonValueKind.String
                            ? (ovEl.GetString() ?? "").Trim()
                            : "";

                        DateTime? releaseDate = null;
                        var dateProp = itemKind == MediaKind.Series ? "first_air_date" : "release_date";
                        if (r.TryGetProperty(dateProp, out var dEl) && dEl.ValueKind == JsonValueKind.String)
                        {
                            if (DateTime.TryParse(dEl.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var dt))
                                releaseDate = dt;
                        }

                        var voteAverage = 0d;
                        if (r.TryGetProperty("vote_average", out var vaEl) && vaEl.ValueKind == JsonValueKind.Number && vaEl.TryGetDouble(out var dva))
                            voteAverage = dva;

                        var voteCount = 0;
                        if (r.TryGetProperty("vote_count", out var vcEl) && vcEl.ValueKind == JsonValueKind.Number && vcEl.TryGetInt32(out var dvc))
                            voteCount = dvc;

                        var popularity = 0d;
                        if (r.TryGetProperty("popularity", out var popEl) && popEl.ValueKind == JsonValueKind.Number && popEl.TryGetDouble(out var dpop))
                            popularity = dpop;

                        var poster = r.TryGetProperty("poster_path", out var ppEl) && ppEl.ValueKind == JsonValueKind.String
                            ? TmdbClient.BuildPosterUrl(ppEl.GetString(), "w500")
                            : "";

                        var backdrop = r.TryGetProperty("backdrop_path", out var bpEl) && bpEl.ValueKind == JsonValueKind.String
                            ? TmdbClient.BuildBackdropUrl(bpEl.GetString(), "w780")
                            : "";

                        if (string.IsNullOrWhiteSpace(poster) && !string.IsNullOrWhiteSpace(backdrop))
                            poster = backdrop;

                        var genreNames = new List<string>();
                        var genreMap = itemKind == MediaKind.Unknown
                            ? new Dictionary<int, string>()
                            : await GetGenreMapAsync(apiKey, itemKind, ct).ConfigureAwait(false);
                        if (r.TryGetProperty("genre_ids", out var gidsEl) && gidsEl.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var g in gidsEl.EnumerateArray())
                            {
                                if (g.ValueKind != JsonValueKind.Number || !g.TryGetInt32(out var gid)) continue;
                                if (genreMap.TryGetValue(gid, out var name) && !string.IsNullOrWhiteSpace(name))
                                    genreNames.Add(name);
                            }
                        }

                        var origLang = r.TryGetProperty("original_language", out var olEl) && olEl.ValueKind == JsonValueKind.String
                            ? (olEl.GetString() ?? "").Trim() : "";

                        var item = new EnrichedMediaObject
                        {
                            Kind = itemKind,
                            TmdbId = tmdbId,
                            Title = title,
                            Overview = overview,
                            Genres = genreNames,
                            OriginalLanguage = origLang,
                            ReleaseDate = releaseDate,
                            VoteCount = voteCount,
                            Poster = poster,
                            Backdrop = backdrop,
                            Ratings = new MediaRatings
                            {
                                Tmdb = voteAverage,
                                Audience = voteAverage,
                                Critic = voteAverage,
                                PopularityRaw = popularity,
                                Signals = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
                                {
                                    ["vote_count"] = voteCount
                                }
                            }
                        };

                        item.Id = MediaScoring.BuildStableId(item.Kind, item.TmdbId, item.ImdbId, item.Title, item.ReleaseDate);
                        list.Add(item);
                    }
                }

                return list;
            }
            catch
            {
                return new List<EnrichedMediaObject>();
            }
        }

        public async Task<int?> SearchCompanyIdAsync(string apiKey, string query, CancellationToken ct)
        {
            return await SearchLookupIdAsync(apiKey, "search/company", query, ct).ConfigureAwait(false);
        }

        public async Task<int?> SearchKeywordIdAsync(string apiKey, string query, CancellationToken ct)
        {
            return await SearchLookupIdAsync(apiKey, "search/keyword", query, ct).ConfigureAwait(false);
        }

        private static async Task<int?> SearchLookupIdAsync(string apiKey, string endpoint, string query, CancellationToken ct)
        {
            apiKey = (apiKey ?? "").Trim();
            query = (query ?? "").Trim();
            if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(query))
                return null;

            var url = $"https://api.themoviedb.org/3/{endpoint}?api_key={Uri.EscapeDataString(apiKey)}&query={Uri.EscapeDataString(query)}&page=1";
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.TryAddWithoutValidation("Accept", "application/json");
                using var res = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                if (!res.IsSuccessStatusCode)
                    return null;

                var json = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("results", out var resultsEl) || resultsEl.ValueKind != JsonValueKind.Array)
                    return null;

                var exact = resultsEl.EnumerateArray()
                    .Where(r => r.ValueKind == JsonValueKind.Object)
                    .Select(r => new
                    {
                        Id = r.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.Number && idEl.TryGetInt32(out var id) ? id : 0,
                        Name = r.TryGetProperty("name", out var nameEl) && nameEl.ValueKind == JsonValueKind.String ? (nameEl.GetString() ?? "").Trim() : ""
                    })
                    .Where(x => x.Id > 0 && !string.IsNullOrWhiteSpace(x.Name))
                    .OrderByDescending(x => string.Equals(x.Name, query, StringComparison.OrdinalIgnoreCase))
                    .ThenBy(x => x.Name.Length)
                    .FirstOrDefault();

                return exact?.Id > 0 ? exact.Id : null;
            }
            catch
            {
                return null;
            }
        }

        public async Task<string> TryGetTrailerUrlAsync(string apiKey, MediaKind kind, int tmdbId, CancellationToken ct)
        {
            apiKey = (apiKey ?? "").Trim();
            if (string.IsNullOrWhiteSpace(apiKey) || tmdbId <= 0) return "";

            var endpoint = KindToEndpoint(kind);
            var url = $"https://api.themoviedb.org/3/{endpoint}/{tmdbId}/videos?api_key={Uri.EscapeDataString(apiKey)}&language=en-US";

            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.TryAddWithoutValidation("Accept", "application/json");
                req.Headers.TryAddWithoutValidation("Cache-Control", "no-cache");
                using var res = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                if (!res.IsSuccessStatusCode)
                {
                    try { SelfHealingMonitor.Instance.ReportApiFailure("TMDB", url, (int)res.StatusCode, "videos"); } catch { }
                    return "";
                }

                var json = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("results", out var resultsEl) || resultsEl.ValueKind != JsonValueKind.Array)
                    return "";

                // Prefer official YouTube trailers.
                var candidates = resultsEl.EnumerateArray()
                    .Where(v => v.ValueKind == JsonValueKind.Object)
                    .Select(v => new
                    {
                        type = v.TryGetProperty("type", out var tEl) && tEl.ValueKind == JsonValueKind.String ? (tEl.GetString() ?? "") : "",
                        site = v.TryGetProperty("site", out var sEl) && sEl.ValueKind == JsonValueKind.String ? (sEl.GetString() ?? "") : "",
                        official = v.TryGetProperty("official", out var oEl) && oEl.ValueKind == JsonValueKind.True,
                        key = v.TryGetProperty("key", out var kEl) && kEl.ValueKind == JsonValueKind.String ? (kEl.GetString() ?? "") : "",
                        name = v.TryGetProperty("name", out var nEl) && nEl.ValueKind == JsonValueKind.String ? (nEl.GetString() ?? "") : "",
                    })
                    .Where(x => !string.IsNullOrWhiteSpace(x.key))
                    .ToList();

                var pick = candidates
                    .OrderByDescending(x => string.Equals(x.site, "YouTube", StringComparison.OrdinalIgnoreCase))
                    .ThenByDescending(x => string.Equals(x.type, "Trailer", StringComparison.OrdinalIgnoreCase))
                    .ThenByDescending(x => x.official)
                    .ThenBy(x => x.name.Length)
                    .FirstOrDefault();

                if (pick == null) return "";
                if (string.Equals(pick.site, "YouTube", StringComparison.OrdinalIgnoreCase))
                    return $"https://www.youtube.com/watch?v={pick.key}";

                return "";
            }
            catch
            {
                try { SelfHealingMonitor.Instance.ReportApiFailure("TMDB", url, 0, "videos"); } catch { }
                return "";
            }
        }

        public async Task<Dictionary<string, string>> TryGetWatchProvidersAsync(string apiKey, MediaKind kind, int tmdbId, string region, CancellationToken ct)
        {
            apiKey = (apiKey ?? "").Trim();
            region = (region ?? "").Trim().ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(apiKey) || tmdbId <= 0) return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (string.IsNullOrWhiteSpace(region))
            {
                try { region = RegionInfo.CurrentRegion.TwoLetterISORegionName; } catch { region = "US"; }
            }

            var endpoint = KindToEndpoint(kind);
            var url = $"https://api.themoviedb.org/3/{endpoint}/{tmdbId}/watch/providers?api_key={Uri.EscapeDataString(apiKey)}";

            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.TryAddWithoutValidation("Accept", "application/json");
                req.Headers.TryAddWithoutValidation("Cache-Control", "no-cache");
                using var res = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                if (!res.IsSuccessStatusCode)
                {
                    try { SelfHealingMonitor.Instance.ReportApiFailure("TMDB", url, (int)res.StatusCode, "watch/providers"); } catch { }
                    return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                }

                var json = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("results", out var resultsEl) || resultsEl.ValueKind != JsonValueKind.Object)
                    return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                if (!resultsEl.TryGetProperty(region, out var regionEl) || regionEl.ValueKind != JsonValueKind.Object)
                    return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                // Flatten common buckets.
                var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var bucket in new[] { "flatrate", "ads", "rent", "buy" })
                {
                    if (!regionEl.TryGetProperty(bucket, out var bEl) || bEl.ValueKind != JsonValueKind.Array) continue;
                    foreach (var p in bEl.EnumerateArray())
                    {
                        if (p.ValueKind != JsonValueKind.Object) continue;
                        if (!p.TryGetProperty("provider_name", out var nEl) || nEl.ValueKind != JsonValueKind.String) continue;
                        var name = (nEl.GetString() ?? "").Trim();
                        if (string.IsNullOrWhiteSpace(name)) continue;
                        if (!dict.ContainsKey(name))
                            dict[name] = bucket;
                    }
                }

                return dict;
            }
            catch
            {
                try { SelfHealingMonitor.Instance.ReportApiFailure("TMDB", url, 0, "watch/providers"); } catch { }
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }
        }
    }
}
