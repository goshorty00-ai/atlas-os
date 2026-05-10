#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using AtlasAI.Memory.Models;

namespace AtlasAI.Memory
{
    /// <summary>
    /// Project-scoped memory store that persists learned decisions, patterns, and preferences
    /// to .AtlasAI/memory.json in the workspace root.
    /// </summary>
    public class ProjectMemoryStore
    {
        private static ProjectMemoryStore? _instance;
        private static readonly object _instanceLock = new();

        private string? _currentWorkspacePath;
        private ProjectMemoryData _memoryData = new();
        private readonly SemaphoreSlim _saveLock = new(1, 1);
        private bool _isDirty;
        private Timer? _autoSaveTimer;
        private readonly List<MemoryOperationLog> _operationLogs = new();

        private static readonly Regex[] PiiPatterns =
        [
            new Regex(@"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Z|a-z]{2,}\b", RegexOptions.Compiled),
            new Regex(@"\b\d{3}[-.]?\d{3}[-.]?\d{4}\b", RegexOptions.Compiled),
            new Regex(@"\b\d{3}[-]?\d{2}[-]?\d{4}\b", RegexOptions.Compiled),
            new Regex(@"\b\d{16}\b", RegexOptions.Compiled),
            new Regex(@"\b(?:password|passwd|pwd)\s*[:=]\s*\S+", RegexOptions.Compiled | RegexOptions.IgnoreCase),
            new Regex(@"\b(?:api[_-]?key|apikey|secret[_-]?key)\s*[:=]\s*\S+", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        ];

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        public event EventHandler<MemoryEntry>? OnMemoryAdded;
        public event EventHandler<string>? OnMemoryRemoved;
        public event EventHandler? OnMemoryLoaded;
        public event EventHandler? OnMemoryCleared;

        public static ProjectMemoryStore Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_instanceLock)
                    {
                        _instance ??= new ProjectMemoryStore();
                    }
                }
                return _instance;
            }
        }

        private ProjectMemoryStore() { }

        public string? CurrentWorkspacePath => _currentWorkspacePath;
        public int EntryCount => _memoryData.Entries.Count;
        public bool HasUnsavedChanges => _isDirty;

        private string? MemoryFilePath => _currentWorkspacePath != null
            ? Path.Combine(_currentWorkspacePath, ".AtlasAI", "memory.json")
            : null;


        public async Task InitializeAsync(string workspacePath)
        {
            if (string.IsNullOrWhiteSpace(workspacePath))
                throw new ArgumentException("Workspace path cannot be empty", nameof(workspacePath));

            _currentWorkspacePath = workspacePath;

            var atlasDir = Path.Combine(workspacePath, ".AtlasAI");
            if (!Directory.Exists(atlasDir))
            {
                Directory.CreateDirectory(atlasDir);
                LogOperation("CreateDirectory", null, $"Created {atlasDir}");
            }

            await LoadAsync();
        }

        public async Task LoadAsync()
        {
            if (MemoryFilePath == null) return;

            await _saveLock.WaitAsync();
            try
            {
                if (!File.Exists(MemoryFilePath))
                {
                    _memoryData = new ProjectMemoryData
                    {
                        WorkspaceName = Path.GetFileName(_currentWorkspacePath) ?? "Unknown"
                    };
                    LogOperation("CreateNew", null, "Created new memory data");
                    OnMemoryLoaded?.Invoke(this, EventArgs.Empty);
                    return;
                }

                try
                {
                    var json = await File.ReadAllTextAsync(MemoryFilePath);
                    var loaded = JsonSerializer.Deserialize<ProjectMemoryData>(json, JsonOptions);

                    if (loaded != null)
                    {
                        var validEntries = new List<MemoryEntry>();
                        foreach (var entry in loaded.Entries)
                        {
                            var (isValid, reason) = ValidateEntry(entry);
                            if (isValid)
                                validEntries.Add(entry);
                            else
                                LogOperation("SkipInvalid", entry.Id, reason);
                        }
                        loaded.Entries = validEntries;
                        _memoryData = loaded;
                        LogOperation("Load", null, $"Loaded {validEntries.Count} entries");
                    }
                }
                catch (JsonException ex)
                {
                    var backupPath = MemoryFilePath + ".backup";
                    File.Copy(MemoryFilePath, backupPath, overwrite: true);
                    LogOperation("BackupCorrupted", null, $"Backed up corrupted file: {ex.Message}");

                    _memoryData = new ProjectMemoryData
                    {
                        WorkspaceName = Path.GetFileName(_currentWorkspacePath) ?? "Unknown"
                    };
                }

                OnMemoryLoaded?.Invoke(this, EventArgs.Empty);
            }
            finally
            {
                _saveLock.Release();
            }
        }

        public async Task SaveAsync()
        {
            if (MemoryFilePath == null) return;

            await _saveLock.WaitAsync();
            try
            {
                _memoryData.LastModifiedAt = DateTime.UtcNow;
                var json = JsonSerializer.Serialize(_memoryData, JsonOptions);

                var tempPath = MemoryFilePath + ".tmp";
                await File.WriteAllTextAsync(tempPath, json);
                File.Move(tempPath, MemoryFilePath, overwrite: true);

                _isDirty = false;
                LogOperation("Save", null, $"Saved {_memoryData.Entries.Count} entries");
            }
            finally
            {
                _saveLock.Release();
            }
        }


        public async Task<MemoryEntry?> AddMemoryAsync(MemoryEntryType type, MemorySource source, string note, double confidence = 0.7)
        {
            if (ContainsPii(note))
            {
                LogOperation("RejectPII", null, "Note contains PII patterns");
                return null;
            }

            if (note.Length > 500)
            {
                note = note[..500];
                LogOperation("TruncateNote", null, "Note truncated to 500 chars");
            }

            confidence = Math.Clamp(confidence, 0.0, 1.0);

            var entry = new MemoryEntry
            {
                Type = type,
                Source = source,
                Note = note,
                Confidence = confidence
            };

            _memoryData.Entries.Add(entry);
            _isDirty = true;
            LogOperation("Add", entry.Id, $"Added {type} entry");

            OnMemoryAdded?.Invoke(this, entry);
            ScheduleAutoSave();

            return entry;
        }

        public async Task<bool> RemoveMemoryAsync(string entryId)
        {
            var entry = _memoryData.Entries.FirstOrDefault(e => e.Id == entryId);
            if (entry == null) return false;

            _memoryData.Entries.Remove(entry);
            _isDirty = true;
            LogOperation("Remove", entryId, "Removed entry");

            OnMemoryRemoved?.Invoke(this, entryId);
            ScheduleAutoSave();

            return true;
        }

        public async Task ClearAllMemoryAsync()
        {
            var count = _memoryData.Entries.Count;
            _memoryData.Entries.Clear();
            _isDirty = true;
            LogOperation("ClearAll", null, $"Cleared {count} entries");

            OnMemoryCleared?.Invoke(this, EventArgs.Empty);
            await SaveAsync();
        }

        public IReadOnlyList<MemoryEntry> GetAllMemories()
        {
            return _memoryData.Entries.AsReadOnly();
        }

        public IReadOnlyList<MemoryEntry> GetMemoriesByType(MemoryEntryType type)
        {
            return _memoryData.Entries.Where(e => e.Type == type).ToList().AsReadOnly();
        }

        public IReadOnlyList<MemoryEntry> GetHighConfidenceMemories(double minConfidence = 0.7)
        {
            return _memoryData.Entries.Where(e => e.Confidence >= minConfidence).ToList().AsReadOnly();
        }

        public async Task MarkAppliedAsync(string entryId)
        {
            var entry = _memoryData.Entries.FirstOrDefault(e => e.Id == entryId);
            if (entry == null) return;

            entry.ApplyCount++;
            entry.LastAppliedAt = DateTime.UtcNow;
            _isDirty = true;
            LogOperation("MarkApplied", entryId, $"Apply count: {entry.ApplyCount}");

            ScheduleAutoSave();
        }

        public async Task UpdateConfidenceAsync(string entryId, double newConfidence)
        {
            var entry = _memoryData.Entries.FirstOrDefault(e => e.Id == entryId);
            if (entry == null) return;

            var oldConfidence = entry.Confidence;
            entry.Confidence = Math.Clamp(newConfidence, 0.0, 1.0);
            _isDirty = true;
            LogOperation("UpdateConfidence", entryId, $"Confidence: {oldConfidence:F2} -> {entry.Confidence:F2}");

            ScheduleAutoSave();
        }


        private static (bool IsValid, string? Reason) ValidateEntry(MemoryEntry entry)
        {
            if (string.IsNullOrEmpty(entry.Id) || entry.Id.Length != 12)
                return (false, "Invalid ID format (must be 12 chars)");

            if (entry.Confidence < 0.0 || entry.Confidence > 1.0)
                return (false, "Confidence out of range [0.0, 1.0]");

            if (string.IsNullOrEmpty(entry.Note))
                return (false, "Note cannot be empty");

            if (entry.Note.Length > 500)
                return (false, "Note exceeds 500 character limit");

            if (ContainsPii(entry.Note))
                return (false, "Note contains PII patterns");

            return (true, null);
        }

        public static bool ContainsPii(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;

            foreach (var pattern in PiiPatterns)
            {
                if (pattern.IsMatch(text))
                    return true;
            }
            return false;
        }

        private void ScheduleAutoSave()
        {
            _autoSaveTimer?.Dispose();
            _autoSaveTimer = new Timer(async _ =>
            {
                if (_isDirty)
                {
                    await SaveAsync();
                }
            }, null, TimeSpan.FromSeconds(5), Timeout.InfiniteTimeSpan);
        }

        private void LogOperation(string operation, string? entryId, string? details)
        {
            var log = new MemoryOperationLog
            {
                Operation = operation,
                Timestamp = DateTime.UtcNow,
                Details = entryId != null ? $"[{entryId}] {details}" : details
            };
            _operationLogs.Add(log);

            if (_operationLogs.Count > 1000)
                _operationLogs.RemoveAt(0);

            System.Diagnostics.Debug.WriteLine($"[ProjectMemoryStore] {operation}: {log.Details}");
        }

        public IReadOnlyList<MemoryOperationLog> GetRecentLogs(int count = 100)
        {
            return _operationLogs.TakeLast(count).ToList().AsReadOnly();
        }
    }
}
