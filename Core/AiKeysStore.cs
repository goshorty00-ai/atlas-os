using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using AtlasAI.Settings;

namespace AtlasAI.Core
{
    public static class AiKeysStore
    {
        private static readonly object IoLock = new();

        public static string AiKeysPath => Path.Combine(AtlasPaths.RoamingDir, "ai_keys.json");

        public static void UpgradeAll()
        {
            lock (IoLock)
            {
                try { UpgradeAiKeysJson(); } catch { }
                try { UpgradeLegacyKeyFiles(); } catch { }
            }
        }

        public static void UpgradeSettingsStoreKeysOnce()
        {
            lock (IoLock)
            {
                try { UpgradeSettingsStoreKeys(); } catch { }
            }
        }

        public static void SetPlaintextKey(string keyName, string plaintext)
        {
            keyName = NormalizeKeyName(keyName);
            if (string.IsNullOrWhiteSpace(keyName))
                return;

            plaintext = (plaintext ?? string.Empty).Trim();

            lock (IoLock)
            {
                if (string.IsNullOrWhiteSpace(plaintext))
                {
                    RemoveKeyInternal(keyName);
                    return;
                }

                PersistProtectedKey(keyName, plaintext);
            }
        }

        public static void RemoveKey(string keyName)
        {
            keyName = NormalizeKeyName(keyName);
            if (string.IsNullOrWhiteSpace(keyName))
                return;

            lock (IoLock)
            {
                RemoveKeyInternal(keyName);
            }
        }

        private static void UpgradeAiKeysJson()
        {
            // Merge any AI key files from legacy locations into AtlasAI (and ensure DPAPI protection).
            // Canonical storage is %APPDATA%\AtlasAI\ai_keys.json.

            var merged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            // Load canonical first (so it wins if duplicates exist).
            TryMergeFromAiKeysJson(AiKeysPath, merged, overwriteExisting: true);

            // Merge alternates (only fill missing keys; don't overwrite canonical values).
            foreach (var candidate in EnumerateAlternateAiKeysJsonCandidates())
            {
                if (string.IsNullOrWhiteSpace(candidate)) continue;
                TryMergeFromAiKeysJson(candidate, merged, overwriteExisting: false);
            }

            if (merged.Count == 0)
                return;

            var normalized = NormalizeAndProtect(merged, out var changed);
            if (!changed)
            {
                // Still ensure canonical exists (some installs only had keys in VisualAIAssistant).
                if (!File.Exists(AiKeysPath))
                {
                    var outJson = JsonSerializer.Serialize(normalized, new JsonSerializerOptions { WriteIndented = true });
                    SafeFile.WriteAllTextAtomic(AiKeysPath, outJson);
                }
                return;
            }

            var jsonOut = JsonSerializer.Serialize(normalized, new JsonSerializerOptions { WriteIndented = true });
            SafeFile.WriteAllTextAtomic(AiKeysPath, jsonOut);

            // Also protect any alternate copies to avoid plaintext secrets lingering.
            foreach (var candidate in EnumerateAlternateAiKeysJsonCandidates())
            {
                try
                {
                    if (!File.Exists(candidate)) continue;
                    SafeFile.WriteAllTextAtomic(candidate, jsonOut);
                }
                catch
                {
                }
            }
        }

        private static IEnumerable<string> EnumerateAlternateAiKeysJsonCandidates()
        {
            // These are legacy locations used by older builds/tools.
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            yield return Path.Combine(AtlasPaths.LocalDir, "ai_keys.json");
            yield return Path.Combine(appData, "VisualAIAssistant", "ai_keys.json");
            yield return Path.Combine(local, "VisualAIAssistant", "ai_keys.json");
        }

        private static void UpgradeSettingsStoreKeys()
        {
            var legacyKeys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                var settings = SettingsStore.Current;
                foreach (var pair in settings.AiProviderKeys)
                {
                    var keyName = NormalizeKeyName(pair.Key);
                    var plaintext = (pair.Value ?? string.Empty).Trim();
                    if (string.IsNullOrWhiteSpace(keyName) || string.IsNullOrWhiteSpace(plaintext))
                        continue;

                    legacyKeys[keyName] = plaintext;
                }
            }
            catch
            {
                return;
            }

            if (legacyKeys.Count == 0)
                return;

            var keys = LoadStoredKeys();
            var changed = false;

            foreach (var pair in legacyKeys)
            {
                if (keys.ContainsKey(pair.Key) && !string.IsNullOrWhiteSpace(keys[pair.Key]))
                    continue;

                keys[pair.Key] = SecretProtector.Protect(pair.Value);
                changed = true;
            }

            if (changed)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(AiKeysPath) ?? AtlasPaths.RoamingDir);
                var json = JsonSerializer.Serialize(keys, new JsonSerializerOptions { WriteIndented = true });
                SafeFile.WriteAllTextAtomic(AiKeysPath, json);
            }

            try
            {
                SettingsStore.Update(settings => settings.AiProviderKeys.Clear());
            }
            catch
            {
            }
        }

        private static void TryMergeFromAiKeysJson(string path, Dictionary<string, string> into, bool overwriteExisting)
        {
            try
            {
                if (!File.Exists(path)) return;
                var json = File.ReadAllText(path);
                var keys = JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                           ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                foreach (var kvp in keys)
                {
                    var name = (kvp.Key ?? "").Trim();
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    if (!overwriteExisting && into.ContainsKey(name)) continue;
                    into[name] = (kvp.Value ?? "");
                }
            }
            catch
            {
            }
        }

        private static Dictionary<string, string> NormalizeAndProtect(Dictionary<string, string> keys, out bool changed)
        {
            changed = false;
            var normalized = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var kvp in keys)
            {
                var name = (kvp.Key ?? "").Trim();
                if (string.IsNullOrWhiteSpace(name)) continue;

                var raw = (kvp.Value ?? "").Trim();
                if (string.IsNullOrWhiteSpace(raw))
                {
                    normalized[name] = "";
                    continue;
                }

                if (raw.StartsWith("dpapi:", StringComparison.Ordinal))
                {
                    normalized[name] = raw;
                    continue;
                }

                // Remove obvious whitespace artifacts.
                var cleaned = raw.Replace("\r", "").Replace("\n", "").Trim();
                normalized[name] = SecretProtector.Protect(cleaned);
                changed = true;
            }

            return normalized;
        }

        private static void UpgradeLegacyKeyFiles()
        {
            UpgradeLegacyKeyFilesInDir(AtlasPaths.RoamingDir);
            UpgradeLegacyKeyFilesInDir(AtlasPaths.LocalDir);

            try
            {
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                UpgradeLegacyKeyFilesInDir(Path.Combine(appData, "VisualAIAssistant"));
            }
            catch
            {
            }

            try
            {
                var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                UpgradeLegacyKeyFilesInDir(Path.Combine(local, "VisualAIAssistant"));
            }
            catch
            {
            }
        }

        private static void UpgradeLegacyKeyFilesInDir(string dir)
        {
            if (string.IsNullOrWhiteSpace(dir)) return;
            if (!Directory.Exists(dir)) return;

            UpgradeLegacyKeyFile(Path.Combine(dir, "openai_key.txt"), "openai");
            UpgradeLegacyKeyFile(Path.Combine(dir, "claude_key.txt"), "claude");
            UpgradeLegacyKeyFile(Path.Combine(dir, "gemini_key.txt"), "gemini");
            UpgradeSettingsTxt(Path.Combine(dir, "settings.txt"));
        }

        private static void UpgradeLegacyKeyFile(string path, string keyName)
        {
            if (!File.Exists(path))
                return;

            string raw;
            try { raw = (File.ReadAllText(path) ?? "").Trim(); }
            catch { return; }

            if (string.IsNullOrWhiteSpace(raw))
                return;

            var plaintext = raw.StartsWith("dpapi:", StringComparison.Ordinal)
                ? SecretProtector.UnprotectIfNeeded(raw)
                : raw;

            if (string.IsNullOrWhiteSpace(plaintext))
                return;

            try
            {
                PersistProtectedKey(keyName, plaintext);
            }
            catch
            {
            }

            // Ensure legacy file no longer contains plaintext.
            if (!raw.StartsWith("dpapi:", StringComparison.Ordinal))
            {
                try { SafeFile.WriteAllTextAtomic(path, SecretProtector.Protect(plaintext)); } catch { }
            }
        }

        private static void UpgradeSettingsTxt(string path)
        {
            if (!File.Exists(path))
                return;

            string raw;
            try { raw = (File.ReadAllText(path) ?? "").Trim(); }
            catch { return; }

            if (string.IsNullOrWhiteSpace(raw))
                return;

            // settings.txt historically stored a single plaintext key.
            // If it's already protected, do nothing.
            if (raw.StartsWith("dpapi:", StringComparison.Ordinal))
                return;

            // Infer provider based on key prefix.
            var keyName = raw.StartsWith("sk-ant-", StringComparison.Ordinal)
                ? "claude"
                : raw.StartsWith("sk-", StringComparison.Ordinal)
                    ? "openai"
                    : "";

            if (string.IsNullOrWhiteSpace(keyName))
                return;

            try
            {
                PersistProtectedKey(keyName, raw);
            }
            catch
            {
            }

            try { SafeFile.WriteAllTextAtomic(path, SecretProtector.Protect(raw)); } catch { }
        }

        private static void PersistProtectedKey(string keyName, string plaintext)
        {
            var keys = LoadStoredKeys();

            keys[keyName] = SecretProtector.Protect(plaintext);
            Directory.CreateDirectory(Path.GetDirectoryName(AiKeysPath) ?? AtlasPaths.RoamingDir);
            var json = JsonSerializer.Serialize(keys, new JsonSerializerOptions { WriteIndented = true });
            SafeFile.WriteAllTextAtomic(AiKeysPath, json);
        }

        private static void RemoveKeyInternal(string keyName)
        {
            var keys = LoadStoredKeys();
            if (!keys.Remove(keyName))
                return;

            Directory.CreateDirectory(Path.GetDirectoryName(AiKeysPath) ?? AtlasPaths.RoamingDir);
            var json = JsonSerializer.Serialize(keys, new JsonSerializerOptions { WriteIndented = true });
            SafeFile.WriteAllTextAtomic(AiKeysPath, json);
        }

        private static Dictionary<string, string> LoadStoredKeys()
        {
            var keys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!File.Exists(AiKeysPath))
                return keys;

            try
            {
                var existingJson = File.ReadAllText(AiKeysPath);
                return JsonSerializer.Deserialize<Dictionary<string, string>>(existingJson) ?? keys;
            }
            catch
            {
                return keys;
            }
        }

        private static string NormalizeKeyName(string? keyName)
        {
            return (keyName ?? string.Empty).Trim().ToLowerInvariant();
        }

        public static bool TryGetPlaintextKey(string keyName, out string plaintext)
        {
            plaintext = "";
            keyName = NormalizeKeyName(keyName);
            if (string.IsNullOrWhiteSpace(keyName))
                return false;

            // Ensure keys are normalized/upgraded before reading.
            try { UpgradeAll(); } catch { }

            lock (IoLock)
            {
                try
                {
                    if (!File.Exists(AiKeysPath))
                        return false;

                    var json = File.ReadAllText(AiKeysPath);
                    var keys = JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                            ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    if (!keys.TryGetValue(keyName, out var raw))
                        return false;

                    raw = (raw ?? "").Trim();
                    if (string.IsNullOrWhiteSpace(raw))
                        return false;

                    var unprotected = raw.StartsWith("dpapi:", StringComparison.Ordinal)
                        ? SecretProtector.UnprotectIfNeeded(raw)
                        : raw;

                    unprotected = (unprotected ?? "").Trim();
                    if (string.IsNullOrWhiteSpace(unprotected))
                        return false;

                    plaintext = unprotected;
                    return true;
                }
                catch
                {
                    return false;
                }
            }
        }
    }
}
