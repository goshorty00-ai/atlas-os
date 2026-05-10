using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using AtlasAI.Services;
using AtlasAI.Views.ViewModels;
using Microsoft.Web.WebView2.Core;

namespace AtlasAI.Views.MediaCentre
{
    public partial class DiscoveryView : UserControl
    {
        private readonly DiscoveryService _discoveryService = new();
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };

        public DiscoveryView()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            try
            {
                await EnsureInitializedAsync();
                
                // Set DataContext to MediaCentreViewModel if available
                if (DataContext == null)
                {
                    var window = Window.GetWindow(this);
                    if (window?.DataContext is MediaCentreViewModel vm)
                    {
                        DataContext = vm;
                    }
                }
            }
            catch
            {
            }
        }

        private async Task EnsureInitializedAsync()
        {
            if (DiscoveryFigmaWebView?.CoreWebView2 != null)
                return;

            var userDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AtlasAI", "WebView2", "Discovery");
            var env = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
            await DiscoveryFigmaWebView.EnsureCoreWebView2Async(env);

            var settings = DiscoveryFigmaWebView.CoreWebView2.Settings;
            settings.AreDefaultContextMenusEnabled = true;
            settings.AreDevToolsEnabled = true;
            settings.AreBrowserAcceleratorKeysEnabled = true;

            // Listen for messages from React app
            DiscoveryFigmaWebView.CoreWebView2.WebMessageReceived += CoreWebView2_WebMessageReceived;

            var dist = FindFigmaDist();
            if (string.IsNullOrWhiteSpace(dist))
            {
                ShowUnavailableState("The discovery interface assets are not installed in this build.");
                return;
            }

            ShowWebView();

            // Clear cache to ensure fresh content
            try
            {
                await DiscoveryFigmaWebView.CoreWebView2.Profile.ClearBrowsingDataAsync();
            }
            catch { }

            DiscoveryFigmaWebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                "discovery-ui",
                dist,
                CoreWebView2HostResourceAccessKind.Allow);

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

            var version = (indexWriteTicks != 0 ? indexWriteTicks : DateTime.UtcNow.Ticks).ToString();
            DiscoveryFigmaWebView.CoreWebView2.Navigate($"https://discovery-ui/index.html?v={version}");
            
            // Wait a moment for the page to load, then send initial data
            await Task.Delay(1000);
            await LoadDiscoveryDataAsync();
        }

        private async void CoreWebView2_WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                var json = e.WebMessageAsJson;
                System.Diagnostics.Debug.WriteLine($"[DiscoveryView] Received message: {json}");
                if (string.IsNullOrWhiteSpace(json))
                    return;

                using var document = JsonDocument.Parse(json);
                var root = document.RootElement;
                if (!root.TryGetProperty("type", out var typeElement))
                    return;

                var type = typeElement.GetString() ?? string.Empty;
                var payload = root.TryGetProperty("payload", out var payloadElement) ? payloadElement : default;
                System.Diagnostics.Debug.WriteLine($"[DiscoveryView] Message type: {type}");

                switch (type)
                {
                    case "discovery.getData":
                        System.Diagnostics.Debug.WriteLine("[DiscoveryView] Loading discovery data...");
                        await LoadDiscoveryDataAsync();
                        break;
                    case "discovery.search":
                        // Handle search
                        break;
                    case "discovery.getDetails":
                        // Handle get details
                        break;
                    case "mediahub.openExternalUrl":
                        try
                        {
                            var url = payload.TryGetProperty("url", out var urlElement) && urlElement.ValueKind == JsonValueKind.String
                                ? (urlElement.GetString() ?? string.Empty).Trim()
                                : string.Empty;
                            OpenInAtlasBrowser(url);
                        }
                        catch
                        {
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DiscoveryView] Error handling message: {ex.Message}");
            }
        }

        private async Task LoadDiscoveryDataAsync()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("[DiscoveryView] LoadDiscoveryDataAsync started");
                
                // Get Trakt credentials from MediaCentreViewModel if available
                var traktClientId = "";
                var traktToken = "";
                
                try
                {
                    if (DataContext is MediaCentreViewModel vm)
                    {
                        traktClientId = vm.TraktClientId;
                        traktToken = vm.TraktToken;
                        System.Diagnostics.Debug.WriteLine($"[DiscoveryView] Got Trakt credentials from VM");
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[DiscoveryView] Error getting Trakt credentials: {ex.Message}");
                }

                System.Diagnostics.Debug.WriteLine("[DiscoveryView] Calling DiscoveryService...");
                var data = await _discoveryService.GetDiscoveryDataAsync(traktClientId, traktToken, CancellationToken.None);
                System.Diagnostics.Debug.WriteLine($"[DiscoveryView] Got data: Trending={data?.Trending?.Count ?? 0}, Trailers={data?.Trailers?.Count ?? 0}, News={data?.News?.Count ?? 0}");
                
                // The live discovery surface must not present mock/demo media as real.
                if (data == null || !string.IsNullOrWhiteSpace(data.Error) || 
                    (data.Trending.Count == 0 && data.Trailers.Count == 0 && data.News.Count == 0))
                {
                    var message = !string.IsNullOrWhiteSpace(data?.Error)
                        ? $"Live discovery data could not be loaded: {data.Error}"
                        : "No live discovery items are available right now.";
                    System.Diagnostics.Debug.WriteLine($"[DiscoveryView] Discovery unavailable: {message}");
                    ShowUnavailableState(message);
                    return;
                }
                
                ShowWebView();
                System.Diagnostics.Debug.WriteLine("[DiscoveryView] Posting data to React...");
                await PostToReactAsync("discovery.data", data);
                System.Diagnostics.Debug.WriteLine("[DiscoveryView] Data posted successfully");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DiscoveryView] Error in LoadDiscoveryDataAsync: {ex.Message}");
                ShowUnavailableState($"Live discovery data failed to load: {ex.Message}");
            }
        }

        private void ShowUnavailableState(string message)
        {
            Dispatcher.Invoke(() =>
            {
                if (UnavailableSummaryText != null)
                    UnavailableSummaryText.Text = string.IsNullOrWhiteSpace(message)
                        ? "Live discovery data is not available."
                        : message;

                if (UnavailableOverlay != null)
                    UnavailableOverlay.Visibility = Visibility.Visible;

                if (DiscoveryFigmaWebView != null)
                    DiscoveryFigmaWebView.Visibility = Visibility.Collapsed;
            });
        }

        private void ShowWebView()
        {
            Dispatcher.Invoke(() =>
            {
                if (UnavailableOverlay != null)
                    UnavailableOverlay.Visibility = Visibility.Collapsed;

                if (DiscoveryFigmaWebView != null)
                    DiscoveryFigmaWebView.Visibility = Visibility.Visible;
            });
        }

        private async void RetryDiscovery_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await LoadDiscoveryDataAsync();
            }
            catch (Exception ex)
            {
                ShowUnavailableState($"Live discovery data failed to load: {ex.Message}");
            }
        }

        private Task PostToReactAsync(string type, object payload)
        {
            try
            {
                if (DiscoveryFigmaWebView?.CoreWebView2 == null)
                {
                    System.Diagnostics.Debug.WriteLine("[DiscoveryView] Cannot post - WebView2 not initialized");
                    return Task.CompletedTask;
                }

                var message = JsonSerializer.Serialize(new { type, payload }, JsonOptions);
                System.Diagnostics.Debug.WriteLine($"[DiscoveryView] Posting message to React: {type}");
                Dispatcher.Invoke(() =>
                {
                    try 
                    { 
                        DiscoveryFigmaWebView.CoreWebView2?.PostWebMessageAsJson(message);
                        System.Diagnostics.Debug.WriteLine("[DiscoveryView] Message posted successfully");
                    } 
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[DiscoveryView] Error posting message: {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DiscoveryView] Error in PostToReactAsync: {ex.Message}");
            }

            return Task.CompletedTask;
        }

        private void OpenInAtlasBrowser(string url)
        {
            if (!IsHttpOrHttpsUrl(url))
                return;

            try
            {
                if (DataContext is MediaCentreViewModel vm)
                {
                    vm.NavigateToAppsBrowserUrl(url);
                    return;
                }
            }
            catch
            {
            }

            try
            {
                var vm = MediaCentreViewModel.Instance;
                if (vm != null)
                {
                    vm.NavigateToAppsBrowserUrl(url);
                    return;
                }
            }
            catch
            {
            }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true,
                });
            }
            catch
            {
            }
        }

        private static bool IsHttpOrHttpsUrl(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            return Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) &&
                   (string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase));
        }

        private static string? FindFigmaDist()
        {
            var candidates = new[]
            {
                Path.Combine(AppContext.BaseDirectory, "Figma", "Media Streamer", "dist"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Figma", "Media Streamer", "dist"),
                Path.Combine(Directory.GetCurrentDirectory(), "Figma", "Media Streamer", "dist"),
                Path.Combine(Directory.GetCurrentDirectory(), "Figma", "Mediahub", "dist"),
                Path.Combine("D:\\Atlas.OS", "Figma", "Futuristic Discovery Section Design (1)", "dist"),
                Path.Combine(AppContext.BaseDirectory, "Figma", "Futuristic Discovery Section Design (1)", "dist"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Figma", "Futuristic Discovery Section Design (1)", "dist"),
                Path.Combine(Directory.GetCurrentDirectory(), "Figma", "Futuristic Discovery Section Design (1)", "dist"),
            };

            foreach (var candidate in candidates)
            {
                try
                {
                    if (File.Exists(Path.Combine(candidate, "index.html")))
                        return candidate;
                }
                catch
                {
                }
            }

            return null;
        }
    }
}
