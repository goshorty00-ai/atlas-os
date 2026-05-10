// Voice/SpeechDedupeLogger.cs
// Diagnostic logger for speech deduplication - proves the fix is working

using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace AtlasAI.Voice
{
    /// <summary>
    /// Rejection reason for speech requests
    /// </summary>
    public enum SpeechRejectReason
    {
        None,
        DuplicateTurnId,
        AlreadySpeaking,
        Suppressed,
        Disabled,
        EmptyText,
        Cancelled
    }

    /// <summary>
    /// Logs speech deduplication events to JSONL for debugging.
    /// File: %AppData%\AtlasAI\logs\speech_dedupe.jsonl
    /// </summary>
    public static class SpeechDedupeLogger
    {
        private static readonly string LogDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AtlasAI", "logs");
        
        private static readonly string LogPath = Path.Combine(LogDir, "speech_dedupe.jsonl");
        private static readonly object _lock = new object();

        /// <summary>
        /// Log a speech request attempt
        /// </summary>
        public static void Log(
            Guid turnId,
            string speechText,
            bool accepted,
            SpeechRejectReason rejectReason = SpeechRejectReason.None,
            UtteranceSource? source = null,
            [CallerMemberName] string? caller = null)
        {
            try
            {
                Directory.CreateDirectory(LogDir);

                var entry = new
                {
                    timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"),
                    turnId = turnId.ToString("N")[..8], // Short ID for readability
                    turnIdFull = turnId.ToString(),
                    requestedSpeechText = speechText?.Length > 60 ? speechText[..60] + "..." : speechText,
                    accepted,
                    rejectReason = rejectReason.ToString(),
                    source = source?.ToString() ?? "Unknown",
                    caller
                };

                var json = JsonSerializer.Serialize(entry);
                
                lock (_lock)
                {
                    File.AppendAllText(LogPath, json + Environment.NewLine);
                }

                // Also write to debug output
                var status = accepted ? "✅ ACCEPTED" : $"❌ REJECTED ({rejectReason})";
                System.Diagnostics.Debug.WriteLine($"[SpeechDedupe] {status} TurnId={entry.turnId} Caller={caller} Text=\"{entry.requestedSpeechText}\"");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SpeechDedupeLogger] Error: {ex.Message}");
            }
        }

        /// <summary>
        /// Log when speech completes
        /// </summary>
        public static void LogComplete(Guid turnId, bool cancelled = false)
        {
            try
            {
                Directory.CreateDirectory(LogDir);

                var entry = new
                {
                    timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"),
                    turnId = turnId.ToString("N")[..8],
                    @event = cancelled ? "cancelled" : "completed"
                };

                var json = JsonSerializer.Serialize(entry);
                
                lock (_lock)
                {
                    File.AppendAllText(LogPath, json + Environment.NewLine);
                }

                System.Diagnostics.Debug.WriteLine($"[SpeechDedupe] Speech {entry.@event} TurnId={entry.turnId}");
            }
            catch { }
        }

        /// <summary>
        /// Get current speech state for UI debug indicator
        /// </summary>
        public static string GetDebugStatus(bool isSpeaking, Guid? currentTurnId)
        {
            if (isSpeaking && currentTurnId.HasValue)
            {
                return $"Speech: 1 active | TurnId: {currentTurnId.Value.ToString("N")[..8]}";
            }
            return "Speech: 0 active";
        }
    }
}
