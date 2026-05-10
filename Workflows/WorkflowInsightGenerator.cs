using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using AtlasAI.Agent;

namespace AtlasAI.Workflows
{
    /// <summary>
    /// Generates human-readable insights from workflow step results.
    /// Analyzes macro output data to provide actionable recommendations.
    /// 
    /// SAFETY: Read-only analysis, no system modifications.
    /// </summary>
    public static class WorkflowInsightGenerator
    {
        /// <summary>
        /// Generate insight text for a specific step based on workflow context
        /// </summary>
        public static string GenerateStepInsight(WorkflowChainInstance workflow, WorkflowStep step)
        {
            if (string.IsNullOrEmpty(step.InsightTemplate))
                return "Analysis complete.";

            // Get all completed macro results
            var macroResults = workflow.Steps
                .Where(s => s.Type == WorkflowStepType.Macro && s.ResultData is MacroResult)
                .Select(s => (s.TargetId, Result: (MacroResult)s.ResultData!))
                .ToDictionary(x => x.TargetId ?? "", x => x.Result);

            return step.InsightTemplate switch
            {
                "{health_summary}" => GenerateHealthSummary(macroResults),
                "{slowness_diagnosis}" => GenerateSlownessDiagnosis(macroResults),
                "{network_diagnosis}" => GenerateNetworkDiagnosis(macroResults),
                "{disk_recommendations}" => GenerateDiskRecommendations(macroResults),
                "{security_assessment}" => GenerateSecurityAssessment(macroResults),
                "{gaming_readiness}" => GenerateGamingReadiness(macroResults),
                _ => "Analysis complete."
            };
        }

        /// <summary>
        /// Generate final summary insight for completed workflow
        /// </summary>
        public static string GenerateFinalInsight(WorkflowChainInstance workflow)
        {
            var completedSteps = workflow.Steps.Count(s => s.Status == WorkflowStepStatus.Complete);
            var failedSteps = workflow.Steps.Count(s => s.Status == WorkflowStepStatus.Failed);
            var skippedSteps = workflow.Steps.Count(s => s.Status == WorkflowStepStatus.Skipped);

            var sb = new StringBuilder();
            sb.Append($"Workflow complete: {completedSteps}/{workflow.Steps.Count} steps finished");

            if (failedSteps > 0)
                sb.Append($", {failedSteps} failed");
            if (skippedSteps > 0)
                sb.Append($", {skippedSteps} skipped");

            // Add duration
            if (workflow.CompletedAt.HasValue)
            {
                var duration = workflow.CompletedAt.Value - workflow.StartedAt;
                sb.Append($" in {duration.TotalSeconds:F1}s");
            }

            return sb.ToString();
        }

        #region Insight Generators

        private static string GenerateHealthSummary(Dictionary<string, MacroResult> results)
        {
            var issues = new List<string>();
            var positives = new List<string>();

            // Analyze system-overview
            if (results.TryGetValue("system-overview", out var sysResult) && sysResult.Success)
            {
                var cpuCard = sysResult.Cards.FirstOrDefault(c => c.Title.Contains("CPU") || c.Title.Contains("System"));
                if (cpuCard != null)
                {
                    var cpuRow = cpuCard.Rows.FirstOrDefault(r => r.Label.Contains("CPU") || r.Label.Contains("Usage"));
                    if (cpuRow != null && TryParsePercent(cpuRow.Value, out var cpuPercent))
                    {
                        if (cpuPercent > 80)
                            issues.Add("CPU usage is high");
                        else if (cpuPercent < 30)
                            positives.Add("CPU usage is normal");
                    }

                    var ramRow = cpuCard.Rows.FirstOrDefault(r => r.Label.Contains("RAM") || r.Label.Contains("Memory"));
                    if (ramRow != null && TryParsePercent(ramRow.Value, out var ramPercent))
                    {
                        if (ramPercent > 85)
                            issues.Add("Memory usage is high");
                        else if (ramPercent < 70)
                            positives.Add("Memory is healthy");
                    }
                }
            }

            // Analyze performance-diagnostics
            if (results.TryGetValue("performance-diagnostics", out var perfResult) && perfResult.Success)
            {
                if (perfResult.Summary?.Contains("bottleneck", StringComparison.OrdinalIgnoreCase) == true)
                    issues.Add("Performance bottleneck detected");
            }

            // Build insight
            if (issues.Count == 0)
                return "✅ Your system is healthy. All metrics are within normal ranges.";

            if (issues.Count == 1)
                return $"⚠️ Minor concern: {issues[0]}. Otherwise looking good.";

            return $"⚠️ Found {issues.Count} concerns: {string.Join(", ", issues)}. Consider investigating.";
        }

        private static string GenerateSlownessDiagnosis(Dictionary<string, MacroResult> results)
        {
            var causes = new List<string>();

            // Check system overview for resource usage
            if (results.TryGetValue("system-overview", out var sysResult) && sysResult.Success)
            {
                foreach (var card in sysResult.Cards)
                {
                    foreach (var row in card.Rows)
                    {
                        if (TryParsePercent(row.Value, out var percent))
                        {
                            if (row.Label.Contains("CPU") && percent > 70)
                                causes.Add($"High CPU usage ({percent}%)");
                            if (row.Label.Contains("RAM") && percent > 85)
                                causes.Add($"High memory usage ({percent}%)");
                            if (row.Label.Contains("Disk") && percent > 90)
                                causes.Add($"Disk nearly full ({percent}%)");
                        }
                    }
                }
            }

            // Check startup programs
            if (results.TryGetValue("startup-inventory", out var startupResult) && startupResult.Success)
            {
                var startupCard = startupResult.Cards.FirstOrDefault();
                if (startupCard != null && startupCard.Rows.Count > 10)
                    causes.Add($"Many startup programs ({startupCard.Rows.Count})");
            }

            // Check performance diagnostics
            if (results.TryGetValue("performance-diagnostics", out var perfResult) && perfResult.Success)
            {
                if (perfResult.Summary?.Contains("thermal", StringComparison.OrdinalIgnoreCase) == true)
                    causes.Add("Possible thermal throttling");
            }

            if (causes.Count == 0)
                return "✅ No obvious slowness causes found. System resources look normal.";

            return $"🐢 Possible causes: {string.Join("; ", causes)}. Consider addressing these.";
        }

        private static string GenerateNetworkDiagnosis(Dictionary<string, MacroResult> results)
        {
            if (!results.TryGetValue("network-snapshot", out var netResult) || !netResult.Success)
                return "❓ Could not analyze network status.";

            var issues = new List<string>();

            foreach (var card in netResult.Cards)
            {
                foreach (var row in card.Rows)
                {
                    var valueLower = row.Value.ToLowerInvariant();
                    if (valueLower.Contains("disconnected") || valueLower.Contains("no connection"))
                        issues.Add($"{row.Label} is disconnected");
                    if (valueLower.Contains("limited"))
                        issues.Add($"{row.Label} has limited connectivity");
                }
            }

            if (issues.Count == 0)
                return "✅ Network appears healthy. All connections are active.";

            return $"🌐 Network issues: {string.Join("; ", issues)}. Check your connection settings.";
        }

        private static string GenerateDiskRecommendations(Dictionary<string, MacroResult> results)
        {
            var recommendations = new List<string>();

            // Check disk health
            if (results.TryGetValue("disk-health", out var diskResult) && diskResult.Success)
            {
                foreach (var card in diskResult.Cards)
                {
                    foreach (var row in card.Rows)
                    {
                        if (TryParsePercent(row.Value, out var percent))
                        {
                            if (percent > 90)
                                recommendations.Add($"Drive {row.Label} is nearly full ({percent}%)");
                            else if (percent > 80)
                                recommendations.Add($"Drive {row.Label} is getting full ({percent}%)");
                        }
                    }
                }
            }

            // Check installed apps
            if (results.TryGetValue("installed-apps", out var appsResult) && appsResult.Success)
            {
                var appCount = appsResult.Cards.Sum(c => c.Rows.Count);
                if (appCount > 100)
                    recommendations.Add($"Many apps installed ({appCount}). Consider uninstalling unused ones.");
            }

            if (recommendations.Count == 0)
                return "✅ Disk space looks healthy. No immediate action needed.";

            return $"💾 Recommendations: {string.Join("; ", recommendations)}";
        }

        private static string GenerateSecurityAssessment(Dictionary<string, MacroResult> results)
        {
            var concerns = new List<string>();

            // Check security status
            if (results.TryGetValue("security-status", out var secResult) && secResult.Success)
            {
                foreach (var card in secResult.Cards)
                {
                    foreach (var row in card.Rows)
                    {
                        var valueLower = row.Value.ToLowerInvariant();
                        if (valueLower.Contains("disabled") || valueLower.Contains("off"))
                            concerns.Add($"{row.Label} is disabled");
                        if (valueLower.Contains("outdated") || valueLower.Contains("update"))
                            concerns.Add($"{row.Label} needs update");
                    }
                }
            }

            // Check event viewer for security events
            if (results.TryGetValue("event-viewer", out var eventResult) && eventResult.Success)
            {
                var securityCard = eventResult.Cards.FirstOrDefault(c => 
                    c.Title.Contains("Security", StringComparison.OrdinalIgnoreCase));
                if (securityCard != null && securityCard.Rows.Count > 5)
                    concerns.Add($"Multiple security events logged ({securityCard.Rows.Count})");
            }

            if (concerns.Count == 0)
                return "🛡️ Security looks good. Windows Defender is active and up to date.";

            return $"⚠️ Security concerns: {string.Join("; ", concerns)}. Review your security settings.";
        }

        private static string GenerateGamingReadiness(Dictionary<string, MacroResult> results)
        {
            var issues = new List<string>();
            var ready = new List<string>();

            // Check system resources
            if (results.TryGetValue("system-overview", out var sysResult) && sysResult.Success)
            {
                foreach (var card in sysResult.Cards)
                {
                    foreach (var row in card.Rows)
                    {
                        if (TryParsePercent(row.Value, out var percent))
                        {
                            if (row.Label.Contains("CPU") && percent > 50)
                                issues.Add($"CPU already at {percent}%");
                            else if (row.Label.Contains("CPU"))
                                ready.Add("CPU available");

                            if (row.Label.Contains("RAM") && percent > 70)
                                issues.Add($"Memory at {percent}%");
                            else if (row.Label.Contains("RAM"))
                                ready.Add("Memory available");
                        }
                    }
                }
            }

            // Check background processes
            if (results.TryGetValue("startup-inventory", out var startupResult) && startupResult.Success)
            {
                var processCount = startupResult.Cards.Sum(c => c.Rows.Count);
                if (processCount > 15)
                    issues.Add($"{processCount} background processes");
            }

            if (issues.Count == 0)
                return "🎮 Ready to game! System resources are available.";

            if (issues.Count == 1)
                return $"🎮 Mostly ready. Note: {issues[0]}. Should be fine for most games.";

            return $"🎮 Consider closing some apps first: {string.Join("; ", issues)}";
        }

        #endregion

        #region Helpers

        private static bool TryParsePercent(string value, out int percent)
        {
            percent = 0;
            if (string.IsNullOrEmpty(value))
                return false;

            // Extract number from strings like "45%", "45 %", "45 percent"
            var numStr = new string(value.TakeWhile(c => char.IsDigit(c) || c == '.').ToArray());
            if (int.TryParse(numStr, out percent))
                return true;

            // Try parsing as double and convert
            if (double.TryParse(numStr, out var dbl))
            {
                percent = (int)dbl;
                return true;
            }

            return false;
        }

        #endregion
    }
}
