// MediaPlayerControl.xaml.cs - LibVLCSharp (VLC) Implementation
// Full codec support via VLC engine
// Split layout (Top/Video/Bottom) to avoid Airspace issues

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Documents;
using System.Windows.Threading;
using System.Runtime.InteropServices;
using System.IO.Compression;
using AtlasAI.MediaScanner;
using LibVLCSharp.Shared;
using LibVLCSharp.WPF;
using NAudio.CoreAudioApi;

namespace AtlasAI.Controls
{
    public class ProgressWidthConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length >= 3 && 
                values[0] is double currentValue && 
                values[1] is double maxValue && 
                values[2] is double totalWidth)
            {
                if (maxValue > 0)
                {
                    return (currentValue / maxValue) * totalWidth;
                }
            }
            return 0.0;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public partial class MediaPlayerControl : UserControl, System.ComponentModel.INotifyPropertyChanged, AtlasAI.MediaScanner.IPlaybackOutput
    {
        public sealed record MediaTrackOption(int Id, string Name);

        public event EventHandler? UserActivityDetected;
        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
            
            public static implicit operator Point(POINT point)
            {
                return new Point(point.X, point.Y);
            }
        }

        [DllImport("user32.dll")]
        public static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOACTIVATE = 0x0010;

        // Static singleton for LibVLC to avoid expensive re-initialization
        private static LibVLC? _sharedLibVLC;
        private static readonly object _vlcLock = new object();
        private static int _vlcLogHooked;
        private static readonly object _vlcLogSync = new object();
        private static string _vlcLogTail = "";

        private LibVLCSharp.Shared.MediaPlayer? _mediaPlayer;

        // Subtitle settings state
        private long _subtitleDelayMs = 0;
        private double _subtitleSizePercent = 100;
        private double _subtitleVerticalPercent = 5;
        private System.Windows.Controls.Primitives.Popup? _subtitlePopup;
        
        private DispatcherTimer _controlsTimer;
        private DispatcherTimer _mousePollingTimer;
        private DispatcherTimer _statusTimer;
        private DispatcherTimer _positionTimer;
        private DispatcherTimer? _languageDefaultsTimer;
        private Point _lastMousePosition;
        private bool _isPlaying = false;
        private bool _wasStopped;
        private bool _isDraggingSlider = false;
        private bool _areControlsHidden = false;
        private MediaItem? _currentMedia;
        private MediaItem? _pendingMediaToLoad;   // deferred load until VideoView HWND is ready
        private bool _mediaLoadQueued;
        private readonly object _endSync = new object();
        private bool _suppressEndReached = false;
        private string? _endedForPath;
        private DateTime _lastLoadStartedAtUtc = DateTime.MinValue;
        private bool _receivedLength = false;
        private bool _receivedTime = false;
        private bool _encounteredError = false;
        private int _loadRequestId = 0;
        private int _languageDefaultsAttempts = 0;
        private string? _languageDefaultsForPath;
        private bool _languageDefaultsAppliedAudio;
        private bool _languageDefaultsAppliedSubtitles;
        
        private bool _isVideoMode = false;
        private bool _isMuted = false;
        private double _volumeBeforeMute = 70;
        private DateTime _lastMouseMoveTime = DateTime.MinValue;
        private bool _isPlayerInitialized = false;
        private int _isClosing = 0;
        private int _isClosed = 0;
        private string _lastCoreInitDir = "";
        private string _lastPlaybackSource = "";
        
        /// <summary>
        /// Flag to prevent player shutdown when moving between visual trees (e.g. fullscreen toggle)
        /// </summary>
        public bool IsReparenting { get; set; } = false;

        public event EventHandler? PlaybackEnded;
        public event EventHandler? PlayerClosed;
        public event EventHandler? PlayerMinimized;
        
        public event EventHandler? FullscreenRequested;
        public event EventHandler<double>? PlaybackPositionChanged;
        public event EventHandler<double>? PlaybackDurationChanged;
        public event EventHandler<bool>? PlaybackStateChanged;
        
        private MediaPlaybackService? _playbackService;
        public MediaPlaybackService? PlaybackService 
        { 
            get => _playbackService;
            set 
            {
                if (_playbackService != null)
                {
                    _playbackService.CurrentMediaChanged -= PlaybackService_CurrentMediaChanged;
                }
                
                _playbackService = value;
                
                if (_playbackService != null)
                {
                    _playbackService.CurrentMediaChanged += PlaybackService_CurrentMediaChanged;
                }
            }
        }

        private void PlaybackService_CurrentMediaChanged(object? sender, MediaItem e)
        {
            System.Diagnostics.Debug.WriteLine($"[BridgePlayTrace] MediaPlayerControl.PlaybackService_CurrentMediaChanged entry path={e?.FilePath ?? ""} title={e?.DisplayName ?? e?.Title ?? ""}");
            // Always route through QueueMediaLoad so every code path defers until
            // the VideoView HWND is realized.  This prevents VLC from opening a
            // detached "Direct3D11 output" window regardless of media type or
            // whether the player is in fullscreen mode.
            if (Dispatcher.CheckAccess())
                QueueMediaLoad(e);
            else
                Dispatcher.BeginInvoke(new Action(() => QueueMediaLoad(e)));
        }

        /// <summary>
        /// Returns true once the VideoView HWND is realized and LibVLC can safely
        /// render into it.  Until this is true, assigning VideoView.MediaPlayer and
        /// calling Play() would cause VLC to open a detached Direct3D11 window.
        /// </summary>
        private bool IsVideoViewReady()
        {
            if (!IsVisible && !IsFullscreen)
                return false;

            return IsLoaded
                && VideoView != null
                && VideoView.IsLoaded
                && PresentationSource.FromVisual(VideoView) != null;
        }

        /// <summary>
        /// Stores <paramref name="media"/> and defers the actual load until
        /// <see cref="IsVideoViewReady"/> is true, then calls <see cref="LoadMedia"/>.
        /// </summary>
        private void QueueMediaLoad(MediaItem? media)
        {
            System.Diagnostics.Debug.WriteLine($"[BridgePlayTrace] MediaPlayerControl.QueueMediaLoad entry mediaNull={media == null} queued={_mediaLoadQueued} pendingPath={_pendingMediaToLoad?.FilePath ?? ""}");
            if (media == null)
                return;

            _pendingMediaToLoad = media;

            if (_mediaLoadQueued) return;

            _mediaLoadQueued = true;

            Dispatcher.BeginInvoke(
                DispatcherPriority.Loaded,
                new Action(TryLoadPendingMedia));
        }

        private void TryLoadPendingMedia()
        {
            System.Diagnostics.Debug.WriteLine($"[BridgePlayTrace] MediaPlayerControl.TryLoadPendingMedia entry queued={_mediaLoadQueued} pendingPath={_pendingMediaToLoad?.FilePath ?? ""}");
            _mediaLoadQueued = false;

            var media = _pendingMediaToLoad;
            LogVideoTrace($"TryLoadPendingMedia: media={(media == null ? "null" : media.FilePath ?? "")} IsVisible={IsVisible} IsFullscreen={IsFullscreen}");

            if (media == null)
                return;

            // Hold latest request until the player is actually visible.
            if (!IsVisible && !IsFullscreen)
            {
                LogVideoTrace($"TryLoadPendingMedia: early-exit !IsVisible && !IsFullscreen");
                return;
            }

            var ready = IsVideoViewReady();
            LogVideoTrace($"TryLoadPendingMedia: IsVideoViewReady={ready} IsLoaded={IsLoaded} VideoViewLoaded={VideoView?.IsLoaded} PresentationSrc={(PresentationSource.FromVisual(VideoView) != null)}");
            if (!ready)
            {
                QueueMediaLoad(media);
                return;
            }

            _pendingMediaToLoad = null;
            LoadMedia(media);
        }

        private bool _isFullscreen = false;
        private bool _useExternalControls = false;

        /// <summary>
        /// When true, an external overlay (e.g. FullscreenPlayerWindow) is providing the top
        /// chrome buttons.  TopControlBar is permanently hidden; ControlBar still auto-hides
        /// normally so the user keeps play/pause/seek.  UserActivityDetected still fires so
        /// the external overlay can sync its visibility.
        /// </summary>
        public bool UseExternalControls
        {
            get => _useExternalControls;
            set
            {
                _useExternalControls = value;
                if (value)
                {
                    // Use Hidden (not Collapsed) so Row 0 keeps its 60px height.
                    // Collapsed would shrink Row 0 to 0px, letting the LibVLC HWND (VideoView, Row 1)
                    // start at y=0 and swallow all click events on the external overlay buttons.
                    Dispatcher.InvokeAsync(() =>
                    {
                        if (TopControlBar != null) TopControlBar.Visibility = Visibility.Hidden;
                    }, System.Windows.Threading.DispatcherPriority.Render);
                }
            }
        }
        private bool _hasSavedWindowState = false;
        private WindowStyle _savedWindowStyle;
        private ResizeMode _savedResizeMode;
        private WindowState _savedWindowState;
        private bool _savedTopmost;
        public bool IsFullscreen 
        { 
            get => _isFullscreen;
            set 
            {
                _isFullscreen = value;
                UpdateFullscreenButton();
                if (!_isFullscreen)
                {
                    try { _controlsTimer?.Stop(); } catch { }
                    try { Mouse.OverrideCursor = Cursors.Arrow; } catch { }
                    ShowControls();
                }
                else
                {
                    if (_isPlaying && _isVideoMode)
                    {
                        try { _controlsTimer?.Stop(); } catch { }
                        try { _controlsTimer?.Start(); } catch { }
                    }
                }
            }
        }

        public bool IsMuted => _isMuted;

        public void Stop()
        {
            if (_mediaPlayer != null)
            {
                _suppressEndReached = true;
                try { _mediaPlayer.Stop(); } catch { }
                
                // Reset state
                _isPlaying = false;
                _wasStopped = true;
                try { _currentMedia = null; } catch { }
                UpdatePlayPauseIcon();

                try
                {
                    if (ProgressSlider != null) ProgressSlider.Value = 0;
                    if (CurrentTimeText != null) CurrentTimeText.Text = "0:00";
                }
                catch
                {
                }
                
                // Ensure controls are visible when stopped
                ShowControls();
            }

            try
            {
                if (!_isVideoMode)
                    PlaybackService?.ClearQueue();
            }
            catch
            {
            }
        }

        public string PlaybackOutputId => nameof(MediaPlayerControl);

        public void StopPlayback()
        {
            try { Stop(); } catch { }
        }
        
        public MediaPlayerControl()
        {
            InitializeComponent();

            _controlsTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(3)
            };
            _controlsTimer.Tick += ControlsTimer_Tick;

            _mousePollingTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(100)
            };
            _mousePollingTimer.Tick += MousePollingTimer_Tick;

            _statusTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(2)
            };
            _statusTimer.Tick += (s, e) =>
            {
                if (StatusOverlay != null)
                {
                    StatusOverlay.Visibility = Visibility.Collapsed;
                }
                _statusTimer.Stop();
            };

            _positionTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(250)
            };
            _positionTimer.Tick += (_, _) =>
            {
                try
                {
                    if (_mediaPlayer == null) return;
                    if (!_mediaPlayer.IsPlaying && !_isPlaying) return;

                    var lengthMs = _mediaPlayer.Length;
                    if (lengthMs > 0)
                    {
                        var total = TimeSpan.FromMilliseconds(lengthMs);
                        if (TotalTimeText != null) TotalTimeText.Text = FormatTime(total);
                        if (ProgressSlider != null) ProgressSlider.Maximum = total.TotalSeconds;
                        PlaybackDurationChanged?.Invoke(this, total.TotalSeconds);
                    }

                    var timeMs = _mediaPlayer.Time;
                    if (timeMs >= 0)
                    {
                        var current = TimeSpan.FromMilliseconds(timeMs);
                        if (!_isDraggingSlider)
                        {
                            if (CurrentTimeText != null) CurrentTimeText.Text = FormatTime(current);
                            if (ProgressSlider != null) ProgressSlider.Value = current.TotalSeconds;
                        }
                        PlaybackPositionChanged?.Invoke(this, current.TotalSeconds);
                    }
                }
                catch
                {
                    // Ignore errors during disposal/shutdown
                }
            };

            Focusable = true;
            KeyDown += MediaPlayerControl_KeyDown;
            this.Unloaded += MediaPlayerControl_Unloaded;
            this.Loaded += MediaPlayerControl_Loaded;
            this.IsVisibleChanged += MediaPlayerControl_IsVisibleChanged;
            // Play any media that was queued before the HWND was realized.
            if (VideoView != null)
                VideoView.Loaded += (_, _) => TryLoadPendingMedia();
        }

        private static void LogVideoTrace(string msg)
        {
            try
            {
                var path = System.IO.Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory, "VideoTrace.txt");
                System.IO.File.AppendAllText(path,
                    $"[{DateTime.Now:HH:mm:ss.fff}] {msg}\r\n");
            }
            catch { }
        }

        private void MediaPlayerControl_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            try
            {
                LogVideoTrace($"IsVisibleChanged: IsVisible={IsVisible}");
                if (IsVisible)
                    TryLoadPendingMedia();
            }
            catch
            {
            }
        }

        private void MediaPlayerControl_Loaded(object sender, RoutedEventArgs e)
        {
            // Ensure controls are visible on load
            // CRITICAL: Force initial visibility with high opacity
            _areControlsHidden = false;
            if (TopControlBar != null) { TopControlBar.Visibility = Visibility.Visible; TopControlBar.Opacity = 1.0; }
            if (ControlBar != null) { ControlBar.Visibility = Visibility.Visible; ControlBar.Opacity = 1.0; }

            if (_isPlayerInitialized && IsFullscreen)
                _controlsTimer.Start();

            // Update transport button visuals
            UpdateShuffleVisuals();
            UpdateRepeatVisuals();
            // Flush any media that was requested before the control was in the visual tree.
            TryLoadPendingMedia();
        }

        private void UpdateShuffleVisuals()
        {
            if (ShuffleButton == null) return;
            var isShuffled = PlaybackService?.IsShuffled ?? false;
            ShuffleButton.Opacity = isShuffled ? 1.0 : 0.5;
        }

        private void UpdateRepeatVisuals()
        {
            if (RepeatButton == null) return;
            var isRepeat = PlaybackService?.IsRepeat ?? false;
            RepeatButton.Opacity = isRepeat ? 1.0 : 0.5;
        }

        private void EnsurePlayerInitialized()
        {
            if (_isPlayerInitialized) return;
            Volatile.Write(ref _isClosed, 0);

            try
            {
                try
                {
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
                    catch
                    {
                        libVlcDir = "";
                    }

                    if (!string.IsNullOrWhiteSpace(libVlcDir))
                        LibVLCSharp.Shared.Core.Initialize(libVlcDir);
                    else
                        LibVLCSharp.Shared.Core.Initialize();

                    _lastCoreInitDir = libVlcDir;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[MediaPlayer] Core.Initialize failed: {ex.Message}");
                }

                InitializePlayer();

                _isPlayerInitialized = _mediaPlayer != null;
                if (_isPlayerInitialized)
                {
                    _mousePollingTimer.Start();
                    _positionTimer.Start();
                    if (IsLoaded && IsFullscreen)
                        _controlsTimer.Start();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MediaPlayer] Init error: {ex.Message}");
                ShowError($"Player initialization failed: {ex.Message}");
            }
        }

        private void InitializePlayer()
        {
            try
            {
                var startTime = DateTime.Now;
                System.Diagnostics.Debug.WriteLine($"[MediaPlayer] {startTime:HH:mm:ss.fff} Initializing VLC player");
                
                // Initialize Shared LibVLC if needed
                if (_sharedLibVLC == null)
                {
                    lock (_vlcLock)
                    {
                        if (_sharedLibVLC == null)
                        {
                            System.Diagnostics.Debug.WriteLine($"[MediaPlayer] Creating new LibVLC instance (First run)");
                            var options = new string[] 
                            {
                                "--no-video-title-show",
                                "--verbose=0"
                        };
                        _sharedLibVLC = new LibVLC(options);
                        }
                    }
                }

                if (_sharedLibVLC != null && Interlocked.Exchange(ref _vlcLogHooked, 1) == 0)
                {
                    try
                    {
                        _sharedLibVLC.Log += (_, e) =>
                        {
                            try
                            {
                                var msg = (e.Message ?? "").Trim();
                                if (string.IsNullOrWhiteSpace(msg)) return;
                                var line = $"{DateTime.Now:HH:mm:ss} [{e.Level}] {msg}";
                                lock (_vlcLogSync)
                                {
                                    _vlcLogTail = (_vlcLogTail + "\n" + line).Trim();
                                    if (_vlcLogTail.Length > 4000)
                                        _vlcLogTail = _vlcLogTail.Substring(_vlcLogTail.Length - 4000);
                                }
                            }
                            catch
                            {
                            }
                        };
                    }
                    catch
                    {
                    }
                }

                // Create MediaPlayer from shared LibVLC
                _mediaPlayer = new LibVLCSharp.Shared.MediaPlayer(_sharedLibVLC);

                _mediaPlayer.AspectRatio = null;
                _mediaPlayer.Scale = 0;
                
                // Assign to VideoView
                VideoView.MediaPlayer = _mediaPlayer;
                
                // Wire up VLC events
                _mediaPlayer.LengthChanged += MediaPlayer_LengthChanged;
                _mediaPlayer.TimeChanged += MediaPlayer_TimeChanged;
                _mediaPlayer.EndReached += MediaPlayer_EndReached;
                _mediaPlayer.EncounteredError += MediaPlayer_EncounteredError;
                _mediaPlayer.Playing += (s, e) => Dispatcher.BeginInvoke(new Action(() =>
                {
                    _suppressEndReached = false;
                    _isPlaying = true;
                    UpdatePlayPauseIcon();
                    PlaybackStateChanged?.Invoke(this, true);

                    // Bring VLC's render HWND to the top of the Win32 z-order so it
                    // renders above WebView2's HWND (WPF Panel.ZIndex has no effect
                    // on Win32 sibling windows).
                    try
                    {
                        var vlcHwnd = _mediaPlayer?.Hwnd ?? IntPtr.Zero;
                        LogVideoTrace($"Playing event: vlcHwnd=0x{vlcHwnd:X} mediaType={_currentMedia?.MediaType} section={_currentMedia?.SectionName} isVideo={(vlcHwnd != IntPtr.Zero)}");
                        if (vlcHwnd != IntPtr.Zero)
                        {
                            var ok = SetWindowPos(vlcHwnd, new IntPtr(0), 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
                            LogVideoTrace($"Playing event: SetWindowPos result={ok}");
                        }
                    }
                    catch (Exception ex) { LogVideoTrace($"Playing event SetWindowPos EXCEPTION: {ex.Message}"); }
                    
                    // VLC resets volume on new media — force it back
                    try
                    {
                        if (_mediaPlayer != null && !_isMuted)
                        {
                            var vol = (int)VolumeSlider.Value;
                            if (vol < 1) vol = 70;
                            _mediaPlayer.Volume = vol;
                            System.Diagnostics.Debug.WriteLine($"[MediaPlayer] Playing event: forced Volume={vol}");
                        }
                    }
                    catch { }
                    
                    // Fix Windows per-app mixer volume (Windows persists low volume from previous sessions)
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(500);
                        try { ResetWindowsMixerVolume(); } catch { }
                    });
                }));
                _mediaPlayer.Paused += (s, e) => Dispatcher.BeginInvoke(new Action(() =>
                {
                    _isPlaying = false;
                    UpdatePlayPauseIcon();
                    PlaybackStateChanged?.Invoke(this, false);
                }));
                _mediaPlayer.Stopped += (s, e) => Dispatcher.BeginInvoke(new Action(() =>
                {
                    _isPlaying = false;
                    UpdatePlayPauseIcon();
                    PlaybackStateChanged?.Invoke(this, false);
                }));

                // Initialize Volume — use slider default or 70
                var initVol = (int)VolumeSlider.Value;
                if (initVol < 1) initVol = 70;
                _mediaPlayer.Volume = initVol;
                System.Diagnostics.Debug.WriteLine($"[MediaPlayer] Init volume: {initVol}");
                
                SetAudioMode();
                UpdatePlayPauseIcon();
                UpdateMuteIcon();
                
                // Fix white bleed: force black on all internal surfaces.
                ForceVideoViewBlack();
                // Also hook Loaded so the visual tree is walked again after layout.
                VideoView.Loaded -= VideoView_Loaded;
                VideoView.Loaded += VideoView_Loaded;
                
                var duration = DateTime.Now - startTime;
                System.Diagnostics.Debug.WriteLine($"[MediaPlayer] VLC initialization complete in {duration.TotalMilliseconds}ms");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MediaPlayer] Init error: {ex.Message}");
                ShowError($"Player initialization failed: {ex.Message}");
            }
        }

        private void MediaPlayerControl_Unloaded(object sender, RoutedEventArgs e)
        {
            if (IsReparenting) return;
            // Keep audio playing when the media page is collapsed (user switched sections).
            // Only dispose the player when nothing is playing or when video is active
            // (video needs a visible surface so there's no point keeping it alive).
            if (_isPlaying && !_isVideoMode) return;
            ClosePlayer();
        }

        #region VLC Event Handlers

        private void MediaPlayer_LengthChanged(object? sender, MediaPlayerLengthChangedEventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                _receivedLength = e.Length > 0;
                var duration = TimeSpan.FromMilliseconds(e.Length);
                TotalTimeText.Text = FormatTime(duration);
                ProgressSlider.Maximum = duration.TotalSeconds;
                System.Diagnostics.Debug.WriteLine($"[MediaPlayer] Duration: {FormatTime(duration)}");
                PlaybackDurationChanged?.Invoke(this, duration.TotalSeconds);
            }));
        }

        private void MediaPlayer_TimeChanged(object? sender, MediaPlayerTimeChangedEventArgs e)
        {
            if (_isDraggingSlider) return;

            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (e.Time > 0)
                    _receivedTime = true;
                var time = TimeSpan.FromMilliseconds(e.Time);
                CurrentTimeText.Text = FormatTime(time);
                ProgressSlider.Value = time.TotalSeconds;
                PlaybackPositionChanged?.Invoke(this, time.TotalSeconds);
                UpdateLyricsSync(time);
            }));
        }

        private void MediaPlayer_EndReached(object? sender, EventArgs e)
        {
            lock (_endSync)
            {
                if (_suppressEndReached)
                    return;

                if (_currentMedia == null)
                    return;

                if (_encounteredError)
                    return;

                if (!_receivedTime && (DateTime.UtcNow - _lastLoadStartedAtUtc).TotalMilliseconds < 2000)
                    return;

                if (string.Equals(_endedForPath, _currentMedia.FilePath, StringComparison.OrdinalIgnoreCase))
                    return;

                _endedForPath = _currentMedia.FilePath;
            }

            Dispatcher.BeginInvoke(new Action(() =>
            {
                System.Diagnostics.Debug.WriteLine("[MediaPlayer] Media ended");
                _isPlaying = false;
                UpdatePlayPauseIcon();
                try { PlaybackStateChanged?.Invoke(this, false); } catch { }
                PlaybackEnded?.Invoke(this, EventArgs.Empty);
            }));
        }

        private void MediaPlayer_EncounteredError(object? sender, EventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    if (_currentMedia != null &&
                        !_retryNoHw &&
                        _errorRetryAttempt == 0)
                    {
                        _errorRetryAttempt = 1;
                        _retryNoHw = true;
                        _encounteredError = false;
                        _suppressEndReached = true;
                        try { ErrorOverlay.Visibility = Visibility.Collapsed; } catch { }
                        try { LoadingOverlay.Visibility = Visibility.Visible; } catch { }
                        Dispatcher.BeginInvoke(new Action(() =>
                        {
                            try
                            {
                                if (_currentMedia != null)
                                    LoadMedia(_currentMedia);
                            }
                            catch
                            {
                            }
                        }));
                        return;
                    }
                }
                catch
                {
                }

                _encounteredError = true;
                _suppressEndReached = true;
                System.Diagnostics.Debug.WriteLine($"[MediaPlayer] Media failed");
                ShowError("Playback error occurred. Check file format or codecs.");
                LoadingOverlay.Visibility = Visibility.Collapsed;
            }));
        }

        #endregion

        #region Public Methods

        private CancellationTokenSource? _loadCts;
        private int _errorRetryAttempt;
        private bool _retryNoHw;
        private string _lastLoadSource = "";
        private Media? _activeMedia;

        public async void LoadMedia(MediaItem mediaItem)
        {
            System.Diagnostics.Debug.WriteLine($"[BridgePlayTrace] MediaPlayerControl.LoadMedia entry path={mediaItem?.FilePath ?? ""} title={mediaItem?.DisplayName ?? mediaItem?.Title ?? ""}");
            // Cancel any pending load
            _loadCts?.Cancel();
            _loadCts = new CancellationTokenSource();
            var token = _loadCts.Token;

            try
            {
                if (mediaItem == null) return;
                var loadSource = (mediaItem.FilePath ?? "").Trim();
                if (!string.Equals(_lastLoadSource, loadSource, StringComparison.OrdinalIgnoreCase))
                {
                    _lastLoadSource = loadSource;
                    _errorRetryAttempt = 0;
                    _retryNoHw = false;
                }

                // Keep a small debounce for rapid key-repeat, but minimize stream startup delay.
                var sourcePath = (mediaItem.FilePath ?? "").Trim();
                var isHttpLike = Uri.TryCreate(sourcePath, UriKind.Absolute, out var preloadUri) &&
                                 (string.Equals(preloadUri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
                                  string.Equals(preloadUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase));
                var debounceMs = isHttpLike ? 40 : 120;
                try { await Task.Delay(debounceMs, token); } catch (TaskCanceledException) { return; }
                if (token.IsCancellationRequested) return;

                System.Diagnostics.Debug.WriteLine($"[MediaPlayer] Loading: {mediaItem.DisplayName}");

                // Safety net for any direct caller of LoadMedia: if VideoView HWND is not
                // yet realized, store as pending and let TryLoadPendingMedia retry once the
                // control is visible.  This is the last guard before LibVLC touches the HWND.
                if (!IsVideoViewReady())
                {
                    QueueMediaLoad(mediaItem);
                    return;
                }

                System.Diagnostics.Debug.WriteLine($"[BridgePlayTrace] MediaPlayerControl.LoadMedia before EnsurePlayerInitialized path={mediaItem.FilePath ?? ""}");
                EnsurePlayerInitialized();
                if (_mediaPlayer == null)
                {
                    ShowError("Player initialization failed.");
                    return;
                }

                if (VideoView.MediaPlayer != _mediaPlayer)
                {
                    LogVideoTrace($"LoadMedia: assigning VideoView.MediaPlayer. PlayerHwnd=0x{_mediaPlayer.Hwnd:X} IsVisible={IsVisible} VideoViewLoaded={VideoView.IsLoaded}");
                    VideoView.MediaPlayer = _mediaPlayer;
                    LogVideoTrace($"LoadMedia: after assign, PlayerHwnd=0x{_mediaPlayer.Hwnd:X}");
                }

                var source = mediaItem.FilePath ?? "";
                _lastPlaybackSource = source;
                var isHttp = Uri.TryCreate(source, UriKind.Absolute, out var httpUri) &&
                             (string.Equals(httpUri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
                              string.Equals(httpUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase));
                var isZipInternalPath = !isHttp && source.Contains("|");
                var zipOuterPath = "";
                if (isZipInternalPath)
                {
                    try
                    {
                        var parts = source.Split('|');
                        if (parts.Length >= 2)
                            zipOuterPath = parts[0];
                    }
                    catch
                    {
                        zipOuterPath = "";
                    }
                }

                var requestId = Interlocked.Increment(ref _loadRequestId);
                if (_currentMedia != null &&
                    string.Equals(_currentMedia.FilePath, mediaItem.FilePath, StringComparison.OrdinalIgnoreCase) &&
                    _mediaPlayer != null &&
                    _mediaPlayer.IsPlaying)
                {
                    return;
                }
                
                _currentMedia = mediaItem;
                lock (_endSync)
                {
                    _endedForPath = null;
                    _lastLoadStartedAtUtc = DateTime.UtcNow;
                }
                _receivedLength = false;
                _receivedTime = false;
                _encounteredError = false;
                
                if (!isHttp)
                {
                    if (isZipInternalPath)
                    {
                        if (string.IsNullOrWhiteSpace(zipOuterPath) || !File.Exists(zipOuterPath))
                        {
                            ShowError($"File not found: {zipOuterPath}");
                            return;
                        }
                    }
                    else if (!File.Exists(source))
                    {
                        ShowError($"File not found: {source}");
                        return;
                    }
                }
                
                LoadingOverlay.Visibility = Visibility.Visible;
                ErrorOverlay.Visibility = Visibility.Collapsed;
                
                // Do not explicitly Stop() here. Let Play() handle the transition internally.
                // Explicitly calling Stop() on background threads causes race conditions with VideoView.
                
                if (token.IsCancellationRequested) return;

                var extension = isHttp
                    ? Path.GetExtension(httpUri.AbsolutePath).ToLowerInvariant()
                    : (isZipInternalPath
                        ? Path.GetExtension(source.Split('|').Last()).ToLowerInvariant()
                        : Path.GetExtension(source).ToLowerInvariant());
                var videoExtensions = new[] { ".mp4", ".mkv", ".avi", ".mov", ".webm", ".m4v", ".flv", ".wmv", ".cdg", ".zip" };
                
                // CRITICAL: Treat Unknown media type as Video to ensure player controls/surface are shown for streams
                _isVideoMode = mediaItem.MediaType == AtlasAI.MediaScanner.MediaType.Video || 
                               mediaItem.MediaType == AtlasAI.MediaScanner.MediaType.Unknown ||
                               Array.Exists(videoExtensions, ext => ext == extension);

                if (!_isVideoMode &&
                    string.Equals(mediaItem.SectionName, "cloud", StringComparison.OrdinalIgnoreCase) &&
                    isHttp &&
                    string.IsNullOrWhiteSpace(extension))
                {
                    _isVideoMode = true;
                }

                // Force video mode if we are playing from Karaoke section
                if (mediaItem.SectionName != null && mediaItem.SectionName.Equals("Karaoke", StringComparison.OrdinalIgnoreCase))
                {
                    _isVideoMode = true;
                }
                
                if (_isVideoMode)
                {
                    SetVideoMode();
                }
                else
                {
                    SetAudioMode();
                }

                // KARAOKE ZIP HANDLING
                // If it is a local zip file, unzip it to temp and play the MP3 inside
                if (!isHttp)
                {
                    if (extension == ".zip")
                    {
                        // Legacy handling for full zip
                        source = await Task.Run(() => ExtractKaraokeZip(source));
                    }
                    else if (source.Contains("|"))
                    {
                        // New handling for internal zip path: "PathToZip|InternalPath"
                        var parts = source.Split('|');
                        if (parts.Length == 2)
                        {
                            var zipPath = parts[0];
                            var internalPath = parts[1];
                            if (Path.GetExtension(zipPath).Equals(".zip", StringComparison.OrdinalIgnoreCase))
                            {
                                source = await Task.Run(() => ExtractSpecificFileFromZip(zipPath, internalPath));
                            }
                        }
                        else if (parts.Length == 3)
                        {
                            var zipPath = parts[0];
                            var nestedZipInternal = parts[1];
                            var internalPath = parts[2];
                            if (Path.GetExtension(zipPath).Equals(".zip", StringComparison.OrdinalIgnoreCase))
                            {
                                source = await Task.Run(() => ExtractSpecificFileFromNestedZip(zipPath, nestedZipInternal, internalPath));
                            }
                        }
                    }
                }
                
                if (token.IsCancellationRequested) return;

                UpdateMediaMetadata(mediaItem);
                LoadLyricsForCurrentMedia();
                
                if (_sharedLibVLC != null && _mediaPlayer != null)
                {
                    var startTime = DateTime.Now;
                    
                    // CRITICAL: Ensure VideoView has the MediaPlayer assigned BEFORE playing
                    // If we are in video mode, we must ensure the view is ready
                    if (_isVideoMode)
                    {
                        if (VideoView.MediaPlayer == null)
                            VideoView.MediaPlayer = _mediaPlayer;
                    }

                    // Create media — DO NOT dispose immediately!  LibVLC Play() is async, 
                    // disposing the Media too early kills playback silently on some codec paths.
                    var media = isHttp
                        ? new Media(_sharedLibVLC, httpUri.AbsoluteUri, FromType.FromLocation)
                        : new Media(_sharedLibVLC, source, FromType.FromPath);
                    
                    if (token.IsCancellationRequested) { media.Dispose(); return; }
                    if (requestId != _loadRequestId) { media.Dispose(); return; }
                    
                    if (_retryNoHw)
                        media.AddOption(":avcodec-hw=none");
                    media.AddOption(":no-video-title-show");
                    if (isHttp)
                    {
                        media.AddOption(":http-reconnect");
                    }
                    
                    // Play() implicitly stops the previous media
                    try { PlaybackOutputCoordinator.SetActive(this); } catch { }
                    try { _activeMedia?.Dispose(); } catch { }
                    _activeMedia = media;
                    System.Diagnostics.Debug.WriteLine($"[BridgePlayTrace] MediaPlayerControl.LoadMedia before MediaPlayer.Play source={source}");
                    var playResult = _mediaPlayer.Play(media);
                    System.Diagnostics.Debug.WriteLine($"[BridgePlayTrace] MediaPlayerControl.LoadMedia after MediaPlayer.Play result={playResult} source={source}");
                    // Ensure volume is applied after play starts (VLC can reset it on new media)
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(300);
                        try
                        {
                            await Dispatcher.InvokeAsync(() =>
                            {
                                if (_mediaPlayer != null && !_isMuted)
                                {
                                    var vol = (int)VolumeSlider.Value;
                                    if (vol < 1) vol = 70;
                                    _mediaPlayer.Volume = vol;
                                    System.Diagnostics.Debug.WriteLine($"[MediaPlayer] Deferred volume reapply: {vol}");
                                }
                            });
                        }
                        catch { }
                    });
                    // Second volume reapply after longer delay for stubborn streams
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(1500);
                        try
                        {
                            await Dispatcher.InvokeAsync(() =>
                            {
                                if (_mediaPlayer != null && !_isMuted)
                                {
                                    var vol = (int)VolumeSlider.Value;
                                    if (vol < 1) vol = 70;
                                    _mediaPlayer.Volume = vol;
                                }
                            });
                        }
                        catch { }
                    });
                    var duration = DateTime.Now - startTime;
                    System.Diagnostics.Debug.WriteLine($"[MediaPlayer] Media.Play() called in {duration.TotalMilliseconds}ms");
                }
                
                _isPlaying = true;
                _wasStopped = false;
                UpdatePlayPauseIcon();
                ProgressSlider.Value = 0;
                CurrentTimeText.Text = "0:00";
                TotalTimeText.Text = "0:00";
                
                LoadingOverlay.Visibility = Visibility.Collapsed;
                
                ShowControls();
                StartLanguageDefaultsForCurrentMedia();
                
                System.Diagnostics.Debug.WriteLine("[MediaPlayer] Load complete");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MediaPlayer] Load error: {ex.Message}");
                // Don't show error dialog for task cancellation or rapid switching
                if (ex is not TaskCanceledException && ex is not OperationCanceledException)
                {
                    ShowError($"Failed to load media: {ex.Message}");
                }
            }
        }

        private string ExtractKaraokeZip(string zipPath)
        {
            try
            {
                var cacheDir = Path.Combine(Path.GetTempPath(), "AtlasAI", "KaraokeCache");
                // Use LastWriteTime in hash to ensure we re-extract if zip changes
                var fileInfo = new FileInfo(zipPath);
                var hash = (zipPath + fileInfo.LastWriteTimeUtc.Ticks).GetHashCode().ToString("X");
                var zipName = Path.GetFileNameWithoutExtension(zipPath);
                var extractPath = Path.Combine(cacheDir, $"{zipName}_{hash}");

                var alreadyExtracted = Directory.Exists(extractPath);
                if (!alreadyExtracted)
                {
                     try
                     {
                         if (Directory.Exists(extractPath)) Directory.Delete(extractPath, true);
                         Directory.CreateDirectory(extractPath);
                         System.IO.Compression.ZipFile.ExtractToDirectory(zipPath, extractPath);
                     }
                     catch (Exception ex)
                     {
                         System.Diagnostics.Debug.WriteLine($"[MediaPlayer] Zip extraction failed: {ex.Message}");
                         return zipPath;
                     }
                }

                var extractedFiles = Directory.GetFiles(extractPath, "*.*", SearchOption.AllDirectories);
                var audioFiles = extractedFiles
                    .Where(f => f.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase) || 
                                f.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(f => f)
                    .ToList();
                
                if (audioFiles.Count == 0) return zipPath;

                // Process ALL files to ensure CDGs are named correctly
                foreach (var audio in audioFiles)
                {
                    var audioName = Path.GetFileNameWithoutExtension(audio);
                    var dir = Path.GetDirectoryName(audio);
                    if (dir == null) continue;

                    // Look for matching CDG in same dir
                    var cdg = Directory.GetFiles(dir, "*.cdg")
                        .FirstOrDefault(f => Path.GetFileNameWithoutExtension(f).Equals(audioName, StringComparison.OrdinalIgnoreCase));
                    
                    if (cdg == null)
                    {
                         // Try fuzzy match? Or maybe the CDG has a different name
                         // For now, if we can't find exact match, look for ANY cdg in the folder if it's the only one
                         var allCdgs = Directory.GetFiles(dir, "*.cdg");
                         if (allCdgs.Length == 1 && audioFiles.Count(a => Path.GetDirectoryName(a) == dir) == 1)
                         {
                             // 1 audio, 1 cdg. Assume match.
                             cdg = allCdgs[0];
                         }
                    }

                    if (cdg != null)
                    {
                        var cdgName = Path.GetFileNameWithoutExtension(cdg);
                        if (!string.Equals(audioName, cdgName, StringComparison.OrdinalIgnoreCase))
                        {
                            try
                            {
                                var newCdgPath = Path.Combine(dir, audioName + ".cdg");
                                if (!File.Exists(newCdgPath))
                                    File.Move(cdg, newCdgPath);
                            }
                            catch { }
                        }
                    }
                }

                // PACK HANDLING: If multiple songs, queue the rest
                if (audioFiles.Count > 1)
                {
                    // We need to inject the REST of the songs into the queue
                    // But we can't easily do that from here without access to PlaybackService queue manipulation
                    // Fortunately we have _playbackService access via property or instance if available?
                    // In this class, we might not have direct access to _playbackService instance easily unless passed in.
                    // Wait, this is MediaPlayerControl.xaml.cs? No, it's partial.
                    // MediaCenterControl has _playbackService. MediaPlayerControl usually just has a player.
                    // Let's check if we have access to PlaybackService.
                    
                    // Actually, we can use MediaPlaybackService.Instance (singleton) if available
                    if (AtlasAI.MediaScanner.MediaPlaybackService.Instance != null)
                    {
                        var newItems = new List<AtlasAI.MediaScanner.MediaItem>();
                        // Add everything AFTER the first one
                        for (int i = 1; i < audioFiles.Count; i++)
                        {
                            var path = audioFiles[i];
                            newItems.Add(new AtlasAI.MediaScanner.MediaItem
                            {
                                FilePath = path,
                                MediaType = AtlasAI.MediaScanner.MediaType.Video, // Force Video for Karaoke
                                DisplayName = Path.GetFileNameWithoutExtension(path),
                                SectionName = "Karaoke", // CRITICAL for IsVideoMedia
                                DateAdded = DateTime.UtcNow
                            });
                        }
                        
                        // Queue them next
                        // We need to run this on UI thread or ensure thread safety? 
                        // QueueNext uses lock, so it's safe.
                        // But we want to insert them all at once. QueueNext(Item) inserts at index+1.
                        // If we call it in reverse order, or if we had AddRange...
                        // QueueNext inserts at _currentIndex + 1.
                        // If we loop i = 1 to N, and call QueueNext, 
                        // 1 inserts at pos 1. 2 inserts at pos 1 (pushing 1 down). 
                        // So we should insert in REVERSE order to keep them 1, 2, 3...
                        // OR insert the last one first.
                        
                        // Correct: QueueNext inserts immediately after current.
                        // If we have [Current], and we want [Current], [1], [2], [3]...
                        // Call QueueNext(3) -> [Current], [3]
                        // Call QueueNext(2) -> [Current], [2], [3]
                        // Call QueueNext(1) -> [Current], [1], [2], [3]
                        
                        for (int i = audioFiles.Count - 1; i >= 1; i--)
                        {
                            AtlasAI.MediaScanner.MediaPlaybackService.Instance.QueueNext(newItems[i - 1]);
                        }
                    }
                }

                return audioFiles[0];
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MediaPlayer] Error extracting karaoke zip: {ex.Message}");
                return zipPath;
            }
        }

        private string ExtractSpecificFileFromZip(string zipPath, string internalPath)
        {
            try
            {
                var cacheDir = Path.Combine(Path.GetTempPath(), "AtlasAI", "KaraokeCache");
                var zipName = Path.GetFileNameWithoutExtension(zipPath);
                // Use hash of internal path to avoid collisions
                var hash = (zipPath + internalPath).GetHashCode().ToString("X");
                var extractPath = Path.Combine(cacheDir, $"{zipName}_{hash}");
                
                Directory.CreateDirectory(extractPath);
                
                var fileName = Path.GetFileName(internalPath);
                var targetFile = Path.Combine(extractPath, fileName);
                
                if (File.Exists(targetFile)) return targetFile;
                
                using (var archive = System.IO.Compression.ZipFile.OpenRead(zipPath))
                {
                    // Find the MP3/WAV entry
                    var entry = archive.GetEntry(internalPath.Replace("\\", "/")) 
                                ?? archive.Entries.FirstOrDefault(e => e.FullName.Replace("/", "\\").Equals(internalPath.Replace("/", "\\"), StringComparison.OrdinalIgnoreCase));
                                
                    if (entry != null)
                    {
                        entry.ExtractToFile(targetFile, true);
                        
                        // Try to find and extract CDG too
                        var baseName = Path.GetFileNameWithoutExtension(internalPath);
                        var dir = Path.GetDirectoryName(internalPath) ?? "";
                        
                        // Look for any CDG in the same folder inside zip with same name
                        // Note: Zip entries use forward slashes usually
                        var cdgEntry = archive.Entries.FirstOrDefault(e => 
                        {
                            var eDir = Path.GetDirectoryName(e.FullName) ?? "";
                            var eName = Path.GetFileNameWithoutExtension(e.Name);
                            var eExt = Path.GetExtension(e.Name);
                            return eExt.Equals(".cdg", StringComparison.OrdinalIgnoreCase) && 
                                   eName.Equals(baseName, StringComparison.OrdinalIgnoreCase);
                        });
                        
                        if (cdgEntry != null)
                        {
                            var cdgTarget = Path.Combine(extractPath, Path.GetFileNameWithoutExtension(fileName) + ".cdg");
                            cdgEntry.ExtractToFile(cdgTarget, true);
                        }
                        
                        return targetFile;
                    }
                }
                
                return zipPath;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MediaPlayer] Error extracting specific file from zip: {ex.Message}");
                return zipPath;
            }
        }

        private string ExtractSpecificFileFromNestedZip(string outerZipPath, string nestedZipInternalPath, string internalPath)
        {
            try
            {
                var nestedZipFile = ExtractZipEntryToFile(outerZipPath, nestedZipInternalPath);
                if (string.IsNullOrWhiteSpace(nestedZipFile) || !File.Exists(nestedZipFile)) return outerZipPath;
                return ExtractSpecificFileFromZip(nestedZipFile, internalPath);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MediaPlayer] Error extracting nested zip: {ex.Message}");
                return outerZipPath;
            }
        }

        private string ExtractZipEntryToFile(string outerZipPath, string entryInternalPath)
        {
            try
            {
                var cacheDir = Path.Combine(Path.GetTempPath(), "AtlasAI", "KaraokeCache");
                var zipName = Path.GetFileNameWithoutExtension(outerZipPath);
                var hash = (outerZipPath + entryInternalPath).GetHashCode().ToString("X");
                var extractPath = Path.Combine(cacheDir, $"{zipName}_{hash}_nested");
                Directory.CreateDirectory(extractPath);

                var targetName = Path.GetFileName(entryInternalPath);
                if (string.IsNullOrWhiteSpace(targetName)) targetName = "nested.zip";
                if (!targetName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) targetName += ".zip";
                var targetFile = Path.Combine(extractPath, targetName);

                if (File.Exists(targetFile)) return targetFile;

                using var archive = ZipFile.OpenRead(outerZipPath);
                var normalized = entryInternalPath.Replace("\\", "/");
                var entry = archive.GetEntry(normalized) ??
                            archive.Entries.FirstOrDefault(e =>
                                string.Equals(e.FullName.Replace("/", "\\"), entryInternalPath.Replace("/", "\\"), StringComparison.OrdinalIgnoreCase));
                if (entry == null) return outerZipPath;

                entry.ExtractToFile(targetFile, true);
                return targetFile;
            }
            catch
            {
                return outerZipPath;
            }
        }

        public void ClosePlayer()
        {
            if (!Dispatcher.CheckAccess())
            {
                try { Dispatcher.Invoke(ClosePlayer); } catch { }
                return;
            }

            if (Volatile.Read(ref _isClosed) == 1)
                return;

            if (Interlocked.Exchange(ref _isClosing, 1) == 1)
                return;

            try
            {
                System.Diagnostics.Debug.WriteLine("[MediaPlayer] Closing player");

                try { _loadCts?.Cancel(); } catch { }
                try { _loadCts?.Dispose(); } catch { }
                _loadCts = null;

                _controlsTimer?.Stop();
                _mousePollingTimer?.Stop();
                _positionTimer?.Stop();
                try { _languageDefaultsTimer?.Stop(); } catch { }

                _suppressEndReached = true;
                LibVLCSharp.Shared.MediaPlayer? playerToDispose = null;
                try
                {
                    playerToDispose = _mediaPlayer;
                    _mediaPlayer = null;
                    _isPlayerInitialized = false;

                    if (playerToDispose != null)
                    {
                        try { playerToDispose.LengthChanged -= MediaPlayer_LengthChanged; } catch { }
                        try { playerToDispose.TimeChanged -= MediaPlayer_TimeChanged; } catch { }
                        try { playerToDispose.EndReached -= MediaPlayer_EndReached; } catch { }
                        try { playerToDispose.EncounteredError -= MediaPlayer_EncounteredError; } catch { }
                    }

                    try { VideoView.MediaPlayer = null; } catch { }
                }
                catch
                {
                    playerToDispose = null;
                }

                try { _activeMedia?.Dispose(); } catch { }
                _activeMedia = null;

                _currentMedia = null;
                _isPlaying = false;

                if (playerToDispose != null)
                {
                    var mp = playerToDispose;
                    try { mp.Stop(); } catch { }
                    try { mp.Dispose(); } catch { }
                }

                Volatile.Write(ref _isClosed, 1);

                try { UpdatePlayPauseIcon(); } catch { }
                try { PlaybackStateChanged?.Invoke(this, false); } catch { }
                try { PlayerClosed?.Invoke(this, EventArgs.Empty); } catch { }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MediaPlayer] Close error: {ex.Message}");
                // Ensure event still fires
                try { PlaybackStateChanged?.Invoke(this, false); } catch { }
                try { PlayerClosed?.Invoke(this, EventArgs.Empty); } catch { }
            }
            finally
            {
                Interlocked.Exchange(ref _isClosing, 0);
            }
        }

        public void Pause()
        {
            if (_mediaPlayer != null && _mediaPlayer.IsPlaying)
            {
                _wasStopped = false;
                _isPlaying = false;
                _mediaPlayer.Pause();
                UpdatePlayPauseIcon();
            }
        }

        public void Play()
        {
            if (_mediaPlayer == null) return;
            if (_mediaPlayer.IsPlaying) return;
            try
            {
                if (_wasStopped && _currentMedia != null)
                {
                    LoadMedia(_currentMedia);
                    return;
                }
                _wasStopped = false;
                _isPlaying = true;
                try { PlaybackOutputCoordinator.SetActive(this); } catch { }
                _mediaPlayer.Play();
                UpdatePlayPauseIcon();
            }
            catch
            {
            }
        }

        public void TogglePlayPause()
        {
            if (_mediaPlayer == null) return;
            if (_wasStopped && !_mediaPlayer.IsPlaying && _currentMedia != null)
            {
                LoadMedia(_currentMedia);
                return;
            }
            try
            {
                _wasStopped = false;
                if (_mediaPlayer.IsPlaying)
                {
                    _isPlaying = false;
                    _mediaPlayer.Pause();
                    UpdatePlayPauseIcon();
                    return;
                }
                _isPlaying = true;
                _mediaPlayer.Play();
                try { PlaybackOutputCoordinator.SetActive(this); } catch { }
                UpdatePlayPauseIcon();
            }
            catch
            {
            }
        }


        public void SetVolume(int volume)
        {
            if (_mediaPlayer == null) return;
            var clamped = Math.Max(0, Math.Min(100, volume));
            _mediaPlayer.Volume = clamped;
        }

        public void SetMuted(bool muted)
        {
            if (_isMuted == muted)
            {
                return;
            }

            ToggleMute();
        }

        public void ToggleFullscreenMode()
        {
            if (FullscreenRequested != null)
            {
                FullscreenRequested.Invoke(this, EventArgs.Empty);
                return;
            }

            var window = Window.GetWindow(this);
            if (window != null)
            {
                ToggleFullscreen(window);
            }
        }

        public void SeekToSeconds(double seconds)
        {
            if (_mediaPlayer == null)
                return;

            if (seconds < 0)
                return;

            var targetMs = (long)(seconds * 1000);
            var lengthMs = _mediaPlayer.Length;

            if (lengthMs > 0)
                targetMs = Math.Clamp(targetMs, 0, lengthMs);

            if (!_mediaPlayer.IsSeekable && lengthMs <= 0)
                return;

            var before = _mediaPlayer.Time;

            try
            {
                _mediaPlayer.Time = targetMs;

                if (lengthMs > 0 && Math.Abs(_mediaPlayer.Time - before) < 250)
                {
                    var pos = Math.Clamp((float)targetMs / lengthMs, 0f, 1f);
                    _mediaPlayer.Position = pos;
                }
            }
            catch
            {
            }
        }

        public double GetPlaybackSpeed()
        {
            try
            {
                return _mediaPlayer?.Rate ?? 1.0;
            }
            catch
            {
                return 1.0;
            }
        }

        public void SetPlaybackSpeed(double speed)
        {
            if (_mediaPlayer == null) return;

            try
            {
                var bounded = Math.Max(0.5, Math.Min(2.0, speed));
                _mediaPlayer.SetRate((float)bounded);
                ShowStatus($"Speed: {bounded:0.##}x");
            }
            catch
            {
            }
        }

        public IReadOnlyList<MediaTrackOption> GetSubtitleTrackOptions()
        {
            try
            {
                return GetTrackList(_mediaPlayer?.SpuDescription)
                    .Where(track => track.Id > 0)
                    .Select(track => new MediaTrackOption(track.Id, track.Name))
                    .ToList();
            }
            catch
            {
                return Array.Empty<MediaTrackOption>();
            }
        }

        public int? GetCurrentSubtitleTrackId()
        {
            try
            {
                if (_mediaPlayer == null) return null;
                return _mediaPlayer.Spu > 0 ? _mediaPlayer.Spu : null;
            }
            catch
            {
                return null;
            }
        }

        public bool AreSubtitlesEnabled()
        {
            try
            {
                return (_mediaPlayer?.Spu ?? -1) > 0;
            }
            catch
            {
                return false;
            }
        }

        public void SetSubtitleEnabled(bool enabled)
        {
            if (_mediaPlayer == null) return;

            try
            {
                if (!enabled)
                {
                    _mediaPlayer.SetSpu(-1);
                    ShowStatus("Subtitles: Disabled");
                    return;
                }

                var currentId = _mediaPlayer.Spu;
                if (currentId > 0)
                {
                    return;
                }

                var tracks = GetSubtitleTrackOptions();
                var preferred = tracks.FirstOrDefault(track => IsEnglishName(track.Name));
                var target = preferred ?? tracks.FirstOrDefault();
                if (target != null)
                {
                    _mediaPlayer.SetSpu(target.Id);
                    ShowStatus($"Subtitles: {target.Name}");
                }
            }
            catch
            {
            }
        }

        public void SetSubtitleTrack(int trackId)
        {
            if (_mediaPlayer == null) return;

            try
            {
                _mediaPlayer.SetSpu(trackId);
                var selected = GetSubtitleTrackOptions().FirstOrDefault(track => track.Id == trackId);
                ShowStatus($"Subtitles: {selected?.Name ?? trackId.ToString(CultureInfo.InvariantCulture)}");
            }
            catch
            {
            }
        }

        public IReadOnlyList<MediaTrackOption> GetAudioTrackOptions()
        {
            try
            {
                return GetTrackList(_mediaPlayer?.AudioTrackDescription)
                    .Where(track => track.Id > 0)
                    .Select(track => new MediaTrackOption(track.Id, track.Name))
                    .ToList();
            }
            catch
            {
                return Array.Empty<MediaTrackOption>();
            }
        }

        public int? GetCurrentAudioTrackId()
        {
            try
            {
                if (_mediaPlayer == null) return null;
                return _mediaPlayer.AudioTrack > 0 ? _mediaPlayer.AudioTrack : null;
            }
            catch
            {
                return null;
            }
        }

        public void SetAudioTrack(int trackId)
        {
            if (_mediaPlayer == null) return;

            try
            {
                _mediaPlayer.SetAudioTrack(trackId);
                var selected = GetAudioTrackOptions().FirstOrDefault(track => track.Id == trackId);
                ShowStatus($"Audio: {selected?.Name ?? trackId.ToString(CultureInfo.InvariantCulture)}");
            }
            catch
            {
            }
        }

        #endregion

        #region UI Mode Switching

        private void SetAudioMode()
        {
            _isVideoMode = false;
            VideoView.Visibility = Visibility.Collapsed;
            VideoView.Opacity = 0.0;
            try
            {
                if (PlayerGrid != null && PlayerGrid.RowDefinitions.Count > 1)
                    PlayerGrid.RowDefinitions[1].Height = new GridLength(360);
            }
            catch
            {
            }
        }

        private void SetVideoMode()
        {
            _isVideoMode = true;
            VideoView.Visibility = Visibility.Visible;
            VideoView.Opacity = 1.0;
            ForceVideoViewBlack();
            try
            {
                if (PlayerGrid != null && PlayerGrid.RowDefinitions.Count > 1)
                    PlayerGrid.RowDefinitions[1].Height = new GridLength(1, GridUnitType.Star);
            }
            catch
            {
            }
        }

        private void UpdateMediaMetadata(MediaItem mediaItem)
        {
            try
            {
                MediaTitleText.Text = mediaItem.DisplayName ?? "Unknown";
                
                var source = mediaItem.FilePath ?? "";
                if (Uri.TryCreate(source, UriKind.Absolute, out var uri) &&
                    (string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
                {
                    _ = Path.GetExtension(uri.AbsolutePath).ToUpperInvariant().TrimStart('.');
                    return;
                }

                var fileInfo = new FileInfo(source);
                _ = fileInfo.Length / (1024.0 * 1024.0);
                _ = Path.GetExtension(source).ToUpperInvariant().TrimStart('.');
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MediaPlayer] Metadata error: {ex.Message}");
            }
        }

        #endregion

        #region Playback Controls
        private DateTime _lastSkipUtc = DateTime.MinValue;
        private bool _skipBusy;

        private void PlayPauseButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isPlaying)
            {
                PauseMedia();
            }
            else
            {
                PlayMedia();
            }
        }

        private void PreviousButton_Click(object sender, RoutedEventArgs e)
        {
            var now = DateTime.UtcNow;
            if (_skipBusy) return;
            if ((now - _lastSkipUtc).TotalMilliseconds < 250) return;
            _skipBusy = true;
            try
            {
                if (_isVideoMode)
                {
                    var t = GetCurrentPlaybackSeconds();
                    SeekToSeconds(Math.Max(0, t - 10));
                    ShowControls();
                }
                else
                {
                    PlaybackService?.PlayPrevious();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MediaPlayer] Previous failed: {ex.Message}");
            }
            finally
            {
                _lastSkipUtc = DateTime.UtcNow;
                _skipBusy = false;
            }
        }

        private void NextButton_Click(object sender, RoutedEventArgs e)
        {
            var now = DateTime.UtcNow;
            if (_skipBusy) return;
            if ((now - _lastSkipUtc).TotalMilliseconds < 250) return;
            _skipBusy = true;
            try
            {
                if (_isVideoMode)
                {
                    var t = GetCurrentPlaybackSeconds();
                    SeekToSeconds(t + 10);
                    ShowControls();
                }
                else
                {
                    PlaybackService?.PlayNext();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MediaPlayer] Next failed: {ex.Message}");
            }
            finally
            {
                _lastSkipUtc = DateTime.UtcNow;
                _skipBusy = false;
            }
        }

        private void ShuffleButton_Click(object sender, RoutedEventArgs e)
        {
            if (PlaybackService != null)
            {
                PlaybackService.IsShuffled = !PlaybackService.IsShuffled;
                UpdateShuffleVisuals();
            }
        }

        private void RepeatButton_Click(object sender, RoutedEventArgs e)
        {
            if (PlaybackService != null)
            {
                PlaybackService.IsRepeat = !PlaybackService.IsRepeat;
                UpdateRepeatVisuals();
            }
        }

        private void MuteButton_Click(object sender, RoutedEventArgs e)
        {
            ToggleMute();
        }

        private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_mediaPlayer != null)
            {
                _mediaPlayer.Volume = (int)e.NewValue;
            }
            
            if (e.NewValue == 0 && !_isMuted)
            {
                _isMuted = true;
                UpdateMuteIcon();
            }
            else if (e.NewValue > 0 && _isMuted)
            {
                _isMuted = false;
                UpdateMuteIcon();
            }
        }

        private void ClosePlayer_Click(object sender, RoutedEventArgs e)
        {
            ClosePlayer();
        }
        
        private void FullscreenButton_Click(object sender, RoutedEventArgs e)
        {
            if (FullscreenRequested != null)
            {
                FullscreenRequested.Invoke(this, EventArgs.Empty);
                return;
            }

            var window = Window.GetWindow(this);
            if (window != null)
                ToggleFullscreen(window);
        }

        private void ToggleFullscreen(Window window)
        {
            if (!IsFullscreen)
            {
                if (!_hasSavedWindowState)
                {
                    _savedWindowStyle = window.WindowStyle;
                    _savedResizeMode = window.ResizeMode;
                    _savedWindowState = window.WindowState;
                    _savedTopmost = window.Topmost;
                    _hasSavedWindowState = true;
                }

                if (!window.AllowsTransparency)
                    window.WindowStyle = WindowStyle.None;
                window.ResizeMode = ResizeMode.NoResize;
                window.Topmost = true;
                window.WindowState = WindowState.Maximized;
                IsFullscreen = true;
                return;
            }

            if (_hasSavedWindowState)
            {
                window.Topmost = _savedTopmost;
                if (window.AllowsTransparency)
                    window.WindowStyle = WindowStyle.None;
                else
                    window.WindowStyle = _savedWindowStyle;
                window.ResizeMode = _savedResizeMode;
                window.WindowState = _savedWindowState;
            }
            else
            {
                window.Topmost = false;
                window.WindowStyle = window.AllowsTransparency ? WindowStyle.None : WindowStyle.SingleBorderWindow;
                window.ResizeMode = ResizeMode.CanResize;
                window.WindowState = WindowState.Normal;
            }

            IsFullscreen = false;
            Mouse.OverrideCursor = Cursors.Arrow;
            ShowControls();
        }

        private void UpdateFullscreenButton()
        {
        }

        private void ShowStatus(string message)
        {
            if (StatusText != null && StatusOverlay != null)
            {
                StatusText.Text = message;
                StatusOverlay.Visibility = Visibility.Visible;
                _statusTimer.Stop();
                _statusTimer.Start();
            }
        }

        public double GetCurrentPlaybackSeconds()
        {
            try
            {
                if (_mediaPlayer == null) return 0;
                var ms = _mediaPlayer.Time;
                if (ms < 0) return 0;
                return ms / 1000.0;
            }
            catch
            {
                return 0;
            }
        }

        public bool GetIsActuallyPlaying()
        {
            try
            {
                return _isPlaying;
            }
            catch
            {
                return false;
            }
        }

        public void RestorePlayback(double seconds, bool shouldPlay)
        {
            try
            {
                if (_mediaPlayer == null) return;
                if (seconds >= 0)
                    _mediaPlayer.Time = (long)(seconds * 1000);

                if (shouldPlay)
                {
                    _wasStopped = false;
                    _isPlaying = true;
                    _mediaPlayer.Play();
                }
                else
                {
                    if (_mediaPlayer.IsPlaying)
                        _mediaPlayer.Pause();
                    _isPlaying = false;
                }
                UpdatePlayPauseIcon();
            }
            catch
            {
            }
        }

        private static bool IsEnglishName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            if (name.IndexOf("english", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (Regex.IsMatch(name, @"\beng\b", RegexOptions.IgnoreCase)) return true;
            if (Regex.IsMatch(name, @"\ben\b", RegexOptions.IgnoreCase)) return true;
            return false;
        }

        private static List<(int Id, string Name)> GetTrackList(object? trackDescriptions)
        {
            var result = new List<(int Id, string Name)>();
            if (trackDescriptions is not Array arr) return result;

            foreach (var track in arr)
            {
                if (track == null) continue;
                try
                {
                    var type = track.GetType();
                    var idProp = type.GetProperty("Id");
                    var nameProp = type.GetProperty("Name");
                    if (idProp == null || nameProp == null) continue;

                    var idObj = idProp.GetValue(track);
                    if (idObj == null) continue;
                    var id = Convert.ToInt32(idObj);
                    var name = Convert.ToString(nameProp.GetValue(track)) ?? "";
                    result.Add((id, name));
                }
                catch
                {
                }
            }

            return result;
        }

        private void EnsureLanguageDefaultsTimer()
        {
            if (_languageDefaultsTimer != null) return;
            _languageDefaultsTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(250)
            };
            _languageDefaultsTimer.Tick += (_, _) =>
            {
                try
                {
                    if (_mediaPlayer == null) { _languageDefaultsTimer.Stop(); return; }
                    if (_currentMedia == null) { _languageDefaultsTimer.Stop(); return; }
                    if (_languageDefaultsForPath == null) { _languageDefaultsTimer.Stop(); return; }
                    if (!string.Equals(_currentMedia.FilePath, _languageDefaultsForPath, StringComparison.OrdinalIgnoreCase))
                    {
                        _languageDefaultsTimer.Stop();
                        return;
                    }

                    _languageDefaultsAttempts++;
                    if (_languageDefaultsAttempts > 24)
                    {
                        _languageDefaultsTimer.Stop();
                        return;
                    }

                    if (!_languageDefaultsAppliedAudio)
                        _languageDefaultsAppliedAudio = TryApplyEnglishAudioDefault();

                    if (!_languageDefaultsAppliedSubtitles)
                        _languageDefaultsAppliedSubtitles = TryApplyEnglishSubtitleDefault();

                    if (_languageDefaultsAppliedAudio && _languageDefaultsAppliedSubtitles)
                        _languageDefaultsTimer.Stop();
                }
                catch
                {
                }
            };
        }

        private void StartLanguageDefaultsForCurrentMedia()
        {
            try
            {
                if (_currentMedia == null) return;
                _languageDefaultsForPath = _currentMedia.FilePath;
                _languageDefaultsAttempts = 0;
                _languageDefaultsAppliedAudio = false;
                _languageDefaultsAppliedSubtitles = false;

                EnsureLanguageDefaultsTimer();
                _languageDefaultsTimer?.Stop();
                _languageDefaultsTimer?.Start();
            }
            catch
            {
            }
        }

        private bool TryApplyEnglishAudioDefault()
        {
            if (_mediaPlayer == null) return false;
            var tracks = GetTrackList(_mediaPlayer.AudioTrackDescription);
            if (tracks.Count == 0) return false;

            var currentId = _mediaPlayer.AudioTrack;
            var current = tracks.FirstOrDefault(t => t.Id == currentId);
            if (IsEnglishName(current.Name)) return true;

            var englishId = tracks
                .Where(t => t.Id > 0 && IsEnglishName(t.Name))
                .Select(t => t.Id)
                .FirstOrDefault();

            if (englishId > 0)
            {
                _mediaPlayer.SetAudioTrack(englishId);
                return true;
            }

            return true;
        }

        private bool TryApplyEnglishSubtitleDefault()
        {
            if (_mediaPlayer == null) return false;
            var tracks = GetTrackList(_mediaPlayer.SpuDescription);
            if (tracks.Count == 0) return false;

            var currentId = _mediaPlayer.Spu;
            if (currentId <= 0) return true;

            var current = tracks.FirstOrDefault(t => t.Id == currentId);
            if (IsEnglishName(current.Name)) return true;

            var englishId = tracks
                .Where(t => t.Id > 0 && IsEnglishName(t.Name))
                .Select(t => t.Id)
                .FirstOrDefault();

            if (englishId > 0)
            {
                _mediaPlayer.SetSpu(englishId);
                return true;
            }

            return true;
        }

        private void OpenMenuOnButton(Button button, ContextMenu menu)
        {
            try
            {
                button.ContextMenu = menu;
                menu.PlacementTarget = button;
                menu.IsOpen = true;
            }
            catch
            {
            }
        }

        private void ApplyMenuTheme(ContextMenu menu)
        {
            try
            {
                menu.Background = new SolidColorBrush(Color.FromRgb(0x05, 0x05, 0x08));
                menu.Foreground = Brushes.White;
                menu.BorderBrush = new SolidColorBrush(Color.FromRgb(0x22, 0xD3, 0xEE));
                menu.BorderThickness = new Thickness(1);
                menu.Padding = new Thickness(6);
                menu.Template = BuildContextMenuTemplate();

                var sepRoot = new FrameworkElementFactory(typeof(Border));
                sepRoot.SetValue(Border.HeightProperty, 1.0);
                sepRoot.SetValue(Border.BackgroundProperty, new SolidColorBrush(Color.FromArgb(0x50, 0x22, 0xD3, 0xEE)));
                var sepTemplate = new ControlTemplate(typeof(Separator)) { VisualTree = sepRoot };
                var sepStyle = new Style(typeof(Separator));
                sepStyle.Setters.Add(new Setter(Control.MarginProperty, new Thickness(6, 4, 6, 4)));
                sepStyle.Setters.Add(new Setter(Control.TemplateProperty, sepTemplate));
                menu.Resources[typeof(Separator)] = sepStyle;

                var menuItemStyle = new Style(typeof(MenuItem));
                menuItemStyle.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
                menuItemStyle.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
                menuItemStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(10, 8, 10, 8)));
                menuItemStyle.Setters.Add(new Setter(Control.MarginProperty, new Thickness(2, 1, 2, 1)));
                menuItemStyle.Setters.Add(new Setter(Control.FontSizeProperty, 12.0));
                menuItemStyle.Setters.Add(new Setter(Control.TemplateProperty, BuildMenuItemTemplate()));
                menu.Resources[typeof(MenuItem)] = menuItemStyle;
            }
            catch
            {
            }
        }

        private static ControlTemplate BuildContextMenuTemplate()
        {
            var bd = new FrameworkElementFactory(typeof(Border));
            bd.Name = "bd";
            bd.SetValue(Border.CornerRadiusProperty, new CornerRadius(12));
            bd.SetValue(Border.BackgroundProperty, new SolidColorBrush(Color.FromRgb(0x05, 0x05, 0x08)));
            bd.SetValue(Border.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(0x22, 0xD3, 0xEE)));
            bd.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            bd.SetValue(Border.PaddingProperty, new Thickness(6));

            var glow = new DropShadowEffect
            {
                Color = Color.FromRgb(0x22, 0xD3, 0xEE),
                BlurRadius = 28,
                ShadowDepth = 0,
                Opacity = 0.0
            };
            bd.SetValue(Border.EffectProperty, glow);

            var sv = new FrameworkElementFactory(typeof(ScrollViewer));
            sv.SetValue(ScrollViewer.PaddingProperty, new Thickness(0));
            sv.SetValue(ScrollViewer.CanContentScrollProperty, true);
            sv.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Hidden);
            sv.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Hidden);

            var ip = new FrameworkElementFactory(typeof(ItemsPresenter));
            ip.SetValue(FrameworkElement.MarginProperty, new Thickness(0));

            sv.AppendChild(ip);
            bd.AppendChild(sv);

            return new ControlTemplate(typeof(ContextMenu)) { VisualTree = bd };
        }

        private static ControlTemplate BuildMenuItemTemplate()
        {
            var root = new FrameworkElementFactory(typeof(Border));
            root.Name = "bd";
            root.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
            root.SetValue(Border.BackgroundProperty, Brushes.Transparent);

            var grid = new FrameworkElementFactory(typeof(Grid));
            grid.SetValue(FrameworkElement.MarginProperty, new Thickness(0));

            var cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(ContentPresenter.ContentSourceProperty, "Header");
            cp.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Left);
            cp.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            cp.SetValue(FrameworkElement.MarginProperty, new Thickness(2, 0, 2, 0));
            cp.SetValue(TextElement.ForegroundProperty, Brushes.White);
            cp.SetValue(TextElement.FontSizeProperty, 12.0);
            cp.SetValue(TextElement.FontFamilyProperty, new FontFamily("Segoe UI"));

            grid.AppendChild(cp);
            root.AppendChild(grid);

            var template = new ControlTemplate(typeof(MenuItem)) { VisualTree = root };

            var hoverTrigger = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hoverTrigger.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(Color.FromArgb(0x30, 0x22, 0xD3, 0xEE)), "bd"));
            template.Triggers.Add(hoverTrigger);

            var disabledTrigger = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
            disabledTrigger.Setters.Add(new Setter(Control.OpacityProperty, 0.55));
            template.Triggers.Add(disabledTrigger);

            return template;
        }

        private ContextMenu BuildSubtitlesMenu()
        {
            var menu = new ContextMenu();
            ApplyMenuTheme(menu);
            if (_mediaPlayer == null) return menu;

            var tracks = GetTrackList(_mediaPlayer.SpuDescription);
            var currentId = _mediaPlayer.Spu;

            var disable = new MenuItem { Header = "Disabled", IsCheckable = true, IsChecked = currentId <= 0 };
            disable.Click += (_, _) =>
            {
                try
                {
                    if (_mediaPlayer == null) return;
                    _mediaPlayer.SetSpu(-1);
                    ShowStatus("Subtitles: Disabled");
                }
                catch
                {
                }
            };
            menu.Items.Add(disable);
            menu.Items.Add(new Separator());

            foreach (var track in tracks
                         .Where(t => t.Id > 0)
                         .OrderByDescending(t => IsEnglishName(t.Name))
                         .ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase))
            {
                var item = new MenuItem
                {
                    Header = track.Name,
                    Tag = track.Id,
                    IsCheckable = true,
                    IsChecked = track.Id == currentId
                };
                item.Click += (_, _) =>
                {
                    try
                    {
                        if (_mediaPlayer == null) return;
                        var id = (int)item.Tag;
                        // Use SetSpu for LibVLC 3.x (Spu property is read-only)
                        _mediaPlayer.SetSpu(id);
                        ShowStatus($"Subtitles: {track.Name}");
                    }
                    catch
                    {
                    }
                };
                menu.Items.Add(item);
            }

            return menu;
        }

        private System.Windows.Controls.Primitives.Popup BuildSubtitlePanel(
            List<(int Id, string Name)> tracks,
            Button anchor)
        {
            var cyan = Color.FromRgb(0x22, 0xD3, 0xEE);
            var cyanBrush = new SolidColorBrush(cyan);
            var bgBrush = new SolidColorBrush(Color.FromArgb(0xF0, 0x0A, 0x0E, 0x17));
            var borderBrush = new SolidColorBrush(Color.FromArgb(0x60, 0x22, 0xD3, 0xEE));
            var subtleBorder = new SolidColorBrush(Color.FromArgb(0x25, 0xFF, 0xFF, 0xFF));
            var textBrush = Brushes.White;
            var dimBrush = new SolidColorBrush(Color.FromArgb(0xAA, 0xFF, 0xFF, 0xFF));
            var activeDot = new SolidColorBrush(Color.FromRgb(0x34, 0xD3, 0x99));

            var currentSpuId = _mediaPlayer?.Spu ?? -1;

            // Group tracks by language for the left column.
            // Parse language from names like "SDH - [English]", "Track 1 - [English]", "Closed captions 1"
            string ExtractLanguage(string name)
            {
                var open = name.LastIndexOf('[');
                var close = name.LastIndexOf(']');
                if (open >= 0 && close > open)
                    return name.Substring(open + 1, close - open - 1).Trim();
                if (name.StartsWith("Closed captions", StringComparison.OrdinalIgnoreCase))
                    return "Closed Captions";
                return name.Trim();
            }

            var languageGroups = tracks
                .GroupBy(t => ExtractLanguage(t.Name), StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(g => g.Any(t => IsEnglishName(t.Name) || string.Equals(g.Key, "English", StringComparison.OrdinalIgnoreCase)))
                .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                .ToList();

            // Determine initially selected language
            var currentTrack = tracks.FirstOrDefault(t => t.Id == currentSpuId);
            var selectedLang = currentSpuId > 0 && currentTrack.Id > 0 ? ExtractLanguage(currentTrack.Name) : null;

            // === Column 1: Languages ===
            var langPanel = new StackPanel { MinWidth = 150 };
            var langHeader = new TextBlock
            {
                Text = "Subtitles Languages",
                FontSize = 14, FontWeight = FontWeights.SemiBold,
                Foreground = textBrush, Margin = new Thickness(12, 10, 12, 8)
            };
            langPanel.Children.Add(langHeader);

            // "OFF" button
            var offBtn = new Button
            {
                Content = BuildTrackLabel("OFF", currentSpuId <= 0, activeDot, textBrush),
                Background = Brushes.Transparent, BorderThickness = new Thickness(0),
                Padding = new Thickness(12, 8, 12, 8), HorizontalContentAlignment = HorizontalAlignment.Left, Cursor = Cursors.Hand
            };

            // === Column 2: Variants (populated on language select) ===
            var variantPanel = new StackPanel { MinWidth = 180 };
            var variantHeader = new TextBlock
            {
                Text = "Subtitles Variants",
                FontSize = 14, FontWeight = FontWeights.SemiBold,
                Foreground = textBrush, Margin = new Thickness(12, 10, 12, 8)
            };
            variantPanel.Children.Add(variantHeader);

            void PopulateVariants(string language)
            {
                variantPanel.Children.Clear();
                variantPanel.Children.Add(variantHeader);

                var group = languageGroups.FirstOrDefault(g => string.Equals(g.Key, language, StringComparison.OrdinalIgnoreCase));
                if (group == null) return;

                foreach (var track in group)
                {
                    var isActive = track.Id == (_mediaPlayer?.Spu ?? -1);
                    // Determine variant label
                    var varLabel = track.Name;
                    if (varLabel.Contains(" - "))
                        varLabel = varLabel.Substring(0, varLabel.IndexOf(" - ", StringComparison.Ordinal)).Trim();
                    else if (varLabel.Contains("["))
                        varLabel = varLabel.Substring(0, varLabel.IndexOf("[", StringComparison.Ordinal)).Trim();
                    if (string.IsNullOrWhiteSpace(varLabel))
                        varLabel = track.Name;

                    var vBtn = new Button
                    {
                        Tag = track.Id,
                        Content = BuildTrackLabel(varLabel, isActive, activeDot, textBrush),
                        Background = Brushes.Transparent, BorderThickness = new Thickness(0),
                        Padding = new Thickness(12, 8, 12, 8), HorizontalContentAlignment = HorizontalAlignment.Left, Cursor = Cursors.Hand
                    };
                    vBtn.Click += (_, _) =>
                    {
                        try
                        {
                            _mediaPlayer?.SetSpu(track.Id);
                            ShowStatus($"Subtitles: {track.Name}");
                            if (_subtitlePopup != null) _subtitlePopup.IsOpen = false;
                        }
                        catch { }
                    };
                    variantPanel.Children.Add(vBtn);
                }
            }

            void RefreshLangButtons(string? activeLang)
            {
                // Rebuild language column with correct active states
                while (langPanel.Children.Count > 1)
                    langPanel.Children.RemoveAt(langPanel.Children.Count - 1);

                var newOffBtn = new Button
                {
                    Content = BuildTrackLabel("OFF", (_mediaPlayer?.Spu ?? -1) <= 0, activeDot, textBrush),
                    Background = Brushes.Transparent, BorderThickness = new Thickness(0),
                    Padding = new Thickness(12, 8, 12, 8), HorizontalContentAlignment = HorizontalAlignment.Left, Cursor = Cursors.Hand
                };
                newOffBtn.Click += (_, _) =>
                {
                    try
                    {
                        _mediaPlayer?.SetSpu(-1);
                        ShowStatus("Subtitles: Disabled");
                        if (_subtitlePopup != null) _subtitlePopup.IsOpen = false;
                    }
                    catch { }
                };
                langPanel.Children.Add(newOffBtn);

                foreach (var group in languageGroups)
                {
                    var lang = group.Key;
                    var isLangActive = string.Equals(lang, activeLang, StringComparison.OrdinalIgnoreCase);
                    var langBtn = new Button
                    {
                        Content = BuildTrackLabel(lang, isLangActive, activeDot, textBrush),
                        Background = Brushes.Transparent, BorderThickness = new Thickness(0),
                        Padding = new Thickness(12, 8, 12, 8), HorizontalContentAlignment = HorizontalAlignment.Left, Cursor = Cursors.Hand
                    };
                    langBtn.Click += (_, _) =>
                    {
                        PopulateVariants(lang);
                        RefreshLangButtons(lang);
                        // Auto-select first variant if none active from this language
                        var firstOfLang = group.FirstOrDefault();
                        if (firstOfLang.Id > 0 && !group.Any(t => t.Id == (_mediaPlayer?.Spu ?? -1)))
                        {
                            try
                            {
                                _mediaPlayer?.SetSpu(firstOfLang.Id);
                                ShowStatus($"Subtitles: {firstOfLang.Name}");
                            }
                            catch { }
                        }
                    };
                    langPanel.Children.Add(langBtn);
                }
            }

            RefreshLangButtons(selectedLang);
            if (selectedLang != null)
                PopulateVariants(selectedLang);

            // === Column 3: Settings ===
            var settingsPanel = new StackPanel { MinWidth = 200 };
            var settingsHeader = new TextBlock
            {
                Text = "Subtitles Settings",
                FontSize = 14, FontWeight = FontWeights.SemiBold,
                Foreground = textBrush, Margin = new Thickness(12, 10, 12, 8)
            };
            settingsPanel.Children.Add(settingsHeader);

            // Delay control
            settingsPanel.Children.Add(BuildSettingLabel("Delay", dimBrush));
            settingsPanel.Children.Add(BuildStepperRow(
                () => _subtitleDelayMs == 0 ? "--" : $"{_subtitleDelayMs:+#;-#;0}",
                () =>
                {
                    _subtitleDelayMs -= 250;
                    try { _mediaPlayer?.SetSpuDelay(_subtitleDelayMs * 1000); } catch { }
                },
                () =>
                {
                    _subtitleDelayMs += 250;
                    try { _mediaPlayer?.SetSpuDelay(_subtitleDelayMs * 1000); } catch { }
                },
                bgBrush, dimBrush, textBrush));

            // Size control
            settingsPanel.Children.Add(BuildSettingLabel("Size", dimBrush));
            settingsPanel.Children.Add(BuildStepperRow(
                () => $"{_subtitleSizePercent:0}%",
                () => { _subtitleSizePercent = Math.Max(50, _subtitleSizePercent - 10); },
                () => { _subtitleSizePercent = Math.Min(200, _subtitleSizePercent + 10); },
                bgBrush, dimBrush, textBrush));

            // Vertical Position control
            settingsPanel.Children.Add(BuildSettingLabel("Vertical Position", dimBrush));
            settingsPanel.Children.Add(BuildStepperRow(
                () => $"{_subtitleVerticalPercent:0}%",
                () => { _subtitleVerticalPercent = Math.Max(0, _subtitleVerticalPercent - 5); },
                () => { _subtitleVerticalPercent = Math.Min(50, _subtitleVerticalPercent + 5); },
                bgBrush, dimBrush, textBrush));

            // Assemble 3-column layout
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Pixel) }); // separator
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Pixel) }); // separator
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            Grid.SetColumn(langPanel, 0);
            grid.Children.Add(langPanel);

            var sep1 = new Border { Width = 1, Background = subtleBorder, Margin = new Thickness(0, 8, 0, 8) };
            Grid.SetColumn(sep1, 1);
            grid.Children.Add(sep1);

            Grid.SetColumn(variantPanel, 2);
            grid.Children.Add(variantPanel);

            var sep2 = new Border { Width = 1, Background = subtleBorder, Margin = new Thickness(0, 8, 0, 8) };
            Grid.SetColumn(sep2, 3);
            grid.Children.Add(sep2);

            Grid.SetColumn(settingsPanel, 4);
            grid.Children.Add(settingsPanel);

            var outerBorder = new Border
            {
                Background = bgBrush,
                BorderBrush = borderBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(4),
                Child = grid,
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    BlurRadius = 24, ShadowDepth = 0, Opacity = 0.6,
                    Color = Color.FromRgb(0, 0, 0)
                }
            };

            var popup = new System.Windows.Controls.Primitives.Popup
            {
                Child = outerBorder,
                PlacementTarget = anchor,
                Placement = System.Windows.Controls.Primitives.PlacementMode.Top,
                StaysOpen = false,
                AllowsTransparency = true,
                VerticalOffset = -8
            };

            return popup;
        }

        private static StackPanel BuildTrackLabel(string text, bool isActive, Brush activeDot, Brush textBrush)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(new TextBlock
            {
                Text = text, Foreground = textBrush, FontSize = 13, VerticalAlignment = VerticalAlignment.Center
            });
            if (isActive)
            {
                sp.Children.Add(new System.Windows.Shapes.Ellipse
                {
                    Width = 8, Height = 8, Fill = activeDot,
                    Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center
                });
            }
            return sp;
        }

        private static TextBlock BuildSettingLabel(string text, Brush foreground)
        {
            return new TextBlock
            {
                Text = text, Foreground = foreground, FontSize = 12,
                Margin = new Thickness(12, 10, 12, 2)
            };
        }

        private static Grid BuildStepperRow(Func<string> getDisplayValue, Action onMinus, Action onPlus,
            Brush bgBrush, Brush dimBrush, Brush textBrush)
        {
            var row = new Grid { Margin = new Thickness(12, 4, 12, 4) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var valueLabel = new TextBlock
            {
                Text = getDisplayValue(), Foreground = textBrush, FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
            };

            var btnBg = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF));

            var minusBtn = new Button
            {
                Content = new TextBlock { Text = "−", FontSize = 16, Foreground = textBrush, HorizontalAlignment = HorizontalAlignment.Center },
                Width = 36, Height = 36,
                Background = btnBg, BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand, Padding = new Thickness(0)
            };
            minusBtn.Click += (_, _) => { onMinus(); valueLabel.Text = getDisplayValue(); };

            var plusBtn = new Button
            {
                Content = new TextBlock { Text = "+", FontSize = 16, Foreground = textBrush, HorizontalAlignment = HorizontalAlignment.Center },
                Width = 36, Height = 36,
                Background = btnBg, BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand, Padding = new Thickness(0)
            };
            plusBtn.Click += (_, _) => { onPlus(); valueLabel.Text = getDisplayValue(); };

            Grid.SetColumn(minusBtn, 0);
            Grid.SetColumn(valueLabel, 1);
            Grid.SetColumn(plusBtn, 2);
            row.Children.Add(minusBtn);
            row.Children.Add(valueLabel);
            row.Children.Add(plusBtn);

            return row;
        }

        private ContextMenu BuildAudioTracksMenu()
        {
            var menu = new ContextMenu();
            ApplyMenuTheme(menu);
            if (_mediaPlayer == null) return menu;

            var tracks = GetTrackList(_mediaPlayer.AudioTrackDescription);
            var currentId = _mediaPlayer.AudioTrack;

            foreach (var track in tracks
                         .Where(t => t.Id > 0)
                         .OrderByDescending(t => IsEnglishName(t.Name))
                         .ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase))
            {
                var item = new MenuItem
                {
                    Header = track.Name,
                    Tag = track.Id,
                    IsCheckable = true,
                    IsChecked = track.Id == currentId
                };
                item.Click += (_, _) =>
                {
                    try
                    {
                        if (_mediaPlayer == null) return;
                        var id = (int)item.Tag;
                        // Use SetAudioTrack for LibVLC 3.x (AudioTrack property is read-only)
                        _mediaPlayer.SetAudioTrack(id);
                        ShowStatus($"Audio: {track.Name}");
                    }
                    catch
                    {
                    }
                };
                menu.Items.Add(item);
            }

            return menu;
        }

        private ContextMenu BuildAspectRatioMenu()
        {
            var menu = new ContextMenu();
            ApplyMenuTheme(menu);
            if (_mediaPlayer == null) return menu;

            if (!_isVideoMode)
            {
                menu.Items.Add(new MenuItem { Header = "Video only", IsEnabled = false });
                return menu;
            }

            var options = new (string Header, string? Ratio)[]
            {
                ("Default", null),
                ("16:9", "16:9"),
                ("4:3", "4:3"),
                ("2.35:1", "2.35:1"),
                ("1:1", "1:1")
            };

            var current = _mediaPlayer.AspectRatio;

            foreach (var opt in options)
            {
                var isChecked = string.Equals(current, opt.Ratio, StringComparison.OrdinalIgnoreCase) ||
                                (current == null && opt.Ratio == null);
                var item = new MenuItem { Header = opt.Header, Tag = opt.Ratio, IsCheckable = true, IsChecked = isChecked };
                item.Click += (_, _) =>
                {
                    try
                    {
                        if (_mediaPlayer == null) return;
                        // Set Scale=0 FIRST (fit-to-window), then AspectRatio.
                        // Setting Scale AFTER AspectRatio resets the ratio in VLC 3.x.
                        _mediaPlayer.Scale = 0;
                        _mediaPlayer.AspectRatio = (string?)item.Tag;
                        ShowStatus($"Aspect Ratio: {opt.Header}");
                    }
                    catch
                    {
                    }
                };
                menu.Items.Add(item);
            }

            return menu;
        }

        private void SubtitleButton_Click(object sender, RoutedEventArgs e)
        {
            if (_mediaPlayer == null) return;
            if (sender is not Button button) return;

            var tracks = GetTrackList(_mediaPlayer.SpuDescription).Where(t => t.Id > 0).ToList();
            if (tracks.Count == 0)
            {
                ShowStatus("Subtitles: None available");
                return;
            }

            // Close an existing popup before building a new one
            if (_subtitlePopup != null)
            {
                _subtitlePopup.IsOpen = false;
                _subtitlePopup = null;
            }

            _subtitlePopup = BuildSubtitlePanel(tracks, button);
            _subtitlePopup.IsOpen = true;
        }

        private void AudioTrackButton_Click(object sender, RoutedEventArgs e)
        {
            if (_mediaPlayer == null) return;
            if (sender is not Button button) return;

            var menu = BuildAudioTracksMenu();
            if (menu.Items.Count == 0)
            {
                ShowStatus("Audio: Default");
                return;
            }
            OpenMenuOnButton(button, menu);
        }

        private string[] _aspectRatios = { null, "16:9", "4:3", "2.35:1", "1:1" };
        private int _currentAspectRatioIndex = 0;

        private void AspectRatioButton_Click(object sender, RoutedEventArgs e)
        {
            if (_mediaPlayer == null) return;
            if (sender is not Button button) return;
            OpenMenuOnButton(button, BuildAspectRatioMenu());
        }

        #endregion

        #region UI Interaction (Auto-Hide & Click)

        private void MousePollingTimer_Tick(object? sender, EventArgs e)
        {
            try
            {
                // Get global mouse position to detect movement even over WinForms host
                if (GetCursorPos(out POINT p))
                {
                    if (!IsCursorOverPlayerBounds(p))
                        return;

                    Point currentPos = p;
                    // Small threshold — main detection path when VLC HWND eats WPF mouse events
                    if (Math.Abs(currentPos.X - _lastMousePosition.X) > 5 || 
                        Math.Abs(currentPos.Y - _lastMousePosition.Y) > 5)
                    {
                        _lastMousePosition = currentPos;
                        ShowControls();
                    }
                }
            }
            catch
            {
                // Ignore errors (e.g. if window is closed/minimized)
            }
        }

        private bool IsCursorOverPlayerBounds(POINT cursor)
        {
            try
            {
                if (!IsLoaded)
                    return false;

                var topLeft = PointToScreen(new Point(0, 0));
                var bottomRight = PointToScreen(new Point(ActualWidth, ActualHeight));

                var minX = Math.Min(topLeft.X, bottomRight.X);
                var maxX = Math.Max(topLeft.X, bottomRight.X);
                var minY = Math.Min(topLeft.Y, bottomRight.Y);
                var maxY = Math.Max(topLeft.Y, bottomRight.Y);

                return cursor.X >= minX && cursor.X <= maxX && cursor.Y >= minY && cursor.Y <= maxY;
            }
            catch
            {
                return false;
            }
        }

        private void Grid_MouseMove(object sender, MouseEventArgs e)
        {
            // Restore mouse move detection for WPF controls
            // This ensures that if the mouse is over the bars, they stay visible
            ShowControls();
        }

        private void PlayerGrid_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;

            // Toggle play/pause
            if (_isPlaying) PauseMedia(); else PlayMedia();
            
            ShowControls();
            
            if (e.ClickCount == 2)
            {
                FullscreenButton_Click(sender, e);
            }
        }

        private void ControlsTimer_Tick(object? sender, EventArgs e)
        {
            if (!_isPlaying) 
             {
                 System.Diagnostics.Debug.WriteLine("[MediaPlayer] Auto-hide skipped: Not playing");
                 return;
             }
             if (!_isVideoMode) 
             {
                 System.Diagnostics.Debug.WriteLine("[MediaPlayer] Auto-hide skipped: Not video mode");
                 return;
             }
             
             // Debug logging to find why it's not hiding
             System.Diagnostics.Debug.WriteLine("[MediaPlayer] Auto-hide timer tick - attempting to hide");
             HideControls();
        }

        private void HideControls()
        {
            if (_areControlsHidden) return;
            
            // Double check we are in video mode
            if (!_isVideoMode) return;
            
            // Don't hide if paused
            if (!_isPlaying) return;

            // Don't hide if mouse is over controls
            if (TopControlBar != null && TopControlBar.IsMouseOver) 
            {
                System.Diagnostics.Debug.WriteLine("[MediaPlayer] Hide skipped: Mouse over TopControlBar");
                return;
            }
            if (ControlBar != null && ControlBar.IsMouseOver) 
            {
                System.Diagnostics.Debug.WriteLine("[MediaPlayer] Hide skipped: Mouse over ControlBar");
                return;
            }
            
            System.Diagnostics.Debug.WriteLine("[MediaPlayer] Hiding controls now");

            if (ControlBar != null)
            {
                ControlBar.Visibility = Visibility.Collapsed;
            }

            if (TopControlBar != null)
            {
                // Use Hidden, not Collapsed: Row 0 is Height="Auto" so Collapsed shrinks it to
                // 0px, which lets the LibVLC HWND start at y=0 and steal all mouse input from
                // the external ControlsOverlay buttons (same reason UseExternalControls uses Hidden).
                TopControlBar.Visibility = Visibility.Hidden;
            }
            
            if (IsFullscreen)
            {
                Mouse.OverrideCursor = Cursors.None;
            }
            
            _areControlsHidden = true;
            _controlsTimer.Stop();
        }

        private void ShowControls()
        {
            // Notify parent that user is active (to show header/sidebar if needed)
            // System.Diagnostics.Debug.WriteLine("[MediaPlayer] User Activity Detected - Showing Controls");
            UserActivityDetected?.Invoke(this, EventArgs.Empty);

            if (_areControlsHidden)
            {
                // System.Diagnostics.Debug.WriteLine("[MediaPlayer] Showing controls");
                _areControlsHidden = false;
                
                // TopControlBar is suppressed when an external overlay provides top chrome
                if (TopControlBar != null && !_useExternalControls) { TopControlBar.Visibility = Visibility.Visible; TopControlBar.Opacity = 1.0; }
                if (ControlBar != null) { ControlBar.Visibility = Visibility.Visible; ControlBar.Opacity = 1.0; }
                
                if (IsFullscreen)
                {
                    Mouse.OverrideCursor = Cursors.Arrow;
                }
            }
            
            if (_isPlaying && _isVideoMode)
            {
                _controlsTimer?.Stop();
                _controlsTimer?.Start();
            }
            else
            {
                _controlsTimer?.Stop();
            }
        }

        #endregion

        #region Playback Methods

        private void PlayMedia()
        {
            if (_mediaPlayer != null)
            {
                try
                {
                    if (_wasStopped && _currentMedia != null)
                    {
                        LoadMedia(_currentMedia);
                        return;
                    }
                    _wasStopped = false;
                    _mediaPlayer.Play();
                    _isPlaying = true;
                    UpdatePlayPauseIcon();
                }
                catch
                {
                }
            }
        }

        private void PauseMedia()
        {
            if (_mediaPlayer != null)
            {
                try
                {
                    _wasStopped = false;
                    if (_mediaPlayer.IsPlaying)
                        _mediaPlayer.Pause();
                    _isPlaying = false;
                    UpdatePlayPauseIcon();
                    ShowControls();
                }
                catch
                {
                }
            }
        }

        private void StopButton_Click(object sender, RoutedEventArgs e)
        {
            try { Stop(); } catch { }
        }

        #endregion

        #region Progress Handling

        private void ProgressSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isDraggingSlider)
            {
                var time = TimeSpan.FromSeconds(e.NewValue);
                CurrentTimeText.Text = FormatTime(time);
            }
        }

        private void ProgressSlider_DragStarted(object sender, System.Windows.Controls.Primitives.DragStartedEventArgs e)
        {
            _isDraggingSlider = true;
            _controlsTimer.Stop();
        }

        private void ProgressSlider_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
        {
            _isDraggingSlider = false;
            if (_mediaPlayer != null)
            {
                _mediaPlayer.Time = (long)(ProgressSlider.Value * 1000);
            }
            
            if (_isPlaying) { _controlsTimer?.Start(); }
        }

        private void ProgressSlider_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _isDraggingSlider = true;
            _controlsTimer?.Stop(); // Keep controls visible while dragging
        }

        private void ProgressSlider_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_mediaPlayer != null)
            {
                var time = (long)(ProgressSlider.Value * 1000); // Slider is in seconds
                _mediaPlayer.Time = time;
            }
            
            _isDraggingSlider = false;
            
            if (_isPlaying) { _controlsTimer?.Start(); }
        }

        private string FormatTime(TimeSpan time)
        {
            if (time.TotalHours >= 1)
                return time.ToString(@"h\:mm\:ss");
            return time.ToString(@"m\:ss");
        }

        #endregion

        #region Helpers

        /// <summary>
        /// Recursively walks the VisualTree of the VideoView to find the internal
        /// WindowsFormsHost and its WinForms Panel, then forces everything to black.
        /// LogicalTreeHelper does NOT find LibVLCSharp's internal WFH — must use VisualTree.
        /// </summary>
        private void ForceVideoViewBlack()
        {
            try
            {
                VideoView.Background = System.Windows.Media.Brushes.Black;
                ForceBlackRecursive(VideoView);
            }
            catch { }
        }

        private void VideoView_Loaded(object sender, RoutedEventArgs e)
        {
            ForceVideoViewBlack();
        }

        private static void ForceBlackRecursive(DependencyObject parent)
        {
            if (parent == null) return;
            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is System.Windows.Forms.Integration.WindowsFormsHost wfh)
                {
                    wfh.Background = System.Windows.Media.Brushes.Black;
                    if (wfh.Child != null)
                    {
                        wfh.Child.BackColor = System.Drawing.Color.Black;
                        // Also blacken every child control inside the WinForms Panel
                        foreach (System.Windows.Forms.Control ctrl in wfh.Child.Controls)
                        {
                            ctrl.BackColor = System.Drawing.Color.Black;
                        }
                    }
                }
                else if (child is System.Windows.Controls.Panel panel)
                {
                    panel.Background = System.Windows.Media.Brushes.Black;
                }
                else if (child is System.Windows.Controls.Border border)
                {
                    border.Background = System.Windows.Media.Brushes.Black;
                }
                ForceBlackRecursive(child);
            }
        }

        /// <summary>
        /// Windows remembers per-app mixer volumes. If a previous session set it to 1,
        /// it stays at 1 forever. This uses NAudio CoreAudio API to find all audio sessions
        /// belonging to our process and set them to full volume (1.0f).
        /// </summary>
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
                        if (currentVol < 0.5f) // Only fix if it's abnormally low
                        {
                            session.SimpleAudioVolume.Volume = 1.0f;
                            session.SimpleAudioVolume.Mute = false;
                            System.Diagnostics.Debug.WriteLine($"[MediaPlayer] Fixed Windows mixer: {currentVol:F2} -> 1.0");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MediaPlayer] ResetWindowsMixerVolume error: {ex.Message}");
            }
        }

        private void UpdatePlayPauseIcon()
        {
            if (PlayIconImage != null && PauseIconImage != null)
            {
                PlayIconImage.Visibility = _isPlaying ? Visibility.Collapsed : Visibility.Visible;
                PauseIconImage.Visibility = _isPlaying ? Visibility.Visible : Visibility.Collapsed;
                return;
            }
        }

        private void ToggleMute()
        {
            if (_isMuted)
            {
                _isMuted = false;
                VolumeSlider.Value = _volumeBeforeMute;
            }
            else
            {
                _isMuted = true;
                _volumeBeforeMute = VolumeSlider.Value;
                VolumeSlider.Value = 0;
            }
            UpdateMuteIcon();
        }

        private void UpdateMuteIcon()
        {
            if (MuteIconImage != null && VolumeIconImage != null)
            {
                MuteIconImage.Visibility = _isMuted ? Visibility.Visible : Visibility.Collapsed;
                VolumeIconImage.Visibility = _isMuted ? Visibility.Collapsed : Visibility.Visible;
                return;
            }
        }

        private void ShowError(string message)
        {
            if (ErrorOverlay != null)
            {
                ErrorOverlay.Visibility = Visibility.Visible;
                if (ErrorMessage != null)
                    ErrorMessage.Text = message;
                if (ErrorDetails != null)
                    ErrorDetails.Text = BuildErrorDetails();
            }
        }

        private string BuildErrorDetails()
        {
            try
            {
                var src = MaskSensitive((_lastPlaybackSource ?? "").Trim());
                var core = (_lastCoreInitDir ?? "").Trim();
                var tail = "";
                lock (_vlcLogSync)
                {
                    tail = MaskSensitive((_vlcLogTail ?? "").Trim());
                }

                var details = "";
                if (!string.IsNullOrWhiteSpace(src))
                    details += $"Source: {src}";
                if (!string.IsNullOrWhiteSpace(core))
                    details += (details.Length == 0 ? "" : "\n") + $"LibVLC: {core}";
                if (!string.IsNullOrWhiteSpace(tail))
                    details += (details.Length == 0 ? "" : "\n\n") + tail;
                return details;
            }
            catch
            {
                return "";
            }
        }

        private static string MaskSensitive(string text)
        {
            try
            {
                var s = text ?? "";
                if (string.IsNullOrWhiteSpace(s)) return "";

                s = System.Text.RegularExpressions.Regex.Replace(s, @"(?i)(auth_token=)[^&\s]+", "$1***");

                s = System.Text.RegularExpressions.Regex.Replace(
                    s,
                    @"(?i)(/realdebrid/)([^/\s]+)",
                    m => m.Groups[1].Value + "***");

                s = System.Text.RegularExpressions.Regex.Replace(
                    s,
                    @"(?i)(/alldebrid/)([^/\s]+)",
                    m => m.Groups[1].Value + "***");

                s = System.Text.RegularExpressions.Regex.Replace(
                    s,
                    @"(?i)(/premiumize/)([^/\s]+)",
                    m => m.Groups[1].Value + "***");

                return s;
            }
            catch
            {
                return (text ?? "");
            }
        }

        private void CopyErrorButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var clip = (ErrorMessage?.Text ?? "").Trim();
                var det = (ErrorDetails?.Text ?? "").Trim();
                var all = string.IsNullOrWhiteSpace(det) ? clip : (clip + "\n\n" + det);
                if (!string.IsNullOrWhiteSpace(all))
                    Clipboard.SetText(all);
            }
            catch
            {
            }
        }
        
        private void RetryButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentMedia != null)
            {
                LoadMedia(_currentMedia);
            }
        }

        private void CloseErrorButton_Click(object sender, RoutedEventArgs e)
        {
            ErrorOverlay.Visibility = Visibility.Collapsed;
            ClosePlayer();
        }

        private void MinimizePlayerButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                PlayerMinimized?.Invoke(this, EventArgs.Empty);
            }
            catch
            {
            }
        }

        private void ClosePlayerButton_Click(object sender, RoutedEventArgs e)
        {
            ClosePlayer();
        }
        
        private void MediaPlayerControl_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Space)
            {
                if (_isPlaying) PauseMedia(); else PlayMedia();
                ShowControls();
                e.Handled = true;
            }
            else if (e.Key == Key.Left)
            {
                try
                {
                    var now = GetCurrentPlaybackSeconds();
                    SeekToSeconds(Math.Max(0, now - 5));
                    ShowControls();
                    e.Handled = true;
                }
                catch
                {
                }
            }
            else if (e.Key == Key.Right)
            {
                try
                {
                    var now = GetCurrentPlaybackSeconds();
                    SeekToSeconds(now + 5);
                    ShowControls();
                    e.Handled = true;
                }
                catch
                {
                }
            }
            else if (e.Key == Key.F11)
            {
                if (FullscreenRequested != null)
                {
                    FullscreenRequested.Invoke(this, EventArgs.Empty);
                    e.Handled = true;
                    return;
                }

                var window = Window.GetWindow(this);
                if (window != null)
                    ToggleFullscreen(window);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                if (IsFullscreen)
                {
                    if (FullscreenRequested != null)
                    {
                        FullscreenRequested.Invoke(this, EventArgs.Empty);
                    }
                    else
                    {
                        var window = Window.GetWindow(this);
                        if (window != null)
                            ToggleFullscreen(window);
                    }
                }
                else
                {
                    ClosePlayer();
                }
                e.Handled = true;
            }
        }

        #region Lyrics Support

        private List<AtlasAI.MediaScanner.LyricLine> _currentLyrics = new List<AtlasAI.MediaScanner.LyricLine>();
        private int _lastLyricIndex = -1;
        private bool _isLyricsVisible = false;

        private async void LoadLyricsForCurrentMedia()
        {
            if (_currentMedia == null) return;
            
            // Clear current lyrics
            _currentLyrics.Clear();
            _lastLyricIndex = -1;
            if (LyricsItemsControl != null)
                LyricsItemsControl.ItemsSource = null;

            if (!_isLyricsVisible) return;

            try
            {
                ShowStatus("Loading lyrics...");
                var lyrics = await AtlasAI.MediaScanner.LyricsService.Instance.GetLyricsAsync(_currentMedia);
                
                if (lyrics != null && lyrics.Count > 0)
                {
                    _currentLyrics = lyrics;
                    if (LyricsItemsControl != null)
                        LyricsItemsControl.ItemsSource = _currentLyrics;
                    ShowStatus("Lyrics loaded");
                    
                    // Initial sync
                    if (_mediaPlayer != null)
                        UpdateLyricsSync(TimeSpan.FromMilliseconds(_mediaPlayer.Time));
                }
                else
                {
                    ShowStatus("No lyrics found");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Lyrics] Error: {ex.Message}");
            }
        }

        private void LyricsButton_Click(object sender, RoutedEventArgs e)
        {
            _isLyricsVisible = !_isLyricsVisible;
            if (LyricsOverlay != null)
                LyricsOverlay.Visibility = _isLyricsVisible ? Visibility.Visible : Visibility.Collapsed;
            
            if (_isLyricsVisible)
            {
                LoadLyricsForCurrentMedia();
            }
        }

        private void UpdateLyricsSync(TimeSpan time)
        {
            if (!_isLyricsVisible || _currentLyrics.Count == 0) return;
            if (LyricsItemsControl == null) return;

            // Find current line
            // The current line is the one with the largest timestamp <= current time
            int index = -1;
            for (int i = 0; i < _currentLyrics.Count; i++)
            {
                if (_currentLyrics[i].Timestamp <= time)
                    index = i;
                else
                    break;
            }

            if (index != _lastLyricIndex)
            {
                // Unhighlight old
                if (_lastLyricIndex >= 0 && _lastLyricIndex < _currentLyrics.Count)
                    _currentLyrics[_lastLyricIndex].IsCurrent = false;

                _lastLyricIndex = index;

                // Highlight new
                if (_lastLyricIndex >= 0 && _lastLyricIndex < _currentLyrics.Count)
                    _currentLyrics[_lastLyricIndex].IsCurrent = true;
                
                // Auto-scroll
                if (index >= 0)
                {
                    try
                    {
                        // Wait for layout update if needed, but for now try direct
                        // Ideally we use ItemContainerGenerator but it might be null if virtualized (ItemsControl is not virtualized by default)
                        // ItemsControl doesn't support BringIntoView on items directly easily without ScrollViewer logic
                        
                        // Simple approach: calculate offset? No, height varies.
                        // Better: Get container.
                        var container = LyricsItemsControl.ItemContainerGenerator.ContainerFromIndex(index) as FrameworkElement;
                        if (container != null)
                        {
                            container.BringIntoView();
                        }
                    }
                    catch { }
                }
            }
        }

        #endregion

        #endregion
    }
}
