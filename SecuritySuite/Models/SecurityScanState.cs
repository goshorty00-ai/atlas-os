using System;

namespace AtlasAI.SecuritySuite.Models
{
    /// <summary>
    /// Scan phases for deterministic progress tracking
    /// </summary>
    public enum SecurityScanPhase
    {
        Idle,
        Starting,
        Enumerating,
        Scanning,
        Analyzing,
        Completed,
        Cancelled,
        Failed
    }

    /// <summary>
    /// Real-time scan progress for UI updates
    /// </summary>
    public class SecurityScanProgress
    {
        public SecurityScanPhase Phase { get; set; } = SecurityScanPhase.Idle;
        public string CurrentPath { get; set; } = string.Empty;
        public long FilesScanned { get; set; }
        public long FilesTotalEstimated { get; set; }
        public int ThreatsFound { get; set; }
        public TimeSpan Elapsed { get; set; }
        public DateTime LastUpdateUtc { get; set; } = DateTime.UtcNow;
        public string StatusLine { get; set; } = string.Empty;
        public int ProgressPercent { get; set; }

        /// <summary>
        /// Sanitized display path (truncated for UI)
        /// </summary>
        public string DisplayPath
        {
            get
            {
                if (string.IsNullOrEmpty(CurrentPath)) return string.Empty;
                if (CurrentPath.Length <= 50) return CurrentPath;
                return "..." + CurrentPath.Substring(CurrentPath.Length - 47);
            }
        }

        /// <summary>
        /// Human-readable elapsed time
        /// </summary>
        public string ElapsedFormatted
        {
            get
            {
                if (Elapsed.TotalMinutes >= 1)
                    return $"{(int)Elapsed.TotalMinutes}m {Elapsed.Seconds}s";
                return $"{Elapsed.Seconds}s";
            }
        }

        /// <summary>
        /// Phase display name for UI
        /// </summary>
        public string PhaseDisplay => Phase switch
        {
            SecurityScanPhase.Idle => "Ready",
            SecurityScanPhase.Starting => "Starting...",
            SecurityScanPhase.Enumerating => "Enumerating...",
            SecurityScanPhase.Scanning => "Scanning...",
            SecurityScanPhase.Analyzing => "Analyzing...",
            SecurityScanPhase.Completed => "Complete",
            SecurityScanPhase.Cancelled => "Cancelled",
            SecurityScanPhase.Failed => "Failed",
            _ => "Unknown"
        };
    }
}
