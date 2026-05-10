// Fullscreen Media Player Window
// Provides exclusive fullscreen playback with auto-hiding controls

using System;
using System.Windows;
using System.Windows.Input;
using AtlasAI.MediaScanner;

namespace AtlasAI
{
    public partial class FullscreenPlayerWindow : Window
    {
        private MediaPlaybackService _playbackService;
        
        public FullscreenPlayerWindow(MediaPlaybackService playbackService)
        {
            InitializeComponent();
            
            System.Diagnostics.Debug.WriteLine("[FullscreenPlayer] ===== INITIALIZING FULLSCREEN WINDOW =====");
            
            _playbackService = playbackService;
            
            // Pass playback service to player control
            PlayerControl.PlaybackService = _playbackService;
            PlayerControl.IsFullscreen = true; // Tell player it's in fullscreen mode
            System.Diagnostics.Debug.WriteLine("[FullscreenPlayer] PlaybackService assigned to PlayerControl");
            
            // Subscribe to playback service events
            _playbackService.CurrentMediaChanged += PlaybackService_CurrentMediaChanged;
            System.Diagnostics.Debug.WriteLine("[FullscreenPlayer] Subscribed to CurrentMediaChanged event");
            
            // Subscribe to player events
            PlayerControl.PlayerClosed += PlayerControl_PlayerClosed;
            PlayerControl.PlaybackEnded += PlayerControl_PlaybackEnded;
            System.Diagnostics.Debug.WriteLine("[FullscreenPlayer] Subscribed to player events");
            
            // CRITICAL: Wait for window to be fully loaded before loading media
            this.Loaded += (s, e) =>
            {
                System.Diagnostics.Debug.WriteLine("[FullscreenPlayer] Window LOADED event fired");
                
                // Load current media if available
                if (_playbackService.CurrentMedia != null)
                {
                    System.Diagnostics.Debug.WriteLine($"[FullscreenPlayer] Loading current media: {_playbackService.CurrentMedia.DisplayName}");
                    PlayerControl.LoadMedia(_playbackService.CurrentMedia);
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("[FullscreenPlayer] WARNING: No current media in playback service");
                }
            };
            
            System.Diagnostics.Debug.WriteLine("[FullscreenPlayer] ===== WINDOW INITIALIZATION COMPLETE =====");
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

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
        
        private void PlaybackService_CurrentMediaChanged(object? sender, MediaItem e)
        {
            System.Diagnostics.Debug.WriteLine("[FullscreenPlayer] ===== CURRENT MEDIA CHANGED EVENT =====");
            System.Diagnostics.Debug.WriteLine($"[FullscreenPlayer] New media: {e.DisplayName}");
            System.Diagnostics.Debug.WriteLine($"[FullscreenPlayer] File path: {e.FilePath}");
            
            Dispatcher.Invoke(() =>
            {
                System.Diagnostics.Debug.WriteLine($"[FullscreenPlayer] Dispatcher.Invoke - Loading media on UI thread");
                PlayerControl.LoadMedia(e);
                System.Diagnostics.Debug.WriteLine($"[FullscreenPlayer] LoadMedia() call complete");
            });
            
            System.Diagnostics.Debug.WriteLine("[FullscreenPlayer] ===== CURRENT MEDIA CHANGED COMPLETE =====");
        }
        
        private void PlayerControl_PlayerClosed(object? sender, EventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("[FullscreenPlayer] Player closed - closing window");
            Close();
        }
        
        private void PlayerControl_PlaybackEnded(object? sender, EventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("[FullscreenPlayer] Playback ended - playing next");
            
            // Auto-play next track
            _playbackService.PlayNext();
        }
        
        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            // Global keyboard shortcuts
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
            // Unsubscribe from events
            _playbackService.CurrentMediaChanged -= PlaybackService_CurrentMediaChanged;
            PlayerControl.PlayerClosed -= PlayerControl_PlayerClosed;
            PlayerControl.PlaybackEnded -= PlayerControl_PlaybackEnded;
            
            base.OnClosed(e);
        }
    }
}
