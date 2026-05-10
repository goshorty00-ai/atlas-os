using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace AtlasAI.Core
{
    /// <summary>
    /// Handles loading and saving user preferences to disk.
    /// Fail-closed: if file missing/corrupt → use defaults, never crash.
    /// Location: %AppData%\AtlasAI\settings.json
    /// </summary>
    public class PreferencesStore
    {
        private static PreferencesStore? _instance;
        public static PreferencesStore Instance => _instance ??= new PreferencesStore();

        private static readonly string AppDataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AtlasAI");
        private static readonly string SettingsPath = Path.Combine(AppDataDir, "settings.json");
        private static readonly string LegacyPreferencesPath = Path.Combine(AppDataDir, "preferences.json");
        private static readonly string ExportsDir = Path.Combine(AppDataDir, "exports");

        private UserPreferences _current;
        private readonly object _lock = new();
        private bool _isDirty = false;
        private DateTime _lastSave = DateTime.MinValue;
        private const int SaveDebounceMs = 2000; // Debounce saves to avoid excessive disk writes

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new JsonStringEnumConverter() }
        };

        public event EventHandler<UserPreferences>? PreferencesChanged;

        private PreferencesStore()
        {
            _current = Load();
        }

        /// <summary>
        /// Get current preferences (read-only snapshot)
        /// </summary>
        public UserPreferences Current
        {
            get
            {
                lock (_lock)
                {
                    return _current;
                }
            }
        }

        /// <summary>
        /// Update preferences with an action. Automatically saves after debounce.
        /// </summary>
        public void Update(Action<UserPreferences> updateAction)
        {
            lock (_lock)
            {
                updateAction(_current);
                _current.Sanitize();
                _current.LastModified = DateTime.UtcNow;
                _isDirty = true;
            }

            // Debounced save
            _ = SaveDebouncedAsync();

            PreferencesChanged?.Invoke(this, _current);
        }

        /// <summary>
        /// Record a command execution (updates most-used and recent)
        /// </summary>
        public void RecordCommandExecution(string commandId, string commandType)
        {
            if (string.IsNullOrWhiteSpace(commandId)) return;

            Update(prefs =>
            {
                // Update most-used
                if (prefs.MostUsedCommands.ContainsKey(commandId))
                    prefs.MostUsedCommands[commandId]++;
                else
                    prefs.MostUsedCommands[commandId] = 1;

                // Update recent (add to front, remove duplicates)
                prefs.RecentCommandIds.Remove(commandId);
                prefs.RecentCommandIds.Insert(0, commandId);

                // Update last used type
                if (!string.IsNullOrWhiteSpace(commandType))
                    prefs.LastUsedCommandType = commandType;
            });
        }

        /// <summary>
        /// Record a module open/navigation (Command Center tabs, etc.).
        /// Stores IDs only (no user content).
        /// </summary>
        public void RecordModuleOpen(string moduleId)
        {
            if (string.IsNullOrWhiteSpace(moduleId)) return;

            var id = moduleId.Trim();
            Update(prefs =>
            {
                if (prefs.MostUsedModules.ContainsKey(id))
                    prefs.MostUsedModules[id]++;
                else
                    prefs.MostUsedModules[id] = 1;

                prefs.RecentModuleIds.RemoveAll(m => string.Equals(m, id, StringComparison.OrdinalIgnoreCase));
                prefs.RecentModuleIds.Insert(0, id);
            });
        }

        /// <summary>
        /// Toggle a pinned command
        /// </summary>
        public void TogglePinned(string commandId)
        {
            if (string.IsNullOrWhiteSpace(commandId)) return;

            Update(prefs =>
            {
                if (prefs.PinnedCommandIds.Contains(commandId))
                    prefs.PinnedCommandIds.Remove(commandId);
                else if (prefs.PinnedCommandIds.Count < UserPreferences.MaxPinnedCommands)
                    prefs.PinnedCommandIds.Add(commandId);
            });
        }

        /// <summary>
        /// Check if a command is pinned
        /// </summary>
        public bool IsPinned(string commandId)
        {
            lock (_lock)
            {
                return _current.PinnedCommandIds.Contains(commandId);
            }
        }

        /// <summary>
        /// Force immediate save
        /// </summary>
        public void SaveNow()
        {
            lock (_lock)
            {
                if (!_isDirty) return;
                SaveInternal();
            }
        }

        /// <summary>
        /// Save specific preferences object (used by settings UI)
        /// </summary>
        public void SavePreferences(UserPreferences preferences)
        {
            lock (_lock)
            {
                _current = preferences;
                _current.Sanitize();
                _current.LastModified = DateTime.UtcNow;
                _isDirty = true;
                SaveInternal();
            }
            PreferencesChanged?.Invoke(this, _current);
        }

        /// <summary>
        /// Reset to defaults
        /// </summary>
        public void ResetToDefaults()
        {
            lock (_lock)
            {
                _current = UserPreferences.CreateDefault();
                _isDirty = true;
            }
            SaveNow();
            PreferencesChanged?.Invoke(this, _current);
        }

        /// <summary>
        /// Export preferences to exports folder
        /// </summary>
        public string? ExportPreferences()
        {
            try
            {
                EnsureDirectoryExists(ExportsDir);
                var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                var exportPath = Path.Combine(ExportsDir, $"preferences_{timestamp}.json");

                lock (_lock)
                {
                    var json = JsonSerializer.Serialize(_current, JsonOptions);
                    File.WriteAllText(exportPath, json);
                }

                Debug.WriteLine($"[PreferencesStore] Exported to: {exportPath}");
                return exportPath;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[PreferencesStore] Export failed: {ex.Message}");
                return null;
            }
        }

        #region Private Methods

        private UserPreferences Load()
        {
            try
            {
                var pathToLoad = File.Exists(SettingsPath)
                    ? SettingsPath
                    : (File.Exists(LegacyPreferencesPath) ? LegacyPreferencesPath : null);

                if (pathToLoad == null)
                {
                    Debug.WriteLine("[PreferencesStore] No preferences file, using defaults");
                    return UserPreferences.CreateDefault();
                }

                var json = File.ReadAllText(pathToLoad);
                var prefs = JsonSerializer.Deserialize<UserPreferences>(json, JsonOptions);

                if (prefs == null)
                {
                    Debug.WriteLine("[PreferencesStore] Deserialization returned null, using defaults");
                    return UserPreferences.CreateDefault();
                }

                // Schema migrations
                if (prefs.SchemaVersion < 2)
                {
                    Debug.WriteLine("[PreferencesStore] Migrating schema v1 → v2");
                    prefs.EnableWakeWordAudioCue = true;
                    prefs.FollowUpListeningDuration = 2.5;
                    prefs.SchemaVersion = 2;
                }

                prefs.Sanitize();
                Debug.WriteLine($"[PreferencesStore] Loaded preferences (schema v{prefs.SchemaVersion})");

                // One-time migration: legacy preferences.json -> settings.json
                try
                {
                    if (!string.Equals(pathToLoad, SettingsPath, StringComparison.OrdinalIgnoreCase))
                    {
                        EnsureDirectoryExists(AppDataDir);
                        var migratedJson = JsonSerializer.Serialize(prefs, JsonOptions);
                        File.WriteAllText(SettingsPath, migratedJson);
                        Debug.WriteLine("[PreferencesStore] Migrated legacy preferences.json to settings.json");
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[PreferencesStore] Migration write failed: {ex.Message}");
                }
                return prefs;
            }
            catch (JsonException ex)
            {
                Debug.WriteLine($"[PreferencesStore] JSON parse error: {ex.Message}, using defaults");
                return UserPreferences.CreateDefault();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[PreferencesStore] Load error: {ex.Message}, using defaults");
                return UserPreferences.CreateDefault();
            }
        }

        private void SaveInternal()
        {
            try
            {
                EnsureDirectoryExists(AppDataDir);
                var json = BuildMergedJson();
                File.WriteAllText(SettingsPath, json);
                _isDirty = false;
                _lastSave = DateTime.Now;
                Debug.WriteLine("[PreferencesStore] Saved preferences");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[PreferencesStore] Save error: {ex.Message}");
                // Fail silently - don't crash the app
            }
        }

        private string BuildMergedJson()
        {
            var serializedPreferences = JsonSerializer.Serialize(_current, JsonOptions);
            var incoming = JsonNode.Parse(serializedPreferences) as JsonObject ?? new JsonObject();
            var root = LoadExistingRoot();

            MergeInto(root, incoming);
            return root.ToJsonString(JsonOptions);
        }

        private static JsonObject LoadExistingRoot()
        {
            try
            {
                if (!File.Exists(SettingsPath))
                    return new JsonObject();

                var existingJson = File.ReadAllText(SettingsPath);
                return JsonNode.Parse(existingJson) as JsonObject ?? new JsonObject();
            }
            catch
            {
                return new JsonObject();
            }
        }

        private static void MergeInto(JsonObject target, JsonObject source)
        {
            foreach (var property in source)
            {
                target[property.Key] = property.Value?.DeepClone();
            }
        }

        private async Task SaveDebouncedAsync()
        {
            await Task.Delay(SaveDebounceMs);

            lock (_lock)
            {
                if (_isDirty && (DateTime.Now - _lastSave).TotalMilliseconds >= SaveDebounceMs)
                {
                    SaveInternal();
                }
            }
        }

        private static void EnsureDirectoryExists(string path)
        {
            if (!Directory.Exists(path))
                Directory.CreateDirectory(path);
        }

        #endregion
    }
}
