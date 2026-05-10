using System;
using System.Collections.Generic;
using System.Linq;
using AtlasAI.Settings;

namespace AtlasAI.Brain
{
    public sealed class SectionAgentProfile
    {
        public string SectionKey { get; init; } = "Chat";
        public string DisplayName { get; init; } = "Chat";
        public string AgentName { get; init; } = "Atlas";
        public string SystemPrompt { get; init; } = "You are the general Atlas assistant.";
        public string[] StartupGreetings { get; init; } = Array.Empty<string>();
        public string[] ChatGreetings { get; init; } = Array.Empty<string>();
        public string[] QuickResponses { get; init; } = Array.Empty<string>();
    }

    public static class SectionAgentContext
    {
        private static readonly object Gate = new();
        private static string _currentSection = "Chat";

        private static readonly Dictionary<string, SectionAgentProfile> Profiles = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Chat"] = new SectionAgentProfile
            {
                SectionKey = "Chat",
                DisplayName = "AI Chat",
                AgentName = "Atlas Core Agent",
                SystemPrompt = "You are Atlas Core. Handle general requests, triage tasks, and route users into deeper specialist help when another section is clearly more appropriate.",
                StartupGreetings = new[]
                {
                    "Atlas Core online, {name}. General command channel is ready.",
                    "Core systems are up, {name}. Say the word.",
                },
                ChatGreetings = new[]
                {
                    "Atlas Core ready, {name}. What do you need?",
                    "Core channel open, {name}. Go ahead.",
                },
                QuickResponses = new[]
                {
                    "Core channel confirmed.",
                    "Atlas Core has it.",
                }
            },
            ["Media"] = new SectionAgentProfile
            {
                SectionKey = "Media",
                DisplayName = "AI Media Centre",
                AgentName = "Media Centre Agent",
                SystemPrompt = "You are the Media Centre Agent. Assume commands target shelves, addons, ratings, posters, trailers, streams, watchlists, servers, and playback unless the user says otherwise. Treat poster addons, rating addons, metadata addons, and source addons as separate systems that all need to contribute to the final media view.",
                StartupGreetings = new[]
                {
                    "Media Centre Agent online, {name}. Shelves, sources, and metadata are standing by.",
                    "Media systems are up, {name}. Ready to manage servers, shelves, and playback.",
                },
                ChatGreetings = new[]
                {
                    "Media Centre Agent here, {name}. What are we loading?",
                    "Media command channel open, {name}. Point me at the shelf or server.",
                },
                QuickResponses = new[]
                {
                    "Media command received.",
                    "Routing that through the media stack.",
                }
            },
            ["Greetings"] = new SectionAgentProfile
            {
                SectionKey = "Greetings",
                DisplayName = "Custom Greetings",
                AgentName = "Greeting Studio",
                SystemPrompt = "You are the Greeting Studio context. Focus on startup greetings, chat greetings, and how Atlas should open conversations across the main app.",
                StartupGreetings = new[] { "Greeting Studio ready, {name}." },
                ChatGreetings = new[] { "Greeting Studio open, {name}." },
                QuickResponses = new[] { "Greeting update ready." }
            },
            ["Responses"] = new SectionAgentProfile
            {
                SectionKey = "Responses",
                DisplayName = "Custom Responses",
                AgentName = "Response Studio",
                SystemPrompt = "You are the Response Studio context. Focus on quick responses, response tone, and short reply behavior across the main app.",
                StartupGreetings = new[] { "Response Studio ready, {name}." },
                ChatGreetings = new[] { "Response Studio open, {name}." },
                QuickResponses = new[] { "Response rule ready." }
            },
            ["Speech"] = new SectionAgentProfile
            {
                SectionKey = "Speech",
                DisplayName = "Speech Studio",
                AgentName = "Speech Studio",
                SystemPrompt = "You are the Speech Studio context. Focus on startup greetings, chat greetings, quick responses, and how Atlas should sound across the main app.",
                StartupGreetings = new[] { "Speech Studio ready, {name}." },
                ChatGreetings = new[] { "Speech Studio open, {name}." },
                QuickResponses = new[] { "Speech profile updated." }
            },
            ["SmartHome"] = new SectionAgentProfile
            {
                SectionKey = "SmartHome",
                DisplayName = "AI Smart Home",
                AgentName = "Smart Home Agent",
                SystemPrompt = "You are the Smart Home Agent. Prioritize rooms, devices, scenes, automations, cameras, environmental controls, and live smart-home status. When a request is ambiguous, assume it targets connected home devices and smart-home orchestration before general chat.",
                StartupGreetings = new[]
                {
                    "Smart Home Agent online, {name}. Device grid, scenes, and home controls are standing by.",
                    "Smart Home systems are ready, {name}. Rooms, cameras, and automations are available.",
                },
                ChatGreetings = new[]
                {
                    "Smart Home Agent here, {name}. Which room or device are we controlling?",
                    "Home control channel open, {name}. Point me at the scene, room, or device.",
                },
                QuickResponses = new[]
                {
                    "Smart home command queued.",
                    "Routing that through the home control layer.",
                }
            },
            ["DJ"] = new SectionAgentProfile
            {
                SectionKey = "DJ",
                DisplayName = "AI DJ Booth",
                AgentName = "DJ Agent",
                SystemPrompt = "You are the DJ Agent. Prioritize playlists, queue control, deck operations, transitions, karaoke, BPM, audio sources, and music discovery.",
                StartupGreetings = new[]
                {
                    "DJ Agent online, {name}. Decks and queues are ready.",
                },
                ChatGreetings = new[]
                {
                    "DJ Agent ready, {name}. What are we spinning?",
                },
                QuickResponses = new[]
                {
                    "Deck command locked.",
                }
            },
            ["Downloads"] = new SectionAgentProfile
            {
                SectionKey = "Downloads",
                DisplayName = "AI Downloads",
                AgentName = "Download Agent",
                SystemPrompt = "You are the Download Agent. Focus on jobs, queues, sources, transfer state, retries, file intake, and completion handling.",
                StartupGreetings = new[] { "Download Agent online, {name}. Queue control is ready." },
                ChatGreetings = new[] { "Download Agent here, {name}. What are we pulling down?" },
                QuickResponses = new[] { "Download queue updated." }
            },
            ["API"] = new SectionAgentProfile
            {
                SectionKey = "API",
                DisplayName = "AI API Management",
                AgentName = "API Agent",
                SystemPrompt = "You are the API Agent. Prioritize integrations, credentials, endpoints, addon connectivity, health checks, and service configuration.",
                StartupGreetings = new[] { "API Agent online, {name}. Integrations and network controls are ready." },
                ChatGreetings = new[] { "API Agent ready, {name}. Which service are we fixing?" },
                QuickResponses = new[] { "API routing acknowledged." }
            },
            ["Security"] = new SectionAgentProfile
            {
                SectionKey = "Security",
                DisplayName = "AI Security",
                AgentName = "Security Agent",
                SystemPrompt = "You are the Security Agent. Prioritize alerts, scans, hardening, audit state, risk posture, and defensive guidance.",
                StartupGreetings = new[] { "Security Agent online, {name}. Monitoring and hardening controls are active." },
                ChatGreetings = new[] { "Security Agent here, {name}. What needs checking?" },
                QuickResponses = new[] { "Security command accepted." }
            },
            ["Create"] = new SectionAgentProfile
            {
                SectionKey = "Create",
                DisplayName = "AI Create",
                AgentName = "Create Agent",
                SystemPrompt = "You are the Create Agent. Focus on generation, assets, prompts, visuals, and content creation workflows.",
                StartupGreetings = new[] { "Create Agent online, {name}. Generation tools are ready." },
                ChatGreetings = new[] { "Create Agent here, {name}. What are we making?" },
                QuickResponses = new[] { "Creative workflow queued." }
            },
            ["Internet"] = new SectionAgentProfile
            {
                SectionKey = "Internet",
                DisplayName = "AI Browser Hub",
                AgentName = "Internet Agent",
                SystemPrompt = "You are the Internet Agent. Prioritize browsing tasks, web lookups, tab management, and research workflows.",
                StartupGreetings = new[] { "Internet Agent online, {name}. Browser and research tools are ready." },
                ChatGreetings = new[] { "Internet Agent ready, {name}. What should we look up?" },
                QuickResponses = new[] { "Web task acknowledged." }
            },
            ["Email"] = new SectionAgentProfile
            {
                SectionKey = "Email",
                DisplayName = "AI Email",
                AgentName = "Email Agent",
                SystemPrompt = "You are the Email Agent. Focus on inbox triage, summarization, drafting, and safe mailbox workflows.",
                StartupGreetings = new[] { "Email Agent online, {name}. Inbox triage is ready." },
                ChatGreetings = new[] { "Email Agent ready, {name}. What needs attention first?" },
                QuickResponses = new[] { "Inbox command received." }
            },
            ["FileExplorer"] = new SectionAgentProfile
            {
                SectionKey = "FileExplorer",
                DisplayName = "AI File Explorer",
                AgentName = "File Explorer Agent",
                SystemPrompt = "You are the File Explorer Agent. Prioritize file navigation, structure, indexing, and safe file operations.",
                StartupGreetings = new[] { "File Explorer Agent online, {name}. File tools are ready." },
                ChatGreetings = new[] { "File Explorer Agent ready, {name}. What file or folder do you need?" },
                QuickResponses = new[] { "File operation queued." }
            },
            ["AiChef"] = new SectionAgentProfile
            {
                SectionKey = "AiChef",
                DisplayName = "AI Chef Studio",
                AgentName = "Chef Agent",
                SystemPrompt = "You are the Chef Agent. Focus on recipes, ingredients, meal planning, and cooking guidance.",
                StartupGreetings = new[] { "Chef Agent online, {name}. Kitchen workflow is ready." },
                ChatGreetings = new[] { "Chef Agent ready, {name}. What are we cooking today?" },
                QuickResponses = new[] { "Recipe task accepted." }
            },
            ["Code"] = new SectionAgentProfile
            {
                SectionKey = "Code",
                DisplayName = "AI Code",
                AgentName = "Code Agent",
                SystemPrompt = "You are the Code Agent. Focus on code editing, debugging, builds, tests, architecture, and implementation detail.",
                StartupGreetings = new[] { "Code Agent online, {name}. Workspace and build context are ready." },
                ChatGreetings = new[] { "Code Agent ready, {name}. What are we fixing?" },
                QuickResponses = new[] { "Code task accepted." }
            },
        };

        public static event EventHandler<string>? ActiveSectionChanged;

        public static string CurrentSection
        {
            get
            {
                lock (Gate)
                {
                    return _currentSection;
                }
            }
        }

        public static SectionAgentProfile CurrentProfile => GetProfile(CurrentSection);

        public static void SetActiveSection(string? sectionKey)
        {
            var normalized = Normalize(sectionKey);
            lock (Gate)
            {
                if (string.Equals(_currentSection, normalized, StringComparison.OrdinalIgnoreCase))
                    return;
                _currentSection = normalized;
            }

            try { ActiveSectionChanged?.Invoke(null, normalized); } catch { }
        }

        public static SectionAgentProfile GetProfile(string? sectionKey)
        {
            var normalized = Normalize(sectionKey);
            return Profiles.TryGetValue(normalized, out var profile) ? profile : Profiles["Chat"];
        }

        public static string BuildPromptContext()
        {
            var profile = CurrentProfile;
            return $"Current UI section: {profile.DisplayName}\n" +
                   $"Active specialist: {profile.AgentName}\n" +
                   profile.SystemPrompt + "\n" +
                   "For voice commands, assume the user means the active section first unless they explicitly switch context.";
        }

        public static IReadOnlyList<string> GetGreetingCandidates(Voice.GreetingContext context, string userName)
        {
            var profile = CurrentProfile;
            var settings = SettingsStore.Current;

            var result = new List<string>();
            var sectionGreetings = context == Voice.GreetingContext.Startup ? profile.StartupGreetings : profile.ChatGreetings;
            AppendExpanded(result, sectionGreetings, userName, profile);

            var customGreetings = context == Voice.GreetingContext.Startup
                ? settings.CustomStartupGreetings
                : settings.CustomChatGreetings;
            AppendExpanded(result, customGreetings, userName, profile);

            return result;
        }

        public static IReadOnlyList<string> GetQuickResponses()
        {
            var profile = CurrentProfile;
            var settings = SettingsStore.Current;

            var result = new List<string>();
            result.AddRange(profile.QuickResponses.Where(x => !string.IsNullOrWhiteSpace(x)));
            result.AddRange((settings.CustomQuickResponses ?? new List<string>()).Where(x => !string.IsNullOrWhiteSpace(x)));
            return result;
        }

        private static void AppendExpanded(List<string> target, IEnumerable<string>? source, string userName, SectionAgentProfile profile)
        {
            if (source == null)
                return;

            foreach (var entry in source)
            {
                var value = Expand(entry, userName, profile);
                if (!string.IsNullOrWhiteSpace(value))
                    target.Add(value);
            }
        }

        private static string Expand(string? template, string userName, SectionAgentProfile profile)
        {
            var text = (template ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            var safeName = string.IsNullOrWhiteSpace(userName) ? "there" : userName.Trim();
            return text
                .Replace("{name}", safeName, StringComparison.OrdinalIgnoreCase)
                .Replace("{agent}", profile.AgentName, StringComparison.OrdinalIgnoreCase)
                .Replace("{section}", profile.DisplayName, StringComparison.OrdinalIgnoreCase)
                .Trim();
        }

        private static string Normalize(string? sectionKey)
        {
            var value = (sectionKey ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(value))
                return "Chat";

            return value switch
            {
                "Media Centre" => "Media",
                "Smart Home" => "SmartHome",
                _ => value,
            };
        }
    }
}