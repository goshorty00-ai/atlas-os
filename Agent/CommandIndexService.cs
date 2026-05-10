using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using AtlasAI.Core;
using AtlasAI.Workflows;

namespace AtlasAI.Agent
{
    /// <summary>
    /// Unified Command Index Service - Aggregates all executable commands from:
    /// - Agent Actions (one-click utilities)
    /// - Agent Macros (read-only diagnostics)
    /// - Workflows (multi-step diagnostic chains)
    /// - Navigation Targets (UI panels/pages)
    /// - Recent Commands (from session memory)
    /// 
    /// Provides fast fuzzy search and execution routing.
    /// Integrates with PreferencesStore for personalized ranking.
    /// </summary>
    public class CommandIndexService
    {
        private static CommandIndexService? _instance;
        public static CommandIndexService Instance => _instance ??= new CommandIndexService();

        private readonly List<CommandEntry> _allCommands = new();
        private readonly List<CommandEntry> _recentCommands = new();
        private const int MaxRecentCommands = 10;

        // Preference-based scoring boosts
        private const int PinnedBoost = 500;
        private const int MostUsedBoostBase = 100;
        private const int MostUsedBoostPerUse = 10;
        private const int RecentBoost = 50;
        private const double TypeBiasMultiplier = 1.2;

        public event EventHandler? IndexUpdated;

        private CommandIndexService()
        {
            BuildIndex();
        }

        /// <summary>
        /// Build/rebuild the command index from all sources
        /// </summary>
        public void BuildIndex()
        {
            var sw = Stopwatch.StartNew();
            _allCommands.Clear();

            // 1. Agent Actions
            foreach (var action in AgentActionEngine.Instance.Actions)
            {
                _allCommands.Add(new CommandEntry
                {
                    Id = action.Id,
                    Type = CommandType.Action,
                    DisplayText = action.Title,
                    Description = action.Description,
                    Icon = action.Icon,
                    Keywords = action.Keywords.ToList(),
                    Category = action.Category.ToString()
                });
            }

            // 2. Agent Macros
            foreach (var macro in AgentMacroEngine.Instance.GetMacroSummaries())
            {
                _allCommands.Add(new CommandEntry
                {
                    Id = macro.Id,
                    Type = CommandType.Macro,
                    DisplayText = macro.Title,
                    Description = macro.Description,
                    Icon = macro.Icon,
                    Keywords = macro.Keywords.ToList(),
                    Category = "Diagnostics"
                });
            }

            // 3. Workflows
            foreach (var workflow in WorkflowEngine.Instance.Definitions)
            {
                _allCommands.Add(new CommandEntry
                {
                    Id = workflow.Id,
                    Type = CommandType.Workflow,
                    DisplayText = workflow.Title,
                    Description = workflow.Description,
                    Icon = workflow.Icon,
                    Keywords = workflow.TriggerKeywords.ToList(),
                    Category = workflow.Category
                });
            }

            // 4. Navigation Targets
            AddNavigationTargets();

            sw.Stop();
            Debug.WriteLine($"[CommandIndex] Built index with {_allCommands.Count} commands in {sw.ElapsedMilliseconds}ms");
            IndexUpdated?.Invoke(this, EventArgs.Empty);
        }

        private void AddNavigationTargets()
        {
            var navTargets = new[]
            {
                new CommandEntry { Id = "nav-dashboard", Type = CommandType.Navigation, DisplayText = "Dashboard", Description = "Go to main dashboard", Icon = "🏠", Keywords = new List<string> { "home", "dashboard", "main" }, Category = "Navigation" },
                new CommandEntry { Id = "nav-agent-mode", Type = CommandType.Navigation, DisplayText = "Agent Mode", Description = "Open Agent Mode panel", Icon = "⚡", Keywords = new List<string> { "agent", "mode", "macros", "actions" }, Category = "Navigation" },
                new CommandEntry { Id = "nav-security", Type = CommandType.Navigation, DisplayText = "Security", Description = "Open Security page", Icon = "🛡️", Keywords = new List<string> { "security", "scan", "threats", "protection" }, Category = "Navigation" },
                new CommandEntry { Id = "nav-code", Type = CommandType.Navigation, DisplayText = "Code", Description = "Open Code assistant", Icon = "💻", Keywords = new List<string> { "code", "programming", "developer", "ide" }, Category = "Navigation" },
                new CommandEntry { Id = "nav-create", Type = CommandType.Navigation, DisplayText = "Create", Description = "Open Create page", Icon = "✨", Keywords = new List<string> { "create", "generate", "new", "make" }, Category = "Navigation" },
                new CommandEntry { Id = "nav-memory", Type = CommandType.Navigation, DisplayText = "Memory", Description = "View Atlas memory", Icon = "🧠", Keywords = new List<string> { "memory", "history", "remember", "context" }, Category = "Navigation" },
                new CommandEntry { Id = "nav-commands", Type = CommandType.Navigation, DisplayText = "Commands", Description = "View all commands", Icon = "📋", Keywords = new List<string> { "commands", "list", "all", "help" }, Category = "Navigation" },
                new CommandEntry { Id = "nav-settings", Type = CommandType.Navigation, DisplayText = "Settings", Description = "Open settings", Icon = "⚙️", Keywords = new List<string> { "settings", "preferences", "config", "options" }, Category = "Navigation" },
                new CommandEntry { Id = "nav-history", Type = CommandType.Navigation, DisplayText = "Chat History", Description = "View conversation history", Icon = "📜", Keywords = new List<string> { "history", "chat", "conversations", "past" }, Category = "Navigation" },
                new CommandEntry { Id = "nav-integrations", Type = CommandType.Navigation, DisplayText = "Integrations", Description = "Manage integrations", Icon = "🔌", Keywords = new List<string> { "integrations", "connect", "apps", "services" }, Category = "Navigation" },
                new CommandEntry { Id = "nav-logs", Type = CommandType.Navigation, DisplayText = "Logs", Description = "View Atlas logs", Icon = "📂", Keywords = new List<string> { "logs", "debug", "errors", "output" }, Category = "Navigation" },
            };

            _allCommands.AddRange(navTargets);
        }

        /// <summary>
        /// Record a command execution for recent history and preferences
        /// </summary>
        public void RecordExecution(CommandEntry command)
        {
            // Remove if already in recent
            _recentCommands.RemoveAll(c => c.Id == command.Id && c.Type == command.Type);

            // Add to front
            var recentEntry = command.Clone();
            recentEntry.LastExecuted = DateTime.Now;
            _recentCommands.Insert(0, recentEntry);

            // Trim to max
            while (_recentCommands.Count > MaxRecentCommands)
                _recentCommands.RemoveAt(_recentCommands.Count - 1);

            // Also record in persistent preferences
            PreferencesStore.Instance.RecordCommandExecution(command.Id, command.Type.ToString());
        }

        /// <summary>
        /// Toggle pinned status for a command
        /// </summary>
        public void TogglePinned(string commandId)
        {
            PreferencesStore.Instance.TogglePinned(commandId);
        }

        /// <summary>
        /// Check if a command is pinned
        /// </summary>
        public bool IsPinned(string commandId)
        {
            return PreferencesStore.Instance.IsPinned(commandId);
        }

        /// <summary>
        /// Search commands with fuzzy matching
        /// </summary>
        public List<CommandEntry> Search(string query, int maxResults = 15)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                // Empty query - return suggested (recent + common)
                return GetSuggested(maxResults);
            }

            var lower = query.ToLowerInvariant().Trim();
            var words = lower.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            // Apply smart hints based on query prefix
            var biasType = DetectBias(lower);

            var scored = _allCommands
                .Select(cmd => new { Command = cmd, Score = CalculateScore(cmd, lower, words, biasType) })
                .Where(x => x.Score > 0)
                .OrderByDescending(x => x.Score)
                .ThenBy(x => x.Command.DisplayText.Length)
                .Take(maxResults)
                .Select(x => x.Command)
                .ToList();

            return scored;
        }

        /// <summary>
        /// Get suggested commands (pinned + recent + common)
        /// </summary>
        public List<CommandEntry> GetSuggested(int maxResults = 10)
        {
            var suggested = new List<CommandEntry>();
            var prefs = PreferencesStore.Instance.Current;

            // Add pinned commands first
            foreach (var pinnedId in prefs.PinnedCommandIds.Take(3))
            {
                var cmd = _allCommands.FirstOrDefault(c => c.Id == pinnedId);
                if (cmd != null)
                {
                    var entry = cmd.Clone();
                    entry.IsPinned = true;
                    suggested.Add(entry);
                }
            }

            // Add recent commands from session
            foreach (var recent in _recentCommands.Take(5))
            {
                if (suggested.Any(s => s.Id == recent.Id)) continue;
                var entry = recent.Clone();
                entry.IsRecent = true;
                suggested.Add(entry);
            }

            // Add most-used commands
            var topUsed = prefs.MostUsedCommands
                .OrderByDescending(kv => kv.Value)
                .Take(3)
                .Select(kv => kv.Key);

            foreach (var usedId in topUsed)
            {
                if (suggested.Count >= maxResults) break;
                if (suggested.Any(s => s.Id == usedId)) continue;

                var cmd = _allCommands.FirstOrDefault(c => c.Id == usedId);
                if (cmd != null)
                    suggested.Add(cmd);
            }

            // Add common/popular commands as fallback
            var common = new[] { "system-overview", "network-snapshot", "open-task-manager", "nav-agent-mode", "nav-settings" };
            foreach (var id in common)
            {
                if (suggested.Count >= maxResults) break;
                if (suggested.Any(s => s.Id == id)) continue;

                var cmd = _allCommands.FirstOrDefault(c => c.Id == id);
                if (cmd != null)
                    suggested.Add(cmd);
            }

            return suggested.Take(maxResults).ToList();
        }

        /// <summary>
        /// Execute a command and return result
        /// </summary>
        public async Task<CommandExecutionResult> ExecuteAsync(CommandEntry command)
        {
            var sw = Stopwatch.StartNew();
            var result = new CommandExecutionResult { Command = command };

            try
            {
                switch (command.Type)
                {
                    case CommandType.Action:
                        var actionResult = await AgentActionEngine.Instance.ExecuteByIdAsync(command.Id);
                        result.Success = actionResult?.Success ?? false;
                        result.Message = actionResult?.Message ?? actionResult?.ErrorMessage;
                        result.ActionResult = actionResult;
                        break;

                    case CommandType.Macro:
                        var macroResult = await AgentMacroEngine.Instance.ExecuteByIdAsync(command.Id);
                        result.Success = macroResult?.Success ?? false;
                        result.Message = macroResult?.Summary ?? macroResult?.ErrorMessage;
                        result.MacroResult = macroResult;
                        break;

                    case CommandType.Workflow:
                        var workflowInstance = WorkflowEngine.Instance.StartWorkflow(command.Id);
                        result.Success = workflowInstance != null;
                        result.Message = workflowInstance != null 
                            ? $"Started workflow: {workflowInstance.Title}" 
                            : "Failed to start workflow";
                        result.WorkflowInstance = workflowInstance;
                        break;

                    case CommandType.Navigation:
                        result.Success = true;
                        result.NavigationTarget = command.Id;
                        result.Message = $"Navigate to {command.DisplayText}";
                        break;

                    default:
                        result.Success = false;
                        result.Message = "Unknown command type";
                        break;
                }

                // Record successful execution
                if (result.Success)
                    RecordExecution(command);
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = ex.Message;
                Debug.WriteLine($"[CommandIndex] Execution error: {ex.Message}");
            }

            sw.Stop();
            result.ExecutionTime = sw.Elapsed;
            return result;
        }

        #region Scoring

        private CommandType? DetectBias(string query)
        {
            // Smart hints based on query prefix
            if (query.StartsWith("open ") || query.StartsWith("launch ") || query.StartsWith("start "))
                return CommandType.Action;

            if (query.StartsWith("show ") || query.StartsWith("view ") || query.StartsWith("check ") || query.StartsWith("scan "))
                return CommandType.Macro;

            if (query.StartsWith("go ") || query.StartsWith("navigate ") || query.StartsWith("goto "))
                return CommandType.Navigation;

            return null;
        }

        private int CalculateScore(CommandEntry cmd, string query, string[] words, CommandType? bias)
        {
            int score = 0;
            var displayLower = cmd.DisplayText.ToLowerInvariant();
            var descLower = cmd.Description.ToLowerInvariant();

            // Exact match on display text
            if (displayLower == query)
                score += 1000;

            // Display text starts with query
            if (displayLower.StartsWith(query))
                score += 500;

            // Display text contains query
            if (displayLower.Contains(query))
                score += 200;

            // Word matches in display text
            foreach (var word in words)
            {
                if (displayLower.Contains(word))
                    score += 100;
            }

            // Keyword matches
            foreach (var keyword in cmd.Keywords)
            {
                var keyLower = keyword.ToLowerInvariant();
                if (keyLower == query)
                    score += 300;
                if (keyLower.StartsWith(query))
                    score += 150;
                if (keyLower.Contains(query))
                    score += 75;

                foreach (var word in words)
                {
                    if (keyLower.Contains(word))
                        score += 50;
                }
            }

            // Description matches (lower weight)
            if (descLower.Contains(query))
                score += 30;

            foreach (var word in words)
            {
                if (descLower.Contains(word))
                    score += 15;
            }

            // Apply bias boost
            if (bias.HasValue && cmd.Type == bias.Value)
                score = (int)(score * 1.5);

            // Recent command boost (session)
            if (_recentCommands.Any(r => r.Id == cmd.Id && r.Type == cmd.Type))
                score += 50;

            // ═══════════════════════════════════════════════════════════════
            // PREFERENCE-BASED SCORING
            // ═══════════════════════════════════════════════════════════════
            var prefs = PreferencesStore.Instance.Current;

            // Pinned commands get highest boost
            if (prefs.PinnedCommandIds.Contains(cmd.Id))
                score += PinnedBoost;

            // Most-used commands get scaled boost
            if (prefs.MostUsedCommands.TryGetValue(cmd.Id, out int useCount))
                score += MostUsedBoostBase + (Math.Min(useCount, 20) * MostUsedBoostPerUse);

            // Recent commands from preferences (persistent across sessions)
            if (prefs.RecentCommandIds.Contains(cmd.Id))
            {
                int recentIndex = prefs.RecentCommandIds.IndexOf(cmd.Id);
                score += RecentBoost - (recentIndex * 2); // More recent = higher boost
            }

            // Type bias based on last used command type
            if (!string.IsNullOrEmpty(prefs.LastUsedCommandType) &&
                Enum.TryParse<CommandType>(prefs.LastUsedCommandType, out var lastType) &&
                cmd.Type == lastType)
            {
                score = (int)(score * TypeBiasMultiplier);
            }

            return score;
        }

        #endregion
    }

    #region Models

    public enum CommandType
    {
        Action,
        Macro,
        Workflow,
        Navigation,
        Recent
    }

    public class CommandEntry
    {
        public string Id { get; set; } = "";
        public CommandType Type { get; set; }
        public string DisplayText { get; set; } = "";
        public string Description { get; set; } = "";
        public string Icon { get; set; } = "";
        public List<string> Keywords { get; set; } = new();
        public string Category { get; set; } = "";
        public DateTime? LastExecuted { get; set; }
        public bool IsRecent { get; set; }
        public bool IsPinned { get; set; }

        public string TypeBadge => Type switch
        {
            CommandType.Action => "ACTION",
            CommandType.Macro => "MACRO",
            CommandType.Workflow => "WORKFLOW",
            CommandType.Navigation => "NAV",
            _ => ""
        };

        public string TypeColor => Type switch
        {
            CommandType.Action => "#22c55e",
            CommandType.Macro => "#22d3ee",
            CommandType.Workflow => "#f59e0b",
            CommandType.Navigation => "#8b5cf6",
            _ => "#64748b"
        };

        public CommandEntry Clone()
        {
            return new CommandEntry
            {
                Id = Id,
                Type = Type,
                DisplayText = DisplayText,
                Description = Description,
                Icon = Icon,
                Keywords = new List<string>(Keywords),
                Category = Category,
                LastExecuted = LastExecuted,
                IsRecent = IsRecent,
                IsPinned = IsPinned
            };
        }
    }

    public class CommandExecutionResult
    {
        public CommandEntry Command { get; set; } = new();
        public bool Success { get; set; }
        public string? Message { get; set; }
        public TimeSpan ExecutionTime { get; set; }
        public ActionResult? ActionResult { get; set; }
        public MacroResult? MacroResult { get; set; }
        public WorkflowChainInstance? WorkflowInstance { get; set; }
        public string? NavigationTarget { get; set; }
    }

    #endregion
}
