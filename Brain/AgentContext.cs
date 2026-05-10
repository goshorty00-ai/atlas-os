using System.Collections.Generic;
using AtlasAI.Personality;

namespace AtlasAI.Brain
{
    public enum PresenceState
    {
        Unknown,
        Idle,
        Listening,
        Working,
        Busy,
        Alert
    }

    public enum ResponseIntent
    {
        NormalChat,
        TaskExecution,
        MetaCapabilities,
        Identity
    }

    public sealed class AgentContext
    {
        public string ActiveSection { get; set; } = "Chat";
        public PresenceState? CurrentPresence { get; set; }
        public PersonalityType CurrentPersonality { get; set; } = PersonalityType.Butler;
        public WorkspaceMode ActiveWorkspace { get; set; } = WorkspaceMode.Unknown;
        public Dictionary<string, object?> Data { get; } = new();
        public ResponseIntent Intent { get; set; } = ResponseIntent.NormalChat;
    }
}
