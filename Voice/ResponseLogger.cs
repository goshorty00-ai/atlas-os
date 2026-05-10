using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using AtlasAI.Conversation.Models;

namespace AtlasAI.Voice
{
    /// <summary>
    /// Structured logging for response quality tracking.
    /// Logs to JSONL format for analysis.
    /// </summary>
    public static class ResponseLogger
    {
        private static readonly string LogDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AtlasAI", "logs");
        
        private static readonly string LogFile = Path.Combine(LogDir, "responses.jsonl");

        /// <summary>
        /// Log a response with quality metrics.
        /// </summary>
        public static void LogResponse(ResponseLogEntry entry)
        {
            try
            {
                if (!Directory.Exists(LogDir))
                    Directory.CreateDirectory(LogDir);

                var json = JsonSerializer.Serialize(entry);
                File.AppendAllText(LogFile, json + Environment.NewLine);
                
                Debug.WriteLine($"[ResponseLogger] Logged: intent={entry.Intent}, depth={entry.Depth}, score={entry.SpecificityScore}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ResponseLogger] Error: {ex.Message}");
            }
        }

        /// <summary>
        /// Create a log entry from current context.
        /// </summary>
        public static ResponseLogEntry CreateEntry(
            string userInput,
            string response,
            ResponseIntentType intent,
            int specificityScore,
            bool regenerated,
            bool cooldownApplied)
        {
            return new ResponseLogEntry
            {
                Timestamp = DateTime.UtcNow,
                UserInput = userInput,
                Response = response,
                Intent = intent.ToString(),
                Depth = ConversationContext.Instance.CurrentDepth.ToString(),
                TurnCount = ConversationContext.Instance.TurnCount,
                SpecificityScore = specificityScore,
                Regenerated = regenerated,
                CooldownApplied = cooldownApplied,
                WordCount = response.Split(new[] { ' ', '\n' }, StringSplitOptions.RemoveEmptyEntries).Length,
                ActiveProblem = ConversationWorkingMemory.Instance.ActiveProblem
            };
        }
    }

    public class ResponseLogEntry
    {
        public DateTime Timestamp { get; set; }
        public string UserInput { get; set; } = "";
        public string Response { get; set; } = "";
        public string Intent { get; set; } = "";
        public string Depth { get; set; } = "";
        public int TurnCount { get; set; }
        public int SpecificityScore { get; set; }
        public bool Regenerated { get; set; }
        public bool CooldownApplied { get; set; }
        public int WordCount { get; set; }
        public string? ActiveProblem { get; set; }
    }
}
