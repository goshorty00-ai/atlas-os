using System;
using System.Collections.Concurrent;

namespace AtlasAI.MediaIntelligence
{
    internal static class MediaCuratorCache
    {
        private sealed class CacheEntry
        {
            public DateTime ExpiresUtc { get; init; }
            public object? Value { get; init; }
        }

        private static readonly ConcurrentDictionary<string, CacheEntry> Entries = new(StringComparer.OrdinalIgnoreCase);

        public static bool TryGet<T>(string key, out T value)
        {
            value = default!;

            if (string.IsNullOrWhiteSpace(key))
                return false;

            if (!Entries.TryGetValue(key, out var entry) || entry == null)
                return false;

            if (entry.ExpiresUtc <= DateTime.UtcNow)
            {
                Entries.TryRemove(key, out _);
                return false;
            }

            if (entry.Value is not T typed)
                return false;

            value = typed;
            return true;
        }

        public static void Set<T>(string key, T value, TimeSpan ttl)
        {
            if (string.IsNullOrWhiteSpace(key) || ttl <= TimeSpan.Zero)
                return;

            Entries[key] = new CacheEntry
            {
                ExpiresUtc = DateTime.UtcNow.Add(ttl),
                Value = value
            };
        }
    }
}