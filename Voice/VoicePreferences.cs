using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AtlasAI.Voice
{
    /// <summary>
    /// User voice preferences - persisted to disk.
    /// Supports global override, per-personality overrides, and system voice.
    /// </summary>
    public class VoicePreferences
    {
        private static VoicePreferences? _current;
        private static readonly object _lock = new();
        private static readonly string PreferencesPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AtlasAI", "voice_preferences.json");

        /// <summary>
        /// Current voice preferences (singleton).
        /// </summary>
        public static VoicePreferences Current
        {
            get
            {
                if (_current == null)
                {
                    lock (_lock)
                    {
                        _current ??= Load();
                    }
                }
                return _current;
            }
        }

        /// <summary>
        /// Global voice override. If set, this voice is used for ALL normal responses
        /// regardless of personality. Set to null to use personality defaults.
        /// </summary>
        [JsonPropertyName("globalVoiceId")]
        public string? GlobalVoiceId { get; set; }

        /// <summary>
        /// Per-personality voice overrides. Key is PersonalityId enum name.
        /// If a personality has an override, it's used instead of the default.
        /// </summary>
        [JsonPropertyName("personalityVoiceOverrides")]
        public Dictionary<string, string> PersonalityVoiceOverrides { get; set; } = new();

        /// <summary>
        /// System voice override for boot/alert/error messages.
        /// If null, uses the hardcoded system voice.
        /// </summary>
        [JsonPropertyName("systemVoiceId")]
        public string? SystemVoiceId { get; set; }

        /// <summary>
        /// Schema version for migration support.
        /// </summary>
        [JsonPropertyName("schemaVersion")]
        public int SchemaVersion { get; set; } = 1;

        /// <summary>
        /// Last modified timestamp.
        /// </summary>
        [JsonPropertyName("lastModified")]
        public DateTime LastModified { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Event fired when preferences change.
        /// </summary>
        public static event Action<VoicePreferences>? PreferencesChanged;

        #region Public Methods

        /// <summary>
        /// Set the global voice override.
        /// </summary>
        public void SetGlobalVoice(string? voiceId)
        {
            GlobalVoiceId = voiceId;
            Save();
            Debug.WriteLine($"[VoicePreferences] Global voice set to: {voiceId ?? "(default)"}");
        }

        /// <summary>
        /// Set a voice override for a specific personality (string key — works with any personality enum).
        /// </summary>
        public void SetPersonalityVoice(string personalityKey, string? voiceId)
        {
            if (string.IsNullOrEmpty(voiceId))
            {
                PersonalityVoiceOverrides.Remove(personalityKey);
                Debug.WriteLine($"[VoicePreferences] Cleared override for {personalityKey}");
            }
            else
            {
                PersonalityVoiceOverrides[personalityKey] = voiceId;
                Debug.WriteLine($"[VoicePreferences] Set {personalityKey} voice to: {voiceId}");
            }
            Save();
        }

        /// <summary>
        /// Get the voice override for a personality by string key (null if using default).
        /// </summary>
        public string? GetPersonalityVoice(string personalityKey)
        {
            return PersonalityVoiceOverrides.TryGetValue(personalityKey, out var voiceId) ? voiceId : null;
        }

        /// <summary>
        /// Set a voice override for a specific personality.
        /// </summary>
        public void SetPersonalityVoice(PersonalityId personality, string? voiceId)
        {
            var key = personality.ToString();
            
            if (string.IsNullOrEmpty(voiceId))
            {
                PersonalityVoiceOverrides.Remove(key);
                Debug.WriteLine($"[VoicePreferences] Cleared override for {personality}");
            }
            else
            {
                PersonalityVoiceOverrides[key] = voiceId;
                Debug.WriteLine($"[VoicePreferences] Set {personality} voice to: {voiceId}");
            }
            
            Save();
        }

        /// <summary>
        /// Get the voice override for a personality (null if using default).
        /// </summary>
        public string? GetPersonalityVoice(PersonalityId personality)
        {
            var key = personality.ToString();
            return PersonalityVoiceOverrides.TryGetValue(key, out var voiceId) ? voiceId : null;
        }

        /// <summary>
        /// Set the system voice override.
        /// </summary>
        public void SetSystemVoice(string? voiceId)
        {
            SystemVoiceId = voiceId;
            Save();
            Debug.WriteLine($"[VoicePreferences] System voice set to: {voiceId ?? "(default)"}");
        }

        /// <summary>
        /// Clear all voice overrides (reset to defaults).
        /// </summary>
        public void ResetToDefaults()
        {
            GlobalVoiceId = null;
            PersonalityVoiceOverrides.Clear();
            SystemVoiceId = null;
            Save();
            Debug.WriteLine("[VoicePreferences] Reset to defaults");
        }

        /// <summary>
        /// Save preferences to disk.
        /// </summary>
        public void Save()
        {
            lock (_lock)
            {
                try
                {
                    LastModified = DateTime.UtcNow;
                    
                    var dir = Path.GetDirectoryName(PreferencesPath);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                        Directory.CreateDirectory(dir);

                    var options = new JsonSerializerOptions { WriteIndented = true };
                    var json = JsonSerializer.Serialize(this, options);
                    File.WriteAllText(PreferencesPath, json);
                    
                    Debug.WriteLine("[VoicePreferences] Saved to disk");
                    PreferencesChanged?.Invoke(this);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[VoicePreferences] Save error: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Reload preferences from disk.
        /// </summary>
        public static void Reload()
        {
            lock (_lock)
            {
                _current = Load();
                PreferencesChanged?.Invoke(_current);
            }
        }

        #endregion

        #region Private Methods

        private static VoicePreferences Load()
        {
            try
            {
                if (File.Exists(PreferencesPath))
                {
                    var json = File.ReadAllText(PreferencesPath);
                    var prefs = JsonSerializer.Deserialize<VoicePreferences>(json);
                    if (prefs != null)
                    {
                        Debug.WriteLine($"[VoicePreferences] Loaded from disk (schema v{prefs.SchemaVersion})");
                        return prefs;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[VoicePreferences] Load error: {ex.Message}");
            }

            Debug.WriteLine("[VoicePreferences] Using defaults");
            return new VoicePreferences();
        }

        #endregion
    }
}
