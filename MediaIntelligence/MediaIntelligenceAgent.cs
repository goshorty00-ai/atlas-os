using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using AtlasAI.Core;
using AtlasAI.Integrations;
using AtlasAI.MediaMetadata;
using AtlasAI.Streaming;

namespace AtlasAI.MediaIntelligence
{
    public sealed class MediaIntelligenceAgent
    {
        private readonly TmdbDiscoveryProvider _tmdb = new();
        private readonly TraktClient _trakt;
        private readonly IStreamResolverService _streamResolver = new StreamResolverService(new IAddonProvider[] { new AddonServersAddonProvider() });

        public MediaIntelligenceAgent(TraktClient? traktClient = null)
        {
            _trakt = traktClient ?? new TraktClient();
        }

        public async Task<MediaIntelligenceResult> BuildHomeAsync(
            string tmdbApiKey,
            string traktClientId,
            string traktAccessToken,
            string? userContextText,
            int perSection,
            CancellationToken ct)
        {
            perSection = Math.Max(10, Math.Min(120, perSection));
            tmdbApiKey = (tmdbApiKey ?? "").Trim();
            traktClientId = (traktClientId ?? "").Trim();
            traktAccessToken = (traktAccessToken ?? "").Trim();

            var mood = MediaScoring.DetectMood(userContextText);

            var continueWatchingTask = BuildContinueWatchingShelfAsync(tmdbApiKey, traktClientId, traktAccessToken, perSection, ct);
            var latestMoviesTask = BuildLatestMoviesShelfAsync(tmdbApiKey, ct);
            var latestSeriesTask = BuildLatestSeriesShelfAsync(tmdbApiKey, ct);
            var marvelTask = BuildMarvelCollectionShelfAsync(tmdbApiKey, ct);
            var dcTask = BuildDcCollectionShelfAsync(tmdbApiKey, ct);
            var greatsTask = BuildAllTimeGreatsShelfAsync(tmdbApiKey, ct);
            var kidsChoiceTask = BuildKidsChoiceShelfAsync(tmdbApiKey, ct);

            await Task.WhenAll(new Task[]
            {
                continueWatchingTask,
                latestMoviesTask,
                latestSeriesTask,
                marvelTask,
                dcTask,
                greatsTask,
                kidsChoiceTask
            }).ConfigureAwait(false);

            var continueWatching = continueWatchingTask.Result;
            var userProfile = await BuildUserBehaviorProfileAsync(tmdbApiKey, traktClientId, traktAccessToken, continueWatching, ct).ConfigureAwait(false);

            var latestMoviesRaw = DistinctByStableId(latestMoviesTask.Result);
            var latestSeriesRaw = DistinctByStableId(latestSeriesTask.Result);
            var marvelRaw = DistinctByStableId(marvelTask.Result);
            var dcRaw = DistinctByStableId(dcTask.Result);
            var allTimeGreatsRaw = DistinctByStableId(greatsTask.Result);
            var kidsChoiceRaw = DistinctByStableId(kidsChoiceTask.Result);

            if (continueWatching.Count == 0)
                continueWatching = TakeOrdered(latestSeriesRaw.Concat(latestMoviesRaw), perSection);

            var usedForNoDuplicates = new HashSet<string>(continueWatching.Select(BuildDedupeKey), StringComparer.OrdinalIgnoreCase);

            var latestMovies = ExcludeAndLimit(latestMoviesRaw, usedForNoDuplicates, perSection);
            AddExcludedKeys(usedForNoDuplicates, latestMovies);

            var latestSeries = ExcludeAndLimit(latestSeriesRaw, usedForNoDuplicates, perSection);
            AddExcludedKeys(usedForNoDuplicates, latestSeries);

            var marvel = ExcludeAndLimit(marvelRaw, usedForNoDuplicates, perSection);
            AddExcludedKeys(usedForNoDuplicates, marvel);

            var dc = ExcludeAndLimit(dcRaw, usedForNoDuplicates, perSection);
            AddExcludedKeys(usedForNoDuplicates, dc);

            var allTimeGreats = ExcludeAndLimit(allTimeGreatsRaw, usedForNoDuplicates, perSection);
            AddExcludedKeys(usedForNoDuplicates, allTimeGreats);

            var kidsChoice = ExcludeAndLimit(OrderByProfile(kidsChoiceRaw, userProfile), usedForNoDuplicates, perSection);
            AddExcludedKeys(usedForNoDuplicates, kidsChoice);

            var favoriteGenres = userProfile.TopGenres
                .Where(g => !string.IsNullOrWhiteSpace(g))
                .Select(g => g.Trim())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (favoriteGenres.Count == 0)
                favoriteGenres = await BuildFavoriteGenresAsync(tmdbApiKey, ct).ConfigureAwait(false);

            var all = MergeAndDedupe(900,
                continueWatching,
                latestMovies,
                latestSeries,
                marvel,
                dc,
                allTimeGreats,
                kidsChoice);
            var batchMaxPopularity = all.Count == 0 ? (double?)null : all.Max(i => i.Ratings.PopularityRaw);
            var batchMaxTrend = all.Count == 0 ? (double?)null : all.Max(i => i.Ratings.TrendRaw);

            foreach (var item in all)
            {
                // Recompute preference if missing.
                if (item.PersonalAffinity <= 0)
                    item.PersonalAffinity = MediaScoring.PreferenceScore(item.Genres, favoriteGenres, mood);

                var scored = MediaScoring.ScoreRating(item, batchMaxPopularity, batchMaxTrend);
                item.AiRating = scored.rating;
                item.AiScore = scored.rating.AiScore;
                item.Confidence = scored.confidence0To1;

                item.Id = MediaScoring.BuildStableId(item.Kind, item.TmdbId, item.ImdbId, item.Title, item.ReleaseDate);
            }

            // Trailer/providers enrichment: only for the first N unique items (cost control).
            var region = "";
            try { region = RegionInfo.CurrentRegion.TwoLetterISORegionName; } catch { region = "US"; }
            await EnrichTopItemsAsync(tmdbApiKey, all, region, maxItems: 40, ct).ConfigureAwait(false);

            var result = new MediaIntelligenceResult
            {
                Mood = mood,
                UserProfile = userProfile
            };

            AddSection(result, "still-watching", "Still Watching", continueWatching, all, perSection, preserveOrder: true);
            AddSection(result, "latest-movies", "Latest Movies", latestMovies, all, perSection);
            AddSection(result, "latest-tv", "Latest TV", latestSeries, all, perSection);
            AddSection(result, "popular-marvel", "Popular Marvel", marvel, all, perSection);
            AddSection(result, "popular-dc", "Popular DC", dc, all, perSection);
            AddSection(result, "best-rated-movies-of-all-time", "Best Rated Movies of All Time", allTimeGreats
                .OrderByDescending(i => i.Ratings?.Tmdb ?? 0)
                .ThenByDescending(i => i.VoteCount), all, perSection, preserveOrder: true);
            AddSection(result, "kids-choice", "Kids Choice", kidsChoice, all, perSection);

            try
            {
                var summary = string.Join(", ", result.Sections.Select(s => $"{(s?.Key ?? "?")}:{(s?.Items?.Count ?? 0)}"));
                AppLogger.LogInfo($"[MediaIntelligence] BuildHome perSection={perSection} sections={result.Sections.Count} counts=[{summary}]");
            }
            catch
            {
            }

            return result;
        }

        internal static string BuildDedupeKey(EnrichedMediaObject? item)
        {
            if (item == null)
                return "";

            var id = (item.Id ?? "").Trim();
            if (!string.IsNullOrWhiteSpace(id))
                return id;

            return $"{item.Kind}:{(item.TmdbId ?? 0)}:{(item.Title ?? "").Trim()}:{item.ReleaseDate?.Year ?? 0}";
        }

        internal static List<EnrichedMediaObject> DistinctByStableId(IEnumerable<EnrichedMediaObject> items)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var result = new List<EnrichedMediaObject>();

            foreach (var item in items ?? Array.Empty<EnrichedMediaObject>())
            {
                if (item == null)
                    continue;

                item.Id = MediaScoring.BuildStableId(item.Kind, item.TmdbId, item.ImdbId, item.Title, item.ReleaseDate);
                var key = BuildDedupeKey(item);
                if (string.IsNullOrWhiteSpace(key) || !seen.Add(key))
                    continue;

                result.Add(item);
            }

            return result;
        }

        internal static List<EnrichedMediaObject> TakeOrdered(IEnumerable<EnrichedMediaObject> items, int limit)
        {
            return (items ?? Array.Empty<EnrichedMediaObject>())
                .Where(i => i != null)
                .Take(Math.Max(1, limit))
                .ToList();
        }

        internal static List<EnrichedMediaObject> FilterAndDeduplicateContinueWatching(IEnumerable<EnrichedMediaObject> items, int limit)
        {
            return (items ?? Array.Empty<EnrichedMediaObject>())
                .Where(i => i != null)
                .Select(i =>
                {
                    i.Id = MediaScoring.BuildStableId(i.Kind, i.TmdbId, i.ImdbId, i.Title, i.ReleaseDate);
                    return i;
                })
                .Where(i => (i.Ratings?.EngagementRaw ?? 0) >= 5d && (i.Ratings?.EngagementRaw ?? 0) <= 95d)
                .GroupBy(BuildDedupeKey, StringComparer.OrdinalIgnoreCase)
                .Select(g => g
                    .OrderByDescending(x => x.LastInteractionUtc ?? DateTime.MinValue)
                    .ThenByDescending(x => x.Ratings?.EngagementRaw ?? 0)
                    .First())
                .OrderByDescending(i => i.LastInteractionUtc ?? DateTime.MinValue)
                .ThenByDescending(i => i.Ratings?.EngagementRaw ?? 0)
                .Take(Math.Max(1, limit))
                .ToList();
        }

        internal static List<EnrichedMediaObject> ExcludeAndLimit(IEnumerable<EnrichedMediaObject> items, ISet<string> excludedIds, int limit)
        {
            var results = new List<EnrichedMediaObject>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var item in items ?? Array.Empty<EnrichedMediaObject>())
            {
                if (item == null)
                    continue;

                var key = BuildDedupeKey(item);
                if (string.IsNullOrWhiteSpace(key))
                    continue;
                if (excludedIds != null && excludedIds.Contains(key))
                    continue;
                if (!seen.Add(key))
                    continue;

                results.Add(item);
                if (results.Count >= Math.Max(1, limit))
                    break;
            }

            return results;
        }

        private static List<EnrichedMediaObject> OrderByAi(List<EnrichedMediaObject> list, List<EnrichedMediaObject> all)
        {
            var map = all.ToDictionary(i => i.Id, i => i, StringComparer.OrdinalIgnoreCase);
            return list
                .Select(i => map.TryGetValue(i.Id, out var enriched) ? enriched : i)
                .OrderByDescending(i => i.AiScore)
                .ThenByDescending(i => i.Ratings.TrendRaw)
                .ThenByDescending(i => i.Ratings.PopularityRaw)
                .ToList();
        }

        private sealed class HistorySample
        {
            public string Title { get; set; } = "";
            public MediaKind Kind { get; set; }
            public int? TmdbId { get; set; }
            public string ImdbId { get; set; } = "";
            public DateTime? WatchedAtUtc { get; set; }
            public int? Season { get; set; }
            public int? Episode { get; set; }
            public List<string> Genres { get; } = new();
            public List<string> Cast { get; } = new();
            public List<string> Directors { get; } = new();
        }

        private static void AddExcludedKeys(ISet<string> excludedIds, IEnumerable<EnrichedMediaObject> items)
        {
            if (excludedIds == null || items == null)
                return;

            foreach (var item in items)
            {
                var key = BuildDedupeKey(item);
                if (!string.IsNullOrWhiteSpace(key))
                    excludedIds.Add(key);
            }
        }

        private static void AddSection(MediaIntelligenceResult result, string key, string title, IEnumerable<EnrichedMediaObject> items, List<EnrichedMediaObject> all, int limit, bool preserveOrder = false)
        {
            if (result == null || items == null)
                return;

            limit = Math.Max(1, limit);
            var source = preserveOrder
                ? items.Where(i => i != null).Take(limit).ToList()
                : OrderByAi(items.Where(i => i != null).Take(limit).ToList(), all).Take(limit).ToList();

            if (source.Count == 0)
                return;

            result.Sections.Add(new MediaSection
            {
                Key = key,
                Title = title,
                Items = source
                    .Select(i => all.FirstOrDefault(x => string.Equals(x.Id, i.Id, StringComparison.OrdinalIgnoreCase)) ?? i)
                    .Take(limit)
                    .ToList()
            });
        }

        private static string BuildBecauseYouWatchedTitle(MediaUserBehaviorProfile profile)
        {
            var recent = (profile?.RecentTitle ?? "").Trim();
            if (string.IsNullOrWhiteSpace(recent))
                return "Because You Watched";

            if (recent.Length > 42)
                recent = recent.Substring(0, 42).TrimEnd() + "...";

            return $"Because You Watched {recent}";
        }

        private static List<EnrichedMediaObject> OrderByProfile(IEnumerable<EnrichedMediaObject> items, MediaUserBehaviorProfile profile)
        {
            var favoriteGenres = (profile?.TopGenres ?? new List<string>())
                .Where(g => !string.IsNullOrWhiteSpace(g))
                .Select(g => g.Trim())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            return DistinctByStableId(items)
                .Select(item => new
                {
                    Item = item,
                    Score = item.Genres.Count(g => favoriteGenres.Contains((g ?? "").Trim())) * 5d +
                            ((profile?.IsBingeWatcher == true && item.Kind == MediaKind.Series) ? 3d : 0d) +
                            (item.Ratings?.Tmdb ?? 0d) +
                            Math.Min(10d, (item.Ratings?.PopularityRaw ?? 0d) / 100d)
                })
                .OrderByDescending(x => x.Score)
                .ThenByDescending(x => x.Item.Ratings?.Tmdb ?? 0d)
                .ThenByDescending(x => x.Item.Ratings?.PopularityRaw ?? 0d)
                .Select(x => x.Item)
                .ToList();
        }

        private static List<EnrichedMediaObject> BuildBecauseYouWatchedShelf(MediaUserBehaviorProfile profile, ISet<string> excludedIds, int limit, params IEnumerable<EnrichedMediaObject>[] lists)
        {
            limit = Math.Max(1, limit);
            var recentTitle = (profile?.RecentTitle ?? "").Trim();
            if (string.IsNullOrWhiteSpace(recentTitle))
                return new List<EnrichedMediaObject>();

            var anchorGenres = (profile?.RecentGenres ?? new List<string>())
                .Where(g => !string.IsNullOrWhiteSpace(g))
                .Select(g => g.Trim())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (anchorGenres.Count == 0)
            {
                foreach (var genre in profile?.TopGenres ?? new List<string>())
                {
                    if (anchorGenres.Count >= 3)
                        break;
                    if (!string.IsNullOrWhiteSpace(genre))
                        anchorGenres.Add(genre.Trim());
                }
            }

            if (anchorGenres.Count == 0)
                return new List<EnrichedMediaObject>();

            return MergeAndDedupe(300, lists)
                .Where(item => item != null)
                .Where(item => !string.Equals((item.Title ?? "").Trim(), recentTitle, StringComparison.OrdinalIgnoreCase))
                .Where(item => excludedIds == null || !excludedIds.Contains(BuildDedupeKey(item)))
                .Select(item => new
                {
                    Item = item,
                    Score = item.Genres.Count(g => anchorGenres.Contains((g ?? "").Trim())) * 6d +
                            ((profile?.IsBingeWatcher == true && item.Kind == MediaKind.Series) ? 2d : 0d) +
                            (item.Ratings?.Tmdb ?? 0d) +
                            Math.Min(10d, (item.Ratings?.PopularityRaw ?? 0d) / 100d)
                })
                .Where(x => x.Score > 0)
                .OrderByDescending(x => x.Score)
                .ThenByDescending(x => x.Item.Ratings?.Tmdb ?? 0d)
                .ThenByDescending(x => x.Item.Ratings?.PopularityRaw ?? 0d)
                .Select(x => x.Item)
                .Take(limit)
                .ToList();
        }

        private async Task<MediaUserBehaviorProfile> BuildUserBehaviorProfileAsync(
            string tmdbApiKey,
            string traktClientId,
            string traktAccessToken,
            List<EnrichedMediaObject> continueWatching,
            CancellationToken ct)
        {
            var profile = new MediaUserBehaviorProfile
            {
                UnfinishedTitles = (continueWatching ?? new List<EnrichedMediaObject>())
                    .Where(i => i != null && !string.IsNullOrWhiteSpace(i.Title))
                    .OrderByDescending(i => i.LastInteractionUtc ?? DateTime.MinValue)
                    .Select(i => i.Title.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(10)
                    .ToList()
            };

            var history = await LoadHistorySamplesAsync(tmdbApiKey, traktClientId, traktAccessToken, ct).ConfigureAwait(false);
            if (history.Count == 0)
                return profile;

            var recent = history
                .OrderByDescending(h => h.WatchedAtUtc ?? DateTime.MinValue)
                .FirstOrDefault();

            profile.RecentTitle = (recent?.Title ?? "").Trim();
            profile.RecentGenres = recent?.Genres
                .Where(g => !string.IsNullOrWhiteSpace(g))
                .Select(g => g.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(4)
                .ToList() ?? new List<string>();

            profile.TopGenres = history
                .SelectMany(h => h.Genres)
                .Where(g => !string.IsNullOrWhiteSpace(g))
                .Select(g => g.Trim())
                .GroupBy(g => g, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(g => g.Count())
                .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                .Take(6)
                .Select(g => g.Key)
                .ToList();

            profile.TopActors = history
                .SelectMany(h => h.Cast.Take(4))
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .GroupBy(name => name.Trim(), StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(g => g.Count())
                .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                .Take(5)
                .Select(g => new MediaProfileCount { Name = g.Key, Count = g.Count() })
                .ToList();

            profile.TopDirectors = history
                .SelectMany(h => h.Directors.Take(3))
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .GroupBy(name => name.Trim(), StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(g => g.Count())
                .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                .Take(5)
                .Select(g => new MediaProfileCount { Name = g.Key, Count = g.Count() })
                .ToList();

            profile.BingeTitles = history
                .Where(h => h.Kind == MediaKind.Series && !string.IsNullOrWhiteSpace(h.Title))
                .Where(h => (DateTime.UtcNow - (h.WatchedAtUtc ?? DateTime.MinValue)).TotalDays <= 14)
                .GroupBy(h => h.Title.Trim(), StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() >= 2)
                .OrderByDescending(g => g.Count())
                .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                .Take(4)
                .Select(g => g.Key)
                .ToList();

            profile.IsBingeWatcher = profile.BingeTitles.Count > 0;
            profile.RecentViewingPatterns = BuildViewingPatterns(history, profile);
            return profile;
        }

        private async Task<List<HistorySample>> LoadHistorySamplesAsync(string tmdbApiKey, string traktClientId, string traktAccessToken, CancellationToken ct)
        {
            var samples = new List<HistorySample>();

            try
            {
                if (!string.IsNullOrWhiteSpace(traktClientId) && !string.IsNullOrWhiteSpace(traktAccessToken))
                {
                    var traktHistory = await _trakt.GetHistoryAsync(traktClientId, traktAccessToken, 80, ct).ConfigureAwait(false);
                    samples = traktHistory
                        .Select(item => new HistorySample
                        {
                            Title = ((item.Type ?? "").Trim().Equals("episode", StringComparison.OrdinalIgnoreCase) ? item.ShowTitle : item.Title) ?? "",
                            Kind = (item.Type ?? "").Trim().Equals("episode", StringComparison.OrdinalIgnoreCase) ? MediaKind.Series : MediaKind.Movie,
                            TmdbId = item.TmdbId,
                            ImdbId = (item.ImdbId ?? "").Trim(),
                            WatchedAtUtc = item.WatchedAtUtc,
                            Season = item.Season,
                            Episode = item.Episode
                        })
                        .Where(item => !string.IsNullOrWhiteSpace(item.Title))
                        .OrderByDescending(item => item.WatchedAtUtc ?? DateTime.MinValue)
                        .Take(30)
                        .ToList();
                }
            }
            catch
            {
            }

            if (samples.Count == 0)
            {
                samples = WatchHistoryStore.GetRecent(30)
                    .Select(r =>
                    {
                        var (tmdbId, imdbId) = ExtractStableIds(r.filePath);
                        return new HistorySample
                        {
                            Title = (r.title ?? "").Trim(),
                            Kind = string.Equals((r.type ?? "").Trim(), "tv", StringComparison.OrdinalIgnoreCase) ||
                                   string.Equals((r.type ?? "").Trim(), "series", StringComparison.OrdinalIgnoreCase)
                                ? MediaKind.Series
                                : MediaKind.Movie,
                            TmdbId = tmdbId > 0 ? tmdbId : null,
                            ImdbId = imdbId,
                            WatchedAtUtc = r.lastWatchedUtc
                        };
                    })
                    .Where(item => !string.IsNullOrWhiteSpace(item.Title))
                    .ToList();
            }

            if (samples.Count == 0 || string.IsNullOrWhiteSpace(tmdbApiKey))
                return samples;

            foreach (var sample in samples.Take(18))
            {
                ct.ThrowIfCancellationRequested();
                await EnrichHistorySampleAsync(tmdbApiKey, sample, ct).ConfigureAwait(false);
            }

            return samples;
        }

        private async Task EnrichHistorySampleAsync(string tmdbApiKey, HistorySample sample, CancellationToken ct)
        {
            if (sample == null || string.IsNullOrWhiteSpace(sample.Title))
                return;

            var tmdb = new TmdbClient();

            try
            {
                if (sample.Kind == MediaKind.Series)
                {
                    if (sample.TmdbId == null || sample.TmdbId.Value <= 0)
                    {
                        var tvHits = await tmdb.SearchTvAsync(tmdbApiKey, CleanLookupTitle(sample.Title, preferTv: true), ct).ConfigureAwait(false);
                        var tv = tvHits.FirstOrDefault(h => h != null && h.Id > 0);
                        if (tv != null)
                            sample.TmdbId = tv.Id;
                    }

                    if (sample.TmdbId != null && sample.TmdbId.Value > 0)
                    {
                        var details = await tmdb.GetTvDetailsAsync(tmdbApiKey, sample.TmdbId.Value, ct).ConfigureAwait(false);
                        if (details != null)
                        {
                            sample.Genres.Clear();
                            sample.Genres.AddRange(details.Genres.Where(g => !string.IsNullOrWhiteSpace(g)).Select(g => g.Trim()));
                        }

                        var credits = await tmdb.GetTvCreditsAsync(tmdbApiKey, sample.TmdbId.Value, ct).ConfigureAwait(false);
                        if (credits != null)
                        {
                            sample.Cast.Clear();
                            sample.Cast.AddRange(credits.Cast.Take(5));
                            sample.Directors.Clear();
                            sample.Directors.AddRange(credits.Directors.Take(3));
                        }
                    }

                    return;
                }

                if (sample.TmdbId == null || sample.TmdbId.Value <= 0)
                {
                    var movieHits = await tmdb.SearchMovieAsync(tmdbApiKey, CleanLookupTitle(sample.Title, preferTv: false), ct).ConfigureAwait(false);
                    var movie = movieHits.FirstOrDefault(h => h != null && h.Id > 0);
                    if (movie != null)
                        sample.TmdbId = movie.Id;
                }

                if (sample.TmdbId != null && sample.TmdbId.Value > 0)
                {
                    var details = await tmdb.GetMovieDetailsAsync(tmdbApiKey, sample.TmdbId.Value, ct).ConfigureAwait(false);
                    if (details != null)
                    {
                        sample.Genres.Clear();
                        sample.Genres.AddRange(details.Genres.Where(g => !string.IsNullOrWhiteSpace(g)).Select(g => g.Trim()));
                    }

                    var credits = await tmdb.GetMovieCreditsAsync(tmdbApiKey, sample.TmdbId.Value, ct).ConfigureAwait(false);
                    if (credits != null)
                    {
                        sample.Cast.Clear();
                        sample.Cast.AddRange(credits.Cast.Take(5));
                        sample.Directors.Clear();
                        sample.Directors.AddRange(credits.Directors.Take(3));
                    }
                }
            }
            catch
            {
            }
        }

        private static (int tmdbId, string imdbId) ExtractStableIds(string filePath)
        {
            var rawKey = (filePath ?? "").Trim();
            var keyToken = rawKey.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? rawKey;
            keyToken = (keyToken ?? "").Trim();

            var tmdbId = 0;
            var imdbId = "";
            if (!string.IsNullOrWhiteSpace(keyToken) && !keyToken.Contains('\\') && !keyToken.Contains('/'))
            {
                if (keyToken.StartsWith("tt", StringComparison.OrdinalIgnoreCase))
                {
                    imdbId = keyToken;
                }
                else if (keyToken.StartsWith("tmdb:", StringComparison.OrdinalIgnoreCase))
                {
                    var tail = keyToken.Substring("tmdb:".Length).Trim();
                    int.TryParse(tail, out tmdbId);
                }
            }

            return (tmdbId, imdbId);
        }

        private static List<MediaViewingPattern> BuildViewingPatterns(List<HistorySample> history, MediaUserBehaviorProfile profile)
        {
            var patterns = new List<MediaViewingPattern>();
            if (history == null || history.Count == 0)
                return patterns;

            var recent = history
                .OrderByDescending(h => h.WatchedAtUtc ?? DateTime.MinValue)
                .Take(12)
                .ToList();

            var daypart = recent
                .Where(h => h.WatchedAtUtc != null)
                .GroupBy(h => GetDaypart(h.WatchedAtUtc!.Value))
                .OrderByDescending(g => g.Count())
                .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
            if (daypart != null && daypart.Count() >= 3)
            {
                patterns.Add(new MediaViewingPattern
                {
                    Key = "daypart",
                    Description = $"{daypart.Key} viewing has dominated your recent sessions.",
                    Count = daypart.Count()
                });
            }

            var mediaBias = recent
                .GroupBy(h => h.Kind == MediaKind.Series ? "series" : "movies")
                .OrderByDescending(g => g.Count())
                .FirstOrDefault();
            if (mediaBias != null && mediaBias.Count() >= 3)
            {
                patterns.Add(new MediaViewingPattern
                {
                    Key = "format",
                    Description = $"You have been leaning toward {mediaBias.Key} lately.",
                    Count = mediaBias.Count()
                });
            }

            if (profile.TopGenres.Count > 0)
            {
                patterns.Add(new MediaViewingPattern
                {
                    Key = "genres",
                    Description = $"Your strongest genres right now are {string.Join(", ", profile.TopGenres.Take(3))}.",
                    Count = profile.TopGenres.Count
                });
            }

            if (profile.IsBingeWatcher && profile.BingeTitles.Count > 0)
            {
                patterns.Add(new MediaViewingPattern
                {
                    Key = "binge",
                    Description = $"Binge behavior detected around {string.Join(", ", profile.BingeTitles.Take(2))}.",
                    Count = profile.BingeTitles.Count
                });
            }

            return patterns.Take(4).ToList();
        }

        private static string GetDaypart(DateTime watchedAtUtc)
        {
            var hour = watchedAtUtc.ToLocalTime().Hour;
            if (hour < 6) return "Late-night";
            if (hour < 12) return "Morning";
            if (hour < 18) return "Afternoon";
            return "Evening";
        }

        private async Task<HashSet<string>> BuildFavoriteGenresAsync(string tmdbApiKey, CancellationToken ct)
        {
            // Very lightweight: use watch history items that already have cover/backdrop; resolve genres by searching TMDB lists.
            tmdbApiKey = (tmdbApiKey ?? "").Trim();
            var favorites = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            try
            {
                if (string.IsNullOrWhiteSpace(tmdbApiKey))
                    return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                var recent = WatchHistoryStore.GetRecent(12);
                if (recent.Count == 0)
                    return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                var tmdbSearch = new AtlasAI.MediaMetadata.TmdbClient();

                foreach (var r in recent)
                {
                    ct.ThrowIfCancellationRequested();
                    var title = (r.title ?? "").Trim();
                    if (string.IsNullOrWhiteSpace(title)) continue;

                    var isTv = string.Equals((r.type ?? "").Trim(), "tv", StringComparison.OrdinalIgnoreCase) ||
                               string.Equals((r.type ?? "").Trim(), "series", StringComparison.OrdinalIgnoreCase);

                    if (isTv)
                    {
                        var hits = await tmdbSearch.SearchTvAsync(tmdbApiKey, title, ct).ConfigureAwait(false);
                        var first = hits.FirstOrDefault();
                        if (first == null || first.Id <= 0) continue;
                        var details = await tmdbSearch.GetTvDetailsAsync(tmdbApiKey, first.Id, ct).ConfigureAwait(false);
                        if (details == null) continue;
                        foreach (var g in details.Genres)
                        {
                            if (string.IsNullOrWhiteSpace(g)) continue;
                            favorites[g] = favorites.TryGetValue(g, out var c) ? c + 1 : 1;
                        }
                    }
                    else
                    {
                        var hits = await tmdbSearch.SearchMovieAsync(tmdbApiKey, title, ct).ConfigureAwait(false);
                        var first = hits.FirstOrDefault();
                        if (first == null || first.Id <= 0) continue;
                        var details = await tmdbSearch.GetMovieDetailsAsync(tmdbApiKey, first.Id, ct).ConfigureAwait(false);
                        if (details == null) continue;
                        foreach (var g in details.Genres)
                        {
                            if (string.IsNullOrWhiteSpace(g)) continue;
                            favorites[g] = favorites.TryGetValue(g, out var c) ? c + 1 : 1;
                        }
                    }
                }

                return favorites
                    .OrderByDescending(kv => kv.Value)
                    .Take(6)
                    .Select(kv => kv.Key)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
            }
            catch
            {
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }
        }

        private static List<EnrichedMediaObject> BuildContinueWatching(int limit)
        {
            try
            {
                var recent = WatchHistoryStore.GetRecent(24);
                var now = DateTime.UtcNow;

                var list = new List<EnrichedMediaObject>();
                foreach (var r in recent)
                {
                    var rawKey = (r.filePath ?? "").Trim();
                    var keyToken = rawKey.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? rawKey;
                    keyToken = (keyToken ?? "").Trim();

                    var tmdbId = 0;
                    var imdbId = "";
                    if (!string.IsNullOrWhiteSpace(keyToken) && !keyToken.Contains('\\') && !keyToken.Contains('/'))
                    {
                        if (keyToken.StartsWith("tt", StringComparison.OrdinalIgnoreCase))
                        {
                            imdbId = keyToken;
                        }
                        else if (keyToken.StartsWith("tmdb:", StringComparison.OrdinalIgnoreCase))
                        {
                            var tail = keyToken.Substring("tmdb:".Length).Trim();
                            int.TryParse(tail, out tmdbId);
                        }
                    }

                    // Only keep items with a stable ID. This prevents stream-label items
                    // (e.g. addon/stream names like "Comet 1080p") from polluting Still Watching.
                    if (tmdbId <= 0 && string.IsNullOrWhiteSpace(imdbId))
                        continue;

                    var title = (r.title ?? "").Trim();
                    if (string.IsNullOrWhiteSpace(title)) continue;

                    var t = (r.type ?? "").Trim();
                    var isTv = string.Equals(t, "tv", StringComparison.OrdinalIgnoreCase) ||
                               string.Equals(t, "series", StringComparison.OrdinalIgnoreCase);

                    var progress = 0d;
                    if (r.durationSeconds > 0 && r.positionSeconds > 0)
                        progress = Math.Clamp((r.positionSeconds / r.durationSeconds) * 100d, 0d, 100d);

                    // forgotten if not watched in 14+ days but unfinished.
                    var forgotten = progress > 2 && progress < 92 && (now - r.lastWatchedUtc).TotalDays >= 14;

                    var item = new EnrichedMediaObject
                    {
                        Kind = isTv ? MediaKind.Series : MediaKind.Movie,
                        TmdbId = tmdbId,
                        ImdbId = imdbId,
                        Title = title,
                        Poster = (r.coverUrl ?? "").Trim(),
                        Backdrop = (r.backdropUrl ?? "").Trim(),
                        LastInteractionUtc = r.lastWatchedUtc,
                        Ratings = new MediaRatings { EngagementRaw = progress },
                        PersonalAffinity = 10,
                        IsForgottenContinue = forgotten,
                        // Release date/genres unknown without API resolution.
                    };

                    item.Id = MediaScoring.BuildStableId(item.Kind, item.TmdbId, item.ImdbId, item.Title, item.ReleaseDate);
                    list.Add(item);
                }

                return FilterAndDeduplicateContinueWatching(list, limit);
            }
            catch
            {
                return new List<EnrichedMediaObject>();
            }
        }

        private async Task<List<EnrichedMediaObject>> BuildStillWatchingAsync(string traktClientId, string traktAccessToken, int limit, CancellationToken ct)
        {
            limit = Math.Max(1, limit);
            traktClientId = (traktClientId ?? "").Trim();
            traktAccessToken = (traktAccessToken ?? "").Trim();

            try
            {
                if (!string.IsNullOrWhiteSpace(traktClientId) && !string.IsNullOrWhiteSpace(traktAccessToken))
                {
                    var playback = await _trakt.GetPlaybackAsync(traktClientId, traktAccessToken, limit: 40, ct).ConfigureAwait(false);
                    var fromTrakt = BuildStillWatchingFromTrakt(playback);
                    if (fromTrakt.Count > 0)
                        return FilterAndDeduplicateContinueWatching(fromTrakt, limit);
                }
            }
            catch
            {
            }

            // Fallback (no Trakt token configured): use local watch history progress.
            return BuildContinueWatching(limit);
        }

        private static List<EnrichedMediaObject> BuildStillWatchingFromTrakt(List<TraktClient.TraktPlaybackItem> playback)
        {
            try
            {
                if (playback == null || playback.Count == 0)
                    return new List<EnrichedMediaObject>();

                var list = new List<EnrichedMediaObject>();
                foreach (var p in playback)
                {
                    if (p == null) continue;

                    var type = (p.Type ?? "").Trim();
                    var isEpisode = string.Equals(type, "episode", StringComparison.OrdinalIgnoreCase) ||
                                    (!string.IsNullOrWhiteSpace(p.ShowTitle) || p.ShowTmdbId != null || !string.IsNullOrWhiteSpace(p.ShowImdbId));

                    if (isEpisode)
                    {
                        var title = (p.ShowTitle ?? "").Trim();
                        if (string.IsNullOrWhiteSpace(title)) continue;

                        var item = new EnrichedMediaObject
                        {
                            Kind = MediaKind.Series,
                            TmdbId = p.ShowTmdbId ?? 0,
                            ImdbId = (p.ShowImdbId ?? "").Trim(),
                            Title = title,
                            ReleaseDate = p.ShowYear != null && p.ShowYear.Value > 0 ? new DateTime(p.ShowYear.Value, 1, 1) : null,
                            LastInteractionUtc = p.PausedAtUtc,
                            Ratings = new MediaRatings { EngagementRaw = Math.Clamp(p.ProgressPercent, 0d, 100d) },
                            PersonalAffinity = 10
                        };

                        item.Id = MediaScoring.BuildStableId(item.Kind, item.TmdbId, item.ImdbId, item.Title, item.ReleaseDate);
                        list.Add(item);
                        continue;
                    }

                    // movie
                    var mTitle = (p.MovieTitle ?? "").Trim();
                    if (string.IsNullOrWhiteSpace(mTitle)) continue;

                    var movie = new EnrichedMediaObject
                    {
                        Kind = MediaKind.Movie,
                        TmdbId = p.MovieTmdbId ?? 0,
                        ImdbId = (p.MovieImdbId ?? "").Trim(),
                        Title = mTitle,
                        ReleaseDate = p.MovieYear != null && p.MovieYear.Value > 0 ? new DateTime(p.MovieYear.Value, 1, 1) : null,
                        LastInteractionUtc = p.PausedAtUtc,
                        Ratings = new MediaRatings { EngagementRaw = Math.Clamp(p.ProgressPercent, 0d, 100d) },
                        PersonalAffinity = 10
                    };

                    movie.Id = MediaScoring.BuildStableId(movie.Kind, movie.TmdbId, movie.ImdbId, movie.Title, movie.ReleaseDate);
                    list.Add(movie);
                }

                // Dedupe by stable id/title and keep most progressed first.
                return FilterAndDeduplicateContinueWatching(list, 40);
            }
            catch
            {
                return new List<EnrichedMediaObject>();
            }
        }

        private async Task<List<EnrichedMediaObject>> BuildContinueWatchingShelfAsync(string tmdbApiKey, string traktClientId, string traktAccessToken, int limit, CancellationToken ct)
        {
            limit = Math.Max(1, limit);
            var items = await BuildStillWatchingAsync(traktClientId, traktAccessToken, limit, ct).ConfigureAwait(false);
            await TryEnrichContinueWatchingAsync(tmdbApiKey, items, ct).ConfigureAwait(false);
            return FilterAndDeduplicateContinueWatching(items, limit);
        }

        private async Task<List<EnrichedMediaObject>> BuildLatestMoviesShelfAsync(string tmdbApiKey, CancellationToken ct)
        {
            var since = DateTime.UtcNow.Date.AddDays(-30);
            var until = DateTime.UtcNow.Date;
            var endpoints = new[]
            {
                $"discover/movie?sort_by=primary_release_date.desc&include_adult=false&include_video=false&primary_release_date.gte={since:yyyy-MM-dd}&primary_release_date.lte={until:yyyy-MM-dd}&vote_count.gte=25",
                $"discover/movie?sort_by=primary_release_date.desc&include_adult=false&include_video=false&primary_release_date.gte={since:yyyy-MM-dd}&primary_release_date.lte={until:yyyy-MM-dd}&vote_count.gte=5",
                $"discover/movie?sort_by=popularity.desc&include_adult=false&include_video=false&primary_release_date.gte={since:yyyy-MM-dd}&primary_release_date.lte={until:yyyy-MM-dd}&vote_count.gte=1"
            };

            var tasks = endpoints
                .Select(endpoint => _tmdb.FetchListPagesAsync(tmdbApiKey, endpoint, MediaKind.Movie, 3, ct))
                .ToArray();

            await Task.WhenAll(tasks).ConfigureAwait(false);

            return DistinctByStableId(tasks
                .SelectMany(task => task.Result)
                .Where(i => i != null &&
                            i.ReleaseDate != null &&
                            i.ReleaseDate.Value.Date >= since &&
                            i.ReleaseDate.Value.Date <= until &&
                            i.VoteCount >= 1)
                .OrderByDescending(i => i.ReleaseDate)
                .ThenByDescending(i => i.Ratings.PopularityRaw)
                .ThenByDescending(i => i.VoteCount));
        }

        private async Task<List<EnrichedMediaObject>> BuildLatestSeriesShelfAsync(string tmdbApiKey, CancellationToken ct)
        {
            var since = DateTime.UtcNow.Date.AddDays(-30);
            var until = DateTime.UtcNow.Date;
            var endpoints = new[]
            {
                $"discover/tv?sort_by=first_air_date.desc&include_adult=false&first_air_date.gte={since:yyyy-MM-dd}&first_air_date.lte={until:yyyy-MM-dd}&vote_count.gte=25",
                $"discover/tv?sort_by=first_air_date.desc&include_adult=false&first_air_date.gte={since:yyyy-MM-dd}&first_air_date.lte={until:yyyy-MM-dd}&vote_count.gte=5",
                $"discover/tv?sort_by=popularity.desc&include_adult=false&first_air_date.gte={since:yyyy-MM-dd}&first_air_date.lte={until:yyyy-MM-dd}&vote_count.gte=1"
            };

            var tasks = endpoints
                .Select(endpoint => _tmdb.FetchListPagesAsync(tmdbApiKey, endpoint, MediaKind.Series, 3, ct))
                .ToArray();

            await Task.WhenAll(tasks).ConfigureAwait(false);

            return DistinctByStableId(tasks
                .SelectMany(task => task.Result)
                .Where(i => i != null &&
                            i.ReleaseDate != null &&
                            i.ReleaseDate.Value.Date >= since &&
                            i.ReleaseDate.Value.Date <= until &&
                            i.VoteCount >= 1)
                .OrderByDescending(i => i.ReleaseDate)
                .ThenByDescending(i => i.Ratings.PopularityRaw)
                .ThenByDescending(i => i.VoteCount));
        }

        private async Task<List<EnrichedMediaObject>> BuildTrendingThisWeekShelfAsync(string tmdbApiKey, string traktClientId, CancellationToken ct)
        {
            var tmdbTrendingTask = _tmdb.FetchMixedListPagesAsync(tmdbApiKey, "trending/all/week", 2, ct);
            var traktMoviesTask = _trakt.GetTrendingMoviesAsync(traktClientId, ct);
            var traktShowsTask = _trakt.GetTrendingShowsAsync(traktClientId, ct);

            await Task.WhenAll(tmdbTrendingTask, traktMoviesTask, traktShowsTask).ConfigureAwait(false);

            var items = DistinctByStableId(tmdbTrendingTask.Result)
                .Where(i => i != null && (i.Kind == MediaKind.Movie || i.Kind == MediaKind.Series))
                .ToList();

            ApplyTraktTrendSignals(items, traktMoviesTask.Result, traktShowsTask.Result);

            return items
                .OrderByDescending(i => i.Ratings.PopularityRaw)
                .ThenByDescending(i => i.Ratings.TrendRaw)
                .ThenByDescending(i => i.Ratings.Tmdb)
                .ToList();
        }

        private async Task<List<EnrichedMediaObject>> BuildMarvelCollectionShelfAsync(string tmdbApiKey, CancellationToken ct)
        {
            var marvelStudiosIdTask = _tmdb.SearchCompanyIdAsync(tmdbApiKey, "Marvel Studios", ct);
            var marvelKeywordIdTask = _tmdb.SearchKeywordIdAsync(tmdbApiKey, "Marvel Comics", ct);
            await Task.WhenAll(marvelStudiosIdTask, marvelKeywordIdTask).ConfigureAwait(false);

            var tasks = new List<Task<List<EnrichedMediaObject>>>();
            var companyId = marvelStudiosIdTask.Result;
            var keywordId = marvelKeywordIdTask.Result;

            if (companyId != null && companyId.Value > 0)
            {
                tasks.Add(_tmdb.FetchListPagesAsync(tmdbApiKey, $"discover/movie?sort_by=primary_release_date.asc&include_adult=false&vote_count.gte=10&with_companies={companyId.Value}", MediaKind.Movie, 4, ct));
                tasks.Add(_tmdb.FetchListPagesAsync(tmdbApiKey, $"discover/tv?sort_by=first_air_date.asc&include_adult=false&vote_count.gte=10&with_companies={companyId.Value}", MediaKind.Series, 4, ct));
            }

            if (keywordId != null && keywordId.Value > 0)
            {
                tasks.Add(_tmdb.FetchListPagesAsync(tmdbApiKey, $"discover/movie?sort_by=primary_release_date.asc&include_adult=false&vote_count.gte=10&with_keywords={keywordId.Value}", MediaKind.Movie, 4, ct));
                tasks.Add(_tmdb.FetchListPagesAsync(tmdbApiKey, $"discover/tv?sort_by=first_air_date.asc&include_adult=false&vote_count.gte=10&with_keywords={keywordId.Value}", MediaKind.Series, 4, ct));
            }

            if (tasks.Count == 0)
                return new List<EnrichedMediaObject>();

            await Task.WhenAll(tasks).ConfigureAwait(false);
            return DistinctByStableId(MergeAndDedupe(300, tasks.Select(t => t.Result).ToArray())
                .OrderBy(i => i.ReleaseDate ?? DateTime.MaxValue)
                .ThenBy(i => i.Title, StringComparer.OrdinalIgnoreCase));
        }

        private async Task<List<EnrichedMediaObject>> BuildDcCollectionShelfAsync(string tmdbApiKey, CancellationToken ct)
        {
            var dcEntertainmentTask = _tmdb.SearchCompanyIdAsync(tmdbApiKey, "DC Entertainment", ct);
            var dcStudiosTask = _tmdb.SearchCompanyIdAsync(tmdbApiKey, "DC Studios", ct);
            var dcKeywordTask = _tmdb.SearchKeywordIdAsync(tmdbApiKey, "DC Comics", ct);
            await Task.WhenAll(dcEntertainmentTask, dcStudiosTask, dcKeywordTask).ConfigureAwait(false);

            var tasks = new List<Task<List<EnrichedMediaObject>>>();
            foreach (var companyId in new[] { dcEntertainmentTask.Result, dcStudiosTask.Result }.Where(id => id != null && id.Value > 0).Distinct())
            {
                tasks.Add(_tmdb.FetchListPagesAsync(tmdbApiKey, $"discover/movie?sort_by=popularity.desc&include_adult=false&vote_count.gte=10&with_companies={companyId!.Value}", MediaKind.Movie, 4, ct));
                tasks.Add(_tmdb.FetchListPagesAsync(tmdbApiKey, $"discover/tv?sort_by=popularity.desc&include_adult=false&vote_count.gte=10&with_companies={companyId!.Value}", MediaKind.Series, 4, ct));
            }

            if (dcKeywordTask.Result != null && dcKeywordTask.Result.Value > 0)
            {
                tasks.Add(_tmdb.FetchListPagesAsync(tmdbApiKey, $"discover/movie?sort_by=popularity.desc&include_adult=false&vote_count.gte=10&with_keywords={dcKeywordTask.Result.Value}", MediaKind.Movie, 4, ct));
                tasks.Add(_tmdb.FetchListPagesAsync(tmdbApiKey, $"discover/tv?sort_by=popularity.desc&include_adult=false&vote_count.gte=10&with_keywords={dcKeywordTask.Result.Value}", MediaKind.Series, 4, ct));
            }

            if (tasks.Count == 0)
                return new List<EnrichedMediaObject>();

            await Task.WhenAll(tasks).ConfigureAwait(false);
            return DistinctByStableId(MergeAndDedupe(300, tasks.Select(t => t.Result).ToArray())
                .OrderByDescending(i => i.Ratings.PopularityRaw)
                .ThenByDescending(i => i.Ratings.Tmdb));
        }

        private async Task<List<EnrichedMediaObject>> BuildAllTimeGreatsShelfAsync(string tmdbApiKey, CancellationToken ct)
        {
            var endpoint = "discover/movie?sort_by=vote_average.desc&include_adult=false&include_video=false&vote_count.gte=5000";
            var items = await _tmdb.FetchListPagesAsync(tmdbApiKey, endpoint, MediaKind.Movie, 6, ct).ConfigureAwait(false);
            return DistinctByStableId(items
                .Where(i => i != null && i.VoteCount >= 5000 && (i.Ratings?.Tmdb ?? 0) >= 8.5d)
                .OrderByDescending(i => i.Ratings.Tmdb)
                .ThenByDescending(i => i.VoteCount));
        }

        private async Task<List<EnrichedMediaObject>> BuildKidsChoiceShelfAsync(string tmdbApiKey, CancellationToken ct)
        {
            var movieEndpoint = "discover/movie?sort_by=popularity.desc&include_adult=false&include_video=false&vote_count.gte=50&certification_country=US&certification.lte=PG&with_genres=16,10751";
            var tvEndpoint = "discover/tv?sort_by=popularity.desc&include_adult=false&vote_count.gte=30&with_genres=16,10751";

            var moviesTask = _tmdb.FetchListPagesAsync(tmdbApiKey, movieEndpoint, MediaKind.Movie, 3, ct);
            var tvTask = _tmdb.FetchListPagesAsync(tmdbApiKey, tvEndpoint, MediaKind.Series, 3, ct);
            await Task.WhenAll(moviesTask, tvTask).ConfigureAwait(false);

            return DistinctByStableId(MergeAndDedupe(240, moviesTask.Result, tvTask.Result)
                .Where(i => i != null && i.Genres.Any(g =>
                    string.Equals(g, "Family", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(g, "Animation", StringComparison.OrdinalIgnoreCase)))
                .OrderByDescending(i => i.Ratings.PopularityRaw)
                .ThenByDescending(i => i.Ratings.Tmdb)
                .ThenByDescending(i => i.VoteCount));
        }

        private async Task<List<EnrichedMediaObject>> BuildHiddenGemsShelfAsync(string tmdbApiKey, CancellationToken ct)
        {
            var endpoint = "discover/movie?sort_by=vote_average.desc&include_adult=false&include_video=false&vote_average.gte=7.5&vote_count.gte=100&vote_count.lte=1500";
            var items = await _tmdb.FetchListPagesAsync(tmdbApiKey, endpoint, MediaKind.Movie, 4, ct).ConfigureAwait(false);
            return DistinctByStableId(items
                .Where(i => i != null && (i.Ratings?.Tmdb ?? 0) >= 7.5d && i.VoteCount >= 100 && i.VoteCount <= 1500)
                .OrderByDescending(i => i.Ratings.Tmdb)
                .ThenByDescending(i => i.VoteCount)
                .ThenByDescending(i => i.Ratings.PopularityRaw));
        }

        private async Task<List<EnrichedMediaObject>> BuildMindBendingMoviesShelfAsync(string tmdbApiKey, CancellationToken ct)
        {
            var endpoint = "discover/movie?sort_by=vote_average.desc&include_adult=false&include_video=false&vote_count.gte=200&with_genres=878,9648,53";
            var items = await _tmdb.FetchListPagesAsync(tmdbApiKey, endpoint, MediaKind.Movie, 4, ct).ConfigureAwait(false);
            return DistinctByStableId(items
                .Where(i => i != null && i.Genres.Any(g => string.Equals(g, "Science Fiction", StringComparison.OrdinalIgnoreCase) || string.Equals(g, "Mystery", StringComparison.OrdinalIgnoreCase) || string.Equals(g, "Thriller", StringComparison.OrdinalIgnoreCase)))
                .OrderByDescending(i => i.Ratings.Tmdb)
                .ThenByDescending(i => i.Ratings.PopularityRaw));
        }

        private async Task<List<EnrichedMediaObject>> BuildLateNightPicksShelfAsync(string tmdbApiKey, CancellationToken ct)
        {
            var endpoint = "discover/movie?sort_by=popularity.desc&include_adult=false&include_video=false&vote_count.gte=100&with_genres=80,53,9648";
            var items = await _tmdb.FetchListPagesAsync(tmdbApiKey, endpoint, MediaKind.Movie, 4, ct).ConfigureAwait(false);
            return DistinctByStableId(items
                .Where(i => i != null && i.Genres.Any(g => string.Equals(g, "Crime", StringComparison.OrdinalIgnoreCase) || string.Equals(g, "Thriller", StringComparison.OrdinalIgnoreCase) || string.Equals(g, "Mystery", StringComparison.OrdinalIgnoreCase)))
                .OrderByDescending(i => i.Ratings.PopularityRaw)
                .ThenByDescending(i => i.Ratings.Tmdb));
        }

        private async Task<List<EnrichedMediaObject>> BuildActionNightShelfAsync(string tmdbApiKey, CancellationToken ct)
        {
            var endpoint = "discover/movie?sort_by=popularity.desc&include_adult=false&include_video=false&vote_count.gte=200&with_genres=28";
            var items = await _tmdb.FetchListPagesAsync(tmdbApiKey, endpoint, MediaKind.Movie, 4, ct).ConfigureAwait(false);
            return DistinctByStableId(items
                .Where(i => i != null)
                .OrderByDescending(i => i.Ratings.PopularityRaw)
                .ThenByDescending(i => i.Ratings.Tmdb));
        }

        private async Task<List<EnrichedMediaObject>> BuildBingeWorthySeriesShelfAsync(string tmdbApiKey, CancellationToken ct)
        {
            var endpoint = "discover/tv?sort_by=popularity.desc&include_adult=false&vote_count.gte=150";
            var items = DistinctByStableId(await _tmdb.FetchListPagesAsync(tmdbApiKey, endpoint, MediaKind.Series, 4, ct).ConfigureAwait(false))
                .OrderByDescending(i => i.Ratings.PopularityRaw)
                .ThenByDescending(i => i.Ratings.Tmdb)
                .Take(24)
                .ToList();

            if (string.IsNullOrWhiteSpace(tmdbApiKey))
                return items;

            var tmdb = new TmdbClient();
            var results = new List<EnrichedMediaObject>();
            foreach (var item in items)
            {
                ct.ThrowIfCancellationRequested();

                if (item.TmdbId == null || item.TmdbId.Value <= 0)
                    continue;

                try
                {
                    var details = await tmdb.GetTvDetailsAsync(tmdbApiKey, item.TmdbId.Value, ct).ConfigureAwait(false);
                    if (details == null || details.NumberOfSeasons < 2)
                        continue;

                    if (item.RuntimeMinutes <= 0 && details.EpisodeRuntimeMinutes > 0)
                        item.RuntimeMinutes = details.EpisodeRuntimeMinutes;
                    if (item.Genres.Count == 0 && details.Genres.Count > 0)
                        item.Genres = details.Genres.Where(g => !string.IsNullOrWhiteSpace(g)).Select(g => g.Trim()).ToList();

                    results.Add(item);
                }
                catch
                {
                }
            }

            return results.Count > 0 ? results : items;
        }

        private static async Task TryEnrichContinueWatchingAsync(string tmdbApiKey, List<EnrichedMediaObject> continueWatching, CancellationToken ct)
        {
            tmdbApiKey = (tmdbApiKey ?? "").Trim();
            if (string.IsNullOrWhiteSpace(tmdbApiKey)) return;
            if (continueWatching == null || continueWatching.Count == 0) return;

            // Only enrich items that actually need it, and cap calls.
            var needs = continueWatching
                .Where(i => i != null)
                .Where(i => string.IsNullOrWhiteSpace(i.Poster) || string.IsNullOrWhiteSpace(i.Backdrop) || string.IsNullOrWhiteSpace(i.Overview) || i.Genres.Count == 0 || i.RuntimeMinutes <= 0)
                .OrderByDescending(i => i.Ratings?.EngagementRaw ?? 0)
                .Take(12)
                .ToList();

            if (needs.Count == 0) return;

            var tmdb = new TmdbClient();

            foreach (var item in needs)
            {
                ct.ThrowIfCancellationRequested();

                try
                {
                    var rawTitle = (item.Title ?? "").Trim();
                    if (string.IsNullOrWhiteSpace(rawTitle)) continue;

                    var preferTv = item.Kind == MediaKind.Series;
                    var query = CleanLookupTitle(rawTitle, preferTv);
                    if (string.IsNullOrWhiteSpace(query)) continue;

                    // Try preferred kind first, then fallback.
                    if (preferTv)
                    {
                        var tvHits = await tmdb.SearchTvAsync(tmdbApiKey, query, ct).ConfigureAwait(false);
                        var tv = tvHits?.FirstOrDefault(h => h != null && h.Id > 0);
                        if (tv != null)
                        {
                            item.Kind = MediaKind.Series;
                            item.TmdbId = tv.Id;
                            if (string.IsNullOrWhiteSpace(item.Poster)) item.Poster = TmdbClient.BuildPosterUrl(tv.PosterPath, "w500");
                            if (string.IsNullOrWhiteSpace(item.Backdrop)) item.Backdrop = TmdbClient.BuildBackdropUrl(tv.BackdropPath, "w780");
                            if (string.IsNullOrWhiteSpace(item.Poster) && !string.IsNullOrWhiteSpace(item.Backdrop)) item.Poster = item.Backdrop;
                            if (item.ReleaseDate == null) item.ReleaseDate = tv.FirstAirDate;

                            var details = await tmdb.GetTvDetailsAsync(tmdbApiKey, tv.Id, ct).ConfigureAwait(false);
                            if (details != null)
                            {
                                if (string.IsNullOrWhiteSpace(item.Overview) && !string.IsNullOrWhiteSpace(details.Overview)) item.Overview = details.Overview.Trim();
                                if (item.ReleaseDate == null) item.ReleaseDate = details.FirstAirDate;
                                if (item.RuntimeMinutes <= 0 && details.EpisodeRuntimeMinutes > 0) item.RuntimeMinutes = details.EpisodeRuntimeMinutes;
                                if (item.Genres.Count == 0 && details.Genres.Count > 0) item.Genres = details.Genres.Where(g => !string.IsNullOrWhiteSpace(g)).Select(g => g.Trim()).ToList();
                                if (string.IsNullOrWhiteSpace(item.Poster)) item.Poster = TmdbClient.BuildPosterUrl(details.PosterPath, "w500");
                                if (string.IsNullOrWhiteSpace(item.Backdrop)) item.Backdrop = TmdbClient.BuildBackdropUrl(details.BackdropPath, "w780");
                                if (string.IsNullOrWhiteSpace(item.Poster) && !string.IsNullOrWhiteSpace(item.Backdrop)) item.Poster = item.Backdrop;
                                if (item.Ratings != null && item.Ratings.Tmdb <= 0 && details.VoteAverage > 0)
                                {
                                    item.Ratings.Tmdb = details.VoteAverage;
                                    if (item.Ratings.Critic <= 0) item.Ratings.Critic = details.VoteAverage;
                                    if (item.Ratings.Audience <= 0) item.Ratings.Audience = details.VoteAverage;
                                }
                            }

                            item.Id = MediaScoring.BuildStableId(item.Kind, item.TmdbId, item.ImdbId, item.Title, item.ReleaseDate);
                            continue;
                        }

                        var movieHits = await tmdb.SearchMovieAsync(tmdbApiKey, query, ct).ConfigureAwait(false);
                        var movie = movieHits?.FirstOrDefault(h => h != null && h.Id > 0);
                        if (movie != null)
                        {
                            item.Kind = MediaKind.Movie;
                            item.TmdbId = movie.Id;
                            if (string.IsNullOrWhiteSpace(item.Poster)) item.Poster = TmdbClient.BuildPosterUrl(movie.PosterPath, "w500");
                            if (string.IsNullOrWhiteSpace(item.Backdrop)) item.Backdrop = TmdbClient.BuildBackdropUrl(movie.BackdropPath, "w780");
                            if (string.IsNullOrWhiteSpace(item.Poster) && !string.IsNullOrWhiteSpace(item.Backdrop)) item.Poster = item.Backdrop;
                            if (item.ReleaseDate == null) item.ReleaseDate = movie.ReleaseDate;

                            var details = await tmdb.GetMovieDetailsAsync(tmdbApiKey, movie.Id, ct).ConfigureAwait(false);
                            if (details != null)
                            {
                                if (string.IsNullOrWhiteSpace(item.Overview) && !string.IsNullOrWhiteSpace(details.Overview)) item.Overview = details.Overview.Trim();
                                if (item.ReleaseDate == null) item.ReleaseDate = details.ReleaseDate;
                                if (item.RuntimeMinutes <= 0 && details.RuntimeMinutes > 0) item.RuntimeMinutes = details.RuntimeMinutes;
                                if (item.Genres.Count == 0 && details.Genres.Count > 0) item.Genres = details.Genres.Where(g => !string.IsNullOrWhiteSpace(g)).Select(g => g.Trim()).ToList();
                                if (string.IsNullOrWhiteSpace(item.Poster)) item.Poster = TmdbClient.BuildPosterUrl(details.PosterPath, "w500");
                                if (string.IsNullOrWhiteSpace(item.Backdrop)) item.Backdrop = TmdbClient.BuildBackdropUrl(details.BackdropPath, "w780");
                                if (string.IsNullOrWhiteSpace(item.Poster) && !string.IsNullOrWhiteSpace(item.Backdrop)) item.Poster = item.Backdrop;
                                if (item.Ratings != null && item.Ratings.Tmdb <= 0 && details.VoteAverage > 0)
                                {
                                    item.Ratings.Tmdb = details.VoteAverage;
                                    if (item.Ratings.Critic <= 0) item.Ratings.Critic = details.VoteAverage;
                                    if (item.Ratings.Audience <= 0) item.Ratings.Audience = details.VoteAverage;
                                }
                            }

                            item.Id = MediaScoring.BuildStableId(item.Kind, item.TmdbId, item.ImdbId, item.Title, item.ReleaseDate);
                            continue;
                        }
                    }
                    else
                    {
                        var movieHits = await tmdb.SearchMovieAsync(tmdbApiKey, query, ct).ConfigureAwait(false);
                        var movie = movieHits?.FirstOrDefault(h => h != null && h.Id > 0);
                        if (movie != null)
                        {
                            item.Kind = MediaKind.Movie;
                            item.TmdbId = movie.Id;
                            if (string.IsNullOrWhiteSpace(item.Poster)) item.Poster = TmdbClient.BuildPosterUrl(movie.PosterPath, "w500");
                            if (string.IsNullOrWhiteSpace(item.Backdrop)) item.Backdrop = TmdbClient.BuildBackdropUrl(movie.BackdropPath, "w780");
                            if (string.IsNullOrWhiteSpace(item.Poster) && !string.IsNullOrWhiteSpace(item.Backdrop)) item.Poster = item.Backdrop;
                            if (item.ReleaseDate == null) item.ReleaseDate = movie.ReleaseDate;

                            var details = await tmdb.GetMovieDetailsAsync(tmdbApiKey, movie.Id, ct).ConfigureAwait(false);
                            if (details != null)
                            {
                                if (string.IsNullOrWhiteSpace(item.Overview) && !string.IsNullOrWhiteSpace(details.Overview)) item.Overview = details.Overview.Trim();
                                if (item.ReleaseDate == null) item.ReleaseDate = details.ReleaseDate;
                                if (item.RuntimeMinutes <= 0 && details.RuntimeMinutes > 0) item.RuntimeMinutes = details.RuntimeMinutes;
                                if (item.Genres.Count == 0 && details.Genres.Count > 0) item.Genres = details.Genres.Where(g => !string.IsNullOrWhiteSpace(g)).Select(g => g.Trim()).ToList();
                                if (string.IsNullOrWhiteSpace(item.Poster)) item.Poster = TmdbClient.BuildPosterUrl(details.PosterPath, "w500");
                                if (string.IsNullOrWhiteSpace(item.Backdrop)) item.Backdrop = TmdbClient.BuildBackdropUrl(details.BackdropPath, "w780");
                                if (string.IsNullOrWhiteSpace(item.Poster) && !string.IsNullOrWhiteSpace(item.Backdrop)) item.Poster = item.Backdrop;
                                if (item.Ratings != null && item.Ratings.Tmdb <= 0 && details.VoteAverage > 0)
                                {
                                    item.Ratings.Tmdb = details.VoteAverage;
                                    if (item.Ratings.Critic <= 0) item.Ratings.Critic = details.VoteAverage;
                                    if (item.Ratings.Audience <= 0) item.Ratings.Audience = details.VoteAverage;
                                }
                            }

                            item.Id = MediaScoring.BuildStableId(item.Kind, item.TmdbId, item.ImdbId, item.Title, item.ReleaseDate);
                            continue;
                        }

                        var tvHits = await tmdb.SearchTvAsync(tmdbApiKey, query, ct).ConfigureAwait(false);
                        var tv = tvHits?.FirstOrDefault(h => h != null && h.Id > 0);
                        if (tv != null)
                        {
                            item.Kind = MediaKind.Series;
                            item.TmdbId = tv.Id;
                            if (string.IsNullOrWhiteSpace(item.Poster)) item.Poster = TmdbClient.BuildPosterUrl(tv.PosterPath, "w500");
                            if (string.IsNullOrWhiteSpace(item.Backdrop)) item.Backdrop = TmdbClient.BuildBackdropUrl(tv.BackdropPath, "w780");
                            if (string.IsNullOrWhiteSpace(item.Poster) && !string.IsNullOrWhiteSpace(item.Backdrop)) item.Poster = item.Backdrop;
                            if (item.ReleaseDate == null) item.ReleaseDate = tv.FirstAirDate;

                            var details = await tmdb.GetTvDetailsAsync(tmdbApiKey, tv.Id, ct).ConfigureAwait(false);
                            if (details != null)
                            {
                                if (string.IsNullOrWhiteSpace(item.Overview) && !string.IsNullOrWhiteSpace(details.Overview)) item.Overview = details.Overview.Trim();
                                if (item.ReleaseDate == null) item.ReleaseDate = details.FirstAirDate;
                                if (item.RuntimeMinutes <= 0 && details.EpisodeRuntimeMinutes > 0) item.RuntimeMinutes = details.EpisodeRuntimeMinutes;
                                if (item.Genres.Count == 0 && details.Genres.Count > 0) item.Genres = details.Genres.Where(g => !string.IsNullOrWhiteSpace(g)).Select(g => g.Trim()).ToList();
                                if (string.IsNullOrWhiteSpace(item.Poster)) item.Poster = TmdbClient.BuildPosterUrl(details.PosterPath, "w500");
                                if (string.IsNullOrWhiteSpace(item.Backdrop)) item.Backdrop = TmdbClient.BuildBackdropUrl(details.BackdropPath, "w780");
                                if (string.IsNullOrWhiteSpace(item.Poster) && !string.IsNullOrWhiteSpace(item.Backdrop)) item.Poster = item.Backdrop;
                                if (item.Ratings != null && item.Ratings.Tmdb <= 0 && details.VoteAverage > 0)
                                {
                                    item.Ratings.Tmdb = details.VoteAverage;
                                    if (item.Ratings.Critic <= 0) item.Ratings.Critic = details.VoteAverage;
                                    if (item.Ratings.Audience <= 0) item.Ratings.Audience = details.VoteAverage;
                                }
                            }

                            item.Id = MediaScoring.BuildStableId(item.Kind, item.TmdbId, item.ImdbId, item.Title, item.ReleaseDate);
                            continue;
                        }
                    }
                }
                catch
                {
                }
            }
        }

        private static string CleanLookupTitle(string title, bool preferTv)
        {
            var s = (title ?? "").Trim();
            if (string.IsNullOrWhiteSpace(s))
                return "";

            var year = "";
            var yearMatch = Regex.Match(s, @"\b(19|20)\d{2}\b", RegexOptions.IgnoreCase);
            if (yearMatch.Success)
                year = yearMatch.Value;

            s = s.Replace('.', ' ').Replace('_', ' ').Replace('-', ' ');
            s = Regex.Replace(s, @"[\[\(\{][^\]\)\}]{1,120}[\]\)\}]", " ", RegexOptions.IgnoreCase);

            s = Regex.Replace(s, @"(?i)\bS\d{1,2}E\d{1,2}\b", " ");
            s = Regex.Replace(s, @"(?i)\b\d{1,2}x\d{1,2}\b", " ");
            s = Regex.Replace(s, @"(?i)\bseason\s*\d{1,2}\b", " ");
            s = Regex.Replace(s, @"(?i)\bepisode\s*\d{1,3}\b", " ");

            s = Regex.Replace(s, @"(?i)\b(480p|720p|1080p|1440p|2160p|4k|8k)\b", " ");
            s = Regex.Replace(s, @"(?i)\b(web\-?dl|webrip|web|hdrip|bdrip|bluray|blu\-?ray|dvdrip|hdtv|cam|ts|tc)\b", " ");
            s = Regex.Replace(s, @"(?i)\b(x264|x265|h264|h265|hevc|av1|10bit|8bit|hdr|dv|dolby|vision)\b", " ");
            s = Regex.Replace(s, @"(?i)\b(aac|ac3|eac3|ddp|dts|truehd|atmos)\b", " ");
            s = Regex.Replace(s, @"(?i)\b(remux|proper|repack|extended|uncut|limited|criterion)\b", " ");
            s = Regex.Replace(s, @"(?i)\b(multi|dual\s*audio|dubbed|subbed)\b", " ");
            s = Regex.Replace(s, @"(?i)\b(\d{3,4}MB|\d{1,3}\.\d{1,2}GB|\d{1,3}GB)\b", " ");

            s = Regex.Replace(s, @"[^\p{L}\p{Nd}\s']", " ");
            s = Regex.Replace(s, @"\s+", " ").Trim();

            if (!string.IsNullOrWhiteSpace(year) && !Regex.IsMatch(s, @"\b(19|20)\d{2}\b"))
                s = $"{s} {year}".Trim();

            if (preferTv)
            {
                s = Regex.Replace(s, @"(?i)\b(complete|pack)\b", " ");
                s = Regex.Replace(s, @"\s+", " ").Trim();
            }

            return s;
        }

        private static void ApplyTraktTrendSignals(
            List<EnrichedMediaObject> items,
            List<TraktClient.TraktTrendingItem> trendingMovies,
            List<TraktClient.TraktTrendingItem> trendingShows)
        {
            if (items == null || items.Count == 0) return;

            var byTmdbMovie = trendingMovies
                .Where(t => t.TmdbId != null && t.TmdbId.Value > 0)
                .ToDictionary(t => t.TmdbId!.Value, t => t.Watchers, EqualityComparer<int>.Default);

            var byTmdbShow = trendingShows
                .Where(t => t.TmdbId != null && t.TmdbId.Value > 0)
                .ToDictionary(t => t.TmdbId!.Value, t => t.Watchers, EqualityComparer<int>.Default);

            foreach (var i in items)
            {
                if (i == null) continue;
                if (i.TmdbId == null || i.TmdbId.Value <= 0) continue;

                var watchers = 0;
                if (i.Kind == MediaKind.Series)
                    byTmdbShow.TryGetValue(i.TmdbId.Value, out watchers);
                else
                    byTmdbMovie.TryGetValue(i.TmdbId.Value, out watchers);

                if (watchers > 0)
                    i.Ratings.TrendRaw = watchers;

                // Trakt rating isn't available from trending payload; keep 0.
            }
        }

        private static List<EnrichedMediaObject> MergeAndDedupe(int cap, params IEnumerable<EnrichedMediaObject>[] lists)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var result = new List<EnrichedMediaObject>();

            foreach (var list in lists)
            {
                foreach (var item in list ?? Array.Empty<EnrichedMediaObject>())
                {
                    if (item == null) continue;
                    item.Id = MediaScoring.BuildStableId(item.Kind, item.TmdbId, item.ImdbId, item.Title, item.ReleaseDate);
                    if (string.IsNullOrWhiteSpace(item.Id)) continue;

                    if (!seen.Add(item.Id))
                    {
                        // Merge additive fields (network sources + history sources).
                        var existing = result.FirstOrDefault(x => string.Equals(x.Id, item.Id, StringComparison.OrdinalIgnoreCase));
                        if (existing != null)
                        {
                            if (string.IsNullOrWhiteSpace(existing.Overview) && !string.IsNullOrWhiteSpace(item.Overview)) existing.Overview = item.Overview;
                            if (existing.RuntimeMinutes <= 0 && item.RuntimeMinutes > 0) existing.RuntimeMinutes = item.RuntimeMinutes;
                            if (existing.ReleaseDate == null && item.ReleaseDate != null) existing.ReleaseDate = item.ReleaseDate;
                            if (string.IsNullOrWhiteSpace(existing.Poster) && !string.IsNullOrWhiteSpace(item.Poster)) existing.Poster = item.Poster;
                            if (string.IsNullOrWhiteSpace(existing.Backdrop) && !string.IsNullOrWhiteSpace(item.Backdrop)) existing.Backdrop = item.Backdrop;
                            if (string.IsNullOrWhiteSpace(existing.Trailer) && !string.IsNullOrWhiteSpace(item.Trailer)) existing.Trailer = item.Trailer;
                            if (string.IsNullOrWhiteSpace(existing.PrimaryStreamSource) && !string.IsNullOrWhiteSpace(item.PrimaryStreamSource)) existing.PrimaryStreamSource = item.PrimaryStreamSource;

                            if (existing.Genres.Count == 0 && item.Genres.Count > 0) existing.Genres = item.Genres;
                            if (existing.Cast.Count == 0 && item.Cast.Count > 0) existing.Cast = item.Cast;
                            if (existing.Directors.Count == 0 && item.Directors.Count > 0) existing.Directors = item.Directors;
                            if (item.StreamingAvailability.Count > 0)
                            {
                                foreach (var kv in item.StreamingAvailability)
                                {
                                    if (!existing.StreamingAvailability.ContainsKey(kv.Key))
                                        existing.StreamingAvailability[kv.Key] = kv.Value;
                                }
                            }

                            // Signals
                            if (existing.Ratings.PopularityRaw <= 0 && item.Ratings.PopularityRaw > 0) existing.Ratings.PopularityRaw = item.Ratings.PopularityRaw;
                            if (existing.Ratings.TrendRaw <= 0 && item.Ratings.TrendRaw > 0) existing.Ratings.TrendRaw = item.Ratings.TrendRaw;
                            if (existing.Ratings.EngagementRaw <= 0 && item.Ratings.EngagementRaw > 0) existing.Ratings.EngagementRaw = item.Ratings.EngagementRaw;

                            // Prefer max affinity when any source indicates it.
                            if (item.PersonalAffinity > existing.PersonalAffinity) existing.PersonalAffinity = item.PersonalAffinity;
                            existing.IsForgottenContinue = existing.IsForgottenContinue || item.IsForgottenContinue;
                        }
                        continue;
                    }

                    result.Add(item);
                    if (result.Count >= cap) return result;
                }
            }

            return result;
        }

        private async Task EnrichTopItemsAsync(string tmdbApiKey, List<EnrichedMediaObject> items, string region, int maxItems, CancellationToken ct)
        {
            tmdbApiKey = (tmdbApiKey ?? "").Trim();

            var candidates = items
                .Where(i => i != null)
                .OrderByDescending(i => i.AiScore)
                .Take(Math.Max(1, maxItems))
                .ToList();

            var tasks = candidates.Select(async item =>
            {
                try
                {
                    ct.ThrowIfCancellationRequested();
                    if (!string.IsNullOrWhiteSpace(tmdbApiKey) && item.TmdbId != null && item.TmdbId.Value > 0)
                    {
                        var trailer = await _tmdb.TryGetTrailerUrlAsync(tmdbApiKey, item.Kind, item.TmdbId.Value, ct).ConfigureAwait(false);
                        if (!string.IsNullOrWhiteSpace(trailer)) item.Trailer = trailer;

                        var providers = await _tmdb.TryGetWatchProvidersAsync(tmdbApiKey, item.Kind, item.TmdbId.Value, region, ct).ConfigureAwait(false);
                        if (providers.Count > 0)
                        {
                            foreach (var kv in providers)
                                item.StreamingAvailability[kv.Key] = kv.Value;
                        }
                    }

                    if (string.IsNullOrWhiteSpace(item.PrimaryStreamSource))
                    {
                        var primaryStreamSource = await TryResolvePrimaryStreamSourceAsync(item, ct).ConfigureAwait(false);
                        if (!string.IsNullOrWhiteSpace(primaryStreamSource))
                        {
                            item.PrimaryStreamSource = primaryStreamSource;
                            item.StreamingAvailability["Addon Source"] = primaryStreamSource;
                        }
                    }
                }
                catch
                {
                }
            });

            await Task.WhenAll(tasks).ConfigureAwait(false);
        }

        private async Task<string> TryResolvePrimaryStreamSourceAsync(EnrichedMediaObject item, CancellationToken ct)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.Title))
                return "";

            var cacheKey = $"stream:{item.Kind}:{item.ImdbId}:{item.TmdbId}:{item.Title}";
            if (MediaCuratorCache.TryGet(cacheKey, out string cached))
                return cached;

            var resolved = "";
            try
            {
                var primary = !string.IsNullOrWhiteSpace(item.ImdbId)
                    ? item.ImdbId
                    : (!string.IsNullOrWhiteSpace(item.Id) ? item.Id : item.Title);

                var request = new MediaRequest(item.Title, item.Kind == MediaKind.Series ? "tv" : "movie", primary);
                var sources = await _streamResolver.GetSourcesAsync(request, ct).ConfigureAwait(false);
                var best = sources.FirstOrDefault(s => !s.IsInfoOnly && !string.IsNullOrWhiteSpace(s.UrlOrPath));
                if (best != null)
                    resolved = best.UrlOrPath;
            }
            catch
            {
            }
            MediaCuratorCache.Set(cacheKey, resolved, TimeSpan.FromHours(6));
            return resolved;
        }

        /// <summary>
        /// Searches TMDB for content matching the user's free-text query by extracting
        /// genre names, decade/year ranges, keywords, and franchises.  Falls back to
        /// TMDB multi-search when the query looks like a title.
        /// </summary>
        public async Task<List<EnrichedMediaObject>> DiscoverByQueryAsync(
            string tmdbApiKey,
            string userQuery,
            int limit,
            CancellationToken ct)
        {
            tmdbApiKey = (tmdbApiKey ?? "").Trim();
            userQuery = (userQuery ?? "").Trim();
            limit = Math.Max(10, Math.Min(120, limit));
            if (string.IsNullOrWhiteSpace(tmdbApiKey) || string.IsNullOrWhiteSpace(userQuery))
                return new List<EnrichedMediaObject>();

            var lower = userQuery.ToLowerInvariant();

            // ---- Genre extraction ----
            var tvGenreMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["action"] = 10759, ["adventure"] = 10759, ["animation"] = 16, ["anime"] = 16,
                ["comedy"] = 35, ["crime"] = 80, ["documentary"] = 99, ["drama"] = 18,
                ["family"] = 10751, ["kids"] = 10762, ["mystery"] = 9648,
                ["sci-fi"] = 10765, ["science fiction"] = 10765, ["scifi"] = 10765,
                ["fantasy"] = 10765, ["war"] = 10768, ["western"] = 37,
                ["horror"] = -1, ["thriller"] = -1, ["romance"] = -1,
            };

            var matchedMovieGenres = TmdbDiscoveryProvider.ResolveMovieGenresFromText(lower)
                .Select(match => match.id)
                .ToHashSet();
            var matchedTvGenres = new HashSet<int>();
            foreach (var kv in tvGenreMap)
            {
                if (lower.Contains(kv.Key) && kv.Value > 0)
                    matchedTvGenres.Add(kv.Value);
            }

            // ---- Year / decade extraction ----
            string yearGte = "", yearLte = "";
            var decadeMatch = Regex.Match(lower, @"\b(19[2-9]|20[0-2])0s\b");
            if (decadeMatch.Success)
            {
                var decadeStart = int.Parse(decadeMatch.Value.TrimEnd('s'));
                yearGte = $"{decadeStart}-01-01";
                yearLte = $"{decadeStart + 9}-12-31";
            }
            else
            {
                var yearRangeMatch = Regex.Match(lower, @"\b((?:19|20)\d{2})\s*(?:to|-)\s*((?:19|20)\d{2})\b");
                if (yearRangeMatch.Success)
                {
                    yearGte = $"{yearRangeMatch.Groups[1].Value}-01-01";
                    yearLte = $"{yearRangeMatch.Groups[2].Value}-12-31";
                }
                else
                {
                    var singleYear = Regex.Match(lower, @"\b((?:19|20)\d{2})\b");
                    if (singleYear.Success)
                    {
                        yearGte = $"{singleYear.Value}-01-01";
                        yearLte = $"{singleYear.Value}-12-31";
                    }
                }
            }

            // ---- Prefer movies vs series vs both ----
            bool wantMovies = lower.Contains("movie") || lower.Contains("film");
            bool wantSeries = lower.Contains("series") || lower.Contains("show") || lower.Contains("tv ");
            if (!wantMovies && !wantSeries)
            {
                wantMovies = true;
                wantSeries = true;
            }

            var tasks = new List<Task<List<EnrichedMediaObject>>>();

            // ---- Build TMDB discover endpoints ----
            if (matchedMovieGenres.Count > 0 || !string.IsNullOrWhiteSpace(yearGte))
            {
                if (wantMovies)
                {
                    var genrePart = matchedMovieGenres.Count > 0 ? $"&with_genres={string.Join(",", matchedMovieGenres)}" : "";
                    var datePart = "";
                    if (!string.IsNullOrWhiteSpace(yearGte)) datePart += $"&primary_release_date.gte={yearGte}";
                    if (!string.IsNullOrWhiteSpace(yearLte)) datePart += $"&primary_release_date.lte={yearLte}";
                    var endpoint = $"discover/movie?sort_by=popularity.desc&include_adult=false&vote_count.gte=50{genrePart}{datePart}";
                    tasks.Add(_tmdb.FetchListPagesAsync(tmdbApiKey, endpoint, MediaKind.Movie, 3, ct));
                }

                if (wantSeries && matchedTvGenres.Count > 0)
                {
                    var genrePart = $"&with_genres={string.Join(",", matchedTvGenres)}";
                    var datePart = "";
                    if (!string.IsNullOrWhiteSpace(yearGte)) datePart += $"&first_air_date.gte={yearGte}";
                    if (!string.IsNullOrWhiteSpace(yearLte)) datePart += $"&first_air_date.lte={yearLte}";
                    var endpoint = $"discover/tv?sort_by=popularity.desc&include_adult=false&vote_count.gte=30{genrePart}{datePart}";
                    tasks.Add(_tmdb.FetchListPagesAsync(tmdbApiKey, endpoint, MediaKind.Series, 3, ct));
                }
            }

            // ---- Keyword / franchise search via TMDB keyword API ----
            var franchisePatterns = new[] { "marvel", "dc", "star wars", "harry potter", "lord of the rings", "james bond", "pixar", "disney", "studio ghibli", "tarantino", "nolan", "spielberg" };
            string? detectedFranchise = null;
            foreach (var f in franchisePatterns)
            {
                if (lower.Contains(f))
                {
                    detectedFranchise = f;
                    break;
                }
            }

            if (detectedFranchise != null)
            {
                var kwId = await _tmdb.SearchKeywordIdAsync(tmdbApiKey, detectedFranchise, ct).ConfigureAwait(false);
                var coId = await _tmdb.SearchCompanyIdAsync(tmdbApiKey, detectedFranchise, ct).ConfigureAwait(false);

                if (kwId != null && kwId.Value > 0)
                {
                    if (wantMovies)
                        tasks.Add(_tmdb.FetchListPagesAsync(tmdbApiKey, $"discover/movie?sort_by=popularity.desc&include_adult=false&vote_count.gte=10&with_keywords={kwId.Value}", MediaKind.Movie, 3, ct));
                    if (wantSeries)
                        tasks.Add(_tmdb.FetchListPagesAsync(tmdbApiKey, $"discover/tv?sort_by=popularity.desc&include_adult=false&vote_count.gte=10&with_keywords={kwId.Value}", MediaKind.Series, 3, ct));
                }
                if (coId != null && coId.Value > 0)
                {
                    if (wantMovies)
                        tasks.Add(_tmdb.FetchListPagesAsync(tmdbApiKey, $"discover/movie?sort_by=popularity.desc&include_adult=false&vote_count.gte=10&with_companies={coId.Value}", MediaKind.Movie, 3, ct));
                    if (wantSeries)
                        tasks.Add(_tmdb.FetchListPagesAsync(tmdbApiKey, $"discover/tv?sort_by=popularity.desc&include_adult=false&vote_count.gte=10&with_companies={coId.Value}", MediaKind.Series, 3, ct));
                }
            }

            // ---- Fallback: TMDB search/movie + search/tv when no genre/keyword/franchise matched ----
            if (tasks.Count == 0)
            {
                var tmdbClient = new TmdbClient();
                var searchQuery = Regex.Replace(userQuery, @"\b(movie|movies|film|films|show|shows|series|tv|best|top|good|great|new|latest|recent|recommended|recommend)\b", " ", RegexOptions.IgnoreCase).Trim();
                searchQuery = Regex.Replace(searchQuery, @"\s+", " ").Trim();
                if (string.IsNullOrWhiteSpace(searchQuery))
                    searchQuery = userQuery;

                if (wantMovies)
                {
                    try
                    {
                        var movieHits = await tmdbClient.SearchMovieAsync(tmdbApiKey, searchQuery, ct).ConfigureAwait(false);
                        var converted = movieHits
                            .Where(h => h != null && h.Id > 0 && !string.IsNullOrWhiteSpace(h.Title))
                            .Select(h => new EnrichedMediaObject
                            {
                                Kind = MediaKind.Movie,
                                TmdbId = h.Id,
                                Title = h.Title ?? "",
                                Overview = h.Overview ?? "",
                                Poster = TmdbClient.BuildPosterUrl(h.PosterPath, "w500"),
                                Backdrop = TmdbClient.BuildBackdropUrl(h.BackdropPath, "w780"),
                                ReleaseDate = h.ReleaseDate,
                                Ratings = new MediaRatings(),
                            })
                            .ToList();
                        if (converted.Count > 0)
                            tasks.Add(Task.FromResult(converted));
                    }
                    catch { }
                }
                if (wantSeries)
                {
                    try
                    {
                        var tvHits = await tmdbClient.SearchTvAsync(tmdbApiKey, searchQuery, ct).ConfigureAwait(false);
                        var converted = tvHits
                            .Where(h => h != null && h.Id > 0 && !string.IsNullOrWhiteSpace(h.Name))
                            .Select(h => new EnrichedMediaObject
                            {
                                Kind = MediaKind.Series,
                                TmdbId = h.Id,
                                Title = h.Name ?? "",
                                Overview = h.Overview ?? "",
                                Poster = TmdbClient.BuildPosterUrl(h.PosterPath, "w500"),
                                Backdrop = TmdbClient.BuildBackdropUrl(h.BackdropPath, "w780"),
                                ReleaseDate = h.FirstAirDate,
                                Ratings = new MediaRatings(),
                            })
                            .ToList();
                        if (converted.Count > 0)
                            tasks.Add(Task.FromResult(converted));
                    }
                    catch { }
                }
            }

            if (tasks.Count == 0)
                return new List<EnrichedMediaObject>();

            await Task.WhenAll(tasks).ConfigureAwait(false);

            var all = MergeAndDedupe(500, tasks.Select(t => t.Result).ToArray());
            foreach (var item in all)
            {
                item.Id = MediaScoring.BuildStableId(item.Kind, item.TmdbId, item.ImdbId, item.Title, item.ReleaseDate);
                var scored = MediaScoring.ScoreRating(item, null, null);
                item.AiRating = scored.rating;
                item.AiScore = scored.rating.AiScore;
            }

            return DistinctByStableId(all)
                .OrderByDescending(i => i.AiScore)
                .ThenByDescending(i => i.Ratings?.PopularityRaw ?? 0)
                .ThenByDescending(i => i.Ratings?.Tmdb ?? 0)
                .Take(limit)
                .ToList();
        }
    }
}
