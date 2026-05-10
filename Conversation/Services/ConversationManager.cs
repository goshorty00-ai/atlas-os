using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using AtlasAI.Conversation.Models;

namespace AtlasAI.Conversation.Services
{
    /// <summary>
    /// Manages the current conversation session and coordinates with storage
    /// </summary>
    public class ConversationManager
    {
        private readonly SessionStore _store;
        private ChatSession? _currentSession;
        private UserProfile? _userProfile;
        private List<MemoryItem> _memories = new();

        public event EventHandler<ChatSession>? SessionChanged;
        public event EventHandler<SessionMessage>? MessageAdded;

        public ChatSession? CurrentSession => _currentSession;
        public UserProfile? UserProfile => _userProfile;
        public IReadOnlyList<MemoryItem> Memories => _memories.AsReadOnly();

        public ConversationManager()
        {
            _store = new SessionStore();
        }

        /// <summary>
        /// Initialize the manager - call on app startup
        /// </summary>
        public async Task InitializeAsync()
        {
            _userProfile = await _store.LoadProfileAsync();
            _memories = await _store.LoadMemoryAsync();
            
            // Always create a new session on app launch
            await StartNewSessionAsync();
            
            Debug.WriteLine($"[ConversationManager] Initialized. Profile: {_userProfile?.DisplayName ?? "Unknown"}, Memories: {_memories.Count}");
        }

        /// <summary>
        /// Check if this is the first run
        /// </summary>
        public async Task<bool> IsFirstRunAsync()
        {
            return await _store.IsFirstRunAsync();
        }

        /// <summary>
        /// Complete the first run onboarding
        /// </summary>
        public async Task CompleteOnboardingAsync(string? displayName, ConversationStyle style, string? voiceId)
        {
            if (_userProfile == null)
                _userProfile = new UserProfile();

            _userProfile.DisplayName = displayName;
            _userProfile.PreferredStyle = style;
            _userProfile.PreferredVoiceId = voiceId;
            _userProfile.IsFirstRunCompleted = true;

            await _store.SaveProfileAsync(_userProfile);
            Debug.WriteLine($"[ConversationManager] Onboarding completed for {displayName}");
        }

        /// <summary>
        /// Start a new chat session
        /// </summary>
        public async Task<ChatSession> StartNewSessionAsync()
        {
            // Save current session if exists
            if (_currentSession != null && _currentSession.Messages.Count > 0)
            {
                await _store.SaveSessionAsync(_currentSession);
            }

            _currentSession = await _store.CreateSessionAsync();
            SessionChanged?.Invoke(this, _currentSession);
            
            Debug.WriteLine($"[ConversationManager] New session started: {_currentSession.Id}");
            return _currentSession;
        }

        /// <summary>
        /// Add a message to the current session
        /// </summary>
        public async Task<SessionMessage> AddMessageAsync(MessageRole role, string content, bool isVoice = false)
        {
            Debug.WriteLine($"[ConversationManager] AddMessageAsync called - Role: {role}, Content length: {content?.Length ?? 0}");
            
            if (_currentSession == null)
            {
                Debug.WriteLine("[ConversationManager] No current session, creating new one...");
                await StartNewSessionAsync();
            }

            var message = new SessionMessage
            {
                Id = Guid.NewGuid().ToString(),
                SessionId = _currentSession!.Id,
                Role = role,
                Content = content,
                Timestamp = DateTime.Now,
                IsVoiceInput = isVoice
            };

            _currentSession.Messages.Add(message);
            _currentSession.LastMessageAt = DateTime.Now;
            _currentSession.Metadata.MessageCount++;
            
            Debug.WriteLine($"[ConversationManager] Added message to session {_currentSession.Id}, now has {_currentSession.Messages.Count} messages");

            // Auto-generate title from first user message
            if (_currentSession.Messages.Count == 1 && role == MessageRole.User)
            {
                _currentSession.Title = _store.GenerateTitle(content);
                Debug.WriteLine($"[ConversationManager] Generated title: {_currentSession.Title}");
            }

            // Save on EVERY message to ensure nothing is lost
            try
            {
                await _store.SaveSessionAsync(_currentSession);
                Debug.WriteLine($"[ConversationManager] Session saved successfully");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ConversationManager] ERROR saving session: {ex.Message}");
            }

            MessageAdded?.Invoke(this, message);
            return message;
        }

        /// <summary>
        /// Load a previous session (read-only or continue)
        /// </summary>
        public async Task<ChatSession?> LoadSessionAsync(string sessionId, bool continueChat = false)
        {
            var session = await _store.LoadSessionAsync(sessionId);
            if (session == null) return null;

            if (continueChat)
            {
                // Create new session with context from old one
                var newSession = await StartNewSessionAsync();
                
                // Add summary of previous conversation as context
                var summary = GenerateSessionSummary(session);
                newSession.Messages.Add(new SessionMessage
                {
                    Role = MessageRole.System,
                    Content = $"[Continuing from previous conversation: {session.Title}]\n{summary}"
                });
                
                return newSession;
            }

            return session;
        }

        /// <summary>
        /// Get sessions grouped by date for history panel
        /// </summary>
        public Dictionary<string, List<SessionIndexEntry>> GetSessionHistory()
        {
            return _store.GetSessionsByDate();
        }

        /// <summary>
        /// Search sessions
        /// </summary>
        public async Task<List<SessionSearchResult>> SearchSessionsAsync(string query)
        {
            return await _store.SearchSessionsAsync(query);
        }

        /// <summary>
        /// Delete a session
        /// </summary>
        public async Task DeleteSessionAsync(string sessionId)
        {
            await _store.DeleteSessionAsync(sessionId);
        }

        /// <summary>
        /// Set a loaded session as the current session (for continuing from history)
        /// </summary>
        public async Task SetCurrentSessionAsync(string sessionId)
        {
            var session = await _store.LoadSessionAsync(sessionId);
            if (session != null)
            {
                _currentSession = session;
                SessionChanged?.Invoke(this, _currentSession);
                Debug.WriteLine($"[ConversationManager] Switched to session: {sessionId} - {session.Title}");
            }
        }

        /// <summary>
        /// Save current session (call on app close)
        /// </summary>
        public async Task SaveCurrentSessionAsync()
        {
            if (_currentSession != null && _currentSession.Messages.Count > 0)
            {
                await _store.SaveSessionAsync(_currentSession);
                Debug.WriteLine($"[ConversationManager] Session saved: {_currentSession.Id}");
            }
        }

        #region Memory Management

        /// <summary>
        /// Add a memory item
        /// </summary>
        public async Task RememberAsync(string content, MemoryCategory category = MemoryCategory.General)
        {
            var item = new MemoryItem
            {
                Content = content,
                Type = MemoryType.Explicit,
                Category = category,
                SourceSessionId = _currentSession?.Id
            };

            await _store.AddMemoryAsync(item);
            _memories = await _store.LoadMemoryAsync();
            
            Debug.WriteLine($"[ConversationManager] Memory added: {content}");
        }

        /// <summary>
        /// Remove a memory item
        /// </summary>
        public async Task ForgetAsync(string memoryId)
        {
            await _store.RemoveMemoryAsync(memoryId);
            _memories = await _store.LoadMemoryAsync();
        }

        /// <summary>
        /// Clear all memories
        /// </summary>
        public async Task ClearAllMemoriesAsync()
        {
            await _store.ClearMemoryAsync();
            _memories.Clear();
        }

        /// <summary>
        /// Get memories relevant to current context
        /// </summary>
        public List<MemoryItem> GetRelevantMemories(string query)
        {
            return _store.GetRelevantMemories(query);
        }

        #endregion

        #region Profile Management

        /// <summary>
        /// Update user profile
        /// </summary>
        public async Task UpdateProfileAsync(Action<UserProfile> updateAction)
        {
            if (_userProfile == null)
                _userProfile = await _store.LoadProfileAsync();

            updateAction(_userProfile);
            await _store.SaveProfileAsync(_userProfile);
        }

        /// <summary>
        /// Get the user's preferred name
        /// </summary>
        public string GetUserName()
        {
            // Return DisplayName if set, otherwise just use "sir" - never use Windows username
            if (!string.IsNullOrWhiteSpace(_userProfile?.DisplayName))
                return _userProfile.DisplayName;
            return "sir";
        }

        /// <summary>
        /// Get the user's preferred conversation style
        /// </summary>
        public ConversationStyle GetConversationStyle()
        {
            return ConversationStyle.Butler; // Always JARVIS style
        }

        #endregion

        #region Context Building

        /// <summary>
        /// Build context for LLM including profile, memory, and recent messages
        /// </summary>
        public string BuildContextForLLM(string currentQuery)
        {
            var context = new System.Text.StringBuilder();

            // User profile context
            if (_userProfile != null)
            {
                context.AppendLine("=== USER PROFILE ===");
                if (!string.IsNullOrEmpty(_userProfile.DisplayName))
                    context.AppendLine($"Name: {_userProfile.DisplayName}");
                if (!string.IsNullOrEmpty(_userProfile.Location))
                    context.AppendLine($"Location: {_userProfile.Location}");
                context.AppendLine($"Preferred Style: {_userProfile.PreferredStyle}");
                context.AppendLine();
            }

            // Relevant memories
            var relevantMemories = GetRelevantMemories(currentQuery);
            if (relevantMemories.Count > 0)
            {
                context.AppendLine("=== REMEMBERED CONTEXT ===");
                foreach (var memory in relevantMemories)
                {
                    context.AppendLine($"- {memory.Content}");
                }
                context.AppendLine();
            }

            // Recent conversation summary if session is long
            if (_currentSession != null && _currentSession.Messages.Count > 20)
            {
                context.AppendLine("=== CONVERSATION SUMMARY ===");
                context.AppendLine(GenerateSessionSummary(_currentSession));
                context.AppendLine();
            }

            return context.ToString();
        }

        private string GenerateSessionSummary(ChatSession session)
        {
            // Simple summary - take key points from conversation
            var userMessages = session.Messages
                .Where(m => m.Role == MessageRole.User)
                .Take(5)
                .Select(m => m.Content.Length > 100 ? m.Content.Substring(0, 100) + "..." : m.Content);

            return $"Topics discussed: {string.Join("; ", userMessages)}";
        }

        #endregion
    }
}
