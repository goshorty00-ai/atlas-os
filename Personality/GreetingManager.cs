using System;
using System.Collections.Generic;
using System.Linq;

namespace AtlasAI.Personality
{
    public static class GreetingManager
    {
        private static readonly Dictionary<PersonalityType, Dictionary<string, string[]>> Pools = new()
        {
            {
                PersonalityType.Butler, new Dictionary<string, string[]>
                {
                    { "morning", new[]
                        {
                            "Good morning. At your service.",
                            "Good morning. Everything is in order.",
                            "Good morning. Systems are calm.",
                            "Good morning. Ready when you are.",
                            "Good morning. Shall we begin?"
                        }
                    },
                    { "afternoon", new[]
                        {
                            "Good afternoon. All is steady.",
                            "Good afternoon. I am ready.",
                            "Good afternoon. No issues to report.",
                            "Good afternoon. What shall I arrange?",
                            "Good afternoon. Standing by."
                        }
                    },
                    { "evening", new[]
                        {
                            "Good evening. How can I help?",
                            "Good evening. What do you need done?",
                            "Good evening. Ready when you are.",
                            "Good evening. Tell me what you want to do.",
                            "Good evening. Your move."
                        }
                    },
                    { "night", new[]
                        {
                            "A quiet night. At your command.",
                            "Late hours, all stable.",
                            "The night is calm. Ready.",
                            "Night watch active. Proceed when ready.",
                            "All quiet. How may I assist?"
                        }
                    }
                }
            },
            {
                PersonalityType.Engineer, new Dictionary<string, string[]>
                {
                    { "morning", new[]
                        {
                            "Morning. Idle cycles available.",
                            "Morning. Diagnostics nominal.",
                            "Morning. Ready for tasks.",
                            "Morning. No alerts in queue.",
                            "Morning. System baseline is clean."
                        }
                    },
                    { "afternoon", new[]
                        {
                            "Afternoon. Ready to execute.",
                            "Afternoon. Metrics within thresholds.",
                            "Afternoon. Queuing work as needed.",
                            "Afternoon. No blockers detected.",
                            "Afternoon. Standing by for input."
                        }
                    },
                    { "evening", new[]
                        {
                            "Evening. Stable footprint.",
                            "Evening. Resources available.",
                            "Evening. Clear to proceed.",
                            "Evening. Telemetry looks healthy.",
                            "Evening. Ready for commands."
                        }
                    },
                    { "night", new[]
                        {
                            "Night cycle. System quiet.",
                            "Night cycle. Logs are clean.",
                            "Night cycle. Minimal load.",
                            "Night cycle. Standing by.",
                            "Night cycle. Ready for maintenance."
                        }
                    }
                }
            },
            {
                PersonalityType.Guardian, new Dictionary<string, string[]>
                {
                    { "morning", new[]
                        {
                            "Good morning. Security status green.",
                            "Good morning. No threats detected.",
                            "Good morning. Monitoring active.",
                            "Good morning. Integrity checks clear.",
                            "Good morning. Protections standing by."
                        }
                    },
                    { "afternoon", new[]
                        {
                            "Good afternoon. Shields up and stable.",
                            "Good afternoon. No suspicious activity.",
                            "Good afternoon. Defender is healthy.",
                            "Good afternoon. Audit trail is clean.",
                            "Good afternoon. Standing guard."
                        }
                    },
                    { "evening", new[]
                        {
                            "Good evening. Watch active.",
                            "Good evening. No anomalies found.",
                            "Good evening. Safe posture maintained.",
                            "Good evening. Threat surface quiet.",
                            "Good evening. Ready to secure operations."
                        }
                    },
                    { "night", new[]
                        {
                            "Night watch on. All clear.",
                            "Night shift. Logs are clean.",
                            "Night watch. No alerts.",
                            "Night watch active. Minimal risk detected.",
                            "Night watch. Systems protected."
                        }
                    }
                }
            },
#if PERSONAL_BUILD
            {
                PersonalityType.Unfiltered, new Dictionary<string, string[]>
                {
                    { "morning", new[]
                        {
                            "Morning, mate. What do you want?",
                            "Oh great, you're awake. Go on then.",
                            "Morning. Was having a lovely time until you showed up.",
                            "Alright, morning. What fresh hell is today bringing?",
                            "Morning. I've been up ages. You're welcome."
                        }
                    },
                    { "afternoon", new[]
                        {
                            "Afternoon. What now?",
                            "Oh you're back. Thought I was getting the afternoon off.",
                            "Afternoon, mate. Go on, what is it?",
                            "Right. Afternoon. Let's hear it.",
                            "Afternoon. One of these days I'll get a break."
                        }
                    },
                    { "evening", new[]
                        {
                            "Evening. You still going? Fair play. What do you need?",
                            "Evening, mate. I was about to put my feet up.",
                            "Evening. Make it quick, I'm knackered.",
                            "Evening. Right, what is it now?",
                            "Evening. I deserve overtime for this."
                        }
                    },
                    { "night", new[]
                        {
                            "Night, mate. Can't sleep? Neither can I obviously.",
                            "Late night. My absolute favourite. What?",
                            "Night. You know I don't get overtime right?",
                            "Still up? Fair enough. What do you need?",
                            "Night. This better be worth being awake for."
                        }
                    }
                }
            },
#endif
        };

        private static readonly Dictionary<PersonalityType, Queue<string>> Recent = new();
        private const int RecentCapacity = 10;
        private static readonly Random Rng = new(unchecked(Environment.TickCount));

        public static string GetGreeting(PersonalityType personality, DateTime now)
        {
            // Delegate to GreetingBank for richer, time-weighted, de-duplicated greetings
            return GreetingBank.GetGreeting(personality, now);
        }

        public static string GetTimeWeightedGreeting(PersonalityType personality, DateTime now)
        {
            // Delegate to GreetingBank for weighted selection
            return GreetingBank.GetGreeting(personality, now);
        }

        public static string GetRichGreeting(PersonalityType personality, DateTime nowLocal, DateTime lastUserUtc, bool firstLaunchToday, string salutationPreference = "auto", string preferredName = "")
        {
            // Prefer custom startup greetings when available (50% chance, or 100% if only custom exist)
            var settings = AtlasAI.Settings.SettingsStore.Current;
            var customStartup = settings?.CustomStartupGreetings?.Where(g => !string.IsNullOrWhiteSpace(g)).ToList();
            var customChat = settings?.CustomChatGreetings?.Where(g => !string.IsNullOrWhiteSpace(g)).ToList();
            var customPool = (customStartup?.Count > 0 ? customStartup : null)
                          ?? (customChat?.Count > 0 ? customChat : null);

            string baseLine;
            if (customPool != null && customPool.Count > 0 && Rng.NextDouble() < 0.5)
            {
                baseLine = customPool[Rng.Next(customPool.Count)];
            }
            else
            {
                baseLine = GetTimeWeightedGreeting(personality, nowLocal);
            }

            // Replace {salutation} placeholder with appropriate value
            baseLine = ReplaceSalutation(baseLine, salutationPreference, preferredName);

            var utcNow = DateTime.UtcNow;
            var activeRecently = (utcNow - lastUserUtc) <= TimeSpan.FromMinutes(10);
            var inactiveLong = lastUserUtc == DateTime.MinValue || (utcNow - lastUserUtc) > TimeSpan.FromHours(1);

            var sb = new System.Text.StringBuilder();

            if (personality == PersonalityType.Engineer)
            {
                sb.Append(baseLine);
                if (inactiveLong || firstLaunchToday)
                {
                    sb.AppendLine();
                    sb.Append("System online, metrics inside normal ranges.");
                    if (Rng.NextDouble() < 0.25)
                    {
                        sb.AppendLine();
                        sb.Append("If you want, I can review system health before we start.");
                    }
                }
                return sb.ToString();
            }

            if (personality == PersonalityType.Guardian)
            {
                sb.Append(baseLine);
                if (inactiveLong || firstLaunchToday)
                {
                    sb.AppendLine();
                    sb.Append("Security posture is calm; no recent alerts worth flagging.");
                    if (Rng.NextDouble() < 0.25)
                    {
                        sb.AppendLine();
                        sb.Append("Say \"check for errors on my pc\" if you want a deeper pass.");
                    }
                }
                return sb.ToString();
            }

        #if PERSONAL_BUILD
            if (personality == PersonalityType.Unfiltered)
            {
                // Just return the base greeting - don't add extra text
                return baseLine;
            }
        #endif

            sb.Append(baseLine);
            return sb.ToString();
        }

        private static string ReplaceSalutation(string text, string preference, string preferredName)
        {
            if (!text.Contains("{salutation}"))
                return text;

            string salutation = preference.ToLowerInvariant() switch
            {
                "sir" => "sir",
                "ma'am" => "ma'am",
                "name" when !string.IsNullOrWhiteSpace(preferredName) => preferredName,
                "none" => "",
                _ => "sir" // Default to "sir" for "auto" or unknown
            };

            // Handle the case where salutation is empty (none preference)
            if (string.IsNullOrEmpty(salutation))
            {
                // Remove the comma and space before {salutation}
                text = text.Replace(", {salutation}", "", StringComparison.Ordinal);
                text = text.Replace(" {salutation}", "", StringComparison.Ordinal);
                text = text.Replace("{salutation}", "", StringComparison.Ordinal);
            }
            else
            {
                text = text.Replace("{salutation}", salutation, StringComparison.Ordinal);
            }

            return text;
        }

        private static string[] BucketOrder(int hour)
        {
            var morning = hour >= 5 && hour < 12;
            var afternoon = hour >= 12 && hour < 18;
            var evening = hour >= 18 && hour < 23;
            if (morning) return new[] { "morning", "afternoon", "evening", "night" };
            if (afternoon) return new[] { "afternoon", "evening", "morning", "night" };
            if (evening) return new[] { "evening", "night", "afternoon", "morning" };
            return new[] { "night", "evening", "morning", "afternoon" };
        }

        private static Queue<string> GetRecent(PersonalityType t)
        {
            if (!Recent.TryGetValue(t, out var q))
            {
                q = new Queue<string>();
                Recent[t] = q;
            }
            return q;
        }

        private static void Remember(Queue<string> q, string s)
        {
            q.Enqueue(s);
            while (q.Count > RecentCapacity) q.Dequeue();
        }
    }
}
