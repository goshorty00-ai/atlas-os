using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace AtlasAI.Ledger
{
    /// <summary>
    /// Manages the system event ledger - tracks all detected system changes
    /// </summary>
    public class LedgerManager
    {
        private static readonly Lazy<LedgerManager> _instance = new(() => new LedgerManager());
        public static LedgerManager Instance => _instance.Value;
        
        private readonly ObservableCollection<LedgerEvent> _events = new();
        private readonly string _ledgerPath;
        private const int MaxEvents = 500;
        
        /// <summary>
        /// Observable collection of events for UI binding
        /// </summary>
        public ObservableCollection<LedgerEvent> Events => _events;
        
        /// <summary>
        /// Fired when a new event is added
        /// </summary>
        public event Action<LedgerEvent>? EventAdded;
        
        /// <summary>
        /// Fired when an event is resolved
        /// </summary>
        public event Action<LedgerEvent>? EventResolved;
        
        /// <summary>
        /// Count of unresolved events
        /// </summary>
        public int UnresolvedCount => _events.Count(e => !e.IsResolved);
        
        /// <summary>
        /// Count of high/critical unresolved events
        /// </summary>
        public int CriticalCount => _events.Count(e => !e.IsResolved && 
            (e.Severity == LedgerSeverity.High || e.Severity == LedgerSeverity.Critical));
        
        private LedgerManager()
        {
            var appData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "AtlasAI");
            Directory.CreateDirectory(appData);
            _ledgerPath = Path.Combine(appData, "ledger.json");
            LoadLedger();
        }
        
        /// <summary>
        /// Add a new event to the ledger
        /// </summary>
        public void AddEvent(LedgerEvent evt)
        {
            evt.Timestamp = DateTime.Now;
            if (string.IsNullOrEmpty(evt.Id))
                evt.Id = Guid.NewGuid().ToString();
            
            // Insert at beginning (newest first)
            _events.Insert(0, evt);
            
            // Trim old events
            while (_events.Count > MaxEvents)
                _events.RemoveAt(_events.Count - 1);
            
            SaveLedger();
            EventAdded?.Invoke(evt);
            
            Debug.WriteLine($"[Ledger] Added: [{evt.Severity}] {evt.Category} - {evt.Title}");
        }
        
        /// <summary>
        /// Execute an action on an event
        /// </summary>
        public async Task<string> ExecuteActionAsync(LedgerEvent evt, LedgerAction action)
        {
            Debug.WriteLine($"[Ledger] Executing action '{action.Label}' on event '{evt.Title}'");
            
            try
            {
                string result = action.Type switch
                {
                    LedgerActionType.Revert => await ExecuteRevertAsync(evt, action),
                    LedgerActionType.Delete => await ExecuteDeleteAsync(evt, action),
                    LedgerActionType.Block => await ExecuteBlockAsync(evt, action),
                    LedgerActionType.Allow => await ExecuteAllowAsync(evt, action),
                    LedgerActionType.Inspect => ExecuteInspect(evt, action),
                    LedgerActionType.Dismiss => ExecuteDismiss(evt, action),
                    _ => "Unknown action type"
                };
                
                // Mark as resolved for most actions
                if (action.Type != LedgerActionType.Inspect)
                {
                    evt.Resolve(action.Label, result);
                    SaveLedger();
                    EventResolved?.Invoke(evt);
                }
                
                return result;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Ledger] Action failed: {ex.Message}");
                return $"❌ Action failed: {ex.Message}";
            }
        }
        
        /// <summary>
        /// Get events by category
        /// </summary>
        public IEnumerable<LedgerEvent> GetByCategory(LedgerCategory category)
            => _events.Where(e => e.Category == category);
        
        /// <summary>
        /// Get unresolved events
        /// </summary>
        public IEnumerable<LedgerEvent> GetUnresolved()
            => _events.Where(e => !e.IsResolved);
        
        /// <summary>
        /// Get events from the last N hours
        /// </summary>
        public IEnumerable<LedgerEvent> GetRecent(int hours = 24)
            => _events.Where(e => e.Timestamp > DateTime.Now.AddHours(-hours));
        
        /// <summary>
        /// Clear all resolved events
        /// </summary>
        public void ClearResolved()
        {
            var resolved = _events.Where(e => e.IsResolved).ToList();
            foreach (var evt in resolved)
                _events.Remove(evt);
            SaveLedger();
        }
        
        #region Action Implementations
        
        private async Task<string> ExecuteRevertAsync(LedgerEvent evt, LedgerAction action)
        {
            // Check if we have backup data
            var backupData = action.Data ?? evt.BackupData;
            if (string.IsNullOrEmpty(backupData))
                return "❌ No backup data available for revert";
            
            // Handle hosts file revert
            if (evt.Category == LedgerCategory.FileSystem && evt.Title.Contains("Hosts file", StringComparison.OrdinalIgnoreCase))
            {
                var hostsWatcher = SecuritySuite.Services.HostsFileWatcher.Instance;
                var (success, message) = await hostsWatcher.RevertAsync(backupData);
                return message;
            }
            
            // Handle startup entry revert (modified entry)
            if (evt.Category == LedgerCategory.Startup && evt.Title.Contains("modified", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var data = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(backupData);
                    var name = data.GetProperty("Name").GetString() ?? "";
                    var oldCommand = data.GetProperty("OldCommand").GetString() ?? "";
                    var registryPath = data.GetProperty("RegistryPath").GetString() ?? "";
                    var isHKLM = data.GetProperty("IsHKLM").GetBoolean();
                    
                    var (success, message) = SecuritySuite.Services.StartupWatcher.Instance.RestoreRegistryEntry(name, oldCommand, registryPath, isHKLM);
                    return message;
                }
                catch (Exception ex)
                {
                    return $"❌ Revert failed: {ex.Message}";
                }
            }
            
            // Generic file revert (for future watchers)
            if (File.Exists(backupData))
            {
                try
                {
                    var pathEvidence = evt.Evidence.FirstOrDefault(e => e.Key == "Path" && e.IsPath);
                    if (pathEvidence != null && !string.IsNullOrEmpty(pathEvidence.Value))
                    {
                        var backupContent = await File.ReadAllTextAsync(backupData);
                        await File.WriteAllTextAsync(pathEvidence.Value, backupContent);
                        return "✅ Reverted successfully";
                    }
                }
                catch (Exception ex)
                {
                    return $"❌ Revert failed: {ex.Message}";
                }
            }
            
            return "❌ Could not perform revert - backup not found";
        }
        
        private async Task<string> ExecuteDeleteAsync(LedgerEvent evt, LedgerAction action)
        {
            var backupData = action.Data ?? evt.BackupData;
            if (string.IsNullOrEmpty(backupData))
                return "❌ No data available for delete action";
            
            try
            {
                var data = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(backupData);
                var type = data.GetProperty("Type").GetString();
                
                // Handle startup registry entry deletion
                if (type == "RegistryAdd" && evt.Category == LedgerCategory.Startup)
                {
                    var name = data.GetProperty("Name").GetString() ?? "";
                    var registryPath = data.GetProperty("RegistryPath").GetString() ?? "";
                    var isHKLM = data.GetProperty("IsHKLM").GetBoolean();
                    
                    var (success, message) = SecuritySuite.Services.StartupWatcher.Instance.RemoveRegistryEntry(name, registryPath, isHKLM);
                    return message;
                }
                
                // Handle startup folder item deletion
                if (type == "FolderAdd" && evt.Category == LedgerCategory.Startup)
                {
                    var fullPath = data.GetProperty("FullPath").GetString() ?? "";
                    var (success, message) = SecuritySuite.Services.StartupWatcher.Instance.DeleteStartupFile(fullPath);
                    return message;
                }
                
                // Handle scheduled task deletion
                if (type == "TaskAdd" && evt.Category == LedgerCategory.ScheduledTask)
                {
                    var taskPath = data.GetProperty("FullPath").GetString() ?? "";
                    var folderPath = data.GetProperty("FolderPath").GetString() ?? "";
                    var (success, message) = SecuritySuite.Services.ScheduledTaskWatcher.Instance.DeleteTask(taskPath, folderPath);
                    return message;
                }
            }
            catch (Exception ex)
            {
                return $"❌ Delete failed: {ex.Message}";
            }
            
            return "❌ Delete action not supported for this event type";
        }
        
        private async Task<string> ExecuteBlockAsync(LedgerEvent evt, LedgerAction action)
        {
            var backupData = action.Data ?? evt.BackupData;
            if (string.IsNullOrEmpty(backupData))
                return "❌ No data available for block action";
            
            try
            {
                var data = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(backupData);
                var type = data.GetProperty("Type").GetString();
                
                // Handle scheduled task disable (block = disable for tasks)
                if ((type == "TaskAdd" || type == "TaskModify") && evt.Category == LedgerCategory.ScheduledTask)
                {
                    var taskPath = data.GetProperty("FullPath").GetString() ?? "";
                    var (success, message) = SecuritySuite.Services.ScheduledTaskWatcher.Instance.DisableTask(taskPath);
                    return message;
                }
            }
            catch (Exception ex)
            {
                return $"❌ Block failed: {ex.Message}";
            }
            
            return "❌ Block action not supported for this event type";
        }
        
        private async Task<string> ExecuteAllowAsync(LedgerEvent evt, LedgerAction action)
        {
            // Allow/whitelist logic - just dismiss for now
            return "✅ Allowed";
        }
        
        private string ExecuteInspect(LedgerEvent evt, LedgerAction action)
        {
            // Open the path for inspection
            var path = action.Data ?? evt.Evidence.FirstOrDefault(e => e.IsPath)?.Value;
            if (!string.IsNullOrEmpty(path))
            {
                try
                {
                    if (File.Exists(path))
                        Process.Start("explorer.exe", $"/select,\"{path}\"");
                    else if (Directory.Exists(path))
                        Process.Start("explorer.exe", $"\"{path}\"");
                    else
                        Process.Start("explorer.exe", $"/select,\"{Path.GetDirectoryName(path)}\"");
                    
                    return "📂 Opened in Explorer";
                }
                catch (Exception ex)
                {
                    return $"❌ Could not open: {ex.Message}";
                }
            }
            return "⚠️ No path to inspect";
        }
        
        private string ExecuteDismiss(LedgerEvent evt, LedgerAction action)
        {
            return "✅ Dismissed";
        }
        
        #endregion
        
        #region Persistence
        
        private void LoadLedger()
        {
            try
            {
                if (File.Exists(_ledgerPath))
                {
                    var json = File.ReadAllText(_ledgerPath);
                    var events = JsonSerializer.Deserialize<List<LedgerEvent>>(json);
                    if (events != null)
                    {
                        _events.Clear();
                        foreach (var evt in events)
                            _events.Add(evt);
                    }
                    Debug.WriteLine($"[Ledger] Loaded {_events.Count} events");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Ledger] Load failed: {ex.Message}");
            }
        }
        
        private void SaveLedger()
        {
            try
            {
                var json = JsonSerializer.Serialize(_events.ToList(), new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_ledgerPath, json);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Ledger] Save failed: {ex.Message}");
            }
        }
        
        #endregion
    }
}
