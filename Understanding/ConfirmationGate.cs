using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace AtlasAI.Understanding
{
    /// <summary>
    /// Manages confirmation requirements for destructive actions.
    /// Ensures user explicitly approves high-risk operations.
    /// Maintains audit log of all confirmed actions.
    /// </summary>
    public class ConfirmationGate
    {
        private readonly string _auditLogPath;
        private readonly List<AuditLogEntry> _pendingActions = new();
        private readonly Dictionary<string, DateTime> _recentConfirmations = new();
        private readonly TimeSpan _confirmationWindow = TimeSpan.FromMinutes(5);

        // Actions that ALWAYS require confirmation
        private static readonly HashSet<string> AlwaysConfirm = new()
        {
            "shutdown",
            "restart",
            "delete_file",
            "delete_folder",
            "format_drive",
            "registry_edit",
            "uninstall_app",
            "empty_recycle_bin"
        };

        // Actions that require confirmation only for certain targets
        private static readonly Dictionary<string, string[]> ConditionalConfirm = new()
        {
            ["close_app"] = new[] { "explorer", "system", "antivirus", "security" },
            ["move_file"] = new[] { "system32", "windows", "program files" },
            ["rename_file"] = new[] { ".exe", ".dll", ".sys" }
        };

        public ConfirmationGate()
        {
            _auditLogPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "AtlasAI", "audit_log.json");
            
            EnsureAuditDirectory();
        }

        /// <summary>
        /// Check if an action requires user confirmation
        /// </summary>
        public bool RequiresConfirmation(string action, Dictionary<string, object> parameters)
        {
            var actionLower = action.ToLower();
            
            // Always confirm these
            if (AlwaysConfirm.Contains(actionLower))
            {
                Debug.WriteLine($"[ConfirmationGate] Action '{action}' always requires confirmation");
                return true;
            }
            
            // Check conditional confirmations
            if (ConditionalConfirm.TryGetValue(actionLower, out var sensitiveTargets))
            {
                var target = parameters.GetValueOrDefault("target", "")?.ToString()?.ToLower() ?? "";
                var app = parameters.GetValueOrDefault("app", "")?.ToString()?.ToLower() ?? "";
                
                foreach (var sensitive in sensitiveTargets)
                {
                    if (target.Contains(sensitive) || app.Contains(sensitive))
                    {
                        Debug.WriteLine($"[ConfirmationGate] Action '{action}' on sensitive target requires confirmation");
                        return true;
                    }
                }
            }
            
            // Check if recently confirmed (within window)
            var actionKey = $"{action}:{JsonSerializer.Serialize(parameters)}";
            if (_recentConfirmations.TryGetValue(actionKey, out var lastConfirmed))
            {
                if (DateTime.Now - lastConfirmed < _confirmationWindow)
                {
                    Debug.WriteLine($"[ConfirmationGate] Action '{action}' was recently confirmed, skipping");
                    return false;
                }
            }
            
            return false;
        }

        /// <summary>
        /// Get the risk level for an action
        /// </summary>
        public RiskLevel GetRiskLevel(string action, Dictionary<string, object> parameters)
        {
            var actionLower = action.ToLower();
            
            // Critical actions
            if (actionLower.Contains("shutdown") || actionLower.Contains("restart") || 
                actionLower.Contains("format") || actionLower.Contains("registry"))
                return RiskLevel.Critical;
            
            // High risk
            if (actionLower.Contains("delete") || actionLower.Contains("uninstall") ||
                actionLower.Contains("empty_recycle"))
                return RiskLevel.High;
            
            // Medium risk
            if (actionLower.Contains("move") || actionLower.Contains("rename") ||
                actionLower.Contains("close_app") || actionLower.Contains("install"))
                return RiskLevel.Medium;
            
            // Low risk
            if (actionLower.Contains("open") || actionLower.Contains("create") ||
                actionLower.Contains("copy"))
                return RiskLevel.Low;
            
            return RiskLevel.None;
        }

        /// <summary>
        /// Generate a confirmation prompt for the user
        /// </summary>
        public string GenerateConfirmationPrompt(string action, Dictionary<string, object> parameters)
        {
            var risk = GetRiskLevel(action, parameters);
            var target = parameters.GetValueOrDefault("target", 
                parameters.GetValueOrDefault("app", ""))?.ToString() ?? "";
            
            var riskEmoji = risk switch
            {
                RiskLevel.Critical => "🚨",
                RiskLevel.High => "⚠️",
                RiskLevel.Medium => "⚡",
                _ => "ℹ️"
            };
            
            var actionDescription = action.ToLower() switch
            {
                "shutdown" => "shut down your computer",
                "restart" => "restart your computer",
                "delete_file" or "delete_folder" => $"permanently delete '{target}'",
                "uninstall_app" => $"uninstall '{target}'",
                "empty_recycle_bin" => "empty the Recycle Bin (cannot be undone)",
                "close_app" => $"close '{target}'",
                "move_file" => $"move '{target}'",
                _ => $"perform {action} on '{target}'"
            };
            
            return $"{riskEmoji} Are you sure you want to {actionDescription}?\n\nType 'yes' to confirm or 'no' to cancel.";
        }

        /// <summary>
        /// Record user confirmation and allow action to proceed
        /// </summary>
        public void RecordConfirmation(string action, Dictionary<string, object> parameters, bool confirmed)
        {
            var entry = new AuditLogEntry
            {
                Action = action,
                Target = parameters.GetValueOrDefault("target", 
                    parameters.GetValueOrDefault("app", ""))?.ToString(),
                Parameters = parameters,
                UserConfirmed = confirmed,
                Success = false // Will be updated after execution
            };
            
            _pendingActions.Add(entry);
            
            if (confirmed)
            {
                // Remember this confirmation for the window period
                var actionKey = $"{action}:{JsonSerializer.Serialize(parameters)}";
                _recentConfirmations[actionKey] = DateTime.Now;
            }
            
            Debug.WriteLine($"[ConfirmationGate] Recorded confirmation: {action} = {confirmed}");
        }

        /// <summary>
        /// Record the result of an executed action
        /// </summary>
        public void RecordExecution(string action, Dictionary<string, object> parameters, bool success, string? error = null)
        {
            var entry = new AuditLogEntry
            {
                Action = action,
                Target = parameters.GetValueOrDefault("target", 
                    parameters.GetValueOrDefault("app", ""))?.ToString(),
                Parameters = parameters,
                Success = success,
                ErrorMessage = error,
                UserConfirmed = true
            };
            
            // Generate rollback info for reversible actions
            entry.RollbackInfo = GenerateRollbackInfo(action, parameters);
            
            AppendToAuditLog(entry);
            Debug.WriteLine($"[ConfirmationGate] Recorded execution: {action} = {(success ? "success" : "failed")}");
        }

        /// <summary>
        /// Generate rollback information for an action
        /// </summary>
        private string? GenerateRollbackInfo(string action, Dictionary<string, object> parameters)
        {
            return action.ToLower() switch
            {
                "delete_file" => $"Restore from Recycle Bin: {parameters.GetValueOrDefault("target")}",
                "move_file" => $"Move back from {parameters.GetValueOrDefault("destination")} to {parameters.GetValueOrDefault("target")}",
                "rename_file" => $"Rename back to {parameters.GetValueOrDefault("target")}",
                "close_app" => $"Reopen: {parameters.GetValueOrDefault("app")}",
                _ => null
            };
        }

        /// <summary>
        /// Get recent audit log entries
        /// </summary>
        public List<AuditLogEntry> GetRecentAuditLog(int count = 20)
        {
            try
            {
                if (!File.Exists(_auditLogPath))
                    return new List<AuditLogEntry>();
                
                var json = File.ReadAllText(_auditLogPath);
                var entries = JsonSerializer.Deserialize<List<AuditLogEntry>>(json) ?? new List<AuditLogEntry>();
                
                return entries.TakeLast(count).Reverse().ToList();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ConfirmationGate] Error reading audit log: {ex.Message}");
                return new List<AuditLogEntry>();
            }
        }

        /// <summary>
        /// Check if user has a pending confirmation
        /// </summary>
        public bool HasPendingConfirmation()
        {
            return _pendingActions.Count > 0;
        }

        /// <summary>
        /// Get the pending action awaiting confirmation
        /// </summary>
        public AuditLogEntry? GetPendingAction()
        {
            return _pendingActions.LastOrDefault();
        }

        /// <summary>
        /// Clear pending confirmations
        /// </summary>
        public void ClearPending()
        {
            _pendingActions.Clear();
        }

        private void EnsureAuditDirectory()
        {
            var dir = Path.GetDirectoryName(_auditLogPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
        }

        private void AppendToAuditLog(AuditLogEntry entry)
        {
            try
            {
                List<AuditLogEntry> entries;
                
                if (File.Exists(_auditLogPath))
                {
                    var json = File.ReadAllText(_auditLogPath);
                    entries = JsonSerializer.Deserialize<List<AuditLogEntry>>(json) ?? new List<AuditLogEntry>();
                }
                else
                {
                    entries = new List<AuditLogEntry>();
                }
                
                entries.Add(entry);
                
                // Keep only last 1000 entries
                if (entries.Count > 1000)
                    entries = entries.TakeLast(1000).ToList();
                
                File.WriteAllText(_auditLogPath, JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ConfirmationGate] Error writing audit log: {ex.Message}");
            }
        }
    }
}
