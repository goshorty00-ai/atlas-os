using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace AtlasAI.Memory
{
    /// <summary>
    /// Tracks tool/skill usage patterns to improve recommendations
    /// and identify which tools work best for the user
    /// </summary>
    public class SkillUsageTracker
    {
        private readonly LongTermMemoryStore _store;
        private static SkillUsageTracker? _instance;
        private readonly Dictionary<string, Stopwatch> _activeTimers = new();

        public static SkillUsageTracker Instance => _instance ??= new SkillUsageTracker();

        private SkillUsageTracker()
        {
            _store = LongTermMemoryStore.Instance;
        }

        /// <summary>
        /// Start tracking a skill execution
        /// </summary>
        public void StartTracking(string skillName)
        {
            var sw = new Stopwatch();
            sw.Start();
            _activeTimers[skillName] = sw;
            Debug.WriteLine($"[SkillTracker] Started tracking: {skillName}");
        }

        /// <summary>
        /// Complete tracking with success/failure
        /// </summary>
        public async Task CompleteTrackingAsync(string skillName, bool success, string? errorMessage = null)
        {
            int durationMs = 0;
            
            if (_activeTimers.TryGetValue(skillName, out var sw))
            {
                sw.Stop();
                durationMs = (int)sw.ElapsedMilliseconds;
                _activeTimers.Remove(skillName);
            }

            await _store.TrackSkillUsageAsync(skillName, success, durationMs);

            if (!success && !string.IsNullOrEmpty(errorMessage))
            {
                // Learn from failures
                await _store.LearnFactAsync(
                    $"Tool '{skillName}' failed with: {errorMessage.Substring(0, Math.Min(100, errorMessage.Length))}",
                    "tool_errors",
                    0.8,
                    "execution"
                );
            }

            Debug.WriteLine($"[SkillTracker] Completed: {skillName} - Success: {success}, Duration: {durationMs}ms");
        }

        /// <summary>
        /// Track a skill usage in one call (for quick operations)
        /// </summary>
        public async Task TrackUsageAsync(string skillName, bool success, int durationMs = 0)
        {
            await _store.TrackSkillUsageAsync(skillName, success, durationMs);
        }

        /// <summary>
        /// Get usage statistics for all skills
        /// </summary>
        public async Task<Dictionary<string, SkillStats>> GetAllStatsAsync()
        {
            var rawStats = await _store.GetSkillStatsAsync();
            return rawStats.ToDictionary(
                kvp => kvp.Key,
                kvp => new SkillStats
                {
                    SkillName = kvp.Key,
                    TotalUses = kvp.Value.Uses,
                    Successes = kvp.Value.Successes,
                    Failures = kvp.Value.Failures,
                    SuccessRate = kvp.Value.Uses > 0 
                        ? (double)kvp.Value.Successes / kvp.Value.Uses 
                        : 0
                }
            );
        }

        /// <summary>
        /// Get the most used skills
        /// </summary>
        public async Task<List<string>> GetMostUsedSkillsAsync(int count = 5)
        {
            var stats = await GetAllStatsAsync();
            return stats
                .OrderByDescending(s => s.Value.TotalUses)
                .Take(count)
                .Select(s => s.Key)
                .ToList();
        }

        /// <summary>
        /// Get skills with high failure rates (for improvement)
        /// </summary>
        public async Task<List<(string Skill, double FailureRate)>> GetProblematicSkillsAsync()
        {
            var stats = await GetAllStatsAsync();
            return stats
                .Where(s => s.Value.TotalUses >= 3 && s.Value.SuccessRate < 0.7)
                .OrderBy(s => s.Value.SuccessRate)
                .Select(s => (s.Key, 1 - s.Value.SuccessRate))
                .ToList();
        }

        /// <summary>
        /// Get recommended skills based on usage patterns
        /// </summary>
        public async Task<List<string>> GetRecommendedSkillsAsync(string context)
        {
            var stats = await GetAllStatsAsync();
            
            // Prioritize skills with high success rates and usage
            return stats
                .Where(s => s.Value.SuccessRate >= 0.7)
                .OrderByDescending(s => s.Value.TotalUses * s.Value.SuccessRate)
                .Take(5)
                .Select(s => s.Key)
                .ToList();
        }

        /// <summary>
        /// Build a context string about skill usage for AI prompts
        /// </summary>
        public async Task<string> BuildSkillContextAsync()
        {
            var stats = await GetAllStatsAsync();
            if (stats.Count == 0) return "";

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("## Tool Usage History:");

            // Most used
            var mostUsed = stats.OrderByDescending(s => s.Value.TotalUses).Take(5);
            sb.AppendLine("Most used tools:");
            foreach (var skill in mostUsed)
            {
                sb.AppendLine($"- {skill.Key}: {skill.Value.TotalUses} uses, {skill.Value.SuccessRate:P0} success rate");
            }

            // Problematic tools
            var problematic = stats.Where(s => s.Value.TotalUses >= 3 && s.Value.SuccessRate < 0.7).ToList();
            if (problematic.Count > 0)
            {
                sb.AppendLine("\nTools with issues (use alternatives if possible):");
                foreach (var skill in problematic)
                {
                    sb.AppendLine($"- {skill.Key}: {skill.Value.SuccessRate:P0} success rate");
                }
            }

            return sb.ToString();
        }
    }

    /// <summary>
    /// Statistics for a single skill
    /// </summary>
    public class SkillStats
    {
        public string SkillName { get; set; } = "";
        public int TotalUses { get; set; }
        public int Successes { get; set; }
        public int Failures { get; set; }
        public double SuccessRate { get; set; }
        public int AverageDurationMs { get; set; }
    }
}
