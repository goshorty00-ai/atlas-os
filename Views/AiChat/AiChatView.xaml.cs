using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using AtlasAI.Core;
using AtlasAI.Views.AiChat.ViewModels;
using AtlasAI.Voice;
using Microsoft.Web.WebView2.Core;
using System.Linq;
using System.Threading.Tasks;
using System.Text.Json;

namespace AtlasAI.Views.AiChat;

public partial class AiChatView : UserControl
{
    private readonly AiChatViewModel _viewModel;
    private DateTime _lastDropDownInteractionUtc = DateTime.MinValue;
    private int _openDropDownCount;
    private bool _orbsWebViewInitialized;
    private DispatcherTimer? _orbsAudioTimer;
    private double _lastOrbsAudioLevel;
    private DateTime _lastOrbsAudioSentUtc = DateTime.MinValue;
    private string _lastOrbsState = "";
    private DateTime _lastOrbsStateSentUtc = DateTime.MinValue;
    private bool _orbOverlayVisible = true;

    public AiChatView(VoiceManager? voiceManager = null)
    {
        InitializeComponent();

        _viewModel = new AiChatViewModel(voiceManager);
        DataContext = _viewModel;

        try
        {
            if (voiceManager != null)
                VoiceActivityService.Instance.ConnectToVoiceManager(voiceManager);
        }
        catch { }

        _viewModel.Messages.CollectionChanged += Messages_CollectionChanged;
        _viewModel.PropertyChanged += ViewModel_PropertyChanged;

        Loaded += async (_, __) =>
        {
            SyncHeaderLottie();
            await InitializeOrbsOverlayAsync();
            StartOrbsAudioPump();
        };
        Unloaded += (_, __) =>
        {
            try
            {
                if (ChatOrbsWebView?.CoreWebView2 != null)
                    ChatOrbsWebView.CoreWebView2.WebMessageReceived -= OrbsWebView_WebMessageReceived;
            }
            catch { }

            try
            {
                if (_orbsAudioTimer != null)
                {
                    _orbsAudioTimer.Stop();
                    _orbsAudioTimer.Tick -= OrbsAudioTimer_Tick;
                    _orbsAudioTimer = null;
                }
            }
            catch { }
        };
        SyncHeaderLottie();
    }

    public void SetOrbOverlayVisible(bool visible)
    {
        try
        {
            _orbOverlayVisible = visible;

            if (OrbOverlay != null)
                OrbOverlay.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;

            if (ChatOrbsWebView != null)
                ChatOrbsWebView.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;

            if (visible)
            {
                _ = InitializeOrbsOverlayAsync();
                StartOrbsAudioPump();
            }
            else if (_orbsAudioTimer != null)
            {
                _orbsAudioTimer.Stop();
            }
        }
        catch
        {
        }
    }

    public bool GetOrbOverlayVisible()
    {
        try
        {
            if (OrbOverlay != null)
                return OrbOverlay.Visibility == Visibility.Visible;
        }
        catch
        {
        }

        return _orbOverlayVisible;
    }

    public Task LoadSessionAsync(string sessionId)
    {
        return _viewModel.LoadSessionAsync(sessionId);
    }

    public Task StartNewSessionAsync()
    {
        return _viewModel.StartNewSessionAsync();
    }

    public Task PrepareRemoteConversationAsync(bool startNewConversation)
    {
        return _viewModel.PrepareRemoteConversationAsync(startNewConversation);
    }

    public Task PresentRemoteConversationTurnAsync(string userMessage, string assistantReply, bool startNewConversation)
    {
        return _viewModel.PresentRemoteConversationTurnAsync(userMessage, assistantReply, startNewConversation);
    }

    private async Task InitializeOrbsOverlayAsync()
    {
        try
        {
            if (_orbsWebViewInitialized) return;
            _orbsWebViewInitialized = true;

            if (ChatOrbsWebView == null) return;

            try
            {
                 var userDataFolder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "AtlasOS_WebView2", "Orbs");
                 var env = await Microsoft.Web.WebView2.Core.CoreWebView2Environment.CreateAsync(null, userDataFolder);
                 await ChatOrbsWebView.EnsureCoreWebView2Async(env);
            }
            catch { }
            
            if (ChatOrbsWebView.CoreWebView2 == null) return;

            try
            {
                var settings = ChatOrbsWebView.CoreWebView2.Settings;
                settings.AreDefaultContextMenusEnabled = false;
                settings.AreDevToolsEnabled = false;
                settings.AreBrowserAcceleratorKeysEnabled = false;
                settings.IsStatusBarEnabled = false;
                settings.IsZoomControlEnabled = false;
            }
            catch { }

            try { ChatOrbsWebView.DefaultBackgroundColor = System.Drawing.Color.Transparent; } catch { }

            var dist = FindFigmaDist();
            if (dist == null) return;

            try
            {
                ChatOrbsWebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    "atlas-ui",
                    dist,
                    CoreWebView2HostResourceAccessKind.Allow);
            }
            catch { }

            ChatOrbsWebView.CoreWebView2.WebMessageReceived += OrbsWebView_WebMessageReceived;

            long indexWriteTicks = 0;
            try
            {
                var indexPath = Path.Combine(dist, "index.html");
                if (File.Exists(indexPath))
                    indexWriteTicks = File.GetLastWriteTimeUtc(indexPath).Ticks;
            }
            catch { }

            var v = (indexWriteTicks != 0 ? indexWriteTicks : DateTime.UtcNow.Ticks).ToString();
            var url = $"https://atlas-ui/index.html?mode=orbs&v={v}";
            
            ChatOrbsWebView.CoreWebView2.Navigate(url);
        }
        catch
        {
        }
    }

    private void OrbsWebView_WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        // Reserved for future state sync; intentionally no-op.
    }

    private void StartOrbsAudioPump()
    {
        try
        {
            if (_orbsAudioTimer != null)
            {
                if (!_orbsAudioTimer.IsEnabled)
                    _orbsAudioTimer.Start();
                return;
            }

            _orbsAudioTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(33)
            };
            _orbsAudioTimer.Tick += OrbsAudioTimer_Tick;
            _orbsAudioTimer.Start();
        }
        catch
        {
        }
    }

    private void OrbsAudioTimer_Tick(object? sender, EventArgs e)
    {
        try
        {
            if (!_orbOverlayVisible)
                return;

            if (ChatOrbsWebView?.CoreWebView2 == null) return;

            var voice = AtlasAI.Voice.VoiceActivityService.Instance;
            try { voice.Update(1.0 / 30.0); } catch { }
            var level = Math.Clamp(voice.CurrentAmplitude01, 0.0, 1.0);

            // Presence/state sync (idle/thinking/working/speaking)
            try
            {
                var mode = _viewModel.CurrentMode;
                var state = "idle";
                if (voice.IsSpeaking || mode == PresenceMode.Talking)
                    state = "speaking";
                else if (mode == PresenceMode.Working)
                    state = "working";
                else if (mode == PresenceMode.Thinking || mode == PresenceMode.Typing)
                    state = "thinking";

                var now2 = DateTime.UtcNow;
                if (!string.Equals(state, _lastOrbsState, StringComparison.Ordinal) || (now2 - _lastOrbsStateSentUtc).TotalMilliseconds >= 750)
                {
                    _lastOrbsState = state;
                    _lastOrbsStateSentUtc = now2;
                    var statePayload = JsonSerializer.Serialize(new { type = "orbs.state", state, mode = mode.ToString() });
                    ChatOrbsWebView.CoreWebView2.PostWebMessageAsJson(statePayload);
                }
            }
            catch { }

            // Throttle updates a bit to reduce message spam.
            var now = DateTime.UtcNow;
            if (Math.Abs(level - _lastOrbsAudioLevel) < 0.01 && (now - _lastOrbsAudioSentUtc).TotalMilliseconds < 150)
                return;

            _lastOrbsAudioLevel = level;
            _lastOrbsAudioSentUtc = now;

            var payload = $"{{\"type\":\"orbs.audio\",\"level\":{level.ToString(System.Globalization.CultureInfo.InvariantCulture)}}}";
            ChatOrbsWebView.CoreWebView2.PostWebMessageAsJson(payload);
        }
        catch
        {
        }
    }

    private static string? FindFigmaDist()
    {
        try
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var shipped = Path.Combine(baseDir, "Figma", "Futuristic AI Command Center (6)", "dist");
            if (Directory.Exists(shipped) && File.Exists(Path.Combine(shipped, "index.html")))
                return shipped;
        }
        catch { }

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
                    var figmaRoot = Path.Combine(dir.FullName, "Figma");
                    if (Directory.Exists(figmaRoot))
                    {
                        var uiFolder = Directory.GetDirectories(figmaRoot, "Futuristic AI Command Center (*)", SearchOption.TopDirectoryOnly)
                            .OrderByDescending(d => d)
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
            catch { }
        }

        return null;
    }

    private void Messages_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => ScrollToBottomSoon();

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AiChatViewModel.IsTyping))
            ScrollToBottomSoon();

        if (e.PropertyName == nameof(AiChatViewModel.HeaderLottieFilePath) ||
            e.PropertyName == nameof(AiChatViewModel.SelectedHeaderLottie))
        {
            SyncHeaderLottie();
        }
    }

    private void SyncHeaderLottie()
    {
        try
        {
            if (HeaderLottie == null) return;

            var path = (_viewModel.HeaderLottieFilePath ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(path)) return;
            if (!System.IO.File.Exists(path)) return;

            // LottieSharp's `FileName` is not a DependencyProperty; set it directly.
            HeaderLottie.FileName = path;
        }
        catch
        {
        }
    }

    private void ScrollToBottomSoon()
    {
        try
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                try { MessageScrollViewer?.ScrollToEnd(); } catch { }
            }));
        }
        catch
        {
        }
    }

    private void InputBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;

        // Shift+Enter should insert newline
        if (Keyboard.Modifiers != ModifierKeys.None)
            return;

        e.Handled = true;
        _viewModel.TrySend();
    }

    private void MessagesList_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        try
        {
            if (MessageScrollViewer == null) return;

            // ListBox eats the wheel even when its own scrolling is disabled.
            // Forward wheel deltas to the parent scroll viewer.
            var target = MessageScrollViewer.VerticalOffset - (e.Delta / 3.0);
            MessageScrollViewer.ScrollToVerticalOffset(Math.Max(0, target));
            e.Handled = true;
        }
        catch
        {
        }
    }

    private void InputBox_PreviewDragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
        }
    }

    private void InputBox_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
            {
                foreach (var file in files)
                {
                    _viewModel.Attachments.Add(file);
                }
            }
        }
    }

    private void InputBox_Paste(object sender, DataObjectPastingEventArgs e)
    {
        if (e.SourceDataObject.GetDataPresent(DataFormats.FileDrop))
        {
            if (e.SourceDataObject.GetData(DataFormats.FileDrop) is string[] files)
            {
                foreach (var file in files)
                {
                    _viewModel.Attachments.Add(file);
                }
                e.CancelCommand(); // Prevent pasting the file path as text
                e.Handled = true;
            }
        }
    }

    private void SettingsPopup_MouseDown(object sender, MouseButtonEventArgs e)
    {
        // Close settings when clicking outside the border
        // (The Border inside the Grid handles its own clicks, so this only fires if clicking the transparent Grid area)
        if (SettingsToggle.IsChecked == true)
        {
            SettingsToggle.IsChecked = false;
        }
    }

    private void SettingsContent_MouseDown(object sender, MouseButtonEventArgs e)
    {
        // Swallow clicks inside the settings box so they don't close the popup
        e.Handled = true;
    }

    private void PersonalityPopup_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (PersonalityToggle.IsChecked == true)
        {
            PersonalityToggle.IsChecked = false;
        }
    }

    private void SettingsOverlay_MouseDown(object sender, MouseButtonEventArgs e)
    {
        try
        {
            if (_openDropDownCount > 0)
                return;
            if ((DateTime.UtcNow - _lastDropDownInteractionUtc).TotalMilliseconds < 250)
                return;
            if (SettingsToggle.IsChecked == true)
                SettingsToggle.IsChecked = false;
        }
        catch
        {
        }
    }

    private void PersonalityOverlay_MouseDown(object sender, MouseButtonEventArgs e)
    {
        try
        {
            if (_openDropDownCount > 0)
                return;
            if ((DateTime.UtcNow - _lastDropDownInteractionUtc).TotalMilliseconds < 250)
                return;
            if (PersonalityToggle.IsChecked == true)
                PersonalityToggle.IsChecked = false;
        }
        catch
        {
        }
    }

    private void CloseSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        try { SettingsToggle.IsChecked = false; } catch { }
    }

    private void ClosePersonalityButton_Click(object sender, RoutedEventArgs e)
    {
        try { PersonalityToggle.IsChecked = false; } catch { }
    }

    private void SettingsCombo_DropDownOpened(object sender, EventArgs e)
    {
        try
        {
            _lastDropDownInteractionUtc = DateTime.UtcNow;
            _openDropDownCount++;
        }
        catch { }
    }

    private void SettingsCombo_DropDownClosed(object sender, EventArgs e)
    {
        try
        {
            _lastDropDownInteractionUtc = DateTime.UtcNow;
            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(280) };
            t.Tick += (_, __) =>
            {
                try
                {
                    t.Stop();
                    if (_openDropDownCount > 0) _openDropDownCount--;
                }
                catch { }
            };
            t.Start();
        }
        catch { }
    }

    private void HeaderOpenArrow_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var window = Window.GetWindow(this);
            if (window is AtlasAI.CommandCenterWindow ccw)
            {
                ccw.ToggleHeader();
                return;
            }

            var m = window?.GetType().GetMethod("ToggleHeader");
            if (m != null && m.GetParameters().Length == 0)
                m.Invoke(window, null);
        }
        catch
        {
        }
    }
}
