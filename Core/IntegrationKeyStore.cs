using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace AtlasAI.Core
{
    public static class IntegrationKeyStore
    {
        private static readonly object Gate = new();
        private static readonly string KeysPath = Path.Combine(AtlasPaths.RoamingDir, "integration_keys.json");

        public static Dictionary<string, string> GetAllDecrypted()
        {
            lock (Gate)
            {
                var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                var raw = LoadRaw();
                foreach (var kv in raw)
                {
                    result[kv.Key] = SecretProtector.UnprotectIfNeeded(kv.Value ?? "");
                }
                return result;
            }
        }

        public static string GetDecrypted(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return "";
            lock (Gate)
            {
                var raw = LoadRaw();
                return raw.TryGetValue(key, out var v) ? SecretProtector.UnprotectIfNeeded(v ?? "") : "";
            }
        }

        public static void SetProtected(string key, string plaintext)
        {
            if (string.IsNullOrWhiteSpace(key)) return;

            try
            {
                lock (Gate)
                {
                    var dir = Path.GetDirectoryName(KeysPath);
                    if (!string.IsNullOrWhiteSpace(dir))
                        Directory.CreateDirectory(dir);

                    var raw = LoadRaw();
                    raw[key] = SecretProtector.Protect(plaintext ?? "");

                    var json = JsonSerializer.Serialize(raw, new JsonSerializerOptions { WriteIndented = true });
                    var tmp = KeysPath + ".tmp";
                    File.WriteAllText(tmp, json);
                    try
                    {
                        if (File.Exists(KeysPath))
                            File.Replace(tmp, KeysPath, destinationBackupFileName: null);
                        else
                            File.Move(tmp, KeysPath);
                    }
                    catch
                    {
                        File.Copy(tmp, KeysPath, overwrite: true);
                        try { File.Delete(tmp); } catch { }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[IntegrationKeyStore] Failed to save key '{key}': {ex.Message}");
            }
        }

        public static void Delete(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return;

            try
            {
                lock (Gate)
                {
                    var raw = LoadRaw();
                    if (!raw.Remove(key)) return;

                    var dir = Path.GetDirectoryName(KeysPath);
                    if (!string.IsNullOrWhiteSpace(dir))
                        Directory.CreateDirectory(dir);

                    var json = JsonSerializer.Serialize(raw, new JsonSerializerOptions { WriteIndented = true });
                    var tmp = KeysPath + ".tmp";
                    File.WriteAllText(tmp, json);
                    try
                    {
                        if (File.Exists(KeysPath))
                            File.Replace(tmp, KeysPath, destinationBackupFileName: null);
                        else
                            File.Move(tmp, KeysPath);
                    }
                    catch
                    {
                        File.Copy(tmp, KeysPath, overwrite: true);
                        try { File.Delete(tmp); } catch { }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[IntegrationKeyStore] Failed to delete key '{key}': {ex.Message}");
            }
        }

        private static Dictionary<string, string> LoadRaw()
        {
            try
            {
                if (!File.Exists(KeysPath))
                    return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                var json = File.ReadAllText(KeysPath);
                var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                if (dict == null || dict.Count == 0)
                    return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                var normalized = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var kv in dict)
                {
                    if (string.IsNullOrWhiteSpace(kv.Key)) continue;
                    normalized[kv.Key] = kv.Value ?? "";
                }
                return normalized;
            }
            catch
            {
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }
        }
    }
}
