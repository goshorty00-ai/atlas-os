using System;
using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Linq;
using System.Threading.Tasks;

namespace AtlasAI.Integrations
{
    public sealed class AddonManifest
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = "";

        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("version")]
        public string Version { get; set; } = "";

        [JsonPropertyName("description")]
        public string Description { get; set; } = "";

        [JsonPropertyName("logo")]
        public string Logo { get; set; } = "";

        [JsonPropertyName("background")]
        public string Background { get; set; } = "";

        [JsonPropertyName("resources")]
        public string[] Resources { get; set; } = Array.Empty<string>();

        [JsonPropertyName("types")]
        public string[] Types { get; set; } = Array.Empty<string>();

        [JsonPropertyName("catalogs")]
        public object[] Catalogs { get; set; } = Array.Empty<object>();
    }

    public sealed class AddonManifestService
    {
        private static readonly HttpClient _httpClient = new()
        {
            Timeout = TimeSpan.FromSeconds(10)
        };

        public async Task<(bool Success, AddonManifest? Manifest, string Error)> FetchManifestAsync(string baseUrl)
        {
            try
            {
                if (AtlasManagedAddonCatalog.TryCreateManifest(baseUrl, out var managedManifest))
                {
                    Debug.WriteLine($"[AddonManifest] Resolved Atlas managed addon manifest: {managedManifest.Name} v{managedManifest.Version}");
                    return (true, managedManifest, "");
                }

                if (!baseUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                    !baseUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    baseUrl = "https://" + baseUrl;
                }

                var manifestUrl = baseUrl.TrimEnd('/') + "/manifest.json";
                Debug.WriteLine($"[AddonManifest] Fetching manifest from: {manifestUrl}");

                var response = await _httpClient.GetAsync(manifestUrl);
                if (!response.IsSuccessStatusCode)
                {
                    return (false, null, $"HTTP {(int)response.StatusCode}: {response.ReasonPhrase}");
                }

                var json = await response.Content.ReadAsStringAsync();
                var manifest = ParseManifest(json);

                if (manifest == null)
                {
                    return (false, null, "Failed to parse manifest JSON");
                }

                if (string.IsNullOrWhiteSpace(manifest.Name))
                {
                    return (false, null, "Invalid manifest: missing 'name' field");
                }

                Debug.WriteLine($"[AddonManifest] Successfully fetched manifest: {manifest.Name} v{manifest.Version}");
                return (true, manifest, "");
            }
            catch (TaskCanceledException)
            {
                Debug.WriteLine($"[AddonManifest] Request timeout for: {baseUrl}");
                return (false, null, "Request timed out (10s)");
            }
            catch (HttpRequestException ex)
            {
                Debug.WriteLine($"[AddonManifest] Network error: {ex.Message}");
                return (false, null, $"Network error: {ex.Message}");
            }
            catch (JsonException ex)
            {
                Debug.WriteLine($"[AddonManifest] JSON parse error: {ex.Message}");
                return (false, null, $"Invalid JSON: {ex.Message}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AddonManifest] Unexpected error: {ex.Message}");
                return (false, null, $"Error: {ex.Message}");
            }
        }

        private static AddonManifest? ParseManifest(string json)
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return null;

            var root = document.RootElement;
            var manifest = new AddonManifest
            {
                Id = ReadString(root, "id"),
                Name = ReadString(root, "name"),
                Version = ReadString(root, "version"),
                Description = ReadString(root, "description"),
                Logo = ReadString(root, "logo"),
                Background = ReadString(root, "background"),
                Resources = ReadFlexibleStringArray(root, "resources"),
                Types = ReadFlexibleStringArray(root, "types"),
                Catalogs = ReadCatalogs(root, "catalogs"),
            };

            return manifest;
        }

        private static string ReadString(JsonElement element, string propertyName)
        {
            if (!element.TryGetProperty(propertyName, out var property))
                return "";

            return property.ValueKind == JsonValueKind.String
                ? property.GetString() ?? ""
                : property.ToString();
        }

        private static string[] ReadFlexibleStringArray(JsonElement element, string propertyName)
        {
            if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.Array)
                return Array.Empty<string>();

            return property.EnumerateArray()
                .Select(ReadArrayEntryAsString)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private static string ReadArrayEntryAsString(JsonElement entry)
        {
            return entry.ValueKind switch
            {
                JsonValueKind.String => entry.GetString() ?? "",
                JsonValueKind.Object => ReadString(entry, "name"),
                _ => entry.ToString(),
            };
        }

        private static object[] ReadCatalogs(JsonElement element, string propertyName)
        {
            if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.Array)
                return Array.Empty<object>();

            return property.EnumerateArray()
                .Where(entry => entry.ValueKind == JsonValueKind.Object)
                .Select(entry => new
                {
                    type = ReadString(entry, "type"),
                    id = ReadString(entry, "id"),
                    name = ReadString(entry, "name"),
                })
                .Cast<object>()
                .ToArray();
        }
    }
}