using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace AtlasAI.Agent
{
    /// <summary>
    /// Conversation memory - remembers important context across sessions.
    /// Stores facts, preferences, and conversation highlights.
    /// </summary>
    public class ConversationMemory
    {
        private static ConversationMemory? _instance;
        public static ConversationMemory Instance => _instance ??= new ConversationMemory();
        
        private readonly string _memoryFile;
        private MemoryStore _store;
        private bool _isDirty;
        
        private ConversationMemory()
        {
            var appData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "AtlasAI");
            Directory.CreateDirectory(appData);
            _memoryFile = Path.Combine(appData, "conversation_memory.json");
            _store = LoadMemory();
        }
        
        /// <summary>
        /// Remember a fact about the user
        /// </summary>
        public void RememberFact(string category, string fact)
        {
            if (!_store.Facts.ContainsKey(category))
                _store.Facts[category] = new List<string>();
            
            if (!_store.Facts[category].Contains(fact))
            {
                _store.Facts[category].Add(fact);
                _isDirty = true;
            }
        }
        
        /// <summary>
        /// Remember a user preference
        /// </summary>
        public void RememberPreference(string key, string value)
        {
            _store.Preferences[key] = value;
            _isDirty = true;
        }
        
        /// <summary>
        /// Remember a topic that was discussed
        /// </summary>
        public void RememberTopic(string topic, string summary)
        {
            _store.Topics[topic] = new TopicMemory
            {
                Summary = summary,
                LastDiscussed = DateTime.Now,
                DiscussionCount = _store.Topics.GetValueOrDefault(topic)?.DiscussionCount + 1 ?? 1
            };
            _isDirty = true;
        }
        
        /// <summary>
        /// Remember a name (person, pet, etc.)
        /// </summary>
        public void RememberName(string type, string name)
        {
            _store.Names[type] = name;
            _isDirty = true;
        }
        
        /// <summary>
        /// Get all facts about a category
        /// </summary>
        public List<string> GetFacts(string category)
        {
            return _store.Facts.GetValueOrDefault(category) ?? new List<string>();
        }
        
        /// <summary>
        /// Get a preference
        /// </summary>
        public string? GetPreference(string key)
        {
            return _store.Preferences.GetValueOrDefault(key);
        }
        
        /// <summary>
        /// Get a remembered name
        /// </summary>
        public string? GetName(string type)
        {
            return _store.Names.GetValueOrDefault(type);
        }
        
        /// <summary>
        /// Get context summary for AI
        /// </summary>
        public string GetContextSummary()
        {
            var parts = new List<string>();
            
            // User's name
            if (_store.Names.TryGetValue("user", out var userName))
                parts.Add($"User's name: {userName}");
            
            // Key preferences
            foreach (var pref in _store.Preferences.Take(5))
                parts.Add($"{pref.Key}: {pref.Value}");
            
            // Recent topics
            var recentTopics = _store.Topics
                .OrderByDescending(t => t.Value.LastDiscussed)
                .Take(3)
                .Select(t => t.Key);
            if (recentTopics.Any())
                parts.Add($"Recent topics: {string.Join(", ", recentTopics)}");
            
            // Key facts
            foreach (var category in _store.Facts.Take(3))
            {
                if (category.Value.Any())
                    parts.Add($"{category.Key}: {string.Join(", ", category.Value.Take(3))}");
            }
            
            return parts.Any() ? string.Join("\n", parts) : "";
        }
        
        /// <summary>
        /// Extract and remember information from a message
        /// </summary>
        public void ProcessMessage(string message)
        {
            var lower = message.ToLowerInvariant();
            
            // Extract name introductions
            ExtractName(message, lower);
            
            // Extract preferences
            ExtractPreferences(message, lower);
            
            // Extract facts
            ExtractFacts(message, lower);
            
            // Auto-save if dirty
            if (_isDirty)
                _ = SaveAsync();
        }
        
        private void ExtractName(string message, string lower)
        {
            // "My name is X" / "I'm X" / "Call me X"
            var patterns = new[]
            {
                (@"my name is (\w+)", "user"),
                (@"i'm (\w+)", "user"),
                (@"call me (\w+)", "user"),
                (@"i am (\w+)", "user"),
                (@"my (?:dog|cat|pet) (?:is called|is named|named) (\w+)", "pet"),
                (@"my (?:wife|husband|partner) (?:is called|is named|named|is) (\w+)", "partner"),
            };
            
            foreach (var (pattern, type) in patterns)
            {
                var match = System.Text.RegularExpressions.Regex.Match(lower, pattern);
                if (match.Success)
                {
                    var name = message.Substring(match.Groups[1].Index, match.Groups[1].Length);
                    // Capitalize first letter
                    name = char.ToUpper(name[0]) + name.Substring(1);
                    RememberName(type, name);
                }
            }
        }
        
        private void ExtractPreferences(string message, string lower)
        {
            // "I prefer X" / "I like X" / "I don't like X"
            if (lower.Contains("i prefer "))
            {
                var idx = lower.IndexOf("i prefer ") + 9;
                var pref = message.Substring(idx).Split(new[] { '.', ',', '!' }, 2)[0].Trim();
                if (pref.Length > 2 && pref.Length < 50)
                    RememberPreference("preference", pref);
            }
            
            if (lower.Contains("i like ") && !lower.Contains("would like"))
            {
                var idx = lower.IndexOf("i like ") + 7;
                var like = message.Substring(idx).Split(new[] { '.', ',', '!' }, 2)[0].Trim();
                if (like.Length > 2 && like.Length < 50)
                    RememberFact("likes", like);
            }
            
            if (lower.Contains("i don't like ") || lower.Contains("i hate "))
            {
                var pattern = lower.Contains("i don't like ") ? "i don't like " : "i hate ";
                var idx = lower.IndexOf(pattern) + pattern.Length;
                var dislike = message.Substring(idx).Split(new[] { '.', ',', '!' }, 2)[0].Trim();
                if (dislike.Length > 2 && dislike.Length < 50)
                    RememberFact("dislikes", dislike);
            }
            
            // Location
            if (lower.Contains("i live in ") || lower.Contains("i'm from "))
            {
                var pattern = lower.Contains("i live in ") ? "i live in " : "i'm from ";
                var idx = lower.IndexOf(pattern) + pattern.Length;
                var location = message.Substring(idx).Split(new[] { '.', ',', '!' }, 2)[0].Trim();
                if (location.Length > 2 && location.Length < 50)
                    RememberPreference("location", location);
            }
            
            // Job
            if (lower.Contains("i work as ") || lower.Contains("i'm a ") || lower.Contains("i am a "))
            {
                string pattern;
                if (lower.Contains("i work as ")) pattern = "i work as ";
                else if (lower.Contains("i'm a ")) pattern = "i'm a ";
                else pattern = "i am a ";
                
                var idx = lower.IndexOf(pattern) + pattern.Length;
                var job = message.Substring(idx).Split(new[] { '.', ',', '!' }, 2)[0].Trim();
                if (job.Length > 2 && job.Length < 50)
                    RememberPreference("job", job);
            }
        }
        
        private void ExtractFacts(string message, string lower)
        {
            // Favorite things
            if (lower.Contains("favorite ") || lower.Contains("favourite "))
            {
                var pattern = lower.Contains("favorite ") ? "favorite " : "favourite ";
                var idx = lower.IndexOf(pattern);
                var rest = message.Substring(idx + pattern.Length);
                var parts = rest.Split(new[] { " is ", " are " }, 2, StringSplitOptions.None);
                if (parts.Length == 2)
                {
                    var thing = parts[0].Trim();
                    var value = parts[1].Split(new[] { '.', ',', '!' }, 2)[0].Trim();
                    if (thing.Length < 30 && value.Length < 50)
                        RememberPreference($"favorite_{thing}", value);
                }
            }
        }
        
        /// <summary>
        /// Save memory to disk
        /// </summary>
        public async Task SaveAsync()
        {
            try
            {
                _store.LastUpdated = DateTime.Now;
                var json = JsonSerializer.Serialize(_store, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(_memoryFile, json);
                _isDirty = false;
            }
            catch { }
        }
        
        /// <summary>
        /// Clear all memory
        /// </summary>
        public async Task ClearAsync()
        {
            _store = new MemoryStore();
            await SaveAsync();
        }
        
        /// <summary>
        /// Get memory summary for display
        /// </summary>
        public string GetMemorySummary()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("🧠 **What I Remember:**\n");
            
            if (_store.Names.Any())
            {
                sb.AppendLine("**Names:**");
                foreach (var name in _store.Names)
                    sb.AppendLine($"  • {name.Key}: {name.Value}");
            }
            
            if (_store.Preferences.Any())
            {
                sb.AppendLine("\n**Preferences:**");
                foreach (var pref in _store.Preferences.Take(10))
                    sb.AppendLine($"  • {pref.Key}: {pref.Value}");
            }
            
            if (_store.Facts.Any())
            {
                sb.AppendLine("\n**Facts:**");
                foreach (var cat in _store.Facts.Take(5))
                {
                    sb.AppendLine($"  • {cat.Key}: {string.Join(", ", cat.Value.Take(5))}");
                }
            }
            
            if (_store.Topics.Any())
            {
                sb.AppendLine("\n**Topics Discussed:**");
                foreach (var topic in _store.Topics.OrderByDescending(t => t.Value.LastDiscussed).Take(5))
                {
                    sb.AppendLine($"  • {topic.Key} ({topic.Value.DiscussionCount}x)");
                }
            }
            
            return sb.ToString();
        }
        
        private MemoryStore LoadMemory()
        {
            try
            {
                if (File.Exists(_memoryFile))
                {
                    var json = File.ReadAllText(_memoryFile);
                    return JsonSerializer.Deserialize<MemoryStore>(json) ?? new MemoryStore();
                }
            }
            catch { }
            return new MemoryStore();
        }
    }
    
    internal class MemoryStore
    {
        public Dictionary<string, List<string>> Facts { get; set; } = new();
        public Dictionary<string, string> Preferences { get; set; } = new();
        public Dictionary<string, string> Names { get; set; } = new();
        public Dictionary<string, TopicMemory> Topics { get; set; } = new();
        public DateTime LastUpdated { get; set; }
    }
    
    internal class TopicMemory
    {
        public string Summary { get; set; } = "";
        public DateTime LastDiscussed { get; set; }
        public int DiscussionCount { get; set; }
    }
}
