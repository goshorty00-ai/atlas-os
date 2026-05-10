using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AtlasAI.Streaming
{
    public sealed class AddonRegistry
    {
        private sealed class CacheEntry
        {
            public AddonManifest? Manifest;
            public DateTimeOffset FetchedAtUtc;
        }

        private readonly object _gate = new object();
        private readonly Dictionary<string, CacheEntry> _cache = new Dictionary<string, CacheEntry>(StringComparer.OrdinalIgnoreCase);
        private readonly AddonClient _client;
        private readonly TimeSpan _cacheTtl;
        private readonly TimeSpan _manifestTimeout;

        public AddonRegistry(AddonClient client, TimeSpan? cacheTtl = null, TimeSpan? manifestTimeout = null)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _cacheTtl = cacheTtl ?? TimeSpan.FromMinutes(15);
            _manifestTimeout = manifestTimeout ?? TimeSpan.FromSeconds(8);
        }

        public async Task<AddonManifest?> GetManifestAsync(string baseUrl, CancellationToken ct)
        {
            var normalized = AddonRouter.WithInjectedKeys(baseUrl);
            if (string.IsNullOrWhiteSpace(normalized))
                return null;

            lock (_gate)
            {
                if (_cache.TryGetValue(normalized, out var entry) &&
                    entry != null &&
                    entry.Manifest != null &&
                    (DateTimeOffset.UtcNow - entry.FetchedAtUtc) < _cacheTtl)
                {
                    return entry.Manifest;
                }
            }

            var url = AddonRouter.ManifestUrl(normalized);
            if (string.IsNullOrWhiteSpace(url))
                return null;

            JsonDocument? doc = null;
            try
            {
                doc = await _client.GetJsonAsync(url, _manifestTimeout, ct).ConfigureAwait(false);
                if (doc == null || doc.RootElement.ValueKind != JsonValueKind.Object)
                    return null;

                var manifest = ParseManifest(doc.RootElement);
                lock (_gate)
                {
                    _cache[normalized] = new CacheEntry { Manifest = manifest, FetchedAtUtc = DateTimeOffset.UtcNow };
                }
                return manifest;
            }
            catch
            {
                return null;
            }
            finally
            {
                try { doc?.Dispose(); } catch { }
            }
        }

        public static bool SupportsResource(AddonManifest? manifest, string resourceName)
        {
            try
            {
                if (manifest == null) return false;
                var rn = (resourceName ?? "").Trim();
                if (string.IsNullOrWhiteSpace(rn)) return false;
                return manifest.Resources.Any(r => string.Equals((r ?? "").Trim(), rn, StringComparison.OrdinalIgnoreCase));
            }
            catch
            {
                return false;
            }
        }

        private static AddonManifest ParseManifest(JsonElement root)
        {
            var id = "";
            var name = "";
            try { if (root.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String) id = (idEl.GetString() ?? "").Trim(); } catch { }
            try { if (root.TryGetProperty("name", out var nEl) && nEl.ValueKind == JsonValueKind.String) name = (nEl.GetString() ?? "").Trim(); } catch { }

            var resources = new List<string>();
            try
            {
                if (root.TryGetProperty("resources", out var resEl) && resEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var r in resEl.EnumerateArray())
                    {
                        if (r.ValueKind == JsonValueKind.String)
                        {
                            var s = (r.GetString() ?? "").Trim();
                            if (!string.IsNullOrWhiteSpace(s)) resources.Add(s);
                        }
                        else if (r.ValueKind == JsonValueKind.Object)
                        {
                            if (r.TryGetProperty("name", out var rnEl) && rnEl.ValueKind == JsonValueKind.String)
                            {
                                var s = (rnEl.GetString() ?? "").Trim();
                                if (!string.IsNullOrWhiteSpace(s)) resources.Add(s);
                            }
                        }
                    }
                }
            }
            catch
            {
            }

            var types = new List<string>();
            try
            {
                if (root.TryGetProperty("types", out var typesEl) && typesEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var t in typesEl.EnumerateArray())
                    {
                        if (t.ValueKind != JsonValueKind.String) continue;
                        var s = (t.GetString() ?? "").Trim();
                        if (!string.IsNullOrWhiteSpace(s)) types.Add(s);
                    }
                }
            }
            catch
            {
            }

            var catalogs = new List<AddonCatalog>();
            try
            {
                if (root.TryGetProperty("catalogs", out var catsEl) && catsEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var c in catsEl.EnumerateArray())
                    {
                        if (c.ValueKind != JsonValueKind.Object) continue;
                        var type = "";
                        var cid = "";
                        try { if (c.TryGetProperty("type", out var tEl) && tEl.ValueKind == JsonValueKind.String) type = (tEl.GetString() ?? "").Trim(); } catch { }
                        try { if (c.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String) cid = (idEl.GetString() ?? "").Trim(); } catch { }
                        if (string.IsNullOrWhiteSpace(type) || string.IsNullOrWhiteSpace(cid)) continue;
                        catalogs.Add(new AddonCatalog(type, cid));
                    }
                }
            }
            catch
            {
            }

            resources = resources
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            types = types
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            catalogs = catalogs
                .GroupBy(c => $"{c.Type}::{c.Id}", StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();

            return new AddonManifest(id, name, resources, types, catalogs);
        }
    }
}

