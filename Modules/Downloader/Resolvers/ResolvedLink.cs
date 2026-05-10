using System;
using System.Collections.Generic;

namespace AtlasAI.Modules.Downloader.Resolvers
{
    public class ResolvedLink
    {
        public Uri DirectUrl { get; set; } = new Uri("about:blank");
        public string ResolverName { get; set; } = "";
        public string? SuggestedFilename { get; set; }
        public Dictionary<string, string>? Headers { get; set; }
        public string? ArtworkUrl { get; set; }
        public string? TrackTitle { get; set; }
        public string? TrackArtists { get; set; }
        public string? Album { get; set; }
        public int Year { get; set; }
    }
}
