using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace AtlasAI.Agent
{
    /// <summary>
    /// Quick Notes - Voice-activated note taking.
    /// "Note: remember to buy milk"
    /// "Add to shopping list: eggs, bread, cheese"
    /// </summary>
    public class QuickNotes
    {
        private static QuickNotes? _instance;
        public static QuickNotes Instance => _instance ??= new QuickNotes();
        
        private readonly string _notesFile;
        private NoteStore _store = new();
        
        private QuickNotes()
        {
            var appData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "AtlasAI");
            Directory.CreateDirectory(appData);
            _notesFile = Path.Combine(appData, "quick_notes.json");
            LoadNotes();
        }
        
        /// <summary>
        /// Add a quick note
        /// </summary>
        public string AddNote(string content, string? category = null)
        {
            var note = new Note
            {
                Id = Guid.NewGuid().ToString("N")[..8],
                Content = content,
                Category = category ?? "general",
                CreatedAt = DateTime.Now
            };
            
            _store.Notes.Add(note);
            SaveNotes();
            
            return $"📝 Noted: \"{content}\"";
        }
        
        /// <summary>
        /// Add item to a list
        /// </summary>
        public string AddToList(string listName, string item)
        {
            var list = listName.ToLowerInvariant();
            if (!_store.Lists.ContainsKey(list))
                _store.Lists[list] = new List<string>();
            
            _store.Lists[list].Add(item);
            SaveNotes();
            
            return $"✓ Added '{item}' to {listName} list";
        }
        
        /// <summary>
        /// Add multiple items to a list
        /// </summary>
        public string AddMultipleToList(string listName, IEnumerable<string> items)
        {
            var list = listName.ToLowerInvariant();
            if (!_store.Lists.ContainsKey(list))
                _store.Lists[list] = new List<string>();
            
            var itemList = items.ToList();
            _store.Lists[list].AddRange(itemList);
            SaveNotes();
            
            return $"✓ Added {itemList.Count} items to {listName} list";
        }
        
        /// <summary>
        /// Get a list
        /// </summary>
        public string GetList(string listName)
        {
            var list = listName.ToLowerInvariant();
            if (!_store.Lists.TryGetValue(list, out var items) || !items.Any())
                return $"📋 {listName} list is empty";
            
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"📋 **{listName} List:**\n");
            for (int i = 0; i < items.Count; i++)
            {
                sb.AppendLine($"  {i + 1}. {items[i]}");
            }
            return sb.ToString();
        }
        
        /// <summary>
        /// Remove item from list
        /// </summary>
        public string RemoveFromList(string listName, string item)
        {
            var list = listName.ToLowerInvariant();
            if (!_store.Lists.TryGetValue(list, out var items))
                return $"❌ {listName} list not found";
            
            // Try exact match first
            if (items.Remove(item))
            {
                SaveNotes();
                return $"✓ Removed '{item}' from {listName}";
            }
            
            // Try partial match
            var match = items.FirstOrDefault(i => i.ToLower().Contains(item.ToLower()));
            if (match != null && items.Remove(match))
            {
                SaveNotes();
                return $"✓ Removed '{match}' from {listName}";
            }
            
            // Try by number
            if (int.TryParse(item, out var index) && index > 0 && index <= items.Count)
            {
                var removed = items[index - 1];
                items.RemoveAt(index - 1);
                SaveNotes();
                return $"✓ Removed '{removed}' from {listName}";
            }
            
            return $"❌ '{item}' not found in {listName}";
        }
        
        /// <summary>
        /// Clear a list
        /// </summary>
        public string ClearList(string listName)
        {
            var list = listName.ToLowerInvariant();
            if (_store.Lists.ContainsKey(list))
            {
                _store.Lists[list].Clear();
                SaveNotes();
                return $"✓ Cleared {listName} list";
            }
            return $"❌ {listName} list not found";
        }
        
        /// <summary>
        /// Get recent notes
        /// </summary>
        public string GetRecentNotes(int count = 10)
        {
            var recent = _store.Notes
                .OrderByDescending(n => n.CreatedAt)
                .Take(count)
                .ToList();
            
            if (!recent.Any())
                return "📝 No notes yet. Say 'note: [something]' to add one!";
            
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("📝 **Recent Notes:**\n");
            
            foreach (var note in recent)
            {
                var time = note.CreatedAt.ToString("MMM d, h:mm tt");
                sb.AppendLine($"• {note.Content}");
                sb.AppendLine($"  _{time}_\n");
            }
            
            return sb.ToString();
        }
        
        /// <summary>
        /// Search notes
        /// </summary>
        public string SearchNotes(string query)
        {
            var lower = query.ToLowerInvariant();
            var matches = _store.Notes
                .Where(n => n.Content.ToLower().Contains(lower))
                .OrderByDescending(n => n.CreatedAt)
                .Take(10)
                .ToList();
            
            if (!matches.Any())
                return $"No notes found matching '{query}'";
            
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"🔍 **Notes matching '{query}':**\n");
            
            foreach (var note in matches)
            {
                sb.AppendLine($"• {note.Content}");
            }
            
            return sb.ToString();
        }
        
        /// <summary>
        /// List all lists
        /// </summary>
        public string ListAllLists()
        {
            if (!_store.Lists.Any())
                return "No lists yet. Say 'add to [list name]: [item]' to create one!";
            
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("📋 **Your Lists:**\n");
            
            foreach (var list in _store.Lists)
            {
                sb.AppendLine($"• **{list.Key}** ({list.Value.Count} items)");
            }
            
            return sb.ToString();
        }
        
        /// <summary>
        /// Delete a note
        /// </summary>
        public string DeleteNote(string query)
        {
            var lower = query.ToLowerInvariant();
            var note = _store.Notes.FirstOrDefault(n => n.Content.ToLower().Contains(lower));
            
            if (note == null)
                return $"❌ No note found matching '{query}'";
            
            _store.Notes.Remove(note);
            SaveNotes();
            
            return $"✓ Deleted note: '{note.Content}'";
        }
        
        /// <summary>
        /// Parse note command
        /// </summary>
        public (string? Action, string? ListName, string? Content) ParseNoteCommand(string input)
        {
            var lower = input.ToLowerInvariant();
            
            // "note: X" or "note X"
            if (lower.StartsWith("note:") || lower.StartsWith("note "))
            {
                var content = input.Substring(lower.StartsWith("note:") ? 5 : 5).Trim();
                return ("add_note", null, content);
            }
            
            // "add to X list: Y" or "add Y to X list"
            var match = System.Text.RegularExpressions.Regex.Match(input, 
                @"add\s+to\s+(\w+)\s+list[:\s]+(.+)", 
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (match.Success)
                return ("add_to_list", match.Groups[1].Value, match.Groups[2].Value);
            
            match = System.Text.RegularExpressions.Regex.Match(input,
                @"add\s+(.+?)\s+to\s+(?:my\s+)?(\w+)\s+list",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (match.Success)
                return ("add_to_list", match.Groups[2].Value, match.Groups[1].Value);
            
            // "show X list" or "what's on my X list"
            match = System.Text.RegularExpressions.Regex.Match(input,
                @"(?:show|get|read|what'?s?\s+(?:on|in)\s+(?:my\s+)?)\s*(\w+)\s+list",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (match.Success)
                return ("get_list", match.Groups[1].Value, null);
            
            // "remove X from Y list"
            match = System.Text.RegularExpressions.Regex.Match(input,
                @"(?:remove|delete|cross off)\s+(.+?)\s+from\s+(?:my\s+)?(\w+)\s+list",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (match.Success)
                return ("remove_from_list", match.Groups[2].Value, match.Groups[1].Value);
            
            // "clear X list"
            match = System.Text.RegularExpressions.Regex.Match(input,
                @"clear\s+(?:my\s+)?(\w+)\s+list",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (match.Success)
                return ("clear_list", match.Groups[1].Value, null);
            
            // "show notes" or "my notes"
            if (lower.Contains("show notes") || lower.Contains("my notes") || lower == "notes")
                return ("show_notes", null, null);
            
            // "show lists" or "my lists"
            if (lower.Contains("show lists") || lower.Contains("my lists") || lower == "lists")
                return ("show_lists", null, null);
            
            return (null, null, null);
        }
        
        private void LoadNotes()
        {
            try
            {
                if (File.Exists(_notesFile))
                {
                    var json = File.ReadAllText(_notesFile);
                    _store = JsonSerializer.Deserialize<NoteStore>(json) ?? new();
                }
            }
            catch { _store = new(); }
        }
        
        private void SaveNotes()
        {
            try
            {
                var json = JsonSerializer.Serialize(_store, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_notesFile, json);
            }
            catch { }
        }
    }
    
    internal class NoteStore
    {
        public List<Note> Notes { get; set; } = new();
        public Dictionary<string, List<string>> Lists { get; set; } = new();
    }
    
    public class Note
    {
        public string Id { get; set; } = "";
        public string Content { get; set; } = "";
        public string Category { get; set; } = "general";
        public DateTime CreatedAt { get; set; }
    }
}
