using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace AtlasAI.Memory
{
    /// <summary>
    /// Learns from user corrections to avoid repeating mistakes
    /// When user says "No, I meant X" or "That's wrong, do Y instead",
    /// Atlas remembers and applies this in future interactions
    /// </summary>
    public class MistakeCorrectionMemory
    {
        private readonly LongTermMemoryStore _store;
        private static MistakeCorrectionMemory? _instance;

        public static MistakeCorrectionMemory Instance => _instance ??= new MistakeCorrectionMemory();

        private MistakeCorrectionMemory()
        {
            _store = LongTermMemoryStore.Instance;
        }

        #region Correction Detection

        /// <summary>
        /// Analyze a user message for corrections
        /// </summary>
        public async Task<CorrectionResult> AnalyzeForCorrectionAsync(string userMessage, string? previousAtlasAction = null)
        {
            var result = new CorrectionResult();
            var messageLower = userMessage.ToLower();

            // Pattern: "No, I meant X" / "I meant X not Y"
            var meantMatch = Regex.Match(userMessage, @"(?:no,?\s*)?i\s+meant\s+(.+?)(?:\s+not\s+(.+?))?(?:\.|$)", RegexOptions.IgnoreCase);
            if (meantMatch.Success)
            {
                result.IsCorrection = true;
                result.CorrectedAction = meantMatch.Groups[1].Value.Trim();
                if (meantMatch.Groups[2].Success)
                    result.OriginalMistake = meantMatch.Groups[2].Value.Trim();
                else if (previousAtlasAction != null)
                    result.OriginalMistake = previousAtlasAction;
            }

            // Pattern: "That's wrong" / "That's not right" / "No, that's incorrect"
            if (Regex.IsMatch(messageLower, @"(?:that'?s|this is)\s+(?:wrong|incorrect|not right|not what i)"))
            {
                result.IsCorrection = true;
                result.OriginalMistake = previousAtlasAction;
                
                // Look for what they actually want
                var wantMatch = Regex.Match(userMessage, @"(?:i\s+(?:want|need|meant)|do|use)\s+(.+?)(?:\.|$)", RegexOptions.IgnoreCase);
                if (wantMatch.Success)
                    result.CorrectedAction = wantMatch.Groups[1].Value.Trim();
            }

            // Pattern: "Don't do X, do Y" / "Not X, Y"
            var notDoMatch = Regex.Match(userMessage, @"(?:don'?t|not)\s+(?:do\s+)?(.+?),?\s+(?:do|use|try)?\s*(.+?)(?:\.|$)", RegexOptions.IgnoreCase);
            if (notDoMatch.Success)
            {
                result.IsCorrection = true;
                result.OriginalMistake = notDoMatch.Groups[1].Value.Trim();
                result.CorrectedAction = notDoMatch.Groups[2].Value.Trim();
            }

            // Pattern: "Instead of X, do Y" / "Rather than X, Y"
            var insteadMatch = Regex.Match(userMessage, @"(?:instead of|rather than)\s+(.+?),?\s+(?:do|use|try)?\s*(.+?)(?:\.|$)", RegexOptions.IgnoreCase);
            if (insteadMatch.Success)
            {
                result.IsCorrection = true;
                result.OriginalMistake = insteadMatch.Groups[1].Value.Trim();
                result.CorrectedAction = insteadMatch.Groups[2].Value.Trim();
            }

            // Pattern: "X is wrong" / "X doesn't work"
            var wrongMatch = Regex.Match(userMessage, @"(.+?)\s+(?:is wrong|doesn'?t work|isn'?t working|failed)", RegexOptions.IgnoreCase);
            if (wrongMatch.Success)
            {
                result.IsCorrection = true;
                result.OriginalMistake = wrongMatch.Groups[1].Value.Trim();
            }

            // If we detected a correction, save it
            if (result.IsCorrection && !string.IsNullOrEmpty(result.OriginalMistake))
            {
                await RecordCorrectionAsync(result.OriginalMistake, result.CorrectedAction ?? "avoid this", userMessage);
            }

            return result;
        }

        #endregion

        #region Correction Storage

        /// <summary>
        /// Record a correction
        /// </summary>
        public async Task RecordCorrectionAsync(string originalMistake, string correction, string context)
        {
            await _store.RecordCorrectionAsync(originalMistake, correction, context);
            System.Diagnostics.Debug.WriteLine($"[Corrections] Recorded: '{originalMistake}' → '{correction}'");
        }

        /// <summary>
        /// Check if an action should be corrected
        /// </summary>
        public async Task<string?> GetCorrectionAsync(string plannedAction)
        {
            return await _store.GetCorrectionForAsync(plannedAction);
        }

        /// <summary>
        /// Get all corrections for context
        /// </summary>
        public async Task<List<CorrectionEntry>> GetAllCorrectionsAsync()
        {
            var corrections = await _store.GetAllCorrectionsAsync();
            return corrections.Select(c => new CorrectionEntry
            {
                OriginalMistake = c.Original,
                Correction = c.Corrected,
                TimesApplied = c.TimesApplied
            }).ToList();
        }

        #endregion

        #region Apply Corrections

        /// <summary>
        /// Apply known corrections to a planned action
        /// </summary>
        public async Task<(string Action, bool WasCorrected, string? Reason)> ApplyCorrectionsAsync(string plannedAction)
        {
            var corrections = await GetAllCorrectionsAsync();
            
            foreach (var correction in corrections.OrderByDescending(c => c.TimesApplied))
            {
                // Check if the planned action matches a known mistake
                if (plannedAction.Contains(correction.OriginalMistake, StringComparison.OrdinalIgnoreCase))
                {
                    var correctedAction = plannedAction.Replace(
                        correction.OriginalMistake, 
                        correction.Correction, 
                        StringComparison.OrdinalIgnoreCase);
                    
                    return (correctedAction, true, $"Applied learned correction: '{correction.OriginalMistake}' → '{correction.Correction}'");
                }
            }

            return (plannedAction, false, null);
        }

        /// <summary>
        /// Check if a tool should be avoided based on corrections
        /// </summary>
        public async Task<(bool ShouldAvoid, string? Alternative, string? Reason)> ShouldAvoidToolAsync(string toolName)
        {
            var corrections = await GetAllCorrectionsAsync();
            
            foreach (var correction in corrections)
            {
                if (correction.OriginalMistake.Contains(toolName, StringComparison.OrdinalIgnoreCase) &&
                    correction.Correction.Contains("avoid", StringComparison.OrdinalIgnoreCase))
                {
                    // Extract alternative if mentioned
                    var altMatch = Regex.Match(correction.Correction, @"use\s+(\w+)", RegexOptions.IgnoreCase);
                    var alternative = altMatch.Success ? altMatch.Groups[1].Value : null;
                    
                    return (true, alternative, $"User previously said to avoid {toolName}");
                }
            }

            return (false, null, null);
        }

        #endregion

        #region Context Building

        /// <summary>
        /// Build correction context for AI prompts
        /// </summary>
        public async Task<string> BuildCorrectionContextAsync()
        {
            var corrections = await GetAllCorrectionsAsync();
            if (corrections.Count == 0) return "";

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("## IMPORTANT - User Corrections (MUST follow these):");
            
            foreach (var correction in corrections.OrderByDescending(c => c.TimesApplied).Take(15))
            {
                sb.AppendLine($"- ❌ '{correction.OriginalMistake}' → ✅ '{correction.Correction}' (corrected {correction.TimesApplied}x)");
            }

            return sb.ToString();
        }

        #endregion
    }

    /// <summary>
    /// Result of analyzing a message for corrections
    /// </summary>
    public class CorrectionResult
    {
        public bool IsCorrection { get; set; }
        public string? OriginalMistake { get; set; }
        public string? CorrectedAction { get; set; }
    }

    /// <summary>
    /// A stored correction entry
    /// </summary>
    public class CorrectionEntry
    {
        public string OriginalMistake { get; set; } = "";
        public string Correction { get; set; } = "";
        public int TimesApplied { get; set; }
    }
}
