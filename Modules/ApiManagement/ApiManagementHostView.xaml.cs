using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using AtlasAI.AI;
using AtlasAI.Core;
using AtlasAI.Views.ViewModels;
using Microsoft.Web.WebView2.Core;

namespace AtlasAI.Modules.ApiManagement
{
    public partial class ApiManagementHostView : UserControl
    {
        private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
        private DispatcherTimer? _stateTimer;
        private DateTime _lastPingUtc = DateTime.MinValue;
        private readonly Dictionary<string, IntegrationStatus> _status = new Dictionary<string, IntegrationStatus>(StringComparer.OrdinalIgnoreCase);
        private const string AddonServersKey = "streaming_addon_servers";
        private const string HiddenIntegrationsKey = "api_hidden_integrations";

        private sealed class IntegrationStatus
        {
            public string Status = "unknown";
            public int LatencyMs;
            public int Requests;
            public double Uptime;
            public DateTime LastCheckedUtc;
        }

        public ApiManagementHostView()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            try
            {
                await EnsureInitializedAsync();
            }
            catch
            {
            }
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            try
            {
                try
                {
                    if (_stateTimer != null)
                    {
                        _stateTimer.Stop();
                        _stateTimer = null;
                    }
                }
                catch
                {
                }

                if (ApiWebView?.CoreWebView2 != null)
                    ApiWebView.CoreWebView2.WebMessageReceived -= CoreWebView2_WebMessageReceived;
            }
            catch
            {
            }
        }

        private async Task EnsureInitializedAsync()
        {
            if (ApiWebView?.CoreWebView2 != null) return;

            await ApiWebView.EnsureCoreWebView2Async();

            ApiWebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
            ApiWebView.CoreWebView2.Settings.AreDevToolsEnabled = true;
            ApiWebView.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = true;
            try
            {
                var settings = ApiWebView.CoreWebView2.Settings;
                var p = settings.GetType().GetProperty("IsWebMessageEnabled");
                if (p != null && p.PropertyType == typeof(bool))
                    p.SetValue(settings, true);
            }
            catch
            {
            }

            var dist = FindFigmaDist();
            if (string.IsNullOrWhiteSpace(dist))
            {
                try { MissingUiOverlay.Visibility = Visibility.Visible; } catch { }
                return;
            }

            try { MissingUiOverlay.Visibility = Visibility.Collapsed; } catch { }

            ApiWebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                "api-ui",
                dist,
                CoreWebView2HostResourceAccessKind.Allow);

            ApiWebView.CoreWebView2.WebMessageReceived += CoreWebView2_WebMessageReceived;

            long indexWriteTicks = 0;
            try
            {
                var indexPath = Path.Combine(dist, "index.html");
                if (File.Exists(indexPath))
                    indexWriteTicks = File.GetLastWriteTimeUtc(indexPath).Ticks;
            }
            catch
            {
            }

            var v = (indexWriteTicks != 0 ? indexWriteTicks : DateTime.UtcNow.Ticks).ToString();
            ApiWebView.CoreWebView2.Navigate($"https://api-ui/index.html?mode=api&v={v}");

            StartStateTimer();
            _ = PingImportantEndpointsAsync();
            Post("api.state", BuildStatePayload());
        }

        private void CoreWebView2_WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                var json = e.WebMessageAsJson;
                if (string.IsNullOrWhiteSpace(json)) return;

                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (!root.TryGetProperty("type", out var typeEl)) return;
                var type = (typeEl.GetString() ?? "").Trim();
                var payload = root.TryGetProperty("payload", out var payloadEl) ? payloadEl : default;

                switch (type)
                {
                    case "api.getState":
                        _ = PingImportantEndpointsAsync();
                        Post("api.state", BuildStatePayload());
                        break;
                    case "api.openUrl":
                        {
                            var url = payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty("url", out var uEl) && uEl.ValueKind == JsonValueKind.String
                                ? (uEl.GetString() ?? "").Trim()
                                : (payload.ValueKind == JsonValueKind.String ? (payload.GetString() ?? "").Trim() : "");
                            if (!TryOpenHttpsUrl(url))
                                Post("api.toast", new { text = "Invalid URL" });
                        }
                        break;
                    case "api.setVoiceKey":
                        {
                            if (TrySaveVoiceKey(payload, out var providerSaved))
                                Post("api.toast", new { text = $"Saved {providerSaved} API key" });
                            else
                                Post("api.toast", new { text = "Save failed (missing provider or API key)" });

                            _ = PingImportantEndpointsAsync();
                            Post("api.state", BuildStatePayload());
                        }
                        break;
                    case "api.setIntegrationKeys":
                        {
                            if (TrySaveIntegrationKeys(payload, out var integrationSaved))
                                Post("api.toast", new { text = $"Saved {integrationSaved}" });
                            else
                                Post("api.toast", new { text = "Save failed" });

                            _ = PingImportantEndpointsAsync();
                            Post("api.state", BuildStatePayload());
                        }
                        break;
                    case "api.openSettings":
                        {
                            var id = payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String
                                ? (idEl.GetString() ?? "").Trim()
                                : "";
                            TryOpenSettings(id);
                        }
                        break;
                    case "api.openLogsFolder":
                        TryOpenLogsFolder();
                        break;
                    case "api.addCustomIntegration":
                        if (TrySaveCustomIntegration(payload, out var savedName))
                        {
                            Post("api.toast", new { text = $"Saved integration: {savedName}" });
                        }
                        else if (TrySaveKnownIntegrationFromCustomPayload(payload, out var knownSaved))
                        {
                            Post("api.toast", new { text = $"Saved {knownSaved} API key" });
                            _ = PingImportantEndpointsAsync();
                        }
                        else
                        {
                            Post("api.toast", new { text = "Connect failed (missing name or URL)" });
                        }
                        Post("api.state", BuildStatePayload());
                        break;
                    case "api.removeIntegration":
                        if (TryRemoveIntegration(payload, out var removedName))
                            Post("api.toast", new { text = $"Removed: {removedName}" });
                        else
                            Post("api.toast", new { text = "Remove failed" });
                        _ = PingImportantEndpointsAsync();
                        Post("api.state", BuildStatePayload());
                        break;
                    case "api.test":
                        _ = RunTestAsync(payload);
                        break;
                    case "api.testIntegration":
                        _ = RunIntegrationTestAsync(payload);
                        break;
                }
            }
            catch
            {
            }
        }

        private void StartStateTimer()
        {
            try
            {
                if (_stateTimer != null) return;
                _stateTimer = new DispatcherTimer(DispatcherPriority.Background)
                {
                    Interval = TimeSpan.FromSeconds(5)
                };
                _stateTimer.Tick += (_, __) =>
                {
                    try
                    {
                        if (ApiWebView?.CoreWebView2 == null) return;
                        Post("api.state", BuildStatePayload());
                    }
                    catch
                    {
                    }
                };
                _stateTimer.Start();
            }
            catch
            {
            }
        }

        private void Post(string type, object payload)
        {
            try
            {
                if (ApiWebView?.CoreWebView2 == null) return;
                var msg = JsonSerializer.Serialize(new { type, payload });
                ApiWebView.CoreWebView2.PostWebMessageAsJson(msg);
            }
            catch
            {
            }
        }

        private object BuildStatePayload()
        {
            var now = DateTime.UtcNow;
            var keys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try { keys = IntegrationKeyStore.GetAllDecrypted(); } catch { keys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); }
            var voiceKeys = LoadVoiceKeys();
            var addonServers = GetAddonServers(keys);

            var hidden = LoadHiddenIntegrations(keys);
            var hiddenDirty = false;

            // NOTE: AI providers are intentionally not exposed/configurable via API Manager.

            var integrations = new List<object>();

            void Add(string id, string name, bool configured, bool allowHideWhenUnconfigured = true, string unconfiguredStatus = "unknown")
            {
                try
                {
                    if (configured)
                    {
                        if (hidden.Remove(id))
                            hiddenDirty = true;
                    }
                    else
                    {
                        if (allowHideWhenUnconfigured && hidden.Contains(id))
                            return;
                    }
                }
                catch
                {
                }

                if (!_status.TryGetValue(id, out var st))
                    st = new IntegrationStatus();

                integrations.Add(new
                {
                    id,
                    name,
                    configured,
                    status = configured ? (string.IsNullOrWhiteSpace(st.Status) ? "unknown" : st.Status) : unconfiguredStatus,
                    latencyMs = configured ? (st.LatencyMs > 0 ? st.LatencyMs : (int?)null) : null,
                    requests = configured ? st.Requests : 0,
                    uptime = configured ? (st.Uptime > 0 ? st.Uptime : 0) : 0
                });
            }

            var tmdbKey = (keys.TryGetValue("tmdb", out var t1) ? t1 : "").Trim();
            Add("tmdb", "TMDB", !string.IsNullOrWhiteSpace(tmdbKey));

            Add("addon_servers", $"Addon Servers ({addonServers.Count})", addonServers.Count > 0);
            if (!hidden.Contains("addon_servers"))
            {
                foreach (var server in addonServers)
                {
                    var id = "addon_" + StableId(server);
                    var label = TryGetHost(server);
                    if (string.IsNullOrWhiteSpace(label))
                        label = server;
                    Add(id, label, true);
                }
            }

            var traktClient = (keys.TryGetValue("trakt_client_id", out var tr1) ? tr1 : "").Trim();
            var traktToken = (keys.TryGetValue("trakt_token", out var tr2) ? tr2 : "").Trim();
            var traktConfigured = !string.IsNullOrWhiteSpace(traktClient) && !string.IsNullOrWhiteSpace(traktToken);
            if (traktConfigured)
            {
                // If Trakt was previously in "warning" (from partial config), clear it until the next ping sets a real result.
                try
                {
                    var st = GetOrCreateStatus("trakt");
                    if (string.Equals(st.Status, "warning", StringComparison.OrdinalIgnoreCase))
                        st.Status = "unknown";
                }
                catch { }
            }
            Add("trakt", "Trakt", traktConfigured);

            var spotifyClientId = (keys.TryGetValue("spotify_client_id", out var sp1) ? sp1 : "").Trim();
            var spotifyClientSecret = (keys.TryGetValue("spotify_client_secret", out var sp2) ? sp2 : "").Trim();
            Add("spotify", "Spotify", !string.IsNullOrWhiteSpace(spotifyClientId) && !string.IsNullOrWhiteSpace(spotifyClientSecret));

            var musicBrainzContact = (keys.TryGetValue("musicbrainz_contact", out var mb) ? mb : "").Trim();
            Add("musicbrainz", "MusicBrainz", true, allowHideWhenUnconfigured: false);

            var lastFm = (keys.TryGetValue("lastfm_key", out var lf) ? lf : "").Trim();
            Add("lastfm", "Last.fm", !string.IsNullOrWhiteSpace(lastFm), allowHideWhenUnconfigured: false);

            var discogs = (keys.TryGetValue("discogs_token", out var dg) ? dg : "").Trim();
            Add("discogs", "Discogs", !string.IsNullOrWhiteSpace(discogs), allowHideWhenUnconfigured: false);

            var soundCloud = (keys.TryGetValue("soundcloud_client_id", out var sc) ? sc : "").Trim();
            Add("soundcloud", "SoundCloud", !string.IsNullOrWhiteSpace(soundCloud), allowHideWhenUnconfigured: false);

            var fanartTv = (keys.TryGetValue("fanarttv_key", out var fa) ? fa : "").Trim();
            Add("fanarttv", "fanart.tv", !string.IsNullOrWhiteSpace(fanartTv), allowHideWhenUnconfigured: false);

            var igdb = (keys.TryGetValue("igdb_client_id", out var ig) ? ig : "").Trim();
            Add("igdb", "IGDB", !string.IsNullOrWhiteSpace(igdb));

            var rpdb = (keys.TryGetValue("rpdb", out var rk) ? rk : "").Trim();
            Add("rpdb", "RPDB", !string.IsNullOrWhiteSpace(rpdb), allowHideWhenUnconfigured: false);

            var omdbKey = (keys.TryGetValue("omdb", out var omk) ? omk : "").Trim();
            Add("omdb", "OMDb", !string.IsNullOrWhiteSpace(omdbKey), allowHideWhenUnconfigured: false);

            var cloudSelected = (keys.TryGetValue("cloud_provider_selected", out var cps) ? cps : "").Trim();
            var realDebrid = (keys.TryGetValue("realdebrid", out var rd) ? rd : "").Trim();
            var cloudConfigured = string.Equals(cloudSelected, "realdebrid", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(realDebrid);
            Add("realdebrid", "Real-Debrid", cloudConfigured, allowHideWhenUnconfigured: false);

            var elevenlabs = (voiceKeys.TryGetValue("elevenlabs", out var el) ? el : "").Trim();
            Add("elevenlabs", "ElevenLabs", !string.IsNullOrWhiteSpace(elevenlabs), allowHideWhenUnconfigured: false);

            try
            {
                var custom = keys.Keys
                    .Where(k => k.StartsWith("api_custom_", StringComparison.OrdinalIgnoreCase) && k.EndsWith("_url", StringComparison.OrdinalIgnoreCase))
                    .ToList();
                foreach (var urlKey in custom)
                {
                    var id = urlKey.Substring(0, urlKey.Length - "_url".Length);
                    var url = (keys.TryGetValue(urlKey, out var u) ? u : "").Trim();
                    var labelKey = id + "_name";
                    var name = (keys.TryGetValue(labelKey, out var n) ? n : "").Trim();
                    if (string.IsNullOrWhiteSpace(name)) name = id.Replace("api_custom_", "").Replace('_', ' ');
                    Add(id, name, !string.IsNullOrWhiteSpace(url));
                }
            }
            catch
            {
            }

            if (hiddenDirty)
            {
                try { SaveHiddenIntegrations(hidden); } catch { }
            }

            return new
            {
                lastUpdatedUtc = now.ToString("O"),
                integrations,
                ai = new { activeProvider = "", last = "" }
            };
        }

        private static bool TryOpenHttpsUrl(string? url)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(url))
                    return false;

                if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
                    return false;

                if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
                    return false;

                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri.AbsoluteUri)
                {
                    UseShellExecute = true
                });
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TrySaveIntegrationKeys(JsonElement payload, out string savedName)
        {
            savedName = "";
            try
            {
                if (payload.ValueKind != JsonValueKind.Object)
                    return false;

                var id = payload.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String
                    ? (idEl.GetString() ?? "").Trim().ToLowerInvariant()
                    : "";
                if (string.IsNullOrWhiteSpace(id))
                    return false;

                if (!payload.TryGetProperty("values", out var valuesEl) || valuesEl.ValueKind != JsonValueKind.Object)
                    return false;

                var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                string label;
                switch (id)
                {
                    case "tmdb":
                        label = "TMDB";
                        allowed.Add("tmdb");
                        break;
                    case "trakt":
                        label = "Trakt";
                        allowed.Add("trakt_client_id");
                        allowed.Add("trakt_client_secret");
                        allowed.Add("trakt_token");
                        break;
                    case "spotify":
                        label = "Spotify";
                        allowed.Add("spotify_client_id");
                        allowed.Add("spotify_client_secret");
                        break;
                    case "musicbrainz":
                        label = "MusicBrainz";
                        allowed.Add("musicbrainz_contact");
                        break;
                    case "lastfm":
                        label = "Last.fm";
                        allowed.Add("lastfm_key");
                        break;
                    case "discogs":
                        label = "Discogs";
                        allowed.Add("discogs_token");
                        break;
                    case "soundcloud":
                        label = "SoundCloud";
                        allowed.Add("soundcloud_client_id");
                        break;
                    case "fanarttv":
                        label = "fanart.tv";
                        allowed.Add("fanarttv_key");
                        break;
                    case "igdb":
                        label = "IGDB";
                        allowed.Add("igdb_client_id");
                        allowed.Add("igdb_client_secret");
                        break;
                    case "rpdb":
                        label = "RPDB";
                        allowed.Add("rpdb");
                        break;
                    case "omdb":
                        label = "OMDb";
                        allowed.Add("omdb");
                        break;
                    case "realdebrid":
                        label = "Real-Debrid";
                        allowed.Add("realdebrid");
                        break;
                    default:
                        return false;
                }

                foreach (var prop in valuesEl.EnumerateObject())
                {
                    var key = (prop.Name ?? "").Trim();
                    if (string.IsNullOrWhiteSpace(key) || !allowed.Contains(key))
                        continue;

                    var raw = prop.Value.ValueKind == JsonValueKind.String ? (prop.Value.GetString() ?? "") : "";
                    raw = (raw ?? "").Trim();

                    if (string.IsNullOrWhiteSpace(raw))
                    {
                        try { IntegrationKeyStore.Delete(key); } catch { }
                        continue;
                    }

                    var sanitized = ApiKeySanitizer.SanitizeForHttpHeader(raw);
                    sanitized = (sanitized ?? "").Trim();
                    if (string.IsNullOrWhiteSpace(sanitized))
                        continue;

                    try { IntegrationKeyStore.SetProtected(key, sanitized); } catch { }
                }

                if (id == "realdebrid")
                {
                    try { IntegrationKeyStore.SetProtected("cloud_provider_selected", "realdebrid"); } catch { }
                }

                savedName = label;
                return true;
            }
            catch
            {
                savedName = "";
                return false;
            }
        }

        private async Task PingImportantEndpointsAsync()
        {
            try
            {
                var now = DateTime.UtcNow;
                if ((now - _lastPingUtc) < TimeSpan.FromSeconds(20))
                    return;
                _lastPingUtc = now;

                Dictionary<string, string> keys;
                try { keys = IntegrationKeyStore.GetAllDecrypted(); } catch { keys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); }
                var voiceKeys = LoadVoiceKeys();
                var addonServers = GetAddonServers(keys);

                var tmdbKey = (keys.TryGetValue("tmdb", out var t1) ? t1 : "").Trim();
                if (!string.IsNullOrWhiteSpace(tmdbKey))
                    await PingAsync("tmdb", $"https://api.themoviedb.org/3/configuration?api_key={Uri.EscapeDataString(tmdbKey)}", null).ConfigureAwait(false);

                var online = 0;
                var worst = "unknown";
                for (var i = 0; i < addonServers.Count; i++)
                {
                    var baseUrl = NormalizeAddonServer(addonServers[i]);
                    if (!string.IsNullOrWhiteSpace(baseUrl))
                    {
                        var id = "addon_" + StableId(addonServers[i]);
                        await PingAsync(id, $"{baseUrl}/manifest.json", null).ConfigureAwait(false);
                        var st = GetOrCreateStatus(id);
                        if (string.Equals(st.Status, "online", StringComparison.OrdinalIgnoreCase))
                            online++;
                        if (string.Equals(st.Status, "offline", StringComparison.OrdinalIgnoreCase))
                            worst = "offline";
                        else if (string.Equals(st.Status, "warning", StringComparison.OrdinalIgnoreCase) && worst != "offline")
                            worst = "warning";
                        else if (string.Equals(st.Status, "online", StringComparison.OrdinalIgnoreCase) && (worst == "unknown" || worst == "online"))
                            worst = "online";
                    }
                }
                try
                {
                    var agg = GetOrCreateStatus("addon_servers");
                    agg.Status = addonServers.Count == 0 ? "offline" : (online > 0 ? "online" : worst);
                    agg.LastCheckedUtc = DateTime.UtcNow;
                }
                catch
                {
                }

                var traktClient = (keys.TryGetValue("trakt_client_id", out var tr1) ? tr1 : "").Trim();
                var traktToken = (keys.TryGetValue("trakt_token", out var tr2) ? tr2 : "").Trim();
                if (!string.IsNullOrWhiteSpace(traktClient) && !string.IsNullOrWhiteSpace(traktToken))
                {
                    await PingAsync(
                        "trakt",
                        "https://api.trakt.tv/users/me",
                        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["trakt-api-version"] = "2",
                            ["trakt-api-key"] = traktClient,
                            ["Authorization"] = "Bearer " + traktToken
                        })
                        .ConfigureAwait(false);
                }
                // Partial Trakt config is treated as "not configured" (offline) in the UI.

                var musicBrainzContact = (keys.TryGetValue("musicbrainz_contact", out var mb) ? mb : "").Trim();
                var effectiveMusicBrainzContact = string.IsNullOrWhiteSpace(musicBrainzContact) ? "AtlasAI" : musicBrainzContact;
                await PingAsync(
                    "musicbrainz",
                    "https://musicbrainz.org/ws/2/release/?query=release:thriller&fmt=json&limit=1",
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["User-Agent"] = $"AtlasAI/1.0 ({effectiveMusicBrainzContact})"
                    })
                    .ConfigureAwait(false);

                var lastFm = (keys.TryGetValue("lastfm_key", out var lf) ? lf : "").Trim();
                if (!string.IsNullOrWhiteSpace(lastFm))
                {
                    await PingAsync(
                        "lastfm",
                        $"https://ws.audioscrobbler.com/2.0/?method=chart.gettopartists&api_key={Uri.EscapeDataString(lastFm)}&format=json&limit=1",
                        null)
                        .ConfigureAwait(false);
                }

                var discogs = (keys.TryGetValue("discogs_token", out var dg) ? dg : "").Trim();
                if (!string.IsNullOrWhiteSpace(discogs))
                {
                    await PingAsync(
                        "discogs",
                        $"https://api.discogs.com/database/search?type=release&per_page=1&q={Uri.EscapeDataString("thriller")}&token={Uri.EscapeDataString(discogs)}",
                        null)
                        .ConfigureAwait(false);
                }

                // AI providers intentionally not pinged from API Manager.

                var elevenlabs = (voiceKeys.TryGetValue("elevenlabs", out var el) ? el : "").Trim();
                if (!string.IsNullOrWhiteSpace(elevenlabs))
                {
                    await PingAsync(
                        "elevenlabs",
                        "https://api.elevenlabs.io/v1/voices",
                        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["xi-api-key"] = elevenlabs })
                        .ConfigureAwait(false);
                }

                var omdbApiKey = (keys.TryGetValue("omdb", out var omk) ? omk : "").Trim();
                if (!string.IsNullOrWhiteSpace(omdbApiKey))
                {
                    await PingAsync(
                        "omdb",
                        $"https://www.omdbapi.com/?apikey={Uri.EscapeDataString(omdbApiKey)}&t=Inception",
                        null)
                        .ConfigureAwait(false);
                }
            }
            catch
            {
            }
        }

        private static string SafeGetActiveAiProvider()
        {
            try { return AIManager.GetActiveProvider().ToString(); } catch { return ""; }
        }

        private static string SafeGetRealtimeUsage()
        {
            try { return AIManager.GetRealtimeUsageShort() ?? ""; } catch { return ""; }
        }

        private static string BuildAiName(string baseName, AIProviderType type)
        {
            try
            {
                var active = AIManager.GetActiveProvider();
                return active == type ? baseName + " (active)" : baseName;
            }
            catch
            {
                return baseName;
            }
        }

        private static List<string> GetAddonServers(Dictionary<string, string> keys)
        {
            var results = new List<string>();
            try
            {
                var raw = (keys.TryGetValue(AddonServersKey, out var v) ? v : "").Trim();
                if (!string.IsNullOrWhiteSpace(raw))
                {
                    List<string> list;
                    try
                    {
                        list = JsonSerializer.Deserialize<List<string>>(raw) ?? new List<string>();
                    }
                    catch
                    {
                        list = raw.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).ToList();
                    }
                    results.AddRange(list);
                }
            }
            catch
            {
            }

            var normalizedFromStore = results
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(NormalizeAddonServer)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

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
                            foreach (var el in doc.RootElement.EnumerateArray())
                            {
                                if (el.ValueKind != JsonValueKind.Object) continue;
                                var url = "";
                                var enabled = (bool?)null;
                                try { if (el.TryGetProperty("Url", out var u) && u.ValueKind == JsonValueKind.String) url = (u.GetString() ?? "").Trim(); } catch { }
                                try { if (el.TryGetProperty("Enabled", out var en) && (en.ValueKind == JsonValueKind.True || en.ValueKind == JsonValueKind.False)) enabled = en.GetBoolean(); } catch { }
                                if (string.IsNullOrWhiteSpace(url)) continue;
                                var norm = NormalizeAddonServer(url);
                                if (string.IsNullOrWhiteSpace(norm)) continue;

                                configEnabled ??= new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
                                configEnabledUrls ??= new List<string>();
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

            if (normalizedFromStore.Count == 0)
            {
                if (configEnabledUrls != null && configEnabledUrls.Count > 0)
                    normalizedFromStore.AddRange(configEnabledUrls);
            }
            else
            {
                if (configEnabled != null && configEnabled.Count > 0)
                    normalizedFromStore = normalizedFromStore.Where(u => !configEnabled.TryGetValue(u, out var en) || en).ToList();
            }

            return normalizedFromStore
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static string TryGetHost(string url)
        {
            try
            {
                if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
                    return uri.Host;
            }
            catch
            {
            }
            return "";
        }

        private static string StableId(string input)
        {
            try
            {
                var bytes = MD5.HashData(Encoding.UTF8.GetBytes((input ?? "").Trim()));
                var sb = new StringBuilder();
                for (var i = 0; i < bytes.Length; i++)
                    sb.Append(bytes[i].ToString("x2"));
                return sb.ToString();
            }
            catch
            {
                return Math.Abs(((input ?? "").Trim()).GetHashCode()).ToString();
            }
        }

        private static Dictionary<string, string> LoadAiKeys()
        {
            try
            {
                var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AtlasAI");
                var path = Path.Combine(dir, "ai_keys.json");
                if (!File.Exists(path))
                    return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                var json = File.ReadAllText(path);
                var raw = JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                var keys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var kv in raw)
                {
                    var v = (kv.Value ?? "").Trim();
                    if (v.StartsWith("dpapi:", StringComparison.Ordinal))
                        v = SecretProtector.UnprotectIfNeeded(v);
                    keys[kv.Key] = (v ?? "").Trim();
                }
                return keys;
            }
            catch
            {
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }
        }

        private static bool TryRemoveIntegration(JsonElement payload, out string removedName)
        {
            removedName = "";
            try
            {
                var id = payload.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String ? (idEl.GetString() ?? "").Trim() : "";
                if (string.IsNullOrWhiteSpace(id)) return false;

                removedName = id;
                var keys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                try { keys = IntegrationKeyStore.GetAllDecrypted(); } catch { keys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); }

                var hidden = LoadHiddenIntegrations(keys);
                var hiddenDirty = false;
                void Hide(string integrationId)
                {
                    try
                    {
                        if (string.IsNullOrWhiteSpace(integrationId)) return;
                        if (hidden.Add(integrationId.Trim()))
                            hiddenDirty = true;
                    }
                    catch
                    {
                    }
                }
                void PersistHidden()
                {
                    try
                    {
                        if (!hiddenDirty) return;
                        SaveHiddenIntegrations(hidden);
                    }
                    catch
                    {
                    }
                }

                if (string.Equals(id, "addon_servers", StringComparison.OrdinalIgnoreCase))
                {
                    try { IntegrationKeyStore.SetProtected("streaming_addon_servers", ""); } catch { }
                    try { IntegrationKeyStore.SetProtected("streaming_addon_servers_seeded", "1"); } catch { }
                    Hide("addon_servers");
                    PersistHidden();
                    try
                    {
                        Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                        {
                            try { MediaCentreViewModel.Instance?.ReloadAddonServersFromStore(); } catch { }
                        }));
                    }
                    catch { }
                    removedName = "Addon Servers";
                    return true;
                }

                if (id.StartsWith("addon_", StringComparison.OrdinalIgnoreCase))
                {
                    var target = id.Substring("addon_".Length);
                    var list = GetAddonServers(keys);
                    var next = list.Where(u => !string.Equals(StableId(u), target, StringComparison.OrdinalIgnoreCase)).ToList();
                    try { IntegrationKeyStore.SetProtected("streaming_addon_servers", JsonSerializer.Serialize(next)); } catch { }
                    if (next.Count == 0)
                    {
                        try { IntegrationKeyStore.SetProtected("streaming_addon_servers_seeded", "1"); } catch { }
                    }

                    try
                    {
                        var appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AtlasAI");
                        var cfgPath = Path.Combine(appData, "addon_servers.json");
                        if (File.Exists(cfgPath))
                        {
                            var json = File.ReadAllText(cfgPath);
                            var items = new List<Dictionary<string, object>>();
                            try { items = JsonSerializer.Deserialize<List<Dictionary<string, object>>>(json) ?? new List<Dictionary<string, object>>(); } catch { }
                            
                            var remaining = new List<Dictionary<string, object>>();
                            foreach (var item in items)
                            {
                                var url = "";
                                if (item.TryGetValue("Url", out var u) && u is JsonElement je && je.ValueKind == JsonValueKind.String)
                                    url = je.GetString() ?? "";
                                else if (item.TryGetValue("Url", out var u2) && u2 is string s2)
                                    url = s2;
                                
                                var stable = StableId(NormalizeAddonServer(url));
                                if (!string.Equals(stable, target, StringComparison.OrdinalIgnoreCase))
                                    remaining.Add(item);
                            }
                            
                            File.WriteAllText(cfgPath, JsonSerializer.Serialize(remaining, new JsonSerializerOptions { WriteIndented = true }));
                        }
                    }
                    catch
                    {
                    }

                    removedName = "Addon Server";
                    try
                    {
                        Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                        {
                            try { MediaCentreViewModel.Instance?.ReloadAddonServersFromStore(); } catch { }
                        }));
                    }
                    catch { }
                    return true;
                }

                if (id.StartsWith("api_custom_", StringComparison.OrdinalIgnoreCase))
                {
                    var prefix = id.Trim();
                    var toRemove = keys.Keys.Where(k => k.StartsWith(prefix + "_", StringComparison.OrdinalIgnoreCase)).ToList();
                    foreach (var k in toRemove)
                    {
                        try { IntegrationKeyStore.Delete(k); } catch { }
                    }
                    removedName = "Custom Integration";
                    return true;
                }

                if (string.Equals(id, "tmdb", StringComparison.OrdinalIgnoreCase))
                {
                    try { IntegrationKeyStore.Delete("tmdb"); } catch { }
                    Hide("tmdb");
                    PersistHidden();
                    removedName = "TMDB";
                    return true;
                }
                if (string.Equals(id, "trakt", StringComparison.OrdinalIgnoreCase))
                {
                    try { IntegrationKeyStore.Delete("trakt_client_id"); } catch { }
                    try { IntegrationKeyStore.Delete("trakt_client_secret"); } catch { }
                    try { IntegrationKeyStore.Delete("trakt_token"); } catch { }
                    Hide("trakt");
                    PersistHidden();
                    removedName = "Trakt";
                    return true;
                }
                if (string.Equals(id, "spotify", StringComparison.OrdinalIgnoreCase))
                {
                    try { IntegrationKeyStore.Delete("spotify_client_id"); } catch { }
                    try { IntegrationKeyStore.Delete("spotify_client_secret"); } catch { }
                    Hide("spotify");
                    PersistHidden();
                    removedName = "Spotify";
                    return true;
                }
                if (string.Equals(id, "musicbrainz", StringComparison.OrdinalIgnoreCase))
                {
                    try { IntegrationKeyStore.Delete("musicbrainz_contact"); } catch { }
                    Hide("musicbrainz");
                    PersistHidden();
                    removedName = "MusicBrainz";
                    return true;
                }
                if (string.Equals(id, "lastfm", StringComparison.OrdinalIgnoreCase))
                {
                    try { IntegrationKeyStore.Delete("lastfm_key"); } catch { }
                    Hide("lastfm");
                    PersistHidden();
                    removedName = "Last.fm";
                    return true;
                }
                if (string.Equals(id, "discogs", StringComparison.OrdinalIgnoreCase))
                {
                    try { IntegrationKeyStore.Delete("discogs_token"); } catch { }
                    Hide("discogs");
                    PersistHidden();
                    removedName = "Discogs";
                    return true;
                }
                if (string.Equals(id, "soundcloud", StringComparison.OrdinalIgnoreCase))
                {
                    try { IntegrationKeyStore.Delete("soundcloud_client_id"); } catch { }
                    Hide("soundcloud");
                    PersistHidden();
                    removedName = "SoundCloud";
                    return true;
                }
                if (string.Equals(id, "fanarttv", StringComparison.OrdinalIgnoreCase))
                {
                    try { IntegrationKeyStore.Delete("fanarttv_key"); } catch { }
                    Hide("fanarttv");
                    PersistHidden();
                    removedName = "fanart.tv";
                    return true;
                }
                if (string.Equals(id, "igdb", StringComparison.OrdinalIgnoreCase))
                {
                    try { IntegrationKeyStore.Delete("igdb_client_id"); } catch { }
                    try { IntegrationKeyStore.Delete("igdb_client_secret"); } catch { }
                    Hide("igdb");
                    PersistHidden();
                    removedName = "IGDB";
                    return true;
                }

                if (string.Equals(id, "rpdb", StringComparison.OrdinalIgnoreCase))
                {
                    try { IntegrationKeyStore.Delete("rpdb"); } catch { }
                    Hide("rpdb");
                    PersistHidden();
                    removedName = "RPDB";
                    return true;
                }

                if (string.Equals(id, "realdebrid", StringComparison.OrdinalIgnoreCase))
                {
                    try { IntegrationKeyStore.Delete("realdebrid"); } catch { }
                    try { IntegrationKeyStore.Delete("cloud_provider_selected"); } catch { }
                    Hide("realdebrid");
                    PersistHidden();
                    removedName = "Real-Debrid";
                    return true;
                }

                if (string.Equals(id, "openai", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(id, "claude", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(id, "gemini", StringComparison.OrdinalIgnoreCase))
                {
                    var provider = id.ToLowerInvariant();
                    try { IntegrationKeyStore.Delete(provider); } catch { }
                    try { RemoveAiKeyFromDisk(provider); } catch { }
                    Hide(provider);
                    PersistHidden();
                    removedName = provider.ToUpperInvariant();
                    return true;
                }

                if (string.Equals(id, "elevenlabs", StringComparison.OrdinalIgnoreCase))
                {
                    try { RemoveVoiceKeyFromDisk("elevenlabs"); } catch { }
                    Hide("elevenlabs");
                    PersistHidden();
                    removedName = "ElevenLabs";
                    return true;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        private static void RemoveAiKeyFromDisk(string provider)
        {
            try
            {
                var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AtlasAI");
                var path = Path.Combine(dir, "ai_keys.json");
                if (!File.Exists(path)) return;
                var json = File.ReadAllText(path);
                var raw = JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                if (raw.Remove(provider))
                {
                    Directory.CreateDirectory(dir);
                    File.WriteAllText(path, JsonSerializer.Serialize(raw, new JsonSerializerOptions { WriteIndented = true }));
                }
            }
            catch
            {
            }
        }

        private static void RemoveVoiceKeyFromDisk(string provider)
        {
            try
            {
                var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AtlasAI");
                var path = Path.Combine(dir, "voice_keys.json");
                if (!File.Exists(path)) return;
                var json = File.ReadAllText(path);
                var raw = JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                if (raw.Remove(provider))
                {
                    Directory.CreateDirectory(dir);
                    File.WriteAllText(path, JsonSerializer.Serialize(raw, new JsonSerializerOptions { WriteIndented = true }));
                }
            }
            catch
            {
            }
        }

        private static bool TrySaveVoiceKey(JsonElement payload, out string providerSaved)
        {
            providerSaved = "";
            try
            {
                if (payload.ValueKind != JsonValueKind.Object)
                    return false;

                var provider = payload.TryGetProperty("provider", out var pEl) && pEl.ValueKind == JsonValueKind.String
                    ? (pEl.GetString() ?? "").Trim()
                    : "";
                var apiKeyRaw = payload.TryGetProperty("apiKey", out var kEl) && kEl.ValueKind == JsonValueKind.String
                    ? (kEl.GetString() ?? "").Trim()
                    : "";

                if (string.IsNullOrWhiteSpace(provider) || string.IsNullOrWhiteSpace(apiKeyRaw))
                    return false;

                return TrySaveVoiceKey(provider, apiKeyRaw, out providerSaved);
            }
            catch
            {
                return false;
            }
        }

        private static bool TrySaveKnownIntegrationFromCustomPayload(JsonElement payload, out string savedName)
        {
            savedName = "";
            try
            {
                if (payload.ValueKind != JsonValueKind.Object)
                    return false;

                var name = payload.TryGetProperty("name", out var nEl) && nEl.ValueKind == JsonValueKind.String
                    ? (nEl.GetString() ?? "").Trim()
                    : "";
                var auth = payload.TryGetProperty("auth", out var aEl) && aEl.ValueKind == JsonValueKind.String
                    ? (aEl.GetString() ?? "").Trim()
                    : "";
                var tokenOrKey = payload.TryGetProperty("tokenOrKey", out var tk) && tk.ValueKind == JsonValueKind.String
                    ? (tk.GetString() ?? "").Trim()
                    : "";

                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(tokenOrKey))
                    return false;
                if (!string.Equals(auth, "apikey", StringComparison.OrdinalIgnoreCase))
                    return false;

                var normalized = name.Trim().ToLowerInvariant();
                if (normalized == "elevenlabs" || normalized == "eleven labs")
                {
                    if (!TrySaveVoiceKey("elevenlabs", tokenOrKey, out var providerSaved))
                        return false;
                    savedName = providerSaved;
                    return true;
                }

                if (normalized == "omdb" || normalized == "omdbapi" || normalized == "omdb api")
                {
                    var sanitized = ApiKeySanitizer.SanitizeForHttpHeader(tokenOrKey);
                    if (string.IsNullOrWhiteSpace(sanitized))
                        return false;
                    IntegrationKeyStore.SetProtected("omdb", sanitized);
                    savedName = "OMDb";
                    return true;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        private static bool TrySaveVoiceKey(string provider, string apiKeyRaw, out string providerSaved)
        {
            providerSaved = "";
            try
            {
                provider = (provider ?? "").Trim().ToLowerInvariant();
                if (provider != "elevenlabs" && provider != "openai")
                    return false;

                var sanitized = ApiKeySanitizer.SanitizeForHttpHeader(apiKeyRaw);
                sanitized = (sanitized ?? "").Trim();
                if (string.IsNullOrWhiteSpace(sanitized))
                    return false;

                var existingPath = AtlasPaths.VoiceKeysReadCandidates().FirstOrDefault(File.Exists);
                Dictionary<string, string> raw;
                try
                {
                    if (!string.IsNullOrWhiteSpace(existingPath) && File.Exists(existingPath))
                    {
                        var json = File.ReadAllText(existingPath);
                        raw = JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    }
                    else
                    {
                        raw = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    }
                }
                catch
                {
                    raw = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                }

                raw[provider] = SecretProtector.Protect(sanitized);

                var jsonOut = JsonSerializer.Serialize(raw, new JsonSerializerOptions { WriteIndented = true });

                if (!string.IsNullOrWhiteSpace(existingPath))
                {
                    try
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(existingPath) ?? AtlasPaths.RoamingDir);
                        SafeFile.WriteAllTextAtomic(existingPath, jsonOut);
                        providerSaved = provider == "elevenlabs" ? "ElevenLabs" : provider;
                        return true;
                    }
                    catch
                    {
                    }
                }

                try
                {
                    Directory.CreateDirectory(AtlasPaths.RoamingDir);
                    SafeFile.WriteAllTextAtomic(AtlasPaths.RoamingVoiceKeysPath, jsonOut);
                    providerSaved = provider == "elevenlabs" ? "ElevenLabs" : provider;
                    return true;
                }
                catch
                {
                }

                try
                {
                    Directory.CreateDirectory(AtlasPaths.LocalDir);
                    SafeFile.WriteAllTextAtomic(AtlasPaths.LocalVoiceKeysPath, jsonOut);
                    providerSaved = provider == "elevenlabs" ? "ElevenLabs" : provider;
                    return true;
                }
                catch
                {
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        private static Dictionary<string, string> LoadVoiceKeys()
        {
            try
            {
                var keys = SettingsWindow.GetVoiceApiKeys();
                var normalized = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var kv in keys)
                    normalized[kv.Key] = (kv.Value ?? "").Trim();
                return normalized;
            }
            catch
            {
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }
        }

        private static string NormalizeAddonServer(string input)
        {
            try
            {
                var s = (input ?? "").Trim().TrimEnd('/');
                if (string.IsNullOrWhiteSpace(s)) return "";
                if (!Uri.TryCreate(s, UriKind.Absolute, out var uri))
                {
                    if (!Uri.TryCreate("http://" + s, UriKind.Absolute, out uri))
                        return "";
                }
                if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
                    return "";
                var b = new UriBuilder(uri) { Fragment = "" };
                if ((b.Path ?? "").EndsWith("/manifest.json", StringComparison.OrdinalIgnoreCase))
                    b.Path = b.Path.Substring(0, b.Path.Length - "/manifest.json".Length).TrimEnd('/');
                return b.Uri.ToString().TrimEnd('/');
            }
            catch
            {
                return "";
            }
        }

        private async Task PingAsync(string id, string url, Dictionary<string, string>? headers)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            int statusCode = 0;
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                if (headers != null)
                {
                    foreach (var kv in headers)
                        req.Headers.TryAddWithoutValidation(kv.Key, kv.Value);
                }
                using var res = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, CancellationToken.None).ConfigureAwait(false);
                statusCode = (int)res.StatusCode;
                var ok = res.IsSuccessStatusCode;
                sw.Stop();
                var st = GetOrCreateStatus(id);
                st.Status = ok ? "online" : "warning";
                st.LatencyMs = (int)Math.Max(0, sw.ElapsedMilliseconds);
                st.LastCheckedUtc = DateTime.UtcNow;
                if (st.Uptime <= 0) st.Uptime = ok ? 100 : 0;
                else st.Uptime = Math.Clamp(st.Uptime + (ok ? 0.02 : -0.2), 0, 100);
                st.Requests = Math.Max(0, st.Requests + 1);
            }
            catch
            {
                sw.Stop();
                var st = GetOrCreateStatus(id);
                st.Status = "offline";
                st.LatencyMs = 0;
                st.LastCheckedUtc = DateTime.UtcNow;
                if (st.Uptime <= 0) st.Uptime = 0;
                else st.Uptime = Math.Clamp(st.Uptime - 0.4, 0, 100);
                st.Requests = Math.Max(0, st.Requests + 1);
            }
        }

        private IntegrationStatus GetOrCreateStatus(string id)
        {
            lock (_status)
            {
                if (_status.TryGetValue(id, out var st))
                    return st;
                st = new IntegrationStatus();
                _status[id] = st;
                return st;
            }
        }

        private void TryOpenSettings(string focusIntegrationId)
        {
            try
            {
                // Addon servers are managed from the Media Centre > Servers view (not the main Settings window).
                if (!string.IsNullOrWhiteSpace(focusIntegrationId) &&
                    (string.Equals(focusIntegrationId, "addon_servers", StringComparison.OrdinalIgnoreCase) ||
                     focusIntegrationId.StartsWith("addon_", StringComparison.OrdinalIgnoreCase)))
                {
                    try
                    {
                        var vm = MediaCentreViewModel.Instance;
                        if (vm?.OpenServersViewCommand?.CanExecute(null) ?? false)
                            vm.OpenServersViewCommand.Execute(null);
                        return;
                    }
                    catch
                    {
                        // fall back to settings window
                    }
                }

                var host = Window.GetWindow(this);
                var m = host?.GetType().GetMethod("OpenAtlasSettings");
                if (m != null && string.IsNullOrWhiteSpace(focusIntegrationId) && m.GetParameters().Length == 0)
                {
                    m.Invoke(host, null);
                    return;
                }

                if (m != null && !string.IsNullOrWhiteSpace(focusIntegrationId) && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(string))
                {
                    m.Invoke(host, new object[] { focusIntegrationId });
                    return;
                }
                var win = new SettingsWindow();
                win.Owner = host;
                if (!string.IsNullOrWhiteSpace(focusIntegrationId))
                {
                    try { win.FocusIntegration(focusIntegrationId); } catch { }
                }

                win.ShowDialog();
            }
            catch
            {
            }
        }

        private static HashSet<string> LoadHiddenIntegrations(Dictionary<string, string> keys)
        {
            try
            {
                var raw = (keys.TryGetValue(HiddenIntegrationsKey, out var v) ? v : "") ?? "";
                raw = raw.Trim();
                if (string.IsNullOrWhiteSpace(raw))
                    return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                try
                {
                    var list = JsonSerializer.Deserialize<List<string>>(raw) ?? new List<string>();
                    var cleaned = list
                        .Select(x => (x ?? "").Trim())
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .ToList();
                    return new HashSet<string>(cleaned, StringComparer.OrdinalIgnoreCase);
                }
                catch
                {
                    var parts = raw.Split(new[] { '\r', '\n', ',' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(x => (x ?? "").Trim())
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .ToList();
                    return new HashSet<string>(parts, StringComparer.OrdinalIgnoreCase);
                }
            }
            catch
            {
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }
        }

        private static void SaveHiddenIntegrations(HashSet<string> hidden)
        {
            try
            {
                var list = (hidden ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase))
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(x => x.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var json = JsonSerializer.Serialize(list);
                IntegrationKeyStore.SetProtected(HiddenIntegrationsKey, json);
            }
            catch
            {
            }
        }

        private static void TryOpenLogsFolder()
        {
            try
            {
                var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AtlasAI");
                try { Directory.CreateDirectory(dir); } catch { }
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = dir,
                    UseShellExecute = true
                });
            }
            catch
            {
            }
        }

        private static bool TrySaveCustomIntegration(JsonElement payload, out string savedName)
        {
            savedName = "";
            try
            {
                var name = payload.TryGetProperty("name", out var nEl) ? (nEl.GetString() ?? "").Trim() : "";
                var auth = payload.TryGetProperty("auth", out var aEl) ? (aEl.GetString() ?? "").Trim() : "";
                var endpointUrl = payload.TryGetProperty("endpointUrl", out var uEl) ? (uEl.GetString() ?? "").Trim() : "";
                var clientId = payload.TryGetProperty("clientId", out var c1) ? (c1.GetString() ?? "").Trim() : "";
                var clientSecret = payload.TryGetProperty("clientSecret", out var c2) ? (c2.GetString() ?? "").Trim() : "";
                var tokenOrKey = payload.TryGetProperty("tokenOrKey", out var tk) ? (tk.GetString() ?? "").Trim() : "";

                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(endpointUrl))
                    return false;

                var slug = new string(name.ToLowerInvariant().Select(ch => char.IsLetterOrDigit(ch) ? ch : '_').ToArray()).Trim('_');
                if (string.IsNullOrWhiteSpace(slug))
                    return false;

                var baseKey = $"api_custom_{slug}";
                IntegrationKeyStore.SetProtected(baseKey + "_name", name);
                IntegrationKeyStore.SetProtected(baseKey + "_auth", auth);
                IntegrationKeyStore.SetProtected(baseKey + "_url", endpointUrl);
                if (!string.IsNullOrWhiteSpace(clientId))
                    IntegrationKeyStore.SetProtected(baseKey + "_client_id", clientId);
                if (!string.IsNullOrWhiteSpace(clientSecret))
                    IntegrationKeyStore.SetProtected(baseKey + "_client_secret", clientSecret);
                if (!string.IsNullOrWhiteSpace(tokenOrKey))
                    IntegrationKeyStore.SetProtected(baseKey + "_token", tokenOrKey);
                savedName = name;
                return true;
            }
            catch
            {
                savedName = "";
                return false;
            }
        }

        private async Task RunIntegrationTestAsync(JsonElement payload)
        {
            try
            {
                var id = payload.TryGetProperty("id", out var idEl) ? (idEl.GetString() ?? "").Trim() : "";
                if (string.IsNullOrWhiteSpace(id))
                {
                    Post("api.testResult", new { ok = false, statusCode = 0 });
                    return;
                }

                Dictionary<string, string> keys;
                try { keys = IntegrationKeyStore.GetAllDecrypted(); } catch { keys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); }
                var voiceKeys = LoadVoiceKeys();

                if (id.StartsWith("addon_", StringComparison.OrdinalIgnoreCase))
                {
                    var servers = GetAddonServers(keys);
                    var match = servers.FirstOrDefault(s => string.Equals("addon_" + StableId(s), id, StringComparison.OrdinalIgnoreCase));
                    if (string.IsNullOrWhiteSpace(match))
                    {
                        Post("api.testResult", new { ok = false, statusCode = 0 });
                        return;
                    }
                    var baseUrl = NormalizeAddonServer(match);
                    await PingAsync(id, $"{baseUrl}/manifest.json", null).ConfigureAwait(false);
                    var st = GetOrCreateStatus(id);
                    Post("api.testResult", new { ok = string.Equals(st.Status, "online", StringComparison.OrdinalIgnoreCase), statusCode = 200, latencyMs = st.LatencyMs });
                    return;
                }

                if (string.Equals(id, "tmdb", StringComparison.OrdinalIgnoreCase))
                {
                    var tmdbKey = (keys.TryGetValue("tmdb", out var t1) ? t1 : "").Trim();
                    if (string.IsNullOrWhiteSpace(tmdbKey))
                    {
                        Post("api.testResult", new { ok = false, statusCode = 0 });
                        return;
                    }
                    await PingAsync("tmdb", $"https://api.themoviedb.org/3/configuration?api_key={Uri.EscapeDataString(tmdbKey)}", null).ConfigureAwait(false);
                    var st = GetOrCreateStatus("tmdb");
                    Post("api.testResult", new { ok = string.Equals(st.Status, "online", StringComparison.OrdinalIgnoreCase), statusCode = 200, latencyMs = st.LatencyMs });
                    return;
                }

                if (string.Equals(id, "trakt", StringComparison.OrdinalIgnoreCase))
                {
                    var traktClient = (keys.TryGetValue("trakt_client_id", out var tr1) ? tr1 : "").Trim();
                    var traktToken = (keys.TryGetValue("trakt_token", out var tr2) ? tr2 : "").Trim();
                    if (string.IsNullOrWhiteSpace(traktClient) || string.IsNullOrWhiteSpace(traktToken))
                    {
                        Post("api.testResult", new { ok = false, statusCode = 0 });
                        return;
                    }
                    await PingAsync(
                        "trakt",
                        "https://api.trakt.tv/users/me",
                        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["trakt-api-version"] = "2",
                            ["trakt-api-key"] = traktClient,
                            ["Authorization"] = "Bearer " + traktToken
                        })
                        .ConfigureAwait(false);
                    var st = GetOrCreateStatus("trakt");
                    Post("api.testResult", new { ok = string.Equals(st.Status, "online", StringComparison.OrdinalIgnoreCase), statusCode = 200, latencyMs = st.LatencyMs });
                    return;
                }

                if (string.Equals(id, "spotify", StringComparison.OrdinalIgnoreCase))
                {
                    var clientId = (keys.TryGetValue("spotify_client_id", out var sp1) ? sp1 : "").Trim();
                    var clientSecret = (keys.TryGetValue("spotify_client_secret", out var sp2) ? sp2 : "").Trim();
                    if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
                    {
                        Post("api.testResult", new { ok = false, statusCode = 0 });
                        return;
                    }

                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    using var req = new HttpRequestMessage(HttpMethod.Post, "https://accounts.spotify.com/api/token");
                    var auth = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(clientId + ":" + clientSecret));
                    req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", auth);
                    req.Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["grant_type"] = "client_credentials" });
                    using var res = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, CancellationToken.None).ConfigureAwait(false);
                    sw.Stop();
                    Post("api.testResult", new { ok = res.IsSuccessStatusCode, statusCode = (int)res.StatusCode, latencyMs = (int)Math.Max(0, sw.ElapsedMilliseconds) });
                    return;
                }

                if (string.Equals(id, "musicbrainz", StringComparison.OrdinalIgnoreCase))
                {
                    var contact = (keys.TryGetValue("musicbrainz_contact", out var mb) ? mb : "").Trim();
                    var effectiveContact = string.IsNullOrWhiteSpace(contact) ? "AtlasAI" : contact;
                    await PingAsync(
                        "musicbrainz",
                        "https://musicbrainz.org/ws/2/release/?query=release:thriller&fmt=json&limit=1",
                        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["User-Agent"] = $"AtlasAI/1.0 ({effectiveContact})"
                        })
                        .ConfigureAwait(false);
                    var st = GetOrCreateStatus("musicbrainz");
                    Post("api.testResult", new { ok = string.Equals(st.Status, "online", StringComparison.OrdinalIgnoreCase), statusCode = 200, latencyMs = st.LatencyMs });
                    return;
                }

                if (string.Equals(id, "lastfm", StringComparison.OrdinalIgnoreCase))
                {
                    var key = (keys.TryGetValue("lastfm_key", out var lf) ? lf : "").Trim();
                    if (string.IsNullOrWhiteSpace(key))
                    {
                        Post("api.testResult", new { ok = false, statusCode = 0 });
                        return;
                    }
                    await PingAsync(
                        "lastfm",
                        $"https://ws.audioscrobbler.com/2.0/?method=chart.gettopartists&api_key={Uri.EscapeDataString(key)}&format=json&limit=1",
                        null)
                        .ConfigureAwait(false);
                    var st = GetOrCreateStatus("lastfm");
                    Post("api.testResult", new { ok = string.Equals(st.Status, "online", StringComparison.OrdinalIgnoreCase), statusCode = 200, latencyMs = st.LatencyMs });
                    return;
                }

                if (string.Equals(id, "discogs", StringComparison.OrdinalIgnoreCase))
                {
                    var token = (keys.TryGetValue("discogs_token", out var dg) ? dg : "").Trim();
                    if (string.IsNullOrWhiteSpace(token))
                    {
                        Post("api.testResult", new { ok = false, statusCode = 0 });
                        return;
                    }
                    await PingAsync(
                        "discogs",
                        $"https://api.discogs.com/database/search?type=release&per_page=1&q={Uri.EscapeDataString("thriller")}&token={Uri.EscapeDataString(token)}",
                        null)
                        .ConfigureAwait(false);
                    var st = GetOrCreateStatus("discogs");
                    Post("api.testResult", new { ok = string.Equals(st.Status, "online", StringComparison.OrdinalIgnoreCase), statusCode = 200, latencyMs = st.LatencyMs });
                    return;
                }

                if (string.Equals(id, "realdebrid", StringComparison.OrdinalIgnoreCase))
                {
                    var token = (keys.TryGetValue("realdebrid", out var rd) ? rd : "").Trim();
                    if (string.IsNullOrWhiteSpace(token))
                    {
                        Post("api.testResult", new { ok = false, statusCode = 0 });
                        return;
                    }
                    await PingAsync(
                        "realdebrid",
                        "https://api.real-debrid.com/rest/1.0/user",
                        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["Authorization"] = "Bearer " + token
                        })
                        .ConfigureAwait(false);
                    var st = GetOrCreateStatus("realdebrid");
                    Post("api.testResult", new { ok = string.Equals(st.Status, "online", StringComparison.OrdinalIgnoreCase), statusCode = 200, latencyMs = st.LatencyMs });
                    return;
                }

                if (string.Equals(id, "omdb", StringComparison.OrdinalIgnoreCase))
                {
                    var omdbApiKey = (keys.TryGetValue("omdb", out var ok2) ? ok2 : "").Trim();
                    if (string.IsNullOrWhiteSpace(omdbApiKey))
                    {
                        Post("api.testResult", new { ok = false, statusCode = 0 });
                        return;
                    }
                    await PingAsync(
                        "omdb",
                        $"https://www.omdbapi.com/?apikey={Uri.EscapeDataString(omdbApiKey)}&t=Inception",
                        null)
                        .ConfigureAwait(false);
                    var st = GetOrCreateStatus("omdb");
                    Post("api.testResult", new { ok = string.Equals(st.Status, "online", StringComparison.OrdinalIgnoreCase), statusCode = 200, latencyMs = st.LatencyMs });
                    return;
                }

                if (string.Equals(id, "openai", StringComparison.OrdinalIgnoreCase) || string.Equals(id, "claude", StringComparison.OrdinalIgnoreCase) || string.Equals(id, "gemini", StringComparison.OrdinalIgnoreCase))
                {
                    Post("api.testResult", new { ok = false, statusCode = 0 });
                    return;
                }

                if (string.Equals(id, "elevenlabs", StringComparison.OrdinalIgnoreCase))
                {
                    var elevenlabs = (voiceKeys.TryGetValue("elevenlabs", out var el) ? el : "").Trim();
                    if (string.IsNullOrWhiteSpace(elevenlabs))
                    {
                        Post("api.testResult", new { ok = false, statusCode = 0 });
                        return;
                    }
                    await PingAsync("elevenlabs", "https://api.elevenlabs.io/v1/voices", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["xi-api-key"] = elevenlabs }).ConfigureAwait(false);
                    var st = GetOrCreateStatus("elevenlabs");
                    Post("api.testResult", new { ok = string.Equals(st.Status, "online", StringComparison.OrdinalIgnoreCase), statusCode = 200, latencyMs = st.LatencyMs });
                    return;
                }

                if (id.StartsWith("api_custom_", StringComparison.OrdinalIgnoreCase))
                {
                    var urlKey = id + "_url";
                    var authKey = id + "_auth";
                    var tokenKey = id + "_token";
                    var url = (keys.TryGetValue(urlKey, out var u) ? u : "").Trim();
                    var auth = (keys.TryGetValue(authKey, out var a) ? a : "").Trim().ToLowerInvariant();
                    var tokenOrKey = (keys.TryGetValue(tokenKey, out var t) ? t : "").Trim();

                    var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    if (auth == "token" && !string.IsNullOrWhiteSpace(tokenOrKey))
                        headers["Authorization"] = "Bearer " + tokenOrKey;
                    if (auth == "apikey" && !string.IsNullOrWhiteSpace(tokenOrKey))
                        headers["X-API-Key"] = tokenOrKey;

                    if (string.IsNullOrWhiteSpace(url))
                    {
                        Post("api.testResult", new { ok = false, statusCode = 0 });
                        return;
                    }

                    await PingAsync(id, url, headers.Count == 0 ? null : headers).ConfigureAwait(false);
                    var st = GetOrCreateStatus(id);
                    Post("api.testResult", new { ok = string.Equals(st.Status, "online", StringComparison.OrdinalIgnoreCase), statusCode = 200, latencyMs = st.LatencyMs });
                    return;
                }

                Post("api.testResult", new { ok = false, statusCode = 0 });
            }
            catch
            {
                Post("api.testResult", new { ok = false, statusCode = 0 });
            }
        }

        private async Task RunTestAsync(JsonElement payload)
        {
            try
            {
                var auth = payload.TryGetProperty("auth", out var aEl) ? (aEl.GetString() ?? "").Trim().ToLowerInvariant() : "";
                var endpointUrl = payload.TryGetProperty("endpointUrl", out var uEl) ? (uEl.GetString() ?? "").Trim() : "";
                var clientId = payload.TryGetProperty("clientId", out var c1) ? (c1.GetString() ?? "").Trim() : "";
                var clientSecret = payload.TryGetProperty("clientSecret", out var c2) ? (c2.GetString() ?? "").Trim() : "";
                var tokenOrKey = payload.TryGetProperty("tokenOrKey", out var tk) ? (tk.GetString() ?? "").Trim() : "";

                if (string.IsNullOrWhiteSpace(endpointUrl))
                {
                    Post("api.testResult", new { ok = false, statusCode = 0 });
                    return;
                }

                var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                if (auth == "token" && !string.IsNullOrWhiteSpace(tokenOrKey))
                    headers["Authorization"] = "Bearer " + tokenOrKey;
                if (auth == "apikey" && !string.IsNullOrWhiteSpace(tokenOrKey))
                    headers["X-API-Key"] = tokenOrKey;
                if (auth == "oauth" && !string.IsNullOrWhiteSpace(clientId) && !string.IsNullOrWhiteSpace(clientSecret))
                    headers["Authorization"] = "Basic " + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(clientId + ":" + clientSecret));

                var sw = System.Diagnostics.Stopwatch.StartNew();
                int statusCode = 0;
                bool ok;
                try
                {
                    using var req = new HttpRequestMessage(HttpMethod.Get, endpointUrl);
                    foreach (var kv in headers)
                        req.Headers.TryAddWithoutValidation(kv.Key, kv.Value);
                    using var res = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, CancellationToken.None).ConfigureAwait(false);
                    statusCode = (int)res.StatusCode;
                    ok = res.IsSuccessStatusCode;
                }
                catch
                {
                    ok = false;
                }
                sw.Stop();
                Post("api.testResult", new { ok, statusCode, latencyMs = (int)Math.Max(0, sw.ElapsedMilliseconds) });
            }
            catch
            {
                Post("api.testResult", new { ok = false, statusCode = 0 });
            }
        }

        private static string? FindFigmaDist()
        {
            try
            {
                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                var shipped = Path.Combine(baseDir, "Figma", "Futuristic API Management Dashboard", "dist");
                if (Directory.Exists(shipped) && File.Exists(Path.Combine(shipped, "index.html")))
                    return shipped;
            }
            catch
            {
            }

            var roots = new[]
            {
                AppDomain.CurrentDomain.BaseDirectory,
                Environment.CurrentDirectory,
                Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "",
            };

            foreach (var root in roots)
            {
                try
                {
                    var dir = new DirectoryInfo(root);
                    for (var i = 0; i < 10 && dir != null; i++)
                    {
                        var figmaRoot = Path.Combine(dir.FullName, "Figma", "Futuristic API Management Dashboard");
                        var dist = Path.Combine(figmaRoot, "dist");
                        if (Directory.Exists(dist) && File.Exists(Path.Combine(dist, "index.html")))
                            return dist;

                        dir = dir.Parent;
                    }
                }
                catch
                {
                }
            }

            return null;
        }
    }
}
