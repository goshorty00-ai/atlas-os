using System;
using System.Collections.Generic;
using System.Linq;
using AtlasAI.Core;

namespace AtlasAI.Agent
{
    /// <summary>
    /// Generates confidence statements and next-action suggestions for Agent Mode.
    /// Deterministic, no speculation, no hedging.
    /// Integrates with PersonaProfile for tone/style variations.
    /// </summary>
    public static class AgentConfidenceMessaging
    {
        // Macro ID → suggested next macros (read-only only)
        private static readonly Dictionary<string, string[]> SuggestionMap = new()
        {
            ["system-overview"] = new[] { "performance-diagnostics", "disk-health", "security-status" },
            ["startup-inventory"] = new[] { "installed-apps", "security-status", "performance-diagnostics" },
            ["network-snapshot"] = new[] { "security-status", "system-overview" },
            ["disk-health"] = new[] { "installed-apps", "system-overview", "performance-diagnostics" },
            ["installed-apps"] = new[] { "startup-inventory", "disk-health", "system-overview" },
            ["security-status"] = new[] { "event-viewer", "startup-inventory", "network-snapshot" },
            ["event-viewer"] = new[] { "performance-diagnostics", "security-status", "system-overview" },
            ["performance-diagnostics"] = new[] { "system-overview", "disk-health", "event-viewer" }
        };

        /// <summary>
        /// Generate a confidence statement based on the macro result.
        /// One sentence max, no speculation, no hedging.
        /// Uses persona from preferences for tone variation.
        /// </summary>
        public static string GetConfidenceStatement(MacroResult result)
        {
            // Check if confidence statements are enabled
            var prefs = PreferencesStore.Instance.Current;
            if (!prefs.ShowConfidenceStatement)
                return "";

            if (!result.Success)
                return PersonaProfile.GetConfidenceStatement(prefs.Persona, ConfidenceContext.Failure);

            var memory = AgentSessionMemory.Instance;
            var wasJustRun = memory.WasJustRun(result.MacroId);
            var hasIssues = DetectIssuesInResult(result);
            var runCount = memory.GetRunCount(result.MacroId);

            // Context-aware phrasing
            if (wasJustRun && runCount > 1)
            {
                // Same macro run again
                if (hasIssues)
                    return PersonaProfile.GetConfidenceStatement(prefs.Persona, ConfidenceContext.RepeatedNoChange);
                return PersonaProfile.GetConfidenceStatement(prefs.Persona, ConfidenceContext.RepeatedStillGood);
            }

            if (hasIssues)
            {
                return GetIssueStatement(result, prefs.Persona);
            }

            // Normal success statements by macro type (persona-aware)
            var context = result.MacroId switch
            {
                "system-overview" => ConfidenceContext.SystemOverview,
                "startup-inventory" => ConfidenceContext.StartupInventory,
                "network-snapshot" => ConfidenceContext.NetworkSnapshot,
                "disk-health" => ConfidenceContext.DiskHealth,
                "installed-apps" => ConfidenceContext.InstalledApps,
                "security-status" => ConfidenceContext.SecurityStatus,
                "event-viewer" => ConfidenceContext.EventViewer,
                "performance-diagnostics" => ConfidenceContext.PerformanceDiagnostics,
                _ => ConfidenceContext.Success
            };

            return PersonaProfile.GetConfidenceStatement(prefs.Persona, context);
        }

        /// <summary>
        /// Get suggested next actions (read-only macros only).
        /// Returns up to 2 suggestions, avoiding recently run macros.
        /// </summary>
        public static List<MacroSuggestion> GetSuggestions(string completedMacroId)
        {
            var suggestions = new List<MacroSuggestion>();
            var memory = AgentSessionMemory.Instance;
            var engine = AgentMacroEngine.Instance;

            if (!SuggestionMap.TryGetValue(completedMacroId, out var candidates))
                return suggestions;

            foreach (var candidateId in candidates)
            {
                // Skip if just run
                if (memory.WasJustRun(candidateId))
                    continue;

                // Find the macro definition
                var macro = engine.Macros.FirstOrDefault(m => m.Id == candidateId);
                if (macro == null || macro.Risk != MacroRiskLevel.SafeReadOnly)
                    continue;

                suggestions.Add(new MacroSuggestion
                {
                    MacroId = macro.Id,
                    Title = macro.Title,
                    Icon = macro.Icon,
                    Reason = GetSuggestionReason(completedMacroId, candidateId)
                });

                if (suggestions.Count >= 2)
                    break;
            }

            return suggestions;
        }

        private static bool DetectIssuesInResult(MacroResult result)
        {
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

        private static string GetIssueStatement(MacroResult result, PersonaType persona)
        {
            // Count severity
            int redCount = 0, yellowCount = 0;
            foreach (var card in result.Cards)
            {
                foreach (var row in card.Rows)
                {
                    if (row.ValueColor == "red") redCount++;
                    else if (row.ValueColor == "yellow") yellowCount++;
                }
            }

            if (redCount > 0)
                return PersonaProfile.GetConfidenceStatement(persona, ConfidenceContext.SuccessWithIssues);
            if (yellowCount > 0)
                return PersonaProfile.GetConfidenceStatement(persona, ConfidenceContext.SuccessMinorIssues);
            
            return PersonaProfile.GetConfidenceStatement(persona, ConfidenceContext.Success);
        }

        private static string GetSuggestionReason(string fromMacro, string toMacro)
        {
            // Contextual reasons for suggestions
            return (fromMacro, toMacro) switch
            {
                ("system-overview", "performance-diagnostics") => "Dig deeper into performance",
                ("system-overview", "disk-health") => "Check storage status",
                ("system-overview", "security-status") => "Verify security settings",
                ("disk-health", "installed-apps") => "See what's using space",
                ("disk-health", "system-overview") => "Full system check",
                ("security-status", "event-viewer") => "Review security events",
                ("security-status", "startup-inventory") => "Check startup programs",
                ("event-viewer", "performance-diagnostics") => "Diagnose issues",
                ("event-viewer", "security-status") => "Check security status",
                ("installed-apps", "startup-inventory") => "See what auto-starts",
                ("installed-apps", "disk-health") => "Check available space",
                ("startup-inventory", "installed-apps") => "Full app inventory",
                ("startup-inventory", "security-status") => "Verify security",
                ("network-snapshot", "security-status") => "Check firewall status",
                ("performance-diagnostics", "system-overview") => "See current status",
                ("performance-diagnostics", "disk-health") => "Check disk performance",
                _ => "Related check"
            };
        }
    }

    public class MacroSuggestion
    {
        public string MacroId { get; set; } = "";
        public string Title { get; set; } = "";
        public string Icon { get; set; } = "";
        public string Reason { get; set; } = "";
    }
}
