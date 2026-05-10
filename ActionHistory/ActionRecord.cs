using System;
using System.Collections.Generic;

namespace AtlasAI.ActionHistory
{
    public enum ActionType
    {
        Unknown,
        FileCreated,
        FileDeleted,
        FileMoved,
        FileCopied,
        FileRenamed,
        FolderCreated,
        FolderDeleted,
        FolderOrganized,
        AppOpened,
        AppClosed,
        ProcessKilled,
        RegistryChanged,
        SettingChanged,
        VolumeChanged,
        BrightnessChanged,
        SystemCommand,
        ScanPerformed,
        ThreatRemoved
    }
    
    public class ActionRecord
    {
        public string Id { get; set; } = "";
        public ActionType Type { get; set; }
        public string Description { get; set; } = "";
        public DateTime Timestamp { get; set; }
        public bool CanUndo { get; set; }
        public bool WasUndone { get; set; }
        public DateTime? UndoneAt { get; set; }
        
        // File operations
        public string? SourcePath { get; set; }
        public string? TargetPath { get; set; }
        public string? BackupPath { get; set; }
        
        // App operations
        public string? ProcessName { get; set; }
        public int? ProcessId { get; set; }
        
        // Registry operations
        public string? RegistryPath { get; set; }
        public string? RegistryValueName { get; set; }
        public string? OldValue { get; set; }
        public string? NewValue { get; set; }
        
        // Settings
        public string? SettingName { get; set; }
        
        // For folder organization - track all moves
        public List<ActionRecord>? SubActions { get; set; }
        public List<string>? CreatedFolders { get; set; }
        
        // User message that triggered this action
        public string? UserCommand { get; set; }
        
        /// <summary>
        /// Create a file created action
        /// </summary>
        public static ActionRecord FileCreated(string path, string? userCommand = null)
        {
            return new ActionRecord
            {
                Type = ActionType.FileCreated,
                Description = $"Created file: {System.IO.Path.GetFileName(path)}",
                TargetPath = path,
                CanUndo = true,
                UserCommand = userCommand
            };
        }
        
        /// <summary>
        /// Create a file deleted action with backup
        /// </summary>
        public static ActionRecord FileDeleted(string path, string backupPath, string? userCommand = null)
        {
            return new ActionRecord
            {
                Type = ActionType.FileDeleted,
                Description = $"Deleted file: {System.IO.Path.GetFileName(path)}",
                TargetPath = path,
                BackupPath = backupPath,
                CanUndo = !string.IsNullOrEmpty(backupPath),
                UserCommand = userCommand
            };
        }
        
        /// <summary>
        /// Create a file moved action
        /// </summary>
        public static ActionRecord FileMoved(string sourcePath, string targetPath, string? userCommand = null)
        {
            return new ActionRecord
            {
                Type = ActionType.FileMoved,
                Description = $"Moved: {System.IO.Path.GetFileName(sourcePath)}",
                SourcePath = sourcePath,
                TargetPath = targetPath,
                CanUndo = true,
                UserCommand = userCommand
            };
        }
        
        /// <summary>
        /// Create a file copied action
        /// </summary>
        public static ActionRecord FileCopied(string sourcePath, string targetPath, string? userCommand = null)
        {
            return new ActionRecord
            {
                Type = ActionType.FileCopied,
                Description = $"Copied: {System.IO.Path.GetFileName(sourcePath)}",
                SourcePath = sourcePath,
                TargetPath = targetPath,
                CanUndo = true,
                UserCommand = userCommand
            };
        }
        
        /// <summary>
        /// Create a file renamed action
        /// </summary>
        public static ActionRecord FileRenamed(string oldPath, string newPath, string? userCommand = null)
        {
            return new ActionRecord
            {
                Type = ActionType.FileRenamed,
                Description = $"Renamed: {System.IO.Path.GetFileName(oldPath)} → {System.IO.Path.GetFileName(newPath)}",
                SourcePath = oldPath,
                TargetPath = newPath,
                CanUndo = true,
                UserCommand = userCommand
            };
        }
        
        /// <summary>
        /// Create a folder organized action
        /// </summary>
        public static ActionRecord FolderOrganized(string folderPath, List<ActionRecord> moves, List<string> createdFolders, string? userCommand = null)
        {
            return new ActionRecord
            {
                Type = ActionType.FolderOrganized,
                Description = $"Organized folder: {System.IO.Path.GetFileName(folderPath)} ({moves.Count} files)",
                TargetPath = folderPath,
                SubActions = moves,
                CreatedFolders = createdFolders,
                CanUndo = true,
                UserCommand = userCommand
            };
        }
        
        /// <summary>
        /// Create an app opened action
        /// </summary>
        public static ActionRecord AppOpened(string appName, string? processName = null, int? processId = null, string? userCommand = null)
        {
            return new ActionRecord
            {
                Type = ActionType.AppOpened,
                Description = $"Opened: {appName}",
                ProcessName = processName ?? appName,
                ProcessId = processId,
                CanUndo = true,
                UserCommand = userCommand
            };
        }
        
        /// <summary>
        /// Create a volume changed action
        /// </summary>
        public static ActionRecord VolumeChanged(int oldVolume, int newVolume, string? userCommand = null)
        {
            return new ActionRecord
            {
                Type = ActionType.VolumeChanged,
                Description = $"Volume: {oldVolume}% → {newVolume}%",
                OldValue = oldVolume.ToString(),
                NewValue = newVolume.ToString(),
                CanUndo = true,
                UserCommand = userCommand
            };
        }
        
        /// <summary>
        /// Create a scan performed action (not undoable)
        /// </summary>
        public static ActionRecord ScanPerformed(string scanType, int threatsFound, string? userCommand = null)
        {
            return new ActionRecord
            {
                Type = ActionType.ScanPerformed,
                Description = $"{scanType} scan: {threatsFound} threats found",
                CanUndo = false,
                UserCommand = userCommand
            };
        }
    }
}
