using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace AtlasAI.ActionHistory
{
    /// <summary>
    /// Tracks actions performed by Atlas and allows undoing them
    /// </summary>
    public class ActionHistoryManager
    {
        private static readonly Lazy<ActionHistoryManager> _instance = new(() => new ActionHistoryManager());
        public static ActionHistoryManager Instance => _instance.Value;
        
        private readonly List<ActionRecord> _history = new();
        private readonly string _historyPath;
        private const int MaxHistorySize = 100;
        
        public event Action<ActionRecord>? ActionRecorded;
        public event Action<ActionRecord>? ActionUndone;
        
        public IReadOnlyList<ActionRecord> History => _history.AsReadOnly();
        public bool CanUndo => _history.Any(a => a.CanUndo && !a.WasUndone);
        
        private ActionHistoryManager()
        {
            var appData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "AtlasAI");
            Directory.CreateDirectory(appData);
            _historyPath = Path.Combine(appData, "action_history.json");
            LoadHistory();
        }
        
        /// <summary>
        /// Record an action that was performed
        /// </summary>
        public void RecordAction(ActionRecord action)
        {
            action.Timestamp = DateTime.Now;
            action.Id = Guid.NewGuid().ToString();
            _history.Insert(0, action);
            
            // Trim history if too large
            while (_history.Count > MaxHistorySize)
                _history.RemoveAt(_history.Count - 1);
            
            SaveHistory();
            ActionRecorded?.Invoke(action);
            Debug.WriteLine($"[ActionHistory] Recorded: {action.Type} - {action.Description}");
        }
        
        /// <summary>
        /// Undo the most recent undoable action
        /// </summary>
        public async Task<string> UndoLastActionAsync()
        {
            var action = _history.FirstOrDefault(a => a.CanUndo && !a.WasUndone);
            if (action == null)
                return "❌ Nothing to undo.";
            
            return await UndoActionAsync(action);
        }
        
        /// <summary>
        /// Undo a specific action by ID
        /// </summary>
        public async Task<string> UndoActionAsync(string actionId)
        {
            var action = _history.FirstOrDefault(a => a.Id == actionId);
            if (action == null)
                return "❌ Action not found.";
            
            return await UndoActionAsync(action);
        }
        
        /// <summary>
        /// Undo a specific action
        /// </summary>
        public async Task<string> UndoActionAsync(ActionRecord action)
        {
            if (!action.CanUndo)
                return $"❌ Cannot undo: {action.Description}";
            
            if (action.WasUndone)
                return $"⚠️ Already undone: {action.Description}";
            
            try
            {
                string result = action.Type switch
                {
                    ActionType.FileCreated => await UndoFileCreated(action),
                    ActionType.FileDeleted => await UndoFileDeleted(action),
                    ActionType.FileMoved => await UndoFileMoved(action),
                    ActionType.FileCopied => await UndoFileCopied(action),
                    ActionType.FileRenamed => await UndoFileRenamed(action),
                    ActionType.FolderCreated => await UndoFolderCreated(action),
                    ActionType.FolderDeleted => await UndoFolderDeleted(action),
                    ActionType.FolderOrganized => await UndoFolderOrganized(action),
                    ActionType.AppOpened => await UndoAppOpened(action),
                    ActionType.RegistryChanged => await UndoRegistryChanged(action),
                    ActionType.SettingChanged => await UndoSettingChanged(action),
                    ActionType.ProcessKilled => "⚠️ Cannot restart killed process",
                    _ => $"⚠️ Unknown action type: {action.Type}"
                };
                
                action.WasUndone = true;
                action.UndoneAt = DateTime.Now;
                SaveHistory();
                ActionUndone?.Invoke(action);
                
                return result;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ActionHistory] Undo failed: {ex.Message}");
                return $"❌ Undo failed: {ex.Message}";
            }
        }
        
        /// <summary>
        /// Get recent actions that can be undone
        /// </summary>
        public List<ActionRecord> GetUndoableActions(int count = 10)
        {
            return _history
                .Where(a => a.CanUndo && !a.WasUndone)
                .Take(count)
                .ToList();
        }
        
        /// <summary>
        /// Clear all history
        /// </summary>
        public void ClearHistory()
        {
            _history.Clear();
            SaveHistory();
        }

        #region Undo Implementations
        
        private async Task<string> UndoFileCreated(ActionRecord action)
        {
            var path = action.TargetPath;
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return "⚠️ File no longer exists";
            
            // Backup before deleting
            var backupPath = CreateBackup(path);
            File.Delete(path);
            
            return $"✅ Undone: Deleted created file\n📁 {Path.GetFileName(path)}\n💾 Backup: {backupPath}";
        }
        
        private async Task<string> UndoFileDeleted(ActionRecord action)
        {
            var path = action.TargetPath;
            var backupPath = action.BackupPath;
            
            if (string.IsNullOrEmpty(backupPath) || !File.Exists(backupPath))
                return "❌ Cannot restore: No backup available";
            
            // Restore from backup
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            
            File.Copy(backupPath, path!, true);
            return $"✅ Undone: Restored deleted file\n📁 {Path.GetFileName(path)}";
        }
        
        private async Task<string> UndoFileMoved(ActionRecord action)
        {
            var sourcePath = action.SourcePath;
            var targetPath = action.TargetPath;
            
            if (string.IsNullOrEmpty(targetPath) || !File.Exists(targetPath))
                return "⚠️ File no longer exists at destination";
            
            if (string.IsNullOrEmpty(sourcePath))
                return "❌ Original location unknown";
            
            // Move back
            var dir = Path.GetDirectoryName(sourcePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            
            File.Move(targetPath, sourcePath, true);
            return $"✅ Undone: Moved file back\n📁 {Path.GetFileName(sourcePath)}";
        }
        
        private async Task<string> UndoFileCopied(ActionRecord action)
        {
            var targetPath = action.TargetPath;
            
            if (string.IsNullOrEmpty(targetPath) || !File.Exists(targetPath))
                return "⚠️ Copied file no longer exists";
            
            File.Delete(targetPath);
            return $"✅ Undone: Deleted copied file\n📁 {Path.GetFileName(targetPath)}";
        }
        
        private async Task<string> UndoFileRenamed(ActionRecord action)
        {
            var oldPath = action.SourcePath;
            var newPath = action.TargetPath;
            
            if (string.IsNullOrEmpty(newPath) || !File.Exists(newPath))
                return "⚠️ File no longer exists";
            
            if (string.IsNullOrEmpty(oldPath))
                return "❌ Original name unknown";
            
            File.Move(newPath, oldPath, true);
            return $"✅ Undone: Renamed back to\n📁 {Path.GetFileName(oldPath)}";
        }
        
        private async Task<string> UndoFolderCreated(ActionRecord action)
        {
            var path = action.TargetPath;
            if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
                return "⚠️ Folder no longer exists";
            
            // Only delete if empty
            if (Directory.GetFileSystemEntries(path).Length > 0)
                return "⚠️ Cannot delete: Folder is not empty";
            
            Directory.Delete(path);
            return $"✅ Undone: Deleted created folder\n📁 {Path.GetFileName(path)}";
        }
        
        private async Task<string> UndoFolderDeleted(ActionRecord action)
        {
            var path = action.TargetPath;
            var backupPath = action.BackupPath;
            
            if (string.IsNullOrEmpty(backupPath) || !Directory.Exists(backupPath))
                return "❌ Cannot restore: No backup available";
            
            // Restore from backup
            CopyDirectory(backupPath, path!);
            return $"✅ Undone: Restored deleted folder\n📁 {Path.GetFileName(path)}";
        }
        
        private async Task<string> UndoFolderOrganized(ActionRecord action)
        {
            // This requires the detailed move records
            if (action.SubActions == null || action.SubActions.Count == 0)
                return "❌ Cannot undo: No move records available";
            
            int restored = 0;
            int failed = 0;
            
            foreach (var subAction in action.SubActions)
            {
                try
                {
                    if (File.Exists(subAction.TargetPath) && !string.IsNullOrEmpty(subAction.SourcePath))
                    {
                        var dir = Path.GetDirectoryName(subAction.SourcePath);
                        if (!string.IsNullOrEmpty(dir))
                            Directory.CreateDirectory(dir);
                        
                        File.Move(subAction.TargetPath, subAction.SourcePath, true);
                        restored++;
                    }
                    else
                    {
                        failed++;
                    }
                }
                catch
                {
                    failed++;
                }
            }
            
            // Clean up empty folders created during organization
            if (action.CreatedFolders != null)
            {
                foreach (var folder in action.CreatedFolders.OrderByDescending(f => f.Length))
                {
                    try
                    {
                        if (Directory.Exists(folder) && Directory.GetFileSystemEntries(folder).Length == 0)
                            Directory.Delete(folder);
                    }
                    catch { }
                }
            }
            
            return $"✅ Undone folder organization\n📁 Restored: {restored} files\n⚠️ Failed: {failed} files";
        }
        
        private async Task<string> UndoAppOpened(ActionRecord action)
        {
            var processName = action.ProcessName;
            if (string.IsNullOrEmpty(processName))
                return "⚠️ Process name unknown";
            
            try
            {
                var processes = Process.GetProcessesByName(processName);
                if (processes.Length == 0)
                    return $"⚠️ {processName} is not running";
                
                foreach (var proc in processes)
                {
                    try { proc.CloseMainWindow(); } catch { }
                }
                
                await Task.Delay(500);
                
                // Force kill if still running
                processes = Process.GetProcessesByName(processName);
                foreach (var proc in processes)
                {
                    try { proc.Kill(); } catch { }
                }
                
                return $"✅ Undone: Closed {processName}";
            }
            catch (Exception ex)
            {
                return $"❌ Failed to close {processName}: {ex.Message}";
            }
        }
        
        private async Task<string> UndoRegistryChanged(ActionRecord action)
        {
            // Registry changes are risky - only undo if we have backup
            if (string.IsNullOrEmpty(action.OldValue))
                return "❌ Cannot undo: No previous value recorded";
            
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(action.RegistryPath!, true);
                if (key == null)
                    return "❌ Registry key not found";
                
                if (action.OldValue == "[DELETED]")
                    key.DeleteValue(action.RegistryValueName!, false);
                else
                    key.SetValue(action.RegistryValueName!, action.OldValue);
                
                return $"✅ Undone: Restored registry value";
            }
            catch (Exception ex)
            {
                return $"❌ Registry undo failed: {ex.Message}";
            }
        }
        
        private async Task<string> UndoSettingChanged(ActionRecord action)
        {
            // Settings are stored in our config - restore old value
            return $"⚠️ Setting undo not yet implemented for: {action.SettingName}";
        }
        
        #endregion
        
        #region Helpers
        
        private string CreateBackup(string path)
        {
            var backupDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "AtlasAI", "Backups", DateTime.Now.ToString("yyyy-MM-dd"));
            Directory.CreateDirectory(backupDir);
            
            var backupPath = Path.Combine(backupDir, $"{Guid.NewGuid()}_{Path.GetFileName(path)}");
            File.Copy(path, backupPath);
            return backupPath;
        }
        
        private void CopyDirectory(string source, string destination)
        {
            Directory.CreateDirectory(destination);
            
            foreach (var file in Directory.GetFiles(source))
            {
                var destFile = Path.Combine(destination, Path.GetFileName(file));
                File.Copy(file, destFile, true);
            }
            
            foreach (var dir in Directory.GetDirectories(source))
            {
                var destDir = Path.Combine(destination, Path.GetFileName(dir));
                CopyDirectory(dir, destDir);
            }
        }
        
        private void LoadHistory()
        {
            try
            {
                if (File.Exists(_historyPath))
                {
                    var json = File.ReadAllText(_historyPath);
                    var history = JsonSerializer.Deserialize<List<ActionRecord>>(json);
                    if (history != null)
                    {
                        _history.Clear();
                        _history.AddRange(history);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ActionHistory] Load failed: {ex.Message}");
            }
        }
        
        private void SaveHistory()
        {
            try
            {
                var json = JsonSerializer.Serialize(_history, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_historyPath, json);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ActionHistory] Save failed: {ex.Message}");
            }
        }
        
        #endregion
    }
}
