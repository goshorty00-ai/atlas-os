using System;
using System.Collections.Generic;

namespace AtlasAI.Conversation.Models
{
    /// <summary>
    /// Represents a chat session (like ChatGPT conversations)
    /// </summary>
    public class ChatSession
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Title { get; set; } = "New Chat";
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime LastMessageAt { get; set; } = DateTime.Now;
        public List<SessionMessage> Messages { get; set; } = new();
        public SessionMetadata Metadata { get; set; } = new();
        public bool IsArchived { get; set; } = false;
        public string? Summary { get; set; } // Auto-generated summary for long sessions
    }

    /// <summary>
    /// Individual chat message within a session
    /// </summary>
    public class SessionMessage
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string SessionId { get; set; } = "";
        public MessageRole Role { get; set; }
        public string Content { get; set; } = "";
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public string? AttachmentPath { get; set; }
        public bool IsVoiceInput { get; set; } = false;
        public bool WasSpoken { get; set; } = false;
        public MessageMetadata? Metadata { get; set; }
    }

    public enum MessageRole
    {
        User,
        Assistant,
        System
    }

    /// <summary>
    /// Metadata about the session (provider, model, etc.)
    /// </summary>
    public class SessionMetadata
    {
        public string Provider { get; set; } = "OpenAI"; // OpenAI, Claude, etc.
        public string Model { get; set; } = "gpt-4o-mini";
        public string? VoiceProvider { get; set; } = "ElevenLabs";
        public string? VoiceId { get; set; }
        public int MessageCount { get; set; } = 0;
        public int TokensUsed { get; set; } = 0;
    }

    /// <summary>
    /// Metadata for individual messages
    /// </summary>
    public class MessageMetadata
    {
        public int? TokenCount { get; set; }
        public double? ResponseTimeMs { get; set; }
        public string? ToolsUsed { get; set; }
        public bool HasMemoryReference { get; set; } = false;
    }

    /// <summary>
    /// User profile with explicit preferences
    /// </summary>
    public class UserProfile
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string? DisplayName { get; set; }
        public string? Pronouns { get; set; }
        public UserHonorific Honorific { get; set; } = UserHonorific.Sir; // sir, ma'am, or name
        public ConversationStyle PreferredStyle { get; set; } = ConversationStyle.Butler; // Always JARVIS style
        public string? PreferredVoiceProvider { get; set; } = "ElevenLabs";
        public string? PreferredVoiceId { get; set; } = Voice.VoiceProfile.DefaultAtlasVoiceId;
        public string? Timezone { get; set; } = "GMT";
        public string? Location { get; set; } = "Middlesbrough, United Kingdom";
        public bool IsFirstRunCompleted { get; set; } = false;
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime LastUpdated { get; set; } = DateTime.Now;
        
        // Permission toggles
        public bool AllowSystemControl { get; set; } = true;
        public bool AllowAppContextReading { get; set; } = true;
        public bool AllowMemory { get; set; } = true;
        
        /// <summary>
        /// Gets the appropriate honorific to use when addressing the user
        /// </summary>
        public string GetHonorific()
        {
            return Honorific switch
            {
                UserHonorific.Sir => "sir",
                UserHonorific.Maam => "ma'am",
                UserHonorific.Miss => "miss",
                UserHonorific.Name => DisplayName ?? "there",
                UserHonorific.None => "",
                _ => "sir"
            };
        }
    }

    /// <summary>
    /// How Atlas should address the user
    /// </summary>
    public enum UserHonorific
    {
        Sir,    // "Yes, sir" - default JARVIS style
        Maam,   // "Yes, ma'am"
        Miss,   // "Yes, miss"
        Name,   // Use their display name
        None    // No honorific
    }

    /// <summary>
    /// Conversation style - JARVIS (Butler) is the only style
    /// </summary>
    public enum ConversationStyle
    {
        Butler         // Polite, concise, JARVIS-like (default and only option)
    }

    /// <summary>
    /// Memory item - things Atlas remembers about the user
    /// </summary>
    public class MemoryItem
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Content { get; set; } = "";
        public MemoryType Type { get; set; } = MemoryType.Explicit;
        public MemoryCategory Category { get; set; } = MemoryCategory.General;
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime? LastUsedAt { get; set; }
        public int UseCount { get; set; } = 0;
        public bool IsActive { get; set; } = true;
        public string? SourceSessionId { get; set; }
    }

    public enum MemoryType
    {
        Explicit,  // User said "remember this"
        Implicit   // Atlas detected a pattern
    }

    public enum MemoryCategory
    {
        General,
        Preference,
        Project,
        PersonalInfo,
        Workflow,
        Technical
    }
}
