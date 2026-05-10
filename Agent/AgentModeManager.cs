using System;
using System.IO;
using System.Text.Json;

namespace AtlasAI.Agent
{
    /// <summary>
    /// Manages Agent Mode state and preferences
    /// </summary>
    public static class AgentModeManager
    {
        private static bool _isAgentModeEnabled = false;
        private static readonly string SettingsPath;

        public static event EventHandler<bool>? AgentModeChanged;

        public static bool IsAgentModeEnabled
        {
            get => _isAgentModeEnabled;
            set
            {
                if (_isAgentModeEnabled != value)
                {
                    _isAgentModeEnabled = value;
                    SaveSettings();
                    AgentModeChanged?.Invoke(null, value);
                }
            }
        }

        static AgentModeManager()
        {
            SettingsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "AtlasAI", "agent_mode.json");
            LoadSettings();
        }

        private static void LoadSettings()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    var json = File.ReadAllText(SettingsPath);
                    var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("enabled", out var enabled))
                        _isAgentModeEnabled = enabled.GetBoolean();
                }
            }
            catch { }
        }

        private static void SaveSettings()
        {
            try
            {
                var dir = Path.GetDirectoryName(SettingsPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                var settings = new { enabled = _isAgentModeEnabled };
                var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SettingsPath, json);
            }
            catch { }
        }

        /// <summary>
        /// Toggle agent mode on/off
        /// </summary>
        public static void Toggle()
        {
            IsAgentModeEnabled = !IsAgentModeEnabled;
        }
    }
}
