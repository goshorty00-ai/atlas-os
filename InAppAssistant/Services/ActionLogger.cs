using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using AtlasAI.InAppAssistant.Models;

namespace AtlasAI.InAppAssistant.Services
{
    /// <summary>
    /// Logs all in-app actions for audit trail
    /// </summary>
    public class ActionLogger
    {
        private readonly string _logPath;
        private readonly List<ActionLogEntry> _recentLogs = new();
        private readonly int _maxRecentLogs = 100;

        public event EventHandler<ActionLogEntry>? ActionLogged;

        public ActionLogger()
        {
            _logPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "AtlasAI", "action_logs.json");
            Load();
        }

        /// <summary>
        /// Log an action execution
        /// </summary>
        public void Log(InAppAction action, ActiveAppContext context, ActionResult result, bool wasConfirmed)
        {
            var entry = new ActionLogEntry
            {
                ActionName = action.Name,
                TargetApp = context.ProcessName,
                TargetWindow = context.WindowTitle,
                ActionType = action.Type,
                Success = result.Success,
                Details = result.Message,
                WasConfirmed = wasConfirmed,
                UserCommand = action.Description
            };

            _recentLogs.Add(entry);
            
            // Trim old logs
            while (_recentLogs.Count > _maxRecentLogs)
                _recentLogs.RemoveAt(0);

            Save();
            ActionLogged?.Invoke(this, entry);
            
            Debug.WriteLine($"[ActionLog] {entry.ActionName} on {entry.TargetApp}: {(entry.Success ? "✓" : "✗")}");
        }

        /// <summary>
        /// Get recent action logs
        /// </summary>
        public IReadOnlyList<ActionLogEntry> GetRecentLogs(int count = 20)
        {
            return _recentLogs.TakeLast(count).Reverse().ToList();
        }

        /// <summary>
        /// Get logs for a specific app
        /// </summary>
        public IReadOnlyList<ActionLogEntry> GetLogsForApp(string processName, int count = 20)
        {
            return _recentLogs
                .Where(l => l.TargetApp.Equals(processName, StringComparison.OrdinalIgnoreCase))
                .TakeLast(count)
                .Reverse()
                .ToList();
        }

        /// <summary>
        /// Get failed actions
        /// </summary>
        public IReadOnlyList<ActionLogEntry> GetFailedActions(int count = 20)
        {
            return _recentLogs
                .Where(l => !l.Success)
                .TakeLast(count)
                .Reverse()
                .ToList();
        }

        /// <summary>
        /// Clear all logs
        /// </summary>
        public void ClearLogs()
        {
            _recentLogs.Clear();
            Save();
            Debug.WriteLine("[ActionLog] Cleared all logs");
        }

        /// <summary>
        /// Export logs to file
        /// </summary>
        public void ExportLogs(string filePath)
        {
            try
            {
                var json = JsonSerializer.Serialize(_recentLogs, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(filePath, json);
                Debug.WriteLine($"[ActionLog] Exported {_recentLogs.Count} logs to {filePath}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ActionLog] Export error: {ex.Message}");
            }
        }

        private void Save()
        {
            try
            {
                var dir = Path.GetDirectoryName(_logPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                var json = JsonSerializer.Serialize(_recentLogs.TakeLast(50).ToList(), 
                    new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_logPath, json);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ActionLog] Save error: {ex.Message}");
            }
        }

        private void Load()
        {
            try
            {
                if (!File.Exists(_logPath)) return;

                var json = File.ReadAllText(_logPath);
                var logs = JsonSerializer.Deserialize<List<ActionLogEntry>>(json);
                if (logs != null)
                {
                    _recentLogs.AddRange(logs);
                    Debug.WriteLine($"[ActionLog] Loaded {_recentLogs.Count} action logs");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ActionLog] Load error: {ex.Message}");
            }
        }
    }
}
