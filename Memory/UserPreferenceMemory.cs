using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace AtlasAI.Memory
{
    /// <summary>
    /// Manages user preferences - learns from conversations and explicit settings
    /// Examples: "Don't use Canva", "I prefer dark mode", "Always use Python 3.11"
    /// </summary>
    public class UserPreferenceMemory
    {
        private readonly LongTermMemoryStore _store;
        private static UserPreferenceMemory? _instance;

        public static UserPreferenceMemory Instance => _instance ??= new UserPreferenceMemory();

        private UserPreferenceMemory()
        {
            _store = LongTermMemoryStore.Instance;
        }

        #region Preference Categories

        public static class Categories
        {
            public const string Tools = "tools";           // Preferred tools/apps
            public const string Coding = "coding";         // Coding preferences
            public const string Communication = "comm";    // How Atlas should communicate
            public const string Privacy = "privacy";       // Privacy settings
            public const string Workflow = "workflow";     // Workflow preferences
            public const string System = "system";         // System preferences
        }

        #endregion

        #region Learn from Conversation

        /// <summary>
        /// Analyze user message for preferences to learn
        /// </summary>
        public async Task LearnFromMessageAsync(string userMessage)
        {
            var messageLower = userMessage.ToLower();

            // Pattern: "Don't use X" / "Never use X" / "Stop using X"
            var dontUseMatch = Regex.Match(messageLower, @"(?:don'?t|never|stop)\s+use\s+(\w+)", RegexOptions.IgnoreCase);
            if (dontUseMatch.Success)
            {
                var tool = dontUseMatch.Groups[1].Value;
                await SetPreferenceAsync($"avoid_{tool}", "true", Categories.Tools);
                await _store.RecordCorrectionAsync($"use {tool}", $"avoid {tool} (user preference)", userMessage);
                System.Diagnostics.Debug.WriteLine($"[Preferences] Learned: Avoid using {tool}");
            }

            // Pattern: "Use X instead of Y" / "Prefer X over Y"
            var preferMatch = Regex.Match(messageLower, @"(?:use|prefer)\s+(\w+)\s+(?:instead of|over|rather than)\s+(\w+)", RegexOptions.IgnoreCase);
            if (preferMatch.Success)
            {
                var preferred = preferMatch.Groups[1].Value;
                var avoided = preferMatch.Groups[2].Value;
                await SetPreferenceAsync($"prefer_{preferred}_over_{avoided}", "true", Categories.Tools);
                await _store.RecordCorrectionAsync($"use {avoided}", $"use {preferred} instead", userMessage);
                System.Diagnostics.Debug.WriteLine($"[Preferences] Learned: Prefer {preferred} over {avoided}");
            }

            // Pattern: "I like X" / "I prefer X" / "I always use X"
            var likeMatch = Regex.Match(messageLower, @"i\s+(?:like|prefer|always use|love)\s+(\w+)", RegexOptions.IgnoreCase);
            if (likeMatch.Success)
            {
                var liked = likeMatch.Groups[1].Value;
                await SetPreferenceAsync($"likes_{liked}", "true", Categories.Tools);
                System.Diagnostics.Debug.WriteLine($"[Preferences] Learned: User likes {liked}");
            }

            // Pattern: "Call me X" / "My name is X"
            var nameMatch = Regex.Match(userMessage, @"(?:call me|my name is|i'm|i am)\s+([A-Z][a-z]+)", RegexOptions.IgnoreCase);
            if (nameMatch.Success)
            {
                var name = nameMatch.Groups[1].Value;
                await SetPreferenceAsync("user_name", name, Categories.Communication);
                await _store.LearnFactAsync($"User's name is {name}", "personal", 1.0, "explicit");
                System.Diagnostics.Debug.WriteLine($"[Preferences] Learned: User's name is {name}");
            }

            // Pattern: "Be more X" / "Be less X" (communication style)
            var styleMatch = Regex.Match(messageLower, @"be\s+(more|less)\s+(\w+)", RegexOptions.IgnoreCase);
            if (styleMatch.Success)
            {
                var direction = styleMatch.Groups[1].Value;
                var style = styleMatch.Groups[2].Value;
                await SetPreferenceAsync($"communication_{style}", direction, Categories.Communication);
                System.Diagnostics.Debug.WriteLine($"[Preferences] Learned: Be {direction} {style}");
            }

            // Pattern: "I work with X" / "I use X for work"
            var workMatch = Regex.Match(messageLower, @"i\s+(?:work with|use .* for work|work on)\s+(.+?)(?:\.|$)", RegexOptions.IgnoreCase);
            if (workMatch.Success)
            {
                var workContext = workMatch.Groups[1].Value.Trim();
                await _store.LearnFactAsync($"User works with {workContext}", "work", 0.9, "conversation");
            }

            // Pattern: Programming language preferences
            var langMatch = Regex.Match(messageLower, @"(?:i\s+(?:use|code in|program in|prefer)|my (?:main|primary) language is)\s+(c#|python|javascript|typescript|java|go|rust|c\+\+|ruby|php)", RegexOptions.IgnoreCase);
            if (langMatch.Success)
            {
                var lang = langMatch.Groups[1].Value;
                await SetPreferenceAsync("primary_language", lang, Categories.Coding);
                await _store.LearnFactAsync($"User's primary programming language is {lang}", "coding", 1.0, "explicit");
            }
        }

        #endregion

        #region Preference Management

        /// <summary>
        /// Set a preference
        /// </summary>
        public async Task SetPreferenceAsync(string key, string value, string category = "general")
        {
            await _store.SetPreferenceAsync(key, value, category, "learned");
        }

        /// <summary>
        /// Get a preference
        /// </summary>
        public async Task<string?> GetPreferenceAsync(string key)
        {
            return await _store.GetPreferenceAsync(key);
        }

        /// <summary>
        /// Check if user wants to avoid a tool
        /// </summary>
        public async Task<bool> ShouldAvoidToolAsync(string toolName)
        {
            var avoidPref = await _store.GetPreferenceAsync($"avoid_{toolName.ToLower()}");
            return avoidPref == "true";
        }

        /// <summary>
        /// Get preferred alternative for a tool
        /// </summary>
        public async Task<string?> GetPreferredAlternativeAsync(string toolName)
        {
            var correction = await _store.GetCorrectionForAsync($"use {toolName.ToLower()}");
            if (correction != null)
            {
                // Extract the preferred tool from correction
                var match = Regex.Match(correction, @"use\s+(\w+)", RegexOptions.IgnoreCase);
                if (match.Success)
                    return match.Groups[1].Value;
            }
            return null;
        }

        /// <summary>
        /// Get user's name if known
        /// </summary>
        public async Task<string?> GetUserNameAsync()
        {
            return await _store.GetPreferenceAsync("user_name");
        }

        /// <summary>
        /// Get all preferences for a category
        /// </summary>
        public async Task<Dictionary<string, string>> GetCategoryPreferencesAsync(string category)
        {
            return await _store.GetPreferencesByCategoryAsync(category);
        }

        #endregion

        #region Communication Style

        /// <summary>
        /// Get communication style adjustments
        /// </summary>
        public async Task<CommunicationStyle> GetCommunicationStyleAsync()
        {
            var style = new CommunicationStyle();
            var prefs = await _store.GetPreferencesByCategoryAsync(Categories.Communication);

            foreach (var pref in prefs)
            {
                if (pref.Key.StartsWith("communication_"))
                {
                    var trait = pref.Key.Replace("communication_", "");
                    var direction = pref.Value;

                    switch (trait.ToLower())
                    {
                        case "verbose":
                            style.Verbosity = direction == "more" ? 1.5 : 0.5;
                            break;
                        case "formal":
                            style.Formality = direction == "more" ? 1.5 : 0.5;
                            break;
                        case "technical":
                            style.TechnicalLevel = direction == "more" ? 1.5 : 0.5;
                            break;
                        case "friendly":
                            style.Friendliness = direction == "more" ? 1.5 : 0.5;
                            break;
                        case "concise":
                            style.Verbosity = direction == "more" ? 0.5 : 1.5;
                            break;
                    }
                }
            }

            if (prefs.TryGetValue("user_name", out var name))
                style.UserName = name;

            return style;
        }

        #endregion
    }

    /// <summary>
    /// Communication style preferences
    /// </summary>
    public class CommunicationStyle
    {
        public double Verbosity { get; set; } = 1.0;      // 0.5 = concise, 1.5 = verbose
        public double Formality { get; set; } = 1.0;      // 0.5 = casual, 1.5 = formal
        public double TechnicalLevel { get; set; } = 1.0; // 0.5 = simple, 1.5 = technical
        public double Friendliness { get; set; } = 1.0;   // 0.5 = professional, 1.5 = friendly
        public string? UserName { get; set; }

        public string GetStylePrompt()
        {
            var prompts = new List<string>();

            if (Verbosity < 0.8) prompts.Add("Be concise and brief");
            else if (Verbosity > 1.2) prompts.Add("Provide detailed explanations");

            if (Formality < 0.8) prompts.Add("Use casual, friendly language");
            else if (Formality > 1.2) prompts.Add("Use professional, formal language");

            if (TechnicalLevel < 0.8) prompts.Add("Explain things simply, avoid jargon");
            else if (TechnicalLevel > 1.2) prompts.Add("Use technical terminology freely");

            if (Friendliness > 1.2) prompts.Add("Be warm and personable");

            if (!string.IsNullOrEmpty(UserName))
                prompts.Add($"Address the user as {UserName}");

            return prompts.Count > 0 
                ? "Communication style: " + string.Join(". ", prompts) + "."
                : "";
        }
    }
}
