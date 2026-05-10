using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AtlasAI.MediaIntelligence;

namespace AtlasAI.Integrations
{
    public sealed class TraktClient
    {
        private readonly HttpClient _http;

        public TraktClient(HttpClient? httpClient = null)
        {
            _http = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            if (_http.BaseAddress == null)
                _http.BaseAddress = new Uri("https://api.trakt.tv/");
        }

        public async Task<bool> MarkEpisodeWatchedAsync(string clientId, string accessToken, string showImdbId, int season, int episode, CancellationToken ct = default)
        {
            clientId = (clientId ?? "").Trim();
            accessToken = (accessToken ?? "").Trim();
            showImdbId = (showImdbId ?? "").Trim();

            if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(accessToken))
                return false;
            if (string.IsNullOrWhiteSpace(showImdbId) || !showImdbId.StartsWith("tt", StringComparison.OrdinalIgnoreCase))
                return false;
            if (season <= 0 || episode <= 0)
                return false;

            using var req = new HttpRequestMessage(HttpMethod.Post, "sync/history");
            req.Headers.TryAddWithoutValidation("trakt-api-version", "2");
            req.Headers.TryAddWithoutValidation("trakt-api-key", clientId);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            var payload = new
            {
                shows = new[]
                {
                    new
                    {
                        ids = new { imdb = showImdbId.ToLowerInvariant() },
                        seasons = new[]
                        {
                            new
                            {
                                number = season,
                                episodes = new[] { new { number = episode } }
                            }
                        }
                    }
                }
            };

            var json = JsonSerializer.Serialize(payload);
            req.Content = new StringContent(json, Encoding.UTF8, "application/json");

            using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
            return resp.IsSuccessStatusCode;
        }

        public sealed class TraktPlaybackItem
        {
            public string Type { get; set; } = ""; // movie | episode
            public double ProgressPercent { get; set; }
            public DateTime? PausedAtUtc { get; set; }

            // Movie
            public string MovieTitle { get; set; } = "";
            public int? MovieYear { get; set; }
            public int? MovieTmdbId { get; set; }
            public string MovieImdbId { get; set; } = "";

            // Episode / show
            public string ShowTitle { get; set; } = "";
            public int? ShowYear { get; set; }
            public int? ShowTmdbId { get; set; }
            public string ShowImdbId { get; set; } = "";
            public int? Season { get; set; }
            public int? Episode { get; set; }
        }

        public async Task<List<TraktPlaybackItem>> GetPlaybackAsync(string clientId, string accessToken, int limit = 30, CancellationToken ct = default)
        {
            clientId = (clientId ?? "").Trim();
            accessToken = (accessToken ?? "").Trim();
            limit = Math.Max(1, Math.Min(100, limit));

            if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(accessToken))
                return new List<TraktPlaybackItem>();

            var path = $"sync/playback?limit={limit}";

            using var req = new HttpRequestMessage(HttpMethod.Get, path);
            req.Headers.TryAddWithoutValidation("trakt-api-version", "2");
            req.Headers.TryAddWithoutValidation("trakt-api-key", clientId);
            req.Headers.TryAddWithoutValidation("Accept", "application/json");
            req.Headers.TryAddWithoutValidation("Cache-Control", "no-cache");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            try
            {
                using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode)
                    return new List<TraktPlaybackItem>();

                var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind != JsonValueKind.Array)
                    return new List<TraktPlaybackItem>();

                var results = new List<TraktPlaybackItem>();
                foreach (var el in doc.RootElement.EnumerateArray())
                {
                    if (el.ValueKind != JsonValueKind.Object) continue;
                    var item = new TraktPlaybackItem();

                    if (el.TryGetProperty("type", out var typeEl) && typeEl.ValueKind == JsonValueKind.String)
                        item.Type = (typeEl.GetString() ?? "").Trim();

                    if (el.TryGetProperty("progress", out var progEl) && progEl.ValueKind == JsonValueKind.Number)
                    {
                        try
                        {
                            item.ProgressPercent = Math.Clamp(progEl.GetDouble(), 0d, 100d);
                        }
                        catch { item.ProgressPercent = 0; }
                    }

                    if (el.TryGetProperty("paused_at", out var pausedEl) && pausedEl.ValueKind == JsonValueKind.String)
                    {
                        var raw = (pausedEl.GetString() ?? "").Trim();
                        if (DateTime.TryParse(raw, out var dt))
                            item.PausedAtUtc = dt.Kind == DateTimeKind.Utc ? dt : dt.ToUniversalTime();
                    }

                    // movie payload
                    if (el.TryGetProperty("movie", out var movieEl) && movieEl.ValueKind == JsonValueKind.Object)
                    {
                        if (movieEl.TryGetProperty("title", out var tEl) && tEl.ValueKind == JsonValueKind.String)
                            item.MovieTitle = (tEl.GetString() ?? "").Trim();
                        if (movieEl.TryGetProperty("year", out var yEl) && yEl.ValueKind == JsonValueKind.Number && yEl.TryGetInt32(out var y))
                            item.MovieYear = y;
                        if (movieEl.TryGetProperty("ids", out var idsEl) && idsEl.ValueKind == JsonValueKind.Object)
                        {
                            if (idsEl.TryGetProperty("tmdb", out var tmdbEl) && tmdbEl.ValueKind == JsonValueKind.Number && tmdbEl.TryGetInt32(out var tmdb))
                                item.MovieTmdbId = tmdb;
                            if (idsEl.TryGetProperty("imdb", out var imdbEl) && imdbEl.ValueKind == JsonValueKind.String)
                                item.MovieImdbId = (imdbEl.GetString() ?? "").Trim();
                        }
                    }

                    // episode payload
                    if (el.TryGetProperty("show", out var showEl) && showEl.ValueKind == JsonValueKind.Object)
                    {
                        if (showEl.TryGetProperty("title", out var stEl) && stEl.ValueKind == JsonValueKind.String)
                            item.ShowTitle = (stEl.GetString() ?? "").Trim();
                        if (showEl.TryGetProperty("year", out var syEl) && syEl.ValueKind == JsonValueKind.Number && syEl.TryGetInt32(out var sy))
                            item.ShowYear = sy;
                        if (showEl.TryGetProperty("ids", out var sidsEl) && sidsEl.ValueKind == JsonValueKind.Object)
                        {
                            if (sidsEl.TryGetProperty("tmdb", out var stmdbEl) && stmdbEl.ValueKind == JsonValueKind.Number && stmdbEl.TryGetInt32(out var stmdb))
                                item.ShowTmdbId = stmdb;
                            if (sidsEl.TryGetProperty("imdb", out var simdbEl) && simdbEl.ValueKind == JsonValueKind.String)
                                item.ShowImdbId = (simdbEl.GetString() ?? "").Trim();
                        }
                    }

                    if (el.TryGetProperty("episode", out var epEl) && epEl.ValueKind == JsonValueKind.Object)
                    {
                        if (epEl.TryGetProperty("season", out var seasonEl) && seasonEl.ValueKind == JsonValueKind.Number && seasonEl.TryGetInt32(out var season))
                            item.Season = season;
                        if (epEl.TryGetProperty("number", out var numEl) && numEl.ValueKind == JsonValueKind.Number && numEl.TryGetInt32(out var number))
                            item.Episode = number;
                    }

                    // Only keep plausible entries.
                    var hasMovie = !string.IsNullOrWhiteSpace(item.MovieTitle) || item.MovieTmdbId != null || !string.IsNullOrWhiteSpace(item.MovieImdbId);
                    var hasShow = !string.IsNullOrWhiteSpace(item.ShowTitle) || item.ShowTmdbId != null || !string.IsNullOrWhiteSpace(item.ShowImdbId);
                    if (!hasMovie && !hasShow) continue;

                    results.Add(item);
                }

                // Prefer most recently paused first.
                return results
                    .OrderByDescending(r => r.PausedAtUtc ?? DateTime.MinValue)
                    .ThenByDescending(r => r.ProgressPercent)
                    .ToList();
            }
            catch
            {
                return new List<TraktPlaybackItem>();
            }
        }

        public sealed class TraktTrendingItem
        {
            public int Watchers { get; set; }
            public int? TmdbId { get; set; }
            public string ImdbId { get; set; } = "";
            public string Title { get; set; } = "";
            public int? Year { get; set; }
        }

        public sealed class TraktHistoryItem
        {
            public string Type { get; set; } = "";
            public string Title { get; set; } = "";
            public string ShowTitle { get; set; } = "";
            public string EpisodeTitle { get; set; } = "";
            public int? Year { get; set; }
            public int? TmdbId { get; set; }
            public string ImdbId { get; set; } = "";
            public DateTime? WatchedAtUtc { get; set; }
            public int? Season { get; set; }
            public int? Episode { get; set; }
        }

        public async Task<List<TraktHistoryItem>> GetHistoryAsync(string clientId, string accessToken, int limit = 80, CancellationToken ct = default)
        {
            clientId = (clientId ?? "").Trim();
            accessToken = (accessToken ?? "").Trim();
            limit = Math.Max(1, Math.Min(200, limit));

            if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(accessToken))
                return new List<TraktHistoryItem>();

            var cacheKey = $"trakt:history:{clientId}:{accessToken}:{limit}";
            if (MediaCuratorCache.TryGet(cacheKey, out List<TraktHistoryItem> cachedHistory))
                return cachedHistory.Select(CloneHistoryItem).ToList();

            using var req = new HttpRequestMessage(HttpMethod.Get, $"sync/history?limit={limit}");
            req.Headers.TryAddWithoutValidation("trakt-api-version", "2");
            req.Headers.TryAddWithoutValidation("trakt-api-key", clientId);
            req.Headers.TryAddWithoutValidation("Accept", "application/json");
            req.Headers.TryAddWithoutValidation("Cache-Control", "no-cache");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            try
            {
                using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode)
                    return new List<TraktHistoryItem>();

                var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind != JsonValueKind.Array)
                    return new List<TraktHistoryItem>();

                var results = new List<TraktHistoryItem>();
                foreach (var el in doc.RootElement.EnumerateArray())
                {
                    if (el.ValueKind != JsonValueKind.Object)
                        continue;

                    var item = new TraktHistoryItem();
                    if (el.TryGetProperty("type", out var typeEl) && typeEl.ValueKind == JsonValueKind.String)
                        item.Type = (typeEl.GetString() ?? "").Trim();

                    if (el.TryGetProperty("watched_at", out var watchedEl) && watchedEl.ValueKind == JsonValueKind.String)
                    {
                        var raw = (watchedEl.GetString() ?? "").Trim();
                        if (DateTime.TryParse(raw, out var dt))
                            item.WatchedAtUtc = dt.Kind == DateTimeKind.Utc ? dt : dt.ToUniversalTime();
                    }

                    if (el.TryGetProperty("movie", out var movieEl) && movieEl.ValueKind == JsonValueKind.Object)
                    {
                        if (movieEl.TryGetProperty("title", out var tEl) && tEl.ValueKind == JsonValueKind.String)
                            item.Title = (tEl.GetString() ?? "").Trim();
                        if (movieEl.TryGetProperty("year", out var yEl) && yEl.ValueKind == JsonValueKind.Number && yEl.TryGetInt32(out var y))
                            item.Year = y;
                        if (movieEl.TryGetProperty("ids", out var idsEl) && idsEl.ValueKind == JsonValueKind.Object)
                        {
                            if (idsEl.TryGetProperty("tmdb", out var tmdbEl) && tmdbEl.ValueKind == JsonValueKind.Number && tmdbEl.TryGetInt32(out var tmdb))
                                item.TmdbId = tmdb;
                            if (idsEl.TryGetProperty("imdb", out var imdbEl) && imdbEl.ValueKind == JsonValueKind.String)
                                item.ImdbId = (imdbEl.GetString() ?? "").Trim();
                        }
                    }

                    if (el.TryGetProperty("show", out var showEl) && showEl.ValueKind == JsonValueKind.Object)
                    {
                        if (showEl.TryGetProperty("title", out var stEl) && stEl.ValueKind == JsonValueKind.String)
                            item.ShowTitle = (stEl.GetString() ?? "").Trim();
                        if (showEl.TryGetProperty("year", out var syEl) && syEl.ValueKind == JsonValueKind.Number && syEl.TryGetInt32(out var sy))
                            item.Year = sy;
                        if (showEl.TryGetProperty("ids", out var sidsEl) && sidsEl.ValueKind == JsonValueKind.Object)
                        {
                            if (sidsEl.TryGetProperty("tmdb", out var stmdbEl) && stmdbEl.ValueKind == JsonValueKind.Number && stmdbEl.TryGetInt32(out var stmdb))
                                item.TmdbId = stmdb;
                            if (sidsEl.TryGetProperty("imdb", out var simdbEl) && simdbEl.ValueKind == JsonValueKind.String)
                                item.ImdbId = (simdbEl.GetString() ?? "").Trim();
                        }
                    }

                    if (el.TryGetProperty("episode", out var epEl) && epEl.ValueKind == JsonValueKind.Object)
                    {
                        if (epEl.TryGetProperty("title", out var etEl) && etEl.ValueKind == JsonValueKind.String)
                            item.EpisodeTitle = (etEl.GetString() ?? "").Trim();
                        if (epEl.TryGetProperty("season", out var seasonEl) && seasonEl.ValueKind == JsonValueKind.Number && seasonEl.TryGetInt32(out var season))
                            item.Season = season;
                        if (epEl.TryGetProperty("number", out var numEl) && numEl.ValueKind == JsonValueKind.Number && numEl.TryGetInt32(out var number))
                            item.Episode = number;
                    }

                    if (string.IsNullOrWhiteSpace(item.Title))
                        item.Title = item.ShowTitle;

                    if (string.IsNullOrWhiteSpace(item.Title) && string.IsNullOrWhiteSpace(item.ShowTitle) && item.TmdbId == null && string.IsNullOrWhiteSpace(item.ImdbId))
                        continue;

                    results.Add(item);
                }

                var ordered = results
                    .OrderByDescending(r => r.WatchedAtUtc ?? DateTime.MinValue)
                    .ThenByDescending(r => r.Season ?? 0)
                    .ThenByDescending(r => r.Episode ?? 0)
                    .ToList();

                MediaCuratorCache.Set(cacheKey, ordered.Select(CloneHistoryItem).ToList(), TimeSpan.FromHours(6));
                return ordered;
            }
            catch
            {
                return new List<TraktHistoryItem>();
            }
        }

        public async Task<List<TraktTrendingItem>> GetTrendingMoviesAsync(string clientId, CancellationToken ct = default)
        {
            return await GetTrendingAsync(clientId, "movies/trending?limit=50", isShow: false, ct).ConfigureAwait(false);
        }

        public async Task<List<TraktTrendingItem>> GetTrendingShowsAsync(string clientId, CancellationToken ct = default)
        {
            return await GetTrendingAsync(clientId, "shows/trending?limit=50", isShow: true, ct).ConfigureAwait(false);
        }

        private async Task<List<TraktTrendingItem>> GetTrendingAsync(string clientId, string path, bool isShow, CancellationToken ct)
        {
            clientId = (clientId ?? "").Trim();
            path = (path ?? "").Trim().TrimStart('/');
            if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(path))
                return new List<TraktTrendingItem>();

            using var req = new HttpRequestMessage(HttpMethod.Get, path);
            req.Headers.TryAddWithoutValidation("trakt-api-version", "2");
            req.Headers.TryAddWithoutValidation("trakt-api-key", clientId);
            req.Headers.TryAddWithoutValidation("Accept", "application/json");
            req.Headers.TryAddWithoutValidation("Cache-Control", "no-cache");

            try
            {
                using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode)
                    return new List<TraktTrendingItem>();

                var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind != JsonValueKind.Array)
                    return new List<TraktTrendingItem>();

                var results = new List<TraktTrendingItem>();
                foreach (var el in doc.RootElement.EnumerateArray())
                {
                    if (el.ValueKind != JsonValueKind.Object) continue;
                    var item = new TraktTrendingItem();

                    if (el.TryGetProperty("watchers", out var wEl) && wEl.ValueKind == JsonValueKind.Number && wEl.TryGetInt32(out var watchers))
                        item.Watchers = watchers;

                    var objProp = isShow ? "show" : "movie";
                    if (!el.TryGetProperty(objProp, out var objEl) || objEl.ValueKind != JsonValueKind.Object)
                        continue;

                    if (objEl.TryGetProperty("title", out var tEl) && tEl.ValueKind == JsonValueKind.String)
                        item.Title = (tEl.GetString() ?? "").Trim();

                    if (objEl.TryGetProperty("year", out var yEl) && yEl.ValueKind == JsonValueKind.Number && yEl.TryGetInt32(out var year))
                        item.Year = year;

                    if (objEl.TryGetProperty("ids", out var idsEl) && idsEl.ValueKind == JsonValueKind.Object)
                    {
                        if (idsEl.TryGetProperty("tmdb", out var tmdbEl) && tmdbEl.ValueKind == JsonValueKind.Number && tmdbEl.TryGetInt32(out var tmdb))
                            item.TmdbId = tmdb;
                        if (idsEl.TryGetProperty("imdb", out var imdbEl) && imdbEl.ValueKind == JsonValueKind.String)
                            item.ImdbId = (imdbEl.GetString() ?? "").Trim();
                    }

                    if (!string.IsNullOrWhiteSpace(item.Title) || item.TmdbId != null)
                        results.Add(item);
                }

                // Stable ordering: most watchers first.
                return results
                    .OrderByDescending(r => r.Watchers)
                    .ThenBy(r => r.Title, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            catch
            {
                return new List<TraktTrendingItem>();
            }
        }

        private static TraktHistoryItem CloneHistoryItem(TraktHistoryItem item)
        {
            return new TraktHistoryItem
            {
                Type = item.Type,
                Title = item.Title,
                ShowTitle = item.ShowTitle,
                EpisodeTitle = item.EpisodeTitle,
                Year = item.Year,
                TmdbId = item.TmdbId,
                ImdbId = item.ImdbId,
                WatchedAtUtc = item.WatchedAtUtc,
                Season = item.Season,
                Episode = item.Episode
            };
        }
    }
}

