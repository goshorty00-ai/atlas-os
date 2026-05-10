using System.Collections.Generic;

namespace AtlasAI.MediaMetadata
{
    public sealed class MusicAlbumMetadata
    {
        public string Provider { get; set; } = "";
        public string ReleaseId { get; set; } = "";
        public string ReleaseUrl { get; set; } = "";
        public string ReleaseDate { get; set; } = "";
        public string Country { get; set; } = "";
        public string Label { get; set; } = "";
        public string Barcode { get; set; } = "";
        public string Status { get; set; } = "";
        public string Packaging { get; set; } = "";
        public int Year { get; set; }
        public List<string> Genres { get; set; } = new();
        public List<Views.ViewModels.AlbumDetailTrack> Tracks { get; set; } = new();
    }

    public sealed class MusicAlbumQuery
    {
        public string Artist { get; set; } = "";
        public string Album { get; set; } = "";
        public int? ExpectedTrackCount { get; set; }
        public int? ExpectedYear { get; set; }
    }
}
