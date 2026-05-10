using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using AtlasAI.MediaIntelligence;
using AtlasAI.Services;

namespace AtlasAI.MediaMetadata
{
    public sealed class TmdbClient
    {
        private static readonly HttpClient Http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(20)
        };

        public static string BuildPosterUrl(string? posterPath, string size = "w500")
        {
            var p = (posterPath ?? "").Trim();
            if (string.IsNullOrWhiteSpace(p)) return "";
            if (!p.StartsWith("/")) p = "/" + p;
            size = string.IsNullOrWhiteSpace(size) ? "w500" : size.Trim();
            return $"https://image.tmdb.org/t/p/{size}{p}";
        }

        public static string BuildBackdropUrl(string? backdropPath, string size = "w780")
        {
            var p = (backdropPath ?? "").Trim();
            if (string.IsNullOrWhiteSpace(p)) return "";
            if (!p.StartsWith("/")) p = "/" + p;
            size = string.IsNullOrWhiteSpace(size) ? "w780" : size.Trim();
            return $"https://image.tmdb.org/t/p/{size}{p}";
        }

        public async Task<System.Collections.Generic.List<TmdbMovieResult>> SearchMovieAsync(string apiKey, string query, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(query))
                return new System.Collections.Generic.List<TmdbMovieResult>();

            var url = "";
            try
            {
                var q = Uri.EscapeDataString(query.Trim());
                url = $"https://api.themoviedb.org/3/search/movie?api_key={Uri.EscapeDataString(apiKey.Trim())}&query={q}&include_adult=false";
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.TryAddWithoutValidation("Accept", "application/json");
                using var res = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                if (!res.IsSuccessStatusCode)
                {
                    try { SelfHealingMonitor.Instance.ReportApiFailure("TMDB", url, (int)res.StatusCode, "search/movie"); } catch { }
                    return new System.Collections.Generic.List<TmdbMovieResult>();
                }

                var json = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
                    return new System.Collections.Generic.List<TmdbMovieResult>();

                var list = new System.Collections.Generic.List<TmdbMovieResult>();
                foreach (var r in results.EnumerateArray())
                {
                    if (r.ValueKind != JsonValueKind.Object) continue;
                    var item = new TmdbMovieResult();
                    if (r.TryGetProperty("id", out var id)) item.Id = id.GetInt32();
                    if (r.TryGetProperty("title", out var title)) item.Title = title.GetString();
                    if (r.TryGetProperty("overview", out var ov)) item.Overview = ov.GetString();
                    if (r.TryGetProperty("release_date", out var rd) && rd.ValueKind == JsonValueKind.String)
                    {
                        if (DateTime.TryParse(rd.GetString(), out var d)) item.ReleaseDate = d;
                    }
                    if (r.TryGetProperty("poster_path", out var pp)) item.PosterPath = pp.GetString();
                    if (r.TryGetProperty("backdrop_path", out var bp)) item.BackdropPath = bp.GetString();
                    list.Add(item);
                }
                return list;
            }
            catch
            {
                try { SelfHealingMonitor.Instance.ReportApiFailure("TMDB", url, 0, "search/movie"); } catch { }
                return new System.Collections.Generic.List<TmdbMovieResult>();
            }
        }

        public class TmdbMovieResult
        {
            public int Id { get; set; }
            public string? Title { get; set; }
            public string? Overview { get; set; }
            public DateTime? ReleaseDate { get; set; }
            public string? PosterPath { get; set; }
            public string? BackdropPath { get; set; }
        }

        public async Task<System.Collections.Generic.List<TmdbTvResult>> SearchTvAsync(string apiKey, string query, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(query))
                return new System.Collections.Generic.List<TmdbTvResult>();

            var url = "";
            try
            {
                var q = Uri.EscapeDataString(query.Trim());
                url = $"https://api.themoviedb.org/3/search/tv?api_key={Uri.EscapeDataString(apiKey.Trim())}&query={q}&include_adult=false";
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.TryAddWithoutValidation("Accept", "application/json");
                using var res = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                if (!res.IsSuccessStatusCode)
                {
                    try { SelfHealingMonitor.Instance.ReportApiFailure("TMDB", url, (int)res.StatusCode, "search/tv"); } catch { }
                    return new System.Collections.Generic.List<TmdbTvResult>();
                }

                var json = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
                    return new System.Collections.Generic.List<TmdbTvResult>();

                var list = new System.Collections.Generic.List<TmdbTvResult>();
                foreach (var r in results.EnumerateArray())
                {
                    if (r.ValueKind != JsonValueKind.Object) continue;
                    var item = new TmdbTvResult();
                    if (r.TryGetProperty("id", out var id)) item.Id = id.GetInt32();
                    if (r.TryGetProperty("name", out var name)) item.Name = name.GetString();
                    if (r.TryGetProperty("overview", out var ov)) item.Overview = ov.GetString();
                    if (r.TryGetProperty("first_air_date", out var rd) && rd.ValueKind == JsonValueKind.String)
                    {
                        if (DateTime.TryParse(rd.GetString(), out var d)) item.FirstAirDate = d;
                    }
                    if (r.TryGetProperty("poster_path", out var pp)) item.PosterPath = pp.GetString();
                    if (r.TryGetProperty("backdrop_path", out var bp)) item.BackdropPath = bp.GetString();
                    list.Add(item);
                }
                return list;
            }
            catch
            {
                try { SelfHealingMonitor.Instance.ReportApiFailure("TMDB", url, 0, "search/tv"); } catch { }
                return new System.Collections.Generic.List<TmdbTvResult>();
            }
        }

        public class TmdbTvResult
        {
            public int Id { get; set; }
            public string? Name { get; set; }
            public string? Overview { get; set; }
            public DateTime? FirstAirDate { get; set; }
            public string? PosterPath { get; set; }
            public string? BackdropPath { get; set; }
        }

        public async Task<TmdbMovieDetails?> GetMovieDetailsAsync(string apiKey, int id, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(apiKey) || id <= 0) return null;
            var url = "";
            try
            {
                url = $"https://api.themoviedb.org/3/movie/{id}?api_key={Uri.EscapeDataString(apiKey.Trim())}&language=en-US";
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.TryAddWithoutValidation("Accept", "application/json");
                using var res = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                if (!res.IsSuccessStatusCode)
                {
                    try { SelfHealingMonitor.Instance.ReportApiFailure("TMDB", url, (int)res.StatusCode, "movie/details"); } catch { }
                    return null;
                }

                var json = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;

                var d = new TmdbMovieDetails { Id = id };
                if (doc.RootElement.TryGetProperty("title", out var tEl)) d.Title = tEl.GetString();
                if (doc.RootElement.TryGetProperty("overview", out var ovEl)) d.Overview = ovEl.GetString();
                if (doc.RootElement.TryGetProperty("poster_path", out var ppEl)) d.PosterPath = ppEl.GetString();
                if (doc.RootElement.TryGetProperty("backdrop_path", out var bpEl)) d.BackdropPath = bpEl.GetString();
                if (doc.RootElement.TryGetProperty("vote_average", out var vEl))
                {
                    if (vEl.ValueKind == JsonValueKind.Number && vEl.TryGetDouble(out var dv)) d.VoteAverage = dv;
                    else if (vEl.ValueKind == JsonValueKind.String && double.TryParse(vEl.GetString(), out var dv2)) d.VoteAverage = dv2;
                }
                if (doc.RootElement.TryGetProperty("runtime", out var rtEl))
                {
                    if (rtEl.ValueKind == JsonValueKind.Number && rtEl.TryGetInt32(out var mins)) d.RuntimeMinutes = mins;
                    else if (rtEl.ValueKind == JsonValueKind.String && int.TryParse(rtEl.GetString(), out var mins2)) d.RuntimeMinutes = mins2;
                }
                if (doc.RootElement.TryGetProperty("release_date", out var rdEl) && rdEl.ValueKind == JsonValueKind.String)
                {
                    var s = (rdEl.GetString() ?? "").Trim();
                    if (!string.IsNullOrWhiteSpace(s) && DateTime.TryParse(s, out var dt)) d.ReleaseDate = dt;
                }
                if (doc.RootElement.TryGetProperty("genres", out var gEl) && gEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var g in gEl.EnumerateArray())
                    {
                        if (g.ValueKind != JsonValueKind.Object) continue;
                        if (!g.TryGetProperty("name", out var nEl) || nEl.ValueKind != JsonValueKind.String) continue;
                        var name = (nEl.GetString() ?? "").Trim();
                        if (!string.IsNullOrWhiteSpace(name)) d.Genres.Add(name);
                    }
                }
                return d;
            }
            catch
            {
                try { SelfHealingMonitor.Instance.ReportApiFailure("TMDB", url, 0, "movie/details"); } catch { }
                return null;
            }
        }

        public sealed class TmdbMovieDetails
        {
            public int Id { get; set; }
            public string? Title { get; set; }
            public string? Overview { get; set; }
            public DateTime? ReleaseDate { get; set; }
            public string? PosterPath { get; set; }
            public string? BackdropPath { get; set; }
            public int RuntimeMinutes { get; set; }
            public double VoteAverage { get; set; }
            public List<string> Genres { get; } = new();
        }

        public async Task<TmdbTvDetails?> GetTvDetailsAsync(string apiKey, int id, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(apiKey) || id <= 0) return null;
            var url = "";
            try
            {
                url = $"https://api.themoviedb.org/3/tv/{id}?api_key={Uri.EscapeDataString(apiKey.Trim())}&language=en-US";
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.TryAddWithoutValidation("Accept", "application/json");
                using var res = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                if (!res.IsSuccessStatusCode)
                {
                    try { SelfHealingMonitor.Instance.ReportApiFailure("TMDB", url, (int)res.StatusCode, "tv/details"); } catch { }
                    return null;
                }

                var json = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;

                var d = new TmdbTvDetails { Id = id };
                if (doc.RootElement.TryGetProperty("name", out var nEl)) d.Name = nEl.GetString();
                if (doc.RootElement.TryGetProperty("overview", out var ovEl)) d.Overview = ovEl.GetString();
                if (doc.RootElement.TryGetProperty("poster_path", out var ppEl)) d.PosterPath = ppEl.GetString();
                if (doc.RootElement.TryGetProperty("backdrop_path", out var bpEl)) d.BackdropPath = bpEl.GetString();
                if (doc.RootElement.TryGetProperty("vote_average", out var vEl))
                {
                    if (vEl.ValueKind == JsonValueKind.Number && vEl.TryGetDouble(out var dv)) d.VoteAverage = dv;
                    else if (vEl.ValueKind == JsonValueKind.String && double.TryParse(vEl.GetString(), out var dv2)) d.VoteAverage = dv2;
                }
                if (doc.RootElement.TryGetProperty("episode_run_time", out var rtEl) && rtEl.ValueKind == JsonValueKind.Array)
                {
                    var first = rtEl.EnumerateArray().FirstOrDefault();
                    if (first.ValueKind == JsonValueKind.Number && first.TryGetInt32(out var mins)) d.EpisodeRuntimeMinutes = mins;
                    else if (first.ValueKind == JsonValueKind.String && int.TryParse(first.GetString(), out var mins2)) d.EpisodeRuntimeMinutes = mins2;
                }
                if (doc.RootElement.TryGetProperty("number_of_seasons", out var seasonEl))
                {
                    if (seasonEl.ValueKind == JsonValueKind.Number && seasonEl.TryGetInt32(out var seasons)) d.NumberOfSeasons = seasons;
                    else if (seasonEl.ValueKind == JsonValueKind.String && int.TryParse(seasonEl.GetString(), out var seasons2)) d.NumberOfSeasons = seasons2;
                }
                if (doc.RootElement.TryGetProperty("first_air_date", out var rdEl) && rdEl.ValueKind == JsonValueKind.String)
                {
                    var s = (rdEl.GetString() ?? "").Trim();
                    if (!string.IsNullOrWhiteSpace(s) && DateTime.TryParse(s, out var dt)) d.FirstAirDate = dt;
                }
                if (doc.RootElement.TryGetProperty("genres", out var gEl) && gEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var g in gEl.EnumerateArray())
                    {
                        if (g.ValueKind != JsonValueKind.Object) continue;
                        if (!g.TryGetProperty("name", out var gnEl) || gnEl.ValueKind != JsonValueKind.String) continue;
                        var name = (gnEl.GetString() ?? "").Trim();
                        if (!string.IsNullOrWhiteSpace(name)) d.Genres.Add(name);
                    }
                }
                return d;
            }
            catch
            {
                try { SelfHealingMonitor.Instance.ReportApiFailure("TMDB", url, 0, "tv/details"); } catch { }
                return null;
            }
        }

        public sealed class TmdbTvDetails
        {
            public int Id { get; set; }
            public string? Name { get; set; }
            public string? Overview { get; set; }
            public DateTime? FirstAirDate { get; set; }
            public string? PosterPath { get; set; }
            public string? BackdropPath { get; set; }
            public int EpisodeRuntimeMinutes { get; set; }
            public int NumberOfSeasons { get; set; }
            public double VoteAverage { get; set; }
            public List<string> Genres { get; } = new();
        }

        public async Task<TmdbCredits?> GetMovieCreditsAsync(string apiKey, int id, CancellationToken ct)
        {
            return await GetCreditsAsync(apiKey, "movie", id, ct).ConfigureAwait(false);
        }

        public async Task<TmdbCredits?> GetTvCreditsAsync(string apiKey, int id, CancellationToken ct)
        {
            return await GetCreditsAsync(apiKey, "tv", id, ct).ConfigureAwait(false);
        }

        private async Task<TmdbCredits?> GetCreditsAsync(string apiKey, string mediaType, int id, CancellationToken ct)
        {
            apiKey = (apiKey ?? "").Trim();
            mediaType = (mediaType ?? "").Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(mediaType) || id <= 0)
                return null;

            var cacheKey = $"tmdb:credits:{mediaType}:{id}";
            if (MediaCuratorCache.TryGet(cacheKey, out TmdbCredits cachedCredits))
                return CloneCredits(cachedCredits);

            var url = "";
            try
            {
                url = $"https://api.themoviedb.org/3/{mediaType}/{id}/credits?api_key={Uri.EscapeDataString(apiKey)}&language=en-US";
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.TryAddWithoutValidation("Accept", "application/json");
                using var res = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                if (!res.IsSuccessStatusCode)
                {
                    try { SelfHealingMonitor.Instance.ReportApiFailure("TMDB", url, (int)res.StatusCode, "credits"); } catch { }
                    return null;
                }

                var json = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind != JsonValueKind.Object)
                    return null;

                var credits = new TmdbCredits();
                if (doc.RootElement.TryGetProperty("cast", out var castEl) && castEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var cast in castEl.EnumerateArray().Take(10))
                    {
                        if (cast.ValueKind != JsonValueKind.Object)
                            continue;
                        if (!cast.TryGetProperty("name", out var nameEl) || nameEl.ValueKind != JsonValueKind.String)
                            continue;

                        var name = (nameEl.GetString() ?? "").Trim();
                        if (!string.IsNullOrWhiteSpace(name) && !credits.Cast.Contains(name, StringComparer.OrdinalIgnoreCase))
                            credits.Cast.Add(name);
                    }
                }

                if (doc.RootElement.TryGetProperty("crew", out var crewEl) && crewEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var crew in crewEl.EnumerateArray())
                    {
                        if (crew.ValueKind != JsonValueKind.Object)
                            continue;
                        if (!crew.TryGetProperty("job", out var jobEl) || jobEl.ValueKind != JsonValueKind.String)
                            continue;

                        var job = (jobEl.GetString() ?? "").Trim();
                        if (!string.Equals(job, "Director", StringComparison.OrdinalIgnoreCase) &&
                            !string.Equals(job, "Series Director", StringComparison.OrdinalIgnoreCase) &&
                            !string.Equals(job, "Creator", StringComparison.OrdinalIgnoreCase) &&
                            !string.Equals(job, "Executive Producer", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        if (!crew.TryGetProperty("name", out var nameEl) || nameEl.ValueKind != JsonValueKind.String)
                            continue;

                        var name = (nameEl.GetString() ?? "").Trim();
                        if (!string.IsNullOrWhiteSpace(name) && !credits.Directors.Contains(name, StringComparer.OrdinalIgnoreCase))
                            credits.Directors.Add(name);
                    }
                }

                MediaCuratorCache.Set(cacheKey, CloneCredits(credits), TimeSpan.FromHours(6));
                return credits;
            }
            catch
            {
                try { SelfHealingMonitor.Instance.ReportApiFailure("TMDB", url, 0, "credits"); } catch { }
                return null;
            }
        }

        public sealed class TmdbCredits
        {
            public List<string> Cast { get; } = new();
            public List<string> Directors { get; } = new();
        }

        public sealed class TmdbReviewData
        {
            public double VoteAverage { get; set; }
            public int VoteCount { get; set; }
            public string? Tagline { get; set; }
            public List<TmdbReview> Reviews { get; } = new();
        }

        public sealed class TmdbReview
        {
            public string? Author { get; set; }
            public double? Rating { get; set; }
            public string? Content { get; set; }
        }

        /// <summary>Fetch TMDB vote data + user reviews for a movie or TV show.</summary>
        public async Task<TmdbReviewData?> GetReviewDataAsync(string apiKey, string mediaType, int id, CancellationToken ct)
        {
            apiKey = (apiKey ?? "").Trim();
            mediaType = (mediaType ?? "").Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(mediaType) || id <= 0)
                return null;

            var cacheKey = $"tmdb:reviews:{mediaType}:{id}";
            if (MediaCuratorCache.TryGet(cacheKey, out TmdbReviewData cachedData))
                return cachedData;

            var data = new TmdbReviewData();

            // Fetch main details for vote_average, vote_count, tagline
            try
            {
                var detailUrl = $"https://api.themoviedb.org/3/{mediaType}/{id}?api_key={Uri.EscapeDataString(apiKey)}&language=en-US";
                using var detailReq = new HttpRequestMessage(HttpMethod.Get, detailUrl);
                detailReq.Headers.TryAddWithoutValidation("Accept", "application/json");
                using var detailRes = await Http.SendAsync(detailReq, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                if (detailRes.IsSuccessStatusCode)
                {
                    var json = await detailRes.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("vote_average", out var vaEl) && vaEl.ValueKind == JsonValueKind.Number)
                        data.VoteAverage = vaEl.GetDouble();
                    if (doc.RootElement.TryGetProperty("vote_count", out var vcEl) && vcEl.ValueKind == JsonValueKind.Number)
                        data.VoteCount = vcEl.GetInt32();
                    if (doc.RootElement.TryGetProperty("tagline", out var tagEl) && tagEl.ValueKind == JsonValueKind.String)
                        data.Tagline = (tagEl.GetString() ?? "").Trim();
                }
            }
            catch { }

            // Fetch user reviews
            try
            {
                var reviewUrl = $"https://api.themoviedb.org/3/{mediaType}/{id}/reviews?api_key={Uri.EscapeDataString(apiKey)}&language=en-US&page=1";
                using var reviewReq = new HttpRequestMessage(HttpMethod.Get, reviewUrl);
                reviewReq.Headers.TryAddWithoutValidation("Accept", "application/json");
                using var reviewRes = await Http.SendAsync(reviewReq, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                if (reviewRes.IsSuccessStatusCode)
                {
                    var json = await reviewRes.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("results", out var results) && results.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var r in results.EnumerateArray().Take(5))
                        {
                            if (r.ValueKind != JsonValueKind.Object) continue;
                            var review = new TmdbReview();
                            if (r.TryGetProperty("author", out var aEl) && aEl.ValueKind == JsonValueKind.String)
                                review.Author = (aEl.GetString() ?? "").Trim();
                            if (r.TryGetProperty("content", out var cEl) && cEl.ValueKind == JsonValueKind.String)
                            {
                                var content = (cEl.GetString() ?? "").Trim();
                                review.Content = content.Length > 400 ? content[..400] + "..." : content;
                            }
                            if (r.TryGetProperty("author_details", out var adEl) && adEl.ValueKind == JsonValueKind.Object)
                            {
                                if (adEl.TryGetProperty("rating", out var ratingEl) && ratingEl.ValueKind == JsonValueKind.Number)
                                    review.Rating = ratingEl.GetDouble();
                            }
                            if (!string.IsNullOrWhiteSpace(review.Content))
                                data.Reviews.Add(review);
                        }
                    }
                }
            }
            catch { }

            MediaCuratorCache.Set(cacheKey, data, TimeSpan.FromHours(6));
            return data;
        }

        /// <summary>Fetch ratings from OMDb (IMDb rating, Rotten Tomatoes, Metacritic). Requires an OMDb API key stored as "omdb" in IntegrationKeyStore.</summary>
        public async Task<Dictionary<string, string>> GetOmdbRatingsAsync(string imdbId, CancellationToken ct)
        {
            var ratings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(imdbId)) return ratings;

            var omdbKey = "";
            try { omdbKey = (AtlasAI.Core.IntegrationKeyStore.GetDecrypted("omdb") ?? "").Trim(); } catch { }
            if (string.IsNullOrWhiteSpace(omdbKey)) return ratings;

            var cacheKey = $"omdb:ratings:{imdbId}";
            if (MediaCuratorCache.TryGet(cacheKey, out Dictionary<string, string> cached))
                return new Dictionary<string, string>(cached, StringComparer.OrdinalIgnoreCase);

            try
            {
                var url = $"https://www.omdbapi.com/?i={Uri.EscapeDataString(imdbId.Trim())}&apikey={Uri.EscapeDataString(omdbKey)}";

                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.TryAddWithoutValidation("Accept", "application/json");
                using var res = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                if (!res.IsSuccessStatusCode) return ratings;

                var json = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind != JsonValueKind.Object) return ratings;

                if (doc.RootElement.TryGetProperty("imdbRating", out var imdbR) && imdbR.ValueKind == JsonValueKind.String)
                {
                    var val = (imdbR.GetString() ?? "").Trim();
                    if (val != "N/A") ratings["IMDb"] = val + "/10";
                }

                if (doc.RootElement.TryGetProperty("Ratings", out var ratingsArr) && ratingsArr.ValueKind == JsonValueKind.Array)
                {
                    foreach (var rating in ratingsArr.EnumerateArray())
                    {
                        if (rating.ValueKind != JsonValueKind.Object) continue;
                        var source = rating.TryGetProperty("Source", out var srcEl) && srcEl.ValueKind == JsonValueKind.String ? (srcEl.GetString() ?? "").Trim() : "";
                        var value = rating.TryGetProperty("Value", out var valEl) && valEl.ValueKind == JsonValueKind.String ? (valEl.GetString() ?? "").Trim() : "";
                        if (!string.IsNullOrWhiteSpace(source) && !string.IsNullOrWhiteSpace(value) && value != "N/A")
                            ratings[source] = value;
                    }
                }

                if (doc.RootElement.TryGetProperty("Metascore", out var metaEl) && metaEl.ValueKind == JsonValueKind.String)
                {
                    var val = (metaEl.GetString() ?? "").Trim();
                    if (val != "N/A") ratings["Metacritic"] = val + "/100";
                }

                if (doc.RootElement.TryGetProperty("Awards", out var awardsEl) && awardsEl.ValueKind == JsonValueKind.String)
                {
                    var val = (awardsEl.GetString() ?? "").Trim();
                    if (val != "N/A" && !string.IsNullOrWhiteSpace(val)) ratings["Awards"] = val;
                }

                if (doc.RootElement.TryGetProperty("BoxOffice", out var boxEl) && boxEl.ValueKind == JsonValueKind.String)
                {
                    var val = (boxEl.GetString() ?? "").Trim();
                    if (val != "N/A" && !string.IsNullOrWhiteSpace(val)) ratings["BoxOffice"] = val;
                }

                MediaCuratorCache.Set(cacheKey, new Dictionary<string, string>(ratings, StringComparer.OrdinalIgnoreCase), TimeSpan.FromHours(12));
            }
            catch { }

            return ratings;
        }

        public async Task<(string? imdbId, string? contentType)> TryResolveImdbIdAsync(string apiKey, string title, bool preferTv, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(apiKey)) return (null, null);
            if (string.IsNullOrWhiteSpace(title)) return (null, null);

            var url = "";
            try
            {
                var q = Uri.EscapeDataString(title.Trim());
                url = $"https://api.themoviedb.org/3/search/multi?api_key={Uri.EscapeDataString(apiKey.Trim())}&query={q}&include_adult=false";
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.TryAddWithoutValidation("Accept", "application/json");
                using var res = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                if (!res.IsSuccessStatusCode)
                {
                    try { SelfHealingMonitor.Instance.ReportApiFailure("TMDB", url, (int)res.StatusCode, "search/multi"); } catch { }
                    return (null, null);
                }

                var json = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(json)) return (null, null);

                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
                    return (null, null);

                int? chosenId = null;
                string? chosenKind = null;
                int bestScore = -1;

                static string Normalize(string s)
                {
                    if (string.IsNullOrWhiteSpace(s)) return "";
                    var sb = new StringBuilder(s.Length);
                    foreach (var ch in s)
                    {
                        if (char.IsLetterOrDigit(ch) || char.IsWhiteSpace(ch))
                            sb.Append(char.ToLowerInvariant(ch));
                        else
                            sb.Append(' ');
                    }
                    var res = sb.ToString();
                    while (res.Contains("  ")) res = res.Replace("  ", " ");
                    return res.Trim();
                }

                var queryNorm = Normalize(title);
                int? queryYear = null;
                var yearMatch = System.Text.RegularExpressions.Regex.Match(title, @"\b(19|20)\d{2}\b");
                if (yearMatch.Success && int.TryParse(yearMatch.Value, out var y))
                {
                    queryYear = y;
                    // Also try a version of queryNorm without the year for better matching
                    var queryNormNoYear = Normalize(title.Replace(yearMatch.Value, " "));
                    if (!string.IsNullOrWhiteSpace(queryNormNoYear))
                        queryNorm = queryNormNoYear;
                }

                foreach (var r in results.EnumerateArray())
                {
                    if (r.ValueKind != JsonValueKind.Object) continue;
                    if (!r.TryGetProperty("media_type", out var mtEl)) continue;
                    var mt = mtEl.GetString() ?? "";
                    if (!string.Equals(mt, "movie", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(mt, "tv", StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (!r.TryGetProperty("id", out var idEl) || idEl.ValueKind != JsonValueKind.Number) continue;
                    if (!idEl.TryGetInt32(out var id)) continue;

                    string candidateTitle = "";
                    string? releaseDate = null;
                    if (string.Equals(mt, "movie", StringComparison.OrdinalIgnoreCase))
                    {
                        if (r.TryGetProperty("title", out var tEl) && tEl.ValueKind == JsonValueKind.String)
                            candidateTitle = tEl.GetString() ?? "";
                        if (r.TryGetProperty("release_date", out var rdEl) && rdEl.ValueKind == JsonValueKind.String)
                            releaseDate = rdEl.GetString();
                    }
                    else
                    {
                        if (r.TryGetProperty("name", out var nEl) && nEl.ValueKind == JsonValueKind.String)
                            candidateTitle = nEl.GetString() ?? "";
                        if (r.TryGetProperty("first_air_date", out var adEl) && adEl.ValueKind == JsonValueKind.String)
                            releaseDate = adEl.GetString();
                    }

                    var candidateNorm = Normalize(candidateTitle);
                    int score = 0;

                    // Title matching
                    if (string.Equals(queryNorm, candidateNorm, StringComparison.OrdinalIgnoreCase))
                        score += 100;
                    else if (candidateNorm.Contains(queryNorm, StringComparison.OrdinalIgnoreCase) || queryNorm.Contains(candidateNorm, StringComparison.OrdinalIgnoreCase))
                        score += 50;
                    else
                    {
                        // Token based fallback
                        var qTokens = queryNorm.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        var cTokens = candidateNorm.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        int matches = 0;
                        foreach(var qt in qTokens) if (cTokens.Contains(qt, StringComparer.OrdinalIgnoreCase)) matches++;
                        if (matches > 0) score += (matches * 10);
                    }

                    // Year matching
                    if (queryYear.HasValue && !string.IsNullOrWhiteSpace(releaseDate))
                    {
                        if (releaseDate.StartsWith(queryYear.Value.ToString()))
                            score += 80;
                        else
                            score -= 50; // Penalty for wrong year
                    }

                    if (preferTv && string.Equals(mt, "tv", StringComparison.OrdinalIgnoreCase))
                        score += 20;
                    else if (!preferTv && string.Equals(mt, "movie", StringComparison.OrdinalIgnoreCase))
                        score += 20;

                    if (score > bestScore)
                    {
                        bestScore = score;
                        chosenId = id;
                        chosenKind = mt;
                    }
                }

                if (chosenId == null || string.IsNullOrWhiteSpace(chosenKind) || bestScore < 30) // Minimum threshold
                    return (null, null);

                var imdbId = await TryGetImdbIdFromExternalIdsAsync(apiKey, chosenKind, chosenId.Value, ct).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(imdbId)) return (null, null);

                var contentType = chosenKind == "movie" ? "movie" : "series";
                return (imdbId, contentType);
            }
            catch
            {
                try { SelfHealingMonitor.Instance.ReportApiFailure("TMDB", url, 0, "resolve/imdb"); } catch { }
                return (null, null);
            }
        }

        public async Task<(string? imdbId, string? contentType)> TryResolveImdbIdFromTmdbIdAsync(string apiKey, int tmdbId, bool preferTv, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(apiKey) || tmdbId <= 0)
                return (null, null);

            var orderedKinds = preferTv ? new[] { "tv", "movie" } : new[] { "movie", "tv" };
            foreach (var kind in orderedKinds)
            {
                var imdbId = await TryGetImdbIdFromExternalIdsAsync(apiKey, kind, tmdbId, ct).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(imdbId))
                    return (imdbId, kind == "movie" ? "movie" : "series");
            }

            return (null, null);
        }

        private static async Task<string?> TryGetImdbIdFromExternalIdsAsync(string apiKey, string mediaType, int id, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(mediaType) || id <= 0)
                return null;

            var normalizedType = string.Equals(mediaType, "tv", StringComparison.OrdinalIgnoreCase) ? "tv" : "movie";
            var externalUrl = $"https://api.themoviedb.org/3/{normalizedType}/{id}/external_ids?api_key={Uri.EscapeDataString(apiKey.Trim())}";

            try
            {
                using var extReq = new HttpRequestMessage(HttpMethod.Get, externalUrl);
                extReq.Headers.TryAddWithoutValidation("Accept", "application/json");
                using var extRes = await Http.SendAsync(extReq, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                if (!extRes.IsSuccessStatusCode)
                {
                    try { SelfHealingMonitor.Instance.ReportApiFailure("TMDB", externalUrl, (int)extRes.StatusCode, "external_ids"); } catch { }
                    return null;
                }

                var extJson = await extRes.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(extJson)) return null;

                using var extDoc = JsonDocument.Parse(extJson);
                if (extDoc.RootElement.ValueKind != JsonValueKind.Object) return null;
                if (!extDoc.RootElement.TryGetProperty("imdb_id", out var imdbEl)) return null;

                var imdbId = (imdbEl.GetString() ?? "").Trim();
                return string.IsNullOrWhiteSpace(imdbId) ? null : imdbId;
            }
            catch
            {
                try { SelfHealingMonitor.Instance.ReportApiFailure("TMDB", externalUrl, 0, "external_ids"); } catch { }
                return null;
            }
        }

        public async Task<bool> TryDownloadPosterAsync(string apiKey, string title, string destinationPath, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(apiKey)) return false;
            if (string.IsNullOrWhiteSpace(title)) return false;
            if (string.IsNullOrWhiteSpace(destinationPath)) return false;

            var url = "";
            try
            {
                var queryRaw = title.Trim();
                var q = Uri.EscapeDataString(queryRaw);
                url = $"https://api.themoviedb.org/3/search/multi?api_key={Uri.EscapeDataString(apiKey.Trim())}&query={q}&include_adult=false";
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.TryAddWithoutValidation("Accept", "application/json");
                using var res = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                if (!res.IsSuccessStatusCode)
                {
                    try { SelfHealingMonitor.Instance.ReportApiFailure("TMDB", url, (int)res.StatusCode, "download-poster/search"); } catch { }
                    return false;
                }

                var json = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
                    return false;

                static string Normalize(string s)
                {
                    if (string.IsNullOrWhiteSpace(s)) return "";
                    var sb = new StringBuilder(s.Length);
                    foreach (var ch in s)
                    {
                        if (char.IsLetterOrDigit(ch) || char.IsWhiteSpace(ch))
                            sb.Append(char.ToLowerInvariant(ch));
                        else
                            sb.Append(' ');
                    }
                    var cleaned = sb.ToString();
                    while (cleaned.Contains("  ", StringComparison.Ordinal))
                        cleaned = cleaned.Replace("  ", " ", StringComparison.Ordinal);
                    return cleaned.Trim();
                }

                static HashSet<string> Tokenize(string normalized)
                {
                    var stop = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                    {
                        "the", "a", "an", "and", "or", "of", "at", "in", "to", "on", "for", "from", "live"
                    };
                    var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var part in (normalized ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries))
                    {
                        var t = part.Trim();
                        if (t.Length < 2) continue;
                        if (stop.Contains(t)) continue;
                        set.Add(t);
                    }
                    return set;
                }

                var queryNorm = Normalize(queryRaw);
                var queryTokens = Tokenize(queryNorm);

                string? posterPath = null;
                var bestScore = int.MinValue;

                foreach (var r in results.EnumerateArray())
                {
                    if (r.ValueKind != JsonValueKind.Object) continue;

                    if (!r.TryGetProperty("media_type", out var mtEl) || mtEl.ValueKind != JsonValueKind.String)
                        continue;
                    var mt = (mtEl.GetString() ?? "").Trim();
                    if (!string.Equals(mt, "movie", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(mt, "tv", StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (!r.TryGetProperty("poster_path", out var pp) || pp.ValueKind != JsonValueKind.String)
                        continue;
                    var ppStr = (pp.GetString() ?? "").Trim();
                    if (string.IsNullOrWhiteSpace(ppStr))
                        continue;

                    string? candidateTitle = null;
                    if (string.Equals(mt, "movie", StringComparison.OrdinalIgnoreCase))
                    {
                        if (r.TryGetProperty("title", out var tEl) && tEl.ValueKind == JsonValueKind.String)
                            candidateTitle = tEl.GetString();
                        if (string.IsNullOrWhiteSpace(candidateTitle) &&
                            r.TryGetProperty("original_title", out var otEl) && otEl.ValueKind == JsonValueKind.String)
                            candidateTitle = otEl.GetString();
                    }
                    else
                    {
                        if (r.TryGetProperty("name", out var nEl) && nEl.ValueKind == JsonValueKind.String)
                            candidateTitle = nEl.GetString();
                        if (string.IsNullOrWhiteSpace(candidateTitle) &&
                            r.TryGetProperty("original_name", out var onEl) && onEl.ValueKind == JsonValueKind.String)
                            candidateTitle = onEl.GetString();
                    }

                    if (string.IsNullOrWhiteSpace(candidateTitle))
                        continue;

                    var candNorm = Normalize(candidateTitle);
                    if (string.IsNullOrWhiteSpace(candNorm))
                        continue;

                    var candTokens = Tokenize(candNorm);
                    var score = 0;

                    if (string.Equals(candNorm, queryNorm, StringComparison.OrdinalIgnoreCase))
                        score += 2000;
                    else if (!string.IsNullOrWhiteSpace(queryNorm) && candNorm.Contains(queryNorm, StringComparison.OrdinalIgnoreCase))
                        score += 1200;
                    else if (!string.IsNullOrWhiteSpace(queryNorm) && queryNorm.Contains(candNorm, StringComparison.OrdinalIgnoreCase))
                        score += 400;

                    if (queryTokens.Count > 0 && candTokens.Count > 0)
                    {
                        var intersection = 0;
                        foreach (var t in queryTokens)
                            if (candTokens.Contains(t)) intersection++;
                        score += intersection * 200;
                        score -= Math.Abs(candTokens.Count - queryTokens.Count) * 10;
                    }

                    if (score > bestScore)
                    {
                        bestScore = score;
                        posterPath = ppStr;
                    }
                }

                if (string.IsNullOrWhiteSpace(posterPath)) return false;

                var imgUrl = $"https://image.tmdb.org/t/p/w500{posterPath}";
                using var imgReq = new HttpRequestMessage(HttpMethod.Get, imgUrl);
                using var imgRes = await Http.SendAsync(imgReq, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                if (!imgRes.IsSuccessStatusCode) return false;

                var dir = Path.GetDirectoryName(destinationPath);
                if (!string.IsNullOrWhiteSpace(dir))
                    Directory.CreateDirectory(dir);

                await using var input = await imgRes.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                await using var output = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.Read);
                await input.CopyToAsync(output, ct).ConfigureAwait(false);
                return File.Exists(destinationPath);
            }
            catch
            {
                try { SelfHealingMonitor.Instance.ReportApiFailure("TMDB", url, 0, "download-poster"); } catch { }
                return false;
            }
        }

        private static TmdbCredits CloneCredits(TmdbCredits credits)
        {
            var clone = new TmdbCredits();
            foreach (var actor in credits.Cast)
                clone.Cast.Add(actor);
            foreach (var director in credits.Directors)
                clone.Directors.Add(director);
            return clone;
        }
    }
}
