using System;
using System.Collections.Generic;
using Microsoft.Win32;

namespace AtlasAI
{
    internal static class WindowsShellIntegration
    {
        private const string ProtocolName = "atlasai";
        private const string MediaProgId = "AtlasAI.Media";

        internal static IReadOnlyList<string> MediaExtensions { get; } = new List<string>
        {
            ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".webm", ".m4v",
            ".mp3", ".flac", ".wav", ".m4a", ".aac", ".ogg", ".opus"
        };

        public static void Register(string appExePath)
        {
            if (string.IsNullOrWhiteSpace(appExePath)) throw new ArgumentException("appExePath is required", nameof(appExePath));

            // Custom protocol: atlasai://...
            using (var protoKey = Registry.CurrentUser.CreateSubKey($@"Software\\Classes\\{ProtocolName}"))
            {
                protoKey?.SetValue("", "URL:AtlasAI Protocol");
                protoKey?.SetValue("URL Protocol", "");
                using (var defaultIcon = protoKey?.CreateSubKey("DefaultIcon"))
                    defaultIcon?.SetValue("", $"\"{appExePath}\",0");

                using (var command = protoKey?.CreateSubKey(@"shell\open\command"))
                    command?.SetValue("", $"\"{appExePath}\" \"%1\"");
            }

            // Media ProgID + file association keys.
            using (var progIdKey = Registry.CurrentUser.CreateSubKey($@"Software\\Classes\\{MediaProgId}"))
            {
                progIdKey?.SetValue("", "AtlasAI Media File");
                using (var defaultIcon = progIdKey?.CreateSubKey("DefaultIcon"))
                    defaultIcon?.SetValue("", $"\"{appExePath}\",0");
                using (var command = progIdKey?.CreateSubKey(@"shell\open\command"))
                    command?.SetValue("", $"\"{appExePath}\" \"%1\"");
            }

            foreach (var ext in MediaExtensions)
            {
                if (string.IsNullOrWhiteSpace(ext) || !ext.StartsWith(".")) continue;
                using var extKey = Registry.CurrentUser.CreateSubKey($@"Software\\Classes\\{ext}");
                extKey?.SetValue("", MediaProgId);
            }
        }
    }
}
