using System;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Linq;
using AtlasAI.Core;
using AtlasAI.UI;
using AtlasAI.Voice;
using AtlasAI.Brain;
using LottieSharp.WPF;
using System.Text.Json;

namespace AtlasAI.Controls
{
    /// <summary>
    /// Top Navigation Bar Control - Tab switcher for main views
    /// </summary>
    public partial class TopNavBarControl : UserControl
    {
        public event EventHandler<string>? TabChanged;
        public event EventHandler? MinimizeRequested;
        public event EventHandler? MaximizeRequested;
        public event EventHandler? CloseRequested;
        public event EventHandler? HeaderToggleRequested;
        public event EventHandler? UnloadRequested;

        private Button? _activeTab;

        private System.Windows.Threading.DispatcherTimer? _prefsPollTimer;
        private System.Windows.Threading.DispatcherTimer? _clockTimer;
        private string _lastSeenHeaderLottie = "";
        private string _lastAppliedHeaderLottiePath = "";
        private bool _voiceHooked;
        private string _activeSectionKey = "Chat";

        public TopNavBarControl()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;

            try
            {
                PreferencesStore.Instance.PreferencesChanged += PreferencesStore_PreferencesChanged;
            }
            catch
            {
            }

            // Setting FileName before/at the animation host's Loaded event tends to be more reliable
            // than waiting for the parent control's Loaded to fire.
            try
            {
                AnimatedLogoHost.Loaded += (_, __) => UpdateAnimatedLogo();
            }
            catch { }

            UpdateAnimatedLogo();

            try
            {
                _clockTimer = new System.Windows.Threading.DispatcherTimer(System.Windows.Threading.DispatcherPriority.Background)
                {
                    Interval = TimeSpan.FromSeconds(1)
                };
                _clockTimer.Tick += (_, __) => UpdateClock();
                _clockTimer.Start();
                UpdateClock();
            }
            catch
            {
            }

            // Fallback sync: keeps animation connected even if a PreferencesChanged event is missed.
            try
            {
                _prefsPollTimer = new System.Windows.Threading.DispatcherTimer(System.Windows.Threading.DispatcherPriority.Background)
                {
                    Interval = TimeSpan.FromSeconds(5)
                };
                _prefsPollTimer.Tick += (_, __) =>
                {
                    try
                    {
                        var p = PreferencesStore.Instance.Current;
                        var v = (p.ChatHeaderLottie ?? "").Trim();
                        if (string.Equals(v, _lastSeenHeaderLottie, StringComparison.OrdinalIgnoreCase))
                            return;
                        _lastSeenHeaderLottie = v;
                        UpdateAnimatedLogo();
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
        }

        public void SetUnloadVisible(bool visible)
        {
            try
            {
                if (UnloadBtn != null)
                    UnloadBtn.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            }
            catch
            {
            }
        }

        public void SetActiveSection(string? sectionKey)
        {
            _activeSectionKey = string.IsNullOrWhiteSpace(sectionKey) ? "Chat" : sectionKey.Trim();
            UpdateSectionVoiceUi();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            try { PreferencesStore.Instance.PreferencesChanged -= PreferencesStore_PreferencesChanged; } catch { }
            try { _prefsPollTimer?.Stop(); } catch { }
            _prefsPollTimer = null;

            try { _clockTimer?.Stop(); } catch { }
            _clockTimer = null;

            if (_voiceHooked)
            {
                try { VoiceStateManager.Instance.StateChanged -= VoiceStateChangedHandler; } catch { }
                _voiceHooked = false;
            }
        }

        private void PreferencesStore_PreferencesChanged(object? sender, UserPreferences e)
        {
            try
            {
                // PreferencesChanged fires for *any* preference update (wallpaper, voice, etc.).
                // Only refresh the header lottie when its backing pref actually changed.
                var v = (e?.ChatHeaderLottie ?? "").Trim();
                if (string.Equals(v, _lastSeenHeaderLottie, StringComparison.OrdinalIgnoreCase))
                    return;
                _lastSeenHeaderLottie = v;
                Dispatcher?.BeginInvoke(new Action(UpdateAnimatedLogo));
            }
            catch
            {
            }
        }

        private void OnLoaded(object? sender, RoutedEventArgs e)
        {
            UpdateAnimatedLogo();
            UpdateClock();
            UpdateSectionVoiceUi();

            if (!_voiceHooked)
            {
                try { VoiceStateManager.Instance.StateChanged += VoiceStateChangedHandler; } catch { }
                _voiceHooked = true;
            }
            UpdateVoicePill(VoiceStateManager.Instance.CurrentState);
        }

        private void SectionSpeech_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (SectionSpeechMicStandard.IsCodeSection(_activeSectionKey))
                    return;

                bool enabled = SectionSpeechMicStandard.ToggleSpeech(_activeSectionKey);
                UpdateSectionVoiceUi();

                var section = string.IsNullOrWhiteSpace(_activeSectionKey) ? "section" : _activeSectionKey;
                ToastNotificationManager.Instance.Show(
                    enabled ? $"Speech enabled for {section}." : $"Speech disabled for {section}.",
                    ToastType.Info,
                    2200);
            }
            catch
            {
            }
        }

        private void SectionMic_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (SectionSpeechMicStandard.IsCodeSection(_activeSectionKey))
                    return;

                if (!SectionSpeechMicStandard.IsMicWired(_activeSectionKey))
                {
                    ToastNotificationManager.Instance.Show("Microphone input is not wired yet in this section.", ToastType.Warning, 2800);
                    return;
                }

                VoiceSystemOrchestrator.Instance.BeginListening(ListeningSource.PushToTalk);
                ToastNotificationManager.Instance.Show("Microphone listening started.", ToastType.Info, 1800);
            }
            catch
            {
                ToastNotificationManager.Instance.Show("Microphone input is not wired yet in this section.", ToastType.Warning, 2800);
            }
        }

        private void UpdateSectionVoiceUi()
        {
            try
            {
                bool isCode = SectionSpeechMicStandard.IsCodeSection(_activeSectionKey);
                bool speechEnabled = SectionSpeechMicStandard.IsSpeechEnabled(_activeSectionKey);
                bool micWired = SectionSpeechMicStandard.IsMicWired(_activeSectionKey);

                if (SectionSpeechBtn != null)
                {
                    SectionSpeechBtn.IsEnabled = !isCode;
                    SectionSpeechBtn.Content = speechEnabled ? "SPEECH ON" : "SPEECH OFF";
                    SectionSpeechBtn.ToolTip = isCode ? "Speech controls excluded in Code section" : "Toggle section speech";
                    SectionSpeechBtn.Foreground = speechEnabled
                        ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#67E8F9"))
                        : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#9CA3AF"));
                }

                if (SectionMicBtn != null)
                {
                    SectionMicBtn.IsEnabled = !isCode;
                    SectionMicBtn.Content = micWired ? "MIC" : "MIC NA";
                    SectionMicBtn.ToolTip = isCode
                        ? "Mic controls excluded in Code section"
                        : micWired
                            ? "Start section microphone"
                            : "Microphone input is not wired yet in this section.";
                    SectionMicBtn.Foreground = micWired
                        ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#34D399"))
                        : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FCA5A5"));
                }
            }
            catch
            {
            }
        }

        private void VoiceStateChangedHandler(object? sender, VoiceSystemState e)
        {
            Dispatcher?.BeginInvoke(new Action(() => UpdateVoicePill(e)));
        }

        private void UpdateVoicePill(VoiceSystemState state)
        {
            if (VoiceStatePill == null || VoiceStatePillText == null) return;

            string text;
            Color fg;
            Color bg;
            Color border;
            Color glow;

            switch (state)
            {
                case VoiceSystemState.Disabled:
                    text = "OFF";
                    fg = (Color)ColorConverter.ConvertFromString("#9CA3AF");
                    bg = (Color)ColorConverter.ConvertFromString("#101828");
                    border = (Color)ColorConverter.ConvertFromString("#334155");
                    glow = (Color)ColorConverter.ConvertFromString("#111827");
                    break;
                case VoiceSystemState.Suspended:
                    text = "PAUSED";
                    fg = (Color)ColorConverter.ConvertFromString("#FCA5A5");
                    bg = (Color)ColorConverter.ConvertFromString("#2A0E12");
                    border = (Color)ColorConverter.ConvertFromString("#EF4444");
                    glow = (Color)ColorConverter.ConvertFromString("#EF4444");
                    break;
                case VoiceSystemState.Processing:
                    text = "THINKING";
                    fg = (Color)ColorConverter.ConvertFromString("#FDBA74");
                    bg = (Color)ColorConverter.ConvertFromString("#20170A");
                    border = (Color)ColorConverter.ConvertFromString("#F59E0B");
                    glow = (Color)ColorConverter.ConvertFromString("#F59E0B");
                    break;
                case VoiceSystemState.Speaking:
                    text = "SPEAKING";
                    fg = (Color)ColorConverter.ConvertFromString("#C4B5FD");
                    bg = (Color)ColorConverter.ConvertFromString("#140F2A");
                    border = (Color)ColorConverter.ConvertFromString("#8B5CF6");
                    glow = (Color)ColorConverter.ConvertFromString("#8B5CF6");
                    break;
                case VoiceSystemState.ActiveListening:
                case VoiceSystemState.FollowUpListening:
                    text = "LISTENING";
                    fg = (Color)ColorConverter.ConvertFromString("#67E8F9");
                    bg = (Color)ColorConverter.ConvertFromString("#07141A");
                    border = (Color)ColorConverter.ConvertFromString("#22D3EE");
                    glow = (Color)ColorConverter.ConvertFromString("#22D3EE");
                    break;
                case VoiceSystemState.PassiveListening:
                default:
                    text = "IDLE";
                    fg = (Color)ColorConverter.ConvertFromString("#7DD5F0");
                    bg = (Color)ColorConverter.ConvertFromString("#07141A");
                    border = (Color)ColorConverter.ConvertFromString("#1FB6CE");
                    glow = (Color)ColorConverter.ConvertFromString("#1FB6CE");
                    break;
            }

            VoiceStatePillText.Text = text;
            VoiceStatePillText.Foreground = new SolidColorBrush(fg);
            VoiceStatePill.Background = new SolidColorBrush(bg);
            VoiceStatePill.BorderBrush = new SolidColorBrush(border);

            if (VoiceStatePill.Effect is System.Windows.Media.Effects.DropShadowEffect ds)
                ds.Color = glow;
        }

        private void UpdateClock()
        {
            try
            {
                if (ClockText == null) return;
                var now = DateTime.Now;
                // Day + date + time (user request)
                ClockText.Text = now.ToString("ddd dd MMM HH:mm");
            }
            catch
            {
            }
        }

        private void StopVoice_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Cancel via the orchestrator (stops wake-word, listening, etc.)
                try { VoiceSystemOrchestrator.Instance.CancelCurrentAction(); } catch { }

                // Also directly stop VoiceManager on every window in case the media centre
                // or another caller bypassed the orchestrator / SpeechCoordinator.
                try
                {
                    foreach (Window win in Application.Current.Windows)
                    {
                        try
                        {
                            if (win is ChatWindow cw)
                            { cw.VoiceManager?.Stop(); }
                            else if (win is CommandCenterWindow cc)
                            { cc.VoiceManager?.Stop(); }
                        }
                        catch { }
                    }
                }
                catch { }

                // Also cancel the SpeechCoordinator directly
                try { SpeechCoordinator.Instance.CancelCurrentSpeech(); } catch { }
            }
            catch
            {
            }
        }

        private void OrbToggle_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var window = Window.GetWindow(this);
                if (TryInvokeWindowMethod(window, "OrbToggleButton_Click", sender, e))
                    return;

                var overlay = FindNamedElement(window, "OrbOverlay") as UIElement
                    ?? FindNamedElement(window, "OrbOverlayTop") as UIElement;
                if (overlay == null)
                    return;

                var nextVisible = overlay.Visibility != Visibility.Visible;
                if (!TryInvokeWindowMethod(window, "SetOrbOverlayVisible", nextVisible))
                {
                    overlay.Visibility = nextVisible ? Visibility.Visible : Visibility.Collapsed;
                    if (FindNamedElement(window, "OrbOverlayTop") is UIElement topOverlay)
                        topOverlay.Visibility = nextVisible ? Visibility.Visible : Visibility.Collapsed;
                }

                if (sender is Button button)
                    button.ToolTip = nextVisible ? "Turn orb off" : "Turn orb on";
            }
            catch
            {
            }
        }

        private void WallpaperOnly_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var window = Window.GetWindow(this);
                if (TryInvokeWindowMethod(window, "WallpaperOnlyButton_Click", sender, e))
                    return;

                var exitButton = FindNamedElement(window, "WallpaperOnlyExitButton") as UIElement;
                var enable = exitButton == null || exitButton.Visibility != Visibility.Visible;

                if (TryInvokeWindowMethod(window, "SetWallpaperOnlyMode", enable))
                    return;

                if (TryInvokeWindowMethod(window, "SetImmersiveMode", enable))
                {
                    if (exitButton != null)
                        exitButton.Visibility = enable ? Visibility.Visible : Visibility.Collapsed;
                    return;
                }

                TryInvokeWindowMethod(window, "ToggleHeader");
            }
            catch
            {
            }
        }

        private static object? FindNamedElement(Window? window, string elementName)
        {
            try
            {
                if (window == null || string.IsNullOrWhiteSpace(elementName))
                    return null;

                return window.FindName(elementName);
            }
            catch
            {
                return null;
            }
        }

        private static bool TryInvokeWindowMethod(Window? window, string methodName, params object?[] args)
        {
            try
            {
                if (window == null || string.IsNullOrWhiteSpace(methodName))
                    return false;

                var method = window.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .FirstOrDefault(candidate =>
                        string.Equals(candidate.Name, methodName, StringComparison.Ordinal) &&
                        candidate.GetParameters().Length == args.Length);

                if (method == null)
                    return false;

                method.Invoke(window, args);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private void UpdateAnimatedLogo()
        {
            var animatedLogoHost = AnimatedLogoHost;
            if (animatedLogoHost == null) return;

            void TryInvoke(object target, params string[] methodNames)
            {
                try
                {
                    var t = target.GetType();
                    foreach (var name in methodNames)
                    {
                        try
                        {
                            var m = t.GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                            if (m == null) continue;
                            if (m.GetParameters().Length != 0) continue;
                            m.Invoke(target, null);
                            return;
                        }
                        catch { }
                    }
                }
                catch { }
            }

            void SetLottieStatus(string? message)
            {
                try
                {
                    if (LottieStatusText == null) return;
                    var m = string.IsNullOrWhiteSpace(message) ? "LOTTIE OK" : message.Trim();
                    LottieStatusText.Text = m;
                }
                catch
                {
                }
            }

            void SetLottieStatusWithFile(string? message, string? path)
            {
                try
                {
                    var name = "";
                    try
                    {
                        name = string.IsNullOrWhiteSpace(path) ? "" : Path.GetFileName(path.Trim());
                    }
                    catch { name = ""; }

                    var baseMsg = string.IsNullOrWhiteSpace(message) ? "LOTTIE OK" : message.Trim();
                    if (!string.IsNullOrWhiteSpace(name))
                        baseMsg = $"{baseMsg} · {name}";
                    SetLottieStatus(baseMsg);
                }
                catch
                {
                    SetLottieStatus(message);
                }
            }

            static string? ValidateLottie(string? path)
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                        return "LOTTIE MISSING";

                    using var fs = File.OpenRead(path);
                    using var doc = JsonDocument.Parse(fs);
                    var root = doc.RootElement;
                    if (root.ValueKind != JsonValueKind.Object)
                        return "LOTTIE JSON INVALID";

                    // Basic Lottie JSON sanity check (most files include these keys)
                    if (!root.TryGetProperty("v", out _)) return "LOTTIE JSON?";
                    if (!root.TryGetProperty("fr", out _)) return "LOTTIE JSON?";
                    if (!root.TryGetProperty("ip", out _)) return "LOTTIE JSON?";
                    if (!root.TryGetProperty("op", out _)) return "LOTTIE JSON?";
                    return null;
                }
                catch
                {
                    return "LOTTIE JSON INVALID";
                }
            }

            static string ExtractJsonFileName(string? raw)
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(raw)) return "";
                    var s = raw.Trim().Replace('\\', '/');
                    var name = Path.GetFileName(s);
                    if (string.IsNullOrWhiteSpace(name)) return "";
                    return name.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? name : "";
                }
                catch
                {
                    return "";
                }
            }

            static string ResolveLabelToPath(string? raw)
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(raw)) return "";
                    var s = raw.Trim();
                    if (!s.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                        return "";

                    // In preferences we store a label like:
                    //   Assets/Animations/Lottie/atlas_globe.json
                    //   Animations/some.json
                    // Resolve it as a relative path under the app base directory.
                    var baseDir = AppDomain.CurrentDomain.BaseDirectory ?? "";
                    if (string.IsNullOrWhiteSpace(baseDir))
                        baseDir = Directory.GetCurrentDirectory();

                    var rel = s.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
                    if (Path.IsPathRooted(rel) && File.Exists(rel))
                        return rel;

                    var candidate = Path.Combine(baseDir, rel);
                    if (File.Exists(candidate))
                        return candidate;

                    return "";
                }
                catch
                {
                    return "";
                }
            }

            string mode = "globe";
            string preferredChatJson = "";
            string preferredChatPath = "";
            try
            {
                var prefs = PreferencesStore.Instance.Current;
                mode = (prefs.AnimatedLogoMode ?? "globe").Trim();
                _lastSeenHeaderLottie = (prefs.ChatHeaderLottie ?? "").Trim();
                preferredChatPath = ResolveLabelToPath(prefs.ChatHeaderLottie);
                preferredChatJson = string.IsNullOrWhiteSpace(preferredChatPath)
                    ? ExtractJsonFileName(prefs.ChatHeaderLottie)
                    : "";
            }
            catch
            {
                // Preferences may not be ready early in startup; fall back to default.
            }

            string fileName = !string.IsNullOrWhiteSpace(preferredChatJson)
                ? preferredChatJson
                : mode switch
            {
                "orb" => "AI Assistant.json",
                "particles" => "Loading loop animation.json",
                "globe" => "Spinning Globe.json",
                _ => "Spinning Globe.json",
            };

            try
            {
                if (!string.IsNullOrWhiteSpace(preferredChatPath) && File.Exists(preferredChatPath))
                {
                    if (string.Equals(_lastAppliedHeaderLottiePath, preferredChatPath, StringComparison.OrdinalIgnoreCase) &&
                        animatedLogoHost.Visibility == Visibility.Visible)
                    {
                        return;
                    }

                    try { animatedLogoHost.AutoPlay = true; } catch { }
                    try { animatedLogoHost.RepeatCount = -1; } catch { }
                    try { TryInvoke(animatedLogoHost, "Stop"); } catch { }
                    try { animatedLogoHost.FileName = string.Empty; } catch { }
                    animatedLogoHost.FileName = preferredChatPath;
                    animatedLogoHost.Visibility = Visibility.Visible;
                    try
                    {
                        Dispatcher?.BeginInvoke(new Action(() =>
                        {
                            try { TryInvoke(animatedLogoHost, "Play", "PlayAnimation", "Start"); } catch { }
                        }), System.Windows.Threading.DispatcherPriority.Background);
                    }
                    catch { }
                    SetLottieStatusWithFile(ValidateLottie(preferredChatPath), preferredChatPath);

                    try
                    {
                        if (!string.Equals(_lastAppliedHeaderLottiePath, preferredChatPath, StringComparison.OrdinalIgnoreCase))
                        {
                            _lastAppliedHeaderLottiePath = preferredChatPath;
                        }
                    }
                    catch { }
                    return;
                }

                var primary = App.GetLottiePath(fileName);
                var candidates = new[]
                {
                    primary,
                    App.GetLottiePath("Spinning Globe.json"),
                    App.GetLottiePath("Loading loop animation.json"),
                    App.GetLottiePath("AI Assistant.json"),
                };

                var path = candidates.FirstOrDefault(File.Exists);

                if (string.IsNullOrWhiteSpace(path))
                {
                    try
                    {
                        var baseDir = AppDomain.CurrentDomain.BaseDirectory ?? "";
                        var folder = Path.Combine(baseDir, "Assets", "Animations", "Lottie");
                        if (Directory.Exists(folder))
                        {
                            path = Directory.EnumerateFiles(folder, "*.json", SearchOption.TopDirectoryOnly)
                                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                                .FirstOrDefault();
                        }
                    }
                    catch
                    {
                    }
                }

                if (!string.IsNullOrWhiteSpace(path))
                {
                    if (string.Equals(_lastAppliedHeaderLottiePath, path, StringComparison.OrdinalIgnoreCase) &&
                        animatedLogoHost.Visibility == Visibility.Visible)
                    {
                        return;
                    }

                    try { animatedLogoHost.AutoPlay = true; } catch { }
                    try { animatedLogoHost.RepeatCount = -1; } catch { }
                    try { TryInvoke(animatedLogoHost, "Stop"); } catch { }
                    try { animatedLogoHost.FileName = string.Empty; } catch { }
                    animatedLogoHost.FileName = path;
                    animatedLogoHost.Visibility = Visibility.Visible;
                    try
                    {
                        Dispatcher?.BeginInvoke(new Action(() =>
                        {
                            try { TryInvoke(animatedLogoHost, "Play", "PlayAnimation", "Start"); } catch { }
                        }), System.Windows.Threading.DispatcherPriority.Background);
                    }
                    catch { }
                    SetLottieStatusWithFile(ValidateLottie(path), path);

                    try
                    {
                        if (!string.Equals(_lastAppliedHeaderLottiePath, path, StringComparison.OrdinalIgnoreCase))
                        {
                            _lastAppliedHeaderLottiePath = path;
                        }
                    }
                    catch { }
                }
                else
                {
                    animatedLogoHost.Visibility = Visibility.Collapsed;
                    SetLottieStatus("LOTTIE NOT FOUND");
                }
            }
            catch
            {
                animatedLogoHost.Visibility = Visibility.Collapsed;
                SetLottieStatus("LOTTIE LOAD FAILED");
            }
        }

        private void Minimize_Click(object sender, RoutedEventArgs e)
        {
            MinimizeRequested?.Invoke(this, EventArgs.Empty);
            
            // Fallback: minimize parent window
            var window = Window.GetWindow(this);
            if (window != null)
            {
                window.WindowState = WindowState.Minimized;
            }
        }

        private void Maximize_Click(object sender, RoutedEventArgs e)
        {
            MaximizeRequested?.Invoke(this, EventArgs.Empty);
            
            // Fallback: toggle maximize/restore
            var window = Window.GetWindow(this);
            if (window != null)
            {
                window.WindowState = window.WindowState == WindowState.Maximized 
                    ? WindowState.Normal 
                    : WindowState.Maximized;
                
                // Update button content
                MaximizeBtn.Content = window.WindowState == WindowState.Maximized ? "❐" : "□";
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            // Schedule the absolute last resort first so even if something throws below,
            // the process still exits.
            _ = System.Threading.Tasks.Task.Run(async () =>
            {
                try { await System.Threading.Tasks.Task.Delay(650).ConfigureAwait(false); } catch { }
                try { Environment.Exit(0); } catch { }
                try { System.Diagnostics.Process.GetCurrentProcess().Kill(); } catch { }
            });

            try { CloseRequested?.Invoke(this, EventArgs.Empty); } catch { }

            // Match the working build behavior: directly close the parent window.
            // Keep a hard fallback to ensure the process actually exits if a window/tray setup swallows close.
            try { AtlasAI.Core.AppLogger.LogInfo("[TopNav] Close clicked"); } catch { }

            var window = Window.GetWindow(this);
            try
            {
                if (window != null)
                {
                    try
                    {
                        window.Dispatcher.BeginInvoke(new Action(() =>
                        {
                            try { window.Close(); } catch { }
                        }), System.Windows.Threading.DispatcherPriority.Send);
                    }
                    catch
                    {
                        try { window.Close(); } catch { }
                    }
                }
            }
            catch
            {
            }

            try
            {
                Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                {
                    try { Application.Current?.Shutdown(); } catch { }
                }), System.Windows.Threading.DispatcherPriority.Background);
            }
            catch
            {
                try { Application.Current?.Shutdown(); } catch { }
            }
        }

        private void HeaderToggle_Click(object sender, RoutedEventArgs e)
        {
            try { AtlasAI.Core.AppLogger.LogInfo("[TopNav] Header toggle clicked"); } catch { }

            // If the host window explicitly subscribed, let it handle toggling.
            // (Avoid double-toggling, which cancels the state change.)
            var handler = HeaderToggleRequested;
            if (handler != null)
            {
                try { handler.Invoke(this, EventArgs.Empty); } catch { }
                return;
            }

            try
            {
                var window = Window.GetWindow(this);
                var m = window?.GetType().GetMethod("ToggleHeader");
                if (m != null && m.GetParameters().Length == 0)
                    m.Invoke(window, null);
            }
            catch
            {
            }
        }

        private void NowPlayingBtn_Click(object sender, RoutedEventArgs e)
        {
            AtlasAI.Views.ViewModels.MediaCentreViewModel.Instance?.RestoreVideoPlayerWindow();
        }

        private void Unload_Click(object sender, RoutedEventArgs e)
        {
            try { UnloadRequested?.Invoke(this, EventArgs.Empty); } catch { }

            try
            {
                var window = Window.GetWindow(this);
                if (window is AtlasAI.CommandCenterWindow ccw)
                {
                    ccw.UnloadCurrentTab();
                    return;
                }

                var m = window?.GetType().GetMethod("UnloadCurrentTab");
                if (m != null && m.GetParameters().Length == 0)
                    m.Invoke(window, null);
            }
            catch
            {
            }
        }

        private void TopNavBar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            try
            {
                static bool IsInsideButtonBase(System.Windows.DependencyObject? start)
                {
                    try
                    {
                        var dep = start;
                        var guard = 0;
                        while (dep != null && guard++ < 128)
                        {
                            if (dep is System.Windows.Controls.Primitives.ButtonBase)
                                return true;

                            // Visual parent first
                            var parent = System.Windows.Media.VisualTreeHelper.GetParent(dep);
                            if (parent != null)
                            {
                                dep = parent;
                                continue;
                            }

                            // Control template boundary: hop to templated parent
                            if (dep is FrameworkElement fe)
                            {
                                if (fe.TemplatedParent is System.Windows.DependencyObject tp)
                                {
                                    dep = tp;
                                    continue;
                                }

                                if (fe.Parent is System.Windows.DependencyObject lp)
                                {
                                    dep = lp;
                                    continue;
                                }
                            }

                            if (dep is FrameworkContentElement fce)
                            {
                                if (fce.TemplatedParent is System.Windows.DependencyObject tp2)
                                {
                                    dep = tp2;
                                    continue;
                                }

                                if (fce.Parent is System.Windows.DependencyObject lp2)
                                {
                                    dep = lp2;
                                    continue;
                                }
                            }

                            break;
                        }
                    }
                    catch
                    {
                    }

                    return false;
                }

                if (IsInsideButtonBase(e.OriginalSource as System.Windows.DependencyObject))
                    return;

                if (e.ClickCount == 2)
                {
                    var w = Window.GetWindow(this);
                    if (w != null)
                    {
                        w.WindowState = w.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
                        try { MaximizeBtn.Content = w.WindowState == WindowState.Maximized ? "❐" : "□"; } catch { }
                    }
                    return;
                }

                Window.GetWindow(this)?.DragMove();
            }
            catch
            {
            }
        }
    }
}
