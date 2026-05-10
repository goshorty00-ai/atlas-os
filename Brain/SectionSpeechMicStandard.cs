using System;
using System.Collections.Generic;

namespace AtlasAI.Brain
{
    public static class SectionSpeechMicStandard
    {
        private static readonly object Gate = new();

        private static readonly Dictionary<string, bool> SpeechEnabledBySection = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Chat"] = true,
            ["Media"] = true,
            ["Speech"] = true,
            ["SmartHome"] = true,
            ["DJ"] = true,
            ["Downloads"] = true,
            ["API"] = true,
            ["Internet"] = true,
            ["Email"] = true,
            ["FileExplorer"] = true,
            ["Security"] = true,
            ["Create"] = true,
            ["AiChef"] = true,
            ["Code"] = true,
        };

        public static bool IsCodeSection(string? sectionKey)
        {
            return string.Equals(Normalize(sectionKey), "Code", StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsSpeechEnabled(string? sectionKey)
        {
            lock (Gate)
            {
                return SpeechEnabledBySection.TryGetValue(Normalize(sectionKey), out bool enabled)
                    ? enabled
                    : true;
            }
        }

        public static bool ToggleSpeech(string? sectionKey)
        {
            lock (Gate)
            {
                string key = Normalize(sectionKey);
                bool current = SpeechEnabledBySection.TryGetValue(key, out bool enabled) ? enabled : true;
                bool next = !current;
                SpeechEnabledBySection[key] = next;
                return next;
            }
        }

        public static bool IsSpeechWired(string? sectionKey)
        {
            string key = Normalize(sectionKey);

            return string.Equals(key, "Chat", StringComparison.OrdinalIgnoreCase)
                || string.Equals(key, "Speech", StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsMicWired(string? sectionKey)
        {
            string key = Normalize(sectionKey);

            return string.Equals(key, "Chat", StringComparison.OrdinalIgnoreCase)
                || string.Equals(key, "Speech", StringComparison.OrdinalIgnoreCase)
                || string.Equals(key, "Email", StringComparison.OrdinalIgnoreCase)
                || string.Equals(key, "Code", StringComparison.OrdinalIgnoreCase);
        }

        private static string Normalize(string? sectionKey)
        {
            string value = (sectionKey ?? string.Empty).Trim();
            if (value.Length == 0)
                return "Chat";

            return value switch
            {
                "Smart Home" => "SmartHome",
                "Media Centre" => "Media",
                "File Explorer" => "FileExplorer",
                _ => value,
            };
        }
    }
}