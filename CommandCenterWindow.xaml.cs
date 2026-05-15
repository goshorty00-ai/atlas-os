using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Shell;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Linq;
using System.Windows.Threading;
using System.Windows.Interop;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using AtlasAI.Views;
using AtlasAI.Controls;
using AtlasAI.Voice;
using AtlasAI.Core;
using AtlasAI.Services;
using System.Text.RegularExpressions;
using AtlasAI.UI;
using AtlasAI.SmartHome;
using System.Windows.Input;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using AtlasAI.Brain;
using Unosquare.FFME;
using Unosquare.FFME.Common;
using LibVLCSharp.Shared;
using System.Runtime.InteropServices;

namespace AtlasAI
{
    /// <summary>
    /// Command Center Window - Main application window replacing MainWindow
    /// Provides tab-based navigation for 6 specialized views
    /// </summary>
    public partial class CommandCenterWindow : Window
    {
        private readonly Dictionary<string, UserControl> _viewCache;
        private string _currentTab = "AI Chat";
        private readonly VoiceManager _voiceManager;
        private bool _startupGreetingSpoken;
        private bool _chatGreetingSpoken;
        private int _chatGreetingState; // 0 = not started, 1 = running, 2 = completed
        private int _chatGreetingRetryScheduled; // 0 = no, 1 = yes
        private bool _startupPreloadMode;
        private bool _startupActivationCompleted;
        private bool _sidebarCollapsed;
        private TaskbarIconHelper? _tray;
        private bool _initialShellLoaded;

        private DispatcherTimer? _prefsPollTimer;
        private string _lastSeenWallpaperPath = "";
        private string _lastSeenWallpaperMode = "";
        private string _lastWallpaperApplyToastPath = "";
        private DateTime _lastWallpaperApplyToastUtc = DateTime.MinValue;
        private const string ChatSubHeaderLogoPackUri = "pack://application:,,,/Assets/Logos/AtlasAiChatLogo.png";
        private string _activeSectionKey = "Chat";
        private bool _voiceStateHooked;
        private Action<string>? _priorCodeMicHandler;
        private bool _codeMicCaptureArmed;
        private DispatcherTimer? _codeMicRestoreTimer;
        private Action<string>? _priorEmailMicHandler;
        private bool _emailMicCaptureArmed;
        private bool _emailMicTranscriptCaptured;
        private DispatcherTimer? _emailMicRestoreTimer;
        private DispatcherTimer? _sectionVoiceNoteTimer;
        private DispatcherTimer? _arrowPollTimer;
        private POINT _lastArrowCursorPos;
        private DateTime _lastArrowMouseMove = DateTime.MinValue;
        private const double ArrowHideAfterSeconds = 3.0;

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X; public int Y; }
        [StructLayout(LayoutKind.Sequential)]
        private struct WINRECT { public int Left, Top, Right, Bottom; }
        [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT pt);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out WINRECT rect);

        public VoiceManager VoiceManager => _voiceManager;
        public string CurrentTab => _currentTab;

        public CommandCenterWindow()
        {
            InitializeComponent();
            RenderOptions.ProcessRenderMode = RenderMode.Default;
            
            // Initialize view cache for instant tab switching
            _viewCache = new Dictionary<string, UserControl>();

            _voiceManager = new VoiceManager();

            TopNavBar.TabChanged += (s, tab) => LoadView(tab);
            TopNavBar.HeaderToggleRequested += (_, __) => ToggleHeader();

            LeftSidebar.SidebarItemClicked += LeftSidebar_ItemClicked;
            
            try { ClipboardManager.Initialize(); } catch { }
            ClipboardManager.ClipboardChanged += OnClipboardChangedPrompt;
            
            try { ToastNotificationManager.Instance.Initialize(FindName("ToastContainer") as System.Windows.Controls.StackPanel ?? null!); } catch { }

            try { InitializeSmartSuggestionMode(); } catch { }
            
            Loaded += CommandCenterWindow_Loaded;

            // Poll mouse position to auto-show/hide the header restore arrow (handles WebView2 HWND airspace).
            _arrowPollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
            _arrowPollTimer.Tick += ArrowPollTimer_Tick;
            _arrowPollTimer.Start();

            TrySetWindowBuildStamp();
            try
            {
                PreferencesStore.Instance.PreferencesChanged += (_, prefs) =>
                {
                    try
                    {
                        Dispatcher.BeginInvoke(new Action(() =>
                        {
                            try { TryLoadWallpaper(prefs); } catch { }
                        }), DispatcherPriority.Background);
                    }
                    catch
                    {
                    }
                };
            }
            catch
            {
            }

            TryLoadWallpaper(PreferencesStore.Instance.Current);

            // Fallback sync: in some environments (single-instance handoff, early-load races)
            // consumers can miss PreferencesChanged. Polling keeps wallpaper reliably connected.
            try
            {
                _prefsPollTimer = new DispatcherTimer(DispatcherPriority.Background)
                {
                    Interval = TimeSpan.FromSeconds(5)
                };
                _prefsPollTimer.Tick += (_, __) =>
                {
                    try
                    {
                        var p = PreferencesStore.Instance.Current;
                        var path = (p.CommandCenterWallpaperPath ?? "").Trim();
                        var mode = (p.CommandCenterWallpaperMode ?? "").Trim();
                        if (string.Equals(path, _lastSeenWallpaperPath, StringComparison.OrdinalIgnoreCase) &&
                            string.Equals(mode, _lastSeenWallpaperMode, StringComparison.OrdinalIgnoreCase))
                            return;

                        _lastSeenWallpaperPath = path;
                        _lastSeenWallpaperMode = mode;
                        TryLoadWallpaper(p);
                    }
                    catch
                    {
                    }
                };
                _prefsPollTimer.Start();
            }
            catch
            {
            }

            try
            {
                Closed += (_, __) =>
                {
                    try { _prefsPollTimer?.Stop(); } catch { }
                    _prefsPollTimer = null;
                    try { UnhookSectionVoiceState(); } catch { }
                    try { RestoreCodeMicHandler(); } catch { }
                    try { RestoreEmailMicHandler(); } catch { }
                    try { _tray?.Dispose(); } catch { }
                    _tray = null;
                };
            }
            catch
            {
            }
        }

        private void CommandCenterWindow_Loaded(object sender, RoutedEventArgs e)
        {
            if (_initialShellLoaded)
                return;

            _initialShellLoaded = true;

            Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    LoadView("AI CHAT");
                    LeftSidebar.SetActiveButton("Chat");
                }
                catch
                {
                }

                if (!_startupPreloadMode)
                    BeginStartupActivationWork();
            }), DispatcherPriority.Background);
        }

        public void SetStartupPreloadMode(bool preloadMode)
        {
            _startupPreloadMode = preloadMode;

            try
            {
                if (preloadMode)
                {
                    try { _tray?.Dispose(); } catch { }
                    _tray = null;
                    ShowInTaskbar = false;
                    ShowActivated = false;
                    Visibility = Visibility.Hidden;
                    Opacity = 0;
                }
            }
            catch
            {
            }
        }

        public void CompleteStartupActivation()
        {
            _startupPreloadMode = false;

            try
            {
                ShowActivated = true;
                ShowInTaskbar = true;
                Visibility = Visibility.Visible;
                Opacity = 1;
            }
            catch
            {
            }

            EnsureTrayIcon();

            BeginStartupActivationWork();
        }

        private void EnsureTrayIcon()
        {
            if (_tray != null)
                return;

            try { _tray = new TaskbarIconHelper(this); } catch { }
        }

        private void BeginStartupActivationWork()
        {
            if (_startupActivationCompleted)
                return;

            _startupActivationCompleted = true;

            Dispatcher.BeginInvoke(new Action(() =>
            {
                try { TrySuggestMostUsedModuleOnStartup(); } catch { }
                try { ConnectToPresence(); } catch { }
                try { _ = InitializeVoiceSystemAsync(); } catch { }
                try
                {
                    if (string.Equals(_currentTab, "AI CHAT", StringComparison.OrdinalIgnoreCase))
                        _ = TrySpeakChatGreetingAsync();
                }
                catch { }
            }), DispatcherPriority.Background);
        }

        private void TrySetWindowBuildStamp()
        {
            try
            {
                var asm = System.Reflection.Assembly.GetExecutingAssembly();
                var path = asm.Location ?? "";
                var t = System.IO.File.Exists(path) ? System.IO.File.GetLastWriteTime(path) : DateTime.Now;
                var dev = path.Contains("_devrun", StringComparison.OrdinalIgnoreCase) ? "DEV" : "RUN";
                Title = $"Atlas AI Command Center ({dev} {t:HH:mm:ss})";
            }
            catch
            {
            }
        }

        public async Task OpenChatHistorySessionAsync(string sessionId)
        {
            LoadView("AI CHAT");
            LeftSidebar.SetActiveButton("Chat");

            if (_viewCache.TryGetValue("AI CHAT", out var view) && view is Views.AiChat.AiChatView aiChatView)
                await aiChatView.LoadSessionAsync(sessionId);
        }

        public async Task StartNewChatSessionAsync()
        {
            LoadView("AI CHAT");
            LeftSidebar.SetActiveButton("Chat");

            if (_viewCache.TryGetValue("AI CHAT", out var view) && view is Views.AiChat.AiChatView aiChatView)
                await aiChatView.StartNewSessionAsync();
        }

        public async Task PrepareRemoteChatSurfaceAsync(bool startNewConversation)
        {
            LoadView("AI CHAT");
            LeftSidebar.SetActiveButton("Chat");

            if (_viewCache.TryGetValue("AI CHAT", out var view) && view is Views.AiChat.AiChatView aiChatView)
            {
                await aiChatView.PrepareRemoteConversationAsync(startNewConversation);
            }
        }

        public async Task PresentRemoteConversationTurnAsync(string userMessage, string assistantReply, bool startNewConversation)
        {
            LoadView("AI CHAT");
            LeftSidebar.SetActiveButton("Chat");

            if (_viewCache.TryGetValue("AI CHAT", out var view) && view is Views.AiChat.AiChatView aiChatView)
            {
                await aiChatView.PresentRemoteConversationTurnAsync(userMessage, assistantReply, startNewConversation);
            }
        }

        private string _lastDownloadSuggestionJobId = "";
        private DateTime _lastDownloadSuggestionUtc = DateTime.MinValue;
        private void InitializeSmartSuggestionMode()
        {
            try
            {
                DownloadService.Instance.JobErrored -= DownloadService_JobErrored;
                DownloadService.Instance.JobErrored += DownloadService_JobErrored;
            }
            catch
            {
            }
        }

        private void DownloadService_JobErrored(object? sender, DownloadService.DownloadJobErrorEventArgs e)
        {
            try
            {
                if (e?.Job == null) return;
                if (!e.LooksLikeTimeout) return;

                var now = DateTime.UtcNow;
                var id = (e.Job.Id ?? "").Trim();
                if (!string.IsNullOrWhiteSpace(id) &&
                    string.Equals(id, _lastDownloadSuggestionJobId, StringComparison.OrdinalIgnoreCase) &&
                    (now - _lastDownloadSuggestionUtc).TotalSeconds < 20)
                    return;

                _lastDownloadSuggestionJobId = id;
                _lastDownloadSuggestionUtc = now;

                Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        var name = (e.Job.DisplayName ?? "download").Trim();
                        if (string.IsNullOrWhiteSpace(name)) name = "download";

                        ToastNotificationManager.Instance.ShowAction(
                            $"That looks like a timeout. Retry {name} with smaller chunks?",
                            "Retry",
                            () =>
                            {
                                try { DownloadService.Instance.RetryWithSmallerChunks(e.Job); } catch { }
                                try { LoadView("AI DOWNLOADS"); LeftSidebar.SetActiveButton("Downloads"); } catch { }
                            },
                            "Ignore",
                            null,
                            ToastType.Warning,
                            12000);
                    }
                    catch
                    {
                    }
                }), DispatcherPriority.Background);

                try { HookSectionVoiceState(); } catch { }
                try { UpdateSectionLocalVoiceUi(); } catch { }
            }
            catch
            {
            }
        }
        
        private DispatcherTimer? _wallpaperLoopTimer;
        private System.Windows.Controls.MediaElement? _wallpaperA;
        private System.Windows.Controls.MediaElement? _wallpaperB;
        private bool _wallpaperIsAActive;
        
        private string? _lastPromptedClipboard;
        private DateTime _lastPromptedUtc = DateTime.MinValue;
        private void OnClipboardChangedPrompt(ClipboardItem item)
        {
            try
            {
                var s = (item?.Content ?? "").Trim();
                if (string.IsNullOrWhiteSpace(s)) return;

                // Link grabber: accept plain URLs or arbitrary text that contains one or more URLs.
                // (JDownloader-like behavior for clipboard.)
                var urls = new List<string>();
                try
                {
                    foreach (Match m in Regex.Matches(s, @"https?://[^\s""'<>\)\]]+", RegexOptions.IgnoreCase))
                    {
                        var u = (m.Value ?? "").Trim();
                        if (!string.IsNullOrWhiteSpace(u)) urls.Add(u);
                    }
                }
                catch
                {
                    urls.Clear();
                }

                if (urls.Count == 0)
                {
                    // Fall back to the old strict behavior.
                    if (!Regex.IsMatch(s, @"^https?://\S+$", RegexOptions.IgnoreCase)) return;
                    urls.Add(s);
                }

                if (string.Equals(s, _lastPromptedClipboard, StringComparison.OrdinalIgnoreCase) && (DateTime.UtcNow - _lastPromptedUtc).TotalSeconds < 10) return;
                _lastPromptedClipboard = s;
                _lastPromptedUtc = DateTime.UtcNow;

                // Deduplicate against current jobs.
                var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                try
                {
                    foreach (var j in DownloadService.Instance.DownloadJobs)
                    {
                        var u = (j?.Url ?? "").Trim();
                        if (!string.IsNullOrWhiteSpace(u)) existing.Add(u);
                    }
                }
                catch
                {
                }

                var toAdd = urls
                    .Select(u => (u ?? "").Trim())
                    .Where(u => !string.IsNullOrWhiteSpace(u))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Where(u => !existing.Contains(u))
                    .ToList();

                if (toAdd.Count == 0) return;

                try
                {
                    if (toAdd.Count == 1) DownloadService.Instance.AddDownload(toAdd[0]);
                    else _ = DownloadService.Instance.AddDownloadsAsync(toAdd);
                }
                catch
                {
                }

                try { LoadView("AI DOWNLOADS"); LeftSidebar.SetActiveButton("Downloads"); } catch { }
                try { ToastNotificationManager.Instance.Show($"Added {toAdd.Count} link(s) from clipboard", ToastType.Success, 3200); } catch { }
                try { _tray?.ShowBalloon("Atlas AI", $"Added {toAdd.Count} link(s) to Downloads from clipboard"); } catch { }
            }
            catch
            {
            }
        }

        private void SetSidebarCollapsed(bool collapsed)
        {
            try
            {
                _sidebarCollapsed = collapsed;

                if (collapsed)
                {
                    SidebarColumn.Width = new GridLength(0);
                    LeftSidebar.Visibility = Visibility.Collapsed;
                    ContentArea.Margin = new Thickness(0);
                    try
                    {
                        Grid.SetColumn(SidebarToggleButton, 1);
                        SidebarToggleButton.HorizontalAlignment = HorizontalAlignment.Left;
                        SidebarToggleButton.Margin = new Thickness(2, 0, 0, 0);
                    }
                    catch
                    {
                    }
                    try { SidebarToggleButton.Content = "⟩"; } catch { }
                }
                else
                {
                    SidebarColumn.Width = new GridLength(90);
                    LeftSidebar.Visibility = Visibility.Visible;
                    ContentArea.Margin = new Thickness(0);
                    try
                    {
                        Grid.SetColumn(SidebarToggleButton, 0);
                        SidebarToggleButton.HorizontalAlignment = HorizontalAlignment.Right;
                        SidebarToggleButton.Margin = new Thickness(0);
                    }
                    catch
                    {
                    }
                    try { SidebarToggleButton.Content = "⟨"; } catch { }
                }
            }
            catch
            {
            }
        }

        private void SidebarHide_Click(object sender, RoutedEventArgs e)
        {
            try { SetSidebarCollapsed(true); } catch { }
        }

        private void SidebarShow_Click(object sender, RoutedEventArgs e)
        {
            try { SetSidebarCollapsed(false); } catch { }
        }

        private void TopNavBar_UnloadRequested(object sender, EventArgs e)
        {
            try { UnloadCurrentTab(); } catch { }
        }

        private void SidebarToggleButton_Click(object sender, RoutedEventArgs e)
        {
            try { SetSidebarCollapsed(!_sidebarCollapsed); } catch { }
        }

        private void ShowHeaderArrowButton_Click(object sender, RoutedEventArgs e)
        {
            // Restore header if it was collapsed
            if (_headerCollapsed) ToggleHeader();
            // Hide the show-arrow itself
            try
            {
                if (sender is System.Windows.Controls.Button btn)
                    btn.Visibility = Visibility.Collapsed;
            }
            catch { }
        }

        private bool _headerCollapsed;
        public bool IsHeaderCollapsed => _headerCollapsed;
        public void RestoreHeader() { if (_headerCollapsed) ToggleHeader(); }
        private bool _wallpaperOnlyMode;

        private void SetWallpaperOnlyMode(bool enable)
        {
            try
            {
                _wallpaperOnlyMode = enable;

                if (MainArea != null)
                    MainArea.Visibility = enable ? Visibility.Collapsed : Visibility.Visible;

                if (enable)
                {
                    if (!_headerCollapsed)
                        ToggleHeader();
                    try { ShowHeaderArrowButton.Visibility = Visibility.Collapsed; } catch { }
                    try { WallpaperOnlyExitButton.Visibility = Visibility.Visible; } catch { }
                }
                else
                {
                    try { WallpaperOnlyExitButton.Visibility = Visibility.Collapsed; } catch { }
                    if (_headerCollapsed)
                        ToggleHeader();
                }
            }
            catch
            {
            }
        }

        public void ToggleHeader()
        {
            try
            {
                _headerCollapsed = !_headerCollapsed;
                var chrome = WindowChrome.GetWindowChrome(this) ?? new WindowChrome();
                if (_headerCollapsed)
                {
                    ShellGrid.RowDefinitions[0].Height = new GridLength(0);
                    TopNavBar.Visibility = Visibility.Collapsed;
                    try
                    {
                        Grid.SetColumn(ShowHeaderArrowButton, 1);
                        ShowHeaderArrowButton.HorizontalAlignment = HorizontalAlignment.Left;
                        ShowHeaderArrowButton.Margin = new Thickness(2, 10, 0, 0);
                        ShowHeaderArrowButton.Visibility = Visibility.Visible; // timer hides when mouse leaves
                    }
                    catch { }
                    // Stamp the collapse moment so the 3-second idle clock starts from now.
                    try
                    {
                        _lastArrowMouseMove = DateTime.UtcNow;
                        GetCursorPos(out _lastArrowCursorPos);
                    }
                    catch { }
                    chrome.CaptionHeight = 0;
                }
                else
                {
                    ShellGrid.RowDefinitions[0].Height = new GridLength(56);
                    TopNavBar.Visibility = Visibility.Visible;
                    try
                    {
                        Grid.SetColumn(ShowHeaderArrowButton, 1);
                        ShowHeaderArrowButton.HorizontalAlignment = HorizontalAlignment.Right;
                        ShowHeaderArrowButton.Margin = new Thickness(0, 10, 12, 0);
                        ShowHeaderArrowButton.Visibility = Visibility.Collapsed;
                    }
                    catch { }
                    chrome.CaptionHeight = 0;
                }
                WindowChrome.SetWindowChrome(this, chrome);

                // Also hide/restore the left sidebar with the header
                try
                {
                    if (_headerCollapsed)
                    {
                        SidebarColumn.Width = new GridLength(0);
                        LeftSidebar.Visibility = Visibility.Collapsed;
                        SidebarToggleButton.Visibility = Visibility.Collapsed;
                    }
                    else
                    {
                        // Restore sidebar to whatever state it was in before header was hidden
                        SetSidebarCollapsed(_sidebarCollapsed);
                        SidebarToggleButton.Visibility = Visibility.Visible;
                    }
                }
                catch { }

                // Notify active MediaHub WebView so the React sidebar collapses/restores with the header
                try
                {
                    if (_viewCache.TryGetValue("AI MEDIA CENTRE", out var mediaCentreView) &&
                        mediaCentreView is AtlasAI.Views.MediaCentre.ServersView sv)
                    {
                        var collapsedStr = _headerCollapsed ? "true" : "false";
                        var json = $"{{\"type\":\"mediahub.chromeCollapsed\",\"payload\":{{\"collapsed\":{collapsedStr}}}}}";
                        sv.PostToWebView(json);
                        AtlasAI.Core.AppLogger.LogInfo($"[MediaHubImmersive] headerCollapsed={_headerCollapsed} sentToWebView=true");
                    }
                    else
                    {
                        AtlasAI.Core.AppLogger.LogInfo($"[MediaHubImmersive] headerCollapsed={_headerCollapsed} sentToWebView=false (ServersView not in cache)");
                    }
                }
                catch (Exception ex) { AtlasAI.Core.AppLogger.LogInfo($"[MediaHubImmersive] ToggleHeader notify error: {ex.Message}"); }
            }
            catch { }
        }

        private void HeaderToggle_Click(object sender, RoutedEventArgs e) => ToggleHeader();

        private void ArrowPollTimer_Tick(object? sender, EventArgs e)
        {
            if (!_headerCollapsed)
            {
                if (ShowHeaderArrowButton.Visibility != Visibility.Collapsed)
                    ShowHeaderArrowButton.Visibility = Visibility.Collapsed;
                return;
            }
            try
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                if (!GetCursorPos(out var pt)) return;

                // Immediately hide if cursor has left the window (different monitor / other app).
                if (hwnd != IntPtr.Zero && GetWindowRect(hwnd, out var wr))
                {
                    bool insideWindow = pt.X >= wr.Left && pt.X < wr.Right && pt.Y >= wr.Top && pt.Y < wr.Bottom;
                    if (!insideWindow)
                    {
                        _lastArrowMouseMove = DateTime.MinValue;
                        if (ShowHeaderArrowButton.Visibility != Visibility.Collapsed)
                            ShowHeaderArrowButton.Visibility = Visibility.Collapsed;
                        return;
                    }
                }

                // Inside the window: show on movement, hide after 3 s of inactivity.
                if (pt.X != _lastArrowCursorPos.X || pt.Y != _lastArrowCursorPos.Y)
                {
                    _lastArrowCursorPos = pt;
                    _lastArrowMouseMove = DateTime.UtcNow;
                    if (ShowHeaderArrowButton.Visibility != Visibility.Visible)
                        ShowHeaderArrowButton.Visibility = Visibility.Visible;
                }
                else if (_lastArrowMouseMove != DateTime.MinValue &&
                         (DateTime.UtcNow - _lastArrowMouseMove).TotalSeconds >= ArrowHideAfterSeconds)
                {
                    if (ShowHeaderArrowButton.Visibility != Visibility.Collapsed)
                        ShowHeaderArrowButton.Visibility = Visibility.Collapsed;
                }
            }
            catch { }
        }

        private void WallpaperOnlyExitButton_Click(object sender, RoutedEventArgs e)
        {
            SetWallpaperOnlyMode(false);
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            try
            {
                if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control && e.Key == Key.H)
                {
                    ToggleHeader();
                    e.Handled = true;
                    return;
                }
                if (e.Key == Key.F6)
                {
                    ToggleHeader();
                    e.Handled = true;
                    return;
                }
            }
            catch
            {
            }
        }

        private System.Windows.FrameworkElement? _wallpaperElement;
        private string _activeWallpaperPath = "";

        private static bool _wallpaperFfmeLoaded;
        private static readonly object _wallpaperFfmeLock = new object();

        private static LibVLC? _wallpaperLibVlc;
        private static readonly object _wallpaperVlcLock = new object();
        private LibVLCSharp.Shared.MediaPlayer? _wallpaperVlcPlayer;
        private LibVLCSharp.Shared.Media? _wallpaperVlcMedia;

        private System.Windows.Controls.Image? _wallpaperVlcImage;
        private WriteableBitmap? _wallpaperVlcBitmap;
        private byte[]? _wallpaperVlcBuffer;
        private GCHandle _wallpaperVlcBufferHandle;
        private IntPtr _wallpaperVlcBufferPtr = IntPtr.Zero;
        private int _wallpaperVlcWidth;
        private int _wallpaperVlcHeight;
        private int _wallpaperVlcStride;
        private bool _wallpaperVlcFrameQueued;

        // Keep delegates alive
        private LibVLCSharp.Shared.MediaPlayer.LibVLCVideoLockCb? _vlcLock;
        private LibVLCSharp.Shared.MediaPlayer.LibVLCVideoUnlockCb? _vlcUnlock;
        private LibVLCSharp.Shared.MediaPlayer.LibVLCVideoDisplayCb? _vlcDisplay;

        private const string WallpaperTag = "CommandCenterWallpaper";

        private void ConfigureWallpaperVisual(FrameworkElement element)
        {
            element.HorizontalAlignment = HorizontalAlignment.Stretch;
            element.VerticalAlignment = VerticalAlignment.Stretch;
            element.RenderTransform = Transform.Identity;
            element.LayoutTransform = Transform.Identity;
            element.Width = double.NaN;
            element.Height = double.NaN;
            element.MaxWidth = double.PositiveInfinity;
            element.MaxHeight = double.PositiveInfinity;
        }

        private Panel? GetWallpaperHostPanel()
        {
            try
            {
                if (FindName("WallpaperHost") is Panel wallpaperHost)
                    return wallpaperHost;
            }
            catch
            {
            }

            return MainArea;
        }

        private void RemoveWallpaperVisualFromPanel(Panel? panel, UIElement? element)
        {
            try
            {
                if (panel != null && element != null && panel.Children.Contains(element))
                    panel.Children.Remove(element);
            }
            catch
            {
            }
        }

        private void ClearWallpaperElement()
        {
            try
            {
                var wallpaperPanel = GetWallpaperHostPanel();

                // Sweep-remove any previously injected wallpaper elements.
                // (This avoids getting stuck if _wallpaperElement ever gets out-of-sync.)
                try
                {
                    var toRemove = new List<UIElement>();

                    void CollectWallpaperChildren(Panel? source)
                    {
                        try
                        {
                            if (source == null)
                                return;

                            foreach (UIElement child in source.Children)
                            {
                                // Legacy safety: older builds may have injected wallpaper elements without Tag.
                                // Remove any full-bleed, non-interactive background media at ZIndex -1.
                                try
                                {
                                    if (Panel.GetZIndex(child) <= -1 && child is FrameworkElement legacyFe)
                                    {
                                        var legacyLooksLikeWallpaper = !legacyFe.IsHitTestVisible &&
                                            (child is System.Windows.Controls.MediaElement ||
                                             child is System.Windows.Controls.Image ||
                                             child is Unosquare.FFME.MediaElement);

                                        if (legacyLooksLikeWallpaper)
                                        {
                                            toRemove.Add(child);
                                            continue;
                                        }
                                    }
                                }
                                catch { }

                                if (child is FrameworkElement fe && fe.Tag is string tag &&
                                    string.Equals(tag, WallpaperTag, StringComparison.Ordinal))
                                {
                                    toRemove.Add(child);
                                }
                            }
                        }
                        catch
                        {
                        }
                    }

                    CollectWallpaperChildren(wallpaperPanel);
                    if (!ReferenceEquals(wallpaperPanel, MainArea))
                        CollectWallpaperChildren(MainArea);

                    foreach (var el in toRemove)
                    {
                        try
                        {
                            if (el is System.Windows.Controls.MediaElement me)
                            {
                                try { me.Stop(); } catch { }
                                try { me.Source = null; } catch { }
                            }

                            if (el is Unosquare.FFME.MediaElement ff)
                            {
                                try { ff.Stop(); } catch { }
                                try { ff.Close(); } catch { }
                                try { ff.Dispose(); } catch { }
                            }
                        }
                        catch { }

                        RemoveWallpaperVisualFromPanel(wallpaperPanel, el);
                        if (!ReferenceEquals(wallpaperPanel, MainArea))
                            RemoveWallpaperVisualFromPanel(MainArea, el);
                    }
                }
                catch
                {
                }

                if (_wallpaperElement != null)
                {
                    try
                    {
                        if (_wallpaperElement is System.Windows.Controls.MediaElement me)
                        {
                            try { me.Stop(); } catch { }
                            try { me.Source = null; } catch { }
                        }

                        if (_wallpaperElement is Unosquare.FFME.MediaElement ff)
                        {
                            try { ff.Stop(); } catch { }
                            try { ff.Close(); } catch { }
                            try { ff.Dispose(); } catch { }
                        }
                    }
                    catch
                    {
                    }

                    RemoveWallpaperVisualFromPanel(wallpaperPanel, _wallpaperElement);
                    if (!ReferenceEquals(wallpaperPanel, MainArea))
                        RemoveWallpaperVisualFromPanel(MainArea, _wallpaperElement);
                    _wallpaperElement = null;
                }

                try
                {
                    if (_wallpaperVlcPlayer != null)
                    {
                        try { _wallpaperVlcPlayer.EndReached -= WallpaperVlc_EndReached; } catch { }
                        try { _wallpaperVlcPlayer.EncounteredError -= WallpaperVlc_EncounteredError; } catch { }
                        try { _wallpaperVlcPlayer.Stop(); } catch { }
                        try { _wallpaperVlcPlayer.Dispose(); } catch { }
                    }
                }
                catch { }

                _wallpaperVlcPlayer = null;

                try { _wallpaperVlcMedia?.Dispose(); } catch { }
                _wallpaperVlcMedia = null;

                try
                {
                    if (_wallpaperVlcImage != null)
                    {
                        RemoveWallpaperVisualFromPanel(wallpaperPanel, _wallpaperVlcImage);
                        if (!ReferenceEquals(wallpaperPanel, MainArea))
                            RemoveWallpaperVisualFromPanel(MainArea, _wallpaperVlcImage);
                        _wallpaperVlcImage.Source = null;
                    }
                }
                catch { }

                _wallpaperVlcImage = null;
                _wallpaperVlcBitmap = null;
                _wallpaperVlcWidth = 0;
                _wallpaperVlcHeight = 0;
                _wallpaperVlcStride = 0;
                _wallpaperVlcFrameQueued = false;

                try
                {
                    if (_wallpaperVlcBufferHandle.IsAllocated)
                        _wallpaperVlcBufferHandle.Free();
                }
                catch { }

                _wallpaperVlcBuffer = null;
                _wallpaperVlcBufferPtr = IntPtr.Zero;
                _vlcLock = null;
                _vlcUnlock = null;
                _vlcDisplay = null;

                _activeWallpaperPath = "";
            }
            catch
            {
            }
        }

        private void EnsureWallpaperVlc()
        {
            try
            {
                lock (_wallpaperVlcLock)
                {
                    if (_wallpaperLibVlc != null) return;
                    try
                    {
                        // NuGet copies native VLC binaries under:
                        //   <output>/libvlc/win-x64 (or win-x86)
                        // Core.Initialize() without an explicit path can fail to locate them depending
                        // on current working directory / probing.
                        var baseDir = AppDomain.CurrentDomain.BaseDirectory ?? "";
                        var archFolder = Environment.Is64BitProcess ? "win-x64" : "win-x86";
                        var libVlcDir = "";
                        try
                        {
                            if (!string.IsNullOrWhiteSpace(baseDir))
                            {
                                var candidate = Path.Combine(baseDir, "libvlc", archFolder);
                                if (Directory.Exists(candidate))
                                    libVlcDir = candidate;
                            }
                        }
                        catch { }

                        if (!string.IsNullOrWhiteSpace(libVlcDir))
                            LibVLCSharp.Shared.Core.Initialize(libVlcDir);
                        else
                            LibVLCSharp.Shared.Core.Initialize();
                    }
                    catch { }
                    _wallpaperLibVlc = new LibVLC(
                        "--no-video-title-show",
                        "--quiet",
                        "--no-audio"
                    );
                }
            }
            catch
            {
            }
        }

        private void WallpaperVlc_EndReached(object? sender, EventArgs e)
        {
            try
            {
                Dispatcher?.BeginInvoke(new Action(async () =>
                {
                    try
                    {
                        if (_wallpaperVlcPlayer == null) return;
                        _wallpaperVlcPlayer.Stop();
                        _wallpaperVlcPlayer.Play();
                    }
                    catch { }
                }), DispatcherPriority.Background);
            }
            catch
            {
            }
        }

        private void WallpaperVlc_EncounteredError(object? sender, EventArgs e)
        {
            try { ToastNotificationManager.Instance.Show("Wallpaper video failed to play", ToastType.Error, 4000); } catch { }
        }

        private static void EnsureWallpaperFfme()
        {
            try
            {
                lock (_wallpaperFfmeLock)
                {
                    if (_wallpaperFfmeLoaded) return;
                    try { Unosquare.FFME.Library.EnableWpfMultiThreadedVideo = true; } catch { }
                    try
                    {
                        var ffmpegDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg");
                        if (Directory.Exists(ffmpegDir))
                            Unosquare.FFME.Library.FFmpegDirectory = ffmpegDir;
                    }
                    catch { }
                    try
                    {
                        Unosquare.FFME.Library.LoadFFmpeg();
                        _wallpaperFfmeLoaded = true;
                        try { AppLogger.LogInfo("[Wallpaper] FFME loaded successfully."); } catch { }
                    }
                    catch (Exception ex)
                    {
                        _wallpaperFfmeLoaded = false;
                        try { AppLogger.LogError("[Wallpaper] FFME failed to load.", ex); } catch { }
                    }
                }
            }
            catch
            {
            }
        }

        private void TryLoadWallpaper(UserPreferences prefs)
        {
            try
            {
                try { _wallpaperLoopTimer?.Stop(); _wallpaperLoopTimer = null; } catch { }
                _lastSeenWallpaperPath = (prefs?.CommandCenterWallpaperPath ?? string.Empty).Trim();
                _lastSeenWallpaperMode = (prefs?.CommandCenterWallpaperMode ?? string.Empty).Trim();
                // Keep current wallpaper until we know we can apply a new one.

                static string ResolvePreferredPath(string raw)
                {
                    try
                    {
                        var preferred = (raw ?? string.Empty).Trim();
                        if (string.IsNullOrWhiteSpace(preferred)) return string.Empty;

                        // Already a valid absolute/relative existing file.
                        if (File.Exists(preferred)) return preferred;

                        // If a label-like relative path was stored (e.g. Assets/Video_Wallpaper/foo.mp4),
                        // try to resolve it under common roots.
                        var baseDir = AppDomain.CurrentDomain.BaseDirectory ?? "";
                        if (string.IsNullOrWhiteSpace(baseDir))
                            baseDir = Directory.GetCurrentDirectory();

                        var rel = preferred.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
                        if (!string.IsNullOrWhiteSpace(baseDir))
                        {
                            var candidate = Path.Combine(baseDir, rel);
                            if (File.Exists(candidate)) return candidate;
                        }

                        // As a last resort, search by file name in known wallpaper folders.
                        var fileName = Path.GetFileName(preferred);
                        if (string.IsNullOrWhiteSpace(fileName)) return string.Empty;

                        var roots = new List<string>();
                        try { if (!string.IsNullOrWhiteSpace(baseDir)) roots.Add(baseDir); } catch { }
                        try { roots.Add(Directory.GetCurrentDirectory()); } catch { }

                        try
                        {
                            var d = new DirectoryInfo(baseDir);
                            for (int i = 0; i < 8 && d != null; i++)
                            {
                                roots.Add(d.FullName);
                                d = d.Parent;
                            }
                        }
                        catch { }

                        foreach (var r in roots.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase))
                        {
                            try
                            {
                                var v = Path.Combine(r, "Assets", "Video_Wallpaper", fileName);
                                if (File.Exists(v)) return v;
                            }
                            catch { }

                            try
                            {
                                var s = Path.Combine(r, "Assets", "Wallpaper", fileName);
                                if (File.Exists(s)) return s;
                            }
                            catch { }
                        }

                        return string.Empty;
                    }
                    catch
                    {
                        return string.Empty;
                    }
                }

                static string? FindNewest(IEnumerable<string> rootsToSearch, IEnumerable<string> exts)
                {
                    try
                    {
                        var allowed = new HashSet<string>(exts ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
                        string? bestPath = null;
                        DateTime bestTime = DateTime.MinValue;

                        foreach (var root in rootsToSearch ?? Array.Empty<string>())
                        {
                            try
                            {
                                if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
                                    continue;

                                foreach (var file in Directory.EnumerateFiles(root, "*.*", SearchOption.TopDirectoryOnly))
                                {
                                    try
                                    {
                                        var ext = Path.GetExtension(file);
                                        if (!allowed.Contains(ext))
                                            continue;

                                        var stamp = File.GetLastWriteTimeUtc(file);
                                        if (stamp > bestTime)
                                        {
                                            bestTime = stamp;
                                            bestPath = file;
                                        }
                                    }
                                    catch { }
                                }
                            }
                            catch { }
                        }

                        return bestPath;
                    }
                    catch
                    {
                        return null;
                    }
                }

                var mode = (prefs?.CommandCenterWallpaperMode ?? "Video").Trim();
                if (!string.Equals(mode, "Video", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(mode, "Still", StringComparison.OrdinalIgnoreCase))
                    mode = "Video";

                var preferredPath = ResolvePreferredPath(prefs?.CommandCenterWallpaperPath ?? string.Empty);
                if (!string.IsNullOrWhiteSpace(preferredPath) && File.Exists(preferredPath))
                {
                    if (string.Equals(_activeWallpaperPath, preferredPath, StringComparison.OrdinalIgnoreCase))
                        return;

                    try
                    {
                        var name = Path.GetFileName(preferredPath);
                        var now = DateTime.UtcNow;
                        if (!string.IsNullOrWhiteSpace(name) &&
                            (!string.Equals(_lastWallpaperApplyToastPath, preferredPath, StringComparison.OrdinalIgnoreCase) ||
                             (now - _lastWallpaperApplyToastUtc).TotalSeconds > 2))
                        {
                            _lastWallpaperApplyToastPath = preferredPath;
                            _lastWallpaperApplyToastUtc = now;
                        }
                    }
                    catch { }

                    var isVideo = preferredPath.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase) ||
                                  preferredPath.EndsWith(".wmv", StringComparison.OrdinalIgnoreCase);
                    var isStill = preferredPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                                  preferredPath.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                                  preferredPath.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) ||
                                  preferredPath.EndsWith(".webp", StringComparison.OrdinalIgnoreCase);

                    // Apply the preferred wallpaper regardless of mode mismatch.
                    // Mode controls auto-pick fallback, not whether a valid file can be used.
                    if (isVideo || isStill)
                    {
                        ClearWallpaperElement();
                        var applied = TryApplyWallpaper(preferredPath);
                        if (applied)
                            _activeWallpaperPath = preferredPath;
                        try
                        {
                            var name = Path.GetFileName(preferredPath);
                            if (!applied && !string.IsNullOrWhiteSpace(name))
                            {
                                ToastNotificationManager.Instance.Show(
                                    $"Wallpaper failed: {name}",
                                    ToastType.Error,
                                    4000);
                            }
                        }
                        catch { }
                        return;
                    }

                    if ((string.Equals(mode, "Video", StringComparison.OrdinalIgnoreCase) && isVideo) ||
                        (string.Equals(mode, "Still", StringComparison.OrdinalIgnoreCase) && isStill))
                    {
                        TryApplyWallpaper(preferredPath);
                        return;
                    }
                }

                var baseDir = AppDomain.CurrentDomain.BaseDirectory ?? "";
                var dirs = new List<string>();
                try
                {
                    var d = new DirectoryInfo(baseDir);
                    for (int i = 0; i < 8 && d != null; i++)
                    {
                        dirs.Add(d.FullName);
                        d = d.Parent;
                    }
                }
                catch { }

                try { dirs.Add(Directory.GetCurrentDirectory()); } catch { }

                var roots = dirs
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(x => x.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                string? auto = null;

                if (string.Equals(mode, "Video", StringComparison.OrdinalIgnoreCase))
                {
                    auto = FindNewest(roots.Select(r => Path.Combine(r, "Assets", "Video_Wallpaper")), new[] { ".mp4", ".wmv" })
                           ?? FindNewest(roots.Select(r => Path.Combine(r, "Assets", "Wallpaper")), new[] { ".mp4", ".wmv" })
                           ?? FindNewest(roots.Select(r => Path.Combine(r, "Assets", "Wallpaper")), new[] { ".png", ".jpg", ".jpeg", ".webp" });
                }
                else
                {
                    auto = FindNewest(roots.Select(r => Path.Combine(r, "Assets", "Wallpaper")), new[] { ".png", ".jpg", ".jpeg", ".webp" })
                           ?? FindNewest(roots.Select(r => Path.Combine(r, "Assets", "Video_Wallpaper")), new[] { ".mp4", ".wmv" })
                           ?? FindNewest(roots.Select(r => Path.Combine(r, "Assets", "Wallpaper")), new[] { ".mp4", ".wmv" });
                }

                if (string.IsNullOrWhiteSpace(auto) || !File.Exists(auto)) return;
                if (string.Equals(_activeWallpaperPath, auto, StringComparison.OrdinalIgnoreCase))
                    return;

                ClearWallpaperElement();
                if (TryApplyWallpaper(auto))
                    _activeWallpaperPath = auto;
            }
            catch
            {
            }
        }

        private bool TryApplyWallpaper(string path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return false;

                var wallpaperPanel = GetWallpaperHostPanel();
                if (wallpaperPanel == null)
                    return false;

                if (path.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".wmv", StringComparison.OrdinalIgnoreCase))
                {
                    try { AppLogger.LogInfo($"[Wallpaper] Applying video wallpaper: {path}"); } catch { }
                    // Prefer FFME first because the VLC bitmap-copy renderer can judder under load.
                    try
                    {
                        EnsureWallpaperFfme();
                        if (_wallpaperFfmeLoaded)
                        {
                            var ff = new Unosquare.FFME.MediaElement
                            {
                                LoadedBehavior = MediaPlaybackState.Manual,
                                UnloadedBehavior = MediaPlaybackState.Close,
                                IsMuted = true,
                                Volume = 0,
                                Stretch = Stretch.UniformToFill,
                                HorizontalAlignment = HorizontalAlignment.Stretch,
                                VerticalAlignment = VerticalAlignment.Stretch,
                                IsHitTestVisible = false,
                                Opacity = 1.0,
                                Tag = WallpaperTag
                            };
                            ConfigureWallpaperVisual(ff);

                            Panel.SetZIndex(ff, -1);
                            wallpaperPanel.Children.Insert(0, ff);
                            _wallpaperElement = ff;

                            ff.MediaOpened += (_, __) =>
                            {
                                try
                                {
                                    ShellGrid.Background = Brushes.Transparent;
                                }
                                catch { }

                                try { ff.Play(); } catch { }
                            };

                            ff.MediaEnded += (_, __) =>
                            {
                                try { ff.Position = TimeSpan.Zero; ff.Play(); } catch { }
                            };

                            ff.MediaFailed += (_, __) =>
                            {
                                try { ToastNotificationManager.Instance.Show("Wallpaper video failed to play", ToastType.Error, 4000); } catch { }
                            };

                            try
                            {
                                ff.Open(new Uri(path, UriKind.Absolute));
                                ff.Play();
                                try { AppLogger.LogInfo("[Wallpaper] Engine selected: FFME."); } catch { }
                                return true;
                            }
                            catch (Exception ex)
                            {
                                try { AppLogger.LogError("[Wallpaper] FFME open/play failed.", ex); } catch { }
                                try
                                {
                                    RemoveWallpaperVisualFromPanel(wallpaperPanel, ff);
                                    ff.Dispose();
                                }
                                catch { }

                                _wallpaperElement = null;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        try { AppLogger.LogError("[Wallpaper] FFME path failed before playback.", ex); } catch { }
                        // fall back to VLC below
                    }

                    // Prefer WMF before VLC now that wallpaper visuals are isolated in WallpaperHost.
                    // WMF is materially lighter than the bitmap callback renderer and reduces UI stalls.
                    try
                    {
                        var me = new System.Windows.Controls.MediaElement
                        {
                            Source = new Uri(path, UriKind.Absolute),
                            LoadedBehavior = System.Windows.Controls.MediaState.Manual,
                            UnloadedBehavior = System.Windows.Controls.MediaState.Close,
                            IsMuted = true,
                            Volume = 0,
                            Stretch = Stretch.UniformToFill,
                            HorizontalAlignment = HorizontalAlignment.Stretch,
                            VerticalAlignment = VerticalAlignment.Stretch,
                            IsHitTestVisible = false,
                            Opacity = 1.0,
                            Tag = WallpaperTag
                        };
                        ConfigureWallpaperVisual(me);

                        Panel.SetZIndex(me, -1);
                        wallpaperPanel.Children.Insert(0, me);
                        _wallpaperElement = me;

                        try
                        {
                            Dispatcher.BeginInvoke(new Action(() =>
                            {
                                try { me.Position = TimeSpan.Zero; me.Play(); } catch { }
                            }), DispatcherPriority.Background);
                        }
                        catch { }

                        me.Loaded += (_, __) => { try { me.Position = TimeSpan.Zero; me.Play(); } catch { } };
                        me.MediaOpened += (_, __) =>
                        {
                            try
                            {
                                ShellGrid.Background = Brushes.Transparent;
                                me.Position = TimeSpan.Zero;
                                me.Play();
                                try { AppLogger.LogInfo("[Wallpaper] Engine selected: WMF MediaElement."); } catch { }
                            }
                            catch { }
                        };
                        me.MediaEnded += (_, __) =>
                        {
                            try { me.Position = TimeSpan.Zero; me.Play(); } catch { }
                        };
                        me.Unloaded += (_, __) =>
                        {
                            try { me.Stop(); me.Source = null; } catch { }
                        };
                        me.MediaFailed += (_, __) =>
                        {
                            try { AppLogger.LogWarning("[Wallpaper] WMF MediaElement failed."); } catch { }
                            try { ToastNotificationManager.Instance.Show("Wallpaper video failed to play", ToastType.Error, 4000); } catch { }
                        };

                        return true;
                    }
                    catch (Exception ex)
                    {
                        try { AppLogger.LogError("[Wallpaper] WMF path failed before playback.", ex); } catch { }
                        // fall back to VLC below
                    }

                    // Final fallback: VLC bitmap callbacks.
                    // This remains available, but it is the heaviest rendering path.
                    try
                    {
                        EnsureWallpaperVlc();
                        if (_wallpaperLibVlc != null)
                        {
                            var dpi = 96.0;
                            var scaleX = 1.0;
                            var scaleY = 1.0;
                            try
                            {
                                var src = PresentationSource.FromVisual(this);
                                if (src?.CompositionTarget != null)
                                {
                                    var m = src.CompositionTarget.TransformToDevice;
                                    scaleX = m.M11;
                                    scaleY = m.M22;
                                }
                            }
                            catch { }

                            var targetWidth = (int)Math.Max(640, (ActualWidth > 0 ? ActualWidth : SystemParameters.PrimaryScreenWidth) * scaleX);
                            var targetHeight = (int)Math.Max(360, (ActualHeight > 0 ? ActualHeight : SystemParameters.PrimaryScreenHeight) * scaleY);

                            // Avoid giant framebuffers (4K/5K) which can cause VLC callbacks to stall
                            // or allocate excessively. Stretch.UniformToFill handles scaling.
                            try
                            {
                                const int maxW = 1920;
                                const int maxH = 1080;
                                var scaleDown = Math.Min(1.0, Math.Min((double)maxW / targetWidth, (double)maxH / targetHeight));
                                if (scaleDown < 1.0)
                                {
                                    targetWidth = (int)Math.Max(640, Math.Floor(targetWidth * scaleDown));
                                    targetHeight = (int)Math.Max(360, Math.Floor(targetHeight * scaleDown));
                                }
                            }
                            catch { }
                            if (targetWidth % 2 == 1) targetWidth++;
                            if (targetHeight % 2 == 1) targetHeight++;

                            _wallpaperVlcWidth = targetWidth;
                            _wallpaperVlcHeight = targetHeight;
                            _wallpaperVlcStride = targetWidth * 4;

                            _wallpaperVlcBitmap = new WriteableBitmap(_wallpaperVlcWidth, _wallpaperVlcHeight, dpi, dpi, PixelFormats.Bgr32, null);

                            _wallpaperVlcBuffer = new byte[_wallpaperVlcStride * _wallpaperVlcHeight];
                            _wallpaperVlcBufferHandle = GCHandle.Alloc(_wallpaperVlcBuffer, GCHandleType.Pinned);
                            _wallpaperVlcBufferPtr = _wallpaperVlcBufferHandle.AddrOfPinnedObject();

                            _wallpaperVlcImage = new System.Windows.Controls.Image
                            {
                                Source = _wallpaperVlcBitmap,
                                Stretch = Stretch.UniformToFill,
                                HorizontalAlignment = HorizontalAlignment.Stretch,
                                VerticalAlignment = VerticalAlignment.Stretch,
                                IsHitTestVisible = false,
                                Opacity = 1.0,
                                Tag = WallpaperTag
                            };
                            ConfigureWallpaperVisual(_wallpaperVlcImage);
                            Panel.SetZIndex(_wallpaperVlcImage, -1);
                            wallpaperPanel.Children.Insert(0, _wallpaperVlcImage);
                            _wallpaperElement = _wallpaperVlcImage;

                            try
                            {
                                ShellGrid.Background = Brushes.Transparent;
                            }
                            catch { }

                            _wallpaperVlcPlayer = new LibVLCSharp.Shared.MediaPlayer(_wallpaperLibVlc);
                            try { _wallpaperVlcPlayer.Mute = true; } catch { }
                            try { _wallpaperVlcPlayer.Volume = 0; } catch { }
                            try { _wallpaperVlcPlayer.EndReached += WallpaperVlc_EndReached; } catch { }
                            try { _wallpaperVlcPlayer.EncounteredError += WallpaperVlc_EncounteredError; } catch { }

                            _vlcLock = (opaque, planes) =>
                            {
                                try
                                {
                                    if (_wallpaperVlcBufferPtr != IntPtr.Zero)
                                        Marshal.WriteIntPtr(planes, _wallpaperVlcBufferPtr);
                                }
                                catch { }

                                return IntPtr.Zero;
                            };

                            _vlcUnlock = (opaque, picture, planes) => { };

                            _vlcDisplay = (opaque, picture) =>
                            {
                                try
                                {
                                    if (_wallpaperVlcFrameQueued) return;
                                    _wallpaperVlcFrameQueued = true;
                                    Dispatcher?.BeginInvoke(new Action(() =>
                                    {
                                        try
                                        {
                                            _wallpaperVlcFrameQueued = false;
                                            if (_wallpaperVlcBuffer == null || _wallpaperVlcWidth <= 0 || _wallpaperVlcHeight <= 0) return;
                                            if (_wallpaperVlcBitmap == null) return;
                                            _wallpaperVlcBitmap.WritePixels(
                                                new Int32Rect(0, 0, _wallpaperVlcWidth, _wallpaperVlcHeight),
                                                _wallpaperVlcBuffer,
                                                _wallpaperVlcStride,
                                                0);
                                        }
                                        catch { }
                                    }), DispatcherPriority.Render);
                                }
                                catch { }
                            };
                            _wallpaperVlcPlayer.SetVideoCallbacks(_vlcLock, _vlcUnlock, _vlcDisplay);
                            _wallpaperVlcPlayer.SetVideoFormat("RV32", (uint)_wallpaperVlcWidth, (uint)_wallpaperVlcHeight, (uint)_wallpaperVlcStride);

                            try { _wallpaperVlcMedia?.Dispose(); } catch { }
                            _wallpaperVlcMedia = new Media(_wallpaperLibVlc, path, FromType.FromPath);
                            try { _wallpaperVlcPlayer.Media = _wallpaperVlcMedia; } catch { }

                            bool started = false;
                            try { started = _wallpaperVlcPlayer.Play(); } catch (Exception ex) { AppLogger.LogError("VLC play failed", ex); started = false; }
                            if (started)
                            {
                                try { AppLogger.LogInfo("[Wallpaper] Engine selected: VLC bitmap callbacks."); } catch { }
                                return true;
                            }

                            try { AppLogger.LogWarning("[Wallpaper] VLC player did not start playback."); } catch { }
                        }
                    }
                    catch (Exception ex)
                    {
                        AppLogger.LogError("VLC initialization failed", ex);
                        // fall back
                    }

                }

                if (path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                    path.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) ||
                    path.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                    path.EndsWith(".webp", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        var bmp = new BitmapImage();
                        bmp.BeginInit();
                        bmp.CacheOption = BitmapCacheOption.OnLoad;
                        bmp.UriSource = new Uri(path, UriKind.Absolute);
                        bmp.EndInit();
                        bmp.Freeze();

                        var img = new System.Windows.Controls.Image
                        {
                            Source = bmp,
                            Stretch = Stretch.UniformToFill,
                            HorizontalAlignment = HorizontalAlignment.Stretch,
                            VerticalAlignment = VerticalAlignment.Stretch,
                            IsHitTestVisible = false,
                            Opacity = 1.0,
                            Tag = WallpaperTag
                        };
                        ConfigureWallpaperVisual(img);

                        Panel.SetZIndex(img, -1);
                        wallpaperPanel.Children.Insert(0, img);
                        _wallpaperElement = img;
                        ShellGrid.Background = Brushes.Transparent;
                        return true;
                    }
                    catch
                    {
                    }
                }
            }
            catch
            {
            }

            return false;
        }

        /// <summary>
        /// Handle tab button click
        /// </summary>
        private void Tab_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button)
            {
                // Set clicked button as active
                button.Tag = "Active";
                
                // Load corresponding view
                string tabName = button.Content.ToString() ?? "AI Chat";
                LoadView(tabName);
            }
        }
        
        /// <summary>
        /// Handle left sidebar item click
        /// </summary>
        private async void LeftSidebar_ItemClicked(object sender, string itemName)
        {
            System.Diagnostics.Debug.WriteLine($"[CommandCenter] Sidebar item clicked: {itemName}");
            try { AtlasAI.Brain.SectionAgentContext.SetActiveSection(itemName); } catch { }
            
            // Map sidebar items to tabs
            switch (itemName)
            {
                case "Chat":
                    LoadView("AI CHAT");
                    break;
                case "Media":
                    LoadView("AI MEDIA CENTRE");
                    break;
                case "Speech":
                case "Greetings":
                case "Responses":
                    LoadView("AI SPEECH STUDIO");
                    itemName = "Speech";
                    break;
                case "SmartHome":
                case "Smart Home":
                    LoadView("AI SMART HOME");
                    itemName = "SmartHome";
                    break;
                case "DJ":
                    LoadView("AI DJ BOOTH");
                    break;
                case "Downloads":
                    LoadView("AI DOWNLOADS");
                    break;
                case "API":
                    LoadView("AI API MANAGEMENT");
                    break;
                case "Internet":
                    LoadView("AI BROWSER HUB");
                    itemName = "Internet";
                    break;
                case "Email":
                    LoadView("AI EMAIL");
                    itemName = "Email";
                    break;
                case "FileExplorer":
                case "File Explorer":
                    LoadView("AI FILE EXPLORER");
                    itemName = "FileExplorer";
                    break;
                case "Security":
                    LoadView("AI SECURITY");
                    break;
                case "Create":
                    LoadView("AI CREATE");
                    break;
                case "Code":
                    LoadView("AI CODE");
                    break;
                case "AiChef":
                    LoadView("AI CHEF STUDIO");
                    break;
                case "Quiz":
                    LoadView("AI QUIZ NIGHT");
                    break;
            }
            
            // Update sidebar active state
            LeftSidebar.SetActiveButton(itemName);
        }
        
        /// <summary>
        /// Window control button handlers
        /// </summary>
        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }
        
        private void MaximizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState == WindowState.Maximized 
                ? WindowState.Normal 
                : WindowState.Maximized;
        }
        
        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        /// <summary>
        /// Navigate to a view (public entry point for child controls)
        /// </summary>
        public void NavigateToView(string viewName)
        {
            try
            {
                LoadView(viewName);
                try { LeftSidebar.SetActiveButton(GetSidebarButtonName(viewName)); } catch { }
            }
            catch { }
        }

        private static string GetSidebarButtonName(string viewName)
        {
            return (viewName ?? "").Trim().ToUpperInvariant() switch
            {
                "AI CHAT" => "Chat",
                "AI MEDIA CENTRE" => "Media Centre",
                "AI SPEECH STUDIO" => "Speech",
                "AI CUSTOM GREETINGS" => "Greetings",
                "AI CUSTOM RESPONSES" => "Responses",
                "AI SMART HOME" => "SmartHome",
                "AI DJ BOOTH" => "DJ",
                "AI DOWNLOADS" => "Downloads",
                "AI API MANAGEMENT" => "API",
                "AI BROWSER HUB" => "Internet",
                "AI INTERNET" => "Internet",
                "AI EMAIL" => "Email",
                "AI FILE EXPLORER" => "FileExplorer",
                "FILE EXPLORER" => "FileExplorer",
                "AI SECURITY" => "Security",
                "AI CREATE" => "Create",
                "AI CODE" => "Code",
                "AI CHEF STUDIO" => "AiChef",
                "AI QUIZ NIGHT" => "Quiz",
                "QUIZ" => "Quiz",
                _ => "Chat",
            };
        }

        /// <summary>
        /// Load a view with caching and fade transition
        /// </summary>
        private void LoadView(string tabName)
        {
            try
            {
                var tabKey = (tabName ?? "").Trim().ToUpperInvariant();
                if (string.IsNullOrWhiteSpace(tabKey))
                    return;

                var sectionKey = TabToSidebarKey(tabKey);
                _activeSectionKey = sectionKey;
                try { AtlasAI.Brain.SectionAgentContext.SetActiveSection(sectionKey); } catch { }
                try { TopNavBar?.SetActiveSection(sectionKey); } catch { }

                try { TopNavBar?.SetUnloadVisible(!string.Equals(tabKey, "AI CHAT", StringComparison.OrdinalIgnoreCase)); } catch { }
                try { UpdateSubHeader(tabKey); } catch { }
                try { UpdateSectionLocalVoiceUi(); } catch { }

                // If we're already showing this view, don't re-run transitions (prevents repeated greeting).
                if (string.Equals(_currentTab, tabKey, StringComparison.OrdinalIgnoreCase) &&
                    _viewCache.TryGetValue(tabKey, out var existingView) &&
                    ContentArea.Children.Contains(existingView) &&
                    existingView.Visibility == Visibility.Visible)
                {
                    return;
                }

                try
                {
                    var isImplicitStartupDefault = _viewCache.Count == 0 && string.Equals(tabKey, "AI CHAT", StringComparison.OrdinalIgnoreCase);
                    if (!isImplicitStartupDefault)
                        PreferencesStore.Instance.RecordModuleOpen(PersonalityLearningService.NormalizeModuleId(tabKey));
                }
                catch
                {
                }

                // Enable caching for instant tab switching
                if (!_viewCache.ContainsKey(tabKey))
                {
                    _viewCache[tabKey] = CreateView(tabKey);
                }

                var view = _viewCache[tabKey];
                view.HorizontalAlignment = HorizontalAlignment.Stretch;
                view.VerticalAlignment = VerticalAlignment.Stretch;

                ContentArea.BeginAnimation(OpacityProperty, null);
                ContentArea.Opacity = 1;

                if (!ContentArea.Children.Contains(view))
                {
                    var staleChildren = ContentArea.Children.OfType<UIElement>()
                        .Where(child => !ReferenceEquals(child, view))
                        .ToList();

                    foreach (var staleChild in staleChildren)
                    {
                        try { staleChild.Visibility = Visibility.Collapsed; } catch { }
                        try { staleChild.IsHitTestVisible = false; } catch { }
                        try { ContentArea.Children.Remove(staleChild); } catch { }
                    }

                    ContentArea.Children.Add(view);
                }
                else
                {
                    var staleChildren = ContentArea.Children.OfType<UIElement>()
                        .Where(child => !ReferenceEquals(child, view))
                        .ToList();

                    foreach (var staleChild in staleChildren)
                    {
                        try { staleChild.Visibility = Visibility.Collapsed; } catch { }
                        try { staleChild.IsHitTestVisible = false; } catch { }
                        try { ContentArea.Children.Remove(staleChild); } catch { }
                    }
                }

                view.Visibility = Visibility.Visible;
                view.IsHitTestVisible = true;

                // Startup greeting is suppressed here to avoid duplicate voice greetings at app launch.
                // Keep TrySpeakChatGreetingAsync for future/manual invocation paths.

                _currentTab = tabKey;
            }
            catch (Exception ex)
            {
                // Log error and show error view
                System.Diagnostics.Debug.WriteLine($"[CommandCenter] Error loading view '{tabName}': {ex.Message}");
                ShowErrorView(tabName, ex);
            }
        }

        private void UpdateSubHeader(string tabKey)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => UpdateSubHeader(tabKey));
                return;
            }

            var t = (tabKey ?? "").Trim().ToUpperInvariant();
            var showFrame = false;
            var showLogo = false;

            try
            {
                if (SubHeaderTitle != null)
                {
                    var profile = SectionAgentContext.GetProfile(_activeSectionKey);
                    SubHeaderTitle.Text = string.IsNullOrWhiteSpace(profile.DisplayName)
                        ? "Section"
                        : profile.DisplayName;
                }
            }
            catch
            {
            }

            try
            {
                if (MainArea != null)
                {
                    MainArea.Background = string.Equals(t, "AI MEDIA CENTRE", StringComparison.OrdinalIgnoreCase)
                        ? new SolidColorBrush(Color.FromRgb(0x0A, 0x0E, 0x14))
                        : Brushes.Transparent;
                }
            }
            catch
            {
            }

            try
            {
                if (SubHeaderRow != null)
                    SubHeaderRow.Height = new GridLength(0);
            }
            catch
            {
            }

            try
            {
                if (SubHeaderBar != null)
                    SubHeaderBar.Visibility = Visibility.Collapsed;
            }
            catch
            {
            }

            try
            {
                if (SubHeaderLogo != null)
                    SubHeaderLogo.Visibility = Visibility.Collapsed;
            }
            catch
            {
            }

            try { UpdateSectionLocalVoiceUi(); } catch { }
        }

        private static bool IsKnownSectionTab(string tabKey)
        {
            return tabKey switch
            {
                "AI CHAT" => true,
                "AI MEDIA CENTRE" => true,
                "AI SPEECH STUDIO" => true,
                "AI CUSTOM GREETINGS" => true,
                "AI CUSTOM RESPONSES" => true,
                "AI SMART HOME" => true,
                "AI DJ BOOTH" => true,
                "AI DOWNLOADS" => true,
                "AI API MANAGEMENT" => true,
                "AI BROWSER HUB" => true,
                "AI INTERNET" => true,
                "AI EMAIL" => true,
                "AI FILE EXPLORER" => true,
                "FILE EXPLORER" => true,
                "AI SECURITY" => true,
                "AI CREATE" => true,
                "AI CODE" => true,
                "AI CHEF STUDIO" => true,
                "AI QUIZ NIGHT" => true,
                "QUIZ" => true,
                _ => false,
            };
        }

        private void HookSectionVoiceState()
        {
            if (_voiceStateHooked)
                return;

            try { VoiceStateManager.Instance.StateChanged += VoiceStateManager_StateChanged; } catch { }
            _voiceStateHooked = true;
        }

        private void UnhookSectionVoiceState()
        {
            if (!_voiceStateHooked)
                return;

            try { VoiceStateManager.Instance.StateChanged -= VoiceStateManager_StateChanged; } catch { }
            _voiceStateHooked = false;
        }

        private void VoiceStateManager_StateChanged(object? sender, VoiceSystemState e)
        {
            try
            {
                Dispatcher?.BeginInvoke(new Action(UpdateSectionLocalVoiceUi), DispatcherPriority.Background);
            }
            catch
            {
            }
        }

        private void UpdateSectionLocalVoiceUi()
        {
            try
            {
                var section = string.IsNullOrWhiteSpace(_activeSectionKey) ? "Chat" : _activeSectionKey;
                var isCode = SectionSpeechMicStandard.IsCodeSection(section);
                var speechEnabled = SectionSpeechMicStandard.IsSpeechEnabled(section);
                var speechWired = SectionSpeechMicStandard.IsSpeechWired(section);
                var micWired = SectionSpeechMicStandard.IsMicWired(section);
                var isListening = VoiceStateManager.Instance.CurrentState == VoiceSystemState.ActiveListening
                    || VoiceStateManager.Instance.CurrentState == VoiceSystemState.FollowUpListening;

                if (SectionSpeechIconBtn != null)
                {
                    SectionSpeechIconBtn.Visibility = Visibility.Collapsed;
                    SectionSpeechIconBtn.Content = speechEnabled ? "🔊" : "🔇";
                    SectionSpeechIconBtn.Foreground = speechEnabled
                        ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#67E8F9"))
                        : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#9CA3AF"));
                    SectionSpeechIconBtn.IsEnabled = speechWired;
                    SectionSpeechIconBtn.Cursor = speechWired ? Cursors.Hand : Cursors.Arrow;
                    SectionSpeechIconBtn.ToolTip = speechWired
                        ? (speechEnabled ? "Speech enabled for this section" : "Speech disabled for this section")
                        : "Speech not wired";
                }

                if (SectionMicIconBtn != null)
                {
                    var micReady = micWired || (isCode && _codeMicCaptureArmed);
                    SectionMicIconBtn.Visibility = Visibility.Collapsed;
                    SectionMicIconBtn.Content = isListening && micReady ? "🔴" : "🎤";
                    SectionMicIconBtn.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#34D399"));
                    SectionMicIconBtn.IsEnabled = micReady;
                    SectionMicIconBtn.Cursor = micReady ? Cursors.Hand : Cursors.Arrow;
                    SectionMicIconBtn.ToolTip = micWired
                        ? (isCode ? "Capture transcript into code input" : "Start section microphone")
                        : "Mic not wired";
                }

                if (SubHeaderTitle != null)
                {
                    var profile = SectionAgentContext.GetProfile(section);
                    SubHeaderTitle.Text = string.IsNullOrWhiteSpace(profile.DisplayName)
                        ? "Section"
                        : profile.DisplayName;
                }
            }
            catch
            {
            }
        }

        private void ShowSectionVoiceNote(string text)
        {
            try
            {
                if (SectionVoiceNote == null) return;
                SectionVoiceNote.Text = text;
                SectionVoiceNote.Visibility = Visibility.Visible;

                _sectionVoiceNoteTimer?.Stop();
                if (_sectionVoiceNoteTimer == null)
                {
                    _sectionVoiceNoteTimer = new DispatcherTimer(DispatcherPriority.Background)
                    {
                        Interval = TimeSpan.FromSeconds(2.4)
                    };
                    _sectionVoiceNoteTimer.Tick += (_, __) =>
                    {
                        try
                        {
                            _sectionVoiceNoteTimer?.Stop();
                            if (SectionVoiceNote != null)
                                SectionVoiceNote.Visibility = Visibility.Collapsed;
                        }
                        catch { }
                    };
                }
                _sectionVoiceNoteTimer.Start();
            }
            catch { }
        }

        private void SectionSpeechIconBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (SectionSpeechMicStandard.IsCodeSection(_activeSectionKey))
                    return;

                if (!SectionSpeechMicStandard.IsSpeechWired(_activeSectionKey))
                {
                    ShowSectionVoiceNote("Speech not wired");
                    return;
                }

                var enabled = SectionSpeechMicStandard.ToggleSpeech(_activeSectionKey);
                UpdateSectionLocalVoiceUi();
                ToastNotificationManager.Instance.Show(
                    enabled ? "Speech enabled for this section." : "Speech disabled for this section.",
                    ToastType.Info,
                    2200);
            }
            catch
            {
            }
        }

        private void SectionMicIconBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!SectionSpeechMicStandard.IsMicWired(_activeSectionKey))
                {
                    ShowSectionVoiceNote("Mic not wired");
                    return;
                }

                if (SectionSpeechMicStandard.IsCodeSection(_activeSectionKey))
                {
                    BeginCodeMicCapture();
                    return;
                }

                if (string.Equals(_activeSectionKey, "Email", StringComparison.OrdinalIgnoreCase))
                {
                    BeginEmailMicCapture();
                    return;
                }

                VoiceSystemOrchestrator.Instance.BeginListening(ListeningSource.PushToTalk);
                ToastNotificationManager.Instance.Show("Microphone listening started.", ToastType.Info, 1800);
            }
            catch
            {
                ToastNotificationManager.Instance.Show("Mic input not wired yet in this section.", ToastType.Warning, 2800);
            }
        }

        private void BeginEmailMicCapture()
        {
            try
            {
                var orchestrator = VoiceSystemOrchestrator.Instance;
                _emailMicTranscriptCaptured = false;

                if (!_emailMicCaptureArmed)
                {
                    _priorEmailMicHandler = orchestrator.PushToTalkCommandHandler;
                    orchestrator.PushToTalkCommandHandler = OnEmailMicTranscriptCaptured;
                    _emailMicCaptureArmed = true;
                }

                if (_emailMicRestoreTimer == null)
                {
                    _emailMicRestoreTimer = new DispatcherTimer(DispatcherPriority.Background)
                    {
                        Interval = TimeSpan.FromSeconds(18)
                    };
                    _emailMicRestoreTimer.Tick += (_, __) =>
                    {
                        try
                        {
                            _emailMicRestoreTimer?.Stop();
                            if (!_emailMicTranscriptCaptured)
                                ToastNotificationManager.Instance.Show("Microphone input failed: No transcript captured", ToastType.Warning, 3200);
                            RestoreEmailMicHandler();
                        }
                        catch
                        {
                        }
                    };
                }

                try { _emailMicRestoreTimer.Stop(); } catch { }
                _emailMicRestoreTimer.Start();

                orchestrator.BeginListening(ListeningSource.PushToTalk);
                UpdateSectionLocalVoiceUi();
                ToastNotificationManager.Instance.Show("Email mic listening started.", ToastType.Info, 1600);
            }
            catch (Exception ex)
            {
                RestoreEmailMicHandler();
                var reason = ex.Message;
                if (string.IsNullOrWhiteSpace(reason))
                    reason = "Speech capture unavailable";
                ToastNotificationManager.Instance.Show($"Microphone input failed: {reason}", ToastType.Warning, 3200);
            }
        }

        private void OnEmailMicTranscriptCaptured(string text)
        {
            try
            {
                Dispatcher?.BeginInvoke(new Action(async () =>
                {
                    try
                    {
                        var transcript = (text ?? string.Empty).Trim();
                        if (string.IsNullOrWhiteSpace(transcript))
                            return;

                        _emailMicTranscriptCaptured = true;

                        if (await TryPostEmailTranscriptAsync(transcript))
                        {
                            ToastNotificationManager.Instance.Show("Transcript ready — press Run", ToastType.Success, 2400);
                        }
                        else
                        {
                            ToastNotificationManager.Instance.Show("Microphone input failed: Email command input unavailable", ToastType.Warning, 3200);
                        }
                    }
                    catch (Exception ex)
                    {
                        var reason = ex.Message;
                        if (string.IsNullOrWhiteSpace(reason))
                            reason = "Transcript dispatch failed";
                        ToastNotificationManager.Instance.Show($"Microphone input failed: {reason}", ToastType.Warning, 3200);
                    }
                    finally
                    {
                        RestoreEmailMicHandler();
                    }
                }), DispatcherPriority.Background);
            }
            catch
            {
                RestoreEmailMicHandler();
            }
        }

        private async Task<bool> TryPostEmailTranscriptAsync(string transcript)
        {
            try
            {
                if (!_viewCache.TryGetValue("AI EMAIL", out var view) || view is not AtlasAI.Modules.Email.EmailHostView emailView)
                    return false;

                await emailView.PostAgentMicTranscriptAsync(transcript);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private void RestoreEmailMicHandler()
        {
            try { _emailMicRestoreTimer?.Stop(); } catch { }

            if (!_emailMicCaptureArmed)
                return;

            try
            {
                VoiceSystemOrchestrator.Instance.PushToTalkCommandHandler = _priorEmailMicHandler;
            }
            catch
            {
            }

            _priorEmailMicHandler = null;
            _emailMicCaptureArmed = false;
            _emailMicTranscriptCaptured = false;

            try { UpdateSectionLocalVoiceUi(); } catch { }
        }

        private void BeginCodeMicCapture()
        {
            try
            {
                var orchestrator = VoiceSystemOrchestrator.Instance;

                if (!_codeMicCaptureArmed)
                {
                    _priorCodeMicHandler = orchestrator.PushToTalkCommandHandler;
                    orchestrator.PushToTalkCommandHandler = OnCodeMicTranscriptCaptured;
                    _codeMicCaptureArmed = true;
                }

                if (_codeMicRestoreTimer == null)
                {
                    _codeMicRestoreTimer = new DispatcherTimer(DispatcherPriority.Background)
                    {
                        Interval = TimeSpan.FromSeconds(14)
                    };
                    _codeMicRestoreTimer.Tick += (_, __) =>
                    {
                        try { RestoreCodeMicHandler(); } catch { }
                    };
                }

                try { _codeMicRestoreTimer.Stop(); } catch { }
                _codeMicRestoreTimer.Start();

                orchestrator.BeginListening(ListeningSource.PushToTalk);
                UpdateSectionLocalVoiceUi();
                ToastNotificationManager.Instance.Show("Code mic listening started.", ToastType.Info, 1600);
            }
            catch
            {
                RestoreCodeMicHandler();
                ToastNotificationManager.Instance.Show("Mic input not wired yet in this section.", ToastType.Warning, 2800);
            }
        }

        private void OnCodeMicTranscriptCaptured(string text)
        {
            try
            {
                Dispatcher?.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        var transcript = (text ?? string.Empty).Trim();
                        if (string.IsNullOrWhiteSpace(transcript))
                            return;

                        if (TryInjectCodeTranscript(transcript))
                        {
                            ToastNotificationManager.Instance.Show("Captured transcript inserted into Code input.", ToastType.Success, 2200);
                        }
                        else
                        {
                            ToastNotificationManager.Instance.Show($"Captured transcript: {transcript}", ToastType.Info, 4000);
                        }
                    }
                    catch
                    {
                    }
                    finally
                    {
                        try { RestoreCodeMicHandler(); } catch { }
                    }
                }), DispatcherPriority.Background);
            }
            catch
            {
                try { RestoreCodeMicHandler(); } catch { }
            }
        }

        private void RestoreCodeMicHandler()
        {
            try { _codeMicRestoreTimer?.Stop(); } catch { }

            if (!_codeMicCaptureArmed)
            {
                try { UpdateSectionLocalVoiceUi(); } catch { }
                return;
            }

            try
            {
                VoiceSystemOrchestrator.Instance.PushToTalkCommandHandler = _priorCodeMicHandler;
            }
            catch
            {
            }

            _priorCodeMicHandler = null;
            _codeMicCaptureArmed = false;

            try { UpdateSectionLocalVoiceUi(); } catch { }
        }

        private bool TryInjectCodeTranscript(string transcript)
        {
            try
            {
                if (!_viewCache.TryGetValue("AI CODE", out var view))
                    return false;

                if (view is AtlasAI.UI.Pages.CodePage codePage && codePage.TryInjectMicTranscript(transcript))
                    return true;

                if (view is not UIElement codeRoot)
                    return false;

                var input = FindDescendantByName<System.Windows.Controls.TextBox>(codeRoot, "ChatInputBox");
                if (input == null)
                    return false;

                input.Text = transcript;
                input.CaretIndex = input.Text.Length;
                input.Focus();
                Keyboard.Focus(input);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static T? FindDescendantByName<T>(DependencyObject? root, string elementName) where T : FrameworkElement
        {
            try
            {
                if (root == null)
                    return null;

                var childrenCount = VisualTreeHelper.GetChildrenCount(root);
                for (var i = 0; i < childrenCount; i++)
                {
                    var child = VisualTreeHelper.GetChild(root, i);
                    if (child is T target && string.Equals(target.Name, elementName, StringComparison.Ordinal))
                        return target;

                    var nested = FindDescendantByName<T>(child, elementName);
                    if (nested != null)
                        return nested;
                }
            }
            catch
            {
            }

            return null;
        }

        private bool TrySetSubHeaderLogo(string tabKey)
        {
            if (SubHeaderLogo == null)
                return false;

            foreach (var candidate in GetSubHeaderLogoCandidates(tabKey))
            {
                try
                {
                    var image = new BitmapImage();
                    image.BeginInit();
                    image.CacheOption = BitmapCacheOption.OnLoad;
                    image.UriSource = candidate;
                    image.EndInit();
                    image.Freeze();

                    SubHeaderLogo.Source = image;
                    return true;
                }
                catch
                {
                }
            }

            return false;
        }

        private IEnumerable<Uri> GetSubHeaderLogoCandidates(string tabKey)
        {
            if (string.Equals(tabKey, "AI CHAT", StringComparison.OrdinalIgnoreCase))
            {
                yield return new Uri(ChatSubHeaderLogoPackUri, UriKind.Absolute);
                var diskPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Logos", "AtlasAiChatLogo.png");
                if (File.Exists(diskPath))
                    yield return new Uri(diskPath, UriKind.Absolute);
                yield break;
            }

            if (!string.Equals(tabKey, "AI DJ BOOTH", StringComparison.OrdinalIgnoreCase))
                yield break;

            foreach (var fileName in new[]
                     {
                         "Atlas_Dj.png",
                         "AtlasDjLogo.png",
                         "AtlasDjLogo.jpg",
                         "AtlasDjLogo.jpeg",
                         "AtlasDjLogo.webp"
                     })
            {
                yield return new Uri($"pack://application:,,,/Assets/Logos/{fileName}", UriKind.Absolute);

                var diskPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Logos", fileName);
                if (File.Exists(diskPath))
                    yield return new Uri(diskPath, UriKind.Absolute);
            }
        }

        private void TrySuggestMostUsedModuleOnStartup()
        {
            try
            {
                var prefs = PreferencesStore.Instance.Current;
                if (!prefs.EnableProactiveSuggestions) return;

                // Rate limit: at most once per day.
                var now = DateTime.UtcNow;
                if (prefs.LastModuleSuggestionUtc != DateTime.MinValue && (now - prefs.LastModuleSuggestionUtc).TotalHours < 24)
                    return;

                var mostUsed = PersonalityLearningService.GetMostUsedModuleSnapshot(prefs);
                if (mostUsed == null) return;

                // Only suggest when there's a clear preference.
                if (mostUsed.Value.Count < 5) return;

                // Only implement the requested UX: Media Centre first.
                if (!string.Equals(mostUsed.Value.ModuleId, "AI MEDIA CENTRE", StringComparison.OrdinalIgnoreCase))
                    return;

                if (string.Equals(_currentTab, "AI MEDIA CENTRE", StringComparison.OrdinalIgnoreCase))
                    return;

                PreferencesStore.Instance.Update(p => p.LastModuleSuggestionUtc = now);

                ToastNotificationManager.Instance.ShowAction(
                    "You usually open Media Centre first. Open it now?",
                    "Open",
                    () =>
                    {
                        try { NavigateToTab("AI MEDIA CENTRE", "Media"); } catch { }
                    },
                    "Not now",
                    null,
                    ToastType.Info,
                    9000);
            }
            catch
            {
            }
        }

        public void UnloadCurrentTab()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(_currentTab)) return;
                if (!_viewCache.TryGetValue(_currentTab, out var view)) return;

                try { ContentArea.Children.Remove(view); } catch { }

                if (view is IDisposable d) { try { d.Dispose(); } catch { } }
                _viewCache.Remove(_currentTab);

                System.Diagnostics.Debug.WriteLine($"[CommandCenter] Unloaded tab: {_currentTab}");
            }
            catch
            {
            }
        }

        public void NavigateToTab(string tabName, string sidebarKey = null)
        {
            try
            {
                if (!Dispatcher.CheckAccess())
                {
                    Dispatcher.Invoke(() => NavigateToTab(tabName, sidebarKey));
                    return;
                }

                if (string.IsNullOrWhiteSpace(tabName)) return;

                LoadView(tabName);

                if (!string.IsNullOrWhiteSpace(sidebarKey))
                    LeftSidebar.SetActiveButton(sidebarKey);
                else
                    LeftSidebar.SetActiveButton(TabToSidebarKey(tabName));
            }
            catch
            {
            }
        }

        public void OpenAtlasSettings()
        {
            try
            {
                if (!Dispatcher.CheckAccess())
                {
                    Dispatcher.Invoke(OpenAtlasSettings);
                    return;
                }

                foreach (Window w in Application.Current.Windows)
                {
                    if (w is SettingsWindow sw && sw.IsLoaded)
                    {
                        sw.Activate();
                        if (sw.WindowState == WindowState.Minimized)
                            sw.WindowState = WindowState.Normal;
                        return;
                    }
                }

                var win = new SettingsWindow();
                win.Owner = this;
				win.ShowDialog();
            }
            catch
            {
            }
        }

        public void OpenAtlasSettings(string focusIntegrationId)
        {
            try
            {
                if (!Dispatcher.CheckAccess())
                {
                    Dispatcher.Invoke(() => OpenAtlasSettings(focusIntegrationId));
                    return;
                }

                SettingsWindow? settings = null;
                foreach (Window w in Application.Current.Windows)
                {
                    if (w is SettingsWindow sw && sw.IsLoaded)
                    {
                        settings = sw;
                        break;
                    }
                }

                if (settings == null)
                {
                    settings = new SettingsWindow();
                    settings.Owner = this;
                }

                try
                {
                    if (!string.IsNullOrWhiteSpace(focusIntegrationId))
                        settings.FocusIntegration(focusIntegrationId);
                }
                catch
                {
                }

                if (!settings.IsVisible)
                    settings.ShowDialog();
                else
                {
                    settings.Activate();
                    if (settings.WindowState == WindowState.Minimized)
                        settings.WindowState = WindowState.Normal;
                }
            }
            catch
            {
            }
        }

        public bool TryFocusSearch()
        {
            try
            {
                if (!Dispatcher.CheckAccess())
                    return Dispatcher.Invoke(TryFocusSearch);

                var view = GetVisibleView();
                if (view == null) return false;

                // Try known search box names first.
                var candidates = new[]
                {
                    "TopSearchBox",
                    "LegacyTopSearchBox",
                    "ServerCatalogSearchBox",
                    "SearchBox",
                    "SearchTextBox",
                };

                foreach (var n in candidates)
                {
                    if (view.FindName(n) is System.Windows.Controls.TextBox tb)
                    {
                        tb.Focus();
                        tb.SelectAll();
                        Keyboard.Focus(tb);
                        return true;
                    }
                }

                // Fallback: first TextBox whose name looks like search.
                var fallback = FindFirstSearchTextBox(view);
                if (fallback != null)
                {
                    fallback.Focus();
                    fallback.SelectAll();
                    Keyboard.Focus(fallback);
                    return true;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        private System.Windows.FrameworkElement GetVisibleView()
        {
            try
            {
                // Prefer the cached view for current tab.
                if (!string.IsNullOrWhiteSpace(_currentTab) && _viewCache.TryGetValue(_currentTab, out var cached))
                {
                    if (cached is System.Windows.FrameworkElement fe) return fe;
                }

                // Otherwise, find the visible child in the content area.
                foreach (UIElement child in ContentArea.Children)
                {
                    if (child.Visibility != Visibility.Visible) continue;
                    if (child is System.Windows.FrameworkElement fe2) return fe2;
                }
            }
            catch
            {
            }

            return null;
        }

        private static System.Windows.Controls.TextBox FindFirstSearchTextBox(DependencyObject root)
        {
            try
            {
                var count = VisualTreeHelper.GetChildrenCount(root);
                for (var i = 0; i < count; i++)
                {
                    var child = VisualTreeHelper.GetChild(root, i);
                    if (child is System.Windows.Controls.TextBox tb)
                    {
                        var name = (tb.Name ?? "").Trim();
                        if (name.IndexOf("search", StringComparison.OrdinalIgnoreCase) >= 0)
                            return tb;
                    }

                    var found = FindFirstSearchTextBox(child);
                    if (found != null) return found;
                }
            }
            catch
            {
            }

            return null;
        }

        private static string TabToSidebarKey(string tabName)
        {
            var t = (tabName ?? "").Trim().ToUpperInvariant();
            return t switch
            {
                "AI CHAT" => "Chat",
                "AI MEDIA CENTRE" => "Media",
                "AI SPEECH STUDIO" => "Speech",
                "AI CUSTOM GREETINGS" => "Greetings",
                "AI CUSTOM RESPONSES" => "Responses",
                "AI SMART HOME" => "SmartHome",
                "AI DJ BOOTH" => "DJ",
                "AI DOWNLOADS" => "Downloads",
                "AI API MANAGEMENT" => "API",
                "AI BROWSER HUB" => "Internet",
                "AI INTERNET" => "Internet",
                "AI EMAIL" => "Email",
                "AI FILE EXPLORER" => "FileExplorer",
                "FILE EXPLORER" => "FileExplorer",
                "AI SECURITY" => "Security",
                "AI CREATE" => "Create",
                "AI CODE" => "Code",
                "AI CHEF STUDIO" => "AiChef",
                "AI QUIZ NIGHT" => "Quiz",
                "QUIZ" => "Quiz",
                _ => "Chat",
            };
        }

        private async System.Threading.Tasks.Task TrySpeakChatGreetingAsync()
        {
            try
            {
                if (System.Threading.Interlocked.CompareExchange(ref _chatGreetingState, 1, 0) != 0)
                    return;

                if (_chatGreetingSpoken) return;
                if (!_voiceManager.SpeechEnabled) _voiceManager.SpeechEnabled = true;
                if (_voiceManager.Volume < 0.05) _voiceManager.Volume = 1.0;

                string userName = "there";
                try
                {
                    var cm = new Conversation.Services.ConversationManager();
                    await cm.InitializeAsync();
                    var n = (cm.GetUserName() ?? "").Trim();
                    if (!string.IsNullOrWhiteSpace(n)) userName = n;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[CommandCenter] Error getting username for greeting: {ex.Message}");
                }

                var greeting = GreetingGenerator.TryNext(GreetingContext.ChatOpen, userName, TimeSpan.FromMinutes(10));
                if (string.IsNullOrWhiteSpace(greeting))
                    greeting = "I'm here. All set. Give me a command.";

                // Mark as spoken before any awaits to prevent re-entrancy from stacking greetings.
                _chatGreetingSpoken = true;
                try
                {
                    if (_viewCache.TryGetValue("AI CHAT", out var view))
                    {
                        var vm = view.DataContext as AtlasAI.Views.AiChat.ViewModels.AiChatViewModel;
                        vm?.Messages.Add(new AtlasAI.Models.ChatMessage
                        {
                            Content = greeting,
                            IsUser = false,
                            Timestamp = DateTime.Now
                        });
                    }
                }
                catch { }

                try
                {
                    await _voiceManager.SpeakAsync(new AssistantUtterance(greeting, UtteranceSource.Conversation));
                }
                catch
                {
                    // Startup can race provider initialization. Schedule a single delayed retry for speech only.
                    if (System.Threading.Interlocked.CompareExchange(ref _chatGreetingRetryScheduled, 1, 0) == 0)
                    {
                        _ = Dispatcher.BeginInvoke(new Action(async () =>
                        {
                            try
                            {
                                await System.Threading.Tasks.Task.Delay(3000);
                                await _voiceManager.SpeakAsync(new AssistantUtterance(greeting, UtteranceSource.Conversation));
                            }
                            catch
                            {
                            }
                        }), DispatcherPriority.Background);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CommandCenter] Error in TrySpeakChatGreetingAsync: {ex.Message}");
            }
            finally
            {
                // Completed (success or fail). Keeps this from spamming.
                System.Threading.Interlocked.Exchange(ref _chatGreetingState, 2);
            }
        }

        /// <summary>
        /// Factory method to create views
        /// </summary>
        private UserControl CreateView(string tabName)
        {
            return tabName.ToUpper() switch
            {
                "AI CHAT" => new Views.AiChat.AiChatView(_voiceManager),
                "AI MEDIA CENTRE" => new Views.MediaCentre.ServersView(),
                "AI SPEECH STUDIO" => new AtlasAI.Modules.SpeechStudio.SpeechStudioHostView(),
                "AI CUSTOM GREETINGS" => new AtlasAI.Modules.SpeechStudio.SpeechStudioHostView(),
                "AI CUSTOM RESPONSES" => new AtlasAI.Modules.SpeechStudio.SpeechStudioHostView(),
                "AI SMART HOME" => new global::AtlasAI.UI.Pages.SmartHomePage(),
                "AI DJ BOOTH" => new DJ.DjConsoleView(),
                "AI DOWNLOADS" => new AtlasAI.Modules.Downloader.DownloaderHostView(),
                "AI API MANAGEMENT" => new AtlasAI.Modules.ApiManagement.ApiManagementHostView(),
                "AI BROWSER HUB" => new AtlasAI.Modules.Internet.InternetHostView(),
                "AI INTERNET" => new AtlasAI.Modules.Internet.InternetHostView(),
                "AI EMAIL" => new AtlasAI.Modules.Email.EmailHostView(),
                "AI FILE EXPLORER" => new AtlasAI.Modules.FileExplorer.FileExplorerHostView(),
                "FILE EXPLORER" => new AtlasAI.Modules.FileExplorer.FileExplorerHostView(),
                "AI SECURITY" => new SecurityControl(),
                "AI CREATE" => new CreateControl(),
                "AI CODE" => new global::AtlasAI.UI.Pages.CodePage(),
                "AI CHEF STUDIO" => new AtlasAI.Modules.AiChef.AiChefHostView(),
                "AI QUIZ NIGHT" => new AtlasAI.Modules.Quiz.QuizHostView(),
                "QUIZ" => new AtlasAI.Modules.Quiz.QuizHostView(),
                _ => CreatePlaceholderView("Unknown", "View not implemented")
            };
        }

        internal async Task<bool> ShowSmartHomeIntentAsync(SmartHomeTextCommandResult result, System.Threading.CancellationToken cancellationToken = default)
        {
            try
            {
                Exception? lastError = null;

                for (var attempt = 0; attempt < 6; attempt++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    global::AtlasAI.UI.Pages.SmartHomePage? smartHomePage = null;

                    await Dispatcher.InvokeAsync(() =>
                    {
                        LoadView("AI SMART HOME");
                        try { LeftSidebar.SetActiveButton("SmartHome"); } catch { }

                        if (_viewCache.TryGetValue("AI SMART HOME", out var view) && view is global::AtlasAI.UI.Pages.SmartHomePage page)
                            smartHomePage = page;
                    });

                    if (smartHomePage is not null)
                    {
                        try
                        {
                            await smartHomePage.ExecuteResolvedVoiceCommandAsync(result, cancellationToken);
                            System.Diagnostics.Debug.WriteLine($"[CommandCenter] Smart Home intent executed on attempt {attempt + 1}");
                            return true;
                        }
                        catch (Exception ex)
                        {
                            lastError = ex;
                            System.Diagnostics.Debug.WriteLine($"[CommandCenter] Smart Home intent attempt {attempt + 1} failed: {ex.Message}");
                        }
                    }

                    await Task.Delay(attempt == 0 ? 250 : 450, cancellationToken);
                }

                System.Diagnostics.Debug.WriteLine($"[CommandCenter] Smart Home intent failed after retries: {lastError?.Message}");
                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Create placeholder view for unimplemented views
        /// </summary>
        private UserControl CreatePlaceholderView(string title, string description)
        {
            var view = new UserControl
            {
                Background = System.Windows.Media.Brushes.Transparent
            };

            var grid = new Grid
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            var stack = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center
            };

            var titleBlock = new TextBlock
            {
                Text = title,
                FontSize = 32,
                FontWeight = FontWeights.Bold,
                Foreground = (System.Windows.Media.Brush)FindResource("AtlasCyanBrush"),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 16)
            };

            var descBlock = new TextBlock
            {
                Text = description,
                FontSize = 16,
                Foreground = (System.Windows.Media.Brush)FindResource("AtlasTextSecondaryBrush"),
                HorizontalAlignment = HorizontalAlignment.Center
            };

            stack.Children.Add(titleBlock);
            stack.Children.Add(descBlock);
            grid.Children.Add(stack);
            view.Content = grid;

            return view;
        }

        /// <summary>
        /// Show error view when view creation fails
        /// </summary>
        private void ShowErrorView(string tabName, Exception ex)
        {
            var errorView = new UserControl
            {
                Background = System.Windows.Media.Brushes.Transparent
            };

            var grid = new Grid
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            var stack = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center
            };

            var titleBlock = new TextBlock
            {
                Text = $"Error Loading {tabName}",
                FontSize = 24,
                FontWeight = FontWeights.Bold,
                Foreground = (System.Windows.Media.Brush)FindResource("AtlasErrorBrush"),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 16)
            };

            var errorBlock = new TextBlock
            {
                Text = ex.Message,
                FontSize = 14,
                Foreground = (System.Windows.Media.Brush)FindResource("AtlasTextSecondaryBrush"),
                HorizontalAlignment = HorizontalAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 600
            };

            stack.Children.Add(titleBlock);
            stack.Children.Add(errorBlock);
            grid.Children.Add(stack);
            errorView.Content = grid;

            errorView.HorizontalAlignment = HorizontalAlignment.Stretch;
            errorView.VerticalAlignment = VerticalAlignment.Stretch;

            ContentArea.Children.Clear();
            ContentArea.Children.Add(errorView);
        }

        /// <summary>
        /// Initialize voice system (placeholder)
        /// </summary>
        private async System.Threading.Tasks.Task InitializeVoiceSystemAsync()
        {
            try
            {
                // Ensure VoiceManager is fully initialized before configuration
                if (_voiceManager != null)
                {
                    await _voiceManager.WaitForInitializationAsync();
                }

                var keys = SettingsWindow.GetVoiceApiKeys() ?? new Dictionary<string, string>();
                if (keys.TryGetValue("elevenlabs", out var elevenKey) && !string.IsNullOrEmpty(elevenKey))
                {
                    _voiceManager.ConfigureProvider(VoiceProviderType.ElevenLabs, new Dictionary<string, string> { ["ApiKey"] = elevenKey });
                }
                if (keys.TryGetValue("openai", out var openAiKey) && !string.IsNullOrEmpty(openAiKey))
                {
                    _voiceManager.ConfigureProvider(VoiceProviderType.OpenAI, new Dictionary<string, string> { ["ApiKey"] = openAiKey });
                }

                // Don't force a cloud provider on startup. VoiceSelectionService will request the right provider
                // at speak-time, and VoiceManager will fall back to Windows SAPI when keys aren't configured.
                if (_voiceManager != null)
                {
                    await _voiceManager.RestoreSavedVoiceAsync();

                    var selectedProvider = SettingsWindow.GetSelectedVoiceProvider();
                    if (selectedProvider != _voiceManager.ActiveProviderType)
                    {
                        var switched = await _voiceManager.SetProviderAsync(selectedProvider);
                        if (switched)
                            await _voiceManager.RestoreSavedVoiceAsync();
                    }

                    if (_voiceManager.Volume < 0.05)
                        _voiceManager.Volume = 1.0;
                }

                try
                {
                    if (_voiceManager != null)
                        VoiceSystemOrchestrator.Instance.SetVoiceManager(_voiceManager);
                }
                catch (Exception ex)
                {
                    AppLogger.LogError("[CommandCenter] Error setting voice manager", ex);
                }

                try
                {
                    var prefs = Core.PreferencesStore.Instance?.Current;
                    if (prefs != null && prefs.EnableWakeWord)
                        _ = Task.Run(async () =>
                        {
                            try { await VoiceSystemOrchestrator.Instance.StartAsync().ConfigureAwait(false); } catch { }
                        });
                }
                catch (Exception ex)
                {
                    AppLogger.LogError("[CommandCenter] Error starting voice orchestrator", ex);
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogError("[CommandCenter] Error in InitializeVoiceSystemAsync", ex);
            }
        }

        /// <summary>
        /// Connect to presence controller (placeholder)
        /// </summary>
        private void ConnectToPresence()
        {
            // TODO: Connect to PresenceController
            System.Diagnostics.Debug.WriteLine("[CommandCenter] Presence controller connection placeholder");
        }
    }
}
