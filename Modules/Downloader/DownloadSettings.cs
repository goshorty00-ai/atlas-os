using System.Text.Json.Serialization;
using AtlasAI.Modules.Downloader.Resolvers;

namespace AtlasAI.Modules.Downloader
{
    public class DownloadSettings
    {
        [JsonPropertyName("maxParallelDownloads")]
        public int MaxParallelDownloads { get; set; } = 3;

        [JsonPropertyName("resolverMode")]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public ResolverMode ResolverMode { get; set; } = ResolverMode.Auto;

        [JsonPropertyName("providers")]
        public ProviderSettings Providers { get; set; } = new ProviderSettings();
    }

    public class ProviderSettings
    {
        [JsonPropertyName("realDebrid")]
        public ProviderConfig RealDebrid { get; set; } = new ProviderConfig { Enabled = true };

        [JsonPropertyName("allDebrid")]
        public ProviderConfig AllDebrid { get; set; } = new ProviderConfig { Enabled = false };

        [JsonPropertyName("premiumize")]
        public ProviderConfig Premiumize { get; set; } = new ProviderConfig { Enabled = false };
    }

    public class ProviderConfig
    {
        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; } = false;

        [JsonPropertyName("token")]
        public string? Token { get; set; }
    }
}

