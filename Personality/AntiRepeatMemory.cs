using System;
using System.Collections.Generic;
using System.Linq;

namespace AtlasAI.Personality
{
    public static class AntiRepeatMemory
    {
        private static readonly Dictionary<string, Queue<string>> _recentByKey = new();
        private static readonly Random _rng = new(unchecked(Environment.TickCount));

        public static string Pick(string key, IEnumerable<string> pool, int recentLimit)
        {
            var list = pool?.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().ToList() ?? new List<string>();
            if (list.Count == 0) return "";

            var q = GetQueue(key);
            var avoid = q.Reverse().Take(Math.Min(recentLimit, q.Count)).ToHashSet(StringComparer.Ordinal);

            var candidates = list.Where(s => !avoid.Contains(s)).ToList();
            var chosen = candidates.Count > 0 ? candidates[_rng.Next(candidates.Count)] : LeastRecent(list, q);

            q.Enqueue(chosen);
            while (q.Count > recentLimit) q.Dequeue();
            return chosen;
        }

        public static IEnumerable<string> AvoidBackToBackLines(IEnumerable<string> lines)
        {
            string? last = null;
            foreach (var l in lines)
            {
                if (string.IsNullOrWhiteSpace(l)) continue;
                if (last != null && string.Equals(last.Trim(), l.Trim(), StringComparison.Ordinal))
                    continue;
                yield return l;
                last = l;
            }
        }

        private static Queue<string> GetQueue(string key)
        {
            if (!_recentByKey.TryGetValue(key, out var q))
            {
                q = new Queue<string>();
                _recentByKey[key] = q;
            }
            return q;
        }

        private static string LeastRecent(IReadOnlyList<string> pool, Queue<string> q)
        {
            foreach (var old in q)
                if (pool.Contains(old)) return old;
            return pool[_rng.Next(pool.Count)];
        }
    }
}
