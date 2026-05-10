using System;
using System.Text.Json.Serialization;

namespace AtlasAI.Modules.Downloader
{
    public class DownloadJob
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = Guid.NewGuid().ToString("n");

        [JsonPropertyName("url")]
        public string Url { get; set; } = "";

        [JsonPropertyName("provider")]
        public string Provider { get; set; } = "Auto";

        [JsonPropertyName("resolver")]
        public string? Resolver { get; set; }

        [JsonPropertyName("resolvedUrl")]
        public string? ResolvedUrl { get; set; }

        [JsonPropertyName("filename")]
        public string? Filename { get; set; }

        [JsonPropertyName("outputPath")]
        public string? OutputPath { get; set; }

        [JsonPropertyName("status")]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public DownloadStatus Status { get; set; } = DownloadStatus.Queued;

        [JsonPropertyName("progress")]
        public double Progress { get; set; }

        [JsonPropertyName("bytesDownloaded")]
        public long BytesDownloaded { get; set; }

        [JsonPropertyName("totalBytes")]
        public long TotalBytes { get; set; }

        [JsonPropertyName("speedBps")]
        public double SpeedBps { get; set; }

        [JsonPropertyName("etaSeconds")]
        public double EtaSeconds { get; set; }

        [JsonPropertyName("error")]
        public string? Error { get; set; }

        [JsonPropertyName("createdUtc")]
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        [JsonPropertyName("nextAttemptUtc")]
        public DateTime? NextAttemptUtc { get; set; }

        [JsonPropertyName("transcodeToMp3")]
        public bool TranscodeToMp3 { get; set; }

        [JsonPropertyName("downloadExtension")]
        public string? DownloadExtension { get; set; }

        [JsonPropertyName("metaTitle")]
        public string? MetaTitle { get; set; }

        [JsonPropertyName("metaArtists")]
        public string? MetaArtists { get; set; }

        [JsonPropertyName("metaAlbum")]
        public string? MetaAlbum { get; set; }

        [JsonPropertyName("metaYear")]
        public int MetaYear { get; set; }

        [JsonPropertyName("metaTrackNumber")]
        public int MetaTrackNumber { get; set; }

        [JsonPropertyName("metaArtworkUrl")]
        public string? MetaArtworkUrl { get; set; }

        [JsonPropertyName("outputFolder")]
        public string? OutputFolder { get; set; }

        [JsonPropertyName("convertToExtension")]
        public string? ConvertToExtension { get; set; }

        [JsonIgnore]
        internal DateTime LastProgressUtc { get; set; } = DateTime.UtcNow;

        [JsonIgnore]
        internal double SmoothedSpeedBps { get; set; }

        [JsonIgnore]
        internal int Attempts { get; set; }

        [JsonIgnore]
        internal bool RequestStop { get; set; }

        [JsonIgnore]
        internal bool InFlight { get; set; }
    }
}
