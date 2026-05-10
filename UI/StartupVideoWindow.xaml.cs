using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using System.Windows.Media;
using System.Windows.Threading;
using AtlasAI.Core;
using LibVLCSharp.Shared;
using LibVLCSharp.WPF;
using Unosquare.FFME.Common;

namespace AtlasAI.UI
{
    public partial class StartupVideoWindow : Window
    {
        private static readonly Duration FadeOutDuration = TimeSpan.FromMilliseconds(220);
        private static readonly TimeSpan PlaybackStartTimeout = TimeSpan.FromSeconds(5);
        private Action? _onComplete;
        private bool _completed;
        private bool _completionFinalized;
        private bool _wmfFallbackStarted;
        private bool _vlcTried;
        private bool _ffmeTried;
        private bool _playbackStarted;
        private MediaElement? _wmfPlayer;
        private Unosquare.FFME.MediaElement? _ffmePlayer;
        private LibVLC? _vlc;
        private LibVLCSharp.Shared.MediaPlayer? _vlcPlayer;
        private LibVLCSharp.Shared.Media? _vlcMedia;
        private VideoView? _vlcView;
        private DispatcherTimer? _safetyTimer;
        private string _videoPath = string.Empty;

        public StartupVideoWindow()
        {
            InitializeComponent();
            Loaded += StartupVideoWindow_Loaded;
        }

        public void PlayVideo(string videoPath, Action onComplete)
        {
            _onComplete = onComplete;
            _videoPath = videoPath ?? string.Empty;

            try
            {
                if (!File.Exists(videoPath))
                {
                    CompleteAndClose();
                    return;
                }

                StartSafetyTimer();

                // VLC first — WMF MediaElement silently fails to fire MediaOpened on many
                // setups even with standard H.264. VLC consistently works.
                if (!TryStartVlc(videoPath) && !TryStartWmf(videoPath) && !TryStartFfme(videoPath))
                {
                    CompleteAndClose();
                }
            }
            catch
            {
                CompleteAndClose();
            }
        }

        private void StartupVideoWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                WindowState = WindowState.Maximized;
                Topmost = true;
            }
            catch
            {
            }
        }

        private void VideoPlayer_MediaEnded(object? sender, RoutedEventArgs e)
        {
            CompleteAndClose();
        }

        private void VideoPlayer_MediaFailed(object? sender, ExceptionRoutedEventArgs e)
        {
            try { AppLogger.LogWarning($"[StartupVideo] WMF playback failed: {e.ErrorException?.Message}"); } catch { }
            CleanupWmfPlayer();
            if (!string.IsNullOrWhiteSpace(_videoPath) && TryNextFallbackPlayer())
                return;

            CompleteAndClose();
        }

        private void SkipButton_Click(object sender, RoutedEventArgs e)
        {
            CompleteAndClose();
        }

        private void CompleteAndClose()
        {
            if (_completed)
                return;

            _completed = true;
            IsHitTestVisible = false;

            try
            {
                SkipButton.Visibility = Visibility.Collapsed;
            }
            catch
            {
            }

            try
            {
                VideoHost.Visibility = Visibility.Collapsed;
            }
            catch
            {
            }

            CleanupPlayers();

            try
            {
                var fadeOut = new DoubleAnimation
                {
                    From = Opacity,
                    To = 0,
                    Duration = FadeOutDuration
                };
                fadeOut.Completed += (_, _) => FinalizeCompletion();
                BeginAnimation(OpacityProperty, fadeOut);
                return;
            }
            catch
            {
            }

            FinalizeCompletion();
        }

        private void FinalizeCompletion()
        {
            if (_completionFinalized)
                return;

            _completionFinalized = true;

            try
            {
                BeginAnimation(OpacityProperty, null);
            }
            catch
            {
            }

            try
            {
                Opacity = 0;
            }
            catch
            {
            }

            try
            {
                CleanupPlayers();
            }
            catch
            {
            }

            try
            {
                Close();
            }
            catch
            {
                InvokeCompletion();
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);

            try
            {
                CleanupPlayers();
            }
            catch
            {
            }

            InvokeCompletion();
        }

        private bool TryStartFfme(string videoPath)
        {
            _ffmeTried = true;
            try
            {
                // Set FFmpeg path before use - FFME won't find it automatically at startup.
                try
                {
                    var ffmpegDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg");
                    if (Directory.Exists(ffmpegDir))
                        Unosquare.FFME.Library.FFmpegDirectory = ffmpegDir;
                    Unosquare.FFME.Library.EnableWpfMultiThreadedVideo = true;
                    Unosquare.FFME.Library.LoadFFmpeg();
                }
                catch { }

                var ff = new Unosquare.FFME.MediaElement
                {
                    LoadedBehavior = MediaPlaybackState.Manual,
                    UnloadedBehavior = MediaPlaybackState.Close,
                    Stretch = Stretch.Uniform,
                    Volume = 1.0,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Stretch,
                    IsHitTestVisible = false,
                };

                ff.MediaOpened += FfmePlayer_MediaOpened;
                ff.MediaEnded += FfmePlayer_MediaEnded;
                ff.MediaFailed += FfmePlayer_MediaFailed;

                VideoHost.Children.Clear();
                VideoHost.Children.Add(ff);
                _ffmePlayer = ff;
                try { AppLogger.LogInfo($"[StartupVideo] Starting FFME intro playback: {videoPath}"); } catch { }
                ff.Open(new Uri(videoPath, UriKind.Absolute));
                return true;
            }
            catch (Exception ex)
            {
                try { AppLogger.LogWarning($"[StartupVideo] FFME initialization failed: {ex.Message}"); } catch { }
                CleanupFfmePlayer();
                return false;
            }
        }

        private bool TryStartVlc(string videoPath)
        {
            _vlcTried = true;
            try
            {
                try
                {
                    var libVlcDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "libvlc");
                    if (Directory.Exists(libVlcDir))
                        LibVLCSharp.Shared.Core.Initialize(libVlcDir);
                    else
                        LibVLCSharp.Shared.Core.Initialize();
                }
                catch
                {
                }

                _vlc = new LibVLC("--quiet", "--avcodec-hw=d3d11va,dxva2,none", "--aout=directsound");
                _vlcPlayer = new LibVLCSharp.Shared.MediaPlayer(_vlc);
                _vlcMedia = new LibVLCSharp.Shared.Media(_vlc, videoPath, FromType.FromPath);
                _vlcPlayer.Media = _vlcMedia;
                _vlcPlayer.Volume = 100;

                _vlcPlayer.Playing += (_, _) =>
                {
                    try
                    {
                        Dispatcher.BeginInvoke(new Action(() =>
                        {
                            _playbackStarted = true;
                            StopSafetyTimer();
                            try { AppLogger.LogInfo("[StartupVideo] VLC intro playback started."); } catch { }
                        }));
                    }
                    catch
                    {
                    }
                };
                _vlcPlayer.EndReached += (_, _) =>
                {
                    try { Dispatcher.BeginInvoke(new Action(CompleteAndClose)); } catch { }
                };
                _vlcPlayer.EncounteredError += (_, _) =>
                {
                    try
                    {
                        AppLogger.LogWarning("[StartupVideo] VLC playback failed.");
                    }
                    catch
                    {
                    }

                    try
                    {
                        Dispatcher.BeginInvoke(new Action(() =>
                        {
                            CleanupVlcPlayer();
                            if (!string.IsNullOrWhiteSpace(_videoPath) && TryNextFallbackPlayer())
                                return;

                            CompleteAndClose();
                        }));
                    }
                    catch
                    {
                    }
                };

                var view = new VideoView
                {
                    MediaPlayer = _vlcPlayer,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Stretch,
                };

                // VLC uses a Win32 child HWND that covers WPF elements (airspace).
                // Move the skip button into the VideoView overlay so it renders on top.
                var overlayGrid = new Grid { Background = System.Windows.Media.Brushes.Transparent };
                var skipBtn = new Button
                {
                    Content = "Skip",
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Margin = new Thickness(0, 0, 40, 40),
                    Padding = new Thickness(20, 10, 20, 10),
                    FontSize = 16,
                    Foreground = System.Windows.Media.Brushes.White,
                    Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)),
                    BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF)),
                    BorderThickness = new Thickness(1),
                    Cursor = System.Windows.Input.Cursors.Hand,
                };
                skipBtn.Click += (_, _) => CompleteAndClose();
                // Style the button with rounded corners
                var template = new ControlTemplate(typeof(Button));
                var borderFactory = new FrameworkElementFactory(typeof(Border));
                borderFactory.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
                borderFactory.SetBinding(Border.BorderBrushProperty, new System.Windows.Data.Binding("BorderBrush") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
                borderFactory.SetBinding(Border.BorderThicknessProperty, new System.Windows.Data.Binding("BorderThickness") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
                borderFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
                borderFactory.SetBinding(Border.PaddingProperty, new System.Windows.Data.Binding("Padding") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
                var cpFactory = new FrameworkElementFactory(typeof(ContentPresenter));
                cpFactory.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
                cpFactory.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
                borderFactory.AppendChild(cpFactory);
                template.VisualTree = borderFactory;
                skipBtn.Template = template;
                overlayGrid.Children.Add(skipBtn);
                view.Content = overlayGrid;

                VideoHost.Children.Clear();
                VideoHost.Children.Add(view);
                _vlcView = view;
                try { AppLogger.LogInfo($"[StartupVideo] Starting VLC intro playback: {videoPath}"); } catch { }
                // Defer VLC Play() to after the VideoView host HWND is laid out
                // so VLC renders onto a valid surface instead of a black rectangle.
                var vlcPlayer = _vlcPlayer;
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Render, new Action(() =>
                {
                    try { vlcPlayer?.Play(); } catch { }
                }));
                return true;
            }
            catch (Exception ex)
            {
                try { AppLogger.LogWarning($"[StartupVideo] VLC initialization failed: {ex.Message}"); } catch { }
                CleanupVlcPlayer();
                return false;
            }
        }

        private bool TryStartWmf(string videoPath)
        {
            try
            {
                _wmfFallbackStarted = true;
                var player = new MediaElement
                {
                    LoadedBehavior = MediaState.Manual,
                    UnloadedBehavior = MediaState.Close,
                    Stretch = Stretch.Uniform,
                    ScrubbingEnabled = true,
                    Volume = 1.0,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Stretch,
                    IsHitTestVisible = false,
                };

                player.MediaOpened += (_, _) =>
                {
                    _playbackStarted = true;
                    StopSafetyTimer();
                    try { player.Play(); } catch { }
                };
                player.MediaEnded += VideoPlayer_MediaEnded;
                player.MediaFailed += VideoPlayer_MediaFailed;

                VideoHost.Children.Clear();
                VideoHost.Children.Add(player);
                _wmfPlayer = player;
                try { AppLogger.LogInfo($"[StartupVideo] Starting WMF intro playback: {videoPath}"); } catch { }
                // Source triggers async media open; Play() fires in MediaOpened.
                // Window.Show() is called before PlayVideo() so the HWND is ready.
                player.Source = new Uri(videoPath, UriKind.Absolute);
                return true;
            }
            catch (Exception ex)
            {
                try { AppLogger.LogWarning($"[StartupVideo] WMF initialization failed: {ex.Message}"); } catch { }
                CleanupWmfPlayer();
                return false;
            }
        }

        private void FfmePlayer_MediaOpened(object? sender, EventArgs e)
        {
            try
            {
                _playbackStarted = true;
                StopSafetyTimer();
                _ffmePlayer?.Play();
            }
            catch
            {
            }
        }

        private void FfmePlayer_MediaEnded(object? sender, EventArgs e)
        {
            CompleteAndClose();
        }

        private void FfmePlayer_MediaFailed(object? sender, Unosquare.FFME.Common.MediaFailedEventArgs e)
        {
            try { AppLogger.LogWarning($"[StartupVideo] FFME playback failed: {e.ErrorException?.Message}"); } catch { }
            CleanupFfmePlayer();
            if (!string.IsNullOrWhiteSpace(_videoPath) && TryNextFallbackPlayer())
                return;

            CompleteAndClose();
        }

        private bool TryNextFallbackPlayer()
        {
            if (string.IsNullOrWhiteSpace(_videoPath))
                return false;

            if (!_vlcTried && TryStartVlc(_videoPath))
                return true;

            if (!_ffmeTried && TryStartFfme(_videoPath))
                return true;

            if (!_wmfFallbackStarted && TryStartWmf(_videoPath))
                return true;

            return false;
        }

        private void CleanupPlayers()
        {
            CleanupVlcPlayer();
            CleanupFfmePlayer();
            CleanupWmfPlayer();
            try
            {
                VideoHost.Children.Clear();
            }
            catch
            {
            }
        }

        private void CleanupFfmePlayer()
        {
            try
            {
                if (_ffmePlayer == null)
                    return;

                _ffmePlayer.MediaOpened -= FfmePlayer_MediaOpened;
                _ffmePlayer.MediaEnded -= FfmePlayer_MediaEnded;
                _ffmePlayer.MediaFailed -= FfmePlayer_MediaFailed;
                _ffmePlayer.Close();
                _ffmePlayer.Dispose();
            }
            catch
            {
            }
            finally
            {
                _ffmePlayer = null;
            }
        }

        private void CleanupVlcPlayer()
        {
            try
            {
                if (_vlcView != null)
                    _vlcView.MediaPlayer = null;
            }
            catch
            {
            }

            try
            {
                _vlcPlayer?.Stop();
            }
            catch
            {
            }

            try
            {
                _vlcMedia?.Dispose();
            }
            catch
            {
            }

            try
            {
                _vlcPlayer?.Dispose();
            }
            catch
            {
            }

            try
            {
                _vlc?.Dispose();
            }
            catch
            {
            }

            _vlcView = null;
            _vlcMedia = null;
            _vlcPlayer = null;
            _vlc = null;
        }

        private void CleanupWmfPlayer()
        {
            try
            {
                if (_wmfPlayer == null)
                    return;

                _wmfPlayer.MediaEnded -= VideoPlayer_MediaEnded;
                _wmfPlayer.MediaFailed -= VideoPlayer_MediaFailed;
                _wmfPlayer.Stop();
                _wmfPlayer.Close();
                _wmfPlayer.Source = null;
            }
            catch
            {
            }
            finally
            {
                _wmfPlayer = null;
            }
        }

        private void StartSafetyTimer()
        {
            StopSafetyTimer();
            _safetyTimer = new DispatcherTimer { Interval = PlaybackStartTimeout };
            _safetyTimer.Tick += (_, _) =>
            {
                StopSafetyTimer();
                if (!_playbackStarted)
                {
                    try { AppLogger.LogWarning("[StartupVideo] Playback did not start before timeout; closing intro window."); } catch { }
                    CompleteAndClose();
                }
            };
            _safetyTimer.Start();
        }

        private void StopSafetyTimer()
        {
            _safetyTimer?.Stop();
            _safetyTimer = null;
        }

        private void InvokeCompletion()
        {
            StopSafetyTimer();
            var completion = _onComplete;
            _onComplete = null;

            if (completion == null)
                return;

            try
            {
                Dispatcher.BeginInvoke(completion, System.Windows.Threading.DispatcherPriority.Normal);
            }
            catch
            {
                try
                {
                    completion();
                }
                catch
                {
                }
            }
        }
    }
}
