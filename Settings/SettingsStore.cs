using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using AtlasAI.Conversation.Models;
using AtlasAI.Core;

namespace AtlasAI.Settings
{
    internal static class SettingsStore
    {
        private static readonly object Sync = new();
        private static AtlasSettings? _current;
        private static DateTime _lastLoadUtc = DateTime.MinValue;
        private const int ReloadSeconds = 10;
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() }
        };

        public static event EventHandler? SettingsChanged;

        public static AtlasSettings Current
        {
            get
            {
                lock (Sync)
                {
                    if (_current == null || (DateTime.UtcNow - _lastLoadUtc).TotalSeconds > ReloadSeconds)
                    {
                        _current = Load();
                        _lastLoadUtc = DateTime.UtcNow;
                    }
                    return _current;
                }
            }
        }

        public static void Save(AtlasSettings settings)
        {
            lock (Sync)
            {
				var path = GetPath();
				try
				{
					var dir = Path.GetDirectoryName(path);
					if (string.IsNullOrWhiteSpace(dir))
						throw new InvalidOperationException($"Invalid settings path: '{path}'");

					Directory.CreateDirectory(dir);
                    var json = BuildMergedJson(path, settings);

					var tmpPath = path + ".tmp";
					File.WriteAllText(tmpPath, json);
					if (File.Exists(path))					
						File.Replace(tmpPath, path, null, ignoreMetadataErrors: true);
					else
						File.Move(tmpPath, path);

					System.Diagnostics.Debug.WriteLine($"[SettingsStore] Save OK: {path}");
					_current = settings;
					_lastLoadUtc = DateTime.UtcNow;
					try { SettingsChanged?.Invoke(null, EventArgs.Empty); } catch { }
				}
				catch (Exception ex)
				{
					System.Diagnostics.Debug.WriteLine($"[SettingsStore] Save FAIL: {path} :: {ex.Message}");
					throw;
				}
            }
        }

        public static void Update(Action<AtlasSettings> updateAction)
        {
            ArgumentNullException.ThrowIfNull(updateAction);

            var snapshot = Current;
            var json = JsonSerializer.Serialize(snapshot, JsonOptions);
            var clone = JsonSerializer.Deserialize<AtlasSettings>(json, JsonOptions) ?? new AtlasSettings();
            clone = NormalizeSecrets(clone);

            updateAction(clone);
            Save(clone);
        }

        public static bool TryGetAiProviderKey(string providerId, out string plaintext)
        {
            var normalizedProviderId = NormalizeProviderId(providerId);
            if (string.IsNullOrWhiteSpace(normalizedProviderId))
            {
                plaintext = string.Empty;
                return false;
            }

            var settings = Current;
            if (settings.AiProviderKeys.TryGetValue(normalizedProviderId, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                plaintext = value;
                return true;
            }

            plaintext = string.Empty;
            return false;
        }

        public static void SetAiProviderKey(string providerId, string plaintext)
        {
            var normalizedProviderId = NormalizeProviderId(providerId);
            if (string.IsNullOrWhiteSpace(normalizedProviderId))
                return;

            Update(settings =>
            {
                if (string.IsNullOrWhiteSpace(plaintext))
                    settings.AiProviderKeys.Remove(normalizedProviderId);
                else
                    settings.AiProviderKeys[normalizedProviderId] = plaintext.Trim();
            });
        }

        public static bool TryGetVoiceProviderKey(string providerId, out string plaintext)
        {
            var normalizedProviderId = NormalizeProviderId(providerId);
            if (string.IsNullOrWhiteSpace(normalizedProviderId))
            {
                plaintext = string.Empty;
                return false;
            }

            var settings = Current;
            if (settings.VoiceProviderKeys.TryGetValue(normalizedProviderId, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                plaintext = value;
                return true;
            }

            plaintext = string.Empty;
            return false;
        }

        public static void SetVoiceProviderKey(string providerId, string plaintext)
        {
            var normalizedProviderId = NormalizeProviderId(providerId);
            if (string.IsNullOrWhiteSpace(normalizedProviderId))
                return;

            Update(settings =>
            {
                if (string.IsNullOrWhiteSpace(plaintext))
                    settings.VoiceProviderKeys.Remove(normalizedProviderId);
                else
                    settings.VoiceProviderKeys[normalizedProviderId] = plaintext.Trim();
            });
        }

        public static UserProfile GetConversationUserProfile()
        {
            try
            {
                var path = GetConversationProfilePath();
                if (!File.Exists(path))
                    return new UserProfile();

                var json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<UserProfile>(json, JsonOptions) ?? new UserProfile();
            }
            catch
            {
                return new UserProfile();
            }
        }

        public static void SaveConversationUserProfile(UserProfile profile)
        {
            var path = GetConversationProfilePath();
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(dir))
                Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(profile ?? new UserProfile(), JsonOptions);
            File.WriteAllText(path, json);
        }

        private static AtlasSettings Load()
        {
            try
            {
                var path = GetPath();
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                    return new AtlasSettings();

                var json = File.ReadAllText(path);
                var settings = JsonSerializer.Deserialize<AtlasSettings>(json, JsonOptions);
                return NormalizeSecrets(settings ?? new AtlasSettings());
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"[SettingsStore] Load FAIL: {ex.Message}");
				return new AtlasSettings();
			}
        }

        private static string BuildMergedJson(string path, AtlasSettings settings)
        {
            var serializedSettings = JsonSerializer.Serialize(CloneForPersistence(settings), JsonOptions);
            var incoming = JsonNode.Parse(serializedSettings) as JsonObject ?? new JsonObject();
            var root = LoadExistingRoot(path);

            MergeInto(root, incoming);
            return root.ToJsonString(JsonOptions);
        }

        private static JsonObject LoadExistingRoot(string path)
        {
            try
            {
                if (!File.Exists(path))
                    return new JsonObject();

                var existingJson = File.ReadAllText(path);
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

        private static string GetPath()
        {
			var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
			if (string.IsNullOrWhiteSpace(appData))
				throw new InvalidOperationException("ApplicationData path is empty");
			var dir = Path.Combine(appData, "AtlasAI");
			return Path.Combine(dir, "settings.json");
        }

        private static AtlasSettings CloneForPersistence(AtlasSettings settings)
        {
            var json = JsonSerializer.Serialize(settings ?? new AtlasSettings(), JsonOptions);
            var clone = JsonSerializer.Deserialize<AtlasSettings>(json, JsonOptions) ?? new AtlasSettings();

            clone.SmartHome.Govee.ApiKey = ProtectIfNeeded(clone.SmartHome.Govee.ApiKey);
            clone.SmartHome.PhilipsHue.ApplicationKey = ProtectIfNeeded(clone.SmartHome.PhilipsHue.ApplicationKey);
            clone.SmartHome.Ring.RefreshToken = ProtectIfNeeded(clone.SmartHome.Ring.RefreshToken);
            clone.SmartHome.LgWebOs.ClientKey = ProtectIfNeeded(clone.SmartHome.LgWebOs.ClientKey);
            clone.SmartHome.SmartThings.AccessToken = ProtectIfNeeded(clone.SmartHome.SmartThings.AccessToken);
            clone.SmartHome.HomeAssistant.AccessToken = ProtectIfNeeded(clone.SmartHome.HomeAssistant.AccessToken);
            clone.SmartHome.TapoKasa.Password = ProtectIfNeeded(clone.SmartHome.TapoKasa.Password);
            clone.SmartHome.OnvifRtsp.Password = ProtectIfNeeded(clone.SmartHome.OnvifRtsp.Password);
            clone.AiProviderKeys = ProtectDictionary(clone.AiProviderKeys);
            clone.VoiceProviderKeys = ProtectDictionary(clone.VoiceProviderKeys);

            return clone;
        }

        private static AtlasSettings NormalizeSecrets(AtlasSettings settings)
        {
            settings.SmartHome.Govee.ApiKey = SecretProtector.UnprotectIfNeeded(settings.SmartHome.Govee.ApiKey);
            settings.SmartHome.PhilipsHue.ApplicationKey = SecretProtector.UnprotectIfNeeded(settings.SmartHome.PhilipsHue.ApplicationKey);
            settings.SmartHome.Ring.RefreshToken = SecretProtector.UnprotectIfNeeded(settings.SmartHome.Ring.RefreshToken);
            settings.SmartHome.LgWebOs.ClientKey = SecretProtector.UnprotectIfNeeded(settings.SmartHome.LgWebOs.ClientKey);
            settings.SmartHome.SmartThings.AccessToken = SecretProtector.UnprotectIfNeeded(settings.SmartHome.SmartThings.AccessToken);
            settings.SmartHome.HomeAssistant.AccessToken = SecretProtector.UnprotectIfNeeded(settings.SmartHome.HomeAssistant.AccessToken);
            settings.SmartHome.TapoKasa.Password = SecretProtector.UnprotectIfNeeded(settings.SmartHome.TapoKasa.Password);
            settings.SmartHome.OnvifRtsp.Password = SecretProtector.UnprotectIfNeeded(settings.SmartHome.OnvifRtsp.Password);
            settings.AiProviderKeys = UnprotectDictionary(settings.AiProviderKeys);
            settings.VoiceProviderKeys = UnprotectDictionary(settings.VoiceProviderKeys);
            return settings;
        }

        private static Dictionary<string, string> ProtectDictionary(Dictionary<string, string>? values)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (values == null)
                return result;

            foreach (var pair in values)
            {
                var key = NormalizeProviderId(pair.Key);
                if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(pair.Value))
                    continue;

                result[key] = ProtectIfNeeded(pair.Value);
            }

            return result;
        }

        private static Dictionary<string, string> UnprotectDictionary(Dictionary<string, string>? values)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (values == null)
                return result;

            foreach (var pair in values)
            {
                var key = NormalizeProviderId(pair.Key);
                if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(pair.Value))
                    continue;

                result[key] = SecretProtector.UnprotectIfNeeded(pair.Value);
            }

            return result;
        }

        private static string NormalizeProviderId(string? providerId)
        {
            return (providerId ?? string.Empty).Trim().ToLowerInvariant();
        }

        private static string GetConversationProfilePath()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var dir = Path.Combine(appData, "AtlasAI");
            return Path.Combine(dir, "conversation-profile.json");
        }

        private static string ProtectIfNeeded(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            return value.StartsWith("dpapi:", StringComparison.Ordinal)
                ? value
                : SecretProtector.Protect(value);
        }
    }
}
