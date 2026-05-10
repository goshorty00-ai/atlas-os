using System;
using System.Collections.Generic;
using System.Linq;

namespace AtlasAI.Agent
{
    /// <summary>
    /// Session-only memory for Agent Mode.
    /// Tracks last 10 macros run to improve phrasing and avoid repetitive suggestions.
    /// Not persisted to disk.
    /// </summary>
    public class AgentSessionMemory
    {
        private static AgentSessionMemory? _instance;
        public static AgentSessionMemory Instance => _instance ??= new AgentSessionMemory();

        private readonly List<SessionMacroEntry> _history = new();
        private const int MaxEntries = 10;

        public IReadOnlyList<SessionMacroEntry> History => _history.AsReadOnly();

        private AgentSessionMemory() { }

        /// <summary>
        /// Record a macro execution
        /// </summary>
        public void RecordExecution(string macroId, string macroTitle, bool success, MacroResult? result = null)
        {
            var entry = new SessionMacroEntry
            {
                MacroId = macroId,
                MacroTitle = macroTitle,
                Timestamp = DateTime.Now,
                Success = success,
                HasIssues = result != null && DetectIssues(result)
            };

            _history.Insert(0, entry);

            // Keep only last N entries
            while (_history.Count > MaxEntries)
                _history.RemoveAt(_history.Count - 1);
        }

        /// <summary>
        /// Get the last executed macro ID (or null if none)
        /// </summary>
        public string? GetLastMacroId()
        {
            return _history.FirstOrDefault()?.MacroId;
        }

        /// <summary>
        /// Check if a macro was run recently (within last N executions)
        /// </summary>
        public bool WasRecentlyRun(string macroId, int withinLast = 3)
        {
            return _history.Take(withinLast).Any(e => e.MacroId == macroId);
        }

        /// <summary>
        /// Check if the same macro was just run (last execution)
        /// </summary>
        public bool WasJustRun(string macroId)
        {
            return _history.FirstOrDefault()?.MacroId == macroId;
        }

        /// <summary>
        /// Get count of times a macro was run this session
        /// </summary>
        public int GetRunCount(string macroId)
        {
            return _history.Count(e => e.MacroId == macroId);
        }

        /// <summary>
        /// Check if the last result had issues worth noting
        /// </summary>
        public bool LastResultHadIssues()
        {
            return _history.FirstOrDefault()?.HasIssues ?? false;
        }

        /// <summary>
        /// Clear session memory
        /// </summary>
        public void Clear()
        {
            _history.Clear();
        }

        /// <summary>
        /// Detect if a result contains issues worth highlighting
        /// </summary>
        private bool DetectIssues(MacroResult result)
        {
            if (!result.Success) return true;

            // Check for warning indicators in cards
            foreach (var card in result.Cards)
            {
                foreach (var row in card.Rows)
                {
                    if (row.ValueColor == "red" || row.ValueColor == "yellow")
                        return true;
                }
            }

            return false;
        }
    }

    public class SessionMacroEntry
    {
        public string MacroId { get; set; } = "";
        public string MacroTitle { get; set; } = "";
        public DateTime Timestamp { get; set; }
        public bool Success { get; set; }
        public bool HasIssues { get; set; }
    }
}
