using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AtlasAI.Agent
{
    /// <summary>
    /// Risk level for agent actions
    /// </summary>
    public enum ActionRiskLevel
    {
        LowRisk,    // Safe one-click utilities (clipboard, open settings, launch apps)
        MediumRisk, // Requires confirmation (flush DNS, clear temp files)
        HighRisk    // Requires LIVE mode + confirmation (registry, delete, uninstall)
    }

    /// <summary>
    /// Category for organizing actions
    /// </summary>
    public enum ActionCategory
    {
        Networking,
        UI,
        Diagnostics,
        Files,
        Apps
    }

    /// <summary>
    /// Result from executing an agent action
    /// </summary>
    public class ActionResult
    {
        public bool Success { get; set; }
        public string? Message { get; set; }
        public string? Output { get; set; }
        public string? OpenFilePath { get; set; }
        public string? ErrorMessage { get; set; }
        public TimeSpan ExecutionTime { get; set; }
        public DateTime ExecutedAt { get; set; } = DateTime.Now;
    }

    /// <summary>
    /// Base definition for an agent action (one-click utility)
    /// </summary>
    public abstract class AgentActionDefinition
    {
        public abstract string Id { get; }
        public abstract string Title { get; }
        public abstract string Description { get; }
        public abstract string Icon { get; }
        public abstract ActionCategory Category { get; }
        public abstract ActionRiskLevel Risk { get; }
        public abstract string[] Keywords { get; }

        /// <summary>
        /// Execute the action and return result
        /// </summary>
        public abstract Task<ActionResult> ExecuteAsync();

        /// <summary>
        /// Check if this action matches the given input
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
