using System;
using System.Diagnostics;

namespace AtlasAI.Voice
{
    /// <summary>
    /// Response type categories for voice selection.
    /// </summary>
    public enum ResponseType
    {
        /// <summary>Normal conversational response.</summary>
        Normal,
        /// <summary>System boot message.</summary>
        Boot,
        /// <summary>System alert or warning.</summary>
        Alert,
        /// <summary>Error notification.</summary>
        Error
    }

    /// <summary>
    /// Voice selection result with logging context.
    /// </summary>
    public class VoiceSelectionResult
    {
        public VoiceProfile Voice { get; set; } = VoiceProfile.AtlasDefault;
        public string Reason { get; set; } = string.Empty;
        public PersonalityId PersonalityId { get; set; }
        public ResponseType ResponseType { get; set; }
        public string? CadenceHint { get; set; }
        public string SelectionRule { get; set; } = string.Empty;
    }

    /// <summary>
    /// Single source of truth for voice selection.
    /// Deterministic, logged, never AI-driven.
    /// 
    /// Selection priority (for normal responses):
    /// 1. User's SystemVoiceId (for Boot/Alert/Error only)
    /// 2. User's GlobalVoiceId (if set, overrides everything for normal responses)
    /// 3. User's PersonalityVoiceOverrides[currentPersonality] (if set)
    /// 4. Personality's DefaultVoice (fallback)
    /// </summary>
    public static class VoiceSelectionService
    {
        private static readonly object _lock = new();

        /// <summary>
        /// Global system override voice (used for Boot/Alert/Error).
        /// If null, uses VoiceProfile.SystemVoice.
        /// </summary>
        public static VoiceProfile? GlobalSystemVoice { get; set; }

        /// <summary>
        /// Event fired on every voice selection for debugging.
        /// </summary>
        public static event Action<VoiceSelectionResult>? VoiceSelected;

        /// <summary>
        /// Select voice based on response type and active personality.
        /// This is the ONLY method that should determine voice selection.
        /// 
        /// Selection rules (in order):
        /// 1. System/Boot/Alert → SystemVoiceId preference → default system voice
        /// 2. GlobalVoiceId preference → overrides all normal responses
        /// 3. PersonalityVoiceOverrides[personality] → per-personality user choice
        /// 4. Personality.DefaultVoice → built-in default
        /// </summary>
        public static VoiceSelectionResult SelectVoice(ResponseType responseType = ResponseType.Normal)
        {
            lock (_lock)
            {
                var prefs = VoicePreferences.Current;
                var result = new VoiceSelectionResult
                {
                    ResponseType = responseType,
                    PersonalityId = PersonalityProfile.Current.Id
                };

                // Rule 1: System/Boot/Alert responses use system voice
                if (responseType == ResponseType.Boot || 
                    responseType == ResponseType.Alert || 
                    responseType == ResponseType.Error)
                {
                    result = SelectSystemVoice(result, prefs);
                    LogSelection(result);
                    return result;
                }

                // Rule 2: Check user's global voice override
                if (!string.IsNullOrEmpty(prefs.GlobalVoiceId))
                {
                    var globalVoice = ResolveVoiceById(prefs.GlobalVoiceId);
                    if (globalVoice != null)
                    {
                        result.Voice = globalVoice;
                        result.Reason = $"Using global voice override: {globalVoice.DisplayName}";
                        result.SelectionRule = "GlobalOverride";
                        result.CadenceHint = GetCadenceHint(result.Voice.CadenceMultiplier, responseType);
                        LogSelection(result);
                        return result;
                    }
                    Debug.WriteLine($"[VoiceSelection] WARNING: Global voice ID '{prefs.GlobalVoiceId}' not found, falling through");
                }

                // Rule 3: Check user's per-personality override
                var personalityOverrideId = prefs.GetPersonalityVoice(PersonalityProfile.Current.Id);
                if (!string.IsNullOrEmpty(personalityOverrideId))
                {
                    var personalityVoice = ResolveVoiceById(personalityOverrideId);
                    if (personalityVoice != null)
                    {
                        result.Voice = personalityVoice;
                        result.Reason = $"Using {PersonalityProfile.Current.Name} personality override: {personalityVoice.DisplayName}";
                        result.SelectionRule = "PersonalityOverride";
                        result.CadenceHint = GetCadenceHint(result.Voice.CadenceMultiplier, responseType);
                        LogSelection(result);
                        return result;
                    }
                    Debug.WriteLine($"[VoiceSelection] WARNING: Personality voice ID '{personalityOverrideId}' not found, falling through");
                }

                // Rule 4: Use personality's default voice
                var personality = PersonalityProfile.Current;
                result.Voice = personality.DefaultVoice;
                result.Reason = $"Using {personality.Name} default voice: {result.Voice.DisplayName}";
                result.SelectionRule = "PersonalityDefault";
                result.CadenceHint = GetCadenceHint(result.Voice.CadenceMultiplier, responseType);

                LogSelection(result);
                return result;
            }
        }

        /// <summary>
        /// Select system voice for Boot/Alert/Error responses.
        /// </summary>
        private static VoiceSelectionResult SelectSystemVoice(VoiceSelectionResult result, VoicePreferences prefs)
        {
            // Check user's system voice preference first
            if (!string.IsNullOrEmpty(prefs.SystemVoiceId))
            {
                var userSystemVoice = ResolveVoiceById(prefs.SystemVoiceId);
                if (userSystemVoice != null)
                {
                    result.Voice = userSystemVoice;
                    result.Reason = $"System response using user preference: {userSystemVoice.DisplayName}";
                    result.SelectionRule = "UserSystemVoice";
                    result.CadenceHint = GetCadenceHint(result.Voice.CadenceMultiplier, result.ResponseType);
                    return result;
                }
            }

            // Check personality's SystemOverrideVoice
            var personalityOverride = PersonalityProfile.Current.SystemOverrideVoice;
            if (personalityOverride != null)
            {
                result.Voice = personalityOverride;
                result.Reason = $"System response using personality override: {personalityOverride.DisplayName}";
                result.SelectionRule = "PersonalitySystemOverride";
                result.CadenceHint = GetCadenceHint(result.Voice.CadenceMultiplier, result.ResponseType);
                return result;
            }

            // Check global system voice
            if (GlobalSystemVoice != null)
            {
                result.Voice = GlobalSystemVoice;
                result.Reason = $"System response using global override: {GlobalSystemVoice.DisplayName}";
                result.SelectionRule = "GlobalSystemVoice";
                result.CadenceHint = GetCadenceHint(result.Voice.CadenceMultiplier, result.ResponseType);
                return result;
            }

            // Fall back to default system voice
            result.Voice = VoiceProfile.SystemVoice;
            result.Reason = "System response using default system voice";
            result.SelectionRule = "DefaultSystemVoice";
            result.CadenceHint = GetCadenceHint(result.Voice.CadenceMultiplier, result.ResponseType);
            return result;
        }

        /// <summary>
        /// Resolve a voice ID to a VoiceProfile.
        /// </summary>
        private static VoiceProfile? ResolveVoiceById(string voiceId)
        {
            // Check catalog first
            var catalogVoice = VoiceCatalogService.Instance.GetVoice(voiceId);
            if (catalogVoice != null)
            {
                return catalogVoice.ToVoiceProfile();
            }

            // Check static voice profiles
            if (voiceId == VoiceProfile.AtlasDefault.VoiceId) return VoiceProfile.AtlasDefault;
            if (voiceId == VoiceProfile.AdamNarrator.VoiceId) return VoiceProfile.AdamNarrator;
            if (voiceId == VoiceProfile.ArnoldDeep.VoiceId) return VoiceProfile.ArnoldDeep;
            if (voiceId == VoiceProfile.AntoniWarm.VoiceId) return VoiceProfile.AntoniWarm;
            if (voiceId == VoiceProfile.RachelCalm.VoiceId) return VoiceProfile.RachelCalm;
            if (voiceId == VoiceProfile.SystemVoice.VoiceId) return VoiceProfile.SystemVoice;

            // Unknown voice IDs should not silently remain active in preferences.
            // Fall back to personality/default selection instead of attempting synthesis
            // with a stale or inaccessible library voice.
            return null;
        }

        /// <summary>
        /// Get the voice ID for TTS synthesis.
        /// Convenience method that returns just the voice ID.
        /// </summary>
        public static string GetVoiceId(ResponseType responseType = ResponseType.Normal)
        {
            return SelectVoice(responseType).Voice.VoiceId;
        }

        /// <summary>
        /// Get cadence hint for prompt phrasing.
        /// </summary>
        private static string? GetCadenceHint(double cadenceMultiplier, ResponseType responseType)
        {
            // System messages always use controlled pacing
            if (responseType != ResponseType.Normal)
            {
                return "controlled pacing";
            }

            return cadenceMultiplier switch
            {
                < 0.9 => "slightly slower",
                > 1.1 => "slightly faster",
                _ => null // Normal pacing, no hint needed
            };
        }

        /// <summary>
        /// Log voice selection decision for debugging.
        /// </summary>
        private static void LogSelection(VoiceSelectionResult result)
        {
            Debug.WriteLine($"[VoiceSelection] {result.ResponseType} | Personality: {result.PersonalityId} | " +
                          $"Voice: {result.Voice.DisplayName} ({result.Voice.VoiceId}) | " +
                          $"Rule: {result.SelectionRule} | " +
                          $"Cadence: {result.Voice.CadenceMultiplier:F2} | " +
                          $"Hint: {result.CadenceHint ?? "none"} | " +
                          $"Reason: {result.Reason}");

            VoiceSelected?.Invoke(result);
        }

        /// <summary>
        /// Get a summary of current voice configuration for debugging.
        /// </summary>
        public static string GetConfigurationSummary()
        {
            var prefs = VoicePreferences.Current;
            var lines = new System.Collections.Generic.List<string>
            {
                "=== Voice Configuration ===",
                $"Global Override: {prefs.GlobalVoiceId ?? "(none)"}",
                $"System Voice: {prefs.SystemVoiceId ?? "(default)"}",
                "Personality Overrides:"
            };

            foreach (PersonalityId id in Enum.GetValues<PersonalityId>())
            {
                var overrideId = prefs.GetPersonalityVoice(id);
                var defaultVoice = PersonalityProfile.GetDefault(id).DefaultVoice;
                lines.Add($"  {id}: {overrideId ?? $"(default: {defaultVoice.DisplayName})"}");
            }

            return string.Join("\n", lines);
        }

        /// <summary>
        /// Validate that all personalities have valid voice profiles.
        /// Call at startup to catch configuration errors early.
        /// </summary>
        public static bool ValidateConfiguration()
        {
            var allValid = true;

            foreach (PersonalityId id in Enum.GetValues<PersonalityId>())
            {
                var profile = PersonalityProfile.GetDefault(id);
                if (profile.DefaultVoice == null || string.IsNullOrEmpty(profile.DefaultVoice.VoiceId))
                {
                    Debug.WriteLine($"[VoiceSelection] ERROR: Personality {id} has no default voice configured!");
                    allValid = false;
                }
                else
                {
                    Debug.WriteLine($"[VoiceSelection] Validated: {id} -> {profile.DefaultVoice.DisplayName}");
                }
            }

            return allValid;
        }
    }
}
