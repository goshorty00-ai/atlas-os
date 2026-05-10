using System;
using System.Linq;
using AtlasAI.Conversation.Models;
using AtlasAI.SecuritySuite.Models;

namespace AtlasAI.SecuritySuite.Services
{
    /// <summary>
    /// STEP 30 + ALIVE MODE: Provides chat integration for security scan results.
    /// Handles "what are the threats?" queries with deterministic, data-backed responses.
    /// 
    /// TRUTHFULNESS RULES:
    /// - Never claim detections without actual scan data
    /// - If no scan exists, say so clearly
    /// - If scan is demo/not real, say "demo mode" or "no engine connected"
    /// - Store scan summary in ConversationWorkingMemory for context
    /// </summary>
    public static class SecurityChatIntegration
    {
        private static SecuritySuiteManager? _manager;
        
        /// <summary>
        /// Initialize with the security suite manager
        /// </summary>
        public static void Initialize(SecuritySuiteManager manager)
        {
            _manager = manager;
        }
        
        /// <summary>
        /// Check if user input is asking about security/threats
        /// </summary>
        public static bool IsSecurityQuery(string userInput)
        {
            var lower = userInput.ToLowerInvariant();
            var securityPhrases = new[]
            {
                "what are the threats", "show me the threats", "list detections",
                "what did you find", "explain threats", "security results",
                "scan results", "what was detected", "show detections",
                "any threats", "any malware", "any viruses", "what threats",
                "show threats", "list threats", "security scan results",
                "what did the scan find", "scan findings", "threat report"
            };
            
            return securityPhrases.Any(p => lower.Contains(p));
        }
        
        /// <summary>
        /// Get the last scan result (from ScanEngine or loaded from disk)
        /// Also updates ConversationWorkingMemory with scan summary
        /// </summary>
        public static SecurityScanResult? GetLastScanResult()
        {
            // Try to get from manager's scan engine first
            SecurityScanResult? result = null;
            if (_manager?.ScanEngine?.LastResult != null)
                result = _manager.ScanEngine.LastResult;
            else
                result = SecurityScanResult.LoadLastResult();
            
            // Update working memory with scan summary
            if (result != null)
            {
                var detectionNames = result.Detections
                    .Take(10)
                    .Select(d => $"{d.Severity}: {d.Name}")
                    .ToList();
                
                ConversationWorkingMemory.Instance.StoreScanSummary(
                    filesScanned: (int)result.FilesScanned,
                    threatCount: result.Detections.Count,
                    detections: detectionNames,
                    isPlaceholder: !result.IsRealScan,
                    placeholderReason: result.IsRealScan ? null : "demo mode - no real scan performed"
                );
            }
            
            return result;
        }
        
        /// <summary>
        /// Check if we have a recent, real scan result available
        /// </summary>
        public static bool HasRecentScanResult()
        {
            var result = GetLastScanResult();
            return result != null && result.IsRecent && result.IsRealScan;
        }
        
        /// <summary>
        /// STEP 30: Generate a deterministic, TRUTHFUL response for security queries.
        /// This is what the AI should use when user asks about threats.
        /// </summary>
        public static string GenerateSecurityResponse(string userInput, string? honorific = null)
        {
            var result = GetLastScanResult();
            var title = string.IsNullOrEmpty(honorific) ? "" : $", {honorific}";
            
            // No scan result exists at all
            if (result == null)
            {
                return $"I don't have any scan results yet{title}. " +
                       "Would you like me to run a security scan now? " +
                       "You can say 'quick scan' for a fast check or 'full scan' for a thorough analysis.";
            }
            
            // STEP 30: Check if this is a real scan
            if (!result.IsRealScan)
            {
                return $"⚠️ The available scan data is from demo mode{title}. " +
                       "No real scan has been performed yet. " +
                       "Would you like me to run an actual security scan?";
            }
            
            // Scan result exists but is old (>30 minutes)
            if (!result.IsRecent)
            {
                var age = DateTime.UtcNow - result.EndedUtc;
                var ageText = age.TotalHours >= 1 
                    ? $"{(int)age.TotalHours} hour(s) ago"
                    : $"{(int)age.TotalMinutes} minutes ago";
                
                var detectionSummary = result.Detections.Count == 0
                    ? "It found no threats."
                    : $"It found {result.Detections.Count} detection(s).";
                
                return $"The last scan was {ageText}{title}. {detectionSummary} " +
                       "Would you like me to run a fresh scan or show you those older results?";
            }
            
            // Recent, real scan result exists - provide detailed response
            return result.GenerateChatExplanation(verbose: false);
        }
        
        /// <summary>
        /// Generate verbose threat explanation (for "show full list" requests)
        /// </summary>
        public static string GenerateDetailedThreatList()
        {
            var result = GetLastScanResult();
            if (result == null)
                return "No scan results available. Please run a security scan first.";
            
            if (!result.IsRealScan)
                return "⚠️ No real scan data available. The current data is from demo mode. Please run an actual security scan.";
            
            if (result.Detections.Count == 0)
                return "✅ The last scan found no threats. Your system appears clean.";
            
            var lines = new System.Collections.Generic.List<string>
            {
                $"**Full Detection List** ({result.Detections.Count} items from {result.ScanType} scan)",
                $"Scanned {result.FilesScanned:N0} items in {result.DurationFormatted}",
                ""
            };
            
            // Group by severity
            var bySeverity = result.Detections
                .GroupBy(d => d.Severity)
                .OrderByDescending(g => (int)g.Key);
            
            foreach (var group in bySeverity)
            {
                var severityIcon = group.Key switch
                {
                    ThreatSeverity.Critical => "🔴",
                    ThreatSeverity.High => "🟠",
                    ThreatSeverity.Medium => "🟡",
                    ThreatSeverity.Low => "🔵",
                    _ => "⚪"
                };
                
                lines.Add($"**{severityIcon} {group.Key}** ({group.Count()})");
                
                foreach (var detection in group.Take(10)) // Limit per severity
                {
                    lines.Add($"  • **{detection.Name}**");
                    if (!string.IsNullOrEmpty(detection.Reason))
                        lines.Add($"    {detection.Reason}");
                    if (!string.IsNullOrEmpty(detection.FilePath))
                    {
                        var shortPath = detection.FilePath.Length > 60
                            ? "..." + detection.FilePath.Substring(detection.FilePath.Length - 57)
                            : detection.FilePath;
                        lines.Add($"    Path: `{shortPath}`");
                    }
                }
                
                if (group.Count() > 10)
                    lines.Add($"  ... and {group.Count() - 10} more");
                
                lines.Add("");
            }
            
            return string.Join("\n", lines);
        }
        
        /// <summary>
        /// Generate remediation guidance (for "fix it" requests)
        /// </summary>
        public static (string Message, bool RequiresConfirmation) GenerateRemediationGuidance()
        {
            var result = GetLastScanResult();
            if (result == null)
            {
                return ("No scan results available. Please run a security scan first.", false);
            }
            
            if (!result.IsRealScan)
            {
                return ("⚠️ Cannot remediate demo data. Please run a real security scan first.", false);
            }
            
            if (result.Detections.Count == 0)
            {
                return ("✅ No threats to remediate. Your system is clean.", false);
            }
            
            var summary = result.Summary;
            var actionableCount = result.Detections.Count(d => d.CanQuarantine || d.CanDelete);
            
            if (actionableCount == 0)
            {
                return ("The detected items are advisory only and don't require action. " +
                        "They're informational findings you may want to review.", false);
            }
            
            // Build remediation plan
            var lines = new System.Collections.Generic.List<string>
            {
                $"I can help remediate {actionableCount} item(s). Here's what I would do:",
                ""
            };
            
            if (summary.CriticalCount > 0 || summary.HighCount > 0)
            {
                lines.Add($"🔴 **Quarantine** {summary.CriticalCount + summary.HighCount} high-risk item(s)");
                lines.Add("   (Moves files to a safe location, can be restored if needed)");
            }
            
            if (summary.MediumCount > 0)
            {
                lines.Add($"🟡 **Review** {summary.MediumCount} medium-risk item(s)");
                lines.Add("   (I'll show you each one for your decision)");
            }
            
            if (summary.LowCount > 0)
            {
                lines.Add($"🔵 **Skip** {summary.LowCount} low-risk item(s)");
                lines.Add("   (These are informational, no action needed)");
            }
            
            lines.Add("");
            lines.Add("**Do you want me to proceed with quarantining the high-risk items?**");
            lines.Add("Say 'yes' to continue or 'show me first' to review each item.");
            
            return (string.Join("\n", lines), true);
        }
        
        /// <summary>
        /// Get context for LLM prompt injection (so AI knows about scan results)
        /// /// </summary>
        public static string GetSecurityConterPrompt()
        {
            var result = GetLastScanResult();
            if (result == null)
                return "[Security: No scan results available]";
            
            if (!result.IsRealScan)
                return "[Security: Demo mode - no real scan data]";
            
            var summary = result.Summary;
            var ageMinutes = (int)(DateTime.UtcNow - result.EndedUtc).TotalMinutes;
            
            return $"[Security: Last scan {ageMinutes}min ago, " +
                   $"{summary.TotalDetections} detections " +
                   $"(Critical:{summary.CriticalCount}, High:{summary.HighCount}, " +
                   $"Medium:{summary.MediumCount}, Low:{summary.LowCount}), " +
                   $"IsRealScan:{result.IsRealScan}]";
        }
    }
}
