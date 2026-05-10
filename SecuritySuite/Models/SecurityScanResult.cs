using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AtlasAI.SecuritySuite.Models
{
    /// <summary>
    /// STEP 30: Structured scan result for chat integration.
    /// This is what the AI references when user asks "what are the threats?"
    /// 
    /// TRUTHFULNESS RULES:
    /// - IsRealScan must be true for actual scan results
    /// - If IsRealScan is false, chat must say "demo mode" or "no engine connected"
    /// - Never claim detections without actual Detections list
    /// </summary>
    public class SecurityScanResult
    {
        public string ScanId { get; set; } = Guid.NewGuid().ToString();
        public DateTime StartedUtc { get; set; }
        public DateTime EndedUtc { get; set; }
        public TimeSpan Duration => EndedUtc - StartedUtc;
        public long FilesScanned { get; set; }
        public ScanType ScanType { get; set; }
        public bool WasCancelled { get; set; }
        public string? ErrorMessage { get; set; }

        /// <summary>
        /// STEP 30: Indicates if this is a real scan result vs demo/placeholder.
        /// Chat responses MUST check this flag and be truthful.
        /// </summary>
        public bool IsRealScan { get; set; } = true;

        /// <summary>
        /// All detections from the scan
        /// </summary>
        public List<ScanDetection> Detections { get; set; } = new();

        /// <summary>
        /// Computed summary for quick reference
        /// </summary>
        [JsonIgnore]
        public ScanResultSummary Summary => ComputeSummary();

        /// <summary>
        /// Check if this result is recent (within 30 minutes)
        /// </summary>
        [JsonIgnore]
        public bool IsRecent => (DateTime.UtcNow - EndedUtc).TotalMinutes <= 30;

        /// <summary>
        /// Human-readable duration
        /// </summary>
        [JsonIgnore]
        public string DurationFormatted
        {
            get
            {
                if (Duration.TotalMinutes >= 1)
                    return $"{(int)Duration.TotalMinutes}m {Duration.Seconds}s";
                return $"{Duration.Seconds}s";
            }
        }

        private ScanResultSummary ComputeSummary()
        {
            var summary = new ScanResultSummary
            {
                TotalDetections = Detections.Count,
                CriticalCount = Detections.Count(d => d.Severity == ThreatSeverity.Critical),
                HighCount = Detections.Count(d => d.Severity == ThreatSeverity.High),
                MediumCount = Detections.Count(d => d.Severity == ThreatSeverity.Medium),
                LowCount = Detections.Count(d => d.Severity == ThreatSeverity.Low),
                InfoCount = Detections.Count(d => d.Severity == ThreatSeverity.Info)
            };

            // Count by category
            summary.CountsByCategory = Detections
                .GroupBy(d => d.Category)
                .ToDictionary(g => g.Key, g => g.Count());

            // Top 5 risky items (by severity then confidence)
            summary.TopRiskyItems = Detections
                .OrderByDescending(d => (int)d.Severity)
                .ThenByDescending(d => d.Confidence)
                .Take(5)
                .ToList();

            return summary;
        }

        /// <summary>
        /// STEP 30: Generate a chat-friendly explanation of the results.
        /// TRUTHFULNESS: Must accurately reflect actual scan data.
        /// </summary>
        public string GenerateChatExplanation(bool verbose = false)
        {
            // STEP 30: Truthfulness check - if not a real scan, say so
            if (!IsRealScan)
            {
                return "⚠️ Scan results are not available (demo mode or scan engine not connected). " +
                       "Would you like me to run a real security scan now?";
            }

            if (WasCancelled)
            {
                return $"The {ScanType} scan was cancelled after scanning {FilesScanned:N0} items. " +
                       $"Found {Detections.Count} detection(s) before cancellation. " +
                       "Would you like to run a complete scan?";
            }

            if (!string.IsNullOrEmpty(ErrorMessage))
            {
                return $"The {ScanType} scan encountered an error: {ErrorMessage}. " +
                       "Would you like to try again?";
            }

            if (Detections.Count == 0)
            {
                return $"✅ The {ScanType} scan completed in {DurationFormatted}, scanning {FilesScanned:N0} items. " +
                       "No threats were detected. Your system appears clean.";
            }

            var summary = Summary;
            var lines = new List<string>
            {
                $"The {ScanType} scan found **{summary.TotalDetections} detection(s)** after scanning {FilesScanned:N0} items in {DurationFormatted}."
            };

            // Severity breakdown with icons
            var severityParts = new List<string>();
            if (summary.CriticalCount > 0) severityParts.Add($"🔴 {summary.CriticalCount} critical");
            if (summary.HighCount > 0) severityParts.Add($"🟠 {summary.HighCount} high");
            if (summary.MediumCount > 0) severityParts.Add($"🟡 {summary.MediumCount} medium");
            if (summary.LowCount > 0) severityParts.Add($"🔵 {summary.LowCount} low");
            if (summary.InfoCount > 0) severityParts.Add($"⚪ {summary.InfoCount} info");

            if (severityParts.Count > 0)
                lines.Add($"Breakdown: {string.Join(", ", severityParts)}.");

            // Top detections - ALWAYS show actual data
            if (summary.TopRiskyItems.Count > 0)
            {
                lines.Add("");
                lines.Add("**Top detections:**");
                foreach (var item in summary.TopRiskyItems.Take(verbose ? 5 : 3))
                {
                    var severityIcon = item.Severity switch
                    {
                        ThreatSeverity.Critical => "🔴",
                        ThreatSeverity.High => "🟠",
                        ThreatSeverity.Medium => "🟡",
                        ThreatSeverity.Low => "🔵",
                        _ => "⚪"
                    };
                    lines.Add($"  {severityIcon} **{item.Name}**");
                    if (!string.IsNullOrEmpty(item.Reason))
                        lines.Add($"     {item.Reason}");
                    if (verbose && !string.IsNullOrEmpty(item.FilePath))
                    {
                        var shortPath = item.FilePath.Length > 50 
                            ? "..." + item.FilePath.Substring(item.FilePath.Length - 47)
                            : item.FilePath;
                        lines.Add($"     Path: `{shortPath}`");
                    }
                }

                if (summary.TotalDetections > (verbose ? 5 : 3))
                {
                    lines.Add($"  ... and {summary.TotalDetections - (verbose ? 5 : 3)} more");
                }
            }

            // Follow-up based on severity
            lines.Add("");
            if (summary.CriticalCount > 0 || summary.HighCount > 0)
            {
                lines.Add("⚠️ **Action recommended.** Would you like me to show the full list or explain how to remediate these?");
            }
            else if (summary.TotalDetections > 3)
            {
                lines.Add("Would you like to see the full list or open the Security Suite for details?");
            }
            else
            {
                lines.Add("These are low-risk items. Would you like more details or to open the Security Suite?");
            }

            return string.Join("\n", lines);
        }

        /// <summary>
        /// Save result to AppData for persistence
        /// </summary>
        public void SaveToFile()
        {
            try
            {
                var reportsDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "AtlasAI", "reports");
                Directory.CreateDirectory(reportsDir);

                var filePath = Path.Combine(reportsDir, "scan_last.json");
                var json = JsonSerializer.Serialize(this, new JsonSerializerOptions 
                { 
                    WriteIndented = true,
                    Converters = { new JsonStringEnumConverter() }
                });
                File.WriteAllText(filePath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SecurityScanResult] Failed to save: {ex.Message}");
            }
        }

        /// <summary>
        /// Load last scan result from AppData
        /// </summary>
        public static SecurityScanResult? LoadLastResult()
        {
            try
            {
                var filePath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "AtlasAI", "reports", "scan_last.json");

                if (!File.Exists(filePath)) return null;

                var json = File.ReadAllText(filePath);
                return JsonSerializer.Deserialize<SecurityScanResult>(json, new JsonSerializerOptions
                {
                    Converters = { new JsonStringEnumConverter() }
                });
            }
            catch
            {
                return null;
            }
        }
    }

    /// <summary>
    /// Individual detection from a scan
    /// </summary>
    public class ScanDetection
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = string.Empty;
        public ThreatCategory Category { get; set; }
        public ThreatSeverity Severity { get; set; }
        public string FilePath { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
        public int Confidence { get; set; } = 50;
        public RecommendedAction RecommendedAction { get; set; } = RecommendedAction.Investigate;
        public DateTime DetectedAt { get; set; } = DateTime.UtcNow;
        public long FileSizeBytes { get; set; }
        public bool CanQuarantine { get; set; } = true;
        public bool CanDelete { get; set; } = true;

        /// <summary>
        /// Create from SecurityFinding
        /// </summary>
        public static ScanDetection FromFinding(SecurityFinding finding)
        {
            return new ScanDetection
            {
                Id = finding.Id,
                Name = finding.Title,
                Category = finding.Category,
                Severity = finding.Severity,
                FilePath = finding.FilePath,
                Reason = finding.Description,
                Confidence = finding.Confidence switch
                {
                    FindingConfidence.Confirmed => 95,
                    FindingConfidence.High => 80,
                    FindingConfidence.Medium => 60,
                    FindingConfidence.Low => 35,
                    _ => 50
                },
                RecommendedAction = finding.Severity >= ThreatSeverity.High 
                    ? RecommendedAction.Quarantine 
                    : RecommendedAction.Investigate,
                DetectedAt = finding.DetectedAt,
                FileSizeBytes = finding.FileSizeBytes,
                CanQuarantine = finding.CanQuarantine,
                CanDelete = finding.CanDelete
            };
        }
    }

    /// <summary>
    /// Recommended action for a detection (read-only recommendation)
    /// </summary>
    public enum RecommendedAction
    {
        Ignore,
        Investigate,
        Quarantine,
        Remove
    }

    /// <summary>
    /// Summary statistics for quick reference
    /// </summary>
    public class ScanResultSummary
    {
        public int TotalDetections { get; set; }
        public int CriticalCount { get; set; }
        public int HighCount { get; set; }
        public int MediumCount { get; set; }
        public int LowCount { get; set; }
        public int InfoCount { get; set; }
        public Dictionary<ThreatCategory, int> CountsByCategory { get; set; } = new();
        public List<ScanDetection> TopRiskyItems { get; set; } = new();

        /// <summary>
        /// True if any high-severity threats exist
        /// </summary>
        public bool HasHighRiskThreats => CriticalCount > 0 || HighCount > 0;
    }
}
