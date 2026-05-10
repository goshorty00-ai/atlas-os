using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;

namespace AtlasAI.Personality
{
    internal static class GreetingSelector
    {
        private static readonly System.Collections.Generic.Dictionary<PersonalityType, string[]> Pools = new()
        {
            { PersonalityType.Butler, new[] { "Good afternoon.", "Systems remain stable.", "All services are operational." } },
            { PersonalityType.Engineer, new[] { "System idle.", "Ready.", "Diagnostics clear." } },
            { PersonalityType.Guardian, new[] { "Security status: green.", "No threats detected.", "Monitoring active." } },
#if PERSONAL_BUILD
            { PersonalityType.Unfiltered, new[] { "Yeah?", "What’s up?", "Alright, what are we breaking?" } },
#endif
        };

        private static readonly System.Collections.Generic.Dictionary<PersonalityType, System.Collections.Generic.Queue<string>> _recentPerType = new();
        private const int RecentPerTypeCapacity = 10;
        private enum GreetingType
        {
            Short,
            Contextual,
            StatusAware,
            Witty
        }

        private static readonly Queue<string> _recent = new();
        private const int RecentCapacity = 20;
        private static GreetingType? _lastType = null;
        private static readonly Random _rng = new(unchecked(Environment.TickCount));

        private static readonly string[] AddressTerms = new[]
        {
            "friend", "there", "team"
        };

        public static string SelectButlerGreeting()
        {
            var type = ChooseType();
            var greeting = Generate(type);

            var guardCount = 0;
            while (IsRecentlyUsed(greeting, 10) && guardCount++ < 5)
            {
                greeting = Generate(type);
            }

            Remember(greeting);
            _lastType = type;
            return greeting;
        }

        public static string SelectGreeting(PersonalityType personality)
        {
            if (!Pools.TryGetValue(personality, out var pool) || pool.Length == 0)
            {
                return SelectButlerGreeting();
            }

            var q = GetRecentQueue(personality);
            var weights = GetWeights(personality);

            var choice = WeightedPickAvoidRecent(pool, weights, q);
            q.Enqueue(choice);
            while (q.Count > RecentPerTypeCapacity) q.Dequeue();
            return choice;
        }

        private static GreetingType ChooseType()
        {
            // Weights: 40% short, 30% contextual, 20% status, 10% witty
            // Avoid same structure twice in a row by single reroll; otherwise pick next bucket
            for (int attempt = 0; attempt < 2; attempt++)
            {
                var roll = _rng.NextDouble();
                var chosen = roll < 0.40 ? GreetingType.Short
                           : roll < 0.70 ? GreetingType.Contextual
                           : roll < 0.90 ? GreetingType.StatusAware
                           : GreetingType.Witty;
                if (_lastType == null || chosen != _lastType.Value)
                    return chosen;
            }

            // Fallback: select a different bucket deterministically
            return _lastType switch
            {
                GreetingType.Short => GreetingType.Contextual,
                GreetingType.Contextual => GreetingType.StatusAware,
                GreetingType.StatusAware => GreetingType.Witty,
                _ => GreetingType.Short
            };
        }

        private static string Generate(GreetingType type)
        {
            var salutation = LoadSalutation();
            if (string.IsNullOrWhiteSpace(salutation))
                salutation = Pick(AddressTerms);

            return type switch
            {
                GreetingType.Short => GenerateShort(salutation),
                GreetingType.Contextual => GenerateContextual(salutation),
                GreetingType.StatusAware => GenerateStatusAware(salutation),
                GreetingType.Witty => GenerateWitty(salutation),
                _ => GenerateShort(salutation)
            };
        }

        private static string GenerateShort(string salutation)
        {
            var options = new[]
            {
                "Good morning.",
                "Good afternoon.",
                "Good evening.",
                "Welcome back.",
                "Hello again.",
                "All's well."
            };
            var pick = Pick(options);
            return pick.Contains("Good ") ? pick : $"{pick}";
        }

        private static string GenerateContextual(string salutation)
        {
            var hour = DateTime.Now.Hour;
            var day =
                hour < 5 ? "late night" :
                hour < 12 ? "morning" :
                hour < 18 ? "afternoon" :
                "evening";

            var options = new[]
            {
                $"Good {day}. Systems are stable.",
                $"Good {day}. All services are running smoothly.",
                $"{Cap(day)}. No critical alerts.",
                $"Good {day}, {salutation}. Everything looks calm."
            };
            return Pick(options);
        }

        private static string GenerateStatusAware(string salutation)
        {
            var cpu = SampleCpu();
            string cpuPhrase =
                cpu < 25 ? "CPU usage is light today." :
                cpu < 60 ? "CPU load is moderate." :
                "CPU is a touch busy.";

            var general = cpu < 60 ? "Systems are stable." : "No critical alerts in view.";

            var options = new[]
            {
                $"Welcome back. {general}",
                $"Hello again — {general.ToLowerInvariant()}",
                $"Good {TimeOfDay()}{(salutation.Length > 0 ? $", {salutation}" : "")}. {cpuPhrase}",
                $"{Cap(TimeOfDay())}. {cpuPhrase}"
            };
            return Pick(options);
        }

        private static string GenerateWitty(string salutation)
        {
            var options = new[]
            {
                "All quiet on the kernel front.",
                "Tea brewed; the system hums along.",
                "Calm seas and steady circuits.",
                $"At your side, {salutation}. The board is in order."
            };
            return Pick(options);
        }

        private static string TimeOfDay()
        {
            var h = DateTime.Now.Hour;
            return h < 12 ? "morning" : h < 18 ? "afternoon" : "evening";
        }

        private static string Cap(string s) =>
            string.IsNullOrWhiteSpace(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);

        private static string Pick(IReadOnlyList<string> items)
        {
            if (items == null || items.Count == 0) return "";
            return items[_rng.Next(items.Count)];
        }

        private static System.Collections.Generic.Queue<string> GetRecentQueue(PersonalityType t)
        {
            if (!_recentPerType.TryGetValue(t, out var q))
            {
                q = new System.Collections.Generic.Queue<string>();
                _recentPerType[t] = q;
            }
            return q;
        }

        private static string WeightedPickAvoidRecent(string[] pool, int[] weights, System.Collections.Generic.Queue<string> recent)
        {
            if (pool.Length == 0) return "";
            if (weights == null || weights.Length != pool.Length)
                weights = Enumerable.Repeat(1, pool.Length).ToArray();

            string choice = "";
            var attempts = 0;
            do
            {
                var idx = WeightedIndex(weights);
                choice = pool[idx];
                attempts++;
            }
            while (attempts < 6 && recent.Reverse().Take(Math.Min(RecentPerTypeCapacity, recent.Count)).Any(s => string.Equals(s, choice, StringComparison.Ordinal)));

            return choice;
        }

        private static int WeightedIndex(int[] weights)
        {
            var total = 0;
            for (int i = 0; i < weights.Length; i++) total += Math.Max(0, weights[i]);
            if (total <= 0) return _rng.Next(weights.Length);
            var roll = _rng.Next(total);
            var run = 0;
            for (int i = 0; i < weights.Length; i++)
            {
                run += Math.Max(0, weights[i]);
                if (roll < run) return i;
            }
            return weights.Length - 1;
        }

        private static int[] GetWeights(PersonalityType t)
        {
            var hour = DateTime.Now.Hour;
            var morning = hour >= 5 && hour < 12;
            var afternoon = hour >= 12 && hour < 18;
            var eveningOrNight = hour >= 18 || hour < 5;

            switch (t)
            {
                case PersonalityType.Butler:
                    // ["Good afternoon.", "Systems remain stable.", "All services are operational."]
                    if (afternoon) return new[] { 5, 3, 2 };
                    if (morning) return new[] { 1, 3, 2 };
                    return new[] { 1, 3, 2 };
                case PersonalityType.Engineer:
                    // ["System idle.", "Ready.", "Diagnostics clear."]
                    if (morning) return new[] { 1, 5, 2 };
                    if (afternoon) return new[] { 4, 2, 2 };
                    return new[] { 2, 2, 5 };
                case PersonalityType.Guardian:
                    // ["Security status: green.", "No threats detected.", "Monitoring active."]
                    if (morning) return new[] { 2, 2, 5 };
                    if (afternoon) return new[] { 5, 2, 2 };
                    return new[] { 2, 5, 2 };
#if PERSONAL_BUILD
                case PersonalityType.Unfiltered:
                    // ["Yeah?", "What’s up?", "Alright, what are we breaking?"]
                    if (morning) return new[] { 2, 5, 1 };
                    if (afternoon) return new[] { 5, 2, 1 };
                    return new[] { 1, 2, 5 };
#endif
                default:
                    return new[] { 3, 3, 3 };
            }
        }

        private static bool IsRecentlyUsed(string s, int within)
        {
            if (string.IsNullOrWhiteSpace(s)) return false;
            if (within <= 0) return false;
            return _recent.Reverse().Take(Math.Min(within, _recent.Count)).Any(x => string.Equals(x, s, StringComparison.Ordinal));
        }

        private static void Remember(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return;
            _recent.Enqueue(s);
            while (_recent.Count > RecentCapacity)
                _recent.Dequeue();
        }

        private static string LoadSalutation()
        {
            try
            {
                var env = Environment.GetEnvironmentVariable("ATLAS_SALUTATION");
                if (!string.IsNullOrWhiteSpace(env))
                    return env.Trim();
            }
            catch
            {
            }
            return Pick(AddressTerms);
        }

        // Lightweight CPU sample (approximate)
        private static double SampleCpu()
        {
            try
            {
                GetSystemTimes(out var idle1, out var kernel1, out var user1);
                var start = Stopwatch.GetTimestamp();
                var target = start + (long)(Stopwatch.Frequency * 0.3); // ~300ms
                while (Stopwatch.GetTimestamp() < target) { }
                GetSystemTimes(out var idle2, out var kernel2, out var user2);

                ulong idle = Diff(idle2, idle1);
                ulong kernel = Diff(kernel2, kernel1);
                ulong user = Diff(user2, user1);
                ulong sys = kernel + user;
                if (sys == 0) return 0;
                var busy = sys - idle;
                return (100.0 * busy) / sys;
            }
            catch
            {
                return 0;
            }
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetSystemTimes(out FILETIME idleTime, out FILETIME kernelTime, out FILETIME userTime);

        [StructLayout(LayoutKind.Sequential)]
        private struct FILETIME
        {
            public uint dwLowDateTime;
            public uint dwHighDateTime;
        }

        private static ulong Diff(FILETIME a, FILETIME b)
        {
            ulong a64 = ((ulong)a.dwHighDateTime << 32) | a.dwLowDateTime;
            ulong b64 = ((ulong)b.dwHighDateTime << 32) | b.dwLowDateTime;
            return a64 - b64;
        }
    }
}
