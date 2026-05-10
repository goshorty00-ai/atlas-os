using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace AtlasAI.MediaIntelligence
{
    internal static class MediaScoring
    {
        public static double Clamp(double value, double min, double max)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) return min;
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        public static double NormalizeToTen(double raw)
        {
            if (double.IsNaN(raw) || double.IsInfinity(raw)) return 0;
            var v = Math.Abs(raw);
            if (v <= 0) return 0;
            if (v > 10 && v <= 100) v = v / 10.0;
            else if (v > 0 && v < 1.0) v = v * 10.0;
            if (v > 10) v = 10;
            return Math.Round(v, 1, MidpointRounding.AwayFromZero);
        }

        public static double NormalizeToHundred(double raw)
        {
            if (double.IsNaN(raw) || double.IsInfinity(raw)) return 0;
            var v = Math.Abs(raw);
            if (v <= 0) return 0;

            if (v <= 1.0) v = v * 100.0;
            else if (v <= 10.0) v = v * 10.0;
            else if (v > 100.0) v = 100.0;

            return Math.Round(Clamp(v, 0, 100), 1, MidpointRounding.AwayFromZero);
        }

        public static string NormalizeTitleKey(string? title)
        {
            var t = (title ?? "").Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(t)) return "";
            // strip punctuation-ish to improve dedupe
            var chars = t.Where(ch => char.IsLetterOrDigit(ch) || char.IsWhiteSpace(ch)).ToArray();
            var cleaned = new string(chars);
            cleaned = string.Join(' ', cleaned.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
            return cleaned;
        }

        public static double RecencyScore(DateTime? releaseDateUtc)
        {
            if (releaseDateUtc == null) return 0;
            var days = (DateTime.UtcNow.Date - releaseDateUtc.Value.ToUniversalTime().Date).TotalDays;
            if (double.IsNaN(days) || double.IsInfinity(days)) return 0;

            // Newest first; within 2 weeks is max.
            if (days <= 14) return 10;
            if (days <= 60) return 10 - ((days - 14) / (60 - 14)) * 4.0; // 10..6
            if (days <= 180) return 6 - ((days - 60) / (180 - 60)) * 3.0; // 6..3
            if (days <= 365 * 5) return 3 - ((days - 180) / ((365 * 5) - 180)) * 2.0; // 3..1
            return 1;
        }

        public static double PopularityScore(double popularityRaw, double? batchMaxPopularity)
        {
            if (popularityRaw <= 0) return 0;

            // Prefer relative scaling within the fetched batch when possible.
            if (batchMaxPopularity != null && batchMaxPopularity.Value > 0)
            {
                var p = Clamp(popularityRaw / batchMaxPopularity.Value, 0, 1);
                return Math.Round(p * 10.0, 1, MidpointRounding.AwayFromZero);
            }

            // Fallback: log scaling (TMDB popularity tends to be heavy-tailed).
            var score = Math.Log10(1 + popularityRaw) / 2.0 * 10.0; // rough
            return Math.Round(Clamp(score, 0, 10), 1, MidpointRounding.AwayFromZero);
        }

        public static double TrendScore(double trendRaw, double? batchMaxTrend)
        {
            if (trendRaw <= 0) return 0;
            if (batchMaxTrend != null && batchMaxTrend.Value > 0)
            {
                var p = Clamp(trendRaw / batchMaxTrend.Value, 0, 1);
                return Math.Round(p * 10.0, 1, MidpointRounding.AwayFromZero);
            }
            var score = Math.Log10(1 + trendRaw) / 2.0 * 10.0;
            return Math.Round(Clamp(score, 0, 10), 1, MidpointRounding.AwayFromZero);
        }

        public static double PreferenceScore(IReadOnlyList<string> itemGenres, HashSet<string> favoriteGenres, string mood)
        {
            if (itemGenres == null || itemGenres.Count == 0) return 0;
            if (favoriteGenres == null || favoriteGenres.Count == 0) return 0;

            var match = itemGenres.Count(g => favoriteGenres.Contains((g ?? "").Trim(), StringComparer.OrdinalIgnoreCase));
            var baseScore = Clamp((match / (double)itemGenres.Count) * 10.0, 0, 10);

            // Mood nudges: very simple.
            var m = (mood ?? "").Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(m)) return Math.Round(baseScore, 1, MidpointRounding.AwayFromZero);

            bool Has(params string[] g) => itemGenres.Any(x => g.Any(y => string.Equals((x ?? "").Trim(), y, StringComparison.OrdinalIgnoreCase)));

            if (m == "chill" && (Has("Comedy", "Animation") || Has("Family"))) baseScore += 0.8;
            if (m == "focus" && Has("Documentary")) baseScore += 1.0;
            if (m == "hype" && (Has("Action", "Adventure") || Has("Sci-Fi"))) baseScore += 0.8;
            if (m == "dark" && (Has("Thriller", "Horror", "Crime") || Has("Mystery"))) baseScore += 0.8;

            return Math.Round(Clamp(baseScore, 0, 10), 1, MidpointRounding.AwayFromZero);
        }

        public static (AiRating rating, double confidence0To1) ScoreRating(EnrichedMediaObject item, double? batchMaxPopularity, double? batchMaxTrend)
        {
            var r = item?.Ratings ?? new MediaRatings();
            var signals = r.Signals ?? new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

            double? TryGetSignal(params string[] keys)
            {
                if (signals == null || signals.Count == 0) return null;
                foreach (var k in keys)
                {
                    if (string.IsNullOrWhiteSpace(k)) continue;
                    if (signals.TryGetValue(k.Trim(), out var v) && v > 0)
                        return v;
                }
                return null;
            }

            // Critic score (0..100)
            var criticCandidates = new List<(double score, double weight)>();
            var rtCritic = TryGetSignal("rt", "rottentomatoes", "tomatoes", "tomatometer", "rt_critic");
            if (rtCritic != null) criticCandidates.Add((NormalizeToHundred(rtCritic.Value), 1.0));
            var metacritic = TryGetSignal("metacritic", "mc", "meta");
            if (metacritic != null) criticCandidates.Add((NormalizeToHundred(metacritic.Value), 0.9));
            if (r.Critic > 0) criticCandidates.Add((NormalizeToHundred(r.Critic), 0.6));
            if (r.Tmdb > 0) criticCandidates.Add((NormalizeToHundred(r.Tmdb), 0.45));

            var criticScore = WeightedAverage(criticCandidates);
            if (criticScore <= 0) criticScore = NormalizeToHundred(r.Tmdb);

            // Audience score (0..100)
            var audienceCandidates = new List<(double score, double weight)>();
            var rtAudience = TryGetSignal("rt_audience", "tomatoes_user", "tomatoesuser", "rt_user");
            if (rtAudience != null) audienceCandidates.Add((NormalizeToHundred(rtAudience.Value), 1.0));
            var imdb = TryGetSignal("imdb", "imdb_rating", "imdbscore");
            if (imdb != null) audienceCandidates.Add((NormalizeToHundred(imdb.Value), 0.95));
            if (r.Trakt > 0) audienceCandidates.Add((NormalizeToHundred(r.Trakt), 0.65));
            if (r.Audience > 0) audienceCandidates.Add((NormalizeToHundred(r.Audience), 0.55));
            if (r.Tmdb > 0) audienceCandidates.Add((NormalizeToHundred(r.Tmdb), 0.45));

            var audienceScore = WeightedAverage(audienceCandidates);
            if (audienceScore <= 0) audienceScore = NormalizeToHundred(r.Tmdb);

            // Trend score (0..100)
            var pop10 = PopularityScore(r.PopularityRaw, batchMaxPopularity);
            var trend10 = TrendScore(r.TrendRaw, batchMaxTrend);
            var recency10 = RecencyScore(item?.ReleaseDate);
            var engagement10 = Clamp((r.EngagementRaw <= 0 ? 0 : r.EngagementRaw / 10.0), 0, 10);

            var trendScore10 =
                pop10 * 0.40 +
                trend10 * 0.40 +
                recency10 * 0.10 +
                engagement10 * 0.10;
            var trendScore = Clamp(trendScore10, 0, 10) * 10.0;

            // Base AI score: normalized, source-agnostic.
            var ai = criticScore * 0.45 + audienceScore * 0.35 + trendScore * 0.20;

            // Genre quality weighting (small nudge).
            ai += GenreQualityAdjustment(item?.Genres);

            // Overhyped detection: high trend, weak quality.
            if (trendScore >= 75 && criticScore < 60 && audienceScore < 65)
            {
                var hypePenalty = (trendScore - 75) * 0.30 + (65 - Math.Max(criticScore, audienceScore)) * 0.20;
                ai -= Clamp(hypePenalty, 2, 12);
            }

            // Critically acclaimed but low popularity/trend boost.
            if (criticScore >= 82 && trendScore <= 35)
            {
                var boost = (criticScore - 82) * 0.20 + (35 - trendScore) * 0.15;
                ai += Clamp(boost, 2, 10);
            }

            // Poorly reviewed sequels penalty.
            if (IsLikelySequel(item?.Title) && criticScore < 58 && audienceScore < 62)
                ai -= 6;

            // Personal affinity (0..10) as a gentle nudge.
            var pref10 = Clamp(item?.PersonalAffinity ?? 0, 0, 10);
            ai += pref10 * 0.4; // up to +4

            ai = Clamp(ai, 0, 100);

            var rating = new AiRating
            {
                AiScore = Math.Round(ai, 1, MidpointRounding.AwayFromZero),
                CriticScore = Math.Round(Clamp(criticScore, 0, 100), 1, MidpointRounding.AwayFromZero),
                AudienceScore = Math.Round(Clamp(audienceScore, 0, 100), 1, MidpointRounding.AwayFromZero),
                TrendScore = Math.Round(Clamp(trendScore, 0, 100), 1, MidpointRounding.AwayFromZero),
                QualityTier = ToQualityTier(ai)
            };

            // Confidence: more distinct signals and sources => higher.
            var signalCount = 0;
            if (r.Tmdb > 0) signalCount++;
            if (r.Trakt > 0) signalCount++;
            if (rtCritic != null || metacritic != null) signalCount++;
            if (rtAudience != null || imdb != null) signalCount++;
            if (r.PopularityRaw > 0) signalCount++;
            if (r.TrendRaw > 0) signalCount++;
            if (r.EngagementRaw > 0) signalCount++;
            if (item?.ReleaseDate != null) signalCount++;
            if (item?.Genres?.Count > 0) signalCount++;

            var conf = Clamp(signalCount / 9.0, 0.2, 1.0);
            return (rating, Math.Round(conf, 2, MidpointRounding.AwayFromZero));
        }

        public static (double aiScore0To100, double confidence0To1) Score(EnrichedMediaObject item, double? batchMaxPopularity, double? batchMaxTrend)
        {
            var scored = ScoreRating(item, batchMaxPopularity, batchMaxTrend);
            if (item != null)
            {
                item.AiRating = scored.rating;
                item.AiScore = scored.rating.AiScore;
                item.Confidence = scored.confidence0To1;
            }
            return (scored.rating.AiScore, scored.confidence0To1);
        }

        private static double WeightedAverage(List<(double score, double weight)> candidates)
        {
            if (candidates == null || candidates.Count == 0) return 0;
            var sumW = candidates.Where(c => c.score > 0 && c.weight > 0).Sum(c => c.weight);
            if (sumW <= 0) return 0;
            var sum = candidates.Where(c => c.score > 0 && c.weight > 0).Sum(c => c.score * c.weight);
            return Clamp(sum / sumW, 0, 100);
        }

        private static QualityTier ToQualityTier(double aiScore0To100)
        {
            var s = Clamp(aiScore0To100, 0, 100);
            if (s >= 90) return QualityTier.Masterpiece;
            if (s >= 80) return QualityTier.Great;
            if (s >= 70) return QualityTier.Good;
            if (s >= 60) return QualityTier.Decent;
            if (s >= 50) return QualityTier.Meh;
            return QualityTier.Skip;
        }

        private static double GenreQualityAdjustment(IReadOnlyList<string>? genres)
        {
            if (genres == null || genres.Count == 0) return 0;

            // Keep adjustments small and bounded.
            var weights = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                ["Documentary"] = 2.5,
                ["Drama"] = 1.2,
                ["History"] = 1.0,
                ["War"] = 1.0,
                ["Animation"] = 0.8,
                ["Crime"] = 0.5,
                ["Mystery"] = 0.4,
                ["Sci-Fi"] = 0.2,
                ["Fantasy"] = 0.2,
                ["Horror"] = -0.2,
                ["Reality"] = -1.2,
                ["TV Movie"] = -0.4,
            };

            var acc = 0d;
            var count = 0;
            foreach (var g in genres)
            {
                var key = (g ?? "").Trim();
                if (string.IsNullOrWhiteSpace(key)) continue;
                count++;
                if (weights.TryGetValue(key, out var w))
                    acc += w;
            }

            if (count <= 0) return 0;
            var avg = acc / Math.Sqrt(count);
            return Clamp(avg, -3.0, 3.0);
        }

        private static bool IsLikelySequel(string? title)
        {
            var t = (title ?? "").Trim();
            if (string.IsNullOrWhiteSpace(t)) return false;

            // Detect common sequel markers; avoid matching years like 1984/2001.
            if (Regex.IsMatch(t, @"(?i)\b(part|chapter)\s*\d{1,2}\b")) return true;
            if (Regex.IsMatch(t, @"(?i)\b(ii|iii|iv|v|vi|vii|viii|ix|x)\b")) return true;

            var m = Regex.Match(t, @"(?i)(?:\s|:|-)\s*(\d{1,2})\s*$");
            if (m.Success && int.TryParse(m.Groups[1].Value, out var n) && n >= 2 && n <= 12)
                return true;

            return false;
        }

        public static string DetectMood(string? userText)
        {
            var t = (userText ?? "").Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(t))
            {
                var hour = DateTime.Now.Hour;
                if (hour >= 22 || hour <= 2) return "dark";
                if (hour >= 6 && hour <= 10) return "focus";
                return "chill";
            }

            bool Has(params string[] terms) => terms.Any(term => t.Contains(term));

            if (Has("stressed", "tired", "relax", "calm", "easy")) return "chill";
            if (Has("work", "study", "focus", "concentrate")) return "focus";
            if (Has("excited", "hype", "pumped", "action")) return "hype";
            if (Has("scared", "dark", "thriller", "horror")) return "dark";

            return "chill";
        }

        public static string BuildStableId(MediaKind kind, int? tmdbId, string imdbId, string title, DateTime? releaseDate)
        {
            if (!string.IsNullOrWhiteSpace(imdbId))
                return imdbId.Trim().ToLowerInvariant();
            if (tmdbId != null && tmdbId.Value > 0)
                return $"tmdb:{kind.ToString().ToLowerInvariant()}:{tmdbId.Value}";

            var year = releaseDate?.Year ?? 0;
            var key = NormalizeTitleKey(title);
            if (year > 0) return $"title:{key}:{year}";
            return $"title:{key}";
        }
    }
}
