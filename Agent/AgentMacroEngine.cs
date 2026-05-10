using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace AtlasAI.Agent
{
    /// <summary>
    /// Agent Macro Engine - Routes commands to safe read-only macros
    /// All macros are read-only and cannot perform destructive operations
    /// </summary>
    public class AgentMacroEngine
    {
        private static AgentMacroEngine? _instance;
        public static AgentMacroEngine Instance => _instance ??= new AgentMacroEngine();

        private readonly List<AgentMacroDefinition> _macros = new();
        private readonly List<MacroActivityEntry> _activityLog = new();
        private const int MaxActivityEntries = 50;

        public event EventHandler<MacroResult>? MacroExecuted;
        public event EventHandler<MacroActivityEntry>? ActivityLogged;

        public IReadOnlyList<AgentMacroDefinition> Macros => _macros.AsReadOnly();
        public IReadOnlyList<MacroActivityEntry> ActivityLog => _activityLog.AsReadOnly();

        private AgentMacroEngine()
        {
            RegisterBuiltInMacros();
        }

        private void RegisterBuiltInMacros()
        {
            // Register all 8 read-only macros
            _macros.Add(new Macros.SystemOverviewMacro());
            _macros.Add(new Macros.StartupInventoryMacro());
            _macros.Add(new Macros.NetworkSnapshotMacro());
            _macros.Add(new Macros.DiskHealthMacro());
            _macros.Add(new Macros.InstalledAppsMacro());
            _macros.Add(new Macros.SecurityStatusMacro());
            _macros.Add(new Macros.EventViewerMacro());
            _macros.Add(new Macros.PerformanceDiagnosticsMacro());
        }

        /// <summary>
        /// Find the best matching macro for the input
        /// </summary>
        public AgentMacroDefinition? FindMacro(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return null;

            var matches = _macros
                .Where(m => m.Matches(input))
                .OrderByDescending(m => m.GetMatchScore(input))
                .ToList();

            return matches.FirstOrDefault();
        }

        /// <summary>
        /// Try to execute a macro matching the input
        /// Returns null if no macro matches
        /// Integrates with AgentIntentController for "alive" feedback
        /// </summary>
        public async Task<MacroResult?> TryExecuteAsync(string input)
        {
            var intent = AgentIntentController.Instance;
            
            // Phase 1: Understanding (brief visual cue)
            await intent.BeginUnderstandingAsync(input);
            
            var macro = FindMacro(input);
            if (macro == null)
            {
                intent.Cancel();
                return null;
            }

            return await ExecuteMacroAsync(macro);
        }

        /// <summary>
        /// Execute a specific macro by ID
        /// Integrates with AgentIntentController for "alive" feedback
        /// </summary>
        public async Task<MacroResult?> ExecuteByIdAsync(string macroId)
        {
            var intent = AgentIntentController.Instance;
            
            // Brief understanding phase
            await intent.BeginUnderstandingAsync(macroId);
            
            var macro = _macros.FirstOrDefault(m => m.Id.Equals(macroId, StringComparison.OrdinalIgnoreCase));
            if (macro == null)
            {
                intent.Cancel();
                return null;
            }

            return await ExecuteMacroAsync(macro);
        }

        /// <summary>
        /// Execute a macro and log the activity
        /// Wires intent phases for "alive" feedback
        /// </summary>
        private async Task<MacroResult> ExecuteMacroAsync(AgentMacroDefinition macro)
        {
            var sw = Stopwatch.StartNew();
            var intent = AgentIntentController.Instance;
            var memory = AgentSessionMemory.Instance;
            MacroResult result;

            // Phase 2: Executing
            intent.BeginExecuting(macro.Title);

            try
            {
                // Safety check - only allow SafeReadOnly macros
                if (macro.Risk != MacroRiskLevel.SafeReadOnly)
                {
                    result = new MacroResult
                    {
                        Success = false,
                        MacroId = macro.Id,
                        MacroTitle = macro.Title,
                        ErrorMessage = "Blocked: requires LIVE mode + confirmation",
                        ExecutionTime = sw.Elapsed
                    };
                }
                else
                {
                    result = await macro.ExecuteAsync();
                    result.MacroId = macro.Id;
                    result.MacroTitle = macro.Title;
                    
                    // Add confidence statement
                    result.ConfidenceStatement = AgentConfidenceMessaging.GetConfidenceStatement(result);
                    
                    // Add suggestions for next actions
                    result.Suggestions = AgentConfidenceMessaging.GetSuggestions(macro.Id);
                }
            }
            catch (Exception ex)
            {
                result = new MacroResult
                {
                    Success = false,
                    MacroId = macro.Id,
                    MacroTitle = macro.Title,
                    ErrorMessage = $"Execution failed: {ex.Message}",
                    ExecutionTime = sw.Elapsed
                };
                Debug.WriteLine($"[MacroEngine] Error executing {macro.Id}: {ex.Message}");
            }

            sw.Stop();
            result.ExecutionTime = sw.Elapsed;

            // Record in session memory
            memory.RecordExecution(macro.Id, macro.Title, result.Success, result);

            // Log activity
            var activity = new MacroActivityEntry
            {
                Timestamp = DateTime.Now,
                MacroId = macro.Id,
                MacroTitle = macro.Title,
                Duration = sw.Elapsed,
                Success = result.Success
            };
            LogActivity(activity);

            MacroExecuted?.Invoke(this, result);
            
            // Phase 3: Completed (async, doesn't block return)
            _ = intent.CompleteAsync(result.Success);

            return result;
        }

        private void LogActivity(MacroActivityEntry entry)
        {
            _activityLog.Insert(0, entry);
            if (_activityLog.Count > MaxActivityEntries)
                _activityLog.RemoveAt(_activityLog.Count - 1);

            ActivityLogged?.Invoke(this, entry);
        }

        /// <summary>
        /// Get all available macro summaries for command palette
        /// </summary>
        public List<MacroSummary> GetMacroSummaries()
        {
            return _macros.Select(m => new MacroSummary
            {
                Id = m.Id,
                Title = m.Title,
                Description = m.Description,
                Icon = m.Icon,
                Keywords = m.Keywords
            }).ToList();
        }
    }

    public class MacroActivityEntry
    {
        public DateTime Timestamp { get; set; }
        public string MacroId { get; set; } = "";
        public string MacroTitle { get; set; } = "";
        public TimeSpan Duration { get; set; }
        public bool Success { get; set; }
    }

    public class MacroSummary
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public string Description { get; set; } = "";
        public string Icon { get; set; } = "";
        public string[] Keywords { get; set; } = Array.Empty<string>();
    }
}
