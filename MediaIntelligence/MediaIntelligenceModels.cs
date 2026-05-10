using System;
using System.Collections.Generic;

namespace AtlasAI.MediaIntelligence
{
    public enum QualityTier
    {
        Unknown = 0,
        Skip = 1,
        Meh = 2,
        Decent = 3,
        Good = 4,
        Great = 5,
        Masterpiece = 6
    }

    public sealed class AiRating
    {
        // All 0..100
        public double AiScore { get; set; }
        public double CriticScore { get; set; }
        public double AudienceScore { get; set; }
        public double TrendScore { get; set; }

        public QualityTier QualityTier { get; set; }
    }

    public enum MediaKind
    {
        Unknown = 0,
        Movie = 1,
        Series = 2
    }

    public sealed class MediaRatings
    {
        // All scores normalized to 0..10 when applicable.
        public double Critic { get; set; }
        public double Audience { get; set; }
        public double Tmdb { get; set; }
        public double Trakt { get; set; }

        // Non-10 scales (raw signals) exposed for transparency.
        public double PopularityRaw { get; set; }
        public double TrendRaw { get; set; }
        public double EngagementRaw { get; set; }

        public Dictionary<string, double> Signals { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    public sealed class EnrichedMediaObject
    {
        public string Id { get; set; } = ""; // stable dedupe id

        public MediaKind Kind { get; set; }
        public string OriginalLanguage { get; set; } = "";
        public int? TmdbId { get; set; }
        public string ImdbId { get; set; } = "";

        public string Title { get; set; } = "";
        public string Overview { get; set; } = "";
        public List<string> Genres { get; set; } = new();

        public int RuntimeMinutes { get; set; }
        public int VoteCount { get; set; }
        public DateTime? ReleaseDate { get; set; }
        public DateTime? LastInteractionUtc { get; set; }

        public MediaRatings Ratings { get; set; } = new();

        public AiRating AiRating { get; set; } = new();

        // 0..100
        public double AiScore { get; set; }

        // 0..1
        public double Confidence { get; set; }

        public string Backdrop { get; set; } = "";
        public string Poster { get; set; } = "";
        public string Trailer { get; set; } = "";
        public string PrimaryStreamSource { get; set; } = "";

        // Provider -> deeplink or label; keep it simple for UI.
        public Dictionary<string, string> StreamingAvailability { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        public List<string> Cast { get; set; } = new();
        public List<string> Directors { get; set; } = new();

        // Personalization hints
        public double PersonalAffinity { get; set; }
        public bool IsForgottenContinue { get; set; }
    }

    public sealed class MediaProfileCount
    {
        public string Name { get; set; } = "";
        public int Count { get; set; }
    }

    public sealed class MediaViewingPattern
    {
        public string Key { get; set; } = "";
        public string Description { get; set; } = "";
        public int Count { get; set; }
    }

    public sealed class MediaUserBehaviorProfile
    {
        public string RecentTitle { get; set; } = "";
        public List<string> RecentGenres { get; set; } = new();
        public List<string> TopGenres { get; set; } = new();
        public List<MediaProfileCount> TopActors { get; set; } = new();
        public List<MediaProfileCount> TopDirectors { get; set; } = new();
        public List<MediaViewingPattern> RecentViewingPatterns { get; set; } = new();
        public List<string> UnfinishedTitles { get; set; } = new();
        public bool IsBingeWatcher { get; set; }
        public List<string> BingeTitles { get; set; } = new();
    }

    public sealed class MediaSection
    {
        public string Key { get; set; } = "";
        public string Title { get; set; } = "";
        public List<EnrichedMediaObject> Items { get; set; } = new();
    }

    public sealed class MediaIntelligenceResult
    {
        public string Mood { get; set; } = "";
        public MediaUserBehaviorProfile UserProfile { get; set; } = new();
        public List<MediaSection> Sections { get; set; } = new();
    }
}
