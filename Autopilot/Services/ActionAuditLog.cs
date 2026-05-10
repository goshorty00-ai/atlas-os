using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using AtlasAI.Autopilot.Models;

namespace AtlasAI.Autopilot.Services
{
    /// <summary>
    /// Full audit trail for all autopilot actions - "Why did you do this?"
    /// </summary>
    public class ActionAuditLog
    {
        private readonly string _logPath;
        private List<AuditLogEntry> _entries = new();
        private const int MaxEntries = 5000;
        
        public event EventHandler<AuditLogEntry>? EntryAdded;
        public IReadOnlyList<AuditLogEntry> Entries => _entries.AsReadOnly();
        
        public ActionAuditLog()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            _logPath = Path.Combine(appData, "AtlasAI", "autopilot_audit.json");
        }

        public async Task InitializeAsync()
        {
            await LoadEntriesAsync();
            Debug.WriteLine($"[AuditLog] Loaded {_entries.Count} entries");
        }
        
        /// <summary>
        /// Log an autopilot action with full context
        /// </summary>
        public async Task LogActionAsync(AutopilotAction action, string? additionalInfo = null)
        {
            var entry = new AuditLogEntry
            {
                ActionId = action.Id,
                ActionType = action.ActionType,
                Description = action.Description,
                Reasoning = action.Reasoning,
                RuleId = action.RuleId,
                AutonomyLevel = action.RequiredLevel,
                WasAutoExecuted = action.WasAutoExecuted,
                WasSuccessful = action.Status == ActionStatus.Completed,
                ErrorDetails = action.ErrorMessage,
                Context = action.Context
            };
            
            if (!string.IsNullOrEmpty(additionalInfo))
            {
                entry.UserFeedback = additionalInfo;
            }
            
            _entries.Add(entry);
            TrimOldEntries();
            
            EntryAdded?.Invoke(this, entry);
            await SaveEntriesAsync();
        }
        
        /// <summary>
        /// Log a custom audit entry
        /// </summary>
        public async Task LogCustomAsync(string actionType, string description, string reasoning)
        {
            var entry = new AuditLogEntry
            {
                ActionType = actionType,
                Description = description,
                Reasoning = reasoning
            };
            
            _entries.Add(entry);
            TrimOldEntries();
            
            EntryAdded?.Invoke(this, entry);
            await SaveEntriesAsync();
        }

        /// <summary>
        /// Get entries for a specific time range
        /// </summary>
        public List<AuditLogEntry> GetEntries(DateTime from, DateTime to)
        {
            return _entries.Where(e => e.Timestamp >= from && e.Timestamp <= to).ToList();
        }
        
        /// <summary>
        /// Get entries for a specific rule
        /// </summary>
        public List<AuditLogEntry> GetEntriesForRule(string ruleId)
        {
            return _entries.Where(e => e.RuleId == ruleId).ToList();
        }
        
        /// <summary>
        /// Get recent entries
        /// </summary>
        public List<AuditLogEntry> GetRecentEntries(int count = 50)
        {
            return _entries.OrderByDescending(e => e.Timestamp).Take(count).ToList();
        }
        
        /// <summary>
        /// Get failed actions
        /// </summary>
        public List<AuditLogEntry> GetFailedActions()
        {
            return _entries.Where(e => !e.WasSuccessful).ToList();
        }
        
        /// <summary>
        /// Get auto-executed actions
        /// </summary>
        public List<AuditLogEntry> GetAutoExecutedActions()
        {
            return _entries.Where(e => e.WasAutoExecuted).ToList();
        }
        
        /// <summary>
        /// Add user feedback to an entry
        /// </summary>
        public async Task AddFeedbackAsync(string actionId, string feedback)
        {
            var entry = _entries.FirstOrDefault(e => e.ActionId == actionId);
            if (entry != null)
            {
                entry.UserFeedback = feedback;
                await SaveEntriesAsync();
            }
        }
        
        /// <summary>
        /// Export audit log
        /// </summary>
        public string ExportToJson()
        {
            return JsonSerializer.Serialize(_entries, new JsonSerializerOptions { WriteIndented = true });
        }
        
        /// <summary>
        /// Clear old entries (keep last N days)
        /// </summary>
        public async Task ClearOldEntriesAsync(int keepDays = 30)
        {
            var cutoff = DateTime.Now.AddDays(-keepDays);
            _entries = _entries.Where(e => e.Timestamp > cutoff).ToList();
            await SaveEntriesAsync();
        }

        private void TrimOldEntries()
        {
            if (_entries.Count > MaxEntries)
            {
                _entries = _entries.Skip(_entries.Count - MaxEntries).ToList();
            }
        }
        
        private async Task LoadEntriesAsync()
        {
            try
            {
                if (File.Exists(_logPath))
                {
                    var json = await File.ReadAllTextAsync(_logPath);
                    _entries = JsonSerializer.Deserialize<List<AuditLogEntry>>(json) ?? new();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AuditLog] Error loading: {ex.Message}");
            }
        }
        
        private async Task SaveEntriesAsync()
        {
            try
            {
                var dir = Path.GetDirectoryName(_logPath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir!);
                
                var json = JsonSerializer.Serialize(_entries, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(_logPath, json);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AuditLog] Error saving: {ex.Message}");
            }
        }
    }
}
