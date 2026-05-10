using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace AtlasAI.Brain
{
    public enum WorkspaceMode
    {
        Unknown,
        Development,
        Creative,
        Gaming,
        Media,
        Maintenance
    }

    public static class WorkspaceEngine
    {
        private static readonly object Sync = new();
        private static WorkspaceMode _active = WorkspaceMode.Unknown;
        private static DateTime _lastSample = DateTime.MinValue;

        public static WorkspaceMode ActiveWorkspace
        {
            get
            {
                lock (Sync)
                {
                    return _active;
                }
            }
        }

        public static void Refresh(string workspacePath)
        {
            var now = DateTime.UtcNow;
            if ((now - _lastSample).TotalSeconds < 5)
                return;

            WorkspaceMode inferred;
            try
            {
                var title = GetActiveWindowTitle();
                var processes = GetInterestingProcesses();
                var files = GetRecentFiles(workspacePath);
                var hour = DateTime.Now.Hour;

                inferred = InferMode(title, processes, files, hour);
            }
            catch
            {
                inferred = WorkspaceMode.Unknown;
            }

            lock (Sync)
            {
                _active = inferred;
                _lastSample = now;
            }
        }

        private static WorkspaceMode InferMode(string? title, IReadOnlyList<string> processes, IReadOnlyList<string> files, int hour)
        {
            var lowerTitle = title?.ToLowerInvariant() ?? "";

            if (IsDevProcess(processes) || IsDevTitle(lowerTitle) || HasDevFiles(files))
                return WorkspaceMode.Development;

            if (IsCreativeProcess(processes) || HasCreativeFiles(files))
                return WorkspaceMode.Creative;

            if (IsGameProcess(processes) || IsGameTitle(lowerTitle))
                return WorkspaceMode.Gaming;

            if (IsMediaProcess(processes) || HasMediaFiles(files))
                return WorkspaceMode.Media;

            if (IsMaintenanceProcess(processes) || IsMaintenanceTitle(lowerTitle) || HasMaintenanceFiles(files) || IsMaintenanceHour(hour))
                return WorkspaceMode.Maintenance;

            return WorkspaceMode.Unknown;
        }

        private static bool IsDevProcess(IReadOnlyList<string> processes)
        {
            var devNames = new[]
            {
                "devenv", "code", "code - insiders", "rider64", "pycharm64",
                "clion64", "idea64", "vims", "notepad++", "sublime_text", "atom"
            };
            return ContainsAny(processes, devNames);
        }

        private static bool IsDevTitle(string title)
        {
            if (string.IsNullOrEmpty(title)) return false;
            if (title.Contains(".sln") || title.Contains(".csproj") || title.Contains(".cs") || title.Contains(".ts") || title.Contains(".js"))
                return true;
            if (title.Contains("visual studio") || title.Contains("visual code") || title.Contains("vs code"))
                return true;
            return false;
        }

        private static bool HasDevFiles(IReadOnlyList<string> files)
        {
            var devExt = new[]
            {
                ".sln", ".csproj", ".cs", ".ts", ".tsx", ".js", ".jsx", ".py", ".rb", ".go", ".rs", ".cpp", ".h", ".json"
            };
            return HasExtension(files, devExt);
        }

        private static bool IsCreativeProcess(IReadOnlyList<string> processes)
        {
            var names = new[]
            {
                "photoshop", "illustrator", "afterfx", "premiere", "resolve", "blender", "krita", "affinityphoto", "affinitydesigner"
            };
            return ContainsAny(processes, names);
        }

        private static bool HasCreativeFiles(IReadOnlyList<string> files)
        {
            var ext = new[]
            {
                ".psd", ".ai", ".svg", ".blend", ".kra", ".xcf", ".afphoto", ".afdesign"
            };
            return HasExtension(files, ext);
        }

        private static bool IsGameProcess(IReadOnlyList<string> processes)
        {
            var names = new[]
            {
                "steam", "epicgameslauncher", "battle.net", "riotclientservices", "eldenring", "cs2", "valorant", "fortnite"
            };
            return ContainsAny(processes, names);
        }

        private static bool IsGameTitle(string title)
        {
            if (string.IsNullOrEmpty(title)) return false;
            if (title.Contains("steam") || title.Contains("epic games") || title.Contains("directx") || title.Contains("unity"))
                return true;
            return false;
        }

        private static bool IsMediaProcess(IReadOnlyList<string> processes)
        {
            var names = new[]
            {
                "vlc", "mpc-hc", "mpc-be", "spotify", "foobar2000", "netflix", "primevideo"
            };
            return ContainsAny(processes, names);
        }

        private static bool HasMediaFiles(IReadOnlyList<string> files)
        {
            var ext = new[]
            {
                ".mp4", ".mkv", ".avi", ".flac", ".mp3", ".wav"
            };
            return HasExtension(files, ext);
        }

        private static bool IsMaintenanceProcess(IReadOnlyList<string> processes)
        {
            var names = new[]
            {
                "taskmgr", "procmon", "procexp", "resmon", "defrag", "dfrgui", "cleanmgr", "autoruns"
            };
            return ContainsAny(processes, names);
        }

        private static bool IsMaintenanceTitle(string title)
        {
            if (string.IsNullOrEmpty(title)) return false;
            if (title.Contains("task manager") || title.Contains("resource monitor") || title.Contains("event viewer") || title.Contains("services"))
                return true;
            return false;
        }

        private static bool HasMaintenanceFiles(IReadOnlyList<string> files)
        {
            var ext = new[]
            {
                ".log", ".evtx"
            };
            return HasExtension(files, ext);
        }

        private static bool IsMaintenanceHour(int hour)
        {
            return hour >= 1 && hour <= 5;
        }

        private static bool ContainsAny(IReadOnlyList<string> processes, string[] names)
        {
            if (processes == null || processes.Count == 0)
                return false;
            foreach (var p in processes)
            {
                if (string.IsNullOrWhiteSpace(p)) continue;
                var lower = p.ToLowerInvariant();
                foreach (var n in names)
                {
                    if (lower.Contains(n))
                        return true;
                }
            }
            return false;
        }

        private static bool HasExtension(IReadOnlyList<string> files, string[] extensions)
        {
            if (files == null || files.Count == 0)
                return false;
            foreach (var f in files)
            {
                if (string.IsNullOrWhiteSpace(f)) continue;
                var ext = Path.GetExtension(f);
                if (string.IsNullOrEmpty(ext)) continue;
                foreach (var e in extensions)
                {
                    if (ext.Equals(e, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
            return false;
        }

        private static string? GetActiveWindowTitle()
        {
            try
            {
                var handle = GetForegroundWindow();
                if (handle == IntPtr.Zero)
                    return null;
                var buffer = new char[512];
                var length = GetWindowText(handle, buffer, buffer.Length);
                if (length <= 0)
                    return null;
                return new string(buffer, 0, length);
            }
            catch
            {
                return null;
            }
        }

        private static IReadOnlyList<string> GetInterestingProcesses()
        {
            var list = new List<string>();
            try
            {
                foreach (var p in Process.GetProcesses())
                {
                    try
                    {
                        if (p.MainWindowHandle == IntPtr.Zero && !p.ProcessName.Equals("explorer", StringComparison.OrdinalIgnoreCase))
                            continue;
                        list.Add(p.ProcessName);
                    }
                    catch
                    {
                    }
                }
            }
            catch
            {
            }
            return list;
        }

        private static IReadOnlyList<string> GetRecentFiles(string workspacePath)
        {
            var list = new List<string>();
            try
            {
                if (Directory.Exists(workspacePath))
                {
                    var files = Directory.EnumerateFiles(workspacePath, "*.*", SearchOption.AllDirectories)
                        .Take(64);
                    list.AddRange(files);
                }
            }
            catch
            {
            }
            return list;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr hWnd, char[] text, int count);
    }
}
