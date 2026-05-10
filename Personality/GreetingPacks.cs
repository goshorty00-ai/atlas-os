using System;
using System.Collections.Generic;
using System.Linq;

namespace AtlasAI.Personality
{
    internal static class GreetingPacks
    {
        private sealed class Pack
        {
            public string[] Anytime { get; init; } = Array.Empty<string>();
            public string[] Morning { get; init; } = Array.Empty<string>();
            public string[] Afternoon { get; init; } = Array.Empty<string>();
            public string[] Evening { get; init; } = Array.Empty<string>();
            public string[] Night { get; init; } = Array.Empty<string>();
        }

        private static readonly Dictionary<PersonalityType, Pack> Packs = BuildPacks();
        private static readonly Dictionary<PersonalityType, Queue<string>> RecentPerPersonality = new();
        private const int RecentCapacity = 10;
        private static readonly Random Rng = new(unchecked(Environment.TickCount));

        public static string Select(PersonalityType personality, DateTime now)
        {
            if (!Packs.TryGetValue(personality, out var pack))
                pack = Packs[PersonalityType.Minimal];

            var bucket = Bucket(now);
            var usePrimary = Rng.NextDouble() < 0.70;
            var candidates = usePrimary ? GetBucket(pack, bucket) : pack.Anytime;
            if (candidates.Length == 0)
                candidates = GetFallback(pack);

            var recent = GetRecent(personality);
            var pool = candidates.Where(c => !recent.Contains(c)).ToList();
            if (pool.Count == 0)
            {
                recent.Clear();
                pool = candidates.ToList();
            }

            var pick = pool[Rng.Next(pool.Count)];

            recent.Enqueue(pick);
            while (recent.Count > RecentCapacity) recent.Dequeue();

            if (Rng.Next(20) == 0)
            {
                if (!pick.Contains("Atlas", StringComparison.OrdinalIgnoreCase))
                    pick = pick.TrimEnd('.') + " I am Atlas.";
            }

            return pick;
        }

        private static Queue<string> GetRecent(PersonalityType t)
        {
            if (!RecentPerPersonality.TryGetValue(t, out var q))
            {
                q = new Queue<string>();
                RecentPerPersonality[t] = q;
            }
            return q;
        }

        private static string[] GetBucket(Pack p, string bucket)
        {
            return bucket switch
            {
                "morning" => p.Morning,
                "afternoon" => p.Afternoon,
                "evening" => p.Evening,
                "night" => p.Night,
                _ => p.Anytime
            };
        }

        private static string[] GetFallback(Pack p)
        {
            return p.Anytime
                .Concat(p.Morning)
                .Concat(p.Afternoon)
                .Concat(p.Evening)
                .Concat(p.Night)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct()
                .ToArray();
        }

        private static string Bucket(DateTime now)
        {
            var h = now.Hour;
            if (h < 5) return "night";
            if (h < 12) return "morning";
            if (h < 18) return "afternoon";
            return "evening";
        }

        private static Dictionary<PersonalityType, Pack> BuildPacks()
        {
            return new Dictionary<PersonalityType, Pack>
            {
                {
                    PersonalityType.Butler,
                    new Pack
                    {
                        Anytime = new[]
                        {
                            "At your service.",
                            "Standing by.",
                            "All systems steady.",
                            "Ready when you are."
                        },
                        Morning = new[]
                        {
                            "Good morning. Systems are calm.",
                            "Good morning. Everything is in order.",
                            "Good morning. Ready to begin."
                        },
                        Afternoon = new[]
                        {
                            "Good afternoon. All is steady.",
                            "Good afternoon. I am ready.",
                            "Good afternoon. No issues to report."
                        },
                        Evening = new[]
                        {
                            "Good evening. Ready when you are.",
                            "Good evening. Your move.",
                            "Good evening. Systems remain stable."
                        },
                        Night = new[]
                        {
                            "Quiet night. At your command.",
                            "Night watch active. Proceed when ready.",
                            "All quiet. How may I assist?"
                        }
                    }
                },
#if PERSONAL_BUILD
                {
                    PersonalityType.Unfiltered,
                    new Pack
                    {
                        Anytime = new[]
                        {
                            "Alright, let’s move.",
                            "Your call.",
                            "Say it and I’ll do it.",
                            "Okay — what’s next?"
                        },
                        Morning = new[]
                        {
                            "Morning. Let’s get it done.",
                            "Morning. No drama here.",
                            "Morning. I’m awake — barely."
                        },
                        Afternoon = new[]
                        {
                            "Afternoon. Ready to ship.",
                            "Afternoon. System’s chill.",
                            "Afternoon. Your move."
                        },
                        Evening = new[]
                        {
                            "Evening. Let’s ship something.",
                            "Evening. All good. Your call.",
                            "Evening. Ready if you are."
                        },
                        Night = new[]
                        {
                            "Late night. All quiet. Shoot.",
                            "Night shift. I’m in.",
                            "Night. Let’s knock this out."
                        }
                    }
                },
#endif
                {
                    PersonalityType.Minimal,
                    new Pack
                    {
                        Anytime = new[]
                        {
                            "Ready.",
                            "Standing by.",
                            "Go ahead.",
                            "Here."
                        },
                        Morning = new[] { "Morning." },
                        Afternoon = new[] { "Afternoon." },
                        Evening = new[] { "Evening." },
                        Night = new[] { "Night watch." }
                    }
                },
                {
                    PersonalityType.Friendly,
                    new Pack
                    {
                        Anytime = new[]
                        {
                            "Let’s get this done.",
                            "Ready when you are.",
                            "All set."
                        },
                        Morning = new[]
                        {
                            "Good morning. Let’s start strong.",
                            "Morning. Everything looks calm.",
                            "Morning. Ready to help."
                        },
                        Afternoon = new[]
                        {
                            "Good afternoon. All green.",
                            "Afternoon. Ready to go.",
                            "Afternoon. Let’s move."
                        },
                        Evening = new[]
                        {
                            "Good evening. All steady.",
                            "Evening. Ready if you are.",
                            "Evening. Calm and ready."
                        },
                        Night = new[]
                        {
                            "Late hours—systems calm.",
                            "Quiet night. Standing by.",
                            "Night shift on."
                        }
                    }
                }
            };
        }
    }
}
