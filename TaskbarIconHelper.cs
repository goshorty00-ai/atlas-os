using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using System.Windows;

namespace AtlasAI
{
    /// <summary>
    /// Helper class to show a proper taskbar icon for borderless WPF windows
    /// Uses Windows Forms NotifyIcon for reliable taskbar presence
    /// </summary>
    public class TaskbarIconHelper : IDisposable
    {
        private static readonly object SyncRoot = new();
        private static TaskbarIconHelper? _activeHelper;
        private NotifyIcon? _notifyIcon;
        private Window? _targetWindow;
        private bool _disposed;

        public TaskbarIconHelper(Window targetWindow)
        {
            _targetWindow = targetWindow;
            RegisterAsActive();
            Initialize();
        }

        private void RegisterAsActive()
        {
            lock (SyncRoot)
            {
                if (_activeHelper != null && !ReferenceEquals(_activeHelper, this))
                    _activeHelper.DisposeInternal(clearActiveReference: false);

                _activeHelper = this;
            }
        }

        private void Initialize()
        {
            try
            {
                _notifyIcon = new NotifyIcon();
                
                // Try to load icon from various locations
                var iconPath = FindIconPath();
                if (!string.IsNullOrEmpty(iconPath) && File.Exists(iconPath))
                {
                    _notifyIcon.Icon = new Icon(iconPath);
                }
                else
                {
                    // Use system application icon as fallback
                    _notifyIcon.Icon = SystemIcons.Application;
                }

                _notifyIcon.Text = "Atlas AI";
                _notifyIcon.Visible = true;

                // Handle click to show/focus window
                _notifyIcon.Click += (s, e) =>
                {
                    if (_targetWindow != null)
                    {
                        if (_targetWindow.WindowState == WindowState.Minimized)
                            _targetWindow.WindowState = WindowState.Normal;
                        _targetWindow.Activate();
                        _targetWindow.Focus();
                    }
                };

                // Handle double-click
                _notifyIcon.DoubleClick += (s, e) =>
                {
                    if (_targetWindow != null)
                    {
                        _targetWindow.WindowState = WindowState.Normal;
                        _targetWindow.Activate();
                        _targetWindow.Focus();
                    }
                };

                // Create context menu
                var contextMenu = new ContextMenuStrip();
                contextMenu.Items.Add("Show Atlas", null, (s, e) => 
                {
                    if (_targetWindow != null)
                    {
                        _targetWindow.WindowState = WindowState.Normal;
                        _targetWindow.Show();
                        _targetWindow.Activate();
                    }
                });
                var audioWallpaperItem = new ToolStripMenuItem();
                audioWallpaperItem.Click += (_, _) =>
                {
                    try { AtlasAI.Wallpaper.WallpaperService.ToggleAudioWallpaper(); } catch { }
                };
                contextMenu.Opening += (_, _) =>
                {
                    try
                    {
                        var running = AtlasAI.Wallpaper.WallpaperService.IsAudioWallpaperRunning;
                        audioWallpaperItem.Text = running ? "Stop Audio Wallpaper" : "Start Audio Wallpaper";
                    }
                    catch
                    {
                        audioWallpaperItem.Text = "Start Audio Wallpaper";
                    }
                };
                contextMenu.Items.Add(audioWallpaperItem);
                contextMenu.Items.Add("-");
                contextMenu.Items.Add("Exit", null, (s, e) => 
                {
                    System.Windows.Application.Current.Shutdown();
                });
                _notifyIcon.ContextMenuStrip = contextMenu;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[TaskbarIconHelper] Error: {ex.Message}");
            }
        }

        private string? FindIconPath()
        {
            var paths = new[]
            {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "atlas.ico"),
                Path.Combine(Directory.GetCurrentDirectory(), "atlas.ico"),
                Path.Combine(Directory.GetCurrentDirectory(), "AtlasAI", "atlas.ico"),
                @"C:\Users\littl\VisualAIVirtualAssistant\AtlasAI\atlas.ico"
            };

            foreach (var path in paths)
            {
                if (File.Exists(path))
                    return path;
            }
            return null;
        }

        public void UpdateIcon(string iconPath)
        {
            if (_notifyIcon != null && File.Exists(iconPath))
            {
                _notifyIcon.Icon = new Icon(iconPath);
            }
        }

        public void ShowBalloon(string title, string text, ToolTipIcon icon = ToolTipIcon.Info)
        {
            _notifyIcon?.ShowBalloonTip(3000, title, text, icon);
        }

        public void Dispose()
        {
            DisposeInternal(clearActiveReference: true);
        }

        private void DisposeInternal(bool clearActiveReference)
        {
            if (!_disposed)
            {
                _disposed = true;
                if (_notifyIcon != null)
                {
                    _notifyIcon.Visible = false;
                    _notifyIcon.Dispose();
                    _notifyIcon = null;
                }

                if (clearActiveReference)
                {
                    lock (SyncRoot)
                    {
                        if (ReferenceEquals(_activeHelper, this))
                            _activeHelper = null;
                    }
                }
            }
        }
    }
}
