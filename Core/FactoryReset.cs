using System;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace AtlasAI.Core
{
    public static class FactoryReset
    {
        private static readonly string RoamingDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AtlasAI");

        private static readonly string LocalDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AtlasAI");

        private static readonly string TempDir = Path.Combine(Path.GetTempPath(), "AtlasAI");

        private static readonly string ResetFlagPath = Path.Combine(RoamingDir, "factory_reset.flag");

        public static void RequestOnNextStart()
        {
            try
            {
                Directory.CreateDirectory(RoamingDir);
                File.WriteAllText(ResetFlagPath, DateTime.UtcNow.ToString("O"));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FactoryReset] Failed to write reset flag: {ex.Message}");
            }
        }

        public static bool IsPending()
        {
            try { return File.Exists(ResetFlagPath); }
            catch { return false; }
        }

        /// <summary>
        /// Runs the factory reset if a pending flag exists.
        /// Returns true if a reset was attempted (whether it succeeded or not).
        /// </summary>
        public static bool TryRunPendingReset(out string statusMessage)
        {
            statusMessage = "";

            if (!IsPending())
                return false;

            // If another instance is still running, don't wipe state underneath it.
            try
            {
                var currentPid = Process.GetCurrentProcess().Id;
                var other = Process.GetProcessesByName("Atlas_v2")
                    .Concat(Process.GetProcessesByName("Atlas"))
                    .Where(p => p.Id != currentPid)
                    .FirstOrDefault();

                if (other != null)
                {
                    statusMessage = "Factory reset is pending, but another Atlas process is still running. Close it and restart Atlas to complete the reset.";
                    return true;
                }
            }
            catch
            {
            }

            try
            {
                PerformResetCore();
                statusMessage = "Factory reset complete. Addons, watch history, settings, and saved integration keys were cleared.";
                TryDeleteFlag();
            }
            catch (Exception ex)
            {
                statusMessage = $"Factory reset failed: {ex.Message}";
                // Keep the flag so user can retry on next start.
            }

            return true;
        }

        private static void PerformResetCore()
        {
            DeleteDirectorySafe(RoamingDir);
            DeleteDirectorySafe(LocalDir);
            DeleteDirectorySafe(TempDir);

            try { Directory.CreateDirectory(RoamingDir); } catch { }
        }

        private static void DeleteDirectorySafe(string path)
        {
            try
            {
                if (!Directory.Exists(path))
                    return;

                // Best-effort delete; swallow failures for individual files.
                try
                {
                    Directory.Delete(path, recursive: true);
                    return;
                }
                catch
                {
                }

                try
                {
                    foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                    {
                        try { File.SetAttributes(file, FileAttributes.Normal); } catch { }
                        try { File.Delete(file); } catch { }
                    }
                }
                catch
                {
                }

                try
                {
                    foreach (var dir in Directory.EnumerateDirectories(path, "*", SearchOption.AllDirectories)
                                 .OrderByDescending(d => d.Length))
                    {
                        try { Directory.Delete(dir, recursive: false); } catch { }
                    }
                }
                catch
                {
                }

                try { Directory.Delete(path, recursive: false); } catch { }
            }
            catch
            {
            }
        }

        private static void TryDeleteFlag()
        {
            try { if (File.Exists(ResetFlagPath)) File.Delete(ResetFlagPath); } catch { }
        }
    }
}
