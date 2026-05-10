using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace AtlasAI.Understanding
{
    /// <summary>
    /// Stores recent context and references for resolving "that", "it", "again", "same folder" etc.
    /// Maintains short-term memory across messages.
    /// </summary>
    public class ContextStore
    {
        private readonly List<ContextEntry> _history = new();
        private readonly int _maxEntries = 20;
        private readonly string _persistPath;
        
        // Quick access to recent references
        public string? LastActiveFeature { get; private set; }
        public string? LastReferencedFile { get; private set; }
        public string? LastReferencedFolder { get; private set; }
        public string? LastReferencedApp { get; private set; }
        public string? LastScanResult { get; private set; }
        public string? LastSearchQuery { get; private set; }
        public string? LastMusicQuery { get; private set; }
        public string? LastActionOutcome { get; private set; }
        public IntentResult? LastIntent { get; private set; }

        public ContextStore()
        {
            _persistPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "AtlasAI", "context_store.json");
            Load();
        }

        /// <summary>
        /// Add a new context entry from user interaction
        /// </summary>
        public void AddEntry(ContextEntry entry)
        {
            _history.Add(entry);
            
            // Update quick access fields
            if (!string.IsNullOrEmpty(entry.ActiveFeature))
                LastActiveFeature = entry.ActiveFeature;
            
            if (entry.ReferencedFiles.Count > 0)
                LastReferencedFile = entry.ReferencedFiles.Last();
            
            if (entry.ReferencedFolders.Count > 0)
                LastReferencedFolder = entry.ReferencedFolders.Last();
            
            if (entry.ReferencedApps.Count > 0)
                LastReferencedApp = entry.ReferencedApps.Last();
            
            if (!string.IsNullOrEmpty(entry.LastScanResult))
                LastScanResult = entry.LastScanResult;
            
            if (!string.IsNullOrEmpty(entry.LastActionOutcome))
                LastActionOutcome = entry.LastActionOutcome;
            
            if (entry.Intent != null)
            {
                LastIntent = entry.Intent;
                
                // Track specific entity types
                if (entry.Intent.Entities.TryGetValue("query", out var query))
                {
                    if (entry.Intent.Intent.Contains("search"))
                        LastSearchQuery = query;
                    else if (entry.Intent.Intent.Contains("music") || entry.Intent.Intent.Contains("play"))
                        LastMusicQuery = query;
                }
            }
            
            // Trim old entries
            while (_history.Count > _maxEntries)
                _history.RemoveAt(0);
            
            Save();
        }

        /// <summary>
        /// Resolve references like "that", "it", "again", "same folder"
        /// </summary>
        public string ResolveReference(string input)
        {
            var lower = input.ToLower();
            var result = input;
            
            // "it" / "that" / "this" -> last referenced entity
            if (System.Text.RegularExpressions.Regex.IsMatch(lower, @"\b(do it|open it|play it|close it|delete it|that one|this one|the same one)\b"))
            {
                var lastEntity = GetLastReferencedEntity();
                if (!string.IsNullOrEmpty(lastEntity))
                {
                    result = System.Text.RegularExpressions.Regex.Replace(result, @"\b(it|that|this|the same one)\b", lastEntity, 
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    Debug.WriteLine($"[Context] Resolved 'it/that' to '{lastEntity}'");
                }
            }
            
            // "again" -> repeat last action
            if (lower.Contains("again") || lower == "repeat" || lower == "do that again")
            {
                var lastEntry = _history.LastOrDefault();
                if (lastEntry?.Intent != null)
                {
                    var lastAction = lastEntry.Intent.Intent;
                    var lastEntity = GetLastReferencedEntity();
                    if (!string.IsNullOrEmpty(lastAction) && !string.IsNullOrEmpty(lastEntity))
                    {
                        result = $"{lastAction} {lastEntity}";
                        Debug.WriteLine($"[Context] Resolved 'again' to '{result}'");
                    }
                }
            }
            
            // "same folder" / "that folder" / "there"
            if (System.Text.RegularExpressions.Regex.IsMatch(lower, @"\b(same folder|that folder|same directory|there|that location)\b"))
            {
                if (!string.IsNullOrEmpty(LastReferencedFolder))
                {
                    result = System.Text.RegularExpressions.Regex.Replace(result, 
                        @"\b(same folder|that folder|same directory|there|that location)\b", 
                        LastReferencedFolder, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    Debug.WriteLine($"[Context] Resolved folder reference to '{LastReferencedFolder}'");
                }
            }
            
            // "same file" / "that file"
            if (System.Text.RegularExpressions.Regex.IsMatch(lower, @"\b(same file|that file)\b"))
            {
                if (!string.IsNullOrEmpty(LastReferencedFile))
                {
                    result = System.Text.RegularExpressions.Regex.Replace(result, 
                        @"\b(same file|that file)\b", 
                        LastReferencedFile, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    Debug.WriteLine($"[Context] Resolved file reference to '{LastReferencedFile}'");
                }
            }
            
            // "that app" / "same app"
            if (System.Text.RegularExpressions.Regex.IsMatch(lower, @"\b(same app|that app|that program)\b"))
            {
                if (!string.IsNullOrEmpty(LastReferencedApp))
                {
                    result = System.Text.RegularExpressions.Regex.Replace(result, 
                        @"\b(same app|that app|that program)\b", 
                        LastReferencedApp, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    Debug.WriteLine($"[Context] Resolved app reference to '{LastReferencedApp}'");
                }
            }
            
            return result;
        }

        /// <summary>
        /// Get the most recently referenced entity (file, folder, app, or query)
        /// </summary>
        public string? GetLastReferencedEntity()
        {
            // Check in order of recency
            var lastEntry = _history.LastOrDefault();
            if (lastEntry?.Intent?.Entities != null)
            {
                if (lastEntry.Intent.Entities.TryGetValue("query", out var query) && !string.IsNullOrEmpty(query))
                    return query;
                if (lastEntry.Intent.Entities.TryGetValue("app", out var app) && !string.IsNullOrEmpty(app))
                    return app;
                if (lastEntry.Intent.Entities.TryGetValue("target", out var target) && !string.IsNullOrEmpty(target))
                    return target;
            }
            
            return LastReferencedFile ?? LastReferencedFolder ?? LastReferencedApp ?? LastMusicQuery ?? LastSearchQuery;
        }

        /// <summary>
        /// Get recent context summary for AI reasoning
        /// </summary>
        public string GetContextSummary()
        {
            var parts = new List<string>();
            
            if (!string.IsNullOrEmpty(LastActiveFeature))
                parts.Add($"Active feature: {LastActiveFeature}");
            
            if (!string.IsNullOrEmpty(LastReferencedFile))
                parts.Add($"Last file: {LastReferencedFile}");
            
            if (!string.IsNullOrEmpty(LastReferencedFolder))
                parts.Add($"Last folder: {LastReferencedFolder}");
            
            if (!string.IsNullOrEmpty(LastReferencedApp))
                parts.Add($"Last app: {LastReferencedApp}");
            
            if (!string.IsNullOrEmpty(LastMusicQuery))
                parts.Add($"Last music: {LastMusicQuery}");
            
            if (!string.IsNullOrEmpty(LastActionOutcome))
                parts.Add($"Last outcome: {LastActionOutcome}");
            
            // Add last few user requests
            var recentRequests = _history.TakeLast(3)
                .Select(e => e.UserInput)
                .Where(s => !string.IsNullOrEmpty(s));
            if (recentRequests.Any())
                parts.Add($"Recent requests: {string.Join("; ", recentRequests)}");
            
            return parts.Count > 0 ? string.Join("\n", parts) : "No recent context";
        }

        /// <summary>
        /// Get recent history entries
        /// </summary>
        public IReadOnlyList<ContextEntry> GetRecentHistory(int count = 5)
        {
            return _history.TakeLast(count).ToList();
        }

        /// <summary>
        /// Update the outcome of the last action
        /// </summary>
        public void UpdateLastOutcome(string outcome)
        {
            LastActionOutcome = outcome;
            var lastEntry = _history.LastOrDefault();
            if (lastEntry != null)
            {
                lastEntry.LastActionOutcome = outcome;
                Save();
            }
        }

        /// <summary>
        /// Clear all context
        /// </summary>
        public void Clear()
        {
            _history.Clear();
            LastActiveFeature = null;
            LastReferencedFile = null;
            LastReferencedFolder = null;
            LastReferencedApp = null;
            LastScanResult = null;
            LastSearchQuery = null;
            LastMusicQuery = null;
            LastActionOutcome = null;
            LastIntent = null;
            Save();
        }

        private void Save()
        {
            try
            {
                var dir = Path.GetDirectoryName(_persistPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                
                var data = new
                {
                    History = _history.TakeLast(10).ToList(),
                    LastActiveFeature,
                    LastReferencedFile,
                    LastReferencedFolder,
                    LastReferencedApp,
                    LastScanResult,
                    LastSearchQuery,
                    LastMusicQuery,
                    LastActionOutcome
                };
                
                File.WriteAllText(_persistPath, JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Context] Save error: {ex.Message}");
            }
        }

        private void Load()
        {
            try
            {
                if (!File.Exists(_persistPath)) return;
                
                var json = File.ReadAllText(_persistPath);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                
                if (root.TryGetProperty("LastActiveFeature", out var laf) && laf.ValueKind == JsonValueKind.String)
                    LastActiveFeature = laf.GetString();
                if (root.TryGetProperty("LastReferencedFile", out var lrf) && lrf.ValueKind == JsonValueKind.String)
                    LastReferencedFile = lrf.GetString();
                if (root.TryGetProperty("LastReferencedFolder", out var lrd) && lrd.ValueKind == JsonValueKind.String)
                    LastReferencedFolder = lrd.GetString();
                if (root.TryGetProperty("LastReferencedApp", out var lra) && lra.ValueKind == JsonValueKind.String)
                    LastReferencedApp = lra.GetString();
                if (root.TryGetProperty("LastSearchQuery", out var lsq) && lsq.ValueKind == JsonValueKind.String)
                    LastSearchQuery = lsq.GetString();
                if (root.TryGetProperty("LastMusicQuery", out var lmq) && lmq.ValueKind == JsonValueKind.String)
                    LastMusicQuery = lmq.GetString();
                
                Debug.WriteLine("[Context] Loaded previous context");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Context] Load error: {ex.Message}");
            }
        }
    }
}
