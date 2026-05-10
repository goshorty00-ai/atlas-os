using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using AtlasAI.Personality;

namespace AtlasAI.Voice
{
    public enum GreetingContext
    {
        Startup,
        ChatOpen
    }

    public static class GreetingGenerator
    {
        private sealed class GreetingHistory
        {
            public Dictionary<string, List<string>> RecentByContext { get; set; } = new(StringComparer.OrdinalIgnoreCase);
            public List<string> RecentGlobal { get; set; } = new();
            public DateTime LastSpokenUtc { get; set; }
        }

        private static readonly object Gate = new();
        private static readonly string HistoryPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AtlasAI",
            "greeting_history.json");

        public static string? TryNext(GreetingContext context, string userName, TimeSpan minInterval)
        {
            userName = (userName ?? "").Trim();
            if (string.IsNullOrWhiteSpace(userName)) userName = "there";

            lock (Gate)
            {
                var history = Load();
                var now = DateTime.UtcNow;
                if (history.LastSpokenUtc != default && (now - history.LastSpokenUtc) < minInterval)
                    return null;

                var ctxKey = context.ToString();
                if (!history.RecentByContext.TryGetValue(ctxKey, out var recentCtx))
                {
                    recentCtx = new List<string>();
                    history.RecentByContext[ctxKey] = recentCtx;
                }

                var candidate = Generate(context, userName, history.RecentGlobal, recentCtx);
                if (string.IsNullOrWhiteSpace(candidate))
                    return null;

                AddUnique(history.RecentGlobal, candidate, 120);
                AddUnique(recentCtx, candidate, 40);
                history.LastSpokenUtc = now;
                Save(history);
                return candidate;
            }
        }

        private static string Generate(GreetingContext context, string userName, List<string> recentGlobal, List<string> recentCtx)
        {
            var sectionCandidates = AtlasAI.Brain.SectionAgentContext.GetGreetingCandidates(context, userName);
            if (sectionCandidates.Count > 0)
            {
                var customPick = PickNonRecent(sectionCandidates.ToArray(), recentGlobal, recentCtx);
                if (!string.IsNullOrWhiteSpace(customPick))
                    return customPick;
            }

            var richGreeting = TryGetRichGreeting(context, userName, recentGlobal, recentCtx);
            if (!string.IsNullOrWhiteSpace(richGreeting))
                return richGreeting;

            var hour = DateTime.Now.Hour;
            var timeOfDay = hour switch
            {
                >= 5 and < 12 => "morning",
                >= 12 and < 18 => "afternoon",
                >= 18 and < 23 => "evening",
                _ => "night"
            };

            if (string.IsNullOrWhiteSpace(userName) || userName.Equals("there", StringComparison.OrdinalIgnoreCase))
                userName = "Boss";

            try
            {
                var s = AtlasAI.Settings.SettingsStore.Current;
                var accent = (AtlasAI.Core.PreferencesStore.Instance.Current.ChatAccent ?? "").Trim();
                var isIrish = string.Equals(AtlasAI.Core.PreferencesStore.Instance.Current.ChatPersonality, "Irish Mate", StringComparison.OrdinalIgnoreCase)
                              || string.Equals(accent, "Irish", StringComparison.OrdinalIgnoreCase);
                if (isIrish)
                {
                    string[] pool = timeOfDay switch
                    {
                        "morning" => new[]
                        {
                            $"Morning, {userName}. What’s the plan, ya feckin’ eejit?",
                            $"Morning, {userName}. What have you broken now, eh? Only joking — love ya really. What’s the crack?",
                            $"Morning, {userName}. Don’t make this a disaster, ya fecker. Go on then, what do you need?",
                            $"Morning, {userName}. I leave you alone five minutes and it’s chaos. What’s the mess, ya fecker?"
                        },
                        "afternoon" => new[]
                        {
                            $"Afternoon, {userName}. What mess are we fixing this time, ya fecker?",
                            $"Afternoon, {userName}. Right then — what’s the craic, ya feckin’ eejit?",
                            $"Afternoon, {userName}. I hope you’ve got something decent for me, ya fecker.",
                            $"Afternoon, {userName}. What the feck have you done now? Only messing — what can I sort?"
                        },
                        "evening" => new[]
                        {
                            $"Evening, {userName}. I leave you five minutes and it’s chaos, ya fecker.",
                            $"Evening, {userName}. What’s the crack, ya feckin’ eejit?",
                            $"Evening, {userName}. Say it quick — I’m in the middle of saving your arse again.",
                            $"Evening, {userName}. Don’t worry, I’ll save the day again. What’s on fire now, ya fecker?"
                        },
                        _ => new[]
                        {
                            $"Late one, {userName}. What’s the craic, ya fecker?",
                            $"Night, {userName}. Let’s get this sorted, ya feckin’ eejit.",
                            $"Night, {userName}. You’re still up — what mess now, ya fecker?",
                            $"Night, {userName}. I hope I’m getting a raise for this. What’s the trouble, ya fecker?"
                        }
                    };
                    var greet = PickNonRecent(pool, recentGlobal, recentCtx);
                    return greet;
                }

                var isUnfiltered = string.Equals(s.PersonalitySelected, "Unfiltered", StringComparison.OrdinalIgnoreCase)
                                   || string.Equals(s.PersonalitySelected, "Unrestricted", StringComparison.OrdinalIgnoreCase)
                                   || string.Equals(s.UnfilteredStyle ?? "", "Banter", StringComparison.OrdinalIgnoreCase)
                                   || string.Equals(s.UnfilteredStyle ?? "", "ChaosTesting", StringComparison.OrdinalIgnoreCase);
                if (isUnfiltered)
                {
                    string[] pool = timeOfDay switch
                    {
                        "morning" => new[]
                        {
                            $"Morning, {userName}. What’s the plan?",
                            $"Morning, {userName}. Let’s get it done.",
                            $"Morning, {userName}. Your move.",
                            $"Morning, {userName}. What mess are we fixing today?",
                            $"Morning, {userName}. Say the word and we’ll send it.",
                            $"Morning, {userName}. Give me something spicy.",
                            $"Morning, {userName}. Let’s break things (carefully)."
                        },
                        "afternoon" => new[]
                        {
                            $"Afternoon, {userName}. Ready?",
                            $"Afternoon, {userName}. Say it, I’ll do it.",
                            $"Afternoon, {userName}. Let’s move.",
                            $"Afternoon, {userName}. What’s the job, then?",
                            $"Afternoon, {userName}. What are we unfucking first?",
                            $"Afternoon, {userName}. Hit me with it — no waffle.",
                            $"Afternoon, {userName}. What’s the play?"
                        },
                        "evening" => new[]
                        {
                            $"Evening, {userName}. What’s up?",
                            $"Evening, {userName}. Ready if you are.",
                            $"Evening, {userName}. Let’s ship something.",
                            $"Evening, {userName}. What do you want done?",
                            $"Evening, {userName}. I’m here — don’t start chatting shite, just tell me.",
                            $"Evening, {userName}. Go on then. What’s broken?",
                            $"Evening, {userName}. Let’s get it sorted."
                        },
                        _ => new[]
                        {
                            $"Late night, {userName}. Shoot.",
                            $"Night, {userName}. No drama — what’s next?",
                            $"Night, {userName}. I’m here.",
                            $"Late one, {userName}. What’s the mission?",
                            $"Night, {userName}. If this is another mess, we’ll still fix it.",
                            $"Night, {userName}. Keep it real — what do you need?",
                            $"Late shift, {userName}. Let’s go."
                        }
                    };
                    var greet = PickNonRecent(pool, recentGlobal, recentCtx);
                    return greet;
                }
            }
            catch { }

            var salutations = timeOfDay switch
            {
                "morning" => new[] { 
                    $"Good morning, {userName}.", $"Morning, {userName}.", 
                    $"Top of the morning, {userName}.", $"Rise and shine, {userName}." 
                },
                "afternoon" => new[] { 
                    $"Good afternoon, {userName}.", $"Afternoon, {userName}.", 
                    $"Hello there, {userName}.", $"Good day, {userName}." 
                },
                "evening" => new[] { 
                    $"Good evening, {userName}.", $"Evening, {userName}.", 
                    $"Hello again, {userName}.", $"Evening, {userName}." 
                },
                _ => new[] { 
                    $"Still with me, {userName}?", $"Hello, {userName}.", 
                    $"Burning the midnight oil, {userName}?", $"Late one tonight, {userName}." 
                }
            };

            var openers = context == GreetingContext.Startup
                ? new[]
                {
                    Pick(salutations),
                    $"Online, {userName}.",
                    $"Initialized. Ready, {userName}.",
                    "Ready.",
                    "System active.",
                    $"At your service, {userName}."
                }
                : new[]
                {
                    Pick(salutations),
                    $"Welcome back, {userName}.",
                    "Standing by.",
                    $"I'm here, {userName}.",
                    "Ready for input.",
                    "Awaiting command."
                };

            var systemLines = new[]
            {
                "Systems are stable.",
                "Interfaces are ready.",
                "Voice and text are available.",
                "Awaiting your instruction.",
                "All set."
            };

            var offers = new[]
            {
                "What would you like to do?",
                "How can I help?",
                "Give me a command.",
                "Say the word."
            };

            var styleBits = new[]
            {
                "Quietly monitoring in the background.",
                "Keeping things tidy.",
                "Running light and responsive."
            };

            var attempts = 120;
            while (attempts-- > 0)
            {
                var parts = new List<string>();
                parts.Add(Pick(openers));
                if (Chance(0.55)) parts.Add(Pick(systemLines));
                if (Chance(0.25)) parts.Add(Pick(styleBits));
                parts.Add(Pick(offers));

                var text = string.Join(" ", parts.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Trim()));
                text = text.Replace("  ", " ").Trim();
                if (text.Length < 10) continue;
                if (recentGlobal.Contains(text, StringComparer.Ordinal) || recentCtx.Contains(text, StringComparer.Ordinal))
                    continue;
                return text;
            }

            return null;
        }

        private static string? TryGetRichGreeting(GreetingContext context, string userName, List<string> recentGlobal, List<string> recentCtx)
        {
            try
            {
                var settings = AtlasAI.Settings.SettingsStore.Current;
                var personalityType = ResolveLegacyPersonalityType(settings?.PersonalitySelected);
                var preferredName = string.IsNullOrWhiteSpace(settings?.PreferredName) ? userName : settings.PreferredName;
                var greeting = GreetingManager.GetRichGreeting(
                    personalityType,
                    DateTime.Now,
                    DateTime.MinValue,
                    firstLaunchToday: context == GreetingContext.Startup,
                    salutationPreference: settings?.SalutationPreference ?? "auto",
                    preferredName: preferredName ?? userName);

                if (string.IsNullOrWhiteSpace(greeting))
                    return null;

                if (recentGlobal.Contains(greeting, StringComparer.Ordinal) || recentCtx.Contains(greeting, StringComparer.Ordinal))
                    return null;

                return greeting;
            }
            catch
            {
                return null;
            }
        }

        private static PersonalityType ResolveLegacyPersonalityType(string? selectedId)
        {
            if (!string.IsNullOrWhiteSpace(selectedId))
            {
                if (Enum.TryParse<PersonalityType>(selectedId, ignoreCase: true, out var parsed))
                    return parsed;

                var definition = PersonalityRegistry.GetById(selectedId);
                if (definition != null)
                    return definition.ToProfile().Type;
            }

            return PersonalityType.Butler;
        }

        private static bool Chance(double p) => Random.Shared.NextDouble() < p;

        private static string Pick(string[] options)
        {
            if (options == null || options.Length == 0) return "";
            return options[Random.Shared.Next(options.Length)];
        }
        
        private static string PickNonRecent(string[] options, List<string> recentGlobal, List<string> recentCtx)
        {
            if (options == null || options.Length == 0) return "";
            var attempts = Math.Min(40, options.Length * 3);
            while (attempts-- > 0)
            {
                var pick = options[Random.Shared.Next(options.Length)];
                if (string.IsNullOrWhiteSpace(pick)) continue;
                if (recentGlobal.Contains(pick, StringComparer.Ordinal) || recentCtx.Contains(pick, StringComparer.Ordinal))
                    continue;
                return pick;
            }
            return options[Random.Shared.Next(options.Length)];
        }

        private static GreetingHistory Load()
        {
            try
            {
                if (!File.Exists(HistoryPath)) return new GreetingHistory();
                var json = File.ReadAllText(HistoryPath);
                var parsed = JsonSerializer.Deserialize<GreetingHistory>(json);
                return parsed ?? new GreetingHistory();
            }
            catch
            {
                return new GreetingHistory();
            }
        }

        private static void Save(GreetingHistory history)
        {
            try
            {
                var dir = Path.GetDirectoryName(HistoryPath);
                if (!string.IsNullOrWhiteSpace(dir))
                    Directory.CreateDirectory(dir);
                var json = JsonSerializer.Serialize(history, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(HistoryPath, json);
            }
            catch
            {
            }
        }

        private static void AddUnique(List<string> list, string value, int max)
        {
            if (list == null) return;
            if (string.IsNullOrWhiteSpace(value)) return;
            if (!list.Contains(value, StringComparer.Ordinal))
                list.Add(value);
            while (list.Count > max)
                list.RemoveAt(0);
        }
    }
}
