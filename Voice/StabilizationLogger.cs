using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace AtlasAI.Voice
{
    /// <summary>
    /// STEP 29: Stabilization debug logger for diagnosing voice coordination issues.
    /// Logs wake state transitions, cooldown triggers, listening start/stop, and mic suspend/resume.
    /// 
    /// This is DIAGNOSTIC ONLY - can be removed after stabilization is complete.
    /// Logs to: %AppData%\AtlasAI\logs\stabilization_debug.jsonl
    /// </summary>
    public static class StabilizationLogger
    {
        private static readonly string LogPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AtlasAI", "logs", "stabilization_debug.jsonl");

        private static readonly object _lock = new();
        private static bool _enabled = true;

        static StabilizationLogger()
        {
            try
            {
                var dir = Path.GetDirectoryName(LogPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                    
                // Write session start marker
                LogEvent("System", "SessionStart", extra: new { 
                    timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    version = "STEP29_STABILIZATION"
                });
            }
            catch { }
        }

        /// <summary>
        /// Enable or disable stabilization logging
        /// </summary>
        public static bool Enabled
        {
            get => _enabled;
            set => _enabled = value;
        }

        /// <summary>
        /// Log a stabilization event
        /// </summary>
        public static void LogEvent(
            string component,
            string eventType,
            string? stateBefore = null,
            string? stateAfter = null,
            string? reason = null,
            object? extra = null)
        {
            if (!_enabled) return;

            try
            {
                var entry = new
                {
                    ts = DateTime.Now.ToString("HH:mm:ss.fff"),
                    component,
                    @event = eventType,
                    before = stateBefore,
                    after = stateAfter,
                    reason,
                    extra
                };

                var json = JsonSerializer.Serialize(entry, new JsonSerializerOptions { WriteIndented = false });

                lock (_lock)
                {
                    File.AppendAllText(LogPath, json + Environment.NewLine);
                }

                // Also write to Debug output with clear prefix
                var debugMsg = $"[STAB] {component}.{eventType}";
                if (!string.IsNullOrEmpty(reason)) debugMsg += $" ({reason})";
                if (stateBefore != null || stateAfter != null) debugMsg += $" [{stateBefore} → {stateAfter}]";
                Debug.WriteLine(debugMsg);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[StabilizationLogger] Log failed: {ex.Message}");
            }
        }

        // === Wake Word State Logging ===
        
        public static void LogWakeStateTransition(string from, string to, string reason)
        {
            LogEvent("WakeWord", "StateTransition", from, to, reason);
        }

        public static void LogWakeCooldownStart(int durationMs)
        {
            LogEvent("WakeWord", "CooldownStart", reason: $"{durationMs}ms");
        }

        public static void LogWakeCooldownEnd()
        {
            LogEvent("WakeWord", "CooldownEnd");
        }

        public static void LogWakeListeningStart(string trigger)
        {
            LogEvent("WakeWord", "ListeningStart", reason: trigger);
        }

        public static void LogWakeListeningStop(string reason)
        {
            LogEvent("WakeWord", "ListeningStop", reason: reason);
        }

        // === Microphone State Logging ===

        public static void LogMicSuspend(string reason)
        {
            LogEvent("Microphone", "Suspend", reason: reason);
        }

        public static void LogMicResume(string reason)
        {
            LogEvent("Microphone", "Resume", reason: reason);
        }

        public static void LogMicConflict(string holder, string requester)
        {
            LogEvent("Microphone", "Conflict", extra: new { holder, requester });
        }

        // === Speech Coordination Logging ===

        public static void LogSpeechRequest(string category, Guid turnId, string reason, bool granted)
        {
            LogEvent("Speech", granted ? "Granted" : "Rejected", 
                reason: reason, 
                extra: new { category, turnId = turnId.ToString("N")[..8] });
        }

        public static void LogSpeechStart(string category, Guid turnId, string text)
        {
            var preview = text.Length > 50 ? text[..50] + "..." : text;
            LogEvent("Speech", "Start", 
                extra: new { category, turnId = turnId.ToString("N")[..8], preview });
        }

        public static void LogSpeechEnd(string category, Guid turnId, bool cancelled)
        {
            LogEvent("Speech", cancelled ? "Cancelled" : "Complete",
                extra: new { category, turnId = turnId.ToString("N")[..8] });
        }

        public static void LogSpeechDuplicate(Guid turnId, string reason)
        {
            LogEvent("Speech", "DuplicateRejected", 
                reason: reason,
                extra: new { turnId = turnId.ToString("N")[..8] });
        }

        // === LLM Response Logging ===

        public static void LogLLMRequest(string provider, string model, int promptLength)
        {
            LogEvent("LLM", "Request", extra: new { provider, model, promptLength });
        }

        public static void LogLLMResponse(string provider, int responseLength, bool success, string? error = null)
        {
            LogEvent("LLM", success ? "Response" : "Error", 
                reason: error,
                extra: new { provider, responseLength });
        }

        public static void LogQualityGateResult(bool passed, int score, string? rejectReason = null)
        {
            LogEvent("QualityGate", passed ? "Passed" : "Rejected",
                reason: rejectReason,
                extra: new { score });
        }

        // === STEP 30: Enhanced Quality Gate Logging ===
        
        public static void LogQualityGateEvaluation(
            string requestId,
            string candidatePreview,
            bool passed,
            string? rejectionReason,
            int specificityScore,
            int retryCount)
        {
            var preview = candidatePreview?.Length > 80 
                ? candidatePreview.Substring(0, 80) + "..." 
                : candidatePreview ?? "";
            
            LogEvent("QualityGate", passed ? "Passed" : "Rejected",
                reason: rejectionReason,
                extra: new { 
                    requestId, 
                    candidatePreview = preview, 
                    specificityScore, 
                    retryCount 
                });
        }

        // === STEP 30: Response Composer Logging ===
        
        public static void LogResponseComposed(string originalPreview, string finalPreview, bool wasModified, string? reason)
        {
            LogEvent("ResponseComposer", wasModified ? "Modified" : "Unchanged",
                reason: reason,
                extra: new { 
                    originalLen = originalPreview?.Length ?? 0,
                    finalLen = finalPreview?.Length ?? 0
                });
        }

        // === STEP 30: TTS/UI Sync Logging ===
        
        public static void LogTTSRequest(string textHash, int textLength, string voiceId)
        {
            LogEvent("TTS", "Request", extra: new { textHash, textLength, voiceId });
        }

        public static void LogUIMessagePosted(string textHash, int textLength, string sender)
        {
            LogEvent("UI", "MessagePosted", extra: new { textHash, textLength, sender });
        }

        public static void LogTTSTextMismatch(string uiHash, string ttsHash, string context)
        {
            LogEvent("TTS", "TextMismatch", 
                reason: context,
                extra: new { uiHash, ttsHash });
        }

        // === Voice Pipeline Logging ===

        public static void LogVoicePipelineActive(string pipeline, bool active)
        {
            LogEvent("VoicePipeline", active ? "Active" : "Inactive", reason: pipeline);
        }

        public static void LogVoiceConflict(string activePipeline, string attemptedPipeline)
        {
            LogEvent("VoicePipeline", "Conflict", 
                extra: new { active = activePipeline, attempted = attemptedPipeline });
        }
    }
}
