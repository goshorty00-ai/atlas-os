using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AtlasAI.Voice
{
    /// <summary>
    /// Available personality identifiers.
    /// </summary>
    public enum PersonalityId
    {
        /// <summary>Premium assistant voice; calm, confident, slightly robotic, British cadence.</summary>
        Atlas,
        /// <summary>Calm, precise, and task-first. Short answers. Clear steps. Minimal chatter.</summary>
        Professional,
        /// <summary>Direct, disciplined, and security-aware. Fastest route to correct result.</summary>
        Serious,
        /// <summary>Terse, direct, minimal warmth, no closers.</summary>
        Cold,
        /// <summary>Light humour, still competent. Keeps it moving. Doesn't derail tasks with jokes.</summary>
        Funny,
        /// <summary>Warm, helpful, and easy to talk to. Explains without talking down.</summary>
        Friendly,
        /// <summary>Unfiltered pub-mate mode. Sweary, blunt, no corporate filter.</summary>
        Unfiltered
    }

    /// <summary>
    /// Humor style flags for Funny personality.
    /// </summary>
    [Flags]
    public enum HumorStyle
    {
        None = 0,
        Dry = 1,
        Subtle = 2,
        Observational = 4
    }

    /// <summary>
    /// Personality profile controlling tone, verbosity, phrasing, and voice.
    /// Does not affect capabilities or system authority.
    /// </summary>
    public class PersonalityProfile
    {
        private static PersonalityProfile? _current;
        private static readonly object _lock = new();
        
        /// <summary>
        /// Current active personality profile.
        /// </summary>
        public static PersonalityProfile Current
        {
            get
            {
                if (_current == null)
                {
                    lock (_lock)
                    {
                        _current ??= LoadOrDefault();
                    }
                }
                // Always sync with settings on access to ensure responsiveness
                _current.SyncWithSettings();
                return _current;
            }
        }

        [JsonPropertyName("id")]
        public PersonalityId Id { get; set; } = PersonalityId.Atlas;

        [JsonPropertyName("name")]
        public string Name { get; set; } = "Atlas";

        /// <summary>
        /// Default TTS voice for this personality. Required.
        /// </summary>
        [JsonIgnore]
        public VoiceProfile DefaultVoice { get; set; } = VoiceProfile.AtlasDefault;

        /// <summary>
        /// Optional override voice for system/boot/alert messages.
        /// If null, uses global system voice.
        /// </summary>
        [JsonIgnore]
        public VoiceProfile? SystemOverrideVoice { get; set; }

        [JsonPropertyName("roboticLevel")]
        public double RoboticLevel { get; set; } = 0.6;

        [JsonPropertyName("formalityLevel")]
        public double FormalityLevel { get; set; } = 0.7;

        [JsonPropertyName("warmthLevel")]
        public double WarmthLevel { get; set; } = 0.4;

        [JsonPropertyName("humorLevel")]
        public double HumorLevel { get; set; } = 0.0;

        // Missing properties for compatibility
        public List<string> BannedPhrases { get; set; } = new();
        public int MaxBullets { get; set; } = 3;
        public int MaxWordCount { get; set; } = 50;

        public string GetSystemPromptGuidance() 
        {
             switch (Id)
             {
                 case PersonalityId.Serious: return "Be direct and professional. No small talk.";
                 case PersonalityId.Funny: return "You can be humorous and witty.";
                 case PersonalityId.Friendly: return "Be warm and helpful.";
                 case PersonalityId.Cold: return "Be terse and efficient.";
                 default: return "Be polite and helpful.";
             }
        }

        public static PersonalityProfile GetDefault(PersonalityId id)
        {
            var p = new PersonalityProfile();
            p.Id = id;
            p.Name = id.ToString();
            return p;
        }

        private void SyncWithSettings()
        {
            try
            {
                var selectedId = AtlasAI.Settings.SettingsStore.Current.PersonalitySelected;

                // Map current personality IDs to the legacy voice profile enum for TTS compatibility.
                Id = selectedId switch
                {
                    "Atlas" => PersonalityId.Atlas,
                    "Professional" => PersonalityId.Professional,
                    "Buddy" => PersonalityId.Friendly,
                    "Funny" => PersonalityId.Funny,
                    "Sarcasm" => PersonalityId.Funny,
                    "Romantic" => PersonalityId.Friendly,
                    "MediaWizard" => PersonalityId.Friendly,
                    "TotalDJ" => PersonalityId.Funny,
                    "DownloadMaster" => PersonalityId.Serious,
                    "CreativeGenius" => PersonalityId.Friendly,
                    "CompleteCoder" => PersonalityId.Serious,
                    "ChaosTesting" => PersonalityId.Serious,
                    "Unfiltered" => PersonalityId.Unfiltered,
                    _ => PersonalityId.Atlas
                };
                
                Name = selectedId;
            }
            catch
            {
                // Fallback
                Id = PersonalityId.Atlas;
            }
        }

        private static PersonalityProfile LoadOrDefault()
        {
            return new PersonalityProfile();
        }
    }
}
