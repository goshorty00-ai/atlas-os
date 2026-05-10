using System;
using System.Collections.Generic;
using System.Linq;
using AtlasAI.Ledger;

namespace AtlasAI.SecuritySuite.Models
{
    #region Enums
    
    public enum ScanType
    {
        Quick,
        Full,
        Custom,
        Junk,
        Privacy
    }
    
    public enum ScanStatus
    {
        Idle,
        Running,
        Paused,
        Completed,
        Cancelled,
        Failed
    }
    
    public enum ThreatSeverity
    {
        Info,
        Low,
        Medium,
        High,
        Critical
    }
    
    public enum ThreatCategory
    {
        Malware,
        PUP,
        Adware,
        Spyware,
        Trojan,
        Rootkit,
        BrowserHijacker,
        PrivacyRisk,
        JunkFile,
        StartupItem,
        ScheduledTask,
        Service,
        Registry,
        NetworkThreat,
        Unknown
    }
    
    public enum FindingConfidence
    {
        Low,
        Medium,
        High,
        Confirmed
    }
    
    public enum QuarantineStatus
    {
        Active,
        Restored,
        Deleted
    }
    
    public enum ProtectionStatus
    {
        Protected,
        AtRisk,
        Critical,
        Unknown
    }
    
    public enum UpdateStatus
    {
        UpToDate,
        UpdateAvailable,
        Updating,
        Failed,
        Offline
    }
    
    /// <summary>
    /// Event significance for timeline display
    /// </summary>
    public enum EventSignificance
    {
        Routine,      // Normal system activity
        Notable,      // Worth mentioning
        Attention,    // Deserves attention
        Significant   // Important finding
    }
    
    #endregion
    
    #region Timeline Event System
    
    /// <summary>
    /// A narrative event in the system timeline
    /// </summary>
    public class TimelineEvent
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public DateTime Timestamp { get; set; } = DateTime.Now;
        
        /// <summary>Human-readable narrative of what happened</summary>
        public string Narrative { get; set; } = string.Empty;
        
        /// <summary>Short title for display</summary>
        public string Title { get; set; } = string.Empty;
        
        /// <summary>Why this matters (optional context)</summary>
        public string Context { get; set; } = string.Empty;
        
        /// <summary>Why this event matters (for details panel)</summary>
        public string WhyItMatters { get; set; } = string.Empty;
        
        /// <summary>How confident Atlas is about this observation</summary>
        public int ConfidencePercent { get; set; } = 80;
        
        /// <summary>Significance level affects visual treatment</summary>
        public EventSignificance Significance { get; set; } = EventSignificance.Routine;
        
        /// <summary>Optional action the user can take</summary>
        public string ActionText { get; set; } = string.Empty;
        
        /// <summary>Action identifier for command binding</summary>
        public string ActionId { get; set; } = string.Empty;
        
        /// <summary>Related file or path (if applicable)</summary>
        public string RelatedPath { get; set; } = string.Empty;
        
        /// <summary>Is this a creative asset (game dev file)?</summary>
        public bool IsCreativeAsset { get; set; } = false;
        
        /// <summary>Evidence items for the details panel</summary>
        public List<LedgerEvidence> Evidence { get; set; } = new();
        
        /// <summary>Available actions for the details panel</summary>
        public List<LedgerAction> Actions { get; set; } = new();
        
        // Display helpers
        public bool HasContext => !string.IsNullOrEmpty(Context);
        public bool HasAction => !string.IsNullOrEmpty(ActionText);
        public bool ShowConfidence => Significance >= EventSignificance.Notable;
        public bool HasPath => !string.IsNullOrEmpty(RelatedPath);
        
        /// <summary>Short narrative for timeline display (truncated)</summary>
        public string ShortNarrative => Narrative.Length > 50 ? Narrative.Substring(0, 47) + "..." : Narrative;
        
        /// <summary>Formatted timestamp for display</summary>
        public string TimestampFormatted => Timestamp.Date == DateTime.Today 
            ? $"Today, {Timestamp:h:mm tt}" 
            : Timestamp.Date == DateTime.Today.AddDays(-1)
                ? $"Yesterday, {Timestamp:h:mm tt}"
                : Timestamp.ToString("MMM d, h:mm tt");
        
        public string ConfidenceText => ConfidencePercent switch
        {
            >= 90 => "Very high",
            >= 70 => "High",
            >= 50 => "Moderate",
            >= 30 => "Low",
            _ => "Uncertain"
        };
        
        public string NodeColor => Significance switch
        {
            EventSignificance.Routine => "#475569",
            EventSignificance.Notable => "#6366f1",
            EventSignificance.Attention => "#22d3ee",
            EventSignificance.Significant => "#8b5cf6",
            _ => "#475569"
        };
        
        /// <summary>
        /// Create a timeline event from a security finding
        /// </summary>
        public static TimelineEvent FromFinding(SecurityFinding finding)
        {
            var isCreative = CreativeAssetPolicy.IsTrustedCreativeAsset(finding.FilePath);
            
            // Generate calm, narrative language
            var narrative = GenerateNarrative(finding, isCreative);
            var context = GenerateContext(finding, isCreative);
            var significance = DetermineSignificance(finding, isCreative);
            
            return new TimelineEvent
            {
                Timestamp = finding.DetectedAt,
                Narrative = narrative,
                Context = context,
                ConfidencePercent = finding.Confidence switch
                {
                    FindingConfidence.Confirmed => 95,
                    FindingConfidence.High => 80,
                    FindingConfidence.Medium => 60,
                    FindingConfidence.Low => 35,
                    _ => 50
                },
                Significance = significance,
                ActionText = significance >= EventSignificance.Attention ? "Review" : "",
                ActionId = finding.Id,
                RelatedPath = finding.FilePath,
                IsCreativeAsset = isCreative
            };
        }
        
        private static string GenerateNarrative(SecurityFinding finding, bool isCreative)
        {
            if (isCreative)
            {
                return $"Noticed a creative asset file: {System.IO.Path.GetFileName(finding.FilePath)}. " +
                       "This appears to be a game development or 3D art file.";
            }
            
            return finding.Category switch
            {
                ThreatCategory.JunkFile => $"Found temporary files that could be cleaned up.",
                ThreatCategory.StartupItem => $"Observed a program configured to run at startup: {finding.Title}",
                ThreatCategory.PrivacyRisk => $"Detected data that may affect your privacy.",
                ThreatCategory.PUP => $"Found software that you may not have intentionally installed.",
                ThreatCategory.Adware => $"Noticed advertising-related software on your system.",
                ThreatCategory.BrowserHijacker => $"Observed browser settings that may have been modified.",
                ThreatCategory.Malware => $"Identified a file that matches known problematic patterns.",
                ThreatCategory.Spyware => $"Found software that may be collecting information.",
                _ => $"Observed: {finding.Title}"
            };
        }
        
        private static string GenerateContext(SecurityFinding finding, bool isCreative)
        {
            if (isCreative)
            {
                return "Creative assets like this are non-executable and pose no risk. " +
                       "Atlas recognizes files from Unreal Engine, Blender, Unity, and similar tools.";
            }
            
            if (finding.Severity <= ThreatSeverity.Low)
            {
                return "This is informational. No action is required.";
            }
            
            if (finding.Severity == ThreatSeverity.Medium)
            {
                return "You may want to review this when convenient. It's not urgent.";
            }
            
            return finding.Recommendation;
        }
        
        private static EventSignificance DetermineSignificance(SecurityFinding finding, bool isCreative)
        {
            if (isCreative) return EventSignificance.Routine;
            
            return finding.Severity switch
            {
                ThreatSeverity.Info => EventSignificance.Routine,
                ThreatSeverity.Low => EventSignificance.Routine,
                ThreatSeverity.Medium => EventSignificance.Notable,
                ThreatSeverity.High => EventSignificance.Attention,
                ThreatSeverity.Critical => EventSignificance.Significant,
                _ => EventSignificance.Routine
            };
        }
    }
    
    #endregion
    
    #region Creative Asset Policy
    
    /// <summary>
    /// Creative Asset Trust Policy - treats creative files as non-executable by default
    /// </summary>
    public static class CreativeAssetPolicy
    {
        public static readonly HashSet<string> TrustedExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            // Unreal Engine
            ".uasset", ".umap", ".uproject", ".uplugin", ".upluginmanifest",
            ".uexp", ".ubulk", ".utoc", ".ucas", ".pak",
            
            // FAB / Quixel
            ".megascan", ".bridge",
            
            // Blender
            ".blend", ".blend1", ".blend2",
            
            // 3D Models
            ".fbx", ".obj", ".gltf", ".glb", ".dae", ".3ds", ".max", ".ma", ".mb",
            ".c4d", ".lwo", ".lws", ".stl", ".ply", ".abc", ".usd", ".usda", ".usdc", ".usdz",
            
            // Textures & Images
            ".psd", ".psb", ".tga", ".dds", ".exr", ".hdr", ".tif", ".tiff",
            ".png", ".jpg", ".jpeg", ".bmp", ".webp", ".svg", ".ai", ".eps",
            
            // Audio
            ".wav", ".mp3", ".ogg", ".flac", ".aiff", ".aif", ".wem", ".bnk",
            
            // Video
            ".mp4", ".mov", ".avi", ".mkv", ".webm", ".bik",
            
            // Unity
            ".unity", ".prefab", ".asset", ".mat", ".anim", ".controller",
            ".physicMaterial", ".physicsMaterial2D", ".cubemap", ".flare",
            ".guiskin", ".fontsettings", ".mask", ".overrideController",
            
            // Substance
            ".sbsar", ".sbs", ".spp", ".sppr",
            
            // ZBrush
            ".ztl", ".zpr", ".zbr",
            
            // Houdini
            ".hip", ".hipnc", ".hda", ".hdanc", ".otl", ".otlnc",
            
            // Fonts
            ".ttf", ".otf", ".woff", ".woff2", ".fnt",
            
            // Shaders
            ".shader", ".cginc", ".hlsl", ".glsl", ".usf", ".ush",
            
            // Config/Data
            ".json", ".xml", ".yaml", ".yml", ".ini", ".cfg", ".config",
            ".csv", ".tsv", ".txt", ".md", ".rtf"
        };
        
        public static readonly HashSet<string> TrustedFolders = new(StringComparer.OrdinalIgnoreCase)
        {
            "Content", "Assets", "Resources", "Textures", "Materials", "Meshes",
            "Models", "Animations", "Audio", "Sounds", "Music", "SFX",
            "Blueprints", "Maps", "Levels", "Prefabs", "Scenes",
            "Plugins", "ThirdParty", "Marketplace", "FAB", "Megascans",
            "StarterContent", "EngineContent", "ProjectContent",
            "UnrealEngine", "Epic Games", "Blender", "Unity"
        };
        
        public static bool IsTrustedCreativeAsset(string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return false;
            
            var ext = System.IO.Path.GetExtension(filePath);
            if (TrustedExtensions.Contains(ext)) return true;
            
            var pathParts = filePath.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
            return pathParts.Any(part => TrustedFolders.Contains(part));
        }
    }
    
    #endregion

    #region Core Models
    
    /// <summary>
    /// Represents a security finding
    /// </summary>
    public class SecurityFinding
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public ThreatCategory Category { get; set; }
        public ThreatSeverity Severity { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public string FileHash { get; set; } = string.Empty;
        public string MatchedRule { get; set; } = string.Empty;
        public string Recommendation { get; set; } = string.Empty;
        public FindingConfidence Confidence { get; set; }
        public DateTime DetectedAt { get; set; } = DateTime.Now;
        public long FileSizeBytes { get; set; }
        public bool CanQuarantine { get; set; } = true;
        public bool CanDelete { get; set; } = true;
        public bool IsAdvisory { get; set; } = false;
        public Dictionary<string, string> Evidence { get; set; } = new();
    }
    
    /// <summary>
    /// Represents a scan job
    /// </summary>
    public class ScanJob
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public ScanType Type { get; set; }
        public ScanStatus Status { get; set; } = ScanStatus.Idle;
        public DateTime StartedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public TimeSpan? Duration => CompletedAt.HasValue ? CompletedAt - StartedAt : DateTime.Now - StartedAt;
        public long FilesScanned { get; set; }
        public long TotalFilesToScan { get; set; }
        public int ThreatsFound { get; set; }
        public int ProgressPercent { get; set; }
        public string CurrentItem { get; set; } = string.Empty;
        public string StatusMessage { get; set; } = string.Empty;
        public List<string> CustomPaths { get; set; } = new();
        public List<SecurityFinding> Findings { get; set; } = new();
        public string? ErrorMessage { get; set; }
    }
    
    /// <summary>
    /// Quarantined item
    /// </summary>
    public class QuarantinedItem
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string OriginalPath { get; set; } = string.Empty;
        public string QuarantinePath { get; set; } = string.Empty;
        public string FileHash { get; set; } = string.Empty;
        public long FileSizeBytes { get; set; }
        public DateTime QuarantinedAt { get; set; } = DateTime.Now;
        public ThreatCategory ThreatCategory { get; set; }
        public ThreatSeverity Severity { get; set; }
        public string ThreatName { get; set; } = string.Empty;
        public QuarantineStatus Status { get; set; } = QuarantineStatus.Active;
        public DateTime? RestoredAt { get; set; }
        public DateTime? DeletedAt { get; set; }
    }

    /// <summary>
    /// Definitions database info
    /// </summary>
    public class DefinitionsInfo
    {
        public string Version { get; set; } = "1.0.0";
        public DateTime LastUpdated { get; set; } = DateTime.MinValue;
        public int SignatureCount { get; set; }
        public string EngineVersion { get; set; } = "1.0.0";
        public UpdateStatus Status { get; set; } = UpdateStatus.UpToDate;
        public string? LatestAvailableVersion { get; set; }
    }

    /// <summary>
    /// Update manifest from server
    /// </summary>
    public class UpdateManifest
    {
        public string Version { get; set; } = string.Empty;
        public string PackageUrl { get; set; } = string.Empty;
        public string Sha256 { get; set; } = string.Empty;
        public string Signature { get; set; } = string.Empty;
        public DateTime ReleaseDate { get; set; }
        public int SignatureCount { get; set; }
        public string EngineVersion { get; set; } = string.Empty;
        public string ReleaseNotes { get; set; } = string.Empty;
    }

    /// <summary>
    /// Update log entry
    /// </summary>
    public class UpdateLogEntry
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public string FromVersion { get; set; } = string.Empty;
        public string ToVersion { get; set; } = string.Empty;
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public TimeSpan Duration { get; set; }
    }

    /// <summary>
    /// Scan report for history
    /// </summary>
    public class ScanReport
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public ScanType Type { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public TimeSpan Duration => EndTime - StartTime;
        public long FilesScanned { get; set; }
        public int ThreatsFound { get; set; }
        public int ThreatsRemoved { get; set; }
        public int ThreatsQuarantined { get; set; }
        public int ThreatsIgnored { get; set; }
        public List<SecurityFinding> Findings { get; set; } = new();
        public bool WasCancelled { get; set; }
        public string? ErrorMessage { get; set; }
    }

    /// <summary>
    /// Protection score calculation
    /// </summary>
    public class ProtectionScore
    {
        public int Score { get; set; } = 100;
        public ProtectionStatus Status { get; set; } = ProtectionStatus.Protected;
        public List<string> Issues { get; set; } = new();
        public List<string> Recommendations { get; set; } = new();
        public DateTime CalculatedAt { get; set; } = DateTime.Now;
    }

    /// <summary>
    /// Dashboard status summary
    /// </summary>
    public class DashboardStatus
    {
        public ProtectionScore ProtectionScore { get; set; } = new();
        public DefinitionsInfo Definitions { get; set; } = new();
        public ScanReport? LastScan { get; set; }
        public int QuarantinedItemsCount { get; set; }
        public int ActiveThreatsCount { get; set; }
        public List<RecentActivity> RecentActivities { get; set; } = new();
    }

    /// <summary>
    /// Recent activity entry
    /// </summary>
    public class RecentActivity
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public string Icon { get; set; } = "🔍";
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string? ActionTaken { get; set; }
    }

    /// <summary>
    /// Scheduler settings
    /// </summary>
    public class SchedulerSettings
    {
        public bool AutoUpdateEnabled { get; set; } = true;
        public int UpdateCheckIntervalHours { get; set; } = 6;
        public bool QuietHoursEnabled { get; set; } = false;
        public TimeSpan QuietHoursStart { get; set; } = new TimeSpan(22, 0, 0);
        public TimeSpan QuietHoursEnd { get; set; } = new TimeSpan(7, 0, 0);
        public bool AutoScanEnabled { get; set; } = false;
        public ScanType AutoScanType { get; set; } = ScanType.Quick;
        public DayOfWeek AutoScanDay { get; set; } = DayOfWeek.Sunday;
        public TimeSpan AutoScanTime { get; set; } = new TimeSpan(3, 0, 0);
    }

    /// <summary>
    /// Junk file item for cleanup
    /// </summary>
    public class JunkFileItem
    {
        public string Path { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public long SizeBytes { get; set; }
        public DateTime LastModified { get; set; }
        public bool IsSelected { get; set; } = true;
    }

    /// <summary>
    /// Junk scan result
    /// </summary>
    public class JunkScanResult
    {
        public List<JunkFileItem> Items { get; set; } = new();
        public long TotalSizeBytes { get; set; }
        public Dictionary<string, long> SizeByCategory { get; set; } = new();
    }
    
    /// <summary>
    /// Embedded definitions structure
    /// </summary>
    public class EmbeddedDefinitions
    {
        public string Version { get; set; } = string.Empty;
        public DateTime LastUpdated { get; set; }
        public List<string> MalwareHashes { get; set; } = new();
        public List<string> SuspiciousPatterns { get; set; } = new();
        public List<string> KnownBadProcesses { get; set; } = new();
        public List<string> KnownBadStartupEntries { get; set; } = new();
        public List<string> KnownBadExtensions { get; set; } = new();
    }

    #endregion
}
