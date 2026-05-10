using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using AtlasAI.Controls;
using AtlasAI.Integrations;
using AtlasAI.Views.ViewModels;
using Microsoft.Web.WebView2.Core;

namespace AtlasAI.Views.MediaCentre
{
    public partial class AddonsView : UserControl
    {
        public event EventHandler? CloseRequested;

        private const string StremioAddonsCatalogUrl = "https://stremio-addons.net/api/addon_catalog/all/stremio-addons.net.json?skip=0";
        private static readonly HttpClient AddonDirectoryHttpClient = CreateAddonDirectoryHttpClient();
        private CoreWebView2Environment? _addonsEnvironment;
        private string? _lastNavigatedAddonsUri;
        private readonly AddonManifestService _addonManifestService = new();
        private MediaCentreViewModel? _viewModel;
        private bool _vmHooked;
        private string _statusText = "Loading addons...";
        private string _networkSummary = "Refreshing network status...";
        private string _wifiNetworksText = "Run a Wi-Fi scan to list nearby networks.";
        private string _lastAction = "";
        private bool _isBusy;
        private bool _frontendReady;
        private List<AddonBrowserItem> _installedAddons = new();
        private List<AddonBrowserItem> _directoryAddons = new();

        public AddonsView()
        {
            InitializeComponent();
            DataContextChanged += AddonsView_DataContextChanged;
            Loaded += AddonsView_Loaded;
            Unloaded += AddonsView_Unloaded;
        }

        private async void AddonsView_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                try
                {
                    AtlasAI.Core.AppLogger.LogInfo("[AddonNavTest] addons.loaded=true");
                }
                catch
                {
                }

                RefreshNetworkSummary();
                await EnsureInitializedAsync();

                try
                {
                    AtlasAI.Core.AppLogger.LogInfo($"[AddonNavTest] addons.bounds width={ActualWidth:0.##} height={ActualHeight:0.##}");
                }
                catch
                {
                }
            }
            catch
            {
            }
        }

        private void AddonsView_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            try
            {
                UnhookViewModel();
                _viewModel = DataContext as MediaCentreViewModel;
                HookViewModel();
            }
            catch
            {
            }
        }

        private void AddonsView_Unloaded(object sender, RoutedEventArgs e)
        {
            try
            {
                if (AddonsWebView?.CoreWebView2 != null)
                    AddonsWebView.CoreWebView2.WebMessageReceived -= AddonsWebView_WebMessageReceived;
            }
            catch
            {
            }

            try
            {
                if (AddonsWebView != null)
                    AddonsWebView.NavigationCompleted -= AddonsWebView_NavigationCompleted;
            }
            catch
            {
            }

            UnhookViewModel();
        }

        private async Task EnsureInitializedAsync()
        {
            try
            {
                if (AddonsWebView == null)
                    return;

                var alreadyInitialized = AddonsWebView.CoreWebView2 != null;
                var dist = FindAddonManagerDist();
                if (string.IsNullOrWhiteSpace(dist))
                    return;

                try
                {
                    var indexPath = Path.Combine(dist, "index.html");
                    var indexExists = File.Exists(indexPath);
                    AtlasAI.Core.AppLogger.LogInfo($"[AddonNavTest] addons.dist.path={dist}");
                    AtlasAI.Core.AppLogger.LogInfo($"[AddonNavTest] addons.index.exists={indexExists.ToString().ToLowerInvariant()}");
                }
                catch
                {
                }

                if (!alreadyInitialized)
                {
                    _addonsEnvironment ??= await CreateAddonsWebViewEnvironmentAsync();
                    await AddonsWebView.EnsureCoreWebView2Async(_addonsEnvironment);
                }

                if (AddonsWebView.CoreWebView2 == null)
                    return;

                try
                {
                    AtlasAI.Core.AppLogger.LogInfo("[AddonNavTest] addons.webview.init=true");
                }
                catch
                {
                }

                try { AddonsWebView.DefaultBackgroundColor = System.Drawing.Color.FromArgb(255, 10, 12, 18); } catch { }

                try
                {
                    AddonsWebView.CoreWebView2.Settings.IsWebMessageEnabled = true;
                    AddonsWebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
                    AddonsWebView.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = true;
                    AddonsWebView.CoreWebView2.Settings.AreDevToolsEnabled = true;
                }
                catch
                {
                }

                const string host = "atlas-ui-addons";
                try
                {
                    AddonsWebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                        host,
                        dist,
                        CoreWebView2HostResourceAccessKind.Allow);
                }
                catch
                {
                }

                try
                {
                    AddonsWebView.NavigationCompleted -= AddonsWebView_NavigationCompleted;
                    AddonsWebView.NavigationCompleted += AddonsWebView_NavigationCompleted;
                }
                catch
                {
                }

                try
                {
                    AddonsWebView.CoreWebView2.WebMessageReceived -= AddonsWebView_WebMessageReceived;
                    AddonsWebView.CoreWebView2.WebMessageReceived += AddonsWebView_WebMessageReceived;
                }
                catch
                {
                }

                var version = GetNewestDistWriteTicks(dist).ToString();
                var targetUri = $"https://{host}/index.html?v={version}";
                if (!alreadyInitialized || !string.Equals(_lastNavigatedAddonsUri, targetUri, StringComparison.OrdinalIgnoreCase))
                {
                    _lastNavigatedAddonsUri = targetUri;
                    AddonsWebView.CoreWebView2.Navigate(targetUri);
                }
            }
            catch
            {
            }
        }

        private void AddonsWebView_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            try
            {
                var source = AddonsWebView?.Source?.ToString() ?? "";
                try
                {
                    AtlasAI.Core.AppLogger.LogInfo($"[AddonNavTest] addons.navigation.completed success={e.IsSuccess.ToString().ToLowerInvariant()} status={(int)e.WebErrorStatus} source={source}");
                }
                catch
                {
                }

                if (!e.IsSuccess)
                {
                    Console.WriteLine($"[AddonsWebView:navigation-failed] status={(int)e.WebErrorStatus} source={AddonsWebView?.Source}");
                    return;
                }

                try
                {
                    if (AddonsWebView?.CoreWebView2 != null)
                    {
                        _ = AddonsWebView.CoreWebView2.ExecuteScriptAsync("console.log(\"[AddonNavTest] addon-frontend.probe document.readyState=\" + document.readyState + \" root=\" + !!document.getElementById(\"root\"));");
                    }
                }
                catch
                {
                }

                _ = RefreshAndPostStateAsync();
            }
            catch
            {
            }
        }

        private async void AddonsWebView_WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                using var document = JsonDocument.Parse(e.WebMessageAsJson);
                var root = document.RootElement;
                if (!root.TryGetProperty("type", out var typeEl) || typeEl.ValueKind != JsonValueKind.String)
                    return;

                var type = (typeEl.GetString() ?? "").Trim();
                var payload = root.TryGetProperty("payload", out var payloadEl) ? payloadEl : default;
                switch (type)
                {
                    case "addons.ready":
                        _frontendReady = true;
                        try
                        {
                            AtlasAI.Core.AppLogger.LogInfo("[AddonNavTest] addons.ready.received=true");
                        }
                        catch
                        {
                        }

                        await RefreshAndPostStateAsync().ConfigureAwait(true);
                        break;
                    case "addons.getState":
                        _frontendReady = true;
                        try
                        {
                            AtlasAI.Core.AppLogger.LogInfo("[AddonNavTest] addons.getState.received=true");
                        }
                        catch
                        {
                        }

                        await RefreshAndPostStateAsync().ConfigureAwait(true);
                        break;
                    case "addons.refresh":
                        try { _viewModel?.ReloadAddonServersFromStore(); } catch { }
                        await RefreshAndPostStateAsync("Addons refreshed.").ConfigureAwait(true);
                        break;
                    case "addons.addManifest":
                        await HandleAddManifestAsync(GetPayloadString(payload, "url")).ConfigureAwait(true);
                        break;
                    case "addons.install":
                        await HandleInstallAsync(
                            GetPayloadString(payload, "url"),
                            GetPayloadString(payload, "manifestUrl"),
                            GetPayloadString(payload, "directoryUrl")).ConfigureAwait(true);
                        break;
                    case "addons.remove":
                        await HandleRemoveAsync(GetPayloadString(payload, "url")).ConfigureAwait(true);
                        break;
                    case "addons.scanWifi":
                        await ScanWifiAsync().ConfigureAwait(true);
                        break;
                    case "addons.openUrl":
                        OpenExternalUrl(GetPayloadString(payload, "url"));
                        break;
                    case "addons.close":
                        try
                        {
                            CloseRequested?.Invoke(this, EventArgs.Empty);
                        }
                        catch
                        {
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                _statusText = string.IsNullOrWhiteSpace(ex.Message) ? "Addon action failed." : ex.Message;
                await PostStateAsync().ConfigureAwait(true);
            }
        }

        private void HookViewModel()
        {
            try
            {
                if (_viewModel == null || _vmHooked)
                    return;

                _viewModel.AddonServers.CollectionChanged += AddonServers_CollectionChanged;
                _vmHooked = true;
            }
            catch
            {
            }
        }

        private void UnhookViewModel()
        {
            try
            {
                if (_viewModel != null && _vmHooked)
                    _viewModel.AddonServers.CollectionChanged -= AddonServers_CollectionChanged;
            }
            catch
            {
            }

            _viewModel = null;
            _vmHooked = false;
        }

        private void AddonServers_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            try
            {
                _ = RefreshAndPostStateAsync();
            }
            catch
            {
            }
        }

        private async Task HandleAddManifestAsync(string input)
        {
            var normalized = NormalizeAddonUrl(input);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                _statusText = "Enter a valid addon URL or manifest URL.";
                await PostStateAsync().ConfigureAwait(true);
                return;
            }

            _isBusy = true;
            _statusText = "Checking addon manifest...";
            await PostStateAsync().ConfigureAwait(true);

            var (success, manifest, error) = await _addonManifestService.FetchManifestAsync(normalized).ConfigureAwait(true);
            if (!success || manifest == null)
            {
                _isBusy = false;
                _statusText = string.IsNullOrWhiteSpace(error) ? "That addon manifest could not be loaded." : error;
                await PostStateAsync().ConfigureAwait(true);
                return;
            }

            try
            {
                if (_viewModel?.AddAddonServerCommand?.CanExecute(normalized) == true)
                    _viewModel.AddAddonServerCommand.Execute(normalized);
                _viewModel?.ReloadAddonServersFromStore();
                if (_viewModel != null)
                    _viewModel.SelectedAddonServer = "";
                if (_viewModel?.RefreshServerCatalogCommand?.CanExecute(null) == true)
                    _viewModel.RefreshServerCatalogCommand.Execute(null);
            }
            catch
            {
            }

            await RefreshAndPostStateAsync($"Installed {manifest.Name}.").ConfigureAwait(true);
        }

        private async Task HandleInstallAsync(string url, string manifestUrl, string directoryUrl)
        {
            var installTarget = !string.IsNullOrWhiteSpace(manifestUrl) ? manifestUrl : url;
            if (string.IsNullOrWhiteSpace(installTarget) && !string.IsNullOrWhiteSpace(directoryUrl))
                installTarget = await ResolveDirectoryInstallManifestUrlAsync(directoryUrl).ConfigureAwait(true);

            await HandleAddManifestAsync(installTarget).ConfigureAwait(true);
        }

        private static void OpenExternalUrl(string input)
        {
            var normalized = NormalizeAddonUrl(input);
            if (string.IsNullOrWhiteSpace(normalized))
                return;

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = normalized,
                    UseShellExecute = true,
                });
            }
            catch
            {
            }
        }

        private async Task HandleRemoveAsync(string url)
        {
            var normalized = NormalizeAddonUrl(url);
            if (string.IsNullOrWhiteSpace(normalized))
                return;

            try
            {
                if (_viewModel?.RemoveAddonServerCommand?.CanExecute(normalized) == true)
                    _viewModel.RemoveAddonServerCommand.Execute(normalized);
                _viewModel?.ReloadAddonServersFromStore();
                if (_viewModel != null)
                    _viewModel.SelectedAddonServer = "";
                if (_viewModel?.RefreshServerCatalogCommand?.CanExecute(null) == true)
                    _viewModel.RefreshServerCatalogCommand.Execute(null);
            }
            catch
            {
            }

            await RefreshAndPostStateAsync("Addon removed.").ConfigureAwait(true);
        }

        private async Task ScanWifiAsync()
        {
            _wifiNetworksText = "Scanning nearby Wi-Fi networks...";
            await PostStateAsync().ConfigureAwait(true);

            try
            {
                var start = new ProcessStartInfo
                {
                    FileName = "netsh",
                    Arguments = "wlan show networks mode=bssid",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                };

                using var process = Process.Start(start);
                if (process == null)
                {
                    _wifiNetworksText = "Unable to start Wi-Fi scan.";
                    await PostStateAsync().ConfigureAwait(true);
                    return;
                }

                var stdout = await process.StandardOutput.ReadToEndAsync().ConfigureAwait(true);
                var stderr = await process.StandardError.ReadToEndAsync().ConfigureAwait(true);
                await process.WaitForExitAsync().ConfigureAwait(true);

                var text = string.IsNullOrWhiteSpace(stdout) ? stderr : stdout;
                _wifiNetworksText = string.IsNullOrWhiteSpace(text) ? "No Wi-Fi scan output was returned." : text.Trim();
            }
            catch (Exception ex)
            {
                _wifiNetworksText = string.IsNullOrWhiteSpace(ex.Message) ? "Wi-Fi scan failed." : ex.Message;
            }

            await PostStateAsync().ConfigureAwait(true);
        }

        private async Task RefreshAndPostStateAsync(string? action = null)
        {
            _isBusy = true;
            if (!string.IsNullOrWhiteSpace(action))
                _lastAction = action.Trim();
            RefreshNetworkSummary();
            await PostStateAsync().ConfigureAwait(true);

            try
            {
                var urls = GetInstalledAddonUrls();
                var installedTasks = urls.Select(BuildAddonBrowserItemAsync).ToList();
                _installedAddons = installedTasks.Count == 0
                    ? new List<AddonBrowserItem>()
                    : (await Task.WhenAll(installedTasks).ConfigureAwait(true)).Where(item => item != null).Cast<AddonBrowserItem>().OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase).ToList();
                _directoryAddons = await LoadRemoteAddonDirectoryAsync(urls).ConfigureAwait(true);

                _statusText = _installedAddons.Count == 0
                    ? "No addons installed yet. Paste a manifest URL or install directly from the directory below."
                    : $"{_installedAddons.Count} addon{(_installedAddons.Count == 1 ? "" : "s")} connected to Atlas.";
            }
            catch (Exception ex)
            {
                _statusText = string.IsNullOrWhiteSpace(ex.Message) ? "Failed to load addon state." : ex.Message;
            }
            finally
            {
                _isBusy = false;
                await PostStateAsync().ConfigureAwait(true);
            }
        }

        private async Task PostStateAsync()
        {
            try
            {
                if (AddonsWebView?.CoreWebView2 == null)
                    return;

                if (!_frontendReady)
                    return;

                var message = JsonSerializer.Serialize(new
                {
                    type = "addons.state",
                    installedAddons = _installedAddons.Select(MapAddon).ToList(),
                    directoryAddons = _directoryAddons.Select(MapAddon).ToList(),
                    installedCount = _installedAddons.Count,
                    statusText = _statusText,
                    networkSummary = _networkSummary,
                    wifiNetworksText = _wifiNetworksText,
                    isBusy = _isBusy,
                    lastAction = _lastAction,
                });

                AddonsWebView.CoreWebView2.PostWebMessageAsJson(message);

                try
                {
                    AtlasAI.Core.AppLogger.LogInfo($"[AddonNavTest] addons.state.posted=true installed={_installedAddons.Count}");
                    AtlasAI.Core.AppLogger.LogInfo($"[AddonNavTest] addons.state.posted.afterReady=true installed={_installedAddons.Count}");
                }
                catch
                {
                }
            }
            catch
            {
            }

            await Task.CompletedTask;
        }

        private static object MapAddon(AddonBrowserItem addon) => new
        {
            name = addon.Name,
            version = addon.Version,
            description = addon.Description,
            typeSummary = addon.TypeSummary,
            resourceSummary = addon.ResourceSummary,
            url = addon.Url,
            manifestUrl = addon.ManifestUrl,
            iconUrl = addon.IconUrl,
            host = addon.Host,
            directoryUrl = addon.DirectoryUrl,
            installManifestUrl = addon.InstallManifestUrl,
            websiteUrl = !string.IsNullOrWhiteSpace(addon.DirectoryUrl) ? addon.DirectoryUrl : addon.Url,
            configureUrl = !string.IsNullOrWhiteSpace(addon.DirectoryUrl) ? addon.DirectoryUrl : addon.Url,
            isInstalled = addon.IsInstalled,
            hasError = addon.HasError,
            errorText = addon.ErrorText,
        };

        private List<string> GetInstalledAddonUrls()
        {
            try
            {
                return _viewModel?.AddonServers
                    .Where(url => !string.IsNullOrWhiteSpace(url))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList() ?? new List<string>();
            }
            catch
            {
                return new List<string>();
            }
        }

        private async Task<AddonBrowserItem?> BuildAddonBrowserItemAsync(string url)
        {
            var normalizedUrl = NormalizeAddonUrl(url);
            if (string.IsNullOrWhiteSpace(normalizedUrl))
                return null;

            var host = TryGetAddonHost(normalizedUrl);
            var (success, manifest, error) = await _addonManifestService.FetchManifestAsync(normalizedUrl).ConfigureAwait(true);
            if (!success || manifest == null)
            {
                return new AddonBrowserItem
                {
                    Name = string.IsNullOrWhiteSpace(host) ? normalizedUrl : host,
                    Version = "Unavailable",
                    Description = string.IsNullOrWhiteSpace(error) ? "Manifest could not be loaded." : error,
                    TypeSummary = "Other",
                    ResourceSummary = "Manifest unavailable",
                    Url = normalizedUrl,
                    ManifestUrl = BuildManifestUrl(normalizedUrl),
                    Host = string.IsNullOrWhiteSpace(host) ? normalizedUrl : host,
                    IsInstalled = true,
                    HasError = true,
                    ErrorText = error ?? "",
                };
            }

            return new AddonBrowserItem
            {
                Name = string.IsNullOrWhiteSpace(manifest.Name) ? host : manifest.Name.Trim(),
                Version = string.IsNullOrWhiteSpace(manifest.Version) ? "v?" : $"v{manifest.Version.TrimStart('v', 'V')}",
                Description = string.IsNullOrWhiteSpace(manifest.Description) ? "No description provided." : manifest.Description.Trim(),
                TypeSummary = BuildAddonTypeSummary(manifest.Types),
                ResourceSummary = BuildAddonResourceSummary(manifest.Resources),
                Url = normalizedUrl,
                ManifestUrl = BuildManifestUrl(normalizedUrl),
                DirectoryUrl = BuildStremioAddonsDetailUrl(manifest.Name, host),
                InstallManifestUrl = BuildManifestUrl(normalizedUrl),
                IconUrl = ResolveAddonAssetUrl(normalizedUrl, manifest.Logo),
                Host = string.IsNullOrWhiteSpace(host) ? normalizedUrl : host,
                IsInstalled = true,
            };
        }

        private async Task<List<AddonBrowserItem>> LoadRemoteAddonDirectoryAsync(IReadOnlyCollection<string> installedUrls)
        {
            var installedSet = new HashSet<string>(installedUrls.Select(NormalizeAddonUrl).Where(url => !string.IsNullOrWhiteSpace(url)), StringComparer.OrdinalIgnoreCase);
            var json = await AddonDirectoryHttpClient.GetStringAsync(StremioAddonsCatalogUrl).ConfigureAwait(true);
            return ParseRemoteAddonDirectoryJson(json, installedSet);
        }

        private static List<AddonBrowserItem> ParseRemoteAddonDirectoryJson(string json, IReadOnlyCollection<string> installedUrls)
        {
            var items = new List<AddonBrowserItem>();
            if (string.IsNullOrWhiteSpace(json))
                return items;

            var installedSet = new HashSet<string>(installedUrls.Where(url => !string.IsNullOrWhiteSpace(url)), StringComparer.OrdinalIgnoreCase);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("addons", out var addons) || addons.ValueKind != JsonValueKind.Array)
                return items;

            foreach (var addon in addons.EnumerateArray())
            {
                var normalizedUrl = NormalizeAddonUrl(GetJsonString(addon, "transportUrl"));
                if (string.IsNullOrWhiteSpace(normalizedUrl) || !seen.Add(normalizedUrl))
                    continue;
                if (!addon.TryGetProperty("manifest", out var manifest) || manifest.ValueKind != JsonValueKind.Object)
                    continue;

                var host = TryGetAddonHost(normalizedUrl);
                var addonName = GetJsonString(manifest, "name");
                items.Add(new AddonBrowserItem
                {
                    Name = string.IsNullOrWhiteSpace(addonName) ? (string.IsNullOrWhiteSpace(host) ? normalizedUrl : host) : addonName.Trim(),
                    Version = string.IsNullOrWhiteSpace(GetJsonString(manifest, "version")) ? "v?" : $"v{GetJsonString(manifest, "version").TrimStart('v', 'V')}",
                    Description = string.IsNullOrWhiteSpace(GetJsonString(manifest, "description")) ? "Community addon directory entry." : GetJsonString(manifest, "description").Trim(),
                    TypeSummary = BuildAddonTypeSummary(GetJsonStringArray(manifest, "types")),
                    ResourceSummary = BuildAddonResourceSummary(GetJsonStringArray(manifest, "resources")),
                    Url = normalizedUrl,
                    ManifestUrl = BuildManifestUrl(normalizedUrl),
                    DirectoryUrl = BuildStremioAddonsDetailUrl(addonName, host),
                    InstallManifestUrl = BuildManifestUrl(normalizedUrl),
                    IconUrl = ResolveAddonAssetUrl(normalizedUrl, GetJsonString(manifest, "logo")),
                    Host = string.IsNullOrWhiteSpace(host) ? normalizedUrl : host,
                    IsInstalled = installedSet.Contains(normalizedUrl),
                });
            }

            return items.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private async Task<string> ResolveDirectoryInstallManifestUrlAsync(string detailUrl)
        {
            if (string.IsNullOrWhiteSpace(detailUrl))
                return "";

            var html = await AddonDirectoryHttpClient.GetStringAsync(detailUrl).ConfigureAwait(true);
            var match = Regex.Match(html, "\\\"manifestUrl\\\":\\\"(?<url>https:[^\\\"]+manifest\\.json)\\\"", RegexOptions.IgnoreCase);
            return match.Success ? WebUtility.HtmlDecode(Regex.Unescape(match.Groups["url"].Value)) : "";
        }

        private void RefreshNetworkSummary()
        {
            try
            {
                var adapters = NetworkInterface.GetAllNetworkInterfaces()
                    .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                    .ToList();

                var active = adapters.FirstOrDefault();
                if (active == null)
                {
                    _networkSummary = "No active network adapters found.";
                    return;
                }

                var props = active.GetIPProperties();
                var ipv4 = props.UnicastAddresses.FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork)?.Address.ToString() ?? "Unknown";
                var gateway = props.GatewayAddresses.FirstOrDefault(g => g.Address.AddressFamily == AddressFamily.InterNetwork)?.Address.ToString() ?? "Unknown";
                var dns = props.DnsAddresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork)?.ToString() ?? "Unknown";
                _networkSummary = $"Adapter: {active.Name}\nType: {active.NetworkInterfaceType}\nIPv4: {ipv4}\nGateway: {gateway}\nDNS: {dns}";
            }
            catch (Exception ex)
            {
                _networkSummary = string.IsNullOrWhiteSpace(ex.Message) ? "Unable to read network status." : ex.Message;
            }
        }

        private static HttpClient CreateAddonDirectoryHttpClient()
        {
            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            try
            {
                client.DefaultRequestHeaders.UserAgent.ParseAdd("AtlasAI/1.0");
                client.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/xhtml+xml,application/json;q=0.9,*/*;q=0.8");
            }
            catch
            {
            }

            return client;
        }

        private static string GetPayloadString(JsonElement payload, string propertyName)
        {
            if (payload.ValueKind != JsonValueKind.Object || !payload.TryGetProperty(propertyName, out var property))
                return "";

            return property.ValueKind == JsonValueKind.String ? property.GetString() ?? "" : property.ToString();
        }

        private static string GetJsonString(JsonElement element, string propertyName)
        {
            if (!element.TryGetProperty(propertyName, out var property))
                return "";
            return property.ValueKind == JsonValueKind.String ? property.GetString() ?? "" : property.ToString();
        }

        private static IEnumerable<string> GetJsonStringArray(JsonElement element, string propertyName)
        {
            if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.Array)
                return Array.Empty<string>();

            var values = new List<string>();
            foreach (var entry in property.EnumerateArray())
            {
                if (entry.ValueKind == JsonValueKind.String)
                {
                    var value = entry.GetString();
                    if (!string.IsNullOrWhiteSpace(value))
                        values.Add(value.Trim());
                }
                else if (entry.ValueKind == JsonValueKind.Object)
                {
                    var value = GetJsonString(entry, "name");
                    if (!string.IsNullOrWhiteSpace(value))
                        values.Add(value.Trim());
                }
            }

            return values;
        }

        private static string NormalizeAddonUrl(string input)
        {
            var value = (input ?? "").Trim();
            if (string.IsNullOrWhiteSpace(value))
                return "";

            while (value.Length >= 2)
            {
                var first = value[0];
                var last = value[^1];
                if ((first == '"' && last == '"') || (first == '\'' && last == '\'') || (first == '<' && last == '>'))
                {
                    value = value.Substring(1, value.Length - 2).Trim();
                    continue;
                }

                break;
            }

            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
            {
                if (!Uri.TryCreate("https://" + value.TrimStart('/'), UriKind.Absolute, out uri))
                    return "";
            }

            if (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                return "";
            }

            var builder = new UriBuilder(uri) { Fragment = "" };
            if (builder.Path.EndsWith("/manifest.json", StringComparison.OrdinalIgnoreCase))
                builder.Path = builder.Path.Substring(0, builder.Path.Length - "/manifest.json".Length);

            return builder.Uri.ToString().TrimEnd('/');
        }

        private static string BuildManifestUrl(string baseUrl)
        {
            var normalized = NormalizeAddonUrl(baseUrl);
            return string.IsNullOrWhiteSpace(normalized) ? "" : normalized.TrimEnd('/') + "/manifest.json";
        }

        private static string TryGetAddonHost(string url)
        {
            try
            {
                return Uri.TryCreate(url, UriKind.Absolute, out var uri) ? (uri.Host ?? "").Trim() : "";
            }
            catch
            {
                return "";
            }
        }

        private static string ResolveAddonAssetUrl(string baseUrl, string assetUrl)
        {
            if (string.IsNullOrWhiteSpace(assetUrl))
                return "";
            try
            {
                if (Uri.TryCreate(assetUrl.Trim(), UriKind.Absolute, out var absolute))
                    return absolute.ToString();
                var normalized = NormalizeAddonUrl(baseUrl);
                if (!Uri.TryCreate(normalized.EndsWith("/") ? normalized : normalized + "/", UriKind.Absolute, out var baseUri))
                    return "";
                return new Uri(baseUri, assetUrl.TrimStart('/')).ToString();
            }
            catch
            {
                return "";
            }
        }

        private static string BuildStremioAddonsDetailUrl(string addonName, string host)
        {
            var slug = BuildDirectorySlug(addonName);
            if (string.IsNullOrWhiteSpace(slug))
                slug = BuildDirectorySlug(host);

            return string.IsNullOrWhiteSpace(slug)
                ? ""
                : $"https://stremio-addons.net/addons/{slug}";
        }

        private static string BuildDirectorySlug(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "";

            var normalized = value.Trim().Normalize(NormalizationForm.FormD);
            var builder = new System.Text.StringBuilder(normalized.Length);
            var lastWasDash = false;

            foreach (var ch in normalized)
            {
                var category = CharUnicodeInfo.GetUnicodeCategory(ch);
                if (category == UnicodeCategory.NonSpacingMark)
                    continue;

                var lower = char.ToLowerInvariant(ch);
                if (char.IsLetterOrDigit(lower) || lower == '+' || lower == '(' || lower == ')' || lower == '.')
                {
                    builder.Append(lower);
                    lastWasDash = false;
                    continue;
                }

                if (char.IsWhiteSpace(lower) || lower == '-' || lower == '_' || lower == '|' || lower == '/' || lower == '\\' || lower == ':')
                {
                    if (!lastWasDash && builder.Length > 0)
                    {
                        builder.Append('-');
                        lastWasDash = true;
                    }
                }
            }

            return builder
                .ToString()
                .Trim('-');
        }

        private static string BuildAddonTypeSummary(IEnumerable<string>? types)
        {
            var normalized = (types ?? Array.Empty<string>())
                .Where(type => !string.IsNullOrWhiteSpace(type))
                .Select(type => type.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (normalized.Count == 0)
                return "Other";
            return string.Join(" & ", normalized.Select(type => char.ToUpperInvariant(type[0]) + type.Substring(1)));
        }

        private static string BuildAddonResourceSummary(IEnumerable<string>? resources)
        {
            var normalized = (resources ?? Array.Empty<string>())
                .Where(resource => !string.IsNullOrWhiteSpace(resource))
                .Select(resource => resource.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            return normalized.Count == 0 ? "Manifest only" : string.Join(" • ", normalized);
        }

        private static long GetNewestDistWriteTicks(string dist)
        {
            long newestWriteTicks = 0;

            try
            {
                var indexPath = Path.Combine(dist, "index.html");
                if (File.Exists(indexPath))
                    newestWriteTicks = Math.Max(newestWriteTicks, File.GetLastWriteTimeUtc(indexPath).Ticks);

                var assetsDir = Path.Combine(dist, "assets");
                if (Directory.Exists(assetsDir))
                {
                    foreach (var filePath in Directory.EnumerateFiles(assetsDir, "*", SearchOption.TopDirectoryOnly))
                    {
                        try
                        {
                            newestWriteTicks = Math.Max(newestWriteTicks, File.GetLastWriteTimeUtc(filePath).Ticks);
                        }
                        catch
                        {
                        }
                    }
                }
            }
            catch
            {
            }

            return newestWriteTicks != 0 ? newestWriteTicks : DateTime.UtcNow.Ticks;
        }

        private static async Task<CoreWebView2Environment> CreateAddonsWebViewEnvironmentAsync()
        {
            var candidates = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AtlasAI", "webview_addons"),
                Path.Combine(Path.GetTempPath(), "AtlasOS_WebView2", "Addons"),
            };

            Exception? lastError = null;
            foreach (var folder in candidates)
            {
                try
                {
                    Directory.CreateDirectory(folder);
                    return await CoreWebView2Environment.CreateAsync(null, folder);
                }
                catch (Exception ex)
                {
                    lastError = ex;
                }
            }

            throw lastError ?? new InvalidOperationException("Unable to create a WebView2 environment for AddonsView.");
        }

        private static string? FindAddonManagerDist()
        {
            const string addonManagerFolder = "Addon Manager";

            try
            {
                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                var shipped = Path.Combine(baseDir, "Figma", addonManagerFolder, "dist");
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
                Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? string.Empty,
            };

            foreach (var root in roots)
            {
                try
                {
                    var dir = new DirectoryInfo(root);
                    for (var i = 0; i < 10 && dir != null; i++)
                    {
                        var figmaRoot = Path.Combine(dir.FullName, "Figma");
                        if (Directory.Exists(figmaRoot))
                        {
                            var uiFolder = Directory.GetDirectories(figmaRoot, addonManagerFolder, SearchOption.TopDirectoryOnly)
                                .FirstOrDefault();
                            if (!string.IsNullOrWhiteSpace(uiFolder))
                            {
                                var dist = Path.Combine(uiFolder, "dist");
                                if (Directory.Exists(dist) && File.Exists(Path.Combine(dist, "index.html")))
                                    return dist;
                            }
                        }

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