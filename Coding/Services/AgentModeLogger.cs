using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Diagnostics;

namespace AtlasAI.Coding.Services
{
    /// <summary>
    /// Logger for Agent Mode v2 with [AGENT2] prefix.
    /// Logs to %AppData%\AtlasAI\logs\agent_mode_v2.jsonl
    /// </summary>
    public class AgentModeLogger
    {
        private static readonly string LogDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AtlasAI", "logs");
        
        private static readonly string LogPath = Path.Combine(LogDir, "agent_mode_v2.jsonl");
        private const long MaxFileSizeBytes = 10 * 1024 * 1024; // 10MB
        private static readonly object _lock = new();
        
        public static AgentModeLogger Instance { get; } = new();
        
        private AgentModeLogger()
        {
            EnsureLogDirectory();
        }
        
        private void EnsureLogDirectory()
        {
            try
            {
                if (!Directory.Exists(LogDir))
                    Directory.CreateDirectory(LogDir);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AGENT2] Failed to create log directory: {ex.Message}");
            }
        }
        
        public void Log(string eventType, object data)
        {
            try
            {
                var entry = new LogEntry
                {
                    Timestamp = DateTime.UtcNow.ToString("o"),
                    Event = eventType,
                    Data = data
                };
                
                var json = JsonSerializer.Serialize(entry, new JsonSerializerOptions 
                { 
                    WriteIndented = false 
                });
                
                lock (_lock)
                {
                    RotateIfNeeded();
                    File.AppendAllText(LogPath, json + Environment.NewLine);
                }
                
                Debug.WriteLine($"[AGENT2] {eventType}: {JsonSerializer.Serialize(data)}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AGENT2] Log error: {ex.Message}");
            }
        }
        
        public void LogRetrievalQuery(string query, int topK, List<SearchResult> results)
        {
            Log("retrieval_query", new
            {
                query,
                topK,
                resultCount = results.Count,
                results = results.ConvertAll(r => new
                {
                    file = r.RelativePath,
                    score = r.Score,
                    reason = r.Reason
                })
            });
        }
        
        public void LogContextManifest(ContextManifest manifest)
        {
            Log("context_manifest", new
            {
                totalChars = manifest.TotalChars,
                fileCount = manifest.IncludedFiles.Count,
                snippetCount = manifest.IncludedSnippets.Count,
                hasProblems = manifest.HasProblems,
                hasTerminal = manifest.HasTerminalOutput
            });
        }
        
        public void LogVerification(VerificationResult result)
        {
            Log("verification", new
            {
                type = result.Type.ToString(),
                success = result.Success,
                exitCode = result.ExitCode,
                errorCount = result.Errors.Count,
                durationMs = result.Duration.TotalMilliseconds,
                summary = Truncate(result.Summary, 500)
            });
        }
        
        public void LogRepairAttempt(RepairResult result)
        {
            Log("repair_attempt", new
            {
                attempted = result.Attempted,
                success = result.Success,
                patchLength = result.PatchApplied?.Length ?? 0,
                changeCount = result.Changes.Count,
                error = result.ErrorMessage
            });
        }
        
        public void LogSkillExecution(string skillName, bool success, string message)
        {
            Log("skill_execution", new
            {
                skill = skillName,
                success,
                message = Truncate(message, 500)
            });
        }
        
        public void LogIndexUpdate(string action, int fileCount, long durationMs)
        {
            Log("index_update", new
            {
                action,
                fileCount,
                durationMs
            });
        }
        
        public void LogError(string context, Exception ex)
        {
            Log("error", new
            {
                context,
                exceptionType = ex.GetType().Name,
                message = ex.Message,
                stackTrace = Truncate(ex.StackTrace ?? "", 1000)
            });
        }
        
        private void RotateIfNeeded()
        {
            try
            {
                if (!File.Exists(LogPath)) return;
                
                var fileInfo = new FileInfo(LogPath);
                if (fileInfo.Length >= MaxFileSizeBytes)
                {
                    var oldPath = LogPath + ".old";
                    if (File.Exists(oldPath))
                        File.Delete(oldPath);
                    File.Move(LogPath, oldPath);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AGENT2] Log rotation error: {ex.Message}");
            }
        }
        
        private static string Truncate(string text, int maxLength)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
                return text;
            return text.Substring(0, maxLength) + "...";
        }
        
        private class LogEntry
        {
            public string Timestamp { get; set; } = "";
            public string Event { get; set; } = "";
            public object? Data { get; set; }
        }
    }
}
