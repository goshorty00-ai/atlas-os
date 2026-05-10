#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AtlasAI.Coding.Services
{
    /// <summary>
    /// Stage snapshot store - maintains before/after snapshots for each edited file.
    /// Enables reliable undo/rollback of agent stages.
    /// </summary>
    public class StageSnapshotStore
    {
        private static readonly string SnapshotDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AtlasAI", "stage_snapshots");
        
        private readonly List<Stage> _stages = new();
        private int _currentStageNumber = 0;
        private string _workspacePath = "";
        
        public static StageSnapshotStore Instance { get; } = new();
        
        // Events
        public event Action<Stage>? OnStageCreated;
        public event Action<Stage>? OnStageUndone;
        public event Action? OnStagesCleared;
        
        private StageSnapshotStore()
        {
            EnsureSnapshotDir();
        }
        
        public IReadOnlyList<Stage> Stages => _stages.AsReadOnly();
        public int CurrentStageNumber => _currentStageNumber;
        public bool HasStages => _stages.Count > 0;
        public bool CanUndo => _stages.Count > 0;
        
        /// <summary>
        /// Set the workspace path for relative file resolution.
        /// </summary>
        public void SetWorkspace(string workspacePath)
        {
            _workspacePath = workspacePath;
        }
        
        /// <summary>
        /// Begin a new stage - call before making changes.
        /// </summary>
        public Stage BeginStage(string description)
        {
            _currentStageNumber++;
            
            var stage = new Stage
            {
                Number = _currentStageNumber,
                Description = description,
                StartTime = DateTime.UtcNow,
                Status = StageStatus.InProgress
            };
            
            _stages.Add(stage);
            
            System.Diagnostics.Debug.WriteLine($"[StageSnapshot] Started Stage {stage.Number}: {description}");
            
            return stage;
        }
        
        /// <summary>
        /// Capture a file's state before modification.
        /// </summary>
        public void CaptureBeforeState(Stage stage, string filePath)
        {
            var fullPath = GetFullPath(filePath);
            var relativePath = GetRelativePath(filePath);
            
            // Check if we already have a snapshot for this file in this stage
            if (stage.FileSnapshots.Any(s => s.RelativePath == relativePath))
            {
                return; // Already captured
            }
            
            var snapshot = new FileSnapshot
            {
                RelativePath = relativePath,
                FullPath = fullPath,
                ExistedBefore = File.Exists(fullPath)
            };
            
            if (snapshot.ExistedBefore)
            {
                snapshot.BeforeContent = File.ReadAllText(fullPath);
                snapshot.BeforeHash = ComputeHash(snapshot.BeforeContent);
                snapshot.BeforeTimestamp = File.GetLastWriteTimeUtc(fullPath);
            }
            
            stage.FileSnapshots.Add(snapshot);
            
            System.Diagnostics.Debug.WriteLine($"[StageSnapshot] Captured before state: {relativePath}");
        }
        
        /// <summary>
        /// Capture a file's state after modification.
        /// </summary>
        public void CaptureAfterState(Stage stage, string filePath)
        {
            var relativePath = GetRelativePath(filePath);
            var fullPath = GetFullPath(filePath);
            
            var snapshot = stage.FileSnapshots.FirstOrDefault(s => s.RelativePath == relativePath);
            if (snapshot == null)
            {
                // File was created without capturing before state
                snapshot = new FileSnapshot
                {
                    RelativePath = relativePath,
                    FullPath = fullPath,
                    ExistedBefore = false
                };
                stage.FileSnapshots.Add(snapshot);
            }
            
            snapshot.ExistsAfter = File.Exists(fullPath);
            
            if (snapshot.ExistsAfter)
            {
                snapshot.AfterContent = File.ReadAllText(fullPath);
                snapshot.AfterHash = ComputeHash(snapshot.AfterContent);
                snapshot.AfterTimestamp = File.GetLastWriteTimeUtc(fullPath);
            }
            
            System.Diagnostics.Debug.WriteLine($"[StageSnapshot] Captured after state: {relativePath}");
        }
        
        /// <summary>
        /// Complete a stage - marks it as applied.
        /// </summary>
        public void CompleteStage(Stage stage, VerificationResult? verificationResult = null)
        {
            stage.EndTime = DateTime.UtcNow;
            stage.Status = StageStatus.Applied;
            stage.VerificationResult = verificationResult;
            
            // Persist the stage
            SaveStage(stage);
            
            OnStageCreated?.Invoke(stage);
            
            System.Diagnostics.Debug.WriteLine($"[StageSnapshot] Completed Stage {stage.Number}");
        }
        
        /// <summary>
        /// Mark a stage as failed.
        /// </summary>
        public void FailStage(Stage stage, string error)
        {
            stage.EndTime = DateTime.UtcNow;
            stage.Status = StageStatus.Failed;
            stage.ErrorMessage = error;
            
            System.Diagnostics.Debug.WriteLine($"[StageSnapshot] Failed Stage {stage.Number}: {error}");
        }
        
        /// <summary>
        /// Undo the last stage - restores all files to their before state.
        /// </summary>
        public UndoResult UndoLastStage()
        {
            if (_stages.Count == 0)
            {
                return new UndoResult { Success = false, ErrorMessage = "No stages to undo" };
            }
            
            var stage = _stages[^1];
            return UndoStage(stage);
        }
        
        /// <summary>
        /// Undo a specific stage by number.
        /// </summary>
        public UndoResult UndoStage(int stageNumber)
        {
            var stage = _stages.FirstOrDefault(s => s.Number == stageNumber);
            if (stage == null)
            {
                return new UndoResult { Success = false, ErrorMessage = $"Stage {stageNumber} not found" };
            }
            
            return UndoStage(stage);
        }
        
        /// <summary>
        /// Undo a specific stage.
        /// </summary>
        public UndoResult UndoStage(Stage stage)
        {
            var result = new UndoResult { StageNumber = stage.Number };
            
            try
            {
                // Check for external modifications first
                var integrityCheck = CheckStageIntegrity(stage);
                if (!integrityCheck.IsValid)
                {
                    result.Success = false;
                    result.ErrorMessage = integrityCheck.ErrorMessage;
                    result.ConflictingFiles = integrityCheck.ConflictingFiles;
                    result.RequiresRebase = true;
                    return result;
                }
                
                // Restore each file to its before state
                foreach (var snapshot in stage.FileSnapshots)
                {
                    RestoreFile(snapshot);
                    result.RestoredFiles.Add(snapshot.RelativePath);
                }
                
                // Remove this stage and all stages after it
                var stageIndex = _stages.IndexOf(stage);
                if (stageIndex >= 0)
                {
                    _stages.RemoveRange(stageIndex, _stages.Count - stageIndex);
                }
                
                // Update stage number
                _currentStageNumber = _stages.Count > 0 ? _stages[^1].Number : 0;
                
                // Delete persisted stage file
                DeleteStagePersistence(stage);
                
                result.Success = true;
                OnStageUndone?.Invoke(stage);
                
                System.Diagnostics.Debug.WriteLine($"[StageSnapshot] Undone Stage {stage.Number}");
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
                System.Diagnostics.Debug.WriteLine($"[StageSnapshot] Undo failed: {ex.Message}");
            }
            
            return result;
        }
        
        /// <summary>
        /// Check if a stage can be safely applied/undone (no external modifications).
        /// </summary>
        public IntegrityCheckResult CheckStageIntegrity(Stage stage)
        {
            var result = new IntegrityCheckResult { IsValid = true };
            
            foreach (var snapshot in stage.FileSnapshots)
            {
                var fullPath = GetFullPath(snapshot.RelativePath);
                
                if (!File.Exists(fullPath))
                {
                    // File was deleted externally
                    if (snapshot.ExistsAfter)
                    {
                        result.IsValid = false;
                        result.ConflictingFiles.Add(new FileConflict
                        {
                            FilePath = snapshot.RelativePath,
                            ConflictType = ConflictType.DeletedExternally,
                            Message = "File was deleted externally since stage was applied"
                        });
                    }
                    continue;
                }
                
                var currentContent = File.ReadAllText(fullPath);
                var currentHash = ComputeHash(currentContent);
                
                // Check if file matches expected after state
                if (snapshot.AfterHash != null && currentHash != snapshot.AfterHash)
                {
                    result.IsValid = false;
                    result.ConflictingFiles.Add(new FileConflict
                    {
                        FilePath = snapshot.RelativePath,
                        ConflictType = ConflictType.ModifiedExternally,
                        Message = "File was modified externally since stage was applied",
                        ExpectedHash = snapshot.AfterHash,
                        ActualHash = currentHash
                    });
                }
            }
            
            if (!result.IsValid)
            {
                result.ErrorMessage = $"{result.ConflictingFiles.Count} file(s) have been modified externally. Rebase required.";
            }
            
            return result;
        }
        
        /// <summary>
        /// Rebase a stage - re-capture current state as the new "after" state.
        /// </summary>
        public void RebaseStage(Stage stage)
        {
            foreach (var snapshot in stage.FileSnapshots)
            {
                var fullPath = GetFullPath(snapshot.RelativePath);
                
                if (File.Exists(fullPath))
                {
                    snapshot.AfterContent = File.ReadAllText(fullPath);
                    snapshot.AfterHash = ComputeHash(snapshot.AfterContent);
                    snapshot.AfterTimestamp = File.GetLastWriteTimeUtc(fullPath);
                    snapshot.ExistsAfter = true;
                }
                else
                {
                    snapshot.AfterContent = null;
                    snapshot.AfterHash = null;
                    snapshot.ExistsAfter = false;
                }
            }
            
            SaveStage(stage);
            
            System.Diagnostics.Debug.WriteLine($"[StageSnapshot] Rebased Stage {stage.Number}");
        }
        
        /// <summary>
        /// Clear all stages (e.g., when starting a new task).
        /// </summary>
        public void ClearAllStages()
        {
            foreach (var stage in _stages)
            {
                DeleteStagePersistence(stage);
            }
            
            _stages.Clear();
            _currentStageNumber = 0;
            
            OnStagesCleared?.Invoke();
            
            System.Diagnostics.Debug.WriteLine("[StageSnapshot] Cleared all stages");
        }
        
        /// <summary>
        /// Get a summary of all stages for display.
        /// </summary>
        public List<StageSummary> GetStageSummaries()
        {
            return _stages.Select(s => new StageSummary
            {
                Number = s.Number,
                Description = s.Description,
                Status = s.Status,
                FileCount = s.FileSnapshots.Count,
                TouchedFiles = s.FileSnapshots.Select(f => f.RelativePath).ToList(),
                StartTime = s.StartTime,
                EndTime = s.EndTime,
                Duration = s.EndTime.HasValue ? s.EndTime.Value - s.StartTime : TimeSpan.Zero,
                VerificationPassed = s.VerificationResult?.Success
            }).ToList();
        }
        
        #region Private Helpers
        
        private void RestoreFile(FileSnapshot snapshot)
        {
            var fullPath = GetFullPath(snapshot.RelativePath);
            
            if (snapshot.ExistedBefore)
            {
                // Restore original content
                var dir = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                
                File.WriteAllText(fullPath, snapshot.BeforeContent ?? "");
                
                System.Diagnostics.Debug.WriteLine($"[StageSnapshot] Restored: {snapshot.RelativePath}");
            }
            else
            {
                // File didn't exist before - delete it
                if (File.Exists(fullPath))
                {
                    File.Delete(fullPath);
                    System.Diagnostics.Debug.WriteLine($"[StageSnapshot] Deleted (was created): {snapshot.RelativePath}");
                }
            }
        }
        
        private string GetFullPath(string path)
        {
            if (Path.IsPathRooted(path))
                return path;
            
            return Path.Combine(_workspacePath, path);
        }
        
        private string GetRelativePath(string path)
        {
            if (!Path.IsPathRooted(path))
                return path;
            
            if (!string.IsNullOrEmpty(_workspacePath) && path.StartsWith(_workspacePath))
            {
                return Path.GetRelativePath(_workspacePath, path);
            }
            
            return path;
        }
        
        private string ComputeHash(string content)
        {
            using var sha256 = SHA256.Create();
            var bytes = Encoding.UTF8.GetBytes(content);
            var hash = sha256.ComputeHash(bytes);
            return Convert.ToHexString(hash);
        }
        
        private void EnsureSnapshotDir()
        {
            if (!Directory.Exists(SnapshotDir))
            {
                Directory.CreateDirectory(SnapshotDir);
            }
        }
        
        private void SaveStage(Stage stage)
        {
            try
            {
                var filePath = Path.Combine(SnapshotDir, $"stage_{stage.Number}.json");
                var json = JsonSerializer.Serialize(stage, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(filePath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[StageSnapshot] Save error: {ex.Message}");
            }
        }
        
        private void DeleteStagePersistence(Stage stage)
        {
            try
            {
                var filePath = Path.Combine(SnapshotDir, $"stage_{stage.Number}.json");
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[StageSnapshot] Delete error: {ex.Message}");
            }
        }
        
        #endregion
    }
    
    #region Models
    
    public class Stage
    {
        [JsonPropertyName("number")]
        public int Number { get; set; }
        
        [JsonPropertyName("description")]
        public string Description { get; set; } = "";
        
        [JsonPropertyName("status")]
        public StageStatus Status { get; set; }
        
        [JsonPropertyName("startTime")]
        public DateTime StartTime { get; set; }
        
        [JsonPropertyName("endTime")]
        public DateTime? EndTime { get; set; }
        
        [JsonPropertyName("fileSnapshots")]
        public List<FileSnapshot> FileSnapshots { get; set; } = new();
        
        [JsonPropertyName("verificationResult")]
        public VerificationResult? VerificationResult { get; set; }
        
        [JsonPropertyName("errorMessage")]
        public string? ErrorMessage { get; set; }
    }
    
    public enum StageStatus
    {
        InProgress,
        Applied,
        Failed,
        Undone
    }
    
    public class FileSnapshot
    {
        [JsonPropertyName("relativePath")]
        public string RelativePath { get; set; } = "";
        
        [JsonPropertyName("fullPath")]
        public string FullPath { get; set; } = "";
        
        [JsonPropertyName("existedBefore")]
        public bool ExistedBefore { get; set; }
        
        [JsonPropertyName("existsAfter")]
        public bool ExistsAfter { get; set; }
        
        [JsonPropertyName("beforeContent")]
        public string? BeforeContent { get; set; }
        
        [JsonPropertyName("afterContent")]
        public string? AfterContent { get; set; }
        
        [JsonPropertyName("beforeHash")]
        public string? BeforeHash { get; set; }
        
        [JsonPropertyName("afterHash")]
        public string? AfterHash { get; set; }
        
        [JsonPropertyName("beforeTimestamp")]
        public DateTime? BeforeTimestamp { get; set; }
        
        [JsonPropertyName("afterTimestamp")]
        public DateTime? AfterTimestamp { get; set; }
    }
    
    public class UndoResult
    {
        public bool Success { get; set; }
        public int StageNumber { get; set; }
        public string? ErrorMessage { get; set; }
        public List<string> RestoredFiles { get; set; } = new();
        public List<FileConflict> ConflictingFiles { get; set; } = new();
        public bool RequiresRebase { get; set; }
    }
    
    public class IntegrityCheckResult
    {
        public bool IsValid { get; set; }
        public string? ErrorMessage { get; set; }
        public List<FileConflict> ConflictingFiles { get; set; } = new();
    }
    
    public class FileConflict
    {
        public string FilePath { get; set; } = "";
        public ConflictType ConflictType { get; set; }
        public string Message { get; set; } = "";
        public string? ExpectedHash { get; set; }
        public string? ActualHash { get; set; }
    }
    
    public enum ConflictType
    {
        ModifiedExternally,
        DeletedExternally,
        CreatedExternally
    }
    
    public class StageSummary
    {
        public int Number { get; set; }
        public string Description { get; set; } = "";
        public StageStatus Status { get; set; }
        public int FileCount { get; set; }
        public List<string> TouchedFiles { get; set; } = new();
        public DateTime StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public TimeSpan Duration { get; set; }
        public bool? VerificationPassed { get; set; }
    }
    
    #endregion
}
