using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using AtlasAI.Brain;
using Microsoft.Web.WebView2.Core;

namespace AtlasAI.Modules.Internet
{
    public partial class InternetHostView : UserControl
    {
        private const string HomeUrl = "https://www.google.com";
        private const string BookmarksFile = "atlas_browser_bookmarks.json";

        private bool _aiPanelOpen = false;
        private bool _isNavigating = false;
        private List<BookmarkEntry> _bookmarks = new();
        private BrowserPageSnapshot? _currentPageSnapshot;
        private readonly bool _internetMicWired = SectionSpeechMicStandard.IsMicWired("Internet");
        private DispatcherTimer? _micNoteTimer;
        private TextBlock? _activeMicNoteTarget;

        // Bookmark model
        private record BookmarkEntry(string Url, string Title = "");

        // Tab model
        private record BrowserTab(string Title, string Url, string ProfileId);
        private record BrowserTabState(int Index, string Title, string Url, bool IsActive);
        private record BrowserPageSnapshot(
            string Url,
            string Title,
            string SelectedText,
            string VisibleText,
            List<string> TopLinks,
            List<string> MediaLinks,
            List<string> Emails,
            int ActiveTabIndex,
            List<BrowserTabState> Tabs,
            DateTime CapturedUtc);

        private sealed class BrowserPageSnapshotPayload
        {
            public string? Url { get; set; }
            public string? Title { get; set; }
            public string? SelectedText { get; set; }
            public string? VisibleText { get; set; }
            public List<string>? TopLinks { get; set; }
            public List<string>? MediaLinks { get; set; }
            public List<string>? Emails { get; set; }
        }

        private readonly List<BrowserTab> _tabs = new();
        private readonly List<Microsoft.Web.WebView2.Wpf.WebView2> _tabWebViews = new();
        private Panel? _webViewHost;
        private int _activeTab = 0;

        private string GetActiveTabProfileId()
        {
            if (_activeTab >= 0 && _activeTab < _tabs.Count)
                return _tabs[_activeTab].ProfileId ?? _activeProfileId;
            return _activeProfileId;
        }

        // Step 2: create a fresh WebView2 for a new tab, insert into the host Grid
        // at the same z-order position as BrowserWebView (index 0, beneath LoadingOverlay),
        // initialize it with a per-profile environment, and attach the standard handlers.
        private async Task<Microsoft.Web.WebView2.Wpf.WebView2> CreateTabWebViewAsync(string profileId)
        {
            var wv = new Microsoft.Web.WebView2.Wpf.WebView2 { Visibility = Visibility.Collapsed };
            if (_webViewHost != null)
            {
                // Insert at the front so LoadingOverlay (added later in XAML) stays on top.
                _webViewHost.Children.Insert(0, wv);
            }

            var userDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AtlasOS", "BrowserHub", "Profiles", profileId);
            Directory.CreateDirectory(userDataFolder);

            var env = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
            await wv.EnsureCoreWebView2Async(env);

            wv.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
            wv.CoreWebView2.Settings.AreDevToolsEnabled = true;
            wv.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = true;
            wv.CoreWebView2.Settings.IsStatusBarEnabled = false;

            wv.CoreWebView2.DownloadStarting += CoreWebView2_DownloadStarting;
            wv.CoreWebView2.NavigationStarting += CoreWebView2_NavigationStarting;
            wv.CoreWebView2.NavigationCompleted += CoreWebView2_NavigationCompleted;
            wv.CoreWebView2.DocumentTitleChanged += CoreWebView2_DocumentTitleChanged;

            return wv;
        }

        // Step 2: append a new tab with its own WebView2, switch to it, then navigate that
        // WebView2 to the requested url.
        private async Task OpenNewTabAsync(string url, string profileId)
        {
            try
            {
                var wv = await CreateTabWebViewAsync(profileId);
                _tabs.Add(new BrowserTab("New Tab", url, profileId));
                _tabWebViews.Add(wv);
                SwitchTab(_tabs.Count - 1);
                Navigate(url);
            }
            catch (Exception ex)
            {
                LogProfile($"OpenNewTabAsync FAILED url={url} profileId={profileId} ex={ex.Message}");
            }
        }

        // Step 3: profile card click — switch to an existing tab for the profile if one exists,
        // otherwise open a new tab in that profile. Always close the picker.
        private void OpenOrSwitchToProfileTab(string profileId)
        {
            try
            {
                LogProfile($"OpenOrSwitchToProfileTab id={profileId} tabCount={_tabs.Count}");
                ProfilePickerPopup.IsOpen = false;

                // Prefer the most recent tab for that profile.
                int existing = -1;
                for (int i = _tabs.Count - 1; i >= 0; i--)
                {
                    if (string.Equals(_tabs[i].ProfileId, profileId, StringComparison.OrdinalIgnoreCase))
                    {
                        existing = i;
                        break;
                    }
                }

                if (existing >= 0)
                {
                    SwitchTab(existing);
                    return;
                }

                _ = OpenNewTabAsync(HomeUrl, profileId);
            }
            catch (Exception ex)
            {
                LogProfile($"OpenOrSwitchToProfileTab FAILED id={profileId} ex={ex.Message}");
            }
        }

        public InternetHostView()
        {
            InitializeComponent();
            Loaded += InternetHostView_Loaded;
            Unloaded += InternetHostView_Unloaded;
            try
            {
                ProfilePickerPopup.Opened += (s, ev) => LogProfile($"VIS ProfilePickerPopup -> Opened (IsOpen={ProfilePickerPopup.IsOpen})");
                ProfilePickerPopup.Closed += (s, ev) => LogProfile($"VIS ProfilePickerPopup -> Closed (IsOpen={ProfilePickerPopup.IsOpen})");
                AddProfilePopup.Opened    += (s, ev) => LogProfile($"VIS AddProfilePopup    -> Opened (IsOpen={AddProfilePopup.IsOpen})");
                AddProfilePopup.Closed    += (s, ev) => LogProfile($"VIS AddProfilePopup    -> Closed (IsOpen={AddProfilePopup.IsOpen})");
                LoadingOverlay.IsVisibleChanged += (s, ev) => LogProfile($"VIS LoadingOverlay      -> {LoadingOverlay.Visibility} (IsVisible={LoadingOverlay.IsVisible})");
            }
            catch { }
        }

        private async void InternetHostView_Loaded(object sender, RoutedEventArgs e)
        {
            LogProfile($"Loaded ENTER pickerOpen={ProfilePickerPopup.IsOpen} addOpen={AddProfilePopup.IsOpen} loading={LoadingOverlay.Visibility}");
            LoadBookmarks();
            LoadProfiles();
            LogProfile($"Loaded after LoadProfiles activeId={_activeProfileId} count={_profiles.Count} pickerOpen={ProfilePickerPopup.IsOpen} addOpen={AddProfilePopup.IsOpen}");
            try
            {
                await InitBrowserAsync();
            }
            catch (Exception ex)
            {
                LoadingOverlay.Visibility = Visibility.Visible;
                var sp = LoadingOverlay.Child as StackPanel;
                if (sp != null)
                {
                    sp.Children.Clear();
                    sp.Children.Add(new TextBlock
                    {
                        Text = "Browser failed to initialize.",
                        Foreground = Brushes.OrangeRed, FontSize = 15,
                        TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(0, 0, 0, 8)
                    });
                    sp.Children.Add(new TextBlock
                    {
                        Text = ex.Message,
                        Foreground = Brushes.Gray, FontSize = 12,
                        TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap
                    });
                }
            }
            LogProfile($"Loaded EXIT pickerOpen={ProfilePickerPopup.IsOpen} addOpen={AddProfilePopup.IsOpen} loading={LoadingOverlay.Visibility}");
        }

        private void InternetHostView_Unloaded(object sender, RoutedEventArgs e)
        {
            try
            {
                if (BrowserWebView?.CoreWebView2 != null)
                {
                    BrowserWebView.CoreWebView2.DownloadStarting -= CoreWebView2_DownloadStarting;
                    BrowserWebView.CoreWebView2.NavigationStarting -= CoreWebView2_NavigationStarting;
                    BrowserWebView.CoreWebView2.NavigationCompleted -= CoreWebView2_NavigationCompleted;
                    BrowserWebView.CoreWebView2.DocumentTitleChanged -= CoreWebView2_DocumentTitleChanged;
                }
            }
            catch { }
        }

        private async Task InitBrowserAsync()
        {
            LogProfile($"InitBrowserAsync ENTER activeId={_activeProfileId} pickerOpen={ProfilePickerPopup.IsOpen} addOpen={AddProfilePopup.IsOpen} loading={LoadingOverlay.Visibility}");
            var userDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AtlasOS", "BrowserHub", "Profiles", _activeProfileId);
            Directory.CreateDirectory(userDataFolder);

            var env = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
            await BrowserWebView.EnsureCoreWebView2Async(env);

            BrowserWebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
            BrowserWebView.CoreWebView2.Settings.AreDevToolsEnabled = true;
            BrowserWebView.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = true;
            BrowserWebView.CoreWebView2.Settings.IsStatusBarEnabled = false;

            BrowserWebView.CoreWebView2.DownloadStarting += CoreWebView2_DownloadStarting;
            BrowserWebView.CoreWebView2.NavigationStarting += CoreWebView2_NavigationStarting;
            BrowserWebView.CoreWebView2.NavigationCompleted += CoreWebView2_NavigationCompleted;
            BrowserWebView.CoreWebView2.DocumentTitleChanged += CoreWebView2_DocumentTitleChanged;

            LoadingOverlay.Visibility = Visibility.Collapsed;
            BrowserWebView.Visibility = Visibility.Visible;

            try { LogProfile($"InitBrowserAsync DONE activeId={_activeProfileId} userDataFolder={userDataFolder} initialHash={System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(BrowserWebView)}"); } catch {}

            // Step 2: register the XAML BrowserWebView as the WebView for tab[0].
            _webViewHost = BrowserWebView.Parent as Panel;
            _tabWebViews.Clear();
            _tabWebViews.Add(BrowserWebView);

            // Add initial tab
            _tabs.Add(new BrowserTab("New Tab", HomeUrl, _activeProfileId));
            RefreshTabBar();
            Navigate(HomeUrl);
        }

        // ─── Navigation ───────────────────────────────────────────────────────

        private void Navigate(string url)
        {
            if (BrowserWebView.CoreWebView2 == null) return;

            if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                url = (url.Contains('.') && !url.Contains(' '))
                    ? "https://" + url
                    : "https://www.google.com/search?q=" + Uri.EscapeDataString(url);
            }

            BrowserWebView.CoreWebView2.Navigate(url);
        }

        private void CoreWebView2_NavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
        {
            _isNavigating = true;
            Dispatcher.Invoke(() =>
            {
                if (_currentPageSnapshot != null &&
                    !string.Equals(_currentPageSnapshot.Url, e.Uri, StringComparison.OrdinalIgnoreCase))
                {
                    _currentPageSnapshot = null;
                }

                AddressBar.Text = e.Uri;
                RefreshIcon.Text = "✕";
                RefreshIcon.Foreground = Brushes.OrangeRed;
                SecurityIcon.Foreground = e.Uri.StartsWith("https", StringComparison.OrdinalIgnoreCase)
                    ? new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50))
                    : new SolidColorBrush(Color.FromRgb(0xFF, 0xA0, 0x00));
                // Update AI panel
                if (_aiPanelOpen) UpdateAiPanel(e.Uri, "Navigating...");
            });
        }

        private void CoreWebView2_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            _isNavigating = false;
            Dispatcher.Invoke(() =>
            {
                RefreshIcon.Text = "⟳";
                RefreshIcon.Foreground = new SolidColorBrush(Color.FromRgb(0x97, 0xA7, 0xBA));
                var src = BrowserWebView.CoreWebView2?.Source ?? "";
                try { LogProfile($"NavCompleted activeId={_activeProfileId} fieldHash={System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(BrowserWebView)} url={src}"); } catch {}
                AddressBar.Text = src;

                if (_activeTab >= 0 && _activeTab < _tabs.Count)
                {
                    var docTitle = BrowserWebView.CoreWebView2?.DocumentTitle ?? "";
                    _tabs[_activeTab] = new BrowserTab(ResolveChromeTitle(src, docTitle), src, GetActiveTabProfileId());
                    RefreshTabBar();
                }

                SecurityIcon.Foreground = src.StartsWith("https", StringComparison.OrdinalIgnoreCase)
                    ? new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50))
                    : new SolidColorBrush(Color.FromRgb(0xFF, 0xA0, 0x00));
                // Update bookmark star
                var isBookmarked = _bookmarks.Any(b => b.Url == src);
                BookmarkIcon.Text = isBookmarked ? "★" : "☆";
                BookmarkIcon.Foreground = isBookmarked
                    ? new SolidColorBrush(Color.FromRgb(0xFF, 0xD7, 0x00))
                    : new SolidColorBrush(Color.FromRgb(0x97, 0xA7, 0xBA));
                if (_aiPanelOpen) UpdateAiPanel(src, "Ready");
                _ = GetCurrentPageSnapshotAsync(true);
            });
        }

        private void CoreWebView2_DocumentTitleChanged(object? sender, object e)
        {
            Dispatcher.Invoke(() =>
            {
                var url = BrowserWebView.CoreWebView2?.Source ?? "";
                var title = ResolveChromeTitle(url, BrowserWebView.CoreWebView2?.DocumentTitle ?? "");
                if (_activeTab >= 0 && _activeTab < _tabs.Count)
                {
                    _tabs[_activeTab] = new BrowserTab(title, url, GetActiveTabProfileId());
                    RefreshTabBar();
                }
            });
        }

        private async void CoreWebView2_DownloadStarting(object? sender, CoreWebView2DownloadStartingEventArgs e)
        {
            var downloadUri = (e.DownloadOperation?.Uri ?? "").Trim();
            var suggestedFileName = "";
            try
            {
                suggestedFileName = Path.GetFileName((e.DownloadOperation?.ResultFilePath ?? e.ResultFilePath ?? "").Trim());
            }
            catch
            {
                suggestedFileName = "";
            }
            var resultFilePath = (e.ResultFilePath ?? "").Trim();
            ulong? totalBytesToReceive = null;
            try { totalBytesToReceive = e.DownloadOperation?.TotalBytesToReceive; } catch { }
            var totalBytesToReceiveText = totalBytesToReceive.HasValue ? totalBytesToReceive.Value.ToString() : "unknown";

            var sourceHost = "unknown";
            try
            {
                if (Uri.TryCreate(downloadUri, UriKind.Absolute, out var srcUri))
                    sourceHost = string.IsNullOrWhiteSpace(srcUri.Host) ? "unknown" : srcUri.Host;
            }
            catch { }

            var fileName = string.IsNullOrWhiteSpace(suggestedFileName)
                ? (Uri.TryCreate(downloadUri, UriKind.Absolute, out var fileUri) ? Path.GetFileName(fileUri.LocalPath) : "")
                : suggestedFileName;
            if (string.IsNullOrWhiteSpace(fileName)) fileName = "unknown";

            var ext = Path.GetExtension(fileName)?.Trim().ToLowerInvariant() ?? "";
            if (string.IsNullOrWhiteSpace(ext)) ext = "(none)";

            var interceptEnabled =
                !string.IsNullOrWhiteSpace(downloadUri) &&
                (downloadUri.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                 downloadUri.StartsWith("https://", StringComparison.OrdinalIgnoreCase));

            System.Diagnostics.Debug.WriteLine(
                $"[BrowserHub.DownloadStarting] Uri={downloadUri}; SuggestedFileName={suggestedFileName}; ResultFilePath={resultFilePath}; TotalBytesToReceive={totalBytesToReceiveText}; Extension={ext}; SourceHost={sourceHost}; AtlasInterceptEnabled={interceptEnabled}");

            if (!interceptEnabled)
            {
                System.Diagnostics.Debug.WriteLine("[BrowserHub.DownloadStarting] enqueue attempted=false; enqueue success/failure=not-attempted; native cancel true/false=false");
                System.Diagnostics.Debug.WriteLine(
                    $"[BrowserHub.DownloadStarting] Missing/invalid metadata for Atlas enqueue. hasUri={!string.IsNullOrWhiteSpace(downloadUri)}; hasSuggestedFileName={!string.IsNullOrWhiteSpace(suggestedFileName)}; hasResultFilePath={!string.IsNullOrWhiteSpace(resultFilePath)}. Native WebView2 download will continue.");
                return;
            }

            // Cancel native browser handling first; Atlas advisory/confirmation owns this flow.
            e.Cancel = true;
            System.Diagnostics.Debug.WriteLine("[BrowserHub.DownloadStarting] native cancel true/false=true (pre-advisory)");

            var sizeText = totalBytesToReceive.HasValue
                ? $"{Math.Max(0d, totalBytesToReceive.Value / 1024d / 1024d):0.##} MB"
                : "unknown";
            var (riskLevel, riskExplanation) = ClassifyDownloadRisk(ext, sourceHost);

            bool userConfirmed = false;
            try
            {
                var advisory = new global::AtlasAI.DownloadAdvisoryWindow(
                    fileName, ext, sourceHost, sizeText, riskLevel, riskExplanation);
                userConfirmed = advisory.ShowDialog() == true;
            }
            catch (Exception advisoryEx)
            {
                System.Diagnostics.Debug.WriteLine($"[BrowserHub.DownloadStarting] Advisory window failed: {advisoryEx.Message}. Download not started.");
            }

            if (!userConfirmed)
            {
                System.Diagnostics.Debug.WriteLine("[BrowserHub.DownloadStarting] User cancelled advisory. enqueue attempted=false; enqueue success/failure=not-attempted; native cancel true/false=true");
                global::AtlasAI.UI.ToastNotificationManager.Instance.Show(
                    "Download cancelled.",
                    global::AtlasAI.UI.ToastType.Info,
                    2800);
                return;
            }

            try
            {
                System.Diagnostics.Debug.WriteLine("[BrowserHub.DownloadStarting] enqueue attempted=true");
                await global::AtlasAI.Services.DownloadService.Instance.AddDownloadAsync(downloadUri);

                System.Diagnostics.Debug.WriteLine("[BrowserHub.DownloadStarting] enqueue success/failure=success; native cancel true/false=true");

                var routed = TryRouteToAtlasSection("AI DOWNLOADS");
                global::AtlasAI.UI.ToastNotificationManager.Instance.Show(
                    routed
                        ? "Download queued in Atlas Downloader."
                        : "Download queued in Atlas Downloader. Open Downloads to view progress.",
                    global::AtlasAI.UI.ToastType.Success,
                    3200);

                System.Diagnostics.Debug.WriteLine("[BrowserHub.DownloadStarting] Atlas enqueue success. Native WebView2 download canceled.");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[BrowserHub.DownloadStarting] enqueue success/failure=failure; native cancel true/false=false");
                System.Diagnostics.Debug.WriteLine($"[BrowserHub.DownloadStarting] Atlas enqueue failed: {ex.Message}. Native WebView2 download remains canceled.");
                global::AtlasAI.UI.ToastNotificationManager.Instance.Show(
                    "Atlas could not start this download after confirmation.",
                    global::AtlasAI.UI.ToastType.Error,
                    4200);
            }
        }

        private static (global::AtlasAI.DownloadRiskLevel level, string explanation) ClassifyDownloadRisk(string extension, string sourceHost)
        {
            var ext  = (extension  ?? "").Trim().ToLowerInvariant();
            var host = (sourceHost ?? "").Trim().ToLowerInvariant();

            var executableExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".exe", ".msi", ".bat", ".cmd", ".ps1", ".vbs", ".scr", ".dll", ".com"
            };

            if (executableExts.Contains(ext))
                return (global::AtlasAI.DownloadRiskLevel.Elevated,
                    "Executable content can run code on your PC. Only continue if you trust the source and have verified the download URL.");

            if (ext == ".bin")
                return (global::AtlasAI.DownloadRiskLevel.Unknown,
                    "Generic binary file. Atlas cannot verify contents before download. Continue only if you trust the source.");

            if (ext == ".zip" || ext == ".rar" || ext == ".7z")
                return (global::AtlasAI.DownloadRiskLevel.Unknown,
                    "Archives may contain executables or scripts. Scan the extracted contents before opening.");

            if (!string.IsNullOrWhiteSpace(host) &&
                (host.EndsWith(".onion", StringComparison.OrdinalIgnoreCase) || host.Contains("ipfs")))
                return (global::AtlasAI.DownloadRiskLevel.Elevated,
                    "Source domain is uncommon. Verify the origin and file integrity before opening.");

            return (global::AtlasAI.DownloadRiskLevel.Safe,
                "File type appears low-risk. Review the source URL before proceeding.");
        }

        private static string ResolveChromeTitle(string url, string documentTitle)
        {
            var trimmedTitle = (documentTitle ?? "").Trim();
            if (string.IsNullOrWhiteSpace(trimmedTitle))
                return UrlFallbackTitle(url);

            if (Uri.TryCreate(trimmedTitle, UriKind.Absolute, out var titleUri) &&
                Uri.TryCreate(url, UriKind.Absolute, out var sourceUri) &&
                !string.Equals(titleUri.Host, sourceUri.Host, StringComparison.OrdinalIgnoreCase))
            {
                return UrlFallbackTitle(url);
            }

            return trimmedTitle;
        }

        private static string UrlFallbackTitle(string url)
        {
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                if (uri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase) ||
                    uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase))
                {
                    return string.IsNullOrWhiteSpace(uri.Host) ? url : uri.Host;
                }

                var authority = string.IsNullOrWhiteSpace(uri.Authority) ? "" : uri.Authority;
                return string.IsNullOrWhiteSpace(authority)
                    ? $"{uri.Scheme}:"
                    : $"{uri.Scheme}://{authority}";
            }

            return string.IsNullOrWhiteSpace(url) ? "New Tab" : url;
        }

        // ─── Tabs ─────────────────────────────────────────────────────────────

        private void RefreshTabBar()
        {
            TabsPanel.Children.Clear();
            for (int i = 0; i < _tabs.Count; i++)
            {
                var idx = i;
                var tab = _tabs[i];
                var isActive = (i == _activeTab);
                var tabProfile = _profiles.Find(p => p.Id == tab.ProfileId);

                var tabBorder = new Border
                {
                    MinWidth = 120,
                    MaxWidth = 200,
                    Height = 26,
                    CornerRadius = new CornerRadius(7),
                    Background = isActive
                        ? new SolidColorBrush(Color.FromRgb(0x12, 0x30, 0x46))
                        : new SolidColorBrush(Color.FromRgb(0x0A, 0x16, 0x24)),
                    BorderBrush = isActive
                        ? new SolidColorBrush(Color.FromRgb(0x00, 0xD1, 0xFF))
                        : new SolidColorBrush(Color.FromRgb(0x1C, 0x2A, 0x3A)),
                    BorderThickness = new Thickness(1),
                    Margin = new Thickness(2, 0, 2, 0),
                    Cursor = System.Windows.Input.Cursors.Hand
                };

                var tabGrid = new Grid();
                tabGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                tabGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                tabGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                // Per-tab profile dot (color from the tab's owning profile)
                var profileDot = new Border
                {
                    Width = 8,
                    Height = 8,
                    CornerRadius = new CornerRadius(4),
                    Background = tabProfile != null
                        ? ParseColor(tabProfile.Color)
                        : new SolidColorBrush(Color.FromRgb(0x1C, 0x6E, 0xBF)),
                    Margin = new Thickness(8, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    ToolTip = tabProfile != null
                        ? $"Profile: {tabProfile.Name}"
                        : $"Profile: {tab.ProfileId}"
                };
                Grid.SetColumn(profileDot, 0);

                var titleText = new TextBlock
                {
                    Text = tab.Title.Length > 20 ? tab.Title[..17] + "..." : tab.Title,
                    Foreground = isActive
                        ? new SolidColorBrush(Color.FromRgb(0xE6, 0xEE, 0xF7))
                        : new SolidColorBrush(Color.FromRgb(0x97, 0xA7, 0xBA)),
                    FontSize = 12,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(6, 0, 4, 0),
                    TextTrimming = TextTrimming.CharacterEllipsis
                };
                Grid.SetColumn(titleText, 1);

                var closeBtn = new Button
                {
                    Content = new TextBlock { Text = "✕", FontSize = 10, Foreground = new SolidColorBrush(Color.FromRgb(0x97, 0xA7, 0xBA)) },
                    Background = Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                    Width = 20,
                    Height = 20,
                    Margin = new Thickness(0, 0, 4, 0),
                    Cursor = System.Windows.Input.Cursors.Hand,
                    VerticalAlignment = VerticalAlignment.Center
                };
                closeBtn.Click += (s, e) => { e.Handled = true; CloseTab(idx); };
                Grid.SetColumn(closeBtn, 2);

                tabGrid.Children.Add(profileDot);
                tabGrid.Children.Add(titleText);
                tabGrid.Children.Add(closeBtn);
                tabBorder.Child = tabGrid;

                tabBorder.MouseLeftButtonDown += (s, e) => SwitchTab(idx);
                TabsPanel.Children.Add(tabBorder);
            }
        }

        private void SwitchTab(int idx)
        {
            if (idx < 0 || idx >= _tabs.Count) return;

            // Hide previously-active tab WebView (if any & different).
            if (_activeTab >= 0 && _activeTab < _tabWebViews.Count && _activeTab != idx)
            {
                try { _tabWebViews[_activeTab].Visibility = Visibility.Collapsed; } catch { }
            }

            _activeTab = idx;

            // Show the selected tab's WebView and reassign the BrowserWebView slot to it
            // so existing helpers (Navigate, AddressBar handlers, etc.) keep working unchanged.
            if (idx < _tabWebViews.Count)
            {
                var wv = _tabWebViews[idx];
                try { wv.Visibility = Visibility.Visible; } catch { }
                BrowserWebView = wv;

                // Sync AddressBar from the WebView's actual Source, falling back to tab metadata.
                try
                {
                    var src = wv.CoreWebView2?.Source;
                    AddressBar.Text = !string.IsNullOrEmpty(src) ? src! : _tabs[idx].Url;
                }
                catch
                {
                    AddressBar.Text = _tabs[idx].Url;
                }
            }

            RefreshTabBar();
            // Step 3: chip follows the active tab.
            try { UpdateProfileButton(); } catch { }

            // Keep _activeProfileId in sync with the visible tab so any legacy code path
            // that still reads it sees the active tab's profile.
            try { _activeProfileId = GetActiveTabProfileId(); } catch { }

            // Reload bookmarks for the selected tab's profile.
            try
            {
                LoadBookmarks();
                RefreshBookmarksBar();
                if (BookmarksPanel != null && BookmarksPanel.Visibility == Visibility.Visible)
                    RefreshBookmarksList();
            }
            catch { }
            // NOTE: do NOT call Navigate() here. Selecting a tab must not reload it.
        }

        private void CloseTab(int idx)
        {
            if (idx < 0 || idx >= _tabs.Count) return;

            if (_tabs.Count <= 1)
            {
                // Don't dispose the only WebView; just go home.
                Navigate(HomeUrl);
                return;
            }

            // Detach handlers, remove from host Grid, dispose the per-tab WebView2.
            if (idx < _tabWebViews.Count)
            {
                var wv = _tabWebViews[idx];
                try
                {
                    if (wv.CoreWebView2 != null)
                    {
                        wv.CoreWebView2.DownloadStarting -= CoreWebView2_DownloadStarting;
                        wv.CoreWebView2.NavigationStarting -= CoreWebView2_NavigationStarting;
                        wv.CoreWebView2.NavigationCompleted -= CoreWebView2_NavigationCompleted;
                        wv.CoreWebView2.DocumentTitleChanged -= CoreWebView2_DocumentTitleChanged;
                    }
                }
                catch { }
                try { _webViewHost?.Children.Remove(wv); } catch { }
                try { wv.Dispose(); } catch { }
                _tabWebViews.RemoveAt(idx);
            }

            _tabs.RemoveAt(idx);

            // Pick a remaining tab to show.
            int newActive;
            if (idx < _activeTab) newActive = _activeTab - 1;
            else if (idx > _activeTab) newActive = _activeTab;
            else newActive = Math.Min(_activeTab, _tabs.Count - 1);

            // Force SwitchTab to actually swap visibility even if newActive == _activeTab.
            _activeTab = -1;
            SwitchTab(newActive);
        }

        private void NewTabButton_Click(object sender, RoutedEventArgs e)
        {
            // Step 3: inherit profile from the currently active tab, not the global _activeProfileId.
            _ = OpenNewTabAsync(HomeUrl, GetActiveTabProfileId());
        }

        private void ToggleTabList_Click(object sender, RoutedEventArgs e)
        {
            // Step A: replace MessageBox with the in-app TabsPopup.
            if (TabsPopup.IsOpen)
            {
                TabsPopup.IsOpen = false;
                return;
            }

            RefreshTabsPopupList();
            TabsPopup.IsOpen = true;
        }

        private void CloseTabsPopup_Click(object sender, RoutedEventArgs e)
        {
            TabsPopup.IsOpen = false;
        }

        private void TabsPopupNewTab_Click(object sender, RoutedEventArgs e)
        {
            TabsPopup.IsOpen = false;
            _ = OpenNewTabAsync(HomeUrl, GetActiveTabProfileId());
        }

        private void RefreshTabsPopupList()
        {
            TabsPopupList.Children.Clear();
            TabsPopupCount.Text = _tabs.Count.ToString();

            if (_tabs.Count == 0)
            {
                TabsPopupList.Children.Add(new TextBlock
                {
                    Text = "No browser tabs are open yet.",
                    Foreground = new SolidColorBrush(Color.FromRgb(0x97, 0xA7, 0xBA)),
                    FontSize = 12,
                    Margin = new Thickness(12, 16, 12, 16),
                    TextAlignment = TextAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Stretch
                });
                return;
            }

            for (int i = 0; i < _tabs.Count; i++)
            {
                var idx = i;
                var tab = _tabs[i];
                var isActive = (i == _activeTab);
                var tabProfile = _profiles.Find(p => p.Id == tab.ProfileId);

                var rowBorder = new Border
                {
                    CornerRadius = new CornerRadius(8),
                    Background = isActive
                        ? new SolidColorBrush(Color.FromRgb(0x12, 0x30, 0x46))
                        : new SolidColorBrush(Color.FromRgb(0x0A, 0x16, 0x24)),
                    BorderBrush = isActive
                        ? new SolidColorBrush(Color.FromRgb(0x00, 0xD1, 0xFF))
                        : new SolidColorBrush(Color.FromRgb(0x1C, 0x2A, 0x3A)),
                    BorderThickness = new Thickness(1),
                    Margin = new Thickness(2, 3, 2, 3),
                    Padding = new Thickness(8, 6, 6, 6),
                    Cursor = System.Windows.Input.Cursors.Hand
                };

                var rowGrid = new Grid();
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var dot = new Border
                {
                    Width = 10,
                    Height = 10,
                    CornerRadius = new CornerRadius(5),
                    Background = tabProfile != null
                        ? ParseColor(tabProfile.Color)
                        : new SolidColorBrush(Color.FromRgb(0x1C, 0x6E, 0xBF)),
                    Margin = new Thickness(2, 0, 8, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    ToolTip = tabProfile != null ? $"Profile: {tabProfile.Name}" : $"Profile: {tab.ProfileId}"
                };
                Grid.SetColumn(dot, 0);

                var titleText = new TextBlock
                {
                    Text = string.IsNullOrWhiteSpace(tab.Title) ? "(Untitled)" : tab.Title,
                    Foreground = isActive
                        ? new SolidColorBrush(Color.FromRgb(0xE6, 0xEE, 0xF7))
                        : new SolidColorBrush(Color.FromRgb(0xC8, 0xD8, 0xEA)),
                    FontSize = 12,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Margin = new Thickness(0, 0, 6, 0)
                };
                Grid.SetColumn(titleText, 1);

                if (isActive)
                {
                    var activePill = new Border
                    {
                        Background = new SolidColorBrush(Color.FromRgb(0x10, 0x32, 0x48)),
                        BorderBrush = new SolidColorBrush(Color.FromRgb(0x00, 0xD1, 0xFF)),
                        BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(8),
                        Padding = new Thickness(6, 1, 6, 1),
                        Margin = new Thickness(0, 0, 6, 0),
                        VerticalAlignment = VerticalAlignment.Center,
                        Child = new TextBlock
                        {
                            Text = "Current",
                            Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0xD1, 0xFF)),
                            FontSize = 10
                        }
                    };
                    Grid.SetColumn(activePill, 2);
                    rowGrid.Children.Add(activePill);
                }

                if (_tabs.Count > 1)
                {
                    var closeBtn = new Button
                    {
                        Content = new TextBlock
                        {
                            Text = "✕",
                            FontSize = 11,
                            Foreground = new SolidColorBrush(Color.FromRgb(0x97, 0xA7, 0xBA))
                        },
                        Background = Brushes.Transparent,
                        BorderThickness = new Thickness(0),
                        Width = 22,
                        Height = 22,
                        Cursor = System.Windows.Input.Cursors.Hand,
                        VerticalAlignment = VerticalAlignment.Center,
                        ToolTip = "Close tab"
                    };
                    closeBtn.Click += (s, ev) =>
                    {
                        ev.Handled = true;
                        CloseTab(idx);
                        RefreshTabsPopupList();
                    };
                    Grid.SetColumn(closeBtn, 3);
                    rowGrid.Children.Add(closeBtn);
                }

                rowGrid.Children.Add(dot);
                rowGrid.Children.Add(titleText);
                rowBorder.Child = rowGrid;

                rowBorder.MouseEnter += (s, _) =>
                {
                    if (idx != _activeTab)
                        rowBorder.Background = new SolidColorBrush(Color.FromRgb(0x10, 0x24, 0x3A));
                };
                rowBorder.MouseLeave += (s, _) =>
                {
                    if (idx != _activeTab)
                        rowBorder.Background = new SolidColorBrush(Color.FromRgb(0x0A, 0x16, 0x24));
                };
                rowBorder.MouseLeftButtonDown += (s, ev) =>
                {
                    SwitchTab(idx);
                    TabsPopup.IsOpen = false;
                };

                TabsPopupList.Children.Add(rowBorder);
            }
        }

        // ─── Address bar ──────────────────────────────────────────────────────

        private void AddressBarMicBtn_Click(object sender, RoutedEventArgs e)
        {
            HandleInternetMicClick(AddressBar, AddressBarMicNote, "Press Enter to navigate.");
        }

        private void AiAskMicBtn_Click(object sender, RoutedEventArgs e)
        {
            HandleInternetMicClick(AiAskBox, AiAskMicNote, "Press Ask Atlas to run.");
        }

        private void HandleInternetMicClick(TextBox targetInput, TextBlock targetNote, string manualActionHint)
        {
            if (!_internetMicWired)
            {
                ShowInternetMicNote(targetNote, "Mic not wired");
                return;
            }

            targetInput.Focus();
            ShowInternetMicNote(targetNote, manualActionHint);
        }

        // Transcript fill is input-only by design; no auto navigation and no auto Ask Atlas.
        private void ApplyMicTranscriptToInput(string transcript, TextBox targetInput, TextBlock targetNote, string manualActionHint)
        {
            var value = (transcript ?? string.Empty).Trim();
            if (value.Length == 0)
            {
                return;
            }

            targetInput.Text = value;
            targetInput.CaretIndex = targetInput.Text.Length;
            targetInput.Focus();
            ShowInternetMicNote(targetNote, manualActionHint);
        }

        private void ShowInternetMicNote(TextBlock target, string text)
        {
            if (_activeMicNoteTarget != null && _activeMicNoteTarget != target)
            {
                _activeMicNoteTarget.Visibility = Visibility.Collapsed;
            }

            target.Text = text;
            target.Visibility = Visibility.Visible;
            _activeMicNoteTarget = target;

            _micNoteTimer?.Stop();
            _micNoteTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(2200) };
            _micNoteTimer.Tick += (_, _) =>
            {
                _micNoteTimer?.Stop();
                if (_activeMicNoteTarget != null)
                {
                    _activeMicNoteTarget.Visibility = Visibility.Collapsed;
                }
            };
            _micNoteTimer.Start();
        }

        private void AddressBar_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter || e.Key == Key.Return)
            {
                Navigate(AddressBar.Text.Trim());
                BrowserWebView.Focus();
            }
        }

        private void AddressBar_GotFocus(object sender, RoutedEventArgs e)
        {
            AddressBar.SelectAll();
        }

        private void AddressBar_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (AddressBarContextPopup != null)
            {
                AddressBarContextPopup.IsOpen = true;
                e.Handled = true;
            }
        }

        private void CloseAddressBarContextPopup()
        {
            if (AddressBarContextPopup != null)
                AddressBarContextPopup.IsOpen = false;
        }

        private void AddressBarCtx_Cut_Click(object sender, RoutedEventArgs e)
        {
            AddressBar.Cut();
            CloseAddressBarContextPopup();
        }

        private void AddressBarCtx_Copy_Click(object sender, RoutedEventArgs e)
        {
            AddressBar.Copy();
            CloseAddressBarContextPopup();
        }

        private void AddressBarCtx_Paste_Click(object sender, RoutedEventArgs e)
        {
            AddressBar.Paste();
            CloseAddressBarContextPopup();
        }

        private void AddressBarCtx_PasteAndGo_Click(object sender, RoutedEventArgs e)
        {
            if (Clipboard.ContainsText())
                AddressBar.Paste();

            var text = AddressBar.Text.Trim();
            if (!string.IsNullOrWhiteSpace(text))
            {
                Navigate(text);
                BrowserWebView.Focus();
            }

            CloseAddressBarContextPopup();
        }

        private void AddressBarCtx_Clear_Click(object sender, RoutedEventArgs e)
        {
            AddressBar.Clear();
            AddressBar.Focus();
            CloseAddressBarContextPopup();
        }

        private void AddressBarCtx_CopyCurrentUrl_Click(object sender, RoutedEventArgs e)
        {
            var currentUrl = (BrowserWebView.CoreWebView2?.Source ?? AddressBar.Text ?? "").Trim();
            if (!string.IsNullOrWhiteSpace(currentUrl))
                Clipboard.SetText(currentUrl);
            CloseAddressBarContextPopup();
        }

        private void AddressBarCtx_OpenInNewTab_Click(object sender, RoutedEventArgs e)
        {
            var target = AddressBar.Text.Trim();
            if (string.IsNullOrWhiteSpace(target))
                target = BrowserWebView.CoreWebView2?.Source ?? "";

            if (string.IsNullOrWhiteSpace(target))
                return;

            _ = OpenNewTabAsync(target, GetActiveTabProfileId());
            CloseAddressBarContextPopup();
        }

        // ─── Nav buttons ──────────────────────────────────────────────────────

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            if (BrowserWebView.CoreWebView2?.CanGoBack == true)
                BrowserWebView.CoreWebView2.GoBack();
        }

        private void ForwardButton_Click(object sender, RoutedEventArgs e)
        {
            if (BrowserWebView.CoreWebView2?.CanGoForward == true)
                BrowserWebView.CoreWebView2.GoForward();
        }

        private void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            if (BrowserWebView.CoreWebView2 == null) return;
            if (_isNavigating)
                BrowserWebView.CoreWebView2.Stop();
            else
                BrowserWebView.CoreWebView2.Reload();
        }

        private void HomeButton_Click(object sender, RoutedEventArgs e)
        {
            Navigate(HomeUrl);
        }

        // ─── Bookmarks ────────────────────────────────────────────────────────

        private void BookmarkPageBtn_Click(object sender, RoutedEventArgs e)
        {
            var url = BrowserWebView.CoreWebView2?.Source ?? "";
            if (string.IsNullOrWhiteSpace(url)) return;
            var title = BrowserWebView.CoreWebView2?.DocumentTitle ?? "";

            if (_bookmarks.Any(b => b.Url == url))
            {
                _bookmarks.RemoveAll(b => b.Url == url);
                BookmarkIcon.Text = "☆";
                BookmarkIcon.Foreground = new SolidColorBrush(Color.FromRgb(0x97, 0xA7, 0xBA));
            }
            else
            {
                _bookmarks.Add(new BookmarkEntry(url, title));
                BookmarkIcon.Text = "★";
                BookmarkIcon.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xD7, 0x00));
            }
            SaveBookmarks();
            RefreshBookmarksList();
            RefreshBookmarksBar();
        }

        private void BookmarksBtn_Click(object sender, RoutedEventArgs e)
        {
            BookmarksPanel.Visibility = BookmarksPanel.Visibility == Visibility.Visible
                ? Visibility.Collapsed
                : Visibility.Visible;
            if (BookmarksPanel.Visibility == Visibility.Visible)
                RefreshBookmarksList();
        }

        private void CloseBookmarks_Click(object sender, RoutedEventArgs e)
        {
            BookmarksPanel.Visibility = Visibility.Collapsed;
        }

        private void RefreshBookmarksList()
        {
            BookmarksList.Children.Clear();

            // --- Import buttons ---
            var importLabel = new TextBlock
            {
                Text = "IMPORT",
                Foreground = new SolidColorBrush(Color.FromRgb(0x97, 0xA7, 0xBA)),
                FontSize = 10, FontWeight = FontWeights.Bold,
                Margin = new Thickness(4, 0, 0, 6)
            };
            BookmarksList.Children.Add(importLabel);

            foreach (var (browser, path) in GetImportableBrowsers())
            {
                var browserName = browser;
                var browserPath = path;
                var importBtn = new Button
                {
                    Background = new SolidColorBrush(Color.FromRgb(0x0D, 0x1A, 0x2A)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(0x1C, 0x2A, 0x3A)),
                    BorderThickness = new Thickness(1),
                    Cursor = System.Windows.Input.Cursors.Hand,
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                    Padding = new Thickness(8, 6, 8, 6),
                    Margin = new Thickness(0, 0, 0, 4)
                };
                importBtn.Content = new TextBlock
                {
                    Text = $"⬇ Import from {browserName}",
                    Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0xD1, 0xFF)),
                    FontSize = 12
                };
                importBtn.Click += (s, e) => ImportBookmarks(browserPath, browserName);
                BookmarksList.Children.Add(importBtn);
            }

            // --- Divider ---
            if (_bookmarks.Count > 0)
            {
                BookmarksList.Children.Add(new Border
                {
                    Height = 1,
                    Background = new SolidColorBrush(Color.FromRgb(0x1C, 0x2A, 0x3A)),
                    Margin = new Thickness(0, 8, 0, 8)
                });
                BookmarksList.Children.Add(new TextBlock
                {
                    Text = "MY BOOKMARKS",
                    Foreground = new SolidColorBrush(Color.FromRgb(0x97, 0xA7, 0xBA)),
                    FontSize = 10, FontWeight = FontWeights.Bold,
                    Margin = new Thickness(4, 0, 0, 6)
                });
            }

            // --- Bookmark entries ---
            foreach (var bm in _bookmarks)
            {
                var entry = bm;
                var label = Uri.TryCreate(entry.Url, UriKind.Absolute, out var u) ? u.Host : entry.Url;
                if (label.StartsWith("www.", StringComparison.OrdinalIgnoreCase)) label = label[4..];

                var row = new Grid { Margin = new Thickness(0, 0, 0, 2) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var navBtn = new Button
                {
                    Background = Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                    Cursor = System.Windows.Input.Cursors.Hand,
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                    Padding = new Thickness(4, 5, 4, 5)
                };
                var displayTitle = string.IsNullOrWhiteSpace(entry.Title) ? label : entry.Title;
                navBtn.Content = new TextBlock
                {
                    Text = displayTitle.Length > 30 ? displayTitle[..27] + "…" : displayTitle,
                    Foreground = new SolidColorBrush(Color.FromRgb(0xC8, 0xD8, 0xEA)),
                    FontSize = 12,
                    ToolTip = entry.Url
                };
                navBtn.Click += (s, e) => { Navigate(entry.Url); BookmarksPanel.Visibility = Visibility.Collapsed; };
                Grid.SetColumn(navBtn, 0);

                var delBtn = new Button
                {
                    Background = Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                    Cursor = System.Windows.Input.Cursors.Hand,
                    Padding = new Thickness(4, 2, 4, 2),
                    Opacity = 0.5,
                    VerticalAlignment = VerticalAlignment.Center
                };
                delBtn.Content = new TextBlock { Text = "✕", Foreground = new SolidColorBrush(Color.FromRgb(0x97, 0xA7, 0xBA)), FontSize = 10 };
                delBtn.MouseEnter += (s, _) => ((Button)s).Opacity = 1;
                delBtn.MouseLeave += (s, _) => ((Button)s).Opacity = 0.5;
                delBtn.Click += (s, e) =>
                {
                    _bookmarks.RemoveAll(x => x.Url == entry.Url);
                    SaveBookmarks();
                    RefreshBookmarksList();
                    RefreshBookmarksBar();
                };
                Grid.SetColumn(delBtn, 1);

                row.Children.Add(navBtn);
                row.Children.Add(delBtn);
                BookmarksList.Children.Add(row);
            }

            if (_bookmarks.Count == 0)
            {
                var noBookmarks = new TextBlock
                {
                    Text = "No bookmarks yet.\nClick ☆ in the address bar to save a page,\nor import from a browser above.",
                    Foreground = new SolidColorBrush(Color.FromRgb(0x97, 0xA7, 0xBA)),
                    FontSize = 12, TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(4, 8, 4, 0)
                };
                BookmarksList.Children.Add(noBookmarks);
            }
        }

        private static IEnumerable<(string Name, string Path)> GetImportableBrowsers()
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var candidates = new[]
            {
                ("Chrome",          System.IO.Path.Combine(local,   "Google", "Chrome", "User Data", "Default", "Bookmarks")),
                ("Chrome (Profile 1)", System.IO.Path.Combine(local, "Google", "Chrome", "User Data", "Profile 1", "Bookmarks")),
                ("Edge",            System.IO.Path.Combine(local,   "Microsoft", "Edge", "User Data", "Default", "Bookmarks")),
                ("Brave",           System.IO.Path.Combine(local,   "BraveSoftware", "Brave-Browser", "User Data", "Default", "Bookmarks")),
                ("Vivaldi",         System.IO.Path.Combine(local,   "Vivaldi", "User Data", "Default", "Bookmarks")),
                ("Opera",           System.IO.Path.Combine(roaming, "Opera Software", "Opera Stable", "Bookmarks")),
            };
            return candidates.Where(c => File.Exists(c.Item2));
        }

        private void ImportBookmarks(string bookmarksFilePath, string browserName)
        {
            try
            {
                var json = File.ReadAllText(bookmarksFilePath);
                using var doc = JsonDocument.Parse(json);
                var imported = new List<BookmarkEntry>();
                ExtractChromeBookmarks(doc.RootElement, imported);

                var newCount = 0;
                foreach (var bm in imported)
                {
                    if (!_bookmarks.Any(x => x.Url == bm.Url))
                    {
                        _bookmarks.Add(bm);
                        newCount++;
                    }
                }

                SaveBookmarks();
                RefreshBookmarksList();
                RefreshBookmarksBar();

                // Show brief confirmation
                var toast = new TextBlock
                {
                    Text = $"✓ Imported {newCount} bookmarks from {browserName}",
                    Foreground = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50)),
                    FontSize = 12, Margin = new Thickness(4, 8, 4, 0), TextWrapping = TextWrapping.Wrap
                };
                BookmarksList.Children.Insert(0, toast);
            }
            catch (Exception ex)
            {
                var err = new TextBlock
                {
                    Text = $"Import failed: {ex.Message}",
                    Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x5A, 0x5A)),
                    FontSize = 11, Margin = new Thickness(4, 4, 4, 0), TextWrapping = TextWrapping.Wrap
                };
                BookmarksList.Children.Insert(0, err);
            }
        }

        private static void ExtractChromeBookmarks(JsonElement el, List<BookmarkEntry> result)
        {
            if (el.ValueKind == JsonValueKind.Object)
            {
                if (el.TryGetProperty("type", out var typeEl) && typeEl.GetString() == "url")
                {
                    var url = el.TryGetProperty("url", out var u) ? u.GetString() ?? "" : "";
                    var name = el.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                    if (!string.IsNullOrWhiteSpace(url) &&
                        (url.StartsWith("http://") || url.StartsWith("https://")))
                        result.Add(new BookmarkEntry(url, name));
                    return;
                }

                foreach (var prop in el.EnumerateObject())
                    ExtractChromeBookmarks(prop.Value, result);
            }
            else if (el.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in el.EnumerateArray())
                    ExtractChromeBookmarks(item, result);
            }
        }


        private string LegacyBookmarksPath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AtlasOS", "BrowserHub", BookmarksFile);

        private string BookmarksPath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AtlasOS", "BrowserHub", "Profiles", GetActiveTabProfileId(), BookmarksFile);

        private void LoadBookmarks()
        {
            _bookmarks = new();
            var hasProfileBookmarks = File.Exists(BookmarksPath);
            var sourcePath = hasProfileBookmarks
                ? BookmarksPath
                : _activeProfileId == "default" && File.Exists(LegacyBookmarksPath)
                    ? LegacyBookmarksPath
                    : null;

            try
            {
                if (!string.IsNullOrWhiteSpace(sourcePath))
                {
                    var json = File.ReadAllText(sourcePath);
                    try
                    {
                        _bookmarks = JsonSerializer.Deserialize<List<BookmarkEntry>>(json) ?? new();
                    }
                    catch
                    {
                        var old = JsonSerializer.Deserialize<List<string>>(json) ?? new();
                        _bookmarks = old.ConvertAll(u => new BookmarkEntry(u));
                    }
                }
            }
            catch { _bookmarks = new(); }

            if (!hasProfileBookmarks && sourcePath == LegacyBookmarksPath && _bookmarks.Count > 0)
                SaveBookmarks();

            // Keep browser import as a one-time default-profile bootstrap only.
            if (_bookmarks.Count == 0 && _activeProfileId == "default" && string.IsNullOrWhiteSpace(sourcePath))
            {
                foreach (var (_, path) in GetImportableBrowsers())
                {
                    try
                    {
                        var json = File.ReadAllText(path);
                        using var doc = JsonDocument.Parse(json);
                        var imported = new List<BookmarkEntry>();
                        ExtractChromeBookmarks(doc.RootElement, imported);
                        foreach (var bm in imported)
                            if (!_bookmarks.Any(x => x.Url == bm.Url))
                                _bookmarks.Add(bm);
                    }
                    catch { }
                }
                if (_bookmarks.Count > 0)
                    SaveBookmarks();
            }

            Dispatcher.InvokeAsync(RefreshBookmarksBar);
        }

        private void SaveBookmarks()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(BookmarksPath)!);
                File.WriteAllText(BookmarksPath, JsonSerializer.Serialize(_bookmarks));
            }
            catch { }
        }

        private void RefreshBookmarksBar()
        {
            BookmarksBar.Children.Clear();
            foreach (var bm in _bookmarks)
            {
                var url = bm.Url;
                var displayTitle = string.IsNullOrWhiteSpace(bm.Title)
                    ? (Uri.TryCreate(url, UriKind.Absolute, out var u2) ? u2.Host : url)
                    : bm.Title;
                var label = displayTitle;
                if (label.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
                    label = label[4..];

                var btn = new Button
                {
                    Background = Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                    Cursor = System.Windows.Input.Cursors.Hand,
                    Padding = new Thickness(8, 0, 8, 0),
                    Height = 26,
                    VerticalContentAlignment = VerticalAlignment.Center
                };

                var template = new ControlTemplate(typeof(Button));
                var bd = new FrameworkElementFactory(typeof(Border));
                bd.Name = "Bd";
                bd.SetValue(Border.BackgroundProperty, Brushes.Transparent);
                bd.SetValue(Border.CornerRadiusProperty, new CornerRadius(5));
                bd.SetValue(Border.PaddingProperty, new Thickness(8, 0, 8, 0));
                var cp = new FrameworkElementFactory(typeof(ContentPresenter));
                cp.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
                cp.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
                bd.AppendChild(cp);
                template.VisualTree = bd;
                btn.Template = template;

                btn.Content = new TextBlock
                {
                    Text = label.Length > 18 ? label[..15] + "…" : label,
                    Foreground = new SolidColorBrush(Color.FromRgb(0xC8, 0xD8, 0xEA)),
                    FontSize = 12,
                    VerticalAlignment = VerticalAlignment.Center
                };
                btn.ToolTip = url;
                btn.MouseEnter += (s, _) => ((Button)s).Opacity = 0.8;
                btn.MouseLeave += (s, _) => ((Button)s).Opacity = 1.0;
                btn.Click += (s, e) => Navigate(url);

                // Right-click context menu: Delete / Open in New Tab
                var ctxMenu = new ContextMenu
                {
                    Background = new SolidColorBrush(Color.FromRgb(0x0D, 0x11, 0x1A)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(0x1C, 0x2A, 0x3A)),
                    BorderThickness = new Thickness(1)
                };
                var menuStyle = new Style(typeof(MenuItem));
                menuStyle.Setters.Add(new Setter(MenuItem.ForegroundProperty, new SolidColorBrush(Color.FromRgb(0xC8, 0xD8, 0xEA))));
                menuStyle.Setters.Add(new Setter(MenuItem.BackgroundProperty, Brushes.Transparent));
                menuStyle.Setters.Add(new Setter(MenuItem.FontSizeProperty, 12.0));

                var newTabItem = new MenuItem { Header = "Open in new tab", Style = menuStyle };
                newTabItem.Click += (s, e) =>
                {
                    _ = OpenNewTabAsync(url, _activeProfileId);
                };

                var deleteItem = new MenuItem { Header = "Remove from bookmarks bar", Style = menuStyle };
                var bmEntry = bm;
                deleteItem.Click += (s, e) =>
                {
                    _bookmarks.RemoveAll(x => x.Url == bmEntry.Url);
                    SaveBookmarks();
                    RefreshBookmarksBar();
                    RefreshBookmarksList();
                };

                ctxMenu.Items.Add(newTabItem);
                ctxMenu.Items.Add(new Separator { Background = new SolidColorBrush(Color.FromRgb(0x1C, 0x2A, 0x3A)) });
                ctxMenu.Items.Add(deleteItem);
                btn.ContextMenu = ctxMenu;

                BookmarksBar.Children.Add(btn);

                // Separator dot
                BookmarksBar.Children.Add(new TextBlock
                {
                    Text = "·",
                    Foreground = new SolidColorBrush(Color.FromRgb(0x2C, 0x3A, 0x4A)),
                    VerticalAlignment = VerticalAlignment.Center,
                    FontSize = 14,
                    Margin = new Thickness(0)
                });
            }

            if (_bookmarks.Count == 0)
                BookmarksBar.Children.Add(new TextBlock
                {
                    Text = "Bookmark pages with ☆ to add them here",
                    Foreground = new SolidColorBrush(Color.FromRgb(0x4A, 0x5A, 0x6A)),
                    FontSize = 11,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(8, 0, 0, 0)
                });
        }

        // ─── History / Downloads (sidebar stubs) ──────────────────────────────

        private void HistoryBtn_Click(object sender, RoutedEventArgs e)
        {
            // Step (history): replace edge://history/ navigation with the in-app HistoryPopup.
            if (HistoryPopup.IsOpen)
            {
                HistoryPopup.IsOpen = false;
                return;
            }

            RefreshHistoryPopupList();
            HistoryPopup.IsOpen = true;
        }

        private void CloseHistoryPopup_Click(object sender, RoutedEventArgs e)
        {
            HistoryPopup.IsOpen = false;
        }

        // ─── Password Manager (sidebar) ───────────────────────────────────────
        // Atlas does not store website logins. This popup gives the user honest
        // routes to Google Password Manager (per active tab profile) and the
        // Windows Credential Manager.

        private void PasswordsBtn_Click(object sender, RoutedEventArgs e)
        {
            if (PasswordsPopup.IsOpen)
            {
                PasswordsPopup.IsOpen = false;
                return;
            }
            PasswordsPopup.IsOpen = true;
        }

        private void ClosePasswordsPopup_Click(object sender, RoutedEventArgs e)
        {
            PasswordsPopup.IsOpen = false;
        }

        private void OpenGooglePasswordManager_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                PasswordsPopup.IsOpen = false;
                _ = OpenNewTabAsync("https://passwords.google.com/", GetActiveTabProfileId());
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[BrowserHub.Passwords] Google open failed: {ex.Message}");
            }
        }

        private void OpenWindowsCredentialManager_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                PasswordsPopup.IsOpen = false;
                var psi = new System.Diagnostics.ProcessStartInfo("control.exe", "/name Microsoft.CredentialManager")
                {
                    UseShellExecute = true
                };
                System.Diagnostics.Process.Start(psi);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[BrowserHub.Passwords] CredMan open failed: {ex.Message}");
            }
        }

        private void RefreshHistoryPopupList()
        {
            HistoryPopupList.Children.Clear();

            // No persistent history store yet — populate from current session tabs.
            var entries = _tabs
                .Where(t => !string.IsNullOrWhiteSpace(t.Url))
                .Select(t => (Title: string.IsNullOrWhiteSpace(t.Title) ? t.Url : t.Title, Url: t.Url, ProfileId: t.ProfileId))
                .ToList();

            HistoryPopupCount.Text = entries.Count.ToString();

            if (entries.Count == 0)
            {
                HistoryPopupList.Children.Add(new TextBlock
                {
                    Text = "No browsing history in this session yet.",
                    Foreground = new SolidColorBrush(Color.FromRgb(0x97, 0xA7, 0xBA)),
                    FontSize = 12,
                    Margin = new Thickness(12, 16, 12, 16),
                    TextAlignment = TextAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Stretch
                });
                return;
            }

            foreach (var entry in entries)
            {
                var url = entry.Url;
                var title = entry.Title;
                var profile = _profiles.Find(p => p.Id == entry.ProfileId);

                var rowBorder = new Border
                {
                    CornerRadius = new CornerRadius(8),
                    Background = new SolidColorBrush(Color.FromRgb(0x0A, 0x16, 0x24)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(0x1C, 0x2A, 0x3A)),
                    BorderThickness = new Thickness(1),
                    Margin = new Thickness(2, 3, 2, 3),
                    Padding = new Thickness(8, 6, 8, 6),
                    Cursor = System.Windows.Input.Cursors.Hand
                };

                var rowGrid = new Grid();
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                var dot = new Border
                {
                    Width = 8,
                    Height = 8,
                    CornerRadius = new CornerRadius(4),
                    Background = profile != null
                        ? ParseColor(profile.Color)
                        : new SolidColorBrush(Color.FromRgb(0x1C, 0x6E, 0xBF)),
                    Margin = new Thickness(2, 0, 8, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    ToolTip = profile != null ? $"Profile: {profile.Name}" : $"Profile: {entry.ProfileId}"
                };
                Grid.SetColumn(dot, 0);

                var stack = new StackPanel();
                stack.Children.Add(new TextBlock
                {
                    Text = title,
                    Foreground = new SolidColorBrush(Color.FromRgb(0xE6, 0xEE, 0xF7)),
                    FontSize = 12,
                    TextTrimming = TextTrimming.CharacterEllipsis
                });
                stack.Children.Add(new TextBlock
                {
                    Text = url,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x6F, 0x8A, 0xA8)),
                    FontSize = 10,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Margin = new Thickness(0, 2, 0, 0)
                });
                Grid.SetColumn(stack, 1);

                rowGrid.Children.Add(dot);
                rowGrid.Children.Add(stack);
                rowBorder.Child = rowGrid;

                rowBorder.MouseEnter += (s, _) =>
                    rowBorder.Background = new SolidColorBrush(Color.FromRgb(0x10, 0x24, 0x3A));
                rowBorder.MouseLeave += (s, _) =>
                    rowBorder.Background = new SolidColorBrush(Color.FromRgb(0x0A, 0x16, 0x24));
                rowBorder.MouseLeftButtonDown += (s, ev) =>
                {
                    HistoryPopup.IsOpen = false;
                    _ = OpenNewTabAsync(url, GetActiveTabProfileId());
                };

                HistoryPopupList.Children.Add(rowBorder);
            }
        }

        private void DownloadsBtn_Click(object sender, RoutedEventArgs e)
        {
            // Route to the in-app Atlas Downloader section (AI DOWNLOADS) inside CommandCenterWindow.
            try { LogProfile("Browser Hub Downloads routed to in-app AI DOWNLOADS"); } catch { }
            try
            {
                var ccw = Window.GetWindow(this) as global::AtlasAI.CommandCenterWindow
                          ?? System.Windows.Application.Current?.Windows
                                 .OfType<global::AtlasAI.CommandCenterWindow>()
                                 .FirstOrDefault();
                if (ccw != null)
                {
                    ccw.NavigateToView("AI DOWNLOADS");
                    return;
                }
            }
            catch (Exception ex)
            {
                try { LogProfile($"DownloadsBtn_Click NavigateToView FAILED: {ex.Message}"); } catch { }
            }

            // Fallback only if no CommandCenterWindow host is available.
            try
            {
                var existing = System.Windows.Application.Current?.Windows
                    .OfType<global::AtlasAI.DownloaderWindow>()
                    .FirstOrDefault();
                if (existing != null)
                {
                    if (existing.WindowState == WindowState.Minimized)
                        existing.WindowState = WindowState.Normal;
                    existing.Activate();
                    return;
                }

                var win = new global::AtlasAI.DownloaderWindow
                {
                    Owner = Window.GetWindow(this)
                };
                win.Show();
            }
            catch (Exception ex)
            {
                try { LogProfile($"DownloadsBtn_Click open DownloaderWindow FAILED: {ex.Message}"); } catch { }
            }
        }

        // ─── Profile Management ───────────────────────────────────────────────

        private record BrowserProfile(string Id, string Name, string Color);

        private List<BrowserProfile> _profiles = new();
        private string _activeProfileId = "default";
        private string _pendingProfileColor = "#1C6EBF";

        private static readonly string ProfileDebugLogPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AtlasOS", "BrowserHub", "profile-debug.log");

        private static void LogProfile(string msg)
        {
            var line = $"[{DateTime.Now:HH:mm:ss.fff}] [BrowserHub.Profile] {msg}";
            System.Diagnostics.Debug.WriteLine(line);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ProfileDebugLogPath)!);
                File.AppendAllText(ProfileDebugLogPath, line + Environment.NewLine);
            }
            catch { }
        }

        private string ActiveProfileStatePath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AtlasOS", "BrowserHub", "active_profile.txt");

        private void LoadActiveProfileId()
        {
            try
            {
                if (File.Exists(ActiveProfileStatePath))
                {
                    var saved = File.ReadAllText(ActiveProfileStatePath).Trim();
                    if (!string.IsNullOrWhiteSpace(saved))
                        _activeProfileId = saved;
                }
            }
            catch { }
        }

        private void SaveActiveProfileId()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ActiveProfileStatePath)!);
                File.WriteAllText(ActiveProfileStatePath, _activeProfileId ?? "default");
            }
            catch { }
        }

        private static readonly string[] _profileColors = new[]
        {
            "#1C6EBF", "#C0392B", "#27AE60", "#8E44AD",
            "#E67E22", "#16A085", "#2980B9", "#D35400"
        };

        private string ProfilesPath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AtlasOS", "BrowserHub", "profiles.json");

        private void LoadProfiles()
        {
            try
            {
                if (File.Exists(ProfilesPath))
                {
                    var json = File.ReadAllText(ProfilesPath);
                    _profiles = JsonSerializer.Deserialize<List<BrowserProfile>>(json) ?? new();
                }
            }
            catch { _profiles = new(); }

            if (_profiles.Count == 0)
                _profiles.Add(new BrowserProfile("default", "Default", "#1C6EBF"));

            LoadActiveProfileId();
            if (_profiles.Find(p => p.Id == _activeProfileId) == null)
                _activeProfileId = _profiles[0].Id;

            UpdateProfileButton();
        }

        private void SaveProfiles()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ProfilesPath)!);
                File.WriteAllText(ProfilesPath, JsonSerializer.Serialize(_profiles));
            }
            catch { }
        }

        private void UpdateProfileButton()
        {
            // Step 3: chip follows the active tab's profile, not the global _activeProfileId.
            var activeTabProfileId = GetActiveTabProfileId();
            var p = _profiles.Find(x => x.Id == activeTabProfileId)
                    ?? _profiles.Find(x => x.Id == _activeProfileId)
                    ?? _profiles[0];
            ProfileName.Text = p.Name;
            ProfileInitial.Text = p.Name.Length > 0 ? p.Name[0].ToString().ToUpper() : "?";
            ProfileAvatar.Background = ParseColor(p.Color);
        }

        private static SolidColorBrush ParseColor(string hex)
        {
            try
            {
                hex = hex.TrimStart('#');
                var r = Convert.ToByte(hex[..2], 16);
                var g = Convert.ToByte(hex[2..4], 16);
                var b = Convert.ToByte(hex[4..6], 16);
                return new SolidColorBrush(Color.FromRgb(r, g, b));
            }
            catch { return new SolidColorBrush(Color.FromRgb(0x1C, 0x6E, 0xBF)); }
        }

        private void ProfileBtn_Click(object sender, RoutedEventArgs e)
        {
            LogProfile($"ProfileBtn_Click ENTER activeId={_activeProfileId} count={_profiles.Count} pickerOpenBefore={ProfilePickerPopup.IsOpen}");
            try { RefreshProfilesGrid(); } catch (Exception ex) { LogProfile($"RefreshProfilesGrid EX: {ex.GetType().Name}: {ex.Message}"); }
            LogProfile($"ProfileBtn_Click after RefreshProfilesGrid gridChildren={ProfilesGrid.Children.Count}");
            AddProfilePopup.IsOpen = false;
            ProfilePickerPopup.IsOpen = true;
            LogProfile($"ProfileBtn_Click EXIT pickerOpenAfter={ProfilePickerPopup.IsOpen}");
        }

        private void CloseProfilePicker_Click(object sender, RoutedEventArgs e)
        {
            ProfilePickerPopup.IsOpen = false;
        }

        private void RefreshProfilesGrid()
        {
            LogProfile($"RefreshProfilesGrid ENTER profileCount={_profiles.Count}");
            ProfilesGrid.Children.Clear();
            foreach (var profile in _profiles)
            {
                var p = profile;
                var isActive = p.Id == _activeProfileId;

                var card = new Border
                {
                    Width = 140,
                    Height = 160,
                    Margin = new Thickness(8),
                    CornerRadius = new CornerRadius(10),
                    Background = new SolidColorBrush(Color.FromRgb(0x0D, 0x1A, 0x2A)),
                    BorderBrush = isActive
                        ? new SolidColorBrush(Color.FromRgb(0x00, 0xD1, 0xFF))
                        : new SolidColorBrush(Color.FromRgb(0x1C, 0x2A, 0x3A)),
                    BorderThickness = new Thickness(isActive ? 2 : 1),
                    Cursor = System.Windows.Input.Cursors.Hand
                };

                var inner = new StackPanel
                {
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(12)
                };

                var avatarBorder = new Border
                {
                    Width = 64,
                    Height = 64,
                    CornerRadius = new CornerRadius(32),
                    Background = ParseColor(p.Color),
                    Margin = new Thickness(0, 0, 0, 10),
                    HorizontalAlignment = HorizontalAlignment.Center
                };
                var initText = new TextBlock
                {
                    Text = p.Name.Length > 0 ? p.Name[0].ToString().ToUpper() : "?",
                    Foreground = Brushes.White,
                    FontSize = 26,
                    FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                avatarBorder.Child = initText;

                var nameText = new TextBlock
                {
                    Text = p.Name,
                    Foreground = new SolidColorBrush(Color.FromRgb(0xE6, 0xEE, 0xF7)),
                    FontSize = 13,
                    FontWeight = isActive ? FontWeights.SemiBold : FontWeights.Normal,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis
                };

                inner.Children.Add(avatarBorder);
                inner.Children.Add(nameText);

                if (isActive)
                    inner.Children.Add(new TextBlock
                    {
                        Text = "Active",
                        Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0xD1, 0xFF)),
                        FontSize = 11,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Margin = new Thickness(0, 3, 0, 0)
                    });

                // Right-click context menu for delete (don't allow deleting last profile)
                if (_profiles.Count > 1)
                {
                    var ctx = new ContextMenu();
                    var delItem = new MenuItem { Header = "Delete Profile" };
                    delItem.Click += (s, ev) => DeleteProfile(p.Id);
                    ctx.Items.Add(delItem);
                    card.ContextMenu = ctx;
                }

                card.Child = inner;
                card.MouseLeftButtonDown += (s, ev) =>
                {
                    LogProfile($"card MouseLeftButtonDown id={p.Id} name={p.Name} eventSource={ev.OriginalSource?.GetType().Name}");
                    OpenOrSwitchToProfileTab(p.Id);
                };
                card.MouseEnter += (s, _) => { if (!isActive) card.Background = new SolidColorBrush(Color.FromRgb(0x13, 0x22, 0x35)); };
                card.MouseLeave += (s, _) => { if (!isActive) card.Background = new SolidColorBrush(Color.FromRgb(0x0D, 0x1A, 0x2A)); };

                ProfilesGrid.Children.Add(card);
            }
            LogProfile($"RefreshProfilesGrid EXIT cardsAdded={ProfilesGrid.Children.Count}");
        }

        private async void SwitchProfile(string profileId)
        {
            var oldActiveId = _activeProfileId;
            string oldSrc = "";
            int oldHash = 0;
            string oldParentName = "<null>";
            try { oldHash = BrowserWebView != null ? System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(BrowserWebView) : 0; } catch { }
            try { oldSrc = BrowserWebView?.CoreWebView2?.Source ?? ""; } catch { }
            try { oldParentName = (BrowserWebView?.Parent as FrameworkElement)?.Name ?? BrowserWebView?.Parent?.GetType().Name ?? "<null>"; } catch { }
            LogProfile($"SwitchProfile ENTER oldId={oldActiveId} requestedId={profileId} oldWvHash={oldHash} oldSrc={oldSrc} oldParent={oldParentName}");

            if (profileId == _activeProfileId)
            {
                LogProfile("SwitchProfile early-return: same profile");
                ProfilePickerPopup.IsOpen = false;
                return;
            }

            _activeProfileId = profileId;
            SaveActiveProfileId();
            ProfilePickerPopup.IsOpen = false;
            UpdateProfileButton();
            LoadBookmarks();
            if (BookmarksPanel.Visibility == Visibility.Visible)
                RefreshBookmarksList();

            // Re-init WebView2 with the new profile's data folder.
            // EnsureCoreWebView2Async cannot swap UserDataFolder on an already-initialized
            // WebView2 — we must dispose the existing control and insert a fresh instance
            // into the same parent at the same child index.
            LoadingOverlay.Visibility = Visibility.Visible;

            var oldWv = BrowserWebView;
            var parent = oldWv.Parent as Panel;
            int insertIndex = -1;

            try
            {
                if (oldWv?.CoreWebView2 != null)
                {
                    oldWv.CoreWebView2.DownloadStarting -= CoreWebView2_DownloadStarting;
                    oldWv.CoreWebView2.NavigationStarting -= CoreWebView2_NavigationStarting;
                    oldWv.CoreWebView2.NavigationCompleted -= CoreWebView2_NavigationCompleted;
                    oldWv.CoreWebView2.DocumentTitleChanged -= CoreWebView2_DocumentTitleChanged;
                }
            }
            catch { }

            try
            {
                if (parent != null && oldWv != null)
                {
                    insertIndex = parent.Children.IndexOf(oldWv);
                    parent.Children.Remove(oldWv);
                }
                try { oldWv?.Dispose(); } catch { }
            }
            catch { }

            var newWv = new Microsoft.Web.WebView2.Wpf.WebView2();
            int newHash = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(newWv);
            if (parent != null)
            {
                if (insertIndex >= 0 && insertIndex <= parent.Children.Count)
                    parent.Children.Insert(insertIndex, newWv);
                else
                    parent.Children.Add(newWv);
            }
            BrowserWebView = newWv;
            LogProfile($"new WebView2 inserted parent={(parent?.Name ?? parent?.GetType().Name ?? "<null>")} childCount={(parent?.Children.Count ?? -1)} insertIndex={insertIndex} newWvHash={newHash} oldWvHash={oldHash} same={(newHash==oldHash)}");

            try
            {
                var userDataFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "AtlasOS", "BrowserHub", "Profiles", profileId);
                Directory.CreateDirectory(userDataFolder);
                LogProfile($"before EnsureCoreWebView2Async userDataFolder={userDataFolder} exists={Directory.Exists(userDataFolder)} newWvHash={newHash}");

                var env = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
                await BrowserWebView.EnsureCoreWebView2Async(env);

                bool coreOk = BrowserWebView.CoreWebView2 != null;
                string newSrc = "";
                string profileName = "<n/a>";
                try { newSrc = BrowserWebView.CoreWebView2?.Source ?? ""; } catch { }
                try { profileName = BrowserWebView.CoreWebView2?.Profile?.ProfileName ?? "<null>"; } catch (Exception px) { profileName = "<ex:" + px.Message + ">"; }
                LogProfile($"after EnsureCoreWebView2Async coreExists={coreOk} src={newSrc} cwv2Profile={profileName} newHash={newHash} oldHash={oldHash} hashesDiffer={(newHash!=oldHash)}");

                BrowserWebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
                BrowserWebView.CoreWebView2.Settings.AreDevToolsEnabled = true;
                BrowserWebView.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = true;
                BrowserWebView.CoreWebView2.Settings.IsStatusBarEnabled = false;

                BrowserWebView.CoreWebView2.DownloadStarting += CoreWebView2_DownloadStarting;
                BrowserWebView.CoreWebView2.NavigationStarting += CoreWebView2_NavigationStarting;
                BrowserWebView.CoreWebView2.NavigationCompleted += CoreWebView2_NavigationCompleted;
                BrowserWebView.CoreWebView2.DocumentTitleChanged += CoreWebView2_DocumentTitleChanged;

                // Reset tabs for new profile
                _tabs.Clear();
                // Step 2: dispose any per-tab WebViews and reset the registry to the recreated single WebView.
                foreach (var extra in _tabWebViews)
                {
                    if (ReferenceEquals(extra, BrowserWebView)) continue;
                    try
                    {
                        if (extra.CoreWebView2 != null)
                        {
                            extra.CoreWebView2.DownloadStarting -= CoreWebView2_DownloadStarting;
                            extra.CoreWebView2.NavigationStarting -= CoreWebView2_NavigationStarting;
                            extra.CoreWebView2.NavigationCompleted -= CoreWebView2_NavigationCompleted;
                            extra.CoreWebView2.DocumentTitleChanged -= CoreWebView2_DocumentTitleChanged;
                        }
                    }
                    catch { }
                    try { _webViewHost?.Children.Remove(extra); } catch { }
                    try { extra.Dispose(); } catch { }
                }
                _tabWebViews.Clear();
                _webViewHost = BrowserWebView.Parent as Panel;
                _tabWebViews.Add(BrowserWebView);

                _tabs.Add(new BrowserTab("New Tab", HomeUrl, _activeProfileId));
                _activeTab = 0;
                RefreshTabBar();

                LoadingOverlay.Visibility = Visibility.Collapsed;
                BrowserWebView.Visibility = Visibility.Visible;
                Navigate(HomeUrl);
                int fieldHash = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(BrowserWebView);
                LogProfile($"post-Navigate activeId={_activeProfileId} profileLabel={ProfileName.Text} fieldHash={fieldHash}");
            }
            catch (Exception ex)
            {
                LogProfile($"SwitchProfile EXCEPTION: {ex.GetType().Name}: {ex.Message}");
                LoadingOverlay.Visibility = Visibility.Collapsed;
                BrowserWebView.Visibility = Visibility.Visible;
            }
        }

        private void DeleteProfile(string profileId)
        {
            LogProfile($"DeleteProfile ENTER id={profileId} active={_activeProfileId} count={_profiles.Count}");
            if (_profiles.Count <= 1)
            {
                LogProfile("DeleteProfile blocked: cannot delete the last remaining profile");
                return;
            }
            if (string.Equals(profileId, "default", StringComparison.OrdinalIgnoreCase))
            {
                LogProfile("DeleteProfile blocked: cannot delete the default profile");
                return;
            }

            _profiles.RemoveAll(p => p.Id == profileId);
            SaveProfiles();
            if (_activeProfileId == profileId)
                SwitchProfile(_profiles[0].Id);
            else
                RefreshProfilesGrid();
        }

        private void AddProfileBtn_Click(object sender, RoutedEventArgs e)
        {
            LogProfile($"AddProfileBtn_Click ENTER count={_profiles.Count} pickerOpenBefore={ProfilePickerPopup.IsOpen} addOpenBefore={AddProfilePopup.IsOpen}");
            try
            {
                ProfilePickerPopup.IsOpen = false;
                _pendingProfileColor = _profileColors[_profiles.Count % _profileColors.Length];
                try { BuildColourPicker(); LogProfile($"BuildColourPicker OK children={ColourPicker.Children.Count}"); }
                catch (Exception cpx) { LogProfile($"BuildColourPicker EX: {cpx.GetType().Name}: {cpx.Message}"); }
                NewProfileNameBox.Text = "";
                AddProfilePopup.IsOpen = true;
                bool focusOk = false;
                try { focusOk = NewProfileNameBox.Focus(); } catch (Exception fx) { LogProfile($"NewProfileNameBox.Focus EX: {fx.GetType().Name}: {fx.Message}"); }
                LogProfile($"AddProfileBtn_Click EXIT addOpen={AddProfilePopup.IsOpen} focusOk={focusOk}");
            }
            catch (Exception ex)
            {
                LogProfile($"AddProfileBtn_Click EXCEPTION: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private void BuildColourPicker()
        {
            ColourPicker.Children.Clear();
            foreach (var hex in _profileColors)
            {
                var color = hex;
                var dot = new Border
                {
                    Width = 28,
                    Height = 28,
                    CornerRadius = new CornerRadius(14),
                    Background = ParseColor(color),
                    Margin = new Thickness(0, 0, 6, 0),
                    Cursor = System.Windows.Input.Cursors.Hand,
                    BorderThickness = new Thickness(2),
                    BorderBrush = color == _pendingProfileColor
                        ? Brushes.White
                        : Brushes.Transparent
                };
                dot.MouseLeftButtonDown += (s, e) =>
                {
                    _pendingProfileColor = color;
                    BuildColourPicker();
                };
                ColourPicker.Children.Add(dot);
            }
        }

        private void CancelAddProfile_Click(object sender, RoutedEventArgs e)
        {
            LogProfile("CancelAddProfile_Click ENTER");
            AddProfilePopup.IsOpen = false;
            ProfilePickerPopup.IsOpen = true;
        }

        private void ConfirmAddProfile_Click(object sender, RoutedEventArgs e)
        {
            CreateProfile();
        }

        private void NewProfileNameBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) CreateProfile();
            if (e.Key == Key.Escape) CancelAddProfile_Click(sender, e);
        }

        private void CreateProfile()
        {
            var name = NewProfileNameBox.Text.Trim();
            LogProfile($"CreateProfile ENTER nameRaw='{name}' currentCount={_profiles.Count}");
            if (string.IsNullOrWhiteSpace(name))
            {
                LogProfile("CreateProfile aborted: empty name");
                return;
            }

            string id;
            do { id = Guid.NewGuid().ToString("N")[..8]; }
            while (_profiles.Any(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase)));

            _profiles.Add(new BrowserProfile(id, name, _pendingProfileColor));
            SaveProfiles();
            LogProfile($"CreateProfile created id={id} name={name} newCount={_profiles.Count}");
            AddProfilePopup.IsOpen = false;
            SwitchProfile(id);
        }



        private void AiToggleBtn_Click(object sender, RoutedEventArgs e)
        {
            _aiPanelOpen = !_aiPanelOpen;
            AiPanelColumn.Width = _aiPanelOpen ? new GridLength(330) : new GridLength(0);
            AiToggleText.Foreground = _aiPanelOpen
                ? new SolidColorBrush(Color.FromRgb(0x00, 0xD1, 0xFF))
                : new SolidColorBrush(Color.FromRgb(0x97, 0xA7, 0xBA));
            if (_aiPanelOpen)
                UpdateAiPanel(BrowserWebView.CoreWebView2?.Source ?? "", "Ready");
        }

        private void ResearchMode_Click(object sender, RoutedEventArgs e)
        {
            AiResearch_Click(sender, e);
        }

        private void OpenRecentSessions_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var root = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "AtlasOS", "BrowserHub");
                if (!Directory.Exists(root))
                {
                    AiResultBorder.Visibility = Visibility.Visible;
                    AiResultText.Text = "No saved sessions found yet.";
                    return;
                }

                var latest = Directory.EnumerateFiles(root, "workspace_*.txt", SearchOption.TopDirectoryOnly)
                    .OrderByDescending(File.GetLastWriteTimeUtc)
                    .FirstOrDefault();
                if (string.IsNullOrWhiteSpace(latest))
                {
                    AiResultBorder.Visibility = Visibility.Visible;
                    AiResultText.Text = "No saved sessions found yet.";
                    return;
                }

                var text = File.ReadAllText(latest);
                var urls = Regex.Matches(text, @"https?://\S+")
                    .Select(m => m.Value.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (urls.Count == 0)
                {
                    AiResultBorder.Visibility = Visibility.Visible;
                    AiResultText.Text = "Session file found, but no URLs were detected.";
                    return;
                }

                _tabs.Clear();
                foreach (var url in urls)
                    _tabs.Add(new BrowserTab("Restored Tab", url, _activeProfileId));

                _activeTab = 0;
                RefreshTabBar();
                Navigate(_tabs[0].Url);

                AiResultBorder.Visibility = Visibility.Visible;
                AiResultText.Text = $"Reopened {urls.Count} tabs from your latest saved workspace.";
            }
            catch (Exception ex)
            {
                AiResultBorder.Visibility = Visibility.Visible;
                AiResultText.Text = $"Failed to reopen recent session: {ex.Message}";
            }
        }

        private async void AiAsk_Click(object sender, RoutedEventArgs e)
        {
            if (BrowserWebView.CoreWebView2 == null)
            {
                AiResultBorder.Visibility = Visibility.Visible;
                AiResultText.Text = "Browser Hub is still initializing. Wait for the page to load before using Ask Atlas.";
                return;
            }
            var question = AiAskBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(question)) return;

            AiResultBorder.Visibility = Visibility.Visible;
            AiResultText.Text = "Atlas is analyzing this page...";

            try
            {
                var snapshot = await GetCurrentPageSnapshotAsync(true);
                var pageTitle = string.IsNullOrWhiteSpace(snapshot.Title) ? "Current page" : snapshot.Title;
                var context = !string.IsNullOrWhiteSpace(snapshot.SelectedText)
                    ? snapshot.SelectedText
                    : snapshot.VisibleText;
                if (context.Length > 1600) context = context[..1600];

                if (string.IsNullOrWhiteSpace(context))
                {
                    AiResultText.Text = "This page has no readable text yet. Try after it fully loads.";
                    return;
                }

                var linksContext = snapshot.TopLinks.Count > 0
                    ? string.Join("\n", snapshot.TopLinks.Take(5))
                    : "(none)";

                var messages = new List<object>
                {
                    new { role = "system", content = "You are Atlas page intelligence. Answer briefly with practical bullet points based on page context." },
                    new { role = "user", content = $"Page title: {pageTitle}\nPage URL: {snapshot.Url}\nTop links:\n{linksContext}\nQuestion: {question}\nContext:\n{context}" }
                };

                var response = await AI.AIManager.SendMessageAsync("Internet", messages, 500);
                var answer = response?.Content?.Trim();
                AiResultText.Text = string.IsNullOrWhiteSpace(answer)
                    ? "Atlas could not generate a response for this page."
                    : answer;
            }
            catch (Exception ex)
            {
                AiResultText.Text = $"Ask Atlas failed: {ex.Message}";
            }
        }

        private void SaveNotes_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var notes = NotesBox.Text.Trim();
                if (string.IsNullOrWhiteSpace(notes))
                {
                    AiResultBorder.Visibility = Visibility.Visible;
                    AiResultText.Text = "Add notes before saving.";
                    return;
                }

                var root = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "AtlasOS", "BrowserHub", "Notes");
                Directory.CreateDirectory(root);

                var file = Path.Combine(root, $"notes_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
                var page = BrowserWebView.CoreWebView2?.Source ?? "";
                File.WriteAllText(file, $"Page: {page}{Environment.NewLine}{Environment.NewLine}{notes}");

                AiResultBorder.Visibility = Visibility.Visible;
                AiResultText.Text = "Notes saved to your browser workspace.";
            }
            catch (Exception ex)
            {
                AiResultBorder.Visibility = Visibility.Visible;
                AiResultText.Text = $"Failed to save notes: {ex.Message}";
            }
        }

        private static string DecodeScriptString(string scriptResult)
        {
            if (string.IsNullOrWhiteSpace(scriptResult) || scriptResult == "null") return "";
            try
            {
                return JsonSerializer.Deserialize<string>(scriptResult) ?? "";
            }
            catch
            {
                return scriptResult.Trim('"');
            }
        }

        private List<BrowserTabState> BuildTabStates()
        {
            var states = new List<BrowserTabState>(_tabs.Count);
            for (int i = 0; i < _tabs.Count; i++)
            {
                var tab = _tabs[i];
                states.Add(new BrowserTabState(i, tab.Title, tab.Url, i == _activeTab));
            }
            return states;
        }

        private BrowserPageSnapshot BuildFallbackSnapshot()
        {
            var currentUrl = BrowserWebView.CoreWebView2?.Source ?? (_activeTab >= 0 && _activeTab < _tabs.Count ? _tabs[_activeTab].Url : "");
            var currentTitle = BrowserWebView.CoreWebView2?.DocumentTitle ?? (_activeTab >= 0 && _activeTab < _tabs.Count ? _tabs[_activeTab].Title : "Current page");
            return new BrowserPageSnapshot(
                currentUrl,
                currentTitle,
                "",
                "",
                new List<string>(),
                new List<string>(),
                new List<string>(),
                _activeTab,
                BuildTabStates(),
                DateTime.UtcNow);
        }

        private async Task<BrowserPageSnapshot> CaptureCurrentPageSnapshotAsync()
        {
            if (BrowserWebView.CoreWebView2 == null)
                return BuildFallbackSnapshot();

            var script = @"(() => {
                const bodyText = (document.body && document.body.innerText) ? document.body.innerText : '';
                const selected = (window.getSelection && window.getSelection().toString ? window.getSelection().toString() : '').trim();
                const visibleText = bodyText.replace(/\s+/g, ' ').trim();

                const topLinks = [];
                const seenLinks = new Set();
                for (const a of document.querySelectorAll('a[href]')) {
                    const href = (a.href || '').trim();
                    if (!href || !/^https?:/i.test(href) || seenLinks.has(href)) continue;
                    seenLinks.add(href);
                    topLinks.push(href);
                    if (topLinks.length >= 15) break;
                }

                const mediaLinks = [];
                const seenMedia = new Set();
                const mediaCandidates = document.querySelectorAll('a[download], a[href*=""download""], a[href$="".zip""], a[href$="".rar""], a[href$="".7z""], a[href$="".torrent""], a[href$="".mp4""], a[href$="".mkv""], a[href$="".mp3""], a[href$="".wav""], a[href$="".flac""], source[src], video[src], audio[src], img[src]');
                for (const el of mediaCandidates) {
                    const value = (el.href || el.src || '').trim();
                    if (!value || !/^https?:/i.test(value) || seenMedia.has(value)) continue;
                    seenMedia.add(value);
                    mediaLinks.push(value);
                    if (mediaLinks.length >= 15) break;
                }

                const emails = [];
                const seenEmails = new Set();
                for (const a of document.querySelectorAll('a[href^=""mailto:""]')) {
                    const raw = (a.getAttribute('href') || '').replace(/^mailto:/i, '').split('?')[0].trim();
                    const key = raw.toLowerCase();
                    if (!raw || seenEmails.has(key)) continue;
                    seenEmails.add(key);
                    emails.push(raw);
                }

                const emailMatches = bodyText.match(/[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}/ig) || [];
                for (const email of emailMatches) {
                    const clean = (email || '').trim();
                    const key = clean.toLowerCase();
                    if (!clean || seenEmails.has(key)) continue;
                    seenEmails.add(key);
                    emails.push(clean);
                    if (emails.length >= 15) break;
                }

                return {
                    url: window.location.href || '',
                    title: document.title || '',
                    selectedText: selected,
                    visibleText: visibleText,
                    topLinks: topLinks,
                    mediaLinks: mediaLinks,
                    emails: emails
                };
            })();";

            try
            {
                var raw = await BrowserWebView.CoreWebView2.ExecuteScriptAsync(script);
                var payload = JsonSerializer.Deserialize<BrowserPageSnapshotPayload>(raw, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (payload == null)
                    return BuildFallbackSnapshot();

                return new BrowserPageSnapshot(
                    payload.Url ?? (BrowserWebView.CoreWebView2.Source ?? ""),
                    payload.Title ?? (BrowserWebView.CoreWebView2.DocumentTitle ?? "Current page"),
                    payload.SelectedText ?? "",
                    payload.VisibleText ?? "",
                    payload.TopLinks ?? new List<string>(),
                    payload.MediaLinks ?? new List<string>(),
                    payload.Emails ?? new List<string>(),
                    _activeTab,
                    BuildTabStates(),
                    DateTime.UtcNow);
            }
            catch
            {
                return BuildFallbackSnapshot();
            }
        }

        private async Task<BrowserPageSnapshot> GetCurrentPageSnapshotAsync(bool forceRefresh)
        {
            var currentUrl = BrowserWebView.CoreWebView2?.Source ?? "";
            var isCurrent = _currentPageSnapshot != null &&
                string.Equals(_currentPageSnapshot.Url, currentUrl, StringComparison.OrdinalIgnoreCase);

            if (!forceRefresh && isCurrent)
                return _currentPageSnapshot!;

            _currentPageSnapshot = await CaptureCurrentPageSnapshotAsync();
            return _currentPageSnapshot;
        }

        private bool TryRouteToAtlasSection(string viewName)
        {
            try
            {
                if (Window.GetWindow(this) is global::AtlasAI.CommandCenterWindow ccw)
                {
                    ccw.NavigateToView(viewName);
                    return true;
                }
            }
            catch { }
            return false;
        }

        private async Task<string> DetectAssetLinkAsync()
        {
            if (BrowserWebView.CoreWebView2 == null) return "";
            var script = @"(() => {
                const candidates = [
                    ...document.querySelectorAll('a[download], a[href*=""download""], a[href$="".zip""], a[href$="".rar""], a[href$="".7z""], a[href$="".torrent""], a[href$="".mp4""], a[href$="".mkv""], a[href$="".mp3""], a[href$="".wav""], a[href$="".flac""], source[src], video[src], audio[src]')
                ];
                for (const el of candidates) {
                    const u = el.href || el.src || '';
                    if (u && /^https?:/i.test(u)) return u;
                }
                return '';
            })();";
            return DecodeScriptString(await BrowserWebView.CoreWebView2.ExecuteScriptAsync(script));
        }

        private async Task<string> DetectEmailAsync()
        {
            if (BrowserWebView.CoreWebView2 == null) return "";
            var script = @"(() => {
                const m = document.querySelector('a[href^=""mailto:""]');
                if (m && m.getAttribute('href')) return m.getAttribute('href').replace(/^mailto:/i, '').split('?')[0];
                const txt = document.body ? (document.body.innerText || '') : '';
                const re = /[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}/ig;
                const match = txt.match(re);
                return match && match.length ? match[0] : '';
            })();";
            return DecodeScriptString(await BrowserWebView.CoreWebView2.ExecuteScriptAsync(script));
        }

        private async void SendToDownloader_Click(object sender, RoutedEventArgs e)
        {
            var asset = await DetectAssetLinkAsync();
            if (string.IsNullOrWhiteSpace(asset))
            {
                AiResultBorder.Visibility = Visibility.Visible;
                AiResultText.Text = "No direct downloadable asset detected on this page.";
                DetectedAssetHint.Text = "No direct downloadable asset detected on this page.";
                return;
            }

            try
            {
                await global::AtlasAI.Services.DownloadService.Instance.AddDownloadAsync(asset);
            }
            catch
            {
                AiResultBorder.Visibility = Visibility.Visible;
                AiResultText.Text = "Failed to create download job.";
                DetectedAssetHint.Text = $"Detected asset: {asset}";
                return;
            }

            Clipboard.SetText(asset);

            var routed = TryRouteToAtlasSection("AI DOWNLOADS");
            AiResultBorder.Visibility = Visibility.Visible;
            AiResultText.Text = routed
                ? "Download job created and sent to Downloader section. Link copied for convenience."
                : "Download job created. Downloader section routing unavailable in this host window.";
            DetectedAssetHint.Text = $"Detected asset: {asset}";
        }

        private async void CopyAssetLink_Click(object sender, RoutedEventArgs e)
        {
            var asset = await DetectAssetLinkAsync();
            if (string.IsNullOrWhiteSpace(asset))
            {
                AiResultBorder.Visibility = Visibility.Visible;
                AiResultText.Text = "No downloadable/media asset link detected.";
                return;
            }
            Clipboard.SetText(asset);
            DetectedAssetHint.Text = $"Detected asset: {asset}";
            AiResultBorder.Visibility = Visibility.Visible;
            AiResultText.Text = "Asset link copied to clipboard.";
        }

        private async void OpenInMediaHub_Click(object sender, RoutedEventArgs e)
        {
            var asset = await DetectAssetLinkAsync();
            if (!string.IsNullOrWhiteSpace(asset)) Clipboard.SetText(asset);

            var routed = TryRouteToAtlasSection("AI MEDIA CENTRE");
            AiResultBorder.Visibility = Visibility.Visible;
            AiResultText.Text = routed
                ? "Opened Media Hub. Asset link copied to clipboard for handoff."
                : "Media Hub routing unavailable in this host window.";
            if (!string.IsNullOrWhiteSpace(asset))
                DetectedAssetHint.Text = $"Detected asset: {asset}";
        }

        private async void SaveAssetLink_Click(object sender, RoutedEventArgs e)
        {
            var asset = await DetectAssetLinkAsync();
            if (string.IsNullOrWhiteSpace(asset))
            {
                AiResultBorder.Visibility = Visibility.Visible;
                AiResultText.Text = "No asset link detected to save.";
                return;
            }

            try
            {
                var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "AtlasOS", "BrowserHub");
                Directory.CreateDirectory(root);
                var file = Path.Combine(root, "workspace_assets.txt");
                File.AppendAllText(file, $"[{DateTime.Now:g}] {asset}{Environment.NewLine}");
                DetectedAssetHint.Text = $"Detected asset: {asset}";
                AiResultBorder.Visibility = Visibility.Visible;
                AiResultText.Text = "Asset link saved to Browser Hub workspace assets.";
            }
            catch (Exception ex)
            {
                AiResultBorder.Visibility = Visibility.Visible;
                AiResultText.Text = $"Failed to save asset link: {ex.Message}";
            }
        }

        private async void OpenInEmailSection_Click(object sender, RoutedEventArgs e)
        {
            var email = await DetectEmailAsync();
            if (!string.IsNullOrWhiteSpace(email)) Clipboard.SetText(email);

            var routed = TryRouteToAtlasSection("AI EMAIL");
            AiResultBorder.Visibility = Visibility.Visible;
            AiResultText.Text = routed
                ? "Opened Email section. Detected email copied to clipboard."
                : "Email section routing unavailable in this host window.";
            DetectedEmailHint.Text = string.IsNullOrWhiteSpace(email)
                ? "No email address detected on this page."
                : $"Detected email: {email}";
        }

        private async void CopyEmail_Click(object sender, RoutedEventArgs e)
        {
            var email = await DetectEmailAsync();
            if (string.IsNullOrWhiteSpace(email))
            {
                AiResultBorder.Visibility = Visibility.Visible;
                AiResultText.Text = "No email address detected on this page.";
                return;
            }
            Clipboard.SetText(email);
            DetectedEmailHint.Text = $"Detected email: {email}";
            AiResultBorder.Visibility = Visibility.Visible;
            AiResultText.Text = "Email address copied to clipboard.";
        }

        private async void CreateDraftInEmail_Click(object sender, RoutedEventArgs e)
        {
            var email = await DetectEmailAsync();
            var title = BrowserWebView.CoreWebView2?.DocumentTitle ?? "Draft";
            var page = BrowserWebView.CoreWebView2?.Source ?? "";
            var draft = string.IsNullOrWhiteSpace(email)
                ? $"Subject: {title}{Environment.NewLine}{Environment.NewLine}Reference page: {page}"
                : $"To: {email}{Environment.NewLine}Subject: {title}{Environment.NewLine}{Environment.NewLine}Reference page: {page}";
            Clipboard.SetText(draft);

            var routed = TryRouteToAtlasSection("AI EMAIL");
            AiResultBorder.Visibility = Visibility.Visible;
            AiResultText.Text = routed
                ? "Opened Email section. Draft scaffold copied to clipboard."
                : "Email section routing unavailable in this host window.";
            if (!string.IsNullOrWhiteSpace(email))
                DetectedEmailHint.Text = $"Detected email: {email}";
        }

        private void UpdateAiPanel(string url, string status)
        {
            try
            {
                var domain = Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : url;
                AiCurrentPage.Text = string.IsNullOrWhiteSpace(domain) ? "—" : domain;
                AiProgress.Value = status == "Ready" ? 95 : 30;
                ConfidenceText.Text = status == "Ready" ? "95% confident" : status;
            }
            catch { }
        }

        private static string BuildSnapshotSummaryFallback(BrowserPageSnapshot snapshot)
        {
            var selected = string.IsNullOrWhiteSpace(snapshot.SelectedText)
                ? "(none)"
                : (snapshot.SelectedText.Length > 220 ? snapshot.SelectedText[..220] + "..." : snapshot.SelectedText);

            var keyPoints = snapshot.VisibleText
                .Split(new[] { '.', '!', '?' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => s.Length > 45)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(4)
                .ToList();

            if (keyPoints.Count == 0 && !string.IsNullOrWhiteSpace(snapshot.VisibleText))
            {
                var preview = snapshot.VisibleText.Length > 260
                    ? snapshot.VisibleText[..260] + "..."
                    : snapshot.VisibleText;
                keyPoints.Add(preview);
            }

            var links = snapshot.TopLinks
                .Take(3)
                .Select(link => "- " + link)
                .ToList();

            var keyPointText = keyPoints.Count > 0
                ? string.Join("\n", keyPoints.Select(p => "- " + p))
                : "- No clear key points were detected yet.";
            var linkText = links.Count > 0
                ? string.Join("\n", links)
                : "- No notable links detected.";

            return
                $"What this page is\n" +
                $"{snapshot.Title} ({snapshot.Url})\n\n" +
                $"Key points\n" +
                $"{keyPointText}\n\n" +
                $"Focus selection\n" +
                $"{selected}\n\n" +
                $"Useful links\n" +
                $"{linkText}\n\n" +
                $"Suggested next step\n" +
                $"Use Ask Atlas on one specific section for deeper analysis.";
        }

        private async void AiSummarize_Click(object sender, RoutedEventArgs e)
        {
            if (BrowserWebView.CoreWebView2 == null)
            {
                AiResultBorder.Visibility = Visibility.Visible;
                AiResultText.Text = "Browser Hub is still initializing. Wait for the page to load before summarizing.";
                return;
            }
            AiResultBorder.Visibility = Visibility.Visible;
            AiResultText.Text = "Summarizing page content...";
            try
            {
                var snapshot = await GetCurrentPageSnapshotAsync(true);
                if (string.IsNullOrWhiteSpace(snapshot.VisibleText))
                {
                    AiResultText.Text = "No readable page content is available yet.";
                    return;
                }

                AiResultText.Text = BuildSnapshotSummaryFallback(snapshot);
            }
            catch { AiResultText.Text = "Unable to read page content."; }
        }

        private async void AiExtract_Click(object sender, RoutedEventArgs e)
        {
            if (BrowserWebView.CoreWebView2 == null)
            {
                AiResultBorder.Visibility = Visibility.Visible;
                AiResultText.Text = "Browser Hub is still initializing. Wait for the page to load before extracting data.";
                return;
            }
            AiResultBorder.Visibility = Visibility.Visible;
            AiResultText.Text = "Extracting page data...";
            try
            {
                var snapshot = await GetCurrentPageSnapshotAsync(true);
                var links = snapshot.TopLinks.Count > 0
                    ? string.Join("\n", snapshot.TopLinks.Take(10))
                    : "(none)";
                var media = snapshot.MediaLinks.Count > 0
                    ? string.Join("\n", snapshot.MediaLinks.Take(5))
                    : "(none)";
                var emails = snapshot.Emails.Count > 0
                    ? string.Join("\n", snapshot.Emails.Take(5))
                    : "(none)";
                var selected = string.IsNullOrWhiteSpace(snapshot.SelectedText)
                    ? "(none)"
                    : (snapshot.SelectedText.Length > 220 ? snapshot.SelectedText[..220] + "..." : snapshot.SelectedText);

                AiResultText.Text =
                    $"Title: {snapshot.Title}\nURL: {snapshot.Url}\nActive tab: {snapshot.ActiveTabIndex + 1}/{snapshot.Tabs.Count}\n\nSelected text:\n{selected}\n\nTop links:\n{links}\n\nMedia links:\n{media}\n\nDetected emails:\n{emails}";
            }
            catch { AiResultText.Text = "Unable to extract data."; }
        }

        private void AiCompare_Click(object sender, RoutedEventArgs e)
        {
            AiResultBorder.Visibility = Visibility.Visible;
            AiResultText.Text = $"Open tabs ({_tabs.Count}):\n\n" +
                string.Join("\n", _tabs.ConvertAll(t => $"• {t.Title}"));
        }

        private async void AiExplain_Click(object sender, RoutedEventArgs e)
        {
            if (BrowserWebView.CoreWebView2 == null) return;
            AiResultBorder.Visibility = Visibility.Visible;
            AiResultText.Text = "Reading page...";
            try
            {
                var heading = await BrowserWebView.CoreWebView2.ExecuteScriptAsync(
                    "document.querySelector('h1')?.innerText ?? document.title");
                AiResultText.Text = $"Page topic: {heading.Trim('"')}\n\nThis page appears to be about the topic above. Use AI chat for a full explanation.";
            }
            catch { AiResultText.Text = "Unable to analyze page."; }
        }

        private void AiResearch_Click(object sender, RoutedEventArgs e)
        {
            var url = BrowserWebView.CoreWebView2?.Source ?? "";
            if (!string.IsNullOrWhiteSpace(url))
                Navigate($"https://www.perplexity.ai/search?q=research+{Uri.EscapeDataString(url)}");
        }

        private void AiSave_Click(object sender, RoutedEventArgs e)
        {
            AiResultBorder.Visibility = Visibility.Visible;
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Workspace saved: {DateTime.Now:g}");
            sb.AppendLine();
            foreach (var t in _tabs)
                sb.AppendLine($"• {t.Title}\n  {t.Url}");
            AiResultText.Text = sb.ToString();

            // Save to file
            try
            {
                var path = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "AtlasOS", "BrowserHub", $"workspace_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, sb.ToString());
            }
            catch { }
        }
    }
}
