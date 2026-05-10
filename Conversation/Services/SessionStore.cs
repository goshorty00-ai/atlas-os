using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using AtlasAI.Conversation.Models;
using AtlasAI.Settings;

namespace AtlasAI.Conversation.Services
{
    /// <summary>
    /// Persistent storage for chat sessions, user profile, and memory
    /// Uses JSON files in %AppData%\AtlasAI\
    /// </summary>
    public class SessionStore
    {
        private static readonly string AppDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AtlasAI");
        
        private static readonly string SessionsPath = Path.Combine(AppDataPath, "Sessions");
        private static readonly string MemoryPath = Path.Combine(AppDataPath, "memory.json");
        private static readonly string IndexPath = Path.Combine(AppDataPath, "session_index.json");

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true  // Fix: Allow reading both camelCase and PascalCase properties
        };

        private List<SessionIndexEntry> _sessionIndex = new();
        private UserProfile? _cachedProfile;
        private List<MemoryItem> _cachedMemory = new();

        public SessionStore()
        {
            EnsureDirectoriesExist();
            LoadIndex();
            
            // Validate and rebuild index if needed
            ValidateAndRebuildIndexIfNeeded();
        }

        private void EnsureDirectoriesExist()
        {
            Directory.CreateDirectory(AppDataPath);
            Directory.CreateDirectory(SessionsPath);
        }
        
        /// <summary>
        /// Validates the index against actual session files and rebuilds if out of sync
        /// </summary>
        private void ValidateAndRebuildIndexIfNeeded()
        {
            try
            {
                // Get all session files
                var sessionFiles = Directory.GetFiles(SessionsPath, "*.json");
                
                // Check if index is significantly out of sync
                bool needsRebuild = false;
                
                // If we have session files but index is empty or much smaller
                if (sessionFiles.Length > 0 && _sessionIndex.Count < sessionFiles.Length / 2)
                {
                    needsRebuild = true;
                    System.Diagnostics.Debug.WriteLine($"[SessionStore] Index has {_sessionIndex.Count} entries but found {sessionFiles.Length} session files - rebuilding");
                }
                
                // Check if index entries have wrong message counts (sample check)
                if (!needsRebuild && _sessionIndex.Count > 0)
                {
                    var samplesToCheck = _sessionIndex.Take(10).ToList();
                    int mismatchCount = 0;
                    
                    foreach (var entry in samplesToCheck)
                    {
                        var filePath = GetSessionFilePath(entry.Id);
                        if (File.Exists(filePath))
                        {
                            try
                            {
                                var json = File.ReadAllText(filePath);
                                var session = JsonSerializer.Deserialize<ChatSession>(json, JsonOptions);
                                if (session != null && session.Messages.Count != entry.MessageCount)
                                {
                                    mismatchCount++;
                                }
                            }
                            catch (Exception ex)
                            {
                                System.Diagnostics.Debug.WriteLine($"[SessionStore] Error validating session {entry.Id}: {ex.Message}");
                            }
                        }
                    }
                    
                    // If more than half of samples are mismatched, rebuild
                    if (mismatchCount > samplesToCheck.Count / 2)
                    {
                        needsRebuild = true;
                        System.Diagnostics.Debug.WriteLine($"[SessionStore] Index message counts are out of sync - rebuilding");
                    }
                }
                
                if (needsRebuild)
                {
                    RebuildIndexFromFiles();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SessionStore] Error validating index: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Rebuilds the session index by scanning all session files
        /// </summary>
        private void RebuildIndexFromFiles()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("[SessionStore] Rebuilding session index from files...");
                
                var newIndex = new List<SessionIndexEntry>();
                var sessionFiles = Directory.GetFiles(SessionsPath, "*.json");
                
                foreach (var filePath in sessionFiles)
                {
                    try
                    {
                        var json = File.ReadAllText(filePath);
                        var session = JsonSerializer.Deserialize<ChatSession>(json, JsonOptions);
                        
                        if (session != null && !string.IsNullOrEmpty(session.Id))
                        {
                            newIndex.Add(new SessionIndexEntry
                            {
                                Id = session.Id,
                                Title = session.Title ?? "Untitled",
                                CreatedAt = session.CreatedAt,
                                LastMessageAt = session.LastMessageAt,
                                MessageCount = session.Messages?.Count ?? 0,
                                Provider = session.Metadata?.Provider ?? ""
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[SessionStore] Error reading session file {filePath}: {ex.Message}");
                    }
                }
                
                // Sort by last message date descending
                _sessionIndex = newIndex.OrderByDescending(e => e.LastMessageAt).ToList();
                
                // Save the rebuilt index
                var indexJson = JsonSerializer.Serialize(_sessionIndex, JsonOptions);
                File.WriteAllText(IndexPath, indexJson);
                
                System.Diagnostics.Debug.WriteLine($"[SessionStore] Index rebuilt with {_sessionIndex.Count} sessions");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SessionStore] Error rebuilding index: {ex.Message}");
            }
        }

        #region Session Management

        /// <summary>
        /// Create a new chat session
        /// </summary>
        public async Task<ChatSession> CreateSessionAsync()
        {
            var session = new ChatSession
            {
                Id = Guid.NewGuid().ToString(),
                Title = "New Chat",
                CreatedAt = DateTime.Now,
                LastMessageAt = DateTime.Now
            };

            await SaveSessionAsync(session);
            
            // Add to index
            _sessionIndex.Insert(0, new SessionIndexEntry
            {
                Id = session.Id,
                Title = session.Title,
                CreatedAt = session.CreatedAt,
                LastMessageAt = session.LastMessageAt,
                MessageCount = 0,
                Provider = session.Metadata.Provider
            });
            await SaveIndexAsync();

            return session;
        }

        /// <summary>
        /// Save a session to disk
        /// </summary>
        public async Task SaveSessionAsync(ChatSession session)
        {
            var filePath = GetSessionFilePath(session.Id);
            var json = JsonSerializer.Serialize(session, JsonOptions);
            await File.WriteAllTextAsync(filePath, json);

            // Update index
            var indexEntry = _sessionIndex.FirstOrDefault(e => e.Id == session.Id);
            if (indexEntry != null)
            {
                indexEntry.Title = session.Title;
                indexEntry.LastMessageAt = session.LastMessageAt;
                indexEntry.MessageCount = session.Messages.Count;
                await SaveIndexAsync();
            }
        }

        /// <summary>
        /// Load a session from disk
        /// </summary>
        public async Task<ChatSession?> LoadSessionAsync(string sessionId)
        {
            var filePath = GetSessionFilePath(sessionId);
            if (!File.Exists(filePath)) return null;

            var json = await File.ReadAllTextAsync(filePath);
            return JsonSerializer.Deserialize<ChatSession>(json, JsonOptions);
        }

        /// <summary>
        /// Get all sessions grouped by date
        /// </summary>
        public Dictionary<string, List<SessionIndexEntry>> GetSessionsByDate()
        {
            var today = DateTime.Today;
            var yesterday = today.AddDays(-1);
            var lastWeek = today.AddDays(-7);

            var grouped = new Dictionary<string, List<SessionIndexEntry>>
            {
                ["Today"] = new(),
                ["Yesterday"] = new(),
                ["Last 7 Days"] = new(),
                ["Older"] = new()
            };

            foreach (var entry in _sessionIndex.OrderByDescending(e => e.LastMessageAt))
            {
                var date = entry.LastMessageAt.Date;
                if (date == today)
                    grouped["Today"].Add(entry);
                else if (date == yesterday)
                    grouped["Yesterday"].Add(entry);
                else if (date > lastWeek)
                    grouped["Last 7 Days"].Add(entry);
                else
                    grouped["Older"].Add(entry);
            }

            return grouped;
        }

        /// <summary>
        /// Search sessions by title or content
        /// </summary>
        public async Task<List<SessionSearchResult>> SearchSessionsAsync(string query)
        {
            var results = new List<SessionSearchResult>();
            var lowerQuery = query.ToLowerInvariant();

            foreach (var entry in _sessionIndex)
            {
                // Check title first
                if (entry.Title.ToLowerInvariant().Contains(lowerQuery))
                {
                    results.Add(new SessionSearchResult
                    {
                        SessionId = entry.Id,
                        Title = entry.Title,
                        MatchType = "Title",
                        CreatedAt = entry.CreatedAt
                    });
                    continue;
                }

                // Search message content
                var session = await LoadSessionAsync(entry.Id);
                if (session != null)
                {
                    var matchingMessage = session.Messages
                        .FirstOrDefault(m => m.Content.ToLowerInvariant().Contains(lowerQuery));
                    
                    if (matchingMessage != null)
                    {
                        results.Add(new SessionSearchResult
                        {
                            SessionId = entry.Id,
                            Title = entry.Title,
                            MatchType = "Message",
                            MatchPreview = GetMatchPreview(matchingMessage.Content, lowerQuery),
                            CreatedAt = entry.CreatedAt
                        });
                    }
                }
            }

            return results;
        }

        /// <summary>
        /// Delete a session
        /// </summary>
        public async Task DeleteSessionAsync(string sessionId)
        {
            var filePath = GetSessionFilePath(sessionId);
            if (File.Exists(filePath))
                File.Delete(filePath);

            _sessionIndex.RemoveAll(e => e.Id == sessionId);
            await SaveIndexAsync();
        }

        /// <summary>
        /// Generate title from first user message
        /// </summary>
        public string GenerateTitle(string firstMessage)
        {
            if (string.IsNullOrWhiteSpace(firstMessage))
                return "New Chat";

            // Take first 50 chars, trim to last word
            var title = firstMessage.Length > 50 
                ? firstMessage.Substring(0, 50).TrimEnd() + "..."
                : firstMessage;

            // Remove newlines
            title = title.Replace("\n", " ").Replace("\r", "");
            
            return title;
        }

        private string GetSessionFilePath(string sessionId) =>
            Path.Combine(SessionsPath, $"{sessionId}.json");

        private string GetMatchPreview(string content, string query)
        {
            var index = content.ToLowerInvariant().IndexOf(query);
            if (index < 0) return "";

            var start = Math.Max(0, index - 20);
            var end = Math.Min(content.Length, index + query.Length + 20);
            var preview = content.Substring(start, end - start);
            
            if (start > 0) preview = "..." + preview;
            if (end < content.Length) preview += "...";
            
            return preview;
        }

        #endregion

        #region Index Management

        private void LoadIndex()
        {
            if (File.Exists(IndexPath))
            {
                var json = File.ReadAllText(IndexPath);
                _sessionIndex = JsonSerializer.Deserialize<List<SessionIndexEntry>>(json, JsonOptions) ?? new();
            }
        }

        private async Task SaveIndexAsync()
        {
            var json = JsonSerializer.Serialize(_sessionIndex, JsonOptions);
            await File.WriteAllTextAsync(IndexPath, json);
        }

        #endregion

        #region User Profile

        /// <summary>
        /// Load user profile (creates default if not exists)
        /// </summary>
        public async Task<UserProfile> LoadProfileAsync()
        {
            if (_cachedProfile != null)
            {
                System.Diagnostics.Debug.WriteLine($"[SessionStore] Returning cached profile. IsFirstRunCompleted={_cachedProfile.IsFirstRunCompleted}");
                return _cachedProfile;
            }

            await Task.CompletedTask;
            _cachedProfile = SettingsStore.GetConversationUserProfile();
            System.Diagnostics.Debug.WriteLine($"[SessionStore] Loaded profile from SettingsStore. IsFirstRunCompleted={_cachedProfile.IsFirstRunCompleted}, DisplayName={_cachedProfile.DisplayName}");

            return _cachedProfile;
        }

        /// <summary>
        /// Save user profile
        /// </summary>
        public async Task SaveProfileAsync(UserProfile profile)
        {
            profile.LastUpdated = DateTime.Now;
            _cachedProfile = profile;
            SettingsStore.SaveConversationUserProfile(profile);
            await Task.CompletedTask;
        }

        /// <summary>
        /// Check if this is first run
        /// </summary>
        public async Task<bool> IsFirstRunAsync()
        {
            var profile = await LoadProfileAsync();
            return !profile.IsFirstRunCompleted;
        }

        /// <summary>
        /// Mark first run as completed
        /// </summary>
        public async Task CompleteFirstRunAsync()
        {
            var profile = await LoadProfileAsync();
            profile.IsFirstRunCompleted = true;
            await SaveProfileAsync(profile);
        }

        #endregion

        #region Memory

        /// <summary>
        /// Load all memory items
        /// </summary>
        public async Task<List<MemoryItem>> LoadMemoryAsync()
        {
            if (_cachedMemory.Count > 0)
                return _cachedMemory;

            if (File.Exists(MemoryPath))
            {
                var json = await File.ReadAllTextAsync(MemoryPath);
                _cachedMemory = JsonSerializer.Deserialize<List<MemoryItem>>(json, JsonOptions) ?? new();
            }

            return _cachedMemory;
        }

        /// <summary>
        /// Add a memory item
        /// </summary>
        public async Task AddMemoryAsync(MemoryItem item)
        {
            // Check for duplicates
            if (_cachedMemory.Any(m => m.Content.Equals(item.Content, StringComparison.OrdinalIgnoreCase)))
                return;

            // Security check - don't store sensitive data
            if (IsSensitiveContent(item.Content))
                return;

            _cachedMemory.Add(item);
            await SaveMemoryAsync();
        }

        /// <summary>
        /// Remove a memory item
        /// </summary>
        public async Task RemoveMemoryAsync(string memoryId)
        {
            _cachedMemory.RemoveAll(m => m.Id == memoryId);
            await SaveMemoryAsync();
        }

        /// <summary>
        /// Clear all memory
        /// </summary>
        public async Task ClearMemoryAsync()
        {
            _cachedMemory.Clear();
            await SaveMemoryAsync();
        }

        /// <summary>
        /// Get relevant memories for context
        /// </summary>
        public List<MemoryItem> GetRelevantMemories(string query, int maxItems = 5)
        {
            var lowerQuery = query.ToLowerInvariant();
            
            return _cachedMemory
                .Where(m => m.IsActive)
                .OrderByDescending(m => 
                    m.Content.ToLowerInvariant().Contains(lowerQuery) ? 1 : 0)
                .ThenByDescending(m => m.UseCount)
                .ThenByDescending(m => m.LastUsedAt ?? m.CreatedAt)
                .Take(maxItems)
                .ToList();
        }

        /// <summary>
        /// Mark memory as used (for relevance tracking)
        /// </summary>
        public async Task MarkMemoryUsedAsync(string memoryId)
        {
            var memory = _cachedMemory.FirstOrDefault(m => m.Id == memoryId);
            if (memory != null)
            {
                memory.UseCount++;
                memory.LastUsedAt = DateTime.Now;
                await SaveMemoryAsync();
            }
        }

        private async Task SaveMemoryAsync()
        {
            var json = JsonSerializer.Serialize(_cachedMemory, JsonOptions);
            await File.WriteAllTextAsync(MemoryPath, json);
        }

        private bool IsSensitiveContent(string content)
        {
            var lowerContent = content.ToLowerInvariant();
            var sensitivePatterns = new[]
            {
                "api key", "apikey", "api_key",
                "password", "passwd", "pwd",
                "secret", "token",
                "credit card", "creditcard",
                "ssn", "social security",
                "bank account"
            };

            return sensitivePatterns.Any(p => lowerContent.Contains(p));
        }

        #endregion
    }

    /// <summary>
    /// Lightweight index entry for session list
    /// </summary>
    public class SessionIndexEntry
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public DateTime CreatedAt { get; set; }
        public DateTime LastMessageAt { get; set; }
        public int MessageCount { get; set; }
        public string Provider { get; set; } = "";
    }

    /// <summary>
    /// Search result for session search
    /// </summary>
    public class SessionSearchResult
    {
        public string SessionId { get; set; } = "";
        public string Title { get; set; } = "";
        public string MatchType { get; set; } = "";
        public string? MatchPreview { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
