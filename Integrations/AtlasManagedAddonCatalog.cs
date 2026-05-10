using System;
using System.Collections.Generic;
using System.Linq;
using AtlasAI.Models;

namespace AtlasAI.Integrations
{
    internal sealed record AtlasManagedAddonDefinition(
        string Id,
        string Name,
        string Url,
        string Description,
        string[] Resources,
        string[] Types,
        object[] Catalogs);

    internal static class AtlasManagedAddonCatalog
    {
        private static readonly AtlasManagedAddonDefinition[] Definitions =
        {
            new AtlasManagedAddonDefinition(
                Id: "atlas.local.library",
                Name: "Atlas Local Library",
                Url: "atlas://local-library",
                Description: "Built-in Atlas addon for local files and scanned library playback.",
                Resources: new[] { "catalog", "meta", "stream" },
                Types: new[] { "movie", "series", "music" },
                Catalogs: new object[]
                {
                    new { type = "movie", id = "atlas-library-movies", name = "Local Movies" },
                    new { type = "series", id = "atlas-library-series", name = "Local Series" },
                    new { type = "music", id = "atlas-library-music", name = "Local Music" }
                }),
            new AtlasManagedAddonDefinition(
                Id: "atlas.cloud.links",
                Name: "Atlas Cloud Links",
                Url: "atlas://cloud-links",
                Description: "Built-in Atlas addon for direct cloud-hosted media links and debrid-ready playback.",
                Resources: new[] { "catalog", "meta", "stream" },
                Types: new[] { "movie", "series", "cloud" },
                Catalogs: new object[]
                {
                    new { type = "cloud", id = "atlas-cloud-links", name = "Cloud Links" }
                })
        };

        public static bool IsManagedUrl(string? baseUrl)
            => TryFind(baseUrl) != null;

        public static bool TryCreateManifest(string? baseUrl, out AddonManifest manifest)
        {
            var definition = TryFind(baseUrl);
            if (definition == null)
            {
                manifest = null!;
                return false;
            }

            manifest = new AddonManifest
            {
                Id = definition.Id,
                Name = definition.Name,
                Version = "1.0.0",
                Description = definition.Description,
                Resources = definition.Resources,
                Types = definition.Types,
                Catalogs = definition.Catalogs
            };
            return true;
        }

        public static IReadOnlyList<AddonServerItem> CreateManagedServerItems()
        {
            return Definitions
                .Select(definition => new AddonServerItem
                {
                    Name = definition.Name,
                    Url = definition.Url,
                    Enabled = true,
                    Status = ServerStatus.Online,
                    ErrorMessage = "Built into Atlas",
                    IsManagedByAtlas = true,
                    LastCheck = DateTime.Now
                })
                .ToArray();
        }

        private static AtlasManagedAddonDefinition? TryFind(string? baseUrl)
        {
            var normalized = Normalize(baseUrl);
            if (string.IsNullOrWhiteSpace(normalized))
                return null;

            return Definitions.FirstOrDefault(definition =>
                string.Equals(definition.Url, normalized, StringComparison.OrdinalIgnoreCase));
        }

        private static string Normalize(string? baseUrl)
        {
            var raw = (baseUrl ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(raw))
                return string.Empty;

            return raw.TrimEnd('/');
        }
    }
}