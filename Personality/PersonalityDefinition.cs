using System;
using System.Collections.Generic;
using System.Linq;
using AtlasAI.Voice;

namespace AtlasAI.Personality
{
    /// <summary>
    /// Data-driven personality definition with domain-specific capabilities.
    /// Replaces the old enum-driven system to allow easy extensibility.
    /// </summary>
    public sealed class PersonalityDefinition
    {
        /// <summary>
        /// Unique identifier for this personality (e.g., "Atlas", "MediaWizard")
        /// </summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// Display name shown in UI (e.g., "Atlas (Butler)", "Media Wizard")
        /// </summary>
        public string DisplayName { get; set; } = string.Empty;

        /// <summary>
        /// Short description of personality's purpose and domain
        /// </summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// Domain scope: "All", "MediaCentre", "DJ", "Downloader", "Creative", "IDE", or "Debug"
        /// </summary>
        public string Domain { get; set; } = "All";

        /// <summary>
        /// Tone style for this personality
        /// </summary>
        public ToneStyle ToneStyle { get; set; } = ToneStyle.ElegantCalm;

        /// <summary>
        /// Response structure pattern
        /// </summary>
        public ResponseStructurePattern StructurePattern { get; set; } = ResponseStructurePattern.StatusThenSuggestion;

        /// <summary>
        /// Risk tolerance level
        /// </summary>
        public RiskToleranceLevel RiskTolerance { get; set; } = RiskToleranceLevel.Low;

        /// <summary>
        /// Proactivity level
        /// </summary>
        public ProactivityLevel Proactivity { get; set; } = ProactivityLevel.Medium;

        /// <summary>
        /// Humor level
        /// </summary>
        public HumorLevel HumorLevel { get; set; } = HumorLevel.None;

        /// <summary>
        /// Verbosity level
        /// </summary>
        public VerbosityLevel VerbosityLevel { get; set; } = VerbosityLevel.Medium;

        /// <summary>
        /// Decision bias
        /// </summary>
        public DecisionBias DecisionBias { get; set; } = DecisionBias.SafetyFirst;

        /// <summary>
        /// Style guide for LLM prompt
        /// </summary>
        public string StyleGuide { get; set; } = string.Empty;

        /// <summary>
        /// Domain-specific prompt additions (e.g., "Focus on media, playback, and entertainment")
        /// </summary>
        public string DomainPrompt { get; set; } = string.Empty;

        /// <summary>
        /// Skills/tools this personality should prioritize (e.g., ["MediaControl", "SpotifyIntegration"])
        /// </summary>
        public string[] PreferredSkills { get; set; } = Array.Empty<string>();

        /// <summary>
        /// Whether this personality is hidden behind a dev toggle (e.g. Chaos Testing)
        /// </summary>
        public bool IsHidden { get; set; } = false;

        /// <summary>
        /// Icon/emoji for UI representation (optional)
        /// </summary>
        public string Icon { get; set; } = "🤖";
        
        /// <summary>
        /// Preferred Voice ID for this personality (can be overridden by user settings)
        /// </summary>
        public string DefaultVoiceId { get; set; } = "en-US-GuyNeural";

        /// <summary>
        /// Converts this definition to a PersonalityProfile for backward compatibility
        /// </summary>
        public PersonalityProfile ToProfile()
        {
            return new PersonalityProfile
            {
                Type = ConvertToLegacyType(),
                // Map other properties if needed
            };
        }

        private PersonalityType ConvertToLegacyType()
        {
            if (Id.Equals("Professional", StringComparison.OrdinalIgnoreCase)) return PersonalityType.Butler;
            if (Id.Equals("Buddy", StringComparison.OrdinalIgnoreCase)) return PersonalityType.Friendly;
            if (Id.Equals("Funny", StringComparison.OrdinalIgnoreCase)) return PersonalityType.Friendly;
            if (Id.Equals("Sarcasm", StringComparison.OrdinalIgnoreCase)) return PersonalityType.Friendly;
            if (Id.Equals("Romantic", StringComparison.OrdinalIgnoreCase)) return PersonalityType.Friendly;
            if (Id.Equals("Atlas", StringComparison.OrdinalIgnoreCase)) return PersonalityType.Butler;
            return PersonalityType.Butler;
        }
    }
}
