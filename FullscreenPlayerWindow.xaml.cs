// Fullscreen Media Player Window
// Provides exclusive fullscreen playback with auto-hiding controls

using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using AtlasAI.MediaScanner;

namespace AtlasAI
{
    public partial class FullscreenPlayerWindow : Window
    {
        private MediaPlaybackService _playbackService;
        private DispatcherTimer _hideControlsTimer;
        private DispatcherTimer _cursorPollTimer;   // direct Win32 poll — works even over LibVLC HWND
        private System.Windows.Point _lastCursorPos;
        private bool _controlsVisible = true;
        private bool _isPipMode = false;

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT pt);
        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X; public int Y; }

        public FullscreenPlayerWindow(MediaPlaybackService playbackService)
        {
            InitializeComponent();
            
            System.Diagnostics.Debug.WriteLine("[FullscreenPlayer] ===== INITIALIZING FULLSCREEN WINDOW =====");
            
            _playbackService = playbackService;
            
            // Auto-hide timer: hides controls after 3 seconds of mouse inactivity
            _hideControlsTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            _hideControlsTimer.Tick += (s, e) => { _hideControlsTimer.Stop(); HideControls(); };

            // Direct cursor poll — LibVLC HWND eats WPF mouse events so Window_MouseMove is
            // unreliable over the video surface.  This 100ms poll works regardless.
            _cursorPollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            _cursorPollTimer.Tick += CursorPollTimer_Tick;

            // Set IsFullscreen first so MediaPlayerControl knows it's the active fullscreen player
            // (used to filter stream URLs in PlaybackService_CurrentMediaChanged)
            PlayerControl.IsFullscreen = true;
            // Suppress MediaPlayerControl's own TopControlBar/ControlBar — FullscreenPlayerWindow
            // provides its own ControlsOverlay so both overlays must never be visible at the same time.
            PlayerControl.UseExternalControls = true;
            // Set PlaybackService so VLC renders correctly and MediaPlayerControl receives playback events
            PlayerControl.PlaybackService = _playbackService;
            System.Diagnostics.Debug.WriteLine("[FullscreenPlayer] PlayerControl configured");
            
            // Subscribe to player events
            PlayerControl.PlayerClosed += PlayerControl_PlayerClosed;
            PlayerControl.PlaybackEnded += PlayerControl_PlaybackEnded;
            // Wire mouse-activity from the LibVLC video surface to our overlay —
            // the native HWND video surface swallows WPF MouseMove so Window_MouseMove
            // never fires over video; UserActivityDetected is our proxy.
            PlayerControl.UserActivityDetected += PlayerControl_UserActivityDetected;
            System.Diagnostics.Debug.WriteLine("[FullscreenPlayer] Subscribed to player events");
            
            // Wait for window to be fully loaded before loading media
            this.Loaded += (s, e) =>
            {
                System.Diagnostics.Debug.WriteLine("[FullscreenPlayer] Window LOADED event fired");
                // Start auto-hide and cursor-poll timers
                _hideControlsTimer.Start();
                _cursorPollTimer.Start();
                // Do NOT call PlayerControl.LoadMedia here. MediaPlayerControl is already
                // subscribed to PlaybackService.CurrentMediaChanged and will load media via
                // QueueMediaLoad/TryLoadPendingMedia. A direct call here causes LoadMedia to
                // run twice for one PlaySingle, cancelling and restarting VLC.
                if (_playbackService.CurrentMedia != null)
                {
                    UpdateNowPlayingLabel(_playbackService.CurrentMedia.DisplayName);
                }
            };
            
            System.Diagnostics.Debug.WriteLine("[FullscreenPlayer] ===== WINDOW INITIALIZATION COMPLETE =====");
        }

        private void ShowControls()
        {
            if (!_controlsVisible)
            {
                _controlsVisible = true;
                ControlsOverlay.Visibility = Visibility.Visible;
                Cursor = Cursors.Arrow;
            }
            _hideControlsTimer.Stop();
            _hideControlsTimer.Start();
        }

        private void CursorPollTimer_Tick(object? sender, EventArgs e)
        {
            if (_isPipMode) return;
            try
            {
                if (GetCursorPos(out POINT pt))
                {
                    var pos = new System.Windows.Point(pt.X, pt.Y);
                    if (Math.Abs(pos.X - _lastCursorPos.X) > 3 || Math.Abs(pos.Y - _lastCursorPos.Y) > 3)
                    {
                        _lastCursorPos = pos;
                        ShowControls();
                    }
                }
            }
            catch { }
        }

        private void HideControls()
        {
            _controlsVisible = false;
            ControlsOverlay.Visibility = Visibility.Collapsed;
            Cursor = Cursors.None;
        }

        private void UpdateNowPlayingLabel(string title)
        {
            if (NowPlayingLabel != null && !string.IsNullOrWhiteSpace(title))
                NowPlayingLabel.Text = title;
        }

        private void Window_MouseMove(object sender, MouseEventArgs e)
        {
            ShowControls();
        }
        
        private void TopBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            try
            {
                if (e.ClickCount >= 2)
                {
                    WindowState = WindowState == WindowState.Normal ? WindowState.Maximized : WindowState.Normal;
                    return;
                }
                DragMove();
            }
            catch
            {
            }
        }

        private void BackButton_Click(object sender, RoutedEventArgs e) => Close();

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void EnterPipMode()
        {
            if (_isPipMode) return;
            _isPipMode = true;
            _hideControlsTimer.Stop();
            _cursorPollTimer.Stop();
            ControlsOverlay.Visibility = Visibility.Collapsed;
            PipBar.Visibility = Visibility.Visible;
            // Switch out of Maximized so we can resize/reposition
            WindowState = WindowState.Normal;
            ResizeMode = ResizeMode.CanResizeWithGrip;
            const double w = 340, h = 212;
            var area = SystemParameters.WorkArea;
            Left = area.Right - w - 16;
            Top = area.Bottom - h - 16;
            Width = w;
            Height = h;
            Topmost = true;
            // Update PiP title label
            if (PipTitleLabel != null && NowPlayingLabel != null)
                PipTitleLabel.Text = NowPlayingLabel.Text;
        }

        private void PipRestoreButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_isPipMode) return;
            _isPipMode = false;
            PipBar.Visibility = Visibility.Collapsed;
            Topmost = false;
            ResizeMode = ResizeMode.NoResize;
            WindowState = WindowState.Maximized;
            _cursorPollTimer.Start();
            ShowControls();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
        
        private void PlayerControl_UserActivityDetected(object? sender, EventArgs e)
        {
            ShowControls();
        }

        private void PlayerControl_PlayerClosed(object? sender, EventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("[FullscreenPlayer] Player closed - closing window");
            Close();
        }
        
        private void PlayerControl_PlaybackEnded(object? sender, EventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("[FullscreenPlayer] Playback ended - playing next");
            _playbackService.PlayNext();
        }
        
        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Escape:
                case Key.F11:
                    Close();
                    e.Handled = true;
                    break;
                    
                case Key.Left when Keyboard.Modifiers == ModifierKeys.Shift:
                    _playbackService.PlayPrevious();
                    e.Handled = true;
                    break;
                    
                case Key.Right when Keyboard.Modifiers == ModifierKeys.Shift:
                    _playbackService.PlayNext();
                    e.Handled = true;
                    break;
            }
        }
        
        protected override void OnClosed(EventArgs e)
        {
            try
            {
                PlayerControl.StopPlayback();
            }
            catch
            {
            }

            _hideControlsTimer.Stop();
            _cursorPollTimer.Stop();
            PlayerControl.PlaybackService = null;
            PlayerControl.PlayerClosed -= PlayerControl_PlayerClosed;
            PlayerControl.PlaybackEnded -= PlayerControl_PlaybackEnded;
            PlayerControl.UserActivityDetected -= PlayerControl_UserActivityDetected;
            base.OnClosed(e);
        }
    }
}
