using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AtlasAI.Core;
using AtlasAI.MediaMetadata;

namespace AtlasAI.Streaming
{
    public sealed class NullAddonProvider : IAddonProvider
    {
        public string Id => "addon.null";
        public string DisplayName => "Null Addon";

        public Task<IReadOnlyList<AddonSource>> GetSourcesAsync(MediaRequest request, CancellationToken ct)
        {
            return Task.FromResult<IReadOnlyList<AddonSource>>(Array.Empty<AddonSource>());
        }
    }

    public sealed class LocalLibraryAddonProvider : IAddonProvider
    {
        public string Id => "addon.local";
        public string DisplayName => "Local Library";

        public Task<IReadOnlyList<AddonSource>> GetSourcesAsync(MediaRequest request, CancellationToken ct)
        {
            var path = (request.PrimaryPathOrUrl ?? "").Trim();
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return Task.FromResult<IReadOnlyList<AddonSource>>(Array.Empty<AddonSource>());

            var title = string.IsNullOrWhiteSpace(request.Title) ? "Local file" : request.Title;
            var source = new AddonSource(
                SourceId: $"local::{path}",
                Name: title,
                UrlOrPath: path,
                ProviderId: Id,
                ProviderName: DisplayName,
                RequiresDebrid: false,
                Rank: 100,
                Quality: "Original");

            return Task.FromResult<IReadOnlyList<AddonSource>>(new[] { source });
        }
    }

    public sealed class CloudLinkAddonProvider : IAddonProvider
    {
        public string Id => "addon.cloudlink";
        public string DisplayName => "Cloud Link";

        public Task<IReadOnlyList<AddonSource>> GetSourcesAsync(MediaRequest request, CancellationToken ct)
        {
            if (!string.Equals(request.Category, "cloud", StringComparison.OrdinalIgnoreCase))
                return Task.FromResult<IReadOnlyList<AddonSource>>(Array.Empty<AddonSource>());

            var url = (request.PrimaryPathOrUrl ?? "").Trim();
            if (!TryHttpUrl(url, out _))
                return Task.FromResult<IReadOnlyList<AddonSource>>(Array.Empty<AddonSource>());

            var title = string.IsNullOrWhiteSpace(request.Title) ? "Cloud link" : request.Title;
            var source = new AddonSource(
                SourceId: $"cloudlink::{url}",
                Name: title,
                UrlOrPath: url,
                ProviderId: Id,
                ProviderName: DisplayName,
                RequiresDebrid: true,
                Rank: 50,
                Quality: null);

            return Task.FromResult<IReadOnlyList<AddonSource>>(new[] { source });
        }

        private static bool TryHttpUrl(string value, out Uri uri)
        {
            if (Uri.TryCreate(value, UriKind.Absolute, out uri!))
            {
                if (string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            uri = null!;
            return false;
        }
    }

    public sealed class AddonServersAddonProvider : IAddonProvider
    {
        private const string AddonServersKey = "streaming_addon_servers";
        private const string AddonServerSelectedKey = "streaming_addon_servers_selected";
        private const string RatingsCompatibilityHost = "stremio-addon-ratings.baby-beamup.club";
        private static readonly TimeSpan ServerQueryTimeout = TimeSpan.FromSeconds(8);
        private const int MaxConcurrentServerQueries = 8;
        private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        private static readonly Dictionary<string, ServerDescriptor> DescriptorCache = new(StringComparer.OrdinalIgnoreCase);
        private static readonly object DescriptorLock = new();
        private static readonly TmdbClient Tmdb = new();
        private static readonly Dictionary<string, (string imdbId, string contentType)> IdCache = new(StringComparer.OrdinalIgnoreCase);
        private static readonly object IdLock = new();

        static AddonServersAddonProvider()
        {
            try
            {
                Http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "AtlasAI/1.0");
                Http.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json");
            }
            catch
            {
            }
        }

        public string Id => "addon.servers";
        public string DisplayName => "Addon Servers";

        public async Task<IReadOnlyList<AddonSource>> GetSourcesAsync(MediaRequest request, CancellationToken ct)
        {
            var servers = LoadServers();
            if (servers.Count == 0)
                return Array.Empty<AddonSource>();

            using var throttler = new SemaphoreSlim(MaxConcurrentServerQueries);
            var tasks = servers.Select(s => QueryServerSafeAsync(s, request, throttler, ct)).ToArray();
            var allTask = Task.WhenAll(tasks);
            var waves = (int)Math.Ceiling((double)servers.Count / Math.Max(1, MaxConcurrentServerQueries));
            var batchTimeout = TimeSpan.FromSeconds(Math.Min(60, Math.Max(20, ServerQueryTimeout.TotalSeconds * waves)));
            var timeoutTask = Task.Delay(batchTimeout, ct);
            await Task.WhenAny(allTask, timeoutTask).ConfigureAwait(false);

            var completed = new List<AddonSource>();
            foreach (var task in tasks)
            {
                if (!task.IsCompletedSuccessfully)
                    continue;

                var result = task.Result;
                if (result == null || result.Count == 0)
                    continue;

                completed.AddRange(result);
            }

            return completed;
        }

        private static List<string> LoadServers()
        {
            try
            {
                var raw = IntegrationKeyStore.GetDecrypted(AddonServersKey);
                if (string.IsNullOrWhiteSpace(raw))
                    return new List<string>();

                List<string> list;
                try
                {
                    list = JsonSerializer.Deserialize<List<string>>(raw) ?? new List<string>();
                }
                catch
                {
                    list = raw
                        .Split(new[] { '\r', '\n', ',' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(x => (x ?? "").Trim())
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .ToList();
                }
                var mdbListKey = "";
                var tmdbKey = "";
                try
                {
                    mdbListKey = (IntegrationKeyStore.GetDecrypted("mdblist") ?? "").Trim();
                    tmdbKey = (IntegrationKeyStore.GetDecrypted("tmdb") ?? "").Trim();
                }
                catch
                {
                }
                var normalized = list
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(Normalize)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Where(x =>
                    {
                        if (!x.Contains(RatingsCompatibilityHost, StringComparison.OrdinalIgnoreCase))
                            return true;
                        return !string.IsNullOrWhiteSpace(mdbListKey) || !string.IsNullOrWhiteSpace(tmdbKey);
                    })
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                // Use addon_servers.json only for metadata (enabled/disabled) when a key-store list exists.
                // If the key-store list is empty, fall back to enabled config entries for migration/back-compat.
                Dictionary<string, bool>? configEnabled = null;
                List<string>? configEnabledUrls = null;

                try
                {
                    var appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AtlasAI");
                    var cfgPath = Path.Combine(appData, "addon_servers.json");
                    if (File.Exists(cfgPath))
                    {
                        var json = File.ReadAllText(cfgPath);
                        if (!string.IsNullOrWhiteSpace(json))
                        {
                            using var doc = JsonDocument.Parse(json);
                            if (doc.RootElement.ValueKind == JsonValueKind.Array)
                            {
                                configEnabled = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
                                configEnabledUrls = new List<string>();
                                foreach (var el in doc.RootElement.EnumerateArray())
                                {
                                    if (el.ValueKind != JsonValueKind.Object) continue;
                                    var url = "";
                                    var enabled = (bool?)null;
                                    try { if (el.TryGetProperty("Url", out var u) && u.ValueKind == JsonValueKind.String) url = (u.GetString() ?? "").Trim(); } catch { }
                                    try { if (el.TryGetProperty("Enabled", out var en) && (en.ValueKind == JsonValueKind.True || en.ValueKind == JsonValueKind.False)) enabled = en.GetBoolean(); } catch { }
                                    if (string.IsNullOrWhiteSpace(url)) continue;
                                    var norm = Normalize(url);
                                    if (string.IsNullOrWhiteSpace(norm)) continue;
                                    if (enabled.HasValue)
                                        configEnabled[norm] = enabled.Value;
                                    if (!enabled.HasValue || enabled.Value)
                                        configEnabledUrls.Add(norm);
                                }
                            }
                        }
                    }
                }
                catch
                {
                }

                if (configEnabledUrls != null && configEnabledUrls.Count > 0)
                {
                    foreach (var url in configEnabledUrls)
                    {
                        if (string.IsNullOrWhiteSpace(url))
                            continue;
                        if (normalized.Contains(url, StringComparer.OrdinalIgnoreCase))
                            continue;
                        normalized.Add(url);
                    }
                }

                if (configEnabled != null && configEnabled.Count > 0)
                    normalized = normalized.Where(u => !configEnabled.TryGetValue(u, out var en) || en).ToList();

                normalized = normalized
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                return normalized;
            }
            catch
            {
                return new List<string>();
            }
        }

        private static string Normalize(string input)
        {
            var s = (input ?? "").Trim();
            if (string.IsNullOrWhiteSpace(s))
                return "";

            s = s.Trim();
            while (s.Length >= 2)
            {
                var first = s[0];
                var last = s[^1];
                if ((first == '"' && last == '"') || (first == '\'' && last == '\'') || (first == '`' && last == '`') || (first == '<' && last == '>'))
                {
                    s = s.Substring(1, s.Length - 2).Trim();
                    continue;
                }
                break;
            }

            s = s.TrimEnd('/');

            if (!Uri.TryCreate(s, UriKind.Absolute, out var uri))
            {
                if (Uri.TryCreate("http://" + s, UriKind.Absolute, out var fallback))
                    uri = fallback;
                else
                    return "";
            }

            if (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
                return "";

            var builder = new UriBuilder(uri) { Fragment = "" };
            if (builder.Path.EndsWith("/manifest.json", StringComparison.OrdinalIgnoreCase))
                builder.Path = builder.Path.Substring(0, builder.Path.Length - "/manifest.json".Length).TrimEnd('/');

            return builder.Uri.ToString().TrimEnd('/');
        }

        private static string BuildEndpointUrl(string baseUrl, string endpointPath, string? extraQuery = null)
        {
            try
            {
                baseUrl = (baseUrl ?? "").Trim();
                endpointPath = (endpointPath ?? "").Trim();
                extraQuery = (extraQuery ?? "").Trim().TrimStart('?');

                if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(endpointPath))
                    return "";

                var normalized = Normalize(baseUrl);
                if (!Uri.TryCreate(normalized, UriKind.Absolute, out var uri))
                    return "";

                var builder = new UriBuilder(uri) { Fragment = "" };

                var basePath = (builder.Path ?? "").Trim();
                if (basePath.EndsWith("/manifest.json", StringComparison.OrdinalIgnoreCase))
                    basePath = basePath.Substring(0, basePath.Length - "/manifest.json".Length);
                basePath = basePath.TrimEnd('/');
                if (string.Equals(basePath, "/", StringComparison.Ordinal))
                    basePath = "";

                if (!endpointPath.StartsWith("/", StringComparison.Ordinal))
                    endpointPath = "/" + endpointPath;

                builder.Path = string.IsNullOrWhiteSpace(basePath) ? endpointPath : basePath + endpointPath;

                var existingQuery = (builder.Query ?? "").TrimStart('?').Trim();
                var combinedQuery = string.IsNullOrWhiteSpace(existingQuery)
                    ? extraQuery
                    : (string.IsNullOrWhiteSpace(extraQuery) ? existingQuery : existingQuery + "&" + extraQuery);
                builder.Query = combinedQuery ?? "";

                return builder.Uri.ToString();
            }
            catch
            {
                return "";
            }
        }

        private static string InjectRatingsKeysIfNeeded(string serverBaseUrl)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(serverBaseUrl))
                    return serverBaseUrl;

                if (!serverBaseUrl.Contains(RatingsCompatibilityHost, StringComparison.OrdinalIgnoreCase))
                    return serverBaseUrl;

                var mdbListKey = (IntegrationKeyStore.GetDecrypted("mdblist") ?? "").Trim();
                var tmdbKey = (IntegrationKeyStore.GetDecrypted("tmdb") ?? "").Trim();

                if (string.IsNullOrWhiteSpace(mdbListKey) && string.IsNullOrWhiteSpace(tmdbKey))
                    return serverBaseUrl;

                var normalized = Normalize(serverBaseUrl);
                if (!Uri.TryCreate(normalized, UriKind.Absolute, out var uri))
                    return serverBaseUrl;

                var builder = new UriBuilder(uri) { Fragment = "" };
                var path = (builder.Path ?? "").Trim();
                if (!string.IsNullOrWhiteSpace(path) && !string.Equals(path, "/", StringComparison.Ordinal))
                {
                    var trimmed = path.TrimStart('/');
                    var segs = trimmed.Split(new[] { '/' }, 2, StringSplitOptions.None);
                    var config = segs.Length > 0 ? segs[0] : "";
                    var rest = segs.Length > 1 ? "/" + segs[1] : "";

                    var pathDict = ParseQuery(config);

                    if (!string.IsNullOrWhiteSpace(mdbListKey) &&
                        (!pathDict.TryGetValue("mdbListApiKey", out var existingMdbListPath) || string.IsNullOrWhiteSpace(existingMdbListPath)))
                    {
                        pathDict["mdbListApiKey"] = mdbListKey;
                    }

                    if (!string.IsNullOrWhiteSpace(tmdbKey) &&
                        (!pathDict.TryGetValue("tmdbApiKey", out var existingTmdbPath) || string.IsNullOrWhiteSpace(existingTmdbPath)))
                    {
                        pathDict["tmdbApiKey"] = tmdbKey;
                    }

                    builder.Path = "/" + BuildQuery(pathDict) + rest;
                    return builder.Uri.ToString().TrimEnd('/');
                }

                var query = (builder.Query ?? "").TrimStart('?').Trim();
                var queryDict = ParseQuery(query);

                if (!string.IsNullOrWhiteSpace(mdbListKey) &&
                    (!queryDict.TryGetValue("mdbListApiKey", out var existingMdbListQuery) || string.IsNullOrWhiteSpace(existingMdbListQuery)))
                {
                    queryDict["mdbListApiKey"] = mdbListKey;
                }

                if (!string.IsNullOrWhiteSpace(tmdbKey) &&
                    (!queryDict.TryGetValue("tmdbApiKey", out var existingTmdbQuery) || string.IsNullOrWhiteSpace(existingTmdbQuery)))
                {
                    queryDict["tmdbApiKey"] = tmdbKey;
                }

                builder.Query = BuildQuery(queryDict);
                return builder.Uri.ToString().TrimEnd('/');
            }
            catch
            {
                return serverBaseUrl;
            }
        }

        private static Dictionary<string, string> ParseQuery(string query)
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(query))
                return dict;

            foreach (var part in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var p = part.Trim();
                if (string.IsNullOrWhiteSpace(p))
                    continue;

                var idx = p.IndexOf('=');
                if (idx < 0)
                {
                    var keyOnly = Uri.UnescapeDataString(p);
                    if (!string.IsNullOrWhiteSpace(keyOnly))
                        dict[keyOnly] = "";
                    continue;
                }

                var k = Uri.UnescapeDataString(p.Substring(0, idx));
                if (string.IsNullOrWhiteSpace(k))
                    continue;

                var v = idx + 1 >= p.Length ? "" : Uri.UnescapeDataString(p.Substring(idx + 1));
                dict[k] = v;
            }

            return dict;
        }

        private static string BuildQuery(Dictionary<string, string> dict)
        {
            if (dict == null || dict.Count == 0)
                return "";

            return string.Join("&", dict.Select(kv =>
                $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString((kv.Value ?? "").Trim())}"));
        }

        private async Task<IReadOnlyList<AddonSource>> QueryServerSafeAsync(string serverBaseUrl, MediaRequest request, SemaphoreSlim throttler, CancellationToken ct)
        {
            await throttler.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(ServerQueryTimeout);
                try
                {
                    return await QueryServerAsync(serverBaseUrl, request, timeoutCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    if (ct.IsCancellationRequested)
                        throw;
                    return Array.Empty<AddonSource>();
                }
                catch
                {
                    return Array.Empty<AddonSource>();
                }
            }
            finally
            {
                throttler.Release();
            }
        }

        private async Task<IReadOnlyList<AddonSource>> QueryServerAsync(string serverBaseUrl, MediaRequest request, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(serverBaseUrl))
                return Array.Empty<AddonSource>();

            serverBaseUrl = InjectRatingsKeysIfNeeded(serverBaseUrl);

            var descriptor = await GetDescriptorAsync(serverBaseUrl, ct).ConfigureAwait(false);
            try
            {
                var stremioSources = await QueryAddonStreamsAsync(descriptor, request, ct).ConfigureAwait(false);
                if (stremioSources.Count > 0)
                    return stremioSources;
            }
            catch
            {
            }

            Uri serverUri;
            try
            {
                serverUri = new Uri(serverBaseUrl, UriKind.Absolute);
            }
            catch
            {
                return Array.Empty<AddonSource>();
            }

            var query =
                $"title={Uri.EscapeDataString(request.Title ?? "")}" +
                $"&category={Uri.EscapeDataString(request.Category ?? "")}" +
                $"&primary={Uri.EscapeDataString(request.PrimaryPathOrUrl ?? "")}";
            var url = BuildEndpointUrl(serverBaseUrl, "/sources", query);
            if (string.IsNullOrWhiteSpace(url))
                return Array.Empty<AddonSource>();

            try
            {
                using var resp = await Http.GetAsync(url, ct).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode)
                    return Array.Empty<AddonSource>();

                await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                var payload = await JsonSerializer.DeserializeAsync<List<RemoteSource>>(stream, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                }, ct).ConfigureAwait(false);

                if (payload == null || payload.Count == 0)
                    return Array.Empty<AddonSource>();

                var host = serverUri.Host;
                var providerName = CleanProviderName(string.IsNullOrWhiteSpace(descriptor.Name) ? host : descriptor.Name);
                var providerId = $"{Id}::{Normalize(serverBaseUrl)}";
                var mapped = payload
                    .Select(p =>
                    {
                        var u = (p.UrlOrPath ?? p.Url ?? "").Trim();
                        if (string.IsNullOrWhiteSpace(u))
                            return null;

                        var name = string.IsNullOrWhiteSpace(p.Name) ? host : p.Name!.Trim();
                        var rank = p.Rank == 0 ? 40 : p.Rank;
                        var converted = TryConvertResolveToMagnet(u);
                        var urlForPlay = converted.url;
                        var requiresDebrid = p.RequiresDebrid || converted.requiresDebrid;
                        return new AddonSource(
                            SourceId: $"{Id}::{serverBaseUrl}::{u}",
                            Name: name,
                            UrlOrPath: urlForPlay,
                            ProviderId: providerId,
                            ProviderName: providerName,
                            RequiresDebrid: requiresDebrid,
                            Rank: rank,
                            Quality: string.IsNullOrWhiteSpace(p.Quality) ? null : p.Quality);
                    })
                    .Where(x => x != null)
                    .Cast<AddonSource>()
                    .ToList();

                return mapped;
            }
            catch
            {
                return Array.Empty<AddonSource>();
            }
        }

        private async Task<ServerDescriptor> GetDescriptorAsync(string serverBaseUrl, CancellationToken ct)
        {
            lock (DescriptorLock)
            {
                if (DescriptorCache.TryGetValue(serverBaseUrl, out var cached))
                {
                    if ((DateTimeOffset.UtcNow - cached.FetchedAtUtc) < TimeSpan.FromMinutes(15))
                        return cached;
                }
            }

            var manifestUrl = BuildEndpointUrl(serverBaseUrl, "/manifest.json");
            if (string.IsNullOrWhiteSpace(manifestUrl))
                return Remember(serverBaseUrl, new ServerDescriptor(serverBaseUrl, SupportsStreamProtocol: false, Name: "", FetchedAtUtc: DateTimeOffset.UtcNow));
            try
            {
                using var resp = await Http.GetAsync(manifestUrl, ct).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode)
                    return Remember(serverBaseUrl, new ServerDescriptor(serverBaseUrl, SupportsStreamProtocol: false, Name: "", FetchedAtUtc: DateTimeOffset.UtcNow));

                await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
                if (doc.RootElement.ValueKind != JsonValueKind.Object)
                    return Remember(serverBaseUrl, new ServerDescriptor(serverBaseUrl, SupportsStreamProtocol: false, Name: "", FetchedAtUtc: DateTimeOffset.UtcNow));

                var name = doc.RootElement.TryGetProperty("name", out var nameEl) ? (nameEl.GetString() ?? "") : "";
                var isStremio = ManifestHasStreamResource(doc.RootElement);
                return Remember(serverBaseUrl, new ServerDescriptor(serverBaseUrl, SupportsStreamProtocol: isStremio, Name: name.Trim(), FetchedAtUtc: DateTimeOffset.UtcNow));
            }
            catch
            {
                return Remember(serverBaseUrl, new ServerDescriptor(serverBaseUrl, SupportsStreamProtocol: false, Name: "", FetchedAtUtc: DateTimeOffset.UtcNow));
            }
        }

        private static bool ManifestHasStreamResource(JsonElement root)
        {
            if (!root.TryGetProperty("resources", out var resources) || resources.ValueKind != JsonValueKind.Array)
                return false;

            foreach (var r in resources.EnumerateArray())
            {
                if (r.ValueKind == JsonValueKind.String)
                {
                    var s = (r.GetString() ?? "").Trim();
                    if (string.Equals(s, "stream", StringComparison.OrdinalIgnoreCase))
                        return true;
                }
                else if (r.ValueKind == JsonValueKind.Object)
                {
                    if (r.TryGetProperty("name", out var nameEl))
                    {
                        var s = (nameEl.GetString() ?? "").Trim();
                        if (string.Equals(s, "stream", StringComparison.OrdinalIgnoreCase))
                            return true;
                    }
                }
            }

            return false;
        }

        private static ServerDescriptor Remember(string serverBaseUrl, ServerDescriptor descriptor)
        {
            lock (DescriptorLock)
            {
                DescriptorCache[serverBaseUrl] = descriptor;
            }
            return descriptor;
        }

        private async Task<IReadOnlyList<AddonSource>> QueryAddonStreamsAsync(ServerDescriptor descriptor, MediaRequest request, CancellationToken ct)
        {
            var primary = (request.PrimaryPathOrUrl ?? "").Trim();
            if (string.Equals(request.Category, "music", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrWhiteSpace(primary))
                    return await QueryStreamsByContentIdAsync(descriptor, request, ct, primary, "music").ConfigureAwait(false);

                var musicTitle = GetRawLookupTitle(request);
                if (!string.IsNullOrWhiteSpace(musicTitle))
                {
                    var resolvedMusicId = await TryResolveMusicIdAsync(descriptor.BaseUrl, musicTitle!, ct).ConfigureAwait(false);
                    if (!string.IsNullOrWhiteSpace(resolvedMusicId))
                        return await QueryStreamsByContentIdAsync(descriptor, request, ct, resolvedMusicId!, "music").ConfigureAwait(false);
                }

                return Array.Empty<AddonSource>();
            }

            var preferTv = string.Equals(request.Category, "tv", StringComparison.OrdinalIgnoreCase);
            var imdbFromText = TryExtractImdbId($"{request.Title} {request.PrimaryPathOrUrl}");
            if (!string.IsNullOrWhiteSpace(imdbFromText))
            {
                var directContentType = preferTv ? "series" : "movie";
                return await QueryStreamsByContentIdAsync(descriptor, request, ct, imdbFromText!, directContentType).ConfigureAwait(false);
            }

            var apiKey = IntegrationKeyStore.GetDecrypted("tmdb");
            var tmdbIdFromPrimary = TryExtractTmdbId(primary);
            if (tmdbIdFromPrimary.HasValue && !string.IsNullOrWhiteSpace(apiKey))
            {
                var (resolvedImdb, resolvedType) = await Tmdb.TryResolveImdbIdFromTmdbIdAsync(apiKey, tmdbIdFromPrimary.Value, preferTv, ct).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(resolvedImdb) && !string.IsNullOrWhiteSpace(resolvedType))
                    return await QueryStreamsByContentIdAsync(descriptor, request, ct, resolvedImdb!, resolvedType!).ConfigureAwait(false);
            }

            var rawTitle = GetRawLookupTitle(request);
            var lookupTitle = CleanLookupTitle(rawTitle, preferTv);
            if (string.IsNullOrWhiteSpace(lookupTitle))
                lookupTitle = (rawTitle ?? "").Trim();

            var cacheSeed = !string.IsNullOrWhiteSpace(primary) &&
                            (primary.StartsWith("tt", StringComparison.OrdinalIgnoreCase) || primary.StartsWith("tmdb:", StringComparison.OrdinalIgnoreCase))
                ? primary.ToLowerInvariant()
                : lookupTitle;
            var cacheKey = $"{(preferTv ? "tv" : "movie")}::{cacheSeed}";
            (string imdbId, string contentType)? cached = null;
            lock (IdLock)
            {
                if (IdCache.TryGetValue(cacheKey, out var v))
                    cached = v;
            }

            string imdbId;
            string contentType;
            if (cached.HasValue)
            {
                imdbId = cached.Value.imdbId;
                contentType = cached.Value.contentType;
            }
            else
            {
                if (string.IsNullOrWhiteSpace(apiKey))
                {
                    var resolved =
                        await TryResolveViaMetadataCatalogAsync(lookupTitle, preferTv, ct).ConfigureAwait(false) ??
                        await TryResolveViaMetadataCatalogAsync(StripYearToken(lookupTitle), preferTv, ct).ConfigureAwait(false);
                    if (!resolved.HasValue && !string.Equals(lookupTitle, (rawTitle ?? "").Trim(), StringComparison.OrdinalIgnoreCase))
                    {
                        var fallback = (rawTitle ?? "").Trim();
                        resolved =
                            await TryResolveViaMetadataCatalogAsync(fallback, preferTv, ct).ConfigureAwait(false) ??
                            await TryResolveViaMetadataCatalogAsync(StripYearToken(fallback), preferTv, ct).ConfigureAwait(false);
                    }
                    if (!resolved.HasValue)
                        return Array.Empty<AddonSource>();

                    imdbId = resolved.Value.imdbId;
                    contentType = resolved.Value.contentType;
                }
                else
                {
                    var (resolvedImdb, resolvedType) = await Tmdb.TryResolveImdbIdAsync(apiKey, lookupTitle, preferTv, ct).ConfigureAwait(false);
                    if ((string.IsNullOrWhiteSpace(resolvedImdb) || string.IsNullOrWhiteSpace(resolvedType)) &&
                        !string.Equals(lookupTitle, (rawTitle ?? "").Trim(), StringComparison.OrdinalIgnoreCase))
                    {
                        (resolvedImdb, resolvedType) = await Tmdb.TryResolveImdbIdAsync(apiKey, (rawTitle ?? "").Trim(), preferTv, ct).ConfigureAwait(false);
                    }
                    if (string.IsNullOrWhiteSpace(resolvedImdb) || string.IsNullOrWhiteSpace(resolvedType))
                    {
                        var resolved =
                            await TryResolveViaMetadataCatalogAsync(lookupTitle, preferTv, ct).ConfigureAwait(false) ??
                            await TryResolveViaMetadataCatalogAsync(StripYearToken(lookupTitle), preferTv, ct).ConfigureAwait(false);
                        if (!resolved.HasValue && !string.Equals(lookupTitle, (rawTitle ?? "").Trim(), StringComparison.OrdinalIgnoreCase))
                        {
                            var fallback = (rawTitle ?? "").Trim();
                            resolved =
                                await TryResolveViaMetadataCatalogAsync(fallback, preferTv, ct).ConfigureAwait(false) ??
                                await TryResolveViaMetadataCatalogAsync(StripYearToken(fallback), preferTv, ct).ConfigureAwait(false);
                        }
                        if (!resolved.HasValue)
                            return Array.Empty<AddonSource>();

                        imdbId = resolved.Value.imdbId;
                        contentType = resolved.Value.contentType;
                    }
                    else
                    {
                        imdbId = resolvedImdb!;
                        contentType = resolvedType!;
                    }
                }
                lock (IdLock)
                {
                    IdCache[cacheKey] = (imdbId, contentType);
                }
            }

            return await QueryStreamsByContentIdAsync(descriptor, request, ct, imdbId, contentType).ConfigureAwait(false);
        }

        private static async Task<string?> TryResolveMusicIdAsync(string baseUrl, string title, CancellationToken ct)
        {
            var q = (title ?? "").Trim();
            if (string.IsNullOrWhiteSpace(q))
                return null;

            var url = BuildEndpointUrl(baseUrl, $"/catalog/music/top/search={Uri.EscapeDataString(q)}.json");
            if (string.IsNullOrWhiteSpace(url))
                return null;

            try
            {
                using var resp = await Http.GetAsync(url, ct).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode)
                    return null;

                await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
                if (doc.RootElement.ValueKind != JsonValueKind.Object)
                    return null;

                if (!doc.RootElement.TryGetProperty("metas", out var metasEl) || metasEl.ValueKind != JsonValueKind.Array)
                    return null;

                foreach (var m in metasEl.EnumerateArray())
                {
                    if (m.ValueKind != JsonValueKind.Object)
                        continue;
                    if (!m.TryGetProperty("id", out var idEl) || idEl.ValueKind != JsonValueKind.String)
                        continue;
                    var id = (idEl.GetString() ?? "").Trim();
                    if (!string.IsNullOrWhiteSpace(id))
                        return id;
                }
            }
            catch
            {
            }

            return null;
        }

        private static async Task<(string imdbId, string contentType)?> TryResolveViaMetadataCatalogAsync(string title, bool preferTv, CancellationToken ct)
        {
            var q = (title ?? "").Trim();
            if (string.IsNullOrWhiteSpace(q))
                return null;

            var order = preferTv
                ? new[] { "series", "movie" }
                : new[] { "movie", "series" };

            foreach (var contentType in order)
            {
                ct.ThrowIfCancellationRequested();
                var url = $"https://v3-cinemeta.strem.io/catalog/{contentType}/top/search={Uri.EscapeDataString(q)}.json";
                try
                {
                    using var resp = await Http.GetAsync(url, ct).ConfigureAwait(false);
                    if (!resp.IsSuccessStatusCode)
                        continue;

                    await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                    using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
                    if (doc.RootElement.ValueKind != JsonValueKind.Object)
                        continue;

                    if (!doc.RootElement.TryGetProperty("metas", out var metasEl) || metasEl.ValueKind != JsonValueKind.Array)
                        continue;

                    foreach (var m in metasEl.EnumerateArray())
                    {
                        if (m.ValueKind != JsonValueKind.Object)
                            continue;

                        if (!m.TryGetProperty("id", out var idEl) || idEl.ValueKind != JsonValueKind.String)
                            continue;

                        var id = (idEl.GetString() ?? "").Trim();
                        if (string.IsNullOrWhiteSpace(id))
                            continue;

                        if (!id.StartsWith("tt", StringComparison.OrdinalIgnoreCase))
                            continue;

                        return (id.ToLowerInvariant(), contentType);
                    }
                }
                catch
                {
                }
            }

            return null;
        }

        private async Task<IReadOnlyList<AddonSource>> QueryStreamsByContentIdAsync(ServerDescriptor descriptor, MediaRequest request, CancellationToken ct, string imdbId, string contentType)
        {
            var id = imdbId;
            if (string.Equals(contentType, "series", StringComparison.OrdinalIgnoreCase))
            {
                var se = TryParseSeasonEpisode(request.PrimaryPathOrUrl ?? "");
                if (!se.HasValue)
                    se = TryParseSeasonEpisode(request.Title ?? "");
                if (se.HasValue)
                    id = $"{imdbId}:{se.Value.season}:{se.Value.episode}";
            }

            var url = BuildEndpointUrl(descriptor.BaseUrl, $"/stream/{Uri.EscapeDataString(contentType)}/{Uri.EscapeDataString(id)}.json");
            if (string.IsNullOrWhiteSpace(url))
                return Array.Empty<AddonSource>();
            try
            {
                using var resp = await Http.GetAsync(url, ct).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode)
                    return Array.Empty<AddonSource>();

                await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
                if (doc.RootElement.ValueKind != JsonValueKind.Object)
                    return Array.Empty<AddonSource>();

                if (!doc.RootElement.TryGetProperty("streams", out var streamsEl) || streamsEl.ValueKind != JsonValueKind.Array)
                    return Array.Empty<AddonSource>();

                var host = "";
                try { host = new Uri(descriptor.BaseUrl, UriKind.Absolute).Host; } catch { }
                var providerName = CleanProviderName(string.IsNullOrWhiteSpace(descriptor.Name) ? host : descriptor.Name);
                var providerId = $"{Id}::{Normalize(descriptor.BaseUrl)}";

                var mapped = new List<AddonSource>();
                var idx = 0;
                foreach (var s in streamsEl.EnumerateArray())
                {
                    ct.ThrowIfCancellationRequested();
                    if (s.ValueKind != JsonValueKind.Object) continue;

                    var (streamUrl, requiresDebrid) = ExtractAddonStreamUrl(s);
                    var label = GetFirstNonEmptyString(s, "title", "name") ?? providerName;
                    if (string.IsNullOrWhiteSpace(streamUrl))
                    {
                        if (string.IsNullOrWhiteSpace(label)) continue;
                        var metadata = ExtractAddonStreamMetadata(s);
                        var infoOnlyRank = InferInfoOnlyRank(providerName, label, metadata);
                        mapped.Add(new AddonSource(
                            SourceId: $"{Id}::{descriptor.BaseUrl}::{contentType}::{id}::{idx}",
                            Name: label,
                            UrlOrPath: "",
                            ProviderId: providerId,
                            ProviderName: providerName,
                            RequiresDebrid: false,
                            Rank: infoOnlyRank,
                            Quality: metadata.TryGetValue("description", out var desc) ? desc : null,
                            IsInfoOnly: true,
                            Metadata: metadata));
                        idx++;
                        continue;
                    }

                    var quality = InferQuality(s, label);
                    var rank = InferRank(label, quality);

                    mapped.Add(new AddonSource(
                        SourceId: $"{Id}::{descriptor.BaseUrl}::{contentType}::{id}::{idx}",
                        Name: label,
                        UrlOrPath: streamUrl,
                        ProviderId: providerId,
                        ProviderName: providerName,
                        RequiresDebrid: requiresDebrid,
                        Rank: rank,
                        Quality: quality,
                        IsInfoOnly: false,
                        Metadata: null));
                    idx++;
                }

                return mapped;
            }
            catch
            {
                return Array.Empty<AddonSource>();
            }
        }

        private static string CleanProviderName(string name)
        {
            try
            {
                var n = (name ?? "").Trim();
                if (string.IsNullOrWhiteSpace(n)) return "";
                var parts = n.Split('|').Select(x => (x ?? "").Trim()).Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
                n = parts.Count > 0 ? parts[0] : n;
                if (n.Length > 80) n = n.Substring(0, 80).Trim();
                return n;
            }
            catch
            {
                return (name ?? "").Trim();
            }
        }

        private static string? TryExtractImdbId(string text)
        {
            var t = (text ?? "").Trim();
            if (string.IsNullOrWhiteSpace(t))
                return null;

            var m = System.Text.RegularExpressions.Regex.Match(t, @"(?i)\btt\d{7,9}\b");
            if (!m.Success)
                return null;

            return m.Value.ToLowerInvariant();
        }

        private static int? TryExtractTmdbId(string text)
        {
            var t = (text ?? "").Trim();
            if (string.IsNullOrWhiteSpace(t))
                return null;

            var m = System.Text.RegularExpressions.Regex.Match(t, @"(?i)\btmdb:(?:movie:|tv:|series:)?(\d+)\b");
            if (!m.Success)
                return null;

            return int.TryParse(m.Groups[1].Value, out var tmdbId) && tmdbId > 0 ? tmdbId : null;
        }

        private static string GetRawLookupTitle(MediaRequest request)
        {
            var title = (request.Title ?? "").Trim();
            if (!string.IsNullOrWhiteSpace(title) && !string.Equals(title, "Unknown", StringComparison.OrdinalIgnoreCase))
                return title;

            var primary = (request.PrimaryPathOrUrl ?? "").Trim();
            if (string.IsNullOrWhiteSpace(primary))
                return title;

            try
            {
                if (Uri.TryCreate(primary, UriKind.Absolute, out var uri) &&
                    (string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
                {
                    var name = Path.GetFileNameWithoutExtension(uri.AbsolutePath) ?? "";
                    if (!string.IsNullOrWhiteSpace(name))
                        return name.Trim();
                }
            }
            catch
            {
            }

            try
            {
                var name = Path.GetFileNameWithoutExtension(primary) ?? "";
                if (!string.IsNullOrWhiteSpace(name))
                    return name.Trim();
            }
            catch
            {
            }

            return title;
        }

        private static string StripYearToken(string title)
        {
            var t = (title ?? "").Trim();
            if (string.IsNullOrWhiteSpace(t))
                return "";

            t = System.Text.RegularExpressions.Regex.Replace(t, @"\b(19|20)\d{2}\b", " ", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            t = System.Text.RegularExpressions.Regex.Replace(t, @"\s+", " ").Trim();
            return t;
        }

        private static string CleanLookupTitle(string title, bool preferTv)
        {
            var s = (title ?? "").Trim();
            if (string.IsNullOrWhiteSpace(s))
                return "";

            var year = "";
            var yearMatch = System.Text.RegularExpressions.Regex.Match(s, @"\b(19|20)\d{2}\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (yearMatch.Success)
                year = yearMatch.Value;

            s = s.Replace('.', ' ').Replace('_', ' ').Replace('-', ' ');

            s = System.Text.RegularExpressions.Regex.Replace(s, @"[\[\(\{][^\]\)\}]{1,120}[\]\)\}]", " ", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            s = System.Text.RegularExpressions.Regex.Replace(s, @"(?i)\bS\d{1,2}E\d{1,2}\b", " ");
            s = System.Text.RegularExpressions.Regex.Replace(s, @"(?i)\b\d{1,2}x\d{1,2}\b", " ");
            s = System.Text.RegularExpressions.Regex.Replace(s, @"(?i)\bseason\s*\d{1,2}\b", " ");
            s = System.Text.RegularExpressions.Regex.Replace(s, @"(?i)\bepisode\s*\d{1,3}\b", " ");

            s = System.Text.RegularExpressions.Regex.Replace(s, @"(?i)\b(480p|720p|1080p|2160p|4k|8k)\b", " ");
            s = System.Text.RegularExpressions.Regex.Replace(s, @"(?i)\b(web\\-?dl|webrip|web|hdrip|bdrip|bluray|blu\\-?ray|dvdrip|hdtv|cam|ts|tc)\b", " ");
            s = System.Text.RegularExpressions.Regex.Replace(s, @"(?i)\b(x264|x265|h264|h265|hevc|av1|10bit|8bit)\b", " ");
            s = System.Text.RegularExpressions.Regex.Replace(s, @"(?i)\b(aac|ac3|eac3|ddp|dts|truehd|atmos)\b", " ");
            s = System.Text.RegularExpressions.Regex.Replace(s, @"(?i)\b(remux|proper|repack|extended|uncut|limited|criterion)\b", " ");

            s = System.Text.RegularExpressions.Regex.Replace(s, @"(?i)\b(multi|dual\\s*audio|dubbed|subbed)\b", " ");
            s = System.Text.RegularExpressions.Regex.Replace(s, @"(?i)\\b(\\d{3,4}MB|\\d{1,3}\\.\\d{1,2}GB|\\d{1,3}GB)\\b", " ");

            s = System.Text.RegularExpressions.Regex.Replace(s, @"[^\p{L}\p{Nd}\s']", " ");
            s = System.Text.RegularExpressions.Regex.Replace(s, @"\s+", " ").Trim();

            if (!string.IsNullOrWhiteSpace(year) && !System.Text.RegularExpressions.Regex.IsMatch(s, @"\b(19|20)\d{2}\b"))
                s = $"{s} {year}".Trim();

            if (preferTv)
            {
                s = System.Text.RegularExpressions.Regex.Replace(s, @"(?i)\b(complete|pack)\b", " ");
                s = System.Text.RegularExpressions.Regex.Replace(s, @"\s+", " ").Trim();
            }

            return s;
        }

        private static (int season, int episode)? TryParseSeasonEpisode(string primaryPathOrUrl)
        {
            var v = (primaryPathOrUrl ?? "").Trim();
            if (string.IsNullOrWhiteSpace(v))
                return null;

            var file = v;
            try { file = Path.GetFileName(v); } catch { }
            if (string.IsNullOrWhiteSpace(file))
                file = v;

            var m = System.Text.RegularExpressions.Regex.Match(file, @"(?i)\bS(?<s>\d{1,2})E(?<e>\d{1,2})\b");
            if (m.Success)
            {
                if (int.TryParse(m.Groups["s"].Value, out var s) && int.TryParse(m.Groups["e"].Value, out var e))
                    return (s, e);
            }

            m = System.Text.RegularExpressions.Regex.Match(file, @"(?i)\b(?<s>\d{1,2})x(?<e>\d{1,2})\b");
            if (m.Success)
            {
                if (int.TryParse(m.Groups["s"].Value, out var s) && int.TryParse(m.Groups["e"].Value, out var e))
                    return (s, e);
            }

            return null;
        }

        private static (string url, bool requiresDebrid) ExtractAddonStreamUrl(JsonElement streamObj)
        {
            string? magnetCandidate = null;
            try
            {
                if (streamObj.TryGetProperty("sources", out var sourcesEl0) && sourcesEl0.ValueKind == JsonValueKind.Array)
                {
                    foreach (var src in sourcesEl0.EnumerateArray())
                    {
                        if (src.ValueKind != JsonValueKind.String) continue;
                        var s = (src.GetString() ?? "").Trim();
                        if (string.IsNullOrWhiteSpace(s)) continue;
                        if (s.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase))
                        {
                            magnetCandidate = s;
                            break;
                        }
                    }
                }
            }
            catch
            {
                magnetCandidate = null;
            }

            if (magnetCandidate == null)
            {
                try
                {
                    if (streamObj.TryGetProperty("infoHash", out var ih0) && ih0.ValueKind == JsonValueKind.String)
                    {
                        var infoHash = (ih0.GetString() ?? "").Trim();
                        if (!string.IsNullOrWhiteSpace(infoHash))
                            magnetCandidate = $"magnet:?xt=urn:btih:{infoHash}";
                    }
                }
                catch
                {
                    magnetCandidate = null;
                }
            }

            var url = GetFirstNonEmptyString(streamObj, "url", "urlOrPath", "magnetUri", "magnet");
            if (!string.IsNullOrWhiteSpace(url))
            {
                var requires = url.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase);
                if (!requires && Uri.TryCreate(url, UriKind.Absolute, out var u) &&
                    (string.Equals(u.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(u.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
                {
                    // The addon (e.g. Torrentio+RD) has already resolved debrid — use the URL as-is.
                    // Only patch null-token placeholder URLs; never convert a direct URL back to a magnet.
                    url = TryPatchDebridStreamUrl(u, url);
                }
                return (url, requires);
            }

            if (streamObj.TryGetProperty("behaviorHints", out var bh) && bh.ValueKind == JsonValueKind.Object)
            {
                var external = GetFirstNonEmptyString(bh, "externalUrl", "webUrl");
                if (!string.IsNullOrWhiteSpace(external))
                {
                    return (external, false);
                }
            }

            if (streamObj.TryGetProperty("sources", out var sourcesEl) && sourcesEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var src in sourcesEl.EnumerateArray())
                {
                    if (src.ValueKind != JsonValueKind.String) continue;
                    var s = (src.GetString() ?? "").Trim();
                    if (string.IsNullOrWhiteSpace(s)) continue;
                    if (s.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase))
                        return (s, true);
                    if (Uri.TryCreate(s, UriKind.Absolute, out var uri) &&
                        (string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
                    {
                        return (TryPatchDebridStreamUrl(uri, s), false);
                    }
                }
            }

            if (streamObj.TryGetProperty("infoHash", out var ih) && ih.ValueKind == JsonValueKind.String)
            {
                var infoHash = (ih.GetString() ?? "").Trim();
                if (!string.IsNullOrWhiteSpace(infoHash))
                {
                    var magnet = $"magnet:?xt=urn:btih:{infoHash}";
                    return (magnet, true);
                }
            }

            return ("", false);
        }

        private static (string url, bool requiresDebrid) TryConvertResolveToMagnet(string url)
        {
            try
            {
                if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
                    return (url, false);
                var host = (uri.Host ?? "").ToLowerInvariant();
                if (!host.Contains("torrentio."))
                    return (url, false);
                var path = (uri.AbsolutePath ?? "").Trim('/');
                if (string.IsNullOrWhiteSpace(path))
                    return (url, false);
                var parts = path.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 4)
                    return (url, false);
                var resolveIdx = Array.FindIndex(parts, p => string.Equals(p, "resolve", StringComparison.OrdinalIgnoreCase));
                if (resolveIdx < 0 || resolveIdx + 3 >= parts.Length)
                    return (url, false);
                if (!string.Equals(parts[resolveIdx + 1], "realdebrid", StringComparison.OrdinalIgnoreCase))
                    return (url, false);

                var hash = parts[resolveIdx + 3];
                if (!LooksLikeHexHash(hash))
                    return (url, false);

                return ($"magnet:?xt=urn:btih:{hash}", true);
            }
            catch
            {
                return (url, false);
            }
        }

        private static bool LooksLikeHexHash(string value)
        {
            var s = (value ?? "").Trim();
            if (s.Length < 32 || s.Length > 64)
                return false;
            for (var i = 0; i < s.Length; i++)
            {
                var c = s[i];
                var ok = (c >= '0' && c <= '9') ||
                         (c >= 'a' && c <= 'f') ||
                         (c >= 'A' && c <= 'F');
                if (!ok) return false;
            }
            return true;
        }

        private static string TryPatchDebridStreamUrl(Uri uri, string original)
        {
            try
            {
                var path = (uri.AbsolutePath ?? "").ToLowerInvariant();
                if (!path.Contains("/resolve/"))
                    return original;

                if (!path.Contains("/resolve/realdebrid/"))
                    return original;

                if (!path.Contains("/realdebrid/") || !path.Contains("/null/"))
                    return original;

                var token = (IntegrationKeyStore.GetDecrypted("realdebrid") ?? "").Trim();
                if (string.IsNullOrWhiteSpace(token))
                    return original;

                var marker = "/realdebrid/";
                var idx = path.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
                if (idx < 0)
                    return original;

                var after = original.Substring(idx + marker.Length);
                if (!after.StartsWith("null/", StringComparison.OrdinalIgnoreCase))
                    return original;

                return original.Substring(0, idx + marker.Length) + Uri.EscapeDataString(token) + "/" + after.Substring("null/".Length);
            }
            catch
            {
                return original;
            }
        }

        private static IReadOnlyDictionary<string, string> ExtractAddonStreamMetadata(JsonElement streamObj)
        {
            var md = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            void Add(string key, string? value)
            {
                var v = (value ?? "").Trim();
                if (!string.IsNullOrWhiteSpace(v))
                    md[key] = v;
            }

            Add("title", GetFirstNonEmptyString(streamObj, "title"));
            Add("name", GetFirstNonEmptyString(streamObj, "name"));
            Add("description", GetFirstNonEmptyString(streamObj, "description"));

            if (streamObj.TryGetProperty("behaviorHints", out var bh) && bh.ValueKind == JsonValueKind.Object)
            {
                Add("externalUrl", GetFirstNonEmptyString(bh, "externalUrl"));
                Add("webUrl", GetFirstNonEmptyString(bh, "webUrl"));
            }

            return md;
        }

        private static int InferInfoOnlyRank(string providerName, string label, IReadOnlyDictionary<string, string> metadata)
        {
            var p = (providerName ?? "").ToUpperInvariant();
            var l = (label ?? "").ToUpperInvariant();
            var d = metadata.TryGetValue("description", out var desc) ? (desc ?? "").ToUpperInvariant() : "";

            var text = $"{p} {l} {d}";
            if (text.Contains("RATING") || text.Contains("IMDB") || text.Contains("METACRITIC") || text.Contains("TOMATO"))
                return 1100;

            return 900;
        }

        private static string? GetFirstNonEmptyString(JsonElement obj, params string[] names)
        {
            foreach (var n in names)
            {
                if (!obj.TryGetProperty(n, out var el)) continue;
                if (el.ValueKind != JsonValueKind.String) continue;
                var s = (el.GetString() ?? "").Trim();
                if (!string.IsNullOrWhiteSpace(s))
                    return s;
            }
            return null;
        }

        private static string? InferQuality(JsonElement streamObj, string label)
        {
            var q = GetFirstNonEmptyString(streamObj, "quality");
            if (!string.IsNullOrWhiteSpace(q))
                return q;

            var l = (label ?? "").ToUpperInvariant();
            if (l.Contains("2160") || l.Contains("4K")) return "4K";
            if (l.Contains("1080")) return "1080p";
            if (l.Contains("720")) return "720p";
            if (l.Contains("480")) return "480p";
            return null;
        }

        private static int InferRank(string label, string? quality)
        {
            var rank = 60;
            var q = (quality ?? "").ToUpperInvariant();
            if (q.Contains("4K") || q.Contains("2160")) rank += 20;
            else if (q.Contains("1080")) rank += 12;
            else if (q.Contains("720")) rank += 6;

            var l = (label ?? "").ToUpperInvariant();
            if (l.Contains("HDR")) rank += 4;
            if (l.Contains("DV") || l.Contains("DOLBY VISION")) rank += 4;
            if (l.Contains("HEVC") || l.Contains("X265")) rank += 2;
            if (l.Contains("H264") || l.Contains("X264")) rank += 1;
            return rank;
        }

        private readonly record struct ServerDescriptor(string BaseUrl, bool SupportsStreamProtocol, string Name, DateTimeOffset FetchedAtUtc);

        private sealed class RemoteSource
        {
            public string? Name { get; set; }
            public string? Url { get; set; }
            public string? UrlOrPath { get; set; }
            public bool RequiresDebrid { get; set; }
            public int Rank { get; set; }
            public string? Quality { get; set; }
        }
    }
}
