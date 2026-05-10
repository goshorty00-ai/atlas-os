using System;
using System.Windows;

namespace AtlasAI.Wallpaper
{
    public static class WallpaperService
    {
        private static readonly object _lock = new();
        private static AudioReactiveWallpaperWindow? _audioWindow;

        public static bool IsAudioWallpaperRunning
        {
            get
            {
                lock (_lock) return _audioWindow != null;
            }
        }

        public static void ToggleAudioWallpaper()
        {
            lock (_lock)
            {
                if (_audioWindow != null)
                {
                    try { _audioWindow.Close(); } catch { }
                    _audioWindow = null;
                    return;
                }

                Application.Current.Dispatcher.Invoke(() =>
                {
                    try
                    {
                        _audioWindow = new AudioReactiveWallpaperWindow
                        {
                            ShowActivated = false
                        };
                        _audioWindow.Show();
                    }
                    catch
                    {
                        _audioWindow = null;
                    }
                });
            }
        }

        public static void StopAudioWallpaper()
        {
            lock (_lock)
            {
                if (_audioWindow == null) return;
                try { _audioWindow.Close(); } catch { }
                _audioWindow = null;
            }
        }
    }
}

