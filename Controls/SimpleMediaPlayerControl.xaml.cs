using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AtlasAI.MediaScanner;
using FormsScreen = System.Windows.Forms.Screen;
using Microsoft.Win32;
using NAudio.CoreAudioApi;

namespace AtlasAI.Controls
{
    public partial class SimpleMediaPlayerControl : UserControl, IPlaybackOutput
    {
        public static readonly DependencyProperty IsCompactProperty =
            DependencyProperty.Register(nameof(IsCompact), typeof(bool), typeof(SimpleMediaPlayerControl), new PropertyMetadata(false));

        public static readonly DependencyProperty IsMiniPlayerProperty =
            DependencyProperty.Register(nameof(IsMiniPlayer), typeof(bool), typeof(SimpleMediaPlayerControl), new PropertyMetadata(false));

        public bool IsCompact
        {
            get => (bool)GetValue(IsCompactProperty);
            set => SetValue(IsCompactProperty, value);
        }

        public bool IsMiniPlayer
        {
            get => (bool)GetValue(IsMiniPlayerProperty);
            set => SetValue(IsMiniPlayerProperty, value);
        }

        public event EventHandler PlayerClosed;
        public event EventHandler PlaybackEnded;
        public event EventHandler<double> PlaybackPositionChanged;
        public event EventHandler<double> PlaybackDurationChanged;

        private DispatcherTimer _timer;
        private DispatcherTimer _hideTimer;
        private bool _isDragging = false;
        private bool _isPlaying = false;
        private bool _isFullscreen = false;
        private Uri? _lastSource;
        private double _resumeSeconds;
        private bool _resumeWasPlaying;
        private bool _resumePending;
        private DispatcherTimer? _resumeRetryTimer;
        private bool _subtitlesEnabled;
        private int _subtitleCueIndex = -1;
        private readonly List<SubtitleCue> _subtitleCues = new();
        private WindowState _restoreWindowState = WindowState.Normal;
        private WindowStyle _restoreWindowStyle = WindowStyle.SingleBorderWindow;
        private bool _restoreTopmost = false;
        private ResizeMode _restoreResizeMode = ResizeMode.CanResize;
        private double _restoreWindowLeft;
        private double _restoreWindowTop;
        private double _restoreWindowWidth;
        private double _restoreWindowHeight;
        private HorizontalAlignment _restoreHorizontalAlignment;
        private VerticalAlignment _restoreVerticalAlignment;
        private Thickness _restoreMargin;
        private double _restoreWidth;
        private double _restoreHeight;
        private double _restoreTopNavHeight = double.NaN;
        private double _restoreLeftSidebarWidth = double.NaN;
        private GridLength _restoreTopRowHeight = GridLength.Auto;
        private GridLength _restoreSidebarColumnWidth = GridLength.Auto;
        private GridLength _restoreFileBrowserColumnWidth = GridLength.Auto;
        private Thickness _restoreContentAreaMargin;
        private bool _hasRestoreChrome;
        private MediaPlaybackService _playbackService;
        private WindowStartupLocation _restoreStartupLocation = WindowStartupLocation.Manual;
        private Visibility _restoreMediaCenterTopNavVisibility = Visibility.Visible;
        private Visibility _restoreMediaCenterLibraryVisibility = Visibility.Visible;
        private bool _hasRestoreMediaCenterChrome;
        private Window? _fullscreenWindow;
        private Panel? _restoreParentPanel;
        private int _restoreParentIndex;
        private int _restoreGridRow;
        private int _restoreGridColumn;
        private int _restoreGridRowSpan;
        private int _restoreGridColumnSpan;
        private bool _isRestoringFromFullscreen;
        private Window? _hostWindow;
        private bool _resumeOnRestorePending;
        private bool _resumeOnRestoreWasPlaying;
        private double _resumeOnRestoreSeconds;

        public bool IsFullscreenActive => _isFullscreen;

        public MediaPlaybackService PlaybackService
        {
            get => _playbackService;
            set
            {
                if (_playbackService != null)
                {
                    _playbackService.CurrentMediaChanged -= OnMediaChanged;
                }
                _playbackService = value;
                if (_playbackService != null)
                {
                    _playbackService.CurrentMediaChanged += OnMediaChanged;
                }
            }
        }

        public SimpleMediaPlayerControl()
        {
            InitializeComponent();

            Loaded += SimpleMediaPlayerControl_Loaded;
            Unloaded += SimpleMediaPlayerControl_Unloaded;

            SubtitleIcon.Opacity = 0.6;
            SubtitleTopButton.Opacity = 0.6;
            
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _timer.Tick += Timer_Tick;

            _hideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            _hideTimer.Tick += (s, e) => 
            {
                if (_isFullscreen)
                {
                    ControlsGrid.Visibility = Visibility.Collapsed;
                    SetMediaCenterChromeVisible(false);
                }
                else if (_isPlaying)
                {
                    ControlsGrid.Visibility = Visibility.Collapsed;
                }
                _hideTimer.Stop();
            };

            IsVisibleChanged += SimpleMediaPlayerControl_IsVisibleChanged;
        }

        private void SimpleMediaPlayerControl_Loaded(object sender, RoutedEventArgs e)
        {
            if (Player != null) Player.Volume = VolumeSlider.Value;
            Focus();
            try { LoadCustomButtonIcons(); } catch { }
            AttachToHostWindow();
        }

        private void SimpleMediaPlayerControl_Unloaded(object sender, RoutedEventArgs e)
        {
            DetachFromHostWindow();
        }

        private void AttachToHostWindow()
        {
            var w = Window.GetWindow(this);
            if (w == null) return;
            if (ReferenceEquals(_hostWindow, w)) return;

            DetachFromHostWindow();
            _hostWindow = w;

            try
            {
                w.StateChanged += HostWindow_StateChanged;
                w.Activated += HostWindow_Activated;
                w.Deactivated += HostWindow_Deactivated;
            }
            catch
            {
            }
        }

        private void DetachFromHostWindow()
        {
            var w = _hostWindow;
            _hostWindow = null;
            if (w == null) return;
            try
            {
                w.StateChanged -= HostWindow_StateChanged;
                w.Activated -= HostWindow_Activated;
                w.Deactivated -= HostWindow_Deactivated;
            }
            catch
            {
            }
        }

        private void HostWindow_StateChanged(object? sender, EventArgs e)
        {
            try
            {
                if (_fullscreenWindow != null) return;
                if (sender is not Window w) return;

                if (w.WindowState == WindowState.Minimized)
                {
                    CaptureResumePointForRestore();
                }
                else
                {
                    if (_resumeOnRestorePending)
                        RequestResumeAfterRestore();
                }
            }
            catch
            {
            }
        }

        private void HostWindow_Activated(object? sender, EventArgs e)
        {
            try
            {
                if (_fullscreenWindow != null) return;
                if (_resumeOnRestorePending)
                    RequestResumeAfterRestore();
            }
            catch
            {
            }
        }

        private void HostWindow_Deactivated(object? sender, EventArgs e)
        {
            try
            {
                if (_fullscreenWindow != null) return;
                CaptureResumePointForRestore();
            }
            catch
            {
            }
        }

        private void CaptureResumePointForRestore()
        {
            try
            {
                if (Player?.Source == null) return;
                if (!Player.NaturalDuration.HasTimeSpan) return;

                _resumeOnRestoreSeconds = Math.Max(0, Player.Position.TotalSeconds);
                _resumeOnRestoreWasPlaying = _isPlaying;
                _resumeOnRestorePending = true;
            }
            catch
            {
            }
        }

        private void RequestResumeAfterRestore()
        {
            try
            {
                if (!_resumeOnRestorePending) return;
                if (Player?.Source == null) return;

                var currentPos = 0.0;
                try { currentPos = Math.Max(0, Player.Position.TotalSeconds); } catch { currentPos = 0; }

                if (_resumeOnRestoreSeconds <= 1.0 || currentPos > 1.0)
                {
                    _resumeOnRestorePending = false;
                    return;
                }

                _lastSource = Player.Source;
                _resumeSeconds = Math.Max(0, _resumeOnRestoreSeconds);
                _resumeWasPlaying = _resumeOnRestoreWasPlaying;
                _resumePending = true;
                _resumeOnRestorePending = false;

                StartResumeRetryTimer();
            }
            catch
            {
            }
        }

        private sealed class SubtitleCue
        {
            public TimeSpan Start { get; init; }
            public TimeSpan End { get; init; }
            public string Text { get; init; } = "";
        }

        private void SimpleMediaPlayerControl_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            try
            {
                if (!IsVisible) return;
            }
            catch
            {
            }
        }

        private void OnMediaChanged(object? sender, MediaItem e)
        {
            try
            {
                if (e == null || string.IsNullOrWhiteSpace(e.FilePath)) return;
                LoadMediaInternal(e.FilePath, autoPlay: !IsCompact);
            }
            catch
            {
            }
        }

        public void LoadMedia(string path)
        {
            LoadMediaInternal(path, autoPlay: true);
        }

        private void LoadMediaInternal(string path, bool autoPlay)
        {
            try
            {
                MessageOverlay.Visibility = Visibility.Visible;
                MessageText.Text = "Loading...";
                _lastSource = new Uri(path);
                _resumePending = false;
                Player.Source = _lastSource;

                if (autoPlay)
                {
                    try { PlaybackOutputCoordinator.SetActive(this); } catch { }
                    Player.Play();
                    _isPlaying = true;
                    _timer.Start();
                }
                else
                {
                    try { Player.Stop(); } catch { }
                    _timer.Stop();
                    _isPlaying = false;
                }

                UpdatePlayIcon();
                ShowControls();
            }
            catch (Exception ex)
            {
                MessageText.Text = "Error: " + ex.Message;
            }
        }

        private static void ResetWindowsMixerVolume()
        {
            try
            {
                var pid = System.Diagnostics.Process.GetCurrentProcess().Id;
                using var enumerator = new MMDeviceEnumerator();
                var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                var sessions = device.AudioSessionManager.Sessions;
                for (int i = 0; i < sessions.Count; i++)
                {
                    var session = sessions[i];
                    if (session.GetProcessID == pid)
                    {
                        var currentVol = session.SimpleAudioVolume.Volume;
                        if (currentVol < 0.5f || session.SimpleAudioVolume.Mute)
                        {
                            session.SimpleAudioVolume.Volume = 1.0f;
                            session.SimpleAudioVolume.Mute = false;
                        }
                    }
                }
            } catch { }
        }

        private void Player_MediaOpened(object sender, RoutedEventArgs e)
        {
            ResetWindowsMixerVolume();
            if (Player != null) { Player.IsMuted = false; Player.Volume = VolumeSlider.Value; }
            MessageOverlay.Visibility = Visibility.Collapsed;
            if (Player.NaturalDuration.HasTimeSpan)
            {
                double totalSeconds = Player.NaturalDuration.TimeSpan.TotalSeconds;
                ProgressSlider.Maximum = totalSeconds;
                TotalTimeText.Text = Player.NaturalDuration.TimeSpan.ToString(@"mm\:ss");
                PlaybackDurationChanged?.Invoke(this, totalSeconds);
            }

            if (_resumePending && _lastSource != null && Player.Source != null && Player.Source == _lastSource)
            {
                try
                {
                    Player.Position = TimeSpan.FromSeconds(Math.Max(0, _resumeSeconds));
                    if (_resumeWasPlaying)
                    {
                        try { PlaybackOutputCoordinator.SetActive(this); } catch { }
                        Player.Play();
                        _isPlaying = true;
                        _timer.Start();
                    }
                    else
                    {
                        Player.Pause();
                        _isPlaying = false;
                    }

                    UpdatePlayIcon();
                    _resumePending = false;
                }
                catch
                {
                }
            }
        }

        private void Player_MediaEnded(object sender, RoutedEventArgs e)
        {
            _isPlaying = false;
            UpdatePlayIcon();
            ShowControls();
            PlaybackEnded?.Invoke(this, EventArgs.Empty);
        }

        private void Player_MediaFailed(object sender, ExceptionRoutedEventArgs e)
        {
            MessageOverlay.Visibility = Visibility.Visible;
            MessageText.Text = "Playback Failed: " + e.ErrorException.Message;
        }

        private void Timer_Tick(object? sender, EventArgs e)
        {
            if (!_isDragging && Player.NaturalDuration.HasTimeSpan)
            {
                double currentSeconds = Player.Position.TotalSeconds;
                ProgressSlider.Value = currentSeconds;
                CurrentTimeText.Text = Player.Position.ToString(@"mm\:ss");
                PlaybackPositionChanged?.Invoke(this, currentSeconds);
            }

            UpdateSubtitles();
        }

        private void PlayPauseButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isPlaying)
            {
                Player.Pause();
                _isPlaying = false;
            }
            else
            {
                try { PlaybackOutputCoordinator.SetActive(this); } catch { }
                Player.Play();
                _isPlaying = true;
            }
            UpdatePlayIcon();
        }

        private void UpdatePlayIcon()
        {
            var showPlay = !_isPlaying;
            var showPause = _isPlaying;

            var playImgLoaded = PlayIconImage?.Source != null;
            var pauseImgLoaded = PauseIconImage?.Source != null;

            if (PlayIconImage != null) PlayIconImage.Visibility = playImgLoaded && showPlay ? Visibility.Visible : Visibility.Collapsed;
            if (PauseIconImage != null) PauseIconImage.Visibility = pauseImgLoaded && showPause ? Visibility.Visible : Visibility.Collapsed;

            if (PlayIcon != null) PlayIcon.Visibility = playImgLoaded ? Visibility.Collapsed : (showPlay ? Visibility.Visible : Visibility.Collapsed);
            if (PauseIcon != null) PauseIcon.Visibility = pauseImgLoaded ? Visibility.Collapsed : (showPause ? Visibility.Visible : Visibility.Collapsed);
        }

        private void MuteButton_Click(object sender, RoutedEventArgs e)
        {
            Player.IsMuted = !Player.IsMuted;
            try { UpdateVolumeIcons(); } catch { }
        }

        private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (Player == null) return;

            Player.Volume = e.NewValue;
            if (Player.IsMuted && e.NewValue > 0)
            {
                Player.IsMuted = false;
                try { UpdateVolumeIcons(); } catch { }
            }
            else
            {
                try { UpdateVolumeIcons(); } catch { }
            }
        }

        private void LoadCustomButtonIcons()
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var root2 = System.IO.Path.GetFullPath(System.IO.Path.Combine(baseDir, "..", ".."));
            var root3 = System.IO.Path.GetFullPath(System.IO.Path.Combine(baseDir, "..", "..", ".."));
            var roots = new[] { baseDir, root2, root3 };

            string FindIcon(string file)
            {
                foreach (var r in roots)
                {
                    var full = System.IO.Path.Combine(r, "Assets", "MediaPlayer", "Buttons", file);
                    if (File.Exists(full)) return full;
                }
                return "";
            }

            BitmapImage Load(string fullPath)
            {
                if (string.IsNullOrWhiteSpace(fullPath) || !File.Exists(fullPath)) return null;
                try
                {
                    var img = new BitmapImage();
                    img.BeginInit();
                    img.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                    img.CacheOption = BitmapCacheOption.OnLoad;
                    img.UriSource = new Uri(fullPath, UriKind.Absolute);
                    img.EndInit();
                    img.Freeze();
                    return img;
                }
                catch
                {
                    return null;
                }
            }

            var play = Load(FindIcon("play.png"));
            if (play != null && PlayIconImage != null) PlayIconImage.Source = play;

            var pause = Load(FindIcon("pause.png"));
            if (pause != null && PauseIconImage != null) PauseIconImage.Source = pause;

            var prev = Load(FindIcon("prev.png"));
            if (prev != null && PrevIconImage != null) PrevIconImage.Source = prev;
            if (PrevIconImage != null && PrevIconPath != null) PrevIconPath.Visibility = PrevIconImage.Source != null ? Visibility.Collapsed : Visibility.Visible;
            if (PrevIconImage != null) PrevIconImage.Visibility = PrevIconImage.Source != null ? Visibility.Visible : Visibility.Collapsed;

            var next = Load(FindIcon("next.png"));
            if (next != null && NextIconImage != null) NextIconImage.Source = next;
            if (NextIconImage != null && NextIconPath != null) NextIconPath.Visibility = NextIconImage.Source != null ? Visibility.Collapsed : Visibility.Visible;
            if (NextIconImage != null) NextIconImage.Visibility = NextIconImage.Source != null ? Visibility.Visible : Visibility.Collapsed;

            var vol = Load(FindIcon("volume.png"));
            if (vol != null && VolumeIconImage != null) VolumeIconImage.Source = vol;

            var mute = Load(FindIcon("mute.png"));
            if (mute != null && MuteIconImage != null) MuteIconImage.Source = mute;

            var close = Load(FindIcon("close.png"));
            if (close != null && CloseIconImage != null) CloseIconImage.Source = close;
            if (CloseIconImage != null && CloseIconPath != null)
                CloseIconPath.Visibility = CloseIconImage.Source != null ? Visibility.Collapsed : Visibility.Visible;
            if (CloseIconImage != null)
                CloseIconImage.Visibility = CloseIconImage.Source != null ? Visibility.Visible : Visibility.Collapsed;

            UpdatePlayIcon();
            UpdateVolumeIcons();
        }

        private void UpdateVolumeIcons()
        {
            var muted = false;
            try { muted = Player?.IsMuted ?? false; } catch { muted = false; }

            var volImgLoaded = VolumeIconImage?.Source != null;
            var muteImgLoaded = MuteIconImage?.Source != null;

            if (VolumeIconImage != null) VolumeIconImage.Visibility = volImgLoaded && !muted ? Visibility.Visible : Visibility.Collapsed;
            if (MuteIconImage != null) MuteIconImage.Visibility = muteImgLoaded && muted ? Visibility.Visible : Visibility.Collapsed;

            if (VolumeIcon != null)
            {
                var showFallback = !(volImgLoaded && muteImgLoaded);
                VolumeIcon.Visibility = showFallback ? Visibility.Visible : Visibility.Collapsed;
                VolumeIcon.Opacity = muted ? 0.5 : 1.0;
            }
        }

        private void ProgressSlider_DragStarted(object sender, System.Windows.Controls.Primitives.DragStartedEventArgs e)
        {
            _isDragging = true;
        }

        private void ProgressSlider_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
        {
            _isDragging = false;
            Player.Position = TimeSpan.FromSeconds(ProgressSlider.Value);
        }

        private void ProgressSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isDragging)
            {
                CurrentTimeText.Text = TimeSpan.FromSeconds(e.NewValue).ToString(@"mm\:ss");
            }
        }

        private void PrevButton_Click(object sender, RoutedEventArgs e) => PlaybackService?.PlayPrevious();
        private void NextButton_Click(object sender, RoutedEventArgs e) => PlaybackService?.PlayNext();

        private void FullscreenButton_Click(object sender, RoutedEventArgs e)
        {
            if (_fullscreenWindow != null)
            {
                ExitFullscreen();
                return;
            }

            EnterFullscreen();
        }

        private void EnterFullscreen()
        {
            if (_fullscreenWindow != null) return;
            if (_isFullscreen) return;

            var currentWindow = Window.GetWindow(this);
            if (currentWindow == null) return;

            _restoreHorizontalAlignment = HorizontalAlignment;
            _restoreVerticalAlignment = VerticalAlignment;
            _restoreMargin = Margin;
            _restoreWidth = Width;
            _restoreHeight = Height;

            _isFullscreen = true;

            Player.Stretch = Stretch.UniformToFill;
            SetMediaCenterChromeVisible(false);
            ShowControls();

            _restoreParentPanel = VisualTreeHelper.GetParent(this) as Panel;
            if (_restoreParentPanel == null)
            {
                SetMediaCenterChromeVisible(true);
                _isFullscreen = false;
                return;
            }

            _restoreParentIndex = _restoreParentPanel.Children.IndexOf(this);
            _restoreGridRow = Grid.GetRow(this);
            _restoreGridColumn = Grid.GetColumn(this);
            _restoreGridRowSpan = Grid.GetRowSpan(this);
            _restoreGridColumnSpan = Grid.GetColumnSpan(this);

            _restoreParentPanel.Children.Remove(this);

            HorizontalAlignment = HorizontalAlignment.Stretch;
            VerticalAlignment = VerticalAlignment.Stretch;
            Margin = new Thickness(0);
            Width = double.NaN;
            Height = double.NaN;

            var targetScreen = FormsScreen.AllScreens
                .OrderByDescending(s => (long)s.Bounds.Width * s.Bounds.Height)
                .FirstOrDefault() ?? FormsScreen.PrimaryScreen;

            var fsWindow = new Window
            {
                Background = Brushes.Black,
                WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.NoResize,
                Topmost = true,
                ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Content = this
            };

            if (targetScreen != null)
            {
                var screenBounds = GetScreenBoundsInDips(fsWindow, targetScreen.Bounds);
                fsWindow.Left = screenBounds.Left;
                fsWindow.Top = screenBounds.Top;
                fsWindow.Width = screenBounds.Width;
                fsWindow.Height = screenBounds.Height;
            }

            var allowExitOnStateChange = false;
            fsWindow.Loaded += (_, __) => allowExitOnStateChange = true;

            fsWindow.StateChanged += (_, __) =>
            {
                if (!allowExitOnStateChange) return;
                if (_fullscreenWindow != fsWindow) return;
                if (fsWindow.WindowState == WindowState.Maximized) return;

                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (_fullscreenWindow == fsWindow)
                    {
                        RestoreFromFullscreenWindow();
                    }
                }));
            };

            fsWindow.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Escape || e.Key == Key.F11)
                {
                    ExitFullscreen();
                    e.Handled = true;
                }
            };

            fsWindow.Closed += (_, __) =>
            {
                if (_isRestoringFromFullscreen) return;
                RestoreFromFullscreenWindow();
            };

            _fullscreenWindow = fsWindow;
            fsWindow.Show();
            fsWindow.WindowState = WindowState.Maximized;
            Focus();
        }

        private void ExitFullscreen()
        {
            if (_fullscreenWindow == null) return;
            RestoreFromFullscreenWindow();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Player.Stop();
            Player.Source = null;
            _timer.Stop();
            _isPlaying = false;
            
            var window = Window.GetWindow(this);
            if (_fullscreenWindow != null) ExitFullscreen();
            if (window != null) window.Topmost = false;

            PlayerClosed?.Invoke(this, EventArgs.Empty);
        }

        private void Grid_MouseMove(object sender, MouseEventArgs e)
        {
            ShowControls();
        }

        private void Grid_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            Focus();
            if (e.ClickCount == 2)
            {
                if (IsCompact) return;
                FullscreenButton_Click(sender, e);
                e.Handled = true;
            }
        }

        private void SimpleMediaPlayerControl_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.F11)
            {
                if (IsCompact) return;
                FullscreenButton_Click(sender, e);
                e.Handled = true;
                return;
            }
            
            if (e.Key == Key.Escape)
            {
                if (_fullscreenWindow != null) ExitFullscreen();
                else CloseButton_Click(sender, e);
                e.Handled = true;
                return;
            }
            
            if (e.Key == Key.S)
            {
                if (IsCompact) return;
                SubtitleButton_Click(sender, e);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Space)
            {
                PlayPauseButton_Click(sender, e);
                e.Handled = true;
            }
        }

        private void ShowControls()
        {
            ControlsGrid.Visibility = Visibility.Visible;
            CloseButton.Visibility = Visibility.Visible;
            Mouse.OverrideCursor = null;
            _hideTimer.Stop();

            if (_isPlaying || _isFullscreen) _hideTimer.Start();
        }

        private void SetContentMargin(Window window, Thickness margin)
        {
            var contentArea = window.FindName("ContentArea") as FrameworkElement;
            if (contentArea != null)
            {
                contentArea.Margin = margin;
            }
        }

        private void SetChromeVisible(Window window, bool visible)
        {
            var topNav = window.FindName("TopNav") as FrameworkElement ?? window.FindName("TopNavBar") as FrameworkElement;
            var leftSidebar = window.FindName("LeftSidebar") as FrameworkElement;
            var sidebarPanel = window.FindName("SidebarPanel") as FrameworkElement;
            var fileBrowserPanel = window.FindName("FileBrowserPanel") as FrameworkElement;
            var contentArea = window.FindName("ContentArea") as FrameworkElement;
            var sidebarColumn = window.FindName("SidebarColumn") as ColumnDefinition;
            var fileBrowserColumn = window.FindName("FileBrowserColumn") as ColumnDefinition;

            Grid? topNavHostGrid = null;
            int topNavRow = -1;
            if (topNav != null)
            {
                topNavHostGrid = VisualTreeHelper.GetParent(topNav) as Grid;
                topNavRow = Grid.GetRow(topNav);
            }

            if (!_hasRestoreChrome)
            {
                if (topNav != null) _restoreTopNavHeight = topNav.Height;
                if (leftSidebar != null) _restoreLeftSidebarWidth = leftSidebar.Width;
                if (contentArea != null) _restoreContentAreaMargin = contentArea.Margin;
                if (topNavHostGrid != null && topNavRow >= 0 && topNavRow < topNavHostGrid.RowDefinitions.Count)
                    _restoreTopRowHeight = topNavHostGrid.RowDefinitions[topNavRow].Height;
                if (sidebarColumn != null) _restoreSidebarColumnWidth = sidebarColumn.Width;
                if (fileBrowserColumn != null) _restoreFileBrowserColumnWidth = fileBrowserColumn.Width;
                _hasRestoreChrome = true;
            }

            if (topNav != null)
            {
                topNav.IsHitTestVisible = visible;
                topNav.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
                topNav.Opacity = visible ? 1 : 0;
                topNav.Height = visible ? _restoreTopNavHeight : 0;
                if (topNavHostGrid != null && topNavRow >= 0 && topNavRow < topNavHostGrid.RowDefinitions.Count)
                    topNavHostGrid.RowDefinitions[topNavRow].Height = visible ? _restoreTopRowHeight : new GridLength(0);
            }

            if (leftSidebar != null)
            {
                leftSidebar.IsHitTestVisible = visible;
                leftSidebar.Opacity = visible ? 1 : 0;
                leftSidebar.Width = visible ? _restoreLeftSidebarWidth : 0;
            }

            if (sidebarPanel != null)
            {
                sidebarPanel.IsHitTestVisible = visible;
                sidebarPanel.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
                sidebarPanel.Opacity = visible ? 1 : 0;
            }

            if (fileBrowserPanel != null)
            {
                fileBrowserPanel.IsHitTestVisible = visible;
                fileBrowserPanel.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
                fileBrowserPanel.Opacity = visible ? 1 : 0;
            }

            if (sidebarColumn != null)
            {
                sidebarColumn.Width = visible ? _restoreSidebarColumnWidth : new GridLength(0);
            }

            if (fileBrowserColumn != null)
            {
                fileBrowserColumn.Width = visible ? _restoreFileBrowserColumnWidth : new GridLength(0);
            }
        }

        private void SetMediaCenterChromeVisible(bool visible)
        {
            var mediaCenter = FindAncestor<MediaCenterControl>(this);
            if (mediaCenter == null) return;

            if (!_hasRestoreMediaCenterChrome)
            {
                if (mediaCenter.TopNavBar != null) _restoreMediaCenterTopNavVisibility = mediaCenter.TopNavBar.Visibility;
                if (mediaCenter.LibraryContent != null) _restoreMediaCenterLibraryVisibility = mediaCenter.LibraryContent.Visibility;
                _hasRestoreMediaCenterChrome = true;
            }

            if (mediaCenter.TopNavBar != null)
                mediaCenter.TopNavBar.Visibility = visible ? _restoreMediaCenterTopNavVisibility : Visibility.Collapsed;

            if (mediaCenter.LibraryContent != null)
                mediaCenter.LibraryContent.Visibility = visible ? _restoreMediaCenterLibraryVisibility : Visibility.Collapsed;
        }

        private void RestoreFromFullscreenWindow()
        {
            if (_fullscreenWindow == null) return;
            if (_restoreParentPanel == null) return;

            try
            {
                _isRestoringFromFullscreen = true;

                var restoreSource = Player.Source;
                var restorePosition = Player.Position;
                var restoreWasPlaying = _isPlaying;

                var w = _fullscreenWindow;
                _fullscreenWindow = null;

                if (w.Content == this) w.Content = null;

                if (w.IsVisible)
                {
                    w.Close();
                }

                if (_restoreParentIndex < 0 || _restoreParentIndex > _restoreParentPanel.Children.Count)
                    _restoreParentPanel.Children.Add(this);
                else
                    _restoreParentPanel.Children.Insert(_restoreParentIndex, this);

                Grid.SetRow(this, _restoreGridRow);
                Grid.SetColumn(this, _restoreGridColumn);
                Grid.SetRowSpan(this, _restoreGridRowSpan);
                Grid.SetColumnSpan(this, _restoreGridColumnSpan);

                HorizontalAlignment = _restoreHorizontalAlignment;
                VerticalAlignment = _restoreVerticalAlignment;
                Margin = _restoreMargin;
                Width = _restoreWidth;
                Height = _restoreHeight;
                Player.Stretch = Stretch.Uniform;

                SetMediaCenterChromeVisible(true);

                _isFullscreen = false;
                ShowControls();

                if (restoreSource != null)
                {
                    ResumePlaybackAfterReattach(restoreSource, restorePosition, restoreWasPlaying);
                }

                Focus();
            }
            finally
            {
                _isRestoringFromFullscreen = false;
            }
        }

        private void ResumePlaybackAfterReattach(Uri source, TimeSpan position, bool wasPlaying)
        {
            try
            {
                _lastSource = source;
                _resumeSeconds = Math.Max(0, position.TotalSeconds);
                _resumeWasPlaying = wasPlaying;
                _resumePending = true;
                _isPlaying = false;
                UpdatePlayIcon();

                try { Player.Stop(); } catch { }
                try { Player.Source = null; } catch { }

                Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        Player.Source = source;
                    }
                    catch
                    {
                    }

                    StartResumeRetryTimer();
                }), DispatcherPriority.Background);
            }
            catch
            {
            }
        }

        private void StartResumeRetryTimer()
        {
            try
            {
                _resumeRetryTimer?.Stop();

                var tries = 0;
                _resumeRetryTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
                _resumeRetryTimer.Tick += (_, __) =>
                {
                    tries++;
                    TryApplyPendingResume();
                    if (!_resumePending || tries >= 20)
                    {
                        _resumeRetryTimer?.Stop();
                    }
                };
                _resumeRetryTimer.Start();
            }
            catch
            {
            }
        }

        private void TryApplyPendingResume()
        {
            try
            {
                if (!_resumePending) return;
                if (_lastSource == null) return;
                if (Player.Source == null) return;
                if (Player.Source != _lastSource) return;
                if (!Player.NaturalDuration.HasTimeSpan) return;

                Player.Position = TimeSpan.FromSeconds(Math.Max(0, _resumeSeconds));
                if (_resumeWasPlaying)
                {
                    Player.Play();
                    _isPlaying = true;
                    _timer.Start();
                }
                else
                {
                    Player.Pause();
                    _isPlaying = false;
                }

                UpdatePlayIcon();
                _resumePending = false;
            }
            catch
            {
            }
        }

        private void SubtitleButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_subtitleCues.Count == 0)
                {
                    var dlg = new OpenFileDialog
                    {
                        Title = "Select subtitle file",
                        Filter = "Subtitles (*.srt)|*.srt|All files (*.*)|*.*"
                    };

                    var ok = dlg.ShowDialog();
                    if (ok == true)
                    {
                        LoadSrtSubtitles(dlg.FileName);
                        _subtitlesEnabled = true;
                    }
                }
                else
                {
                    _subtitlesEnabled = !_subtitlesEnabled;
                    if (!_subtitlesEnabled)
                    {
                        SubtitleOverlay.Visibility = Visibility.Collapsed;
                        SubtitleText.Text = "";
                    }
                }

                SubtitleIcon.Opacity = (_subtitlesEnabled && _subtitleCues.Count > 0) ? 1.0 : 0.6;
                SubtitleTopButton.Opacity = SubtitleIcon.Opacity;
                ShowControls();
                UpdateSubtitles();
            }
            catch (Exception ex)
            {
                MessageOverlay.Visibility = Visibility.Visible;
                MessageText.Text = "Subtitles: " + ex.Message;
            }
        }

        private void LoadSrtSubtitles(string filePath)
        {
            _subtitleCues.Clear();
            _subtitleCueIndex = -1;

            var text = File.ReadAllText(filePath, Encoding.UTF8);
            var blocks = text.Replace("\r\n", "\n").Replace("\r", "\n")
                             .Split(new[] { "\n\n" }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var rawBlock in blocks)
            {
                var lines = rawBlock.Split('\n')
                                    .Select(l => l.TrimEnd())
                                    .Where(l => !string.IsNullOrWhiteSpace(l))
                                    .ToList();
                if (lines.Count < 2) continue;

                var timeLineIndex = 0;
                if (int.TryParse(lines[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                {
                    timeLineIndex = 1;
                }
                if (timeLineIndex >= lines.Count) continue;

                var timeLine = lines[timeLineIndex];
                var arrowIndex = timeLine.IndexOf("-->", StringComparison.Ordinal);
                if (arrowIndex <= 0) continue;

                var startText = timeLine.Substring(0, arrowIndex).Trim();
                var endText = timeLine.Substring(arrowIndex + 3).Trim();

                if (!TryParseSrtTime(startText, out var start)) continue;
                if (!TryParseSrtTime(endText, out var end)) continue;
                if (end <= start) continue;

                var subtitleText = string.Join("\n", lines.Skip(timeLineIndex + 1)).Trim();
                if (string.IsNullOrWhiteSpace(subtitleText)) continue;

                _subtitleCues.Add(new SubtitleCue { Start = start, End = end, Text = subtitleText });
            }

            _subtitleCues.Sort((a, b) => a.Start.CompareTo(b.Start));
            SubtitleIcon.Opacity = (_subtitlesEnabled && _subtitleCues.Count > 0) ? 1.0 : 0.6;
            SubtitleTopButton.Opacity = SubtitleIcon.Opacity;
        }

        private static bool TryParseSrtTime(string text, out TimeSpan time)
        {
            time = default;
            var s = (text ?? "").Trim();
            var parts = s.Split(new[] { ':', ',' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 4) return false;

            if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var hh)) return false;
            if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var mm)) return false;
            if (!int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var ss)) return false;
            if (!int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var ms)) return false;

            if (hh < 0 || hh > 99) return false;
            if (mm < 0 || mm > 59) return false;
            if (ss < 0 || ss > 59) return false;
            if (ms < 0 || ms > 999) return false;

            time = new TimeSpan(0, hh, mm, ss, ms);
            return true;
        }

        private void UpdateSubtitles()
        {
            if (!_subtitlesEnabled || _subtitleCues.Count == 0)
            {
                SubtitleOverlay.Visibility = Visibility.Collapsed;
                return;
            }

            if (!Player.NaturalDuration.HasTimeSpan || Player.Source == null)
            {
                SubtitleOverlay.Visibility = Visibility.Collapsed;
                return;
            }

            var pos = Player.Position;

            if (_subtitleCueIndex >= 0 && _subtitleCueIndex < _subtitleCues.Count)
            {
                var current = _subtitleCues[_subtitleCueIndex];
                if (pos >= current.Start && pos <= current.End)
                {
                    SubtitleText.Text = current.Text;
                    SubtitleOverlay.Visibility = Visibility.Visible;
                    return;
                }
            }

            var idx = FindCueIndex(pos);
            _subtitleCueIndex = idx;
            if (idx >= 0)
            {
                SubtitleText.Text = _subtitleCues[idx].Text;
                SubtitleOverlay.Visibility = Visibility.Visible;
            }
            else
            {
                SubtitleOverlay.Visibility = Visibility.Collapsed;
                SubtitleText.Text = "";
            }
        }

        private int FindCueIndex(TimeSpan position)
        {
            var lo = 0;
            var hi = _subtitleCues.Count - 1;

            while (lo <= hi)
            {
                var mid = lo + ((hi - lo) / 2);
                var cue = _subtitleCues[mid];
                if (position < cue.Start) hi = mid - 1;
                else if (position > cue.End) lo = mid + 1;
                else return mid;
            }

            return -1;
        }

        private static T? FindAncestor<T>(DependencyObject start) where T : DependencyObject
        {
            var current = start;
            while (current != null)
            {
                if (current is T t) return t;
                current = VisualTreeHelper.GetParent(current);
            }
            return null;
        }
        
        // Exposed methods for VM
        public void TogglePlayPause() => PlayPauseButton_Click(this, null);
        public void SetVolume(int vol) => Player.Volume = vol / 100.0;
        public bool IsMuted => Player?.IsMuted ?? false;
        public void SeekToSeconds(double sec) => Player.Position = TimeSpan.FromSeconds(sec);

        public void SetMuted(bool muted)
        {
            if (Player == null) return;
            Player.IsMuted = muted;
            UpdateVolumeIcons();
        }

        public void ToggleFullscreenMode()
        {
            if (_fullscreenWindow != null)
            {
                ExitFullscreen();
                return;
            }

            EnterFullscreen();
        }

        public double GetPlaybackSpeed()
        {
            try
            {
                return Player?.SpeedRatio ?? 1.0;
            }
            catch
            {
                return 1.0;
            }
        }

        public void SetPlaybackSpeed(double speed)
        {
            try
            {
                if (Player == null) return;
                Player.SpeedRatio = Math.Max(0.5, Math.Min(2.0, speed));
            }
            catch
            {
            }
        }

        public bool HasLoadedSubtitles() => _subtitleCues.Count > 0;

        public bool AreSubtitlesEnabled() => _subtitlesEnabled && _subtitleCues.Count > 0;

        public void SetSubtitleEnabled(bool enabled)
        {
            if (_subtitleCues.Count == 0) return;

            _subtitlesEnabled = enabled;
            if (!_subtitlesEnabled)
            {
                SubtitleOverlay.Visibility = Visibility.Collapsed;
                SubtitleText.Text = "";
            }

            SubtitleIcon.Opacity = (_subtitlesEnabled && _subtitleCues.Count > 0) ? 1.0 : 0.6;
            SubtitleTopButton.Opacity = SubtitleIcon.Opacity;
            UpdateSubtitles();
        }

        public string PlaybackOutputId => nameof(SimpleMediaPlayerControl);

        public void StopPlayback()
        {
            try
            {
                if (Player == null) return;
                Player.Stop();
                _timer.Stop();
                _isPlaying = false;
                UpdatePlayIcon();
            }
            catch
            {
            }
        }

        private static Rect GetScreenBoundsInDips(Window window, System.Drawing.Rectangle boundsPixels)
        {
            var source = PresentationSource.FromVisual(window);
            if (source?.CompositionTarget != null)
            {
                var transform = source.CompositionTarget.TransformFromDevice;
                var topLeft = transform.Transform(new Point(boundsPixels.Left, boundsPixels.Top));
                var bottomRight = transform.Transform(new Point(boundsPixels.Right, boundsPixels.Bottom));
                return new Rect(topLeft, bottomRight);
            }

            return new Rect(boundsPixels.Left, boundsPixels.Top, boundsPixels.Width, boundsPixels.Height);
        }
    }
}
