using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AtlasAI.Agent
{
    /// <summary>
    /// Risk level for macro operations
    /// </summary>
    public enum MacroRiskLevel
    {
        SafeReadOnly,   // No system changes, read-only queries
        LowRisk         // Minor changes that are easily reversible
    }

    /// <summary>
    /// Result card for macro output display
    /// </summary>
    public class MacroResultCard
    {
        public string Title { get; set; } = "";
        public string Icon { get; set; } = "📋";
        public List<MacroResultRow> Rows { get; set; } = new();
        public string? Footer { get; set; }
        public string? StatusColor { get; set; } // cyan, green, yellow, red
    }

    public class MacroResultRow
    {
        public string Label { get; set; } = "";
        public string Value { get; set; } = "";
        public string? Icon { get; set; }
        public string? ValueColor { get; set; }
    }

    /// <summary>
    /// Structured result from macro execution
    /// </summary>
    public class MacroResult
    {
        public bool Success { get; set; }
        public string MacroId { get; set; } = "";
        public string MacroTitle { get; set; } = "";
        public List<MacroResultCard> Cards { get; set; } = new();
        public string? Summary { get; set; }
        public string? ErrorMessage { get; set; }
        public TimeSpan ExecutionTime { get; set; }
        public DateTime ExecutedAt { get; set; } = DateTime.Now;
        
        /// <summary>
        /// One-line confidence statement (e.g., "Your system is operating normally.")
        /// </summary>
        public string? ConfidenceStatement { get; set; }
        
        /// <summary>
        /// Suggested next actions (read-only macros only)
        /// </summary>
        public List<MacroSuggestion> Suggestions { get; set; } = new();
    }

    /// <summary>
    /// Base definition for an agent macro
    /// </summary>
    public abstract class AgentMacroDefinition
    {
        public abstract string Id { get; }
        public abstract string Title { get; }
        public abstract string Description { get; }
        public abstract string Icon { get; }
        public abstract MacroRiskLevel Risk { get; }
        public abstract string[] Keywords { get; }

        /// <summary>
        /// Execute the macro and return structured results
        /// </summary>
        public abstract Task<MacroResult> ExecuteAsync();

        /// <summary>
        /// Check if this macro matches the given input
        /// </summary>
        public virtual bool Matches(string input)
        {
            var lower = input.ToLowerInvariant();
            foreach (var keyword in Keywords)
            {
                if (lower.Contains(keyword.ToLowerInvariant()))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Calculate match score (higher = better match)
        /// </summary>
        public virtual int GetMatchScore(string input)
        {
            var lower = input.ToLowerInvariant();
            int score = 0;
            foreach (var keyword in Keywords)
            {
                if (lower.Contains(keyword.ToLowerInvariant()))
                    score += keyword.Length;
            }
            return score;
        }
    }
}
