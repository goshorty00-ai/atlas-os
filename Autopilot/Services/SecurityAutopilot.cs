using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AtlasAI.Autopilot.Models;

namespace AtlasAI.Autopilot.Services
{
    /// <summary>
    /// Security-focused autopilot actions with explainable reasoning
    /// </summary>
    public class SecurityAutopilot
    {
        private readonly AutopilotEngine _engine;
        private readonly ActionAuditLog _auditLog;
        private bool _isEnabled = true;
        
        public event EventHandler<AutopilotSuggestion>? SecurityAlert;
        public bool IsEnabled { get => _isEnabled; set => _isEnabled = value; }
        
        public SecurityAutopilot(AutopilotEngine engine, ActionAuditLog auditLog)
        {
            _engine = engine;
            _auditLog = auditLog;
        }

        /// <summary>
        /// Check for suspicious file in downloads
        /// </summary>
        public async Task<AutopilotAction?> CheckSuspiciousDownloadAsync(string filePath)
        {
            if (!_isEnabled) return null;
            
            var fileName = Path.GetFileName(filePath);
            var extension = Path.GetExtension(filePath).ToLowerInvariant();
            
            var suspiciousExtensions = new[] { ".exe", ".bat", ".cmd", ".ps1", ".vbs", ".js", ".msi" };
            
            if (suspiciousExtensions.Contains(extension))
            {
                var reasoning = $"The file '{fileName}' has extension '{extension}' which can execute code. " +
                               "Executable files from unknown sources can contain malware.";
                
                return await _engine.ProcessActionAsync(
                    "security_scan",
                    $"Scan downloaded file: {fileName}",
                    reasoning,
                    new Dictionary<string, object> { ["FilePath"] = filePath }
                );
            }
            
            return null;
        }
        
        /// <summary>
        /// Alert about unusual network activity
        /// </summary>
        public void AlertUnusualNetworkActivity(string processName, string destination)
        {
            if (!_isEnabled) return;
            
            var suggestion = new AutopilotSuggestion
            {
                Title = "Unusual Network Activity",
                Description = $"Process '{processName}' is connecting to {destination}",
                Reasoning = "Unexpected network connections could indicate malware or data exfiltration",
                Type = SuggestionType.Security,
                Priority = SuggestionPriority.High,
                ProposedAction = "Review and block if suspicious"
            };
            
            SecurityAlert?.Invoke(this, suggestion);
        }
        
        /// <summary>
        /// Check for outdated software
        /// </summary>
        public async Task CheckOutdatedSoftwareAsync()
        {
            if (!_isEnabled) return;
            
            // Check Windows Defender definitions
            try
            {
                var defenderPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "Microsoft", "Windows Defender", "Definition Updates"
                );
                
                if (Directory.Exists(defenderPath))
                {
                    var lastUpdate = Directory.GetLastWriteTime(defenderPath);
                    if (lastUpdate < DateTime.Now.AddDays(-7))
                    {
                        _engine.GenerateSuggestion(
                            "Update Security Definitions",
                            "Windows Defender definitions may be outdated",
                            "Outdated security definitions leave your system vulnerable to new threats",
                            SuggestionType.Security,
                            SuggestionPriority.High,
                            "Run Windows Update"
                        );
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SecurityAutopilot] Error checking Defender: {ex.Message}");
            }
            
            await Task.CompletedTask;
        }

        /// <summary>
        /// Validate a file before execution
        /// </summary>
        public async Task<(bool IsSafe, string Reasoning)> ValidateFileAsync(string filePath)
        {
            if (!File.Exists(filePath))
                return (false, "File does not exist");
            
            var extension = Path.GetExtension(filePath).ToLowerInvariant();
            var fileName = Path.GetFileName(filePath);
            
            // Check for double extensions (common malware trick)
            if (fileName.Count(c => c == '.') > 1)
            {
                var parts = fileName.Split('.');
                if (parts.Length > 2)
                {
                    return (false, $"File has suspicious double extension: {fileName}. This is a common malware technique.");
                }
            }
            
            // Check file size (very small executables are suspicious)
            var fileInfo = new FileInfo(filePath);
            if (extension == ".exe" && fileInfo.Length < 10000)
            {
                return (false, "Executable file is suspiciously small. Legitimate programs are usually larger.");
            }
            
            // Check for hidden file
            if ((fileInfo.Attributes & FileAttributes.Hidden) != 0)
            {
                return (false, "File is hidden. Hidden executables are often malicious.");
            }
            
            await _auditLog.LogCustomAsync(
                "security_validation",
                $"Validated file: {fileName}",
                "File passed basic security checks"
            );
            
            return (true, "File passed basic security validation");
        }
        
        /// <summary>
        /// Check startup programs for suspicious entries
        /// </summary>
        public async Task<List<AutopilotSuggestion>> CheckStartupProgramsAsync()
        {
            var suggestions = new List<AutopilotSuggestion>();
            if (!_isEnabled) return suggestions;
            
            try
            {
                var startupPath = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
                if (Directory.Exists(startupPath))
                {
                    var files = Directory.GetFiles(startupPath);
                    foreach (var file in files)
                    {
                        var ext = Path.GetExtension(file).ToLowerInvariant();
                        if (ext == ".bat" || ext == ".cmd" || ext == ".vbs")
                        {
                            suggestions.Add(new AutopilotSuggestion
                            {
                                Title = "Suspicious Startup Script",
                                Description = $"Found script in startup: {Path.GetFileName(file)}",
                                Reasoning = "Scripts in startup folder run automatically and could be malicious",
                                Type = SuggestionType.Security,
                                Priority = SuggestionPriority.Medium,
                                ProposedAction = "Review and remove if not recognized"
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SecurityAutopilot] Error checking startup: {ex.Message}");
            }
            
            await Task.CompletedTask;
            return suggestions;
        }
    }
}
