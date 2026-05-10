using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace AtlasAI.Core
{
    internal static class WatchHistoryStore
    {
        private const int MaxItems = 40;
        private static readonly object LockObj = new();
        private static readonly string StorePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AtlasAI",
            "watch_history.json");

        private class Entry
        {
            public string FilePath { get; set; } = "";
            public string Title { get; set; } = "";
            public string Type { get; set; } = "";
            public string? CoverUrl { get; set; }
            public string? BackdropUrl { get; set; }
            public double PositionSeconds { get; set; }
            public double DurationSeconds { get; set; }
            public DateTime LastWatchedUtc { get; set; }
        }

        public static void AddOrUpdate(string filePath, string title, string type, string? coverUrl, string? backdropUrl)
        {
            if (string.IsNullOrWhiteSpace(filePath)) return;
            lock (LockObj)
            {
                var items = LoadAllUnsafe();
                var existing = items.FirstOrDefault(i => string.Equals(i.FilePath, filePath, StringComparison.OrdinalIgnoreCase));
                if (existing != null)
                {
                    existing.Title = ChooseBestTitle(existing.Title, title);
                    existing.Type = type ?? existing.Type;
                    if (!string.IsNullOrWhiteSpace(coverUrl)) existing.CoverUrl = coverUrl;
                    if (!string.IsNullOrWhiteSpace(backdropUrl)) existing.BackdropUrl = backdropUrl;
                    existing.LastWatchedUtc = DateTime.UtcNow;
                }
                else
                {
                    items.Add(new Entry
                    {
                        FilePath = filePath,
                        Title = title ?? "",
                        Type = type ?? "",
                        CoverUrl = coverUrl,
                        BackdropUrl = backdropUrl,
                        PositionSeconds = 0,
                        DurationSeconds = 0,
                        LastWatchedUtc = DateTime.UtcNow
                    });
                }

                items = items
                    .OrderByDescending(i => i.LastWatchedUtc)
                    .Take(MaxItems)
                    .ToList();

                SaveAllUnsafe(items);
            }
        }

        public static void UpdateProgress(string filePath, double positionSeconds, double durationSeconds)
        {
            if (string.IsNullOrWhiteSpace(filePath)) return;
            if (positionSeconds < 0) return;
            if (durationSeconds <= 0) return;

            lock (LockObj)
            {
                var items = LoadAllUnsafe();
                var existing = items.FirstOrDefault(i => string.Equals(i.FilePath, filePath, StringComparison.OrdinalIgnoreCase));
                if (existing == null) return;

                existing.PositionSeconds = Math.Max(0, positionSeconds);
                existing.DurationSeconds = Math.Max(0, durationSeconds);
                existing.LastWatchedUtc = DateTime.UtcNow;

                items = items
                    .OrderByDescending(i => i.LastWatchedUtc)
                    .Take(MaxItems)
                    .ToList();

                SaveAllUnsafe(items);
            }
        }

        public static List<(string filePath, string title, string type, string? coverUrl, string? backdropUrl, DateTime lastWatchedUtc, double positionSeconds, double durationSeconds)> GetRecent(int take = 20)
        {
            lock (LockObj)
            {
                return LoadAllUnsafe()
                    .OrderByDescending(i => i.LastWatchedUtc)
                    .Take(Math.Max(1, Math.Min(MaxItems, take)))
                    .Select(i => (i.FilePath, i.Title, i.Type, i.CoverUrl, i.BackdropUrl, i.LastWatchedUtc, i.PositionSeconds, i.DurationSeconds))
                    .ToList();
            }
        }

        internal static string NormalizeIdentity(string filePath)
        {
            try
            {
                var s = (filePath ?? "").Trim();
                if (string.IsNullOrWhiteSpace(s)) return "";

                // Local/URL paths should remain intact (they can contain spaces).
                if (s.Contains('\\') || s.Contains('/'))
                    return s;

                // For compound media ids, keep only the first token (e.g. "tt123 1 2" -> "tt123").
                var token = s.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? s;
                token = (token ?? "").Trim();
                if (string.IsNullOrWhiteSpace(token)) return s;

                if (LooksLikeStableMediaIdToken(token))
                    return token;

                return s;
            }
            catch
            {
                return (filePath ?? "").Trim();
            }
        }

        private static bool LooksLikeStableMediaIdToken(string token)
        {
            try
            {
                var t = (token ?? "").Trim();
                if (string.IsNullOrWhiteSpace(t)) return false;
                if (t.Contains('\\') || t.Contains('/')) return false;

                if (t.StartsWith("tt", StringComparison.OrdinalIgnoreCase))
                {
                    // IMDb ids look like tt1234567
                    if (t.Length >= 3 && char.IsDigit(t[2])) return true;
                    return false;
                }

                // Canonical media ids (tmdb:..., imdb:..., etc.). Keep this strict to avoid treating magnet: and similar as stable.
                if (t.StartsWith("tmdb:", StringComparison.OrdinalIgnoreCase)) return true;
                if (t.StartsWith("imdb:", StringComparison.OrdinalIgnoreCase)) return true;
                if (t.StartsWith("tvdb:", StringComparison.OrdinalIgnoreCase)) return true;

                return false;
            }
            catch
            {
                return false;
            }
        }

        public static int RemoveMatching(string filePathOrKey)
        {
            if (string.IsNullOrWhiteSpace(filePathOrKey)) return 0;

            lock (LockObj)
            {
                var items = LoadAllUnsafe();
                if (items.Count == 0) return 0;

                var raw = (filePathOrKey ?? "").Trim();
                var normWanted = NormalizeIdentity(raw);

                var before = items.Count;
                items = items
                    .Where(i =>
                    {
                        if (i == null) return false;
                        var fp = (i.FilePath ?? "").Trim();
                        if (string.IsNullOrWhiteSpace(fp)) return false;
                        if (string.Equals(fp, raw, StringComparison.OrdinalIgnoreCase)) return false;

                        if (!string.IsNullOrWhiteSpace(normWanted))
                        {
                            var norm = NormalizeIdentity(fp);
                            if (string.Equals(norm, normWanted, StringComparison.OrdinalIgnoreCase))
                                return false;
                        }

                        return true;
                    })
                    .ToList();

                var removed = before - items.Count;
                if (removed > 0)
                {
                    items = items
                        .OrderByDescending(i => i.LastWatchedUtc)
                        .Take(MaxItems)
                        .ToList();
                    SaveAllUnsafe(items);
                }

                return Math.Max(0, removed);
            }
        }

        public static int RemoveByTitle(string title)
        {
            if (string.IsNullOrWhiteSpace(title)) return 0;

            lock (LockObj)
            {
                var items = LoadAllUnsafe();
                if (items.Count == 0) return 0;

                var wantedRaw = (title ?? "").Trim();
                if (string.IsNullOrWhiteSpace(wantedRaw)) return 0;

                var wanted = NormalizeTitleForMatch(wantedRaw);
                if (string.IsNullOrWhiteSpace(wanted))
                    wanted = wantedRaw;

                var before = items.Count;
                items = items
                    .Where(i => i != null)
                    .Where(i =>
                    {
                        var existingRaw = (i.Title ?? "").Trim();
                        if (string.IsNullOrWhiteSpace(existingRaw)) return true;

                        if (string.Equals(existingRaw, wantedRaw, StringComparison.OrdinalIgnoreCase))
                            return false;

                        var existing = NormalizeTitleForMatch(existingRaw);
                        if (string.IsNullOrWhiteSpace(existing))
                            existing = existingRaw;

                        if (string.Equals(existing, wanted, StringComparison.OrdinalIgnoreCase))
                            return false;

                        // Best-effort: handle cases like "[RD] | Comet 1080p" vs "Comet".
                        if (wanted.Length >= 4)
                        {
                            if (existing.IndexOf(wanted, StringComparison.OrdinalIgnoreCase) >= 0)
                                return false;
                            if (wanted.IndexOf(existing, StringComparison.OrdinalIgnoreCase) >= 0 && existing.Length >= 4)
                                return false;
                        }

                        return true;
                    })
                    .ToList();

                var removed = before - items.Count;
                if (removed > 0)
                {
                    items = items
                        .OrderByDescending(i => i.LastWatchedUtc)
                        .Take(MaxItems)
                        .ToList();
                    SaveAllUnsafe(items);
                }

                return Math.Max(0, removed);
            }
        }

        public static int RemoveByTitleContains(string containsToken, bool requireStreamLabel = false)
        {
            if (string.IsNullOrWhiteSpace(containsToken)) return 0;

            lock (LockObj)
            {
                var items = LoadAllUnsafe();
                if (items.Count == 0) return 0;

                var token = (containsToken ?? "").Trim();
                if (string.IsNullOrWhiteSpace(token)) return 0;
                var tokenNorm = NormalizeTitleForMatch(token);
                if (string.IsNullOrWhiteSpace(tokenNorm)) tokenNorm = token;

                static bool IsStreamy(string title)
                {
                    try
                    {
                        var t = (title ?? "").Trim();
                        if (string.IsNullOrWhiteSpace(t)) return false;
                        if (t.Contains("[RD", StringComparison.OrdinalIgnoreCase)) return true;
                        if (t.Contains("RD", StringComparison.OrdinalIgnoreCase) && t.Contains("|", StringComparison.Ordinal)) return true;
                        if (t.Contains("1080p", StringComparison.OrdinalIgnoreCase) ||
                            t.Contains("2160p", StringComparison.OrdinalIgnoreCase) ||
                            t.Contains("720p", StringComparison.OrdinalIgnoreCase) ||
                            t.Contains("WEBRip", StringComparison.OrdinalIgnoreCase) ||
                            t.Contains("BluRay", StringComparison.OrdinalIgnoreCase) ||
                            t.Contains("HDR", StringComparison.OrdinalIgnoreCase))
                            return true;
                        return false;
                    }
                    catch
                    {
                        return false;
                    }
                }

                var before = items.Count;
                items = items
                    .Where(i => i != null)
                    .Where(i =>
                    {
                        var existingRaw = (i.Title ?? "").Trim();
                        if (string.IsNullOrWhiteSpace(existingRaw)) return true;

                        if (requireStreamLabel && !IsStreamy(existingRaw))
                            return true;

                        if (existingRaw.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
                            return false;

                        var existing = NormalizeTitleForMatch(existingRaw);
                        if (string.IsNullOrWhiteSpace(existing)) existing = existingRaw;
                        if (!string.IsNullOrWhiteSpace(tokenNorm) && existing.IndexOf(tokenNorm, StringComparison.OrdinalIgnoreCase) >= 0)
                            return false;

                        return true;
                    })
                    .ToList();

                var removed = before - items.Count;
                if (removed > 0)
                {
                    items = items
                        .OrderByDescending(i => i.LastWatchedUtc)
                        .Take(MaxItems)
                        .ToList();
                    SaveAllUnsafe(items);
                }

                return Math.Max(0, removed);
            }
        }

        internal static string NormalizeTitleForMatch(string title)
        {
            try
            {
                var s = (title ?? "").Trim();
                if (string.IsNullOrWhiteSpace(s)) return "";

                // Drop leading bracket tags like "[RD]", "[HDR]", etc.
                while (s.StartsWith("[", StringComparison.Ordinal) && s.Contains("]", StringComparison.Ordinal))
                {
                    var idx = s.IndexOf(']');
                    if (idx < 0) break;
                    var head = s.Substring(0, idx + 1);
                    if (head.Length > 32) break;
                    s = (s.Substring(idx + 1) ?? "").Trim();
                    if (s.StartsWith("|", StringComparison.Ordinal))
                        s = s.Substring(1).Trim();
                }

                // Common "RD |" prefix patterns.
                if (s.StartsWith("RD", StringComparison.OrdinalIgnoreCase))
                {
                    var rest = s.Substring(2).TrimStart();
                    if (rest.StartsWith("|", StringComparison.Ordinal))
                        s = rest.Substring(1).Trim();
                }

                // Remove common quality/source tokens.
                var tokens = new[]
                {
                    "2160p","1080p","720p","480p","4k",
                    "webrip","web","web-dl","webdl","bluray","bdrip","hdrip","dvdrip","remux",
                    "hdr","dolby vision","dv",
                    "x264","x265","h264","h265","hevc","av1",
                    "aac","ddp","dd","dts","atmos",
                    "proper","repack"
                };

                var lowered = s;
                foreach (var t in tokens)
                {
                    lowered = System.Text.RegularExpressions.Regex.Replace(
                        lowered,
                        $@"\b{System.Text.RegularExpressions.Regex.Escape(t)}\b",
                        " ",
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                }

                // Collapse separators and whitespace.
                lowered = lowered.Replace('|', ' ');
                lowered = System.Text.RegularExpressions.Regex.Replace(lowered, @"\s+", " ").Trim();

                return lowered;
            }
            catch
            {
                return (title ?? "").Trim();
            }
        }

        private static string ChooseBestTitle(string existingTitle, string newTitle)
        {
            var existing = (existingTitle ?? "").Trim();
            var incoming = (newTitle ?? "").Trim();
            if (string.IsNullOrWhiteSpace(incoming)) return existing;
            if (string.IsNullOrWhiteSpace(existing)) return incoming;

            // Avoid overwriting a clean title with a stream label like "[RD] | Title 1080p".
            if (LooksLikeStreamLabel(incoming) && !LooksLikeStreamLabel(existing))
                return existing;

            return incoming;
        }

        private static bool LooksLikeStreamLabel(string title)
        {
            try
            {
                var t = (title ?? "").Trim();
                if (string.IsNullOrWhiteSpace(t)) return false;
                if (t.StartsWith("[", StringComparison.Ordinal) && t.Contains("RD", StringComparison.OrdinalIgnoreCase))
                    return true;
                if (t.Contains("|", StringComparison.Ordinal) && t.Contains("RD", StringComparison.OrdinalIgnoreCase))
                    return true;

                // Common quality tags.
                if (t.Contains("1080p", StringComparison.OrdinalIgnoreCase) ||
                    t.Contains("2160p", StringComparison.OrdinalIgnoreCase) ||
                    t.Contains("720p", StringComparison.OrdinalIgnoreCase) ||
                    t.Contains("WEBRip", StringComparison.OrdinalIgnoreCase) ||
                    t.Contains("BluRay", StringComparison.OrdinalIgnoreCase) ||
                    t.Contains("HDR", StringComparison.OrdinalIgnoreCase))
                    return true;

                return false;
            }
            catch
            {
                return false;
            }
        }

        private static List<Entry> LoadAllUnsafe()
        {
            try
            {
                if (!File.Exists(StorePath)) return new List<Entry>();
                var json = File.ReadAllText(StorePath);
                var items = JsonSerializer.Deserialize<List<Entry>>(json) ?? new List<Entry>();
                return items.Where(i => !string.IsNullOrWhiteSpace(i.FilePath)).ToList();
            }
            catch
            {
                return new List<Entry>();
            }
        }

        private static void SaveAllUnsafe(List<Entry> items)
        {
            try
            {
                var dir = Path.GetDirectoryName(StorePath);
                if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                var json = JsonSerializer.Serialize(items, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(StorePath, json);
            }
            catch
            {
            }
        }
    }
}
