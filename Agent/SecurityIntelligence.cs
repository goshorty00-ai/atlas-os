using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AtlasAI.ActionHistory;

namespace AtlasAI.Agent
{
    /// <summary>
    /// AI Security Intelligence Layer - Routes security detections through AI for
    /// classification, explanation, and user decision recording.
    /// This enhances existing ProactiveSecurityMonitor with AI authority.
    /// </summary>
    public class SecurityIntelligence
    {
        private static SecurityIntelligence? _instance;
        public static SecurityIntelligence Instance => _instance ??= new SecurityIntelligence();
        
        // Events for UI notification
        public event Action<SecurityInsight>? OnSecurityInsight;
        public event Action<string>? OnStatusUpdate;
        
        // Track past decisions for learning
        private readonly Dictionary<string, UserDecision> _pastDecisions = new();
        private readonly List<SecurityInsight> _recentInsights = new();
        private const int MaxInsightHistory = 50;
        
        private SecurityIntelligence()
        {
            // Wire up to existing ProactiveSecurityMonitor
            WireUpSecurityMonitor();
        }
        
        /// <summary>
        /// Connect to existing security monitoring infrastructure
        /// </summary>
        private void WireUpSecurityMonitor()
        {
            try
            {
                // Subscribe to installation alerts from ProactiveSecurityMonitor
                ProactiveSecurityMonitor.Instance.InstallationDetected += OnInstallationDetected;
                ProactiveSecurityMonitor.Instance.HealthScanCompleted += OnHealthScanCompleted;
                ProactiveSecurityMonitor.Instance.StatusChanged += (s, msg) => OnStatusUpdate?.Invoke(msg);
                
                Debug.WriteLine("[SecurityIntelligence] Wired up to ProactiveSecurityMonitor");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SecurityIntelligence] Wire-up error: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Handle installation detection from ProactiveSecurityMonitor
        /// </summary>
        private async void OnInstallationDetected(object? sender, InstallationAlert alert)
        {
            Debug.WriteLine($"[SecurityIntelligence] Processing installation: {alert.FileName}");
            
            var insight = await AnalyzeInstallationAsync(alert);
            
            // Record in history
            RecordInsight(insight);
            
            // Notify UI
            OnSecurityInsight?.Invoke(insight);
            
            // Record in ProactiveAssistant for pattern learning
            ProactiveAssistant.Instance.RecordAction(
                "security_detection",
                $"{insight.Category}:{alert.FileName}",
                true
            );
        }
        
        /// <summary>
        /// Handle health scan completion
        /// </summary>
        private void OnHealthScanCompleted(object? sender, HealthReport report)
        {
            Debug.WriteLine($"[SecurityIntelligence] Health scan: {report.OverallStatus}");
            
            // Create insights for any issues found
            if (report.CriticalIssues > 0 || report.Warnings > 0)
            {
                var insight = AnalyzeHealthReport(report);
                RecordInsight(insight);
                OnSecurityInsight?.Invoke(insight);
            }
        }
        
        /// <summary>
        /// Analyze an installation alert and generate AI insight
        /// </summary>
        public async Task<SecurityInsight> AnalyzeInstallationAsync(InstallationAlert alert)
        {
            var insight = new SecurityInsight
            {
                Id = Guid.NewGuid().ToString(),
                Timestamp = DateTime.Now,
                Category = DetermineCategory(alert),
                SourceFile = alert.FileName,
                SourcePath = alert.FilePath
            };
            
            // Classify severity
            insight.Severity = ClassifySeverity(alert);
            
            // Generate plain language explanation
            insight.Explanation = GenerateExplanation(alert, insight.Severity);
            
            // Generate recommended actions
            insight.RecommendedActions = GenerateRecommendations(alert, insight.Severity);
            
            // Check if we've seen similar before
            insight.PreviousDecision = GetPreviousDecision(alert.FileName, alert.Publisher);
            
            // Generate the question to ask user
            insight.UserQuestion = GenerateUserQuestion(alert, insight);
            
            return insight;
        }
        
        /// <summary>
        /// Analyze a health report and generate insight
        /// </summary>
        private SecurityInsight AnalyzeHealthReport(HealthReport report)
        {
            var insight = new SecurityInsight
            {
                Id = Guid.NewGuid().ToString(),
                Timestamp = DateTime.Now,
                Category = SecurityCategory.SystemHealth,
                SourceFile = "System Health Scan"
            };
            
            // Classify based on issues
            if (report.CriticalIssues > 0)
                insight.Severity = SecuritySeverity.High;
            else if (report.Warnings > 0)
                insight.Severity = SecuritySeverity.Medium;
            else
                insight.Severity = SecuritySeverity.Low;
            
            // Build explanation
            var issues = new List<string>();
            
            foreach (var disk in report.DiskUsage.Where(d => d.Status != "OK"))
            {
                issues.Add($"Drive {disk.DriveName} is {(disk.Status == "Critical" ? "critically" : "getting")} low on space ({disk.UsedPercent:F0}% used)");
            }
            
            if (report.MemoryUsedPercent > 80)
            {
                issues.Add($"Memory usage is high ({report.MemoryUsedPercent:F0}% used)");
            }
            
            if (report.HighMemoryProcesses.Count > 0)
            {
                issues.Add($"{report.HighMemoryProcesses.Count} processes using excessive memory: {string.Join(", ", report.HighMemoryProcesses.Take(3))}");
            }
            
            insight.Explanation = issues.Any()
                ? "I noticed some system health concerns:\n• " + string.Join("\n• ", issues)
                : "Your system is running smoothly.";
            
            insight.RecommendedActions = new List<string>();
            if (report.DiskUsage.Any(d => d.Status == "Critical"))
                insight.RecommendedActions.Add("Free up disk space by removing unused files");
            if (report.MemoryUsedPercent > 85)
                insight.RecommendedActions.Add("Close some applications to free memory");
            if (report.HighMemoryProcesses.Count > 3)
                insight.RecommendedActions.Add("Review high-memory processes");
            
            insight.UserQuestion = insight.Severity == SecuritySeverity.High
                ? "Would you like me to help address these issues?"
                : "Would you like more details about your system health?";
            
            return insight;
        }
        
        #region Helper Methods
        
        /// <summary>
        /// Determine the security category based on the alert
        /// </summary>
        private SecurityCategory DetermineCategory(InstallationAlert alert)
        {
            if (alert.IsBloatware)
                return SecurityCategory.Bloatware;
            if (alert.VirusTotalMalicious > 0)
                return SecurityCategory.Malware;
            if (!alert.IsSigned)
                return SecurityCategory.UnsignedSoftware;
            if (alert.Type == InstallationType.NewProgram)
                return SecurityCategory.NewInstallation;
            return SecurityCategory.NewDownload;
        }
        
        /// <summary>
        /// Classify severity based on alert details
        /// </summary>
        private SecuritySeverity ClassifySeverity(InstallationAlert alert)
        {
            // High severity conditions
            if (alert.VirusTotalMalicious > 5)
                return SecuritySeverity.High;
            if (alert.IsBloatware)
                return SecuritySeverity.High;
            if (alert.RiskLevel == SecurityRiskLevel.High)
                return SecuritySeverity.High;
            
            // Medium severity conditions
            if (alert.VirusTotalMalicious > 0)
                return SecuritySeverity.Medium;
            if (!alert.IsSigned)
                return SecuritySeverity.Medium;
            if (alert.RiskLevel == SecurityRiskLevel.Medium)
                return SecuritySeverity.Medium;
            
            // Low severity - trusted or signed
            return SecuritySeverity.Low;
        }
        
        /// <summary>
        /// Generate plain language explanation for the user
        /// </summary>
        private string GenerateExplanation(InstallationAlert alert, SecuritySeverity severity)
        {
            var parts = new List<string>();
            
            // Opening based on severity
            parts.Add(severity switch
            {
                SecuritySeverity.High => $"⚠️ I detected something concerning: {alert.FileName}",
                SecuritySeverity.Medium => $"⚡ Heads up - I noticed: {alert.FileName}",
                _ => $"📥 New file detected: {alert.FileName}"
            });
            
            // Publisher info
            if (!string.IsNullOrEmpty(alert.Publisher) && alert.Publisher != "Unknown")
            {
                if (alert.IsTrusted)
                    parts.Add($"✅ From trusted publisher: {CleanPublisher(alert.Publisher)}");
                else
                    parts.Add($"📝 Publisher: {CleanPublisher(alert.Publisher)}");
            }
            else if (!alert.IsSigned)
            {
                parts.Add("⚠️ This file is not digitally signed - I can't verify who made it.");
            }
            
            // Bloatware warning
            if (alert.IsBloatware)
            {
                parts.Add("🚫 This looks like bloatware/PUP (potentially unwanted program). These often slow down your PC or show ads.");
            }
            
            // VirusTotal results
            if (alert.OnlineVerified)
            {
                if (alert.VirusTotalMalicious > 0)
                    parts.Add($"🔴 VirusTotal flagged this: {alert.VirusTotalMalicious} security vendors detected issues.");
                else if (alert.VirusTotalClean > 0)
                    parts.Add($"🟢 VirusTotal scan clean: {alert.VirusTotalClean} vendors found no issues.");
            }
            
            // File size context
            if (alert.FileSize > 0)
            {
                var sizeMB = alert.FileSize / (1024.0 * 1024.0);
                if (sizeMB > 500)
                    parts.Add($"📦 Large file: {sizeMB:F0} MB");
            }
            
            return string.Join("\n", parts);
        }
        
        /// <summary>
        /// Clean up publisher name from certificate
        /// </summary>
        private string CleanPublisher(string publisher)
        {
            // Extract CN= value if present
            if (publisher.Contains("CN="))
            {
                var start = publisher.IndexOf("CN=") + 3;
                var end = publisher.IndexOf(',', start);
                if (end == -1) end = publisher.Length;
                return publisher.Substring(start, end - start).Trim();
            }
            return publisher;
        }
        
        /// <summary>
        /// Generate recommended actions based on the alert
        /// </summary>
        private List<string> GenerateRecommendations(InstallationAlert alert, SecuritySeverity severity)
        {
            var actions = new List<string>();
            
            if (severity == SecuritySeverity.High)
            {
                actions.Add("Delete this file");
                actions.Add("Quarantine for later review");
                if (alert.IsBloatware)
                    actions.Add("Block similar downloads in future");
            }
            else if (severity == SecuritySeverity.Medium)
            {
                actions.Add("Scan with Windows Defender before running");
                actions.Add("Research the publisher online");
                actions.Add("Proceed with caution");
            }
            else
            {
                actions.Add("Safe to run");
                actions.Add("Add to trusted list");
            }
            
            return actions;
        }
        
        /// <summary>
        /// Check if we've made a decision about similar files before
        /// </summary>
        private UserDecision? GetPreviousDecision(string fileName, string publisher)
        {
            // Check by publisher first (more reliable)
            var publisherKey = $"publisher:{publisher.ToLower()}";
            if (_pastDecisions.TryGetValue(publisherKey, out var pubDecision))
                return pubDecision;
            
            // Check by filename pattern
            var nameKey = $"file:{Path.GetFileNameWithoutExtension(fileName).ToLower()}";
            if (_pastDecisions.TryGetValue(nameKey, out var fileDecision))
                return fileDecision;
            
            return null;
        }
        
        /// <summary>
        /// Generate the question to ask the user
        /// </summary>
        private string GenerateUserQuestion(InstallationAlert alert, SecurityInsight insight)
        {
            // If we have a previous decision, reference it
            if (insight.PreviousDecision != null)
            {
                var prev = insight.PreviousDecision;
                return $"Last time you {prev.Action.ToLower()} a similar file from {CleanPublisher(alert.Publisher)}. Want me to do the same?";
            }
            
            // Generate question based on severity
            return insight.Severity switch
            {
                SecuritySeverity.High => "This looks risky. Should I delete it, quarantine it, or let you decide?",
                SecuritySeverity.Medium => "Want me to scan this before you run it, or is it something you expected?",
                _ => "This looks safe. Want me to remember this publisher as trusted?"
            };
        }
        
        /// <summary>
        /// Record an insight in history
        /// </summary>
        private void RecordInsight(SecurityInsight insight)
        {
            _recentInsights.Insert(0, insight);
            while (_recentInsights.Count > MaxInsightHistory)
                _recentInsights.RemoveAt(_recentInsights.Count - 1);
        }
        
        #endregion
        
        #region User Decision Recording
        
        /// <summary>
        /// Record a user's decision about a security insight
        /// </summary>
        public void RecordUserDecision(SecurityInsight insight, string action, string? reason = null)
        {
            var decision = new UserDecision
            {
                InsightId = insight.Id,
                Action = action,
                Reason = reason,
                Timestamp = DateTime.Now,
                FileName = insight.SourceFile,
                Publisher = insight.Publisher,
                Severity = insight.Severity
            };
            
            // Store for future reference
            if (!string.IsNullOrEmpty(insight.Publisher) && insight.Publisher != "Unknown")
            {
                var publisherKey = $"publisher:{insight.Publisher.ToLower()}";
                _pastDecisions[publisherKey] = decision;
            }
            
            var nameKey = $"file:{Path.GetFileNameWithoutExtension(insight.SourceFile).ToLower()}";
            _pastDecisions[nameKey] = decision;
            
            // Record in ActionHistoryManager
            var actionRecord = new ActionRecord
            {
                Type = ActionType.ScanPerformed,
                Description = $"Security decision: {action} for {insight.SourceFile}",
                TargetPath = insight.SourcePath,
                CanUndo = action == "Quarantine" || action == "Delete",
                UserCommand = $"User chose to {action}: {reason ?? "No reason given"}"
            };
            
            ActionHistoryManager.Instance.RecordAction(actionRecord);
            
            // Record in ProactiveAssistant for pattern learning
            ProactiveAssistant.Instance.RecordAction(
                "security_decision",
                $"{insight.Category}:{action}:{insight.SourceFile}",
                true
            );
            
            Debug.WriteLine($"[SecurityIntelligence] Recorded decision: {action} for {insight.SourceFile}");
        }
        
        /// <summary>
        /// Get recent security insights
        /// </summary>
        public List<SecurityInsight> GetRecentInsights(int count = 10)
        {
            return _recentInsights.Take(count).ToList();
        }
        
        /// <summary>
        /// Check if a file/publisher is trusted based on past decisions
        /// </summary>
        public bool IsTrusted(string fileName, string publisher)
        {
            var decision = GetPreviousDecision(fileName, publisher);
            return decision?.Action == "Trust" || decision?.Action == "Allow";
        }
        
        #endregion
    }
    
    #region Models
    
    /// <summary>
    /// Security insight generated by AI analysis
    /// </summary>
    public class SecurityInsight
    {
        public string Id { get; set; } = "";
        public DateTime Timestamp { get; set; }
        public SecurityCategory Category { get; set; }
        public SecuritySeverity Severity { get; set; }
        public string SourceFile { get; set; } = "";
        public string? SourcePath { get; set; }
        public string? Publisher { get; set; }
        public string Explanation { get; set; } = "";
        public List<string> RecommendedActions { get; set; } = new();
        public string UserQuestion { get; set; } = "";
        public UserDecision? PreviousDecision { get; set; }
    }
    
    /// <summary>
    /// User's decision about a security insight
    /// </summary>
    public class UserDecision
    {
        public string InsightId { get; set; } = "";
        public string Action { get; set; } = ""; // Trust, Allow, Block, Delete, Quarantine, Ignore
        public string? Reason { get; set; }
        public DateTime Timestamp { get; set; }
        public string? FileName { get; set; }
        public string? Publisher { get; set; }
        public SecuritySeverity Severity { get; set; }
    }
    
    /// <summary>
    /// Categories of security detections
    /// </summary>
    public enum SecurityCategory
    {
        NewDownload,
        NewInstallation,
        UnsignedSoftware,
        Bloatware,
        Malware,
        StartupChange,
        BackgroundProcess,
        SystemHealth
    }
    
    /// <summary>
    /// Severity levels for security insights
    /// </summary>
    public enum SecuritySeverity
    {
        Low,
        Medium,
        High
    }
    
    #endregion
}
