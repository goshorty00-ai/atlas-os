// Media Centre UI Models
// These models are specifically for the Media Centre dashboard UI
// They extend the base MediaItem with UI-specific properties

using System;

namespace AtlasAI.MediaScanner
{
    /// <summary>
    /// UI model for media cards in the Media Centre dashboard
    /// </summary>
    public class MediaCardItem
    {
        public string Title { get; set; } = "";
        public int Year { get; set; }
        public double Rating { get; set; }
        public string Genre { get; set; } = "";
        public string Metadata { get; set; } = "";
        public double Progress { get; set; }
        public string ImageUrl { get; set; } = "";
        public MediaType MediaType { get; set; }
        
        // Still Watching / Extended fields
        public DateTime? LastWatched { get; set; }
        public int? SeasonNumber { get; set; }
        public int? EpisodeNumber { get; set; }
        public string RuntimeRemaining { get; set; } = "";
        public bool IsRecent { get; set; }

        // Optional: Reference to underlying MediaItem
        public MediaItem? SourceItem { get; set; }
    }
}
