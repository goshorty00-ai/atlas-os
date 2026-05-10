using System;
using System.Collections.Generic;
using System.IO;

namespace AtlasAI.MediaScanner
{
    /// <summary>
    /// Media item data model - represents a single media file
    /// </summary>
    public class MediaItem
    {
        public string FilePath { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string Extension { get; set; } = "";
        public long FileSize { get; set; }
        public DateTime LastModified { get; set; }
        public MediaType MediaType { get; set; }
        public string SectionName { get; set; } = "";
        public string FolderPath { get; set; } = "";
        public DateTime DateAdded { get; set; }

        // Audio-specific metadata
        public string? Album { get; set; }
        public string? Artist { get; set; }
        public int? TrackNumber { get; set; }
        public TimeSpan? Duration { get; set; }

        // Video-specific metadata
        public string? Resolution { get; set; }
        public int? Width { get; set; }
        public int? Height { get; set; }

        // Image-specific metadata
        public DateTime? DateTaken { get; set; }
        public string? CameraModel { get; set; }

        // Computed properties
        public string FileName => Path.GetFileName(FilePath);
        public string FolderName => Path.GetFileName(FolderPath);
        public string FileSizeFormatted => FormatFileSize(FileSize);

        // Extended properties for Media Centre (Bindable via ViewModel)
        private string? _title;
        public string? Title { get => _title; set => _title = value; }

        private int _year;
        public int Year { get => _year; set => _year = value; }

        private double _rating;
        public double Rating { get => _rating; set => _rating = value; }

        private List<string> _genres = new();
        public List<string> Genres { get => _genres; set => _genres = value; }

        private string? _coverUrl;
        public string? CoverUrl { get => _coverUrl; set => _coverUrl = value; }

        private string? _backdropUrl;
        public string? BackdropUrl { get => _backdropUrl; set => _backdropUrl = value; }

        private string? _logoUrl;
        public string? LogoUrl { get => _logoUrl; set => _logoUrl = value; }

        private Dictionary<string, string> _rpdbRatings = new();
        public Dictionary<string, string> RpdbRatings { get => _rpdbRatings; set => _rpdbRatings = value; }

        private string? _type;
        public string? Type { get => _type; set => _type = value; }

        // Reference to underlying playback item if needed
        public MediaItem? PlaybackItem { get; set; }

        private static string FormatFileSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }
    }

    /// <summary>
    /// Album information - groups audio files into albums (MANDATORY for music)
    /// </summary>
    public class AlbumInfo
    {
        public string AlbumTitle { get; set; } = "";
        public string Artist { get; set; } = "";
        public int TrackCount { get; set; }
        public TimeSpan TotalDuration { get; set; }
        public string? CoverArtPath { get; set; }
        public string SourceFolderPath { get; set; } = "";
        public List<MediaItem> Tracks { get; set; } = new();
        public DateTime DateAdded { get; set; } = DateTime.UtcNow;

        // Computed properties
        public string DurationFormatted => FormatDuration(TotalDuration);
        public string AlbumKey => $"{Artist}|{AlbumTitle}";
        public bool HasCoverArt => !string.IsNullOrEmpty(CoverArtPath) && File.Exists(CoverArtPath);

        private static string FormatDuration(TimeSpan duration)
        {
            if (duration.TotalHours >= 1)
                return $"{(int)duration.TotalHours}h {duration.Minutes}m";
            else
                return $"{duration.Minutes}m {duration.Seconds}s";
        }
    }

    /// <summary>
    /// Media type enumeration
    /// </summary>
    public enum MediaType
    {
        Unknown = 0,
        Video = 1,
        Audio = 2,
        Image = 3
    }

    /// <summary>
    /// Scan result for a complete media scan operation
    /// </summary>
    public class ScanResult
    {
        public bool Success { get; set; }
        public int FilesScanned { get; set; }
        public int Errors { get; set; }
        public string Message { get; set; } = "";
        public TimeSpan Duration { get; set; }
        public DateTime CompletedAt { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Scan result for a specific section (Movies, Music, etc.)
    /// </summary>
    public class SectionScanResult
    {
        public string SectionName { get; set; } = "";
        public List<MediaItem> MediaItems { get; set; } = new();
        public List<AlbumInfo> Albums { get; set; } = new();
        public int FilesFound { get; set; }
        public int Errors { get; set; }
    }

    /// <summary>
    /// Event args for scan progress updates
    /// </summary>
    public class ScanProgressEventArgs : EventArgs
    {
        public string SectionName { get; set; } = "";
        public int FilesProcessed { get; set; }
        public string CurrentFile { get; set; } = "";
        public int TotalFiles { get; set; }
        public double ProgressPercentage => TotalFiles > 0 ? (double)FilesProcessed / TotalFiles * 100 : 0;
    }

    /// <summary>
    /// Event args for scan completion
    /// </summary>
    public class ScanCompletedEventArgs : EventArgs
    {
        public ScanResult Result { get; }

        public ScanCompletedEventArgs(ScanResult result)
        {
            Result = result;
        }
    }
}