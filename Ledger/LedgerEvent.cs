using System;
using System.Collections.Generic;

namespace AtlasAI.Ledger
{
    /// <summary>
    /// Categories of system events that Atlas monitors
    /// </summary>
    public enum LedgerCategory
    {
        System,           // General system changes
        Security,         // Security-related events
        Network,          // Network configuration changes
        Startup,          // Startup/autorun changes
        ScheduledTask,    // Scheduled task changes
        Software,         // Software install/uninstall
        FileSystem,       // Critical file changes (hosts, etc.)
        Registry,         // Registry modifications
        CreativeAsset,    // FAB/Unreal/Blender assets
        Mode              // Work/Game/Quiet mode changes
    }

    /// <summary>
    /// Severity level of the event
    /// </summary>
    public enum LedgerSeverity
    {
        Info,             // Informational, no action needed
        Low,              // Minor change, review if curious
        Medium,           // Notable change, worth reviewing
        High,             // Significant change, review recommended
        Critical          // Potentially dangerous, immediate attention
    }

    /// <summary>
    /// Type of action that can be taken on an event
    /// </summary>
    public enum LedgerActionType
    {
        Revert,           // Restore previous state
        Delete,           // Remove the item
        Block,            // Block/disable the item
        Allow,            // Whitelist/allow the item
        Inspect,          // Open for manual inspection
        Dismiss           // Mark as reviewed/safe
    }

    /// <summary>
    /// An action that can be performed on a ledger event
    /// </summary>
    public class LedgerAction
    {
        public string Label { get; set; } = "";
        public LedgerActionType Type { get; set; }
        public string? Data { get; set; }  // Action-specific data (e.g., backup path for revert)
        public bool RequiresConfirmation { get; set; } = false;
        
        public static LedgerAction Revert(string label = "Revert", string? backupData = null) 
            => new() { Label = label, Type = LedgerActionType.Revert, Data = backupData };
        
        public static LedgerAction Delete(string label = "Delete") 
            => new() { Label = label, Type = LedgerActionType.Delete, RequiresConfirmation = true };
        
        public static LedgerAction Block(string label = "Block") 
            => new() { Label = label, Type = LedgerActionType.Block };
        
        public static LedgerAction Allow(string label = "Allow") 
            => new() { Label = label, Type = LedgerActionType.Allow };
        
        public static LedgerAction Inspect(string label = "Inspect", string? path = null) 
            => new() { Label = label, Type = LedgerActionType.Inspect, Data = path };
        
        public static LedgerAction Dismiss(string label = "Dismiss") 
            => new() { Label = label, Type = LedgerActionType.Dismiss };
    }

    /// <summary>
    /// A piece of evidence associated with an event
    /// </summary>
    public class LedgerEvidence
    {
        public string Key { get; set; } = "";
        public string Value { get; set; } = "";
        public bool IsPath { get; set; } = false;  // If true, can be clicked to open
        
        public LedgerEvidence() { }
        public LedgerEvidence(string key, string value, bool isPath = false)
        {
            Key = key;
            Value = value;
            IsPath = isPath;
        }
    }

    /// <summary>
    /// A ledger event representing a system change detected by Atlas
    /// </summary>
    public class LedgerEvent
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public LedgerCategory Category { get; set; }
        public LedgerSeverity Severity { get; set; }
        public string Title { get; set; } = "";
        public string WhyItMatters { get; set; } = "";
        
        public List<LedgerEvidence> Evidence { get; set; } = new();
        public List<LedgerAction> Actions { get; set; } = new();
        
        // State tracking
        public bool IsResolved { get; set; } = false;
        public DateTime? ResolvedAt { get; set; }
        public string? ResolvedBy { get; set; }  // Action that resolved it
        public string? ResolvedNote { get; set; }
        
        // For revert functionality
        public string? BackupData { get; set; }  // Serialized backup for revert
        
        /// <summary>
        /// Add evidence to this event
        /// </summary>
        public LedgerEvent WithEvidence(string key, string value, bool isPath = false)
        {
            Evidence.Add(new LedgerEvidence(key, value, isPath));
            return this;
        }
        
        /// <summary>
        /// Add an action to this event
        /// </summary>
        public LedgerEvent WithAction(LedgerAction action)
        {
            Actions.Add(action);
            return this;
        }
        
        /// <summary>
        /// Mark this event as resolved
        /// </summary>
        public void Resolve(string actionLabel, string? note = null)
        {
            IsResolved = true;
            ResolvedAt = DateTime.Now;
            ResolvedBy = actionLabel;
            ResolvedNote = note;
        }
    }
}
