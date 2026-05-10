using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Win32;
using System.Text.RegularExpressions;

namespace AtlasAI.SystemControl
{
    public sealed class DiscoveredGame
    {
        public string Name { get; init; } = "";
        public string ExecutablePath { get; init; } = "";
        public string Platform { get; init; } = "";
        public int? SteamAppId { get; init; }
    }

    public static class GameDiscoveryService
    {
        public static List<DiscoveredGame> DiscoverInstalledGames()
        {
            var results = new List<DiscoveredGame>();

            try
            {
                results.AddRange(DiscoverSteamGames());
            }
            catch
            {
            }

            try
            {
                results.AddRange(DiscoverEpicGames());
            }
            catch
            {
            }

            try
            {
                results.AddRange(DiscoverFromInstalledApps());
            }
            catch
            {
            }

            return results
                .Where(g => !string.IsNullOrWhiteSpace(g.Name) && !string.IsNullOrWhiteSpace(g.ExecutablePath))
                .GroupBy(g => g.ExecutablePath, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static string? TryGetSteamRootPath()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
                var steamPath = key?.GetValue("SteamPath")?.ToString();
                if (!string.IsNullOrWhiteSpace(steamPath) && Directory.Exists(steamPath))
                    return steamPath;
            }
            catch
            {
            }

            var defaults = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam"),
                @"C:\Program Files (x86)\Steam",
                @"C:\Steam"
            };

            foreach (var p in defaults)
            {
                if (Directory.Exists(p))
                    return p;
            }

            return null;
        }

        public static string? TryGetSteamCoverPath(int appId)
        {
            if (appId <= 0) return null;
            var steamRoot = TryGetSteamRootPath();
            if (string.IsNullOrWhiteSpace(steamRoot)) return null;

            var cache = Path.Combine(steamRoot, "appcache", "librarycache");
            var candidates = new[]
            {
                Path.Combine(cache, $"{appId}_library_600x900.jpg"),
                Path.Combine(cache, $"{appId}_library_600x900.png"),
                Path.Combine(cache, $"{appId}_library_hero.jpg"),
                Path.Combine(cache, $"{appId}_library_hero.png"),
                Path.Combine(cache, $"{appId}_header.jpg"),
                Path.Combine(cache, $"{appId}_header.png"),
                Path.Combine(cache, $"{appId}_library_capsule.jpg"),
                Path.Combine(cache, $"{appId}_library_capsule.png")
            };

            foreach (var c in candidates)
            {
                if (File.Exists(c)) return c;
            }

            return null;
        }

        private static List<DiscoveredGame> DiscoverSteamGames()
        {
            var steamRoot = TryGetSteamRootPath();
            if (string.IsNullOrWhiteSpace(steamRoot)) return new List<DiscoveredGame>();

            var libraries = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                steamRoot
            };

            var vdfPath = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
            if (File.Exists(vdfPath))
            {
                try
                {
                    var text = File.ReadAllText(vdfPath);
                    foreach (Match m in Regex.Matches(text, "\"path\"\\s*\"(?<p>[^\"]+)\"", RegexOptions.IgnoreCase))
                    {
                        var p = m.Groups["p"].Value.Replace(@"\\", @"\").Trim();
                        if (!string.IsNullOrWhiteSpace(p) && Directory.Exists(p))
                            libraries.Add(p);
                    }
                }
                catch
                {
                }
            }

            var games = new List<DiscoveredGame>();

            foreach (var lib in libraries)
            {
                var steamApps = Path.Combine(lib, "steamapps");
                if (!Directory.Exists(steamApps)) continue;

                string[] manifests;
                try
                {
                    manifests = Directory.GetFiles(steamApps, "appmanifest_*.acf", SearchOption.TopDirectoryOnly);
                }
                catch
                {
                    continue;
                }

                foreach (var manifest in manifests)
                {
                    try
                    {
                        var acf = File.ReadAllText(manifest);
                        var appId = TryParseAcfInt(acf, "appid");
                        var name = TryParseAcfString(acf, "name");
                        var installDirName = TryParseAcfString(acf, "installdir");
                        if (appId <= 0) continue;
                        if (string.IsNullOrWhiteSpace(installDirName)) continue;

                        var installDir = Path.Combine(steamApps, "common", installDirName);
                        if (!Directory.Exists(installDir)) continue;

                        var exe = FindBestExe(installDir, installDirName, name);
                        if (string.IsNullOrWhiteSpace(exe) || !File.Exists(exe)) continue;

                        games.Add(new DiscoveredGame
                        {
                            Name = string.IsNullOrWhiteSpace(name) ? installDirName : name,
                            ExecutablePath = exe,
                            Platform = "Steam",
                            SteamAppId = appId
                        });
                    }
                    catch
                    {
                    }
                }
            }

            return games;
        }

        private static List<DiscoveredGame> DiscoverEpicGames()
        {
            var manifestsRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "Epic", "EpicGamesLauncher", "Data", "Manifests");

            if (!Directory.Exists(manifestsRoot)) return new List<DiscoveredGame>();

            string[] items;
            try
            {
                items = Directory.GetFiles(manifestsRoot, "*.item", SearchOption.TopDirectoryOnly);
            }
            catch
            {
                return new List<DiscoveredGame>();
            }

            var results = new List<DiscoveredGame>();

            foreach (var item in items)
            {
                try
                {
                    var json = File.ReadAllText(item);
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;

                    var displayName = root.TryGetProperty("DisplayName", out var dn) ? dn.GetString() ?? "" : "";
                    var installLocation = root.TryGetProperty("InstallLocation", out var il) ? il.GetString() ?? "" : "";
                    var launchExe = root.TryGetProperty("LaunchExecutable", out var le) ? le.GetString() ?? "" : "";

                    if (string.IsNullOrWhiteSpace(displayName)) continue;
                    if (string.IsNullOrWhiteSpace(installLocation) || !Directory.Exists(installLocation)) continue;

                    var exePath = "";
                    if (!string.IsNullOrWhiteSpace(launchExe))
                    {
                        exePath = Path.IsPathRooted(launchExe) ? launchExe : Path.Combine(installLocation, launchExe);
                    }

                    if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
                    {
                        exePath = FindBestExe(installLocation, displayName, displayName);
                    }

                    if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath)) continue;

                    results.Add(new DiscoveredGame
                    {
                        Name = displayName,
                        ExecutablePath = exePath,
                        Platform = "Epic",
                        SteamAppId = null
                    });
                }
                catch
                {
                }
            }

            return results;
        }

        private static List<DiscoveredGame> DiscoverFromInstalledApps()
        {
            var apps = InstalledAppsManager.Instance.GetAllApps();
            if (apps == null || apps.Count == 0) return new List<DiscoveredGame>();

            var blockedTokens = new[]
            {
                "unreal", "unity", "editor", "engine", "launcher", "installer", "setup",
                "sdk", "redistributable", "directx", "vcredist", "tool", "server"
            };

            var results = new List<DiscoveredGame>();

            foreach (var app in apps)
            {
                if (app == null) continue;
                var name = app.Name ?? "";
                var exe = app.ExecutablePath ?? "";
                if (string.IsNullOrWhiteSpace(name)) continue;
                if (string.IsNullOrWhiteSpace(exe)) continue;
                if (!exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) continue;
                if (!File.Exists(exe)) continue;

                if (blockedTokens.Any(t => name.Contains(t, StringComparison.OrdinalIgnoreCase)))
                    continue;

                var lowerExe = exe.ToLowerInvariant();
                var looksGameLike =
                    lowerExe.Contains("\\steamapps\\common\\") ||
                    lowerExe.Contains("\\epic games\\") ||
                    lowerExe.Contains("\\gog galaxy\\games\\") ||
                    lowerExe.Contains("\\ea games\\") ||
                    lowerExe.Contains("\\origin games\\") ||
                    lowerExe.Contains("\\ubisoft game launcher\\games\\") ||
                    lowerExe.Contains("\\battle.net\\") ||
                    lowerExe.Contains("\\games\\");

                if (!looksGameLike)
                    continue;

                results.Add(new DiscoveredGame
                {
                    Name = name,
                    ExecutablePath = exe,
                    Platform = string.IsNullOrWhiteSpace(app.Source) ? "Windows" : app.Source,
                    SteamAppId = null
                });
            }

            return results;
        }

        private static int TryParseAcfInt(string text, string key)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0;
            if (string.IsNullOrWhiteSpace(key)) return 0;

            var m = Regex.Match(text, $"\"{Regex.Escape(key)}\"\\s*\"(?<v>\\d+)\"", RegexOptions.IgnoreCase);
            if (!m.Success) return 0;
            return int.TryParse(m.Groups["v"].Value, out var v) ? v : 0;
        }

        private static string TryParseAcfString(string text, string key)
        {
            if (string.IsNullOrWhiteSpace(text)) return "";
            if (string.IsNullOrWhiteSpace(key)) return "";

            var m = Regex.Match(text, $"\"{Regex.Escape(key)}\"\\s*\"(?<v>[^\"]*)\"", RegexOptions.IgnoreCase);
            return m.Success ? (m.Groups["v"].Value ?? "") : "";
        }

        private static string? FindBestExe(string installDir, string installDirName, string? displayName)
        {
            if (string.IsNullOrWhiteSpace(installDir) || !Directory.Exists(installDir)) return null;

            var candidates = new List<string>();
            try
            {
                candidates.AddRange(Directory.GetFiles(installDir, "*.exe", SearchOption.TopDirectoryOnly));
            }
            catch
            {
            }

            var commonSubDirs = new[]
            {
                "bin", "Bin", "Binaries", "Binaries\\Win64", "Binaries\\Win32", "Win64", "Win32", "Game", "Launcher", "Program"
            };

            foreach (var sub in commonSubDirs)
            {
                var p = Path.Combine(installDir, sub);
                if (!Directory.Exists(p)) continue;
                try
                {
                    candidates.AddRange(Directory.GetFiles(p, "*.exe", SearchOption.TopDirectoryOnly));
                }
                catch
                {
                }
            }

            candidates = candidates
                .Where(e => !IsIgnoredExe(e))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (candidates.Count == 0) return null;

            var nameTokens = new List<string>();
            if (!string.IsNullOrWhiteSpace(installDirName)) nameTokens.Add(installDirName);
            if (!string.IsNullOrWhiteSpace(displayName)) nameTokens.Add(displayName);

            foreach (var token in nameTokens.Where(t => !string.IsNullOrWhiteSpace(t)))
            {
                var exact = candidates.FirstOrDefault(e =>
                    string.Equals(Path.GetFileNameWithoutExtension(e), token, StringComparison.OrdinalIgnoreCase));
                if (exact != null) return exact;
            }

            foreach (var token in nameTokens.Where(t => !string.IsNullOrWhiteSpace(t)))
            {
                var partial = candidates.FirstOrDefault(e =>
                    Path.GetFileNameWithoutExtension(e).Contains(token, StringComparison.OrdinalIgnoreCase));
                if (partial != null) return partial;
            }

            return candidates
                .Select(e => new { Path = e, Size = SafeFileSize(e) })
                .OrderByDescending(x => x.Size)
                .First()
                .Path;
        }

        private static bool IsIgnoredExe(string exePath)
        {
            var name = Path.GetFileName(exePath);
            if (string.IsNullOrWhiteSpace(name)) return true;

            if (name.Contains("unins", StringComparison.OrdinalIgnoreCase)) return true;
            if (name.Contains("uninstall", StringComparison.OrdinalIgnoreCase)) return true;
            if (name.Contains("setup", StringComparison.OrdinalIgnoreCase)) return true;
            if (name.Contains("installer", StringComparison.OrdinalIgnoreCase)) return true;
            if (name.Contains("crash", StringComparison.OrdinalIgnoreCase)) return true;
            if (name.Contains("vcredist", StringComparison.OrdinalIgnoreCase)) return true;
            if (name.Contains("dxsetup", StringComparison.OrdinalIgnoreCase)) return true;
            if (name.Contains("dotnet", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static long SafeFileSize(string path)
        {
            try
            {
                return new FileInfo(path).Length;
            }
            catch
            {
                return 0;
            }
        }
    }
}
