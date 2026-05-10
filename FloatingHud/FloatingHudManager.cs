using System;
using System.Diagnostics;
using System.Windows;
using AtlasAI.Core;

namespace AtlasAI.FloatingHud
{
    /// <summary>
    /// Manages the Floating HUD window lifecycle.
    /// Handles showing/hiding based on preferences.
    /// 
    /// SAFETY: No system operations, just window management.
    /// </summary>
    public class FloatingHudManager : IDisposable
    {
        private static FloatingHudManager? _instance;
        private static readonly object _lock = new();

        public static FloatingHudManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        _instance ??= new FloatingHudManager();
                    }
                }
                return _instance;
            }
        }

        private FloatingHudWindow? _hudWindow;
        private bool _disposed = false;

        private FloatingHudManager()
        {
            // Subscribe to preference changes
            PreferencesStore.Instance.PreferencesChanged += OnPreferencesChanged;
        }

        /// <summary>
        /// Initialize the HUD based on current preferences.
        /// Call this after the main window loads.
        /// </summary>
        public void Initialize()
        {
            try
            {
                var prefs = PreferencesStore.Instance.Current;
                if (prefs.FloatingHudEnabled)
                {
                    ShowHud();
                }
                Debug.WriteLine($"[FloatingHudManager] Initialized, enabled={prefs.FloatingHudEnabled}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FloatingHudManager] Init error: {ex.Message}");
            }
        }

        private void OnPreferencesChanged(object? sender, UserPreferences prefs)
        {
            try
            {
                Application.Current?.Dispatcher.BeginInvoke(() =>
                {
                    if (prefs.FloatingHudEnabled)
                    {
                        ShowHud();
                    }
                    else
                    {
                        HideHud();
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FloatingHudManager] Preference change error: {ex.Message}");
            }
        }

        /// <summary>
        /// Show the floating HUD window
        /// </summary>
        public void ShowHud()
        {
            try
            {
                if (_hudWindow == null || !_hudWindow.IsLoaded)
                {
                    _hudWindow = new FloatingHudWindow();
                    _hudWindow.Closed += (s, e) => _hudWindow = null;
                    _hudWindow.Show();
                    Debug.WriteLine("[FloatingHudManager] HUD window shown");
                }
                else
                {
                    _hudWindow.Activate();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FloatingHudManager] Show error: {ex.Message}");
            }
        }

        /// <summary>
        /// Hide the floating HUD window
        /// </summary>
        public void HideHud()
        {
            try
            {
                if (_hudWindow != null && _hudWindow.IsLoaded)
                {
                    _hudWindow.Close();
                    _hudWindow = null;
                    Debug.WriteLine("[FloatingHudManager] HUD window hidden");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FloatingHudManager] Hide error: {ex.Message}");
            }
        }

        /// <summary>
        /// Toggle HUD visibility
        /// </summary>
        public void ToggleHud()
        {
            var prefs = PreferencesStore.Instance.Current;
            PreferencesStore.Instance.Update(p => p.FloatingHudEnabled = !prefs.FloatingHudEnabled);
        }

        /// <summary>
        /// Check if HUD is currently visible
        /// </summary>
        public bool IsHudVisible => _hudWindow != null && _hudWindow.IsLoaded;

        public void Dispose()
        {
            if (!_disposed)
            {
                PreferencesStore.Instance.PreferencesChanged -= OnPreferencesChanged;
                HideHud();
                _disposed = true;
            }
        }
    }
}
