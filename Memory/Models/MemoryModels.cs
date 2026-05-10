#nullable enable
using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace AtlasAI.Memory.Models
{
    /// <summary>
    /// Type of memory entry - categorizes what kind of decision/pattern is being stored.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum MemoryEntryType
    {
        /// <summary>Naming conventions, code style preferences</summary>
        Convention,
        
        /// <summary>Structural decisions, architectural patterns</summary>
        Architecture,
        
        /// <summary>Preferred implementation patterns</summary>
        Pattern,
        
        /// <summary>Approaches to avoid, rejected suggestions</summary>
        Rejection,
        
        /// <summary>Build tools, commands, configurations</summary>
        Tooling,
        
        /// <summary>Security/safety constraints</summary>
        Safety
    }

    /// <summary>
    /// Source of the memory entry - who created it.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum MemorySource
    {
        /// <summary>Explicitly set by user</summary>
        User,
        
        /// <summary>Automatically captured by Atlas agent</summary>
        Agent
    }

    /// <summary>
    /// A single memory entry representing a learned decision, pattern, or preference.
    /// </summary>
    public class MemoryEntry
    {
        /// <summary>
        /// Unique identifier for this entry (12 character hex string).
        /// </summary>
        public string Id { get; set; } = Guid.NewGuid().ToString("N")[..12];

        /// <summary>
        /// Type/category of this memory entry.
        /// </summary>
        public MemoryEntryType Type { get; set; }

        /// <summary>
        /// Source of this memory (user or agent).
        /// </summary>
        public MemorySource Source { get; set; }

        /// <summary>
        /// Confidence score between 0.0 and 1.0 indicating how certain Atlas is about this memory.
        /// </summary>
        public double Confidence { get; set; }

        /// <summary>
        /// One-line note describing the memory (max 500 characters).
        /// </summary>
        public string Note { get; set; } = "";

        /// <summary>
        /// When this memory was created.
        /// </summary>
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// When this memory was last applied as a constraint (null if never applied).
        /// </summary>
        public DateTime? LastAppliedAt { get; set; }

        /// <summary>
        /// Number of times this memory has been applied as a constraint.
        /// </summary>
        public int ApplyCount { get; set; }

        /// <summary>
        /// Creates a deep copy of this memory entry.
        /// </summary>
        public MemoryEntry Clone()
        {
            return new MemoryEntry
            {
                Id = Id,
                Type = Type,
                Source = Source,
                Confidence = Confidence,
                Note = Note,
                CreatedAt = CreatedAt,
                LastAppliedAt = LastAppliedAt,
                ApplyCount = ApplyCount
            };
        }

        /// <summary>
        /// Checks equality based on all fields.
        /// </summary>
        public bool Equals(MemoryEntry? other)
        {
            if (other is null) return false;
            return Id == other.Id &&
                   Type == other.Type &&
                   Source == other.Source &&
                   Math.Abs(Confidence - other.Confidence) < 0.0001 &&
                   Note == other.Note &&
                   CreatedAt == other.CreatedAt &&
                   LastAppliedAt == other.LastAppliedAt &&
                   ApplyCount == other.ApplyCount;
        }

        public override bool Equals(object? obj) => Equals(obj as MemoryEntry);

        public override int GetHashCode() => HashCode.Combine(Id, Type, Source, Confidence, Note);

        public override string ToString() => $"[{Type}] {Note} (confidence: {Confidence:P0})";
    }

    /// <summary>
    /// Container for all project memory data, serialized to .AtlasAI/memory.json.
    /// </summary>
    public class ProjectMemoryData
    {
        /// <summary>
        /// Schema version for forward compatibility.
        /// </summary>
        public string Version { get; set; } = "1.0";

        /// <summary>
        /// Name of the workspace this memory belongs to.
        /// </summary>
        public string WorkspaceName { get; set; } = "";

        /// <summary>
        /// When this memory file was first created.
        /// </summary>
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// When this memory file was last modified.
        /// </summary>
        public DateTime LastModifiedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// All memory entries for this project.
        /// </summary>
        public List<MemoryEntry> Entries { get; set; } = new();
    }

    /// <summary>
    /// Result of a memory operation for logging and auditing.
    /// </summary>
    public class MemoryOperationLog
    {
        /// <summary>
        /// Type of operation performed.
        /// </summary>
        public string Operation { get; set; } = "";

        /// <summary>
        /// When the operation occurred.
        /// </summary>
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// ID of the affected entry (if applicable).
        /// </summary>
        public string? EntryId { get; set; }

        /// <summary>
        /// Additional details about the operation.
        /// </summary>
        public string? Details { get; set; }
    }
}
