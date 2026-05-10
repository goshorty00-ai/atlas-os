using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace AtlasAI.Memory
{
    /// <summary>
    /// Central memory manager - coordinates all memory systems
    /// Use this as the main entry point for memory operations
    /// </summary>
    public class MemoryManager
    {
        private static MemoryManager? _instance;
        private string? _cachedContext;
        private DateTime _cacheTime = DateTime.MinValue;
        private readonly TimeSpan _cacheExpiry = TimeSpan.FromSeconds(30);
        private string? _currentWorkspacePath;
        
        public LongTermMemoryStore Store { get; }
        public UserPreferenceMemory Preferences { get; }
        public SkillUsageTracker SkillTracker { get; }
        public MistakeCorrectionMemory Corrections { get; }
        public RepositoryMemory Repository { get; }

        public static MemoryManager Instance => _instance ??= new MemoryManager();

        private MemoryManager()
        {
            Store = LongTermMemoryStore.Instance;
            Preferences = UserPreferenceMemory.Instance;
            SkillTracker = SkillUsageTracker.Instance;
            Corrections = MistakeCorrectionMemory.Instance;
            Repository = RepositoryMemory.Instance;
            
            // Pre-build context in background
            _ = Task.Run(async () => 
            {
                try
                {
                    _cachedContext = await BuildFullContextAsync();
                    _cacheTime = DateTime.Now;
                }
                catch { }
            });
            
            System.Diagnostics.Debug.WriteLine("[MemoryManager] Initialized all memory systems");
        }
        
        /// <summary>
        /// Set the current workspace path for repository memory.
        /// </summary>
        public void SetCurrentWorkspace(string workspacePath)
        {
            _currentWorkspacePath = workspacePath;
            System.Diagnostics.Debug.WriteLine($"[MemoryManager] Workspace set: {workspacePath}");
        }
        
        /// <summary>
        /// Get cached context for fast access (returns null if not cached)
        /// </summary>
        public string? GetCachedContext()
        {
            if (_cachedContext != null && DateTime.Now - _cacheTime < _cacheExpiry)
            {
                return _cachedContext;
            }
            
            // Refresh cache in background
            _ = Task.Run(async () =>
            {
                try
                {
                    _cachedContext = await BuildFullContextAsync();
                    _cacheTime = DateTime.Now;
                }
                catch { }
            });
            
            return _cachedContext; // Return stale cache while refreshing
        }

        #region Quick Access Methods

        /// <summary>
        /// Process a user message - learns preferences and detects corrections
        /// Call this for every user message
        /// </summary>
        public async Task ProcessUserMessageAsync(string userMessage, string? previousAtlasAction = null)
        {
            // Learn preferences from the message
            await Preferences.LearnFromMessageAsync(userMessage);
            
            // Check for corrections
            await Corrections.AnalyzeForCorrectionAsync(userMessage, previousAtlasAction);
        }

        /// <summary>
        /// Get user's name if known
        /// </summary>
        public async Task<string?> GetUserNameAsync()
        {
            return await Preferences.GetUserNameAsync();
        }

        /// <summary>
        /// Check if a tool should be avoided
        /// </summary>
        public async Task<(bool Avoid, string? Alternative)> ShouldAvoidToolAsync(string toolName)
        {
            // Check preferences
            if (await Preferences.ShouldAvoidToolAsync(toolName))
            {
                var alt = await Preferences.GetPreferredAlternativeAsync(toolName);
                return (true, alt);
            }

            // Check corrections
            var (shouldAvoid, alternative, _) = await Corrections.ShouldAvoidToolAsync(toolName);
            return (shouldAvoid, alternative);
        }

        /// <summary>
        /// Track a tool execution
        /// </summary>
        public void StartToolTracking(string toolName)
        {
            SkillTracker.StartTracking(toolName);
        }

        /// <summary>
        /// Complete tool tracking
        /// </summary>
        public async Task CompleteToolTrackingAsync(string toolName, bool success, string? error = null)
        {
            await SkillTracker.CompleteTrackingAsync(toolName, success, error);
        }

        /// <summary>
        /// Learn a fact about the user
        /// </summary>
        public async Task LearnFactAsync(string fact, string category = "general")
        {
            await Store.LearnFactAsync(fact, category, 1.0, "conversation");
        }

        /// <summary>
        /// Set a user preference
        /// </summary>
        public async Task SetPreferenceAsync(string key, string value, string category = "general")
        {
            await Preferences.SetPreferenceAsync(key, value, category);
        }

        /// <summary>
        /// Record a correction
        /// </summary>
        public async Task RecordCorrectionAsync(string mistake, string correction, string context = "")
        {
            await Corrections.RecordCorrectionAsync(mistake, correction, context);
        }

        #endregion

        #region Repository Memory Methods

        /// <summary>
        /// Learn a preferred command for the current workspace.
        /// </summary>
        public async Task LearnWorkspaceCommandAsync(string commandType, string command)
        {
            if (string.IsNullOrEmpty(_currentWorkspacePath)) return;
            await Repository.LearnCommandAsync(_currentWorkspacePath, commandType, command);
        }

        /// <summary>
        /// Get preferred command for the current workspace.
        /// </summary>
        public async Task<string?> GetWorkspaceCommandAsync(string commandType)
        {
            if (string.IsNullOrEmpty(_currentWorkspacePath)) return null;
            return await Repository.GetPreferredCommandAsync(_currentWorkspacePath, commandType);
        }

        /// <summary>
        /// Get the project type for the current workspace.
        /// </summary>
        public async Task<ProjectType> GetWorkspaceProjectTypeAsync()
        {
            if (string.IsNullOrEmpty(_currentWorkspacePath)) return ProjectType.Unknown;
            return await Repository.DetectProjectTypeAsync(_currentWorkspacePath);
        }

        /// <summary>
        /// Add an entry point file for the current workspace.
        /// </summary>
        public async Task AddWorkspaceEntryPointAsync(string relativePath, string description = "")
        {
            if (string.IsNullOrEmpty(_currentWorkspacePath)) return;
            await Repository.AddEntryPointAsync(_currentWorkspacePath, relativePath, description);
        }

        /// <summary>
        /// Track access to an entry point file.
        /// </summary>
        public async Task TrackEntryPointAccessAsync(string relativePath)
        {
            if (string.IsNullOrEmpty(_currentWorkspacePath)) return;
            await Repository.TrackEntryPointAccessAsync(_currentWorkspacePath, relativePath);
        }

        /// <summary>
        /// Reset workspace memory (for "Reset workspace memory" UI action).
        /// </summary>
        public async Task ResetWorkspaceMemoryAsync()
        {
            if (string.IsNullOrEmpty(_currentWorkspacePath)) return;
            await Repository.ResetWorkspaceMemoryAsync(_currentWorkspacePath);
        }

        #endregion

        #region Context Building

        /// <summary>
        /// Build complete memory context for AI prompts
        /// This should be included in system prompts to give AI memory
        /// </summary>
        public async Task<string> BuildFullContextAsync()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("# ATLAS MEMORY - What I know about you:\n");

            // Repository/workspace context (if workspace is set)
            if (!string.IsNullOrEmpty(_currentWorkspacePath))
            {
                var repoContext = await Repository.BuildWorkspaceContextAsync(_currentWorkspacePath);
                if (!string.IsNullOrEmpty(repoContext))
                    sb.AppendLine(repoContext);
            }

            // User preferences and facts
            var memoryContext = await Store.BuildMemoryContextAsync();
            if (!string.IsNullOrEmpty(memoryContext))
                sb.AppendLine(memoryContext);

            // Corrections (most important)
            var correctionContext = await Corrections.BuildCorrectionContextAsync();
            if (!string.IsNullOrEmpty(correctionContext))
                sb.AppendLine(correctionContext);

            // Skill usage
            var skillContext = await SkillTracker.BuildSkillContextAsync();
            if (!string.IsNullOrEmpty(skillContext))
                sb.AppendLine(skillContext);

            // Communication style
            var style = await Preferences.GetCommunicationStyleAsync();
            var stylePrompt = style.GetStylePrompt();
            if (!string.IsNullOrEmpty(stylePrompt))
            {
                sb.AppendLine();
                sb.AppendLine(stylePrompt);
            }

            return sb.ToString();
        }

        /// <summary>
        /// Build a concise memory context (for token-limited situations)
        /// </summary>
        public async Task<string> BuildConciseContextAsync()
        {
            var sb = new System.Text.StringBuilder();

            // Just corrections and key preferences
            var corrections = await Corrections.GetAllCorrectionsAsync();
            if (corrections.Count > 0)
            {
                sb.AppendLine("Remember: " + string.Join("; ", 
                    corrections.Take(5).Select(c => $"'{c.OriginalMistake}'→'{c.Correction}'")));
            }

            var userName = await GetUserNameAsync();
            if (!string.IsNullOrEmpty(userName))
                sb.AppendLine($"User's name: {userName}");

            return sb.ToString();
        }

        #endregion

        #region Memory Stats

        /// <summary>
        /// Get memory statistics
        /// </summary>
        public async Task<MemoryStats> GetStatsAsync()
        {
            var stats = new MemoryStats();
            
            var corrections = await Corrections.GetAllCorrectionsAsync();
            stats.TotalCorrections = corrections.Count;
            
            var facts = await Store.GetFactsAsync(limit: 1000);
            stats.TotalFacts = facts.Count;
            
            var skillStats = await SkillTracker.GetAllStatsAsync();
            stats.TotalSkillsTracked = skillStats.Count;
            stats.TotalToolExecutions = skillStats.Values.Sum(s => s.TotalUses);
            
            var prefs = await Store.GetPreferencesByCategoryAsync("tools");
            stats.TotalPreferences = prefs.Count;

            return stats;
        }

        #endregion
    }

    /// <summary>
    /// Memory system statistics
    /// </summary>
    public class MemoryStats
    {
        public int TotalCorrections { get; set; }
        public int TotalFacts { get; set; }
        public int TotalPreferences { get; set; }
        public int TotalSkillsTracked { get; set; }
        public int TotalToolExecutions { get; set; }

        public override string ToString()
        {
            return $"Memory: {TotalCorrections} corrections, {TotalFacts} facts, {TotalPreferences} preferences, {TotalToolExecutions} tool executions";
        }
    }
}
