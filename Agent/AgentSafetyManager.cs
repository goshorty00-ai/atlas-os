using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace AtlasAI.Agent
{
    /// <summary>
    /// Manages safety features for the AI agent - restore points, undo stack, previews
    /// </summary>
    public class AgentSafetyManager
    {
        private static readonly Lazy<AgentSafetyManager> _instance = new(() => new AgentSafetyManager());
        public static AgentSafetyManager Instance => _instance.Value;
        
        private readonly Stack<AgentUndoAction> _undoStack = new();
        private readonly string _backupFolder;
        private const int MaxUndoActions = 50;
        
        public event Action<string>? OnStatusUpdate;
        
        private AgentSafetyManager()
        {
            _backupFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "AtlasAI", "AgentBackups");
            Directory.CreateDirectory(_backupFolder);
            CleanOldBackups();
        }
        
        /// <summary>
        /// Create a Windows System Restore Point before major operations
        /// </summary>
        public async Task<(bool Success, string Message)> CreateRestorePointAsync(string description)
        {
            try
            {
                OnStatusUpdate?.Invoke("Creating system restore point...");
                
                var script = $@"
                    $description = 'Atlas AI: {description.Replace("'", "''")}'
                    Checkpoint-Computer -Description $description -RestorePointType 'APPLICATION_INSTALL' -ErrorAction Stop
                    Write-Output 'SUCCESS'
                ";
                
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{script}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    Verb = "runas" // Request admin
                };
                
                using var process = Process.Start(psi);
                if (process == null)
                    return (false, "Failed to start PowerShell");
                
                var output = await process.StandardOutput.ReadToEndAsync();
                var error = await process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();
                
                if (output.Contains("SUCCESS"))
                {
                    Debug.WriteLine($"[Safety] Restore point created: {description}");
                    return (true, $"✅ Restore point created: {description}");
                }
                
                // Restore points might be disabled or rate-limited
                if (error.Contains("disabled") || error.Contains("frequency"))
                {
                    Debug.WriteLine($"[Safety] Restore point skipped (disabled/rate-limited)");
                    return (true, "⚠️ System restore points are disabled or rate-limited");
                }
                
                return (false, $"❌ Failed: {error}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Safety] Restore point error: {ex.Message}");
                return (false, $"❌ Error: {ex.Message}");
            }
        }

        /// <summary>
        /// Backup a file before modifying it
        /// </summary>
        public async Task<string?> BackupFileAsync(string filePath)
        {
            try
            {
                if (!File.Exists(filePath))
                    return null;
                
                var fileName = Path.GetFileName(filePath);
                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                var backupName = $"{timestamp}_{fileName}";
                var backupPath = Path.Combine(_backupFolder, backupName);
                
                await Task.Run(() => File.Copy(filePath, backupPath, true));
                Debug.WriteLine($"[Safety] Backed up: {filePath} -> {backupPath}");
                
                return backupPath;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Safety] Backup failed: {ex.Message}");
                return null;
            }
        }
        
        /// <summary>
        /// Push an action onto the undo stack
        /// </summary>
        public void PushUndo(AgentUndoAction action)
        {
            action.Timestamp = DateTime.Now;
            _undoStack.Push(action);
            
            // Trim stack if too large
            while (_undoStack.Count > MaxUndoActions)
            {
                var old = _undoStack.ToArray();
                _undoStack.Clear();
                foreach (var a in old.Take(MaxUndoActions))
                    _undoStack.Push(a);
            }
            
            Debug.WriteLine($"[Safety] Undo pushed: {action.Description} (stack size: {_undoStack.Count})");
        }
        
        /// <summary>
        /// Undo the last action
        /// </summary>
        public async Task<(bool Success, string Message)> UndoLastAsync()
        {
            if (_undoStack.Count == 0)
                return (false, "❌ Nothing to undo");
            
            var action = _undoStack.Pop();
            OnStatusUpdate?.Invoke($"Undoing: {action.Description}...");
            
            try
            {
                switch (action.Type)
                {
                    case UndoType.FileCreated:
                        // Delete the file that was created
                        if (File.Exists(action.TargetPath))
                        {
                            File.Delete(action.TargetPath);
                            return (true, $"✅ Undone: Deleted created file `{Path.GetFileName(action.TargetPath)}`");
                        }
                        return (false, "File no longer exists");
                        
                    case UndoType.FileModified:
                        // Restore from backup
                        if (!string.IsNullOrEmpty(action.BackupPath) && File.Exists(action.BackupPath))
                        {
                            File.Copy(action.BackupPath, action.TargetPath, true);
                            return (true, $"✅ Undone: Restored `{Path.GetFileName(action.TargetPath)}` to previous version");
                        }
                        else if (!string.IsNullOrEmpty(action.OriginalContent))
                        {
                            await File.WriteAllTextAsync(action.TargetPath, action.OriginalContent);
                            return (true, $"✅ Undone: Restored `{Path.GetFileName(action.TargetPath)}`");
                        }
                        return (false, "No backup available");
                        
                    case UndoType.FileDeleted:
                        // Restore deleted file
                        if (!string.IsNullOrEmpty(action.OriginalContent))
                        {
                            var dir = Path.GetDirectoryName(action.TargetPath);
                            if (!string.IsNullOrEmpty(dir))
                                Directory.CreateDirectory(dir);
                            await File.WriteAllTextAsync(action.TargetPath, action.OriginalContent);
                            return (true, $"✅ Undone: Restored deleted file `{Path.GetFileName(action.TargetPath)}`");
                        }
                        else if (!string.IsNullOrEmpty(action.BackupPath) && File.Exists(action.BackupPath))
                        {
                            File.Copy(action.BackupPath, action.TargetPath);
                            return (true, $"✅ Undone: Restored deleted file `{Path.GetFileName(action.TargetPath)}`");
                        }
                        return (false, "No backup available");
                        
                    case UndoType.DirectoryCreated:
                        if (Directory.Exists(action.TargetPath) && 
                            !Directory.EnumerateFileSystemEntries(action.TargetPath).Any())
                        {
                            Directory.Delete(action.TargetPath);
                            return (true, $"✅ Undone: Removed created directory `{action.TargetPath}`");
                        }
                        return (false, "Directory not empty or doesn't exist");
                        
                    case UndoType.SoftwareInstalled:
                        // Can't auto-uninstall - just inform user
                        return (false, $"⚠️ Cannot auto-uninstall {action.Description}. Use 'uninstall {action.TargetPath}' manually.");
                        
                    case UndoType.CommandExecuted:
                        return (false, $"⚠️ Cannot undo command execution: {action.Description}");
                        
                    default:
                        return (false, "Unknown undo type");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Safety] Undo failed: {ex.Message}");
                return (false, $"❌ Undo failed: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Undo multiple actions
        /// </summary>
        public async Task<string> UndoMultipleAsync(int count)
        {
            var results = new List<string>();
            for (int i = 0; i < count && _undoStack.Count > 0; i++)
            {
                var (success, message) = await UndoLastAsync();
                results.Add(message);
                if (!success) break;
            }
            return string.Join("\n", results);
        }
        
        /// <summary>
        /// Get undo stack summary
        /// </summary>
        public string GetUndoSummary()
        {
            if (_undoStack.Count == 0)
                return "No actions to undo.";
            
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"📋 **Undo Stack ({_undoStack.Count} actions):**\n");
            
            int i = 1;
            foreach (var action in _undoStack.Take(10))
            {
                var time = action.Timestamp.ToString("HH:mm:ss");
                sb.AppendLine($"{i}. [{time}] {action.Description}");
                i++;
            }
            
            if (_undoStack.Count > 10)
                sb.AppendLine($"... and {_undoStack.Count - 10} more");
            
            return sb.ToString();
        }
        
        /// <summary>
        /// Check if there are actions to undo
        /// </summary>
        public bool CanUndo => _undoStack.Count > 0;
        
        /// <summary>
        /// Get count of undoable actions
        /// </summary>
        public int UndoCount => _undoStack.Count;
        
        /// <summary>
        /// Track a file creation for undo
        /// </summary>
        public void TrackFileCreation(string filePath)
        {
            PushUndo(new AgentUndoAction
            {
                Type = UndoType.FileCreated,
                Description = $"Created file: {Path.GetFileName(filePath)}",
                TargetPath = filePath
            });
        }
        
        /// <summary>
        /// Track a file move for undo
        /// </summary>
        public void TrackFileMove(string sourcePath, string destPath)
        {
            PushUndo(new AgentUndoAction
            {
                Type = UndoType.FileModified,
                Description = $"Moved: {Path.GetFileName(sourcePath)} -> {Path.GetFileName(destPath)}",
                TargetPath = destPath,
                Metadata = new Dictionary<string, object> { { "originalPath", sourcePath } }
            });
        }
        
        /// <summary>
        /// Track a file deletion for undo
        /// </summary>
        public void TrackFileDeletion(string filePath, string? backupPath = null)
        {
            PushUndo(new AgentUndoAction
            {
                Type = UndoType.FileDeleted,
                Description = $"Deleted file: {Path.GetFileName(filePath)}",
                TargetPath = filePath,
                BackupPath = backupPath
            });
        }

        /// <summary>
        /// Preview what an operation will do (dry run)
        /// </summary>
        public string PreviewOperation(string tool, Dictionary<string, object> parameters, string workspace)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("📋 **Preview - This operation will:**\n");
            
            switch (tool.ToLower())
            {
                case "write_file":
                    var path = parameters.GetValueOrDefault("path")?.ToString() ?? "";
                    var content = parameters.GetValueOrDefault("content")?.ToString() ?? "";
                    var fullPath = Path.Combine(workspace, path);
                    var exists = File.Exists(fullPath);
                    
                    sb.AppendLine(exists 
                        ? $"📝 **Overwrite** existing file: `{path}`"
                        : $"📝 **Create** new file: `{path}`");
                    sb.AppendLine($"   Size: {content.Length} characters");
                    if (exists)
                        sb.AppendLine($"   ⚠️ Current file will be backed up");
                    sb.AppendLine($"\n✅ **Can be undone**");
                    break;
                    
                case "delete_file":
                    var delPath = parameters.GetValueOrDefault("path")?.ToString() ?? "";
                    sb.AppendLine($"🗑️ **Delete** file: `{delPath}`");
                    sb.AppendLine($"   ⚠️ File content will be backed up");
                    sb.AppendLine($"\n✅ **Can be undone**");
                    break;
                    
                case "install_software":
                    var name = parameters.GetValueOrDefault("name")?.ToString() ?? "";
                    sb.AppendLine($"📦 **Install** software: `{name}`");
                    sb.AppendLine($"   Will use: winget, pip, npm, or choco");
                    sb.AppendLine($"\n⚠️ **Cannot be auto-undone** (use uninstall command)");
                    break;
                    
                case "uninstall_software":
                case "uninstall":
                    var uninstallName = parameters.GetValueOrDefault("name")?.ToString() ?? "";
                    sb.AppendLine($"🗑️ **Uninstall** software: `{uninstallName}`");
                    sb.AppendLine($"\n❌ **Cannot be undone**");
                    break;
                    
                case "run_command":
                    var cmd = parameters.GetValueOrDefault("command")?.ToString() ?? "";
                    sb.AppendLine($"⚡ **Execute** command:");
                    sb.AppendLine($"   `{cmd}`");
                    sb.AppendLine($"\n⚠️ **May not be undoable** depending on command");
                    break;
                    
                case "run_powershell":
                    var script = parameters.GetValueOrDefault("script")?.ToString() ?? "";
                    var preview = script.Length > 100 ? script.Substring(0, 100) + "..." : script;
                    sb.AppendLine($"⚡ **Execute** PowerShell:");
                    sb.AppendLine($"   `{preview}`");
                    sb.AppendLine($"\n⚠️ **May not be undoable** depending on script");
                    break;
                    
                case "create_directory":
                    var dirPath = parameters.GetValueOrDefault("path")?.ToString() ?? "";
                    sb.AppendLine($"📁 **Create** directory: `{dirPath}`");
                    sb.AppendLine($"\n✅ **Can be undone** (if empty)");
                    break;
                    
                default:
                    sb.AppendLine($"🔧 **Execute** tool: `{tool}`");
                    foreach (var p in parameters)
                        sb.AppendLine($"   {p.Key}: {p.Value}");
                    break;
            }
            
            return sb.ToString();
        }
        
        /// <summary>
        /// Determine risk level of an operation
        /// </summary>
        public RiskLevel AssessRisk(string tool, Dictionary<string, object> parameters)
        {
            return tool.ToLower() switch
            {
                "read_file" or "list_directory" or "search_files" or 
                "search_content" or "get_file_info" or "check_installed" or
                "explain_code" => RiskLevel.Safe,
                
                "write_file" or "append_file" or "create_directory" or
                "move_file" or "generate_code" or "modify_code" or
                "fix_code" or "refactor_code" or "generate_tests" or
                "create_code_file" => RiskLevel.Low,
                
                "delete_file" or "install_software" => RiskLevel.Medium,
                
                "uninstall_software" or "uninstall" => RiskLevel.High,
                
                "run_command" or "run_powershell" => AssessCommandRisk(parameters),
                
                _ => RiskLevel.Medium
            };
        }
        
        private RiskLevel AssessCommandRisk(Dictionary<string, object> parameters)
        {
            var cmd = (parameters.GetValueOrDefault("command")?.ToString() ?? 
                      parameters.GetValueOrDefault("script")?.ToString() ?? "").ToLower();
            
            // High risk commands
            var highRisk = new[] { "rm ", "del ", "format", "shutdown", "restart", 
                "reg delete", "remove-item -recurse", "stop-service", "uninstall" };
            if (highRisk.Any(h => cmd.Contains(h)))
                return RiskLevel.High;
            
            // Medium risk
            var mediumRisk = new[] { "install", "pip ", "npm ", "choco ", "winget ", 
                "set-", "new-", "reg add", "start-service" };
            if (mediumRisk.Any(m => cmd.Contains(m)))
                return RiskLevel.Medium;
            
            // Low risk (read-only commands)
            var lowRisk = new[] { "get-", "dir", "ls", "cat", "type", "echo", 
                "where", "which", "dotnet --version", "python --version" };
            if (lowRisk.Any(l => cmd.Contains(l)))
                return RiskLevel.Low;
            
            return RiskLevel.Medium;
        }
        
        /// <summary>
        /// Clean up old backup files (older than 7 days)
        /// </summary>
        private void CleanOldBackups()
        {
            try
            {
                var cutoff = DateTime.Now.AddDays(-7);
                foreach (var file in Directory.GetFiles(_backupFolder))
                {
                    var info = new FileInfo(file);
                    if (info.CreationTime < cutoff)
                    {
                        File.Delete(file);
                        Debug.WriteLine($"[Safety] Cleaned old backup: {file}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Safety] Cleanup error: {ex.Message}");
            }
        }
    }
    
    /// <summary>
    /// Represents an action that can be undone
    /// </summary>
    public class AgentUndoAction
    {
        public UndoType Type { get; set; }
        public string Description { get; set; } = "";
        public string TargetPath { get; set; } = "";
        public string? BackupPath { get; set; }
        public string? OriginalContent { get; set; }
        public DateTime Timestamp { get; set; }
        public Dictionary<string, object> Metadata { get; set; } = new();
    }
    
    public enum UndoType
    {
        FileCreated,
        FileModified,
        FileDeleted,
        DirectoryCreated,
        SoftwareInstalled,
        CommandExecuted
    }
    
    public enum RiskLevel
    {
        Safe,    // Read-only operations
        Low,     // File creation/modification (undoable)
        Medium,  // Installations, deletions (partially undoable)
        High     // System changes, uninstalls (not undoable)
    }
}
