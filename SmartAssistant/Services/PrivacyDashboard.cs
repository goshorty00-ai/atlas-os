using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using AtlasAI.Core;
using AtlasAI.SmartAssistant.Models;

namespace AtlasAI.SmartAssistant.Services
{
    /// <summary>
    /// Manages privacy settings and provides audit trail of data access
    /// </summary>
    public class PrivacyDashboard
    {
        private readonly string _settingsPath;
        private readonly string _auditPath;
        private PrivacySettings _settings = new();
        private List<PrivacyAuditEntry> _auditLog = new();
        private const int MaxAuditEntries = 1000;
        
        public event EventHandler<PrivacyAuditEntry>? AccessLogged;
        public event EventHandler<PrivacySettings>? SettingsChanged;
        
        public PrivacySettings Settings => _settings;
        public IReadOnlyList<PrivacyAuditEntry> AuditLog => _auditLog.AsReadOnly();
        
        public PrivacyDashboard()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var basePath = Path.Combine(appData, "AtlasAI");
            _settingsPath = Path.Combine(basePath, "privacy_settings.json");
            _auditPath = Path.Combine(basePath, "privacy_audit.json");
        }
        
        public async Task InitializeAsync()
        {
            await LoadSettingsAsync();
            await LoadAuditLogAsync();
            Debug.WriteLine($"[Privacy] Loaded settings and {_auditLog.Count} audit entries");
        }
        
        /// <summary>
        /// Check if an action is allowed by privacy settings
        /// </summary>
        public bool IsAllowed(string category, string target = "")
        {
            var allowed = category.ToLowerInvariant() switch
            {
                "voice" => _settings.EnableVoiceRecording,
                "screen" => _settings.EnableScreenCapture,
                "clipboard" => _settings.EnableClipboardAccess,
                "file" => _settings.EnableFileAccess && !IsBlockedPath(target),
                "network" => _settings.EnableNetworkAccess,
                "habit" => _settings.EnableHabitLearning,
                "data" => _settings.EnableDataCollection,
                _ => true
            };
            
            return allowed;
        }
        
        /// <summary>
        /// Log a data access event
        /// </summary>
        public async Task LogAccessAsync(string category, string action, string details, bool wasAllowed)
        {
            var entry = new PrivacyAuditEntry
            {
                Category = category,
                Action = action,
                Details = details,
                WasAllowed = wasAllowed
            };
            
            _auditLog.Add(entry);
            
            // Trim old entries
            if (_auditLog.Count > MaxAuditEntries)
            {
                _auditLog = _auditLog.Skip(_auditLog.Count - MaxAuditEntries).ToList();
            }
            
            AccessLogged?.Invoke(this, entry);
            await SaveAuditLogAsync();
        }
        
        /// <summary>
        /// Update privacy settings
        /// </summary>
        public async Task UpdateSettingsAsync(Action<PrivacySettings> updateAction)
        {
            updateAction(_settings);
            _settings.LastUpdated = DateTime.Now;
            await SaveSettingsAsync();
            SettingsChanged?.Invoke(this, _settings);
        }
        
        /// <summary>
        /// Add an app to the blocked list
        /// </summary>
        public async Task BlockAppAsync(string appName)
        {
            if (!_settings.BlockedApps.Contains(appName, StringComparer.OrdinalIgnoreCase))
            {
                _settings.BlockedApps.Add(appName);
                await SaveSettingsAsync();
            }
        }
        
        /// <summary>
        /// Remove an app from the blocked list
        /// </summary>
        public async Task UnblockAppAsync(string appName)
        {
            _settings.BlockedApps.RemoveAll(a => a.Equals(appName, StringComparison.OrdinalIgnoreCase));
            await SaveSettingsAsync();
        }
        
        /// <summary>
        /// Add a folder to the blocked list
        /// </summary>
        public async Task BlockFolderAsync(string folderPath)
        {
            if (!_settings.BlockedFolders.Contains(folderPath, StringComparer.OrdinalIgnoreCase))
            {
                _settings.BlockedFolders.Add(folderPath);
                await SaveSettingsAsync();
            }
        }
        
        /// <summary>
        /// Get privacy summary
        /// </summary>
        public PrivacySummary GetSummary()
        {
            var last24h = _auditLog.Where(e => e.Timestamp > DateTime.Now.AddHours(-24)).ToList();
            
            return new PrivacySummary
            {
                TotalAccessesLast24h = last24h.Count,
                AllowedAccessesLast24h = last24h.Count(e => e.WasAllowed),
                BlockedAccessesLast24h = last24h.Count(e => !e.WasAllowed),
                VoiceRecordingsLast24h = last24h.Count(e => e.Category == "voice"),
                ScreenCapturesLast24h = last24h.Count(e => e.Category == "screen"),
                FileAccessesLast24h = last24h.Count(e => e.Category == "file"),
                DataRetentionDays = _settings.DataRetentionDays,
                BlockedAppsCount = _settings.BlockedApps.Count,
                BlockedFoldersCount = _settings.BlockedFolders.Count
            };
        }
        
        /// <summary>
        /// Clear audit log
        /// </summary>
        public async Task ClearAuditLogAsync()
        {
            _auditLog.Clear();
            await SaveAuditLogAsync();
        }
        
        /// <summary>
        /// Export privacy data
        /// </summary>
        public async Task<string> ExportDataAsync()
        {
            var export = new
            {
                ExportedAt = DateTime.Now,
                Settings = _settings,
                AuditLog = _auditLog
            };
            
            return JsonSerializer.Serialize(export, new JsonSerializerOptions { WriteIndented = true });
        }
        
        /// <summary>
        /// Delete all collected data
        /// </summary>
        public async Task DeleteAllDataAsync()
        {
            // SAFETY GATE: Check before data deletion
            var habitsPath = Path.Combine(Path.GetDirectoryName(_settingsPath)!, "habits");
            var safetyCheck = await SafetyKernel.Instance.CheckAndBlockAsync(
                OperationType.FolderDelete,
                OperationRisk.Medium,
                "Delete all collected privacy data",
                new Dictionary<string, object>
                {
                    ["path"] = habitsPath,
                    ["reason"] = "user requested data deletion"
                });
            
            if (safetyCheck.Decision == SafetyDecision.Blocked)
            {
                Debug.WriteLine($"[Privacy] Data deletion blocked: {safetyCheck.Message}");
                return;
            }
            
            _auditLog.Clear();
            await SaveAuditLogAsync();
            
            // Also clear habit data
            if (Directory.Exists(habitsPath))
            {
                Directory.Delete(habitsPath, true);
            }
            
            Debug.WriteLine("[Privacy] All collected data deleted");
        }
        
        private bool IsBlockedPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            
            return _settings.BlockedFolders.Any(blocked => 
                path.StartsWith(blocked, StringComparison.OrdinalIgnoreCase));
        }
        
        private async Task LoadSettingsAsync()
        {
            try
            {
                if (File.Exists(_settingsPath))
                {
                    var json = await File.ReadAllTextAsync(_settingsPath);
                    _settings = JsonSerializer.Deserialize<PrivacySettings>(json) ?? new();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Privacy] Error loading settings: {ex.Message}");
            }
        }
        
        private async Task SaveSettingsAsync()
        {
            try
            {
                var dir = Path.GetDirectoryName(_settingsPath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir!);
                
                var json = JsonSerializer.Serialize(_settings, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(_settingsPath, json);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Privacy] Error saving settings: {ex.Message}");
            }
        }
        
        private async Task LoadAuditLogAsync()
        {
            try
            {
                if (File.Exists(_auditPath))
                {
                    var json = await File.ReadAllTextAsync(_auditPath);
                    _auditLog = JsonSerializer.Deserialize<List<PrivacyAuditEntry>>(json) ?? new();
                    
                    // Apply retention policy
                    var cutoff = DateTime.Now.AddDays(-_settings.DataRetentionDays);
                    _auditLog = _auditLog.Where(e => e.Timestamp > cutoff).ToList();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Privacy] Error loading audit log: {ex.Message}");
            }
        }
        
        private async Task SaveAuditLogAsync()
        {
            try
            {
                var dir = Path.GetDirectoryName(_auditPath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir!);
                
                var json = JsonSerializer.Serialize(_auditLog, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(_auditPath, json);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Privacy] Error saving audit log: {ex.Message}");
            }
        }
    }
    
    public class PrivacySummary
    {
        public int TotalAccessesLast24h { get; set; }
        public int AllowedAccessesLast24h { get; set; }
        public int BlockedAccessesLast24h { get; set; }
        public int VoiceRecordingsLast24h { get; set; }
        public int ScreenCapturesLast24h { get; set; }
        public int FileAccessesLast24h { get; set; }
        public int DataRetentionDays { get; set; }
        public int BlockedAppsCount { get; set; }
        public int BlockedFoldersCount { get; set; }
    }
}
