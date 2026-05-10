using System.Text.Json.Serialization;

namespace AtlasAI.Voice
{
    /// <summary>
    /// Defines a voice profile for TTS synthesis.
    /// Bound to personalities for deterministic voice selection.
    /// </summary>
    public class VoiceProfile
    {
        /// <summary>
        /// ElevenLabs voice ID (e.g., "rz1Dju9fYa3YHIYzRCM4").
        /// </summary>
        [JsonPropertyName("voiceId")]
        public string VoiceId { get; set; } = string.Empty;

        /// <summary>
        /// TTS provider name (currently only "ElevenLabs" supported).
        /// </summary>
        [JsonPropertyName("provider")]
        public string Provider { get; set; } = "ElevenLabs";

        /// <summary>
        /// Advisory cadence multiplier (0.9 = slower, 1.0 = normal, 1.1 = faster).
        /// Used via prompt phrasing hints, not direct engine control.
        /// </summary>
        [JsonPropertyName("cadenceMultiplier")]
        public double CadenceMultiplier { get; set; } = 1.0;

        /// <summary>
        /// Human-readable description of the voice character.
        /// </summary>
        [JsonPropertyName("description")]
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// Display name for UI purposes.
        /// </summary>
        [JsonPropertyName("displayName")]
        public string DisplayName { get; set; } = string.Empty;

        #region Predefined Voice Profiles

        public const string DefaultAtlasVoiceId = "onwK4e9ZLuTAKqWW03F9";

        /// <summary>Atlas AI - calm, confident, British cadence. Default for Atlas personality.</summary>
        public static VoiceProfile AtlasDefault => new()
        {
            VoiceId = DefaultAtlasVoiceId,
            Provider = "ElevenLabs",
            CadenceMultiplier = 0.95,
            Description = "Calm, confident British delivery with measured cadence",
            DisplayName = "Daniel (Atlas Default)"
        };

        /// <summary>Adam - deep narrator voice. Good for Serious personality.</summary>
        public static VoiceProfile AdamNarrator => new()
        {
            VoiceId = "pNInz6obpgDQGcFmaJgB",
            Provider = "ElevenLabs",
            CadenceMultiplier = 0.9,
            Description = "Deep, authoritative narrator voice",
            DisplayName = "Adam (Narrator)"
        };

        /// <summary>Arnold - deep male voice. Good for Cold personality.</summary>
        public static VoiceProfile ArnoldDeep => new()
        {
            VoiceId = "VR6AewLTigWG4xSOukaG",
            Provider = "ElevenLabs",
            CadenceMultiplier = 0.85,
            Description = "Deep, terse male voice",
            DisplayName = "Arnold (Deep)"
        };

        /// <summary>Antoni - warm male voice. Good for Friendly personality.</summary>
        public static VoiceProfile AntoniWarm => new()
        {
            VoiceId = "ErXwobaYiN019PkySvjV",
            Provider = "ElevenLabs",
            CadenceMultiplier = 1.0,
            Description = "Warm, approachable male voice",
            DisplayName = "Antoni (Warm)"
        };

        /// <summary>Rachel - calm female voice. Alternative option.</summary>
        public static VoiceProfile RachelCalm => new()
        {
            VoiceId = "21m00Tcm4TlvDq8ikWAM",
            Provider = "ElevenLabs",
            CadenceMultiplier = 1.0,
            Description = "Calm, professional female voice",
            DisplayName = "Rachel (Calm)"
        };

        /// <summary>System voice - used for boot/alert messages. Never changes.</summary>
        public static VoiceProfile SystemVoice => new()
        {
            VoiceId = DefaultAtlasVoiceId,
            Provider = "ElevenLabs",
            CadenceMultiplier = 0.9,
            Description = "System announcements and alerts",
            DisplayName = "System"
        };

        #endregion
    }
}
