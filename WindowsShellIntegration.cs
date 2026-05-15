using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace AtlasAI
{
    internal static class WindowsShellIntegration
    {
        private const string ProtocolName = "atlasai";
        private const string VideoProgId = "AtlasAI.Video";
        private const string MusicProgId = "AtlasAI.Music";

        internal static IReadOnlyList<string> VideoExtensions { get; } = new List<string>
        {
            ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".webm", ".m4v", ".flv", ".ts", ".m2ts", ".mpg", ".mpeg"
        };

        internal static IReadOnlyList<string> MusicExtensions { get; } = new List<string>
        {
            ".mp3", ".flac", ".wav", ".m4a", ".aac", ".ogg", ".opus", ".wma"
        };

        internal static IReadOnlySet<string> MediaExtensions { get; } = new HashSet<string>(
            new List<string>
            {
                ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".webm", ".m4v", ".flv", ".ts", ".m2ts", ".mpg", ".mpeg",
                ".mp3", ".flac", ".wav", ".m4a", ".aac", ".ogg", ".opus", ".wma"
            },
            StringComparer.OrdinalIgnoreCase);

        public static void Register(string appExePath)
        {
            if (string.IsNullOrWhiteSpace(appExePath)) throw new ArgumentException("appExePath is required", nameof(appExePath));

            // Custom protocol: atlasai://...
            using (var protoKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{ProtocolName}"))
            {
                protoKey?.SetValue("", "URL:AtlasAI Protocol");
                protoKey?.SetValue("URL Protocol", "");
                using (var defaultIcon = protoKey?.CreateSubKey("DefaultIcon"))
                    defaultIcon?.SetValue("", $"\"{appExePath}\",0");
                using (var command = protoKey?.CreateSubKey(@"shell\open\command"))
                    command?.SetValue("", $"\"{appExePath}\" \"%1\"");
            }

            // Video ProgID — shown as "Atlas Media Player" in Open With dialogs
            using (var progIdKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{VideoProgId}"))
            {
                progIdKey?.SetValue("", "Atlas Media Player");
                progIdKey?.SetValue("FriendlyTypeName", "Atlas Media Player");
                using (var defaultIcon = progIdKey?.CreateSubKey("DefaultIcon"))
                    defaultIcon?.SetValue("", $"\"{appExePath}\",0");
                using (var command = progIdKey?.CreateSubKey(@"shell\open\command"))
                    command?.SetValue("", $"\"{appExePath}\" \"%1\"");
            }

            // Music ProgID — shown as "Atlas Music" in Open With dialogs
            using (var progIdKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{MusicProgId}"))
            {
                progIdKey?.SetValue("", "Atlas Music");
                progIdKey?.SetValue("FriendlyTypeName", "Atlas Music");
                using (var defaultIcon = progIdKey?.CreateSubKey("DefaultIcon"))
                    defaultIcon?.SetValue("", $"\"{appExePath}\",0");
                using (var command = progIdKey?.CreateSubKey(@"shell\open\command"))
                    command?.SetValue("", $"\"{appExePath}\" \"%1\"");
            }

            foreach (var ext in VideoExtensions)
            {
                if (string.IsNullOrWhiteSpace(ext) || !ext.StartsWith(".")) continue;
                using var extKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{ext}");
                extKey?.SetValue("", VideoProgId);
            }

            foreach (var ext in MusicExtensions)
            {
                if (string.IsNullOrWhiteSpace(ext) || !ext.StartsWith(".")) continue;
                using var extKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{ext}");
                extKey?.SetValue("", MusicProgId);
            }

            // Notify Windows shell to refresh file type associations
            try { SHChangeNotify(); } catch { }
        }

        private static void SHChangeNotify()
        {
            // Signal Windows to flush its file association cache
            SHChangeNotifyNative(0x08000000, 0x0000, IntPtr.Zero, IntPtr.Zero);
        }

        [DllImport("shell32.dll", EntryPoint = "SHChangeNotify")]
        private static extern void SHChangeNotifyNative(int wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);
    }
}
