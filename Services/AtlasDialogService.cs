using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using AtlasAI.UI;

namespace AtlasAI.Services
{
    /// <summary>
    /// Implementation of Atlas-themed dialog service.
    /// Thread-safe, automatically marshals to UI thread.
    /// </summary>
    public class AtlasDialogService : IAtlasDialogService
    {
        private readonly Dictionary<string, bool> _dontShowAgainPrefs = new();
        private readonly string _prefsPath;
        private static readonly object _lock = new object();

        public AtlasDialogService()
        {
            var appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "AtlasAI");
            Directory.CreateDirectory(appDataPath);
            _prefsPath = Path.Combine(appDataPath, "dialog_prefs.json");

            LoadPreferences();
        }

        public async Task<AtlasDialogResult> ShowAsync(
            string title,
            string message,
            AtlasDialogButtons buttons = AtlasDialogButtons.OK,
            AtlasDialogIcon icon = AtlasDialogIcon.None,
            AtlasDialogButton defaultButton = AtlasDialogButton.Button1,
            bool showDontShowAgain = false,
            string? dontShowAgainKey = null)
        {
            // Check if user opted to not show this dialog
            if (!string.IsNullOrEmpty(dontShowAgainKey) && !ShouldShowDialog(dontShowAgainKey))
            {
                // Return default result based on button configuration
                return buttons switch
                {
                    AtlasDialogButtons.YesNo => AtlasDialogResult.Yes,
                    AtlasDialogButtons.YesNoCancel => AtlasDialogResult.Yes,
                    _ => AtlasDialogResult.OK
                };
            }

            // Marshal to UI thread if needed
            if (!Application.Current.Dispatcher.CheckAccess())
            {
                return await Application.Current.Dispatcher.InvokeAsync(() =>
                    ShowDialogInternal(title, message, buttons, icon, defaultButton, showDontShowAgain, dontShowAgainKey));
            }

            return ShowDialogInternal(title, message, buttons, icon, defaultButton, showDontShowAgain, dontShowAgainKey);
        }

        private AtlasDialogResult ShowDialogInternal(
            string title,
            string message,
            AtlasDialogButtons buttons,
            AtlasDialogIcon icon,
            AtlasDialogButton defaultButton,
            bool showDontShowAgain,
            string? dontShowAgainKey)
        {
            var dialog = new AtlasDialogWindow(title, message, buttons, icon, defaultButton, showDontShowAgain);

            // Set owner to main window if available
            try
            {
                if (Application.Current.MainWindow != null && Application.Current.MainWindow.IsLoaded)
                {
                    dialog.Owner = Application.Current.MainWindow;
                }
                else
                {
                    // Try to find an active window
                    foreach (Window window in Application.Current.Windows)
                    {
                        if (window.IsActive && window.IsVisible)
                        {
                            dialog.Owner = window;
                            break;
                        }
                    }
                }
            }
            catch
            {
                // Owner setting failed, continue without owner
            }

            dialog.ShowDialog();

            // Save "don't show again" preference
            if (dialog.DontShowAgain && !string.IsNullOrEmpty(dontShowAgainKey))
            {
                SetDontShowAgain(dontShowAgainKey, true);
            }

            return dialog.Result;
        }

        public async Task ShowInfoAsync(string title, string message)
        {
            await ShowAsync(title, message, AtlasDialogButtons.OK, AtlasDialogIcon.Info);
        }

        public async Task ShowWarningAsync(string title, string message)
        {
            await ShowAsync(title, message, AtlasDialogButtons.OK, AtlasDialogIcon.Warning);
        }

        public async Task ShowErrorAsync(string title, string message)
        {
            await ShowAsync(title, message, AtlasDialogButtons.OK, AtlasDialogIcon.Error);
        }

        public async Task<bool> ShowConfirmAsync(string title, string message, AtlasDialogIcon icon = AtlasDialogIcon.Question)
        {
            var result = await ShowAsync(title, message, AtlasDialogButtons.YesNo, icon);
            return result == AtlasDialogResult.Yes;
        }

        public bool ShouldShowDialog(string key)
        {
            lock (_lock)
            {
                return !_dontShowAgainPrefs.ContainsKey(key) || !_dontShowAgainPrefs[key];
            }
        }

        private void SetDontShowAgain(string key, bool value)
        {
            lock (_lock)
            {
                _dontShowAgainPrefs[key] = value;
                SavePreferences();
            }
        }

        public void ClearDontShowAgain(string key)
        {
            lock (_lock)
            {
                _dontShowAgainPrefs.Remove(key);
                SavePreferences();
            }
        }

        public void ClearAllDontShowAgain()
        {
            lock (_lock)
            {
                _dontShowAgainPrefs.Clear();
                SavePreferences();
            }
        }

        private void LoadPreferences()
        {
            try
            {
                if (File.Exists(_prefsPath))
                {
                    var json = File.ReadAllText(_prefsPath);
                    var prefs = JsonSerializer.Deserialize<Dictionary<string, bool>>(json);
                    if (prefs != null)
                    {
                        lock (_lock)
                        {
                            _dontShowAgainPrefs.Clear();
                            foreach (var kvp in prefs)
                            {
                                _dontShowAgainPrefs[kvp.Key] = kvp.Value;
                            }
                        }
                    }
                }
            }
            catch
            {
                // Ignore errors loading preferences
            }
        }

        private void SavePreferences()
        {
            try
            {
                lock (_lock)
                {
                    var json = JsonSerializer.Serialize(_dontShowAgainPrefs, new JsonSerializerOptions
                    {
                        WriteIndented = true
                    });
                    File.WriteAllText(_prefsPath, json);
                }
            }
            catch
            {
                // Ignore errors saving preferences
            }
        }
    }
}
