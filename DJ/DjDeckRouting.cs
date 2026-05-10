using System;

namespace AtlasAI.DJ
{
    internal static class DjDeckRouting
    {
        public static string? Normalize(string? deckLabel)
        {
            var normalized = (deckLabel ?? string.Empty).Trim().ToUpperInvariant();
            return normalized switch
            {
                "A" => "A",
                "B" => "B",
                _ => null,
            };
        }

        public static bool IsSupported(string? deckLabel)
        {
            return Normalize(deckLabel) != null;
        }

        public static string Opposite(string deckLabel)
        {
            return Normalize(deckLabel) == "A" ? "B" : "A";
        }
    }
}