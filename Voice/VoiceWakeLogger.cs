using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace AtlasAI.Voice
{
    /// <summary>
    /// Stop reason enum for wake word logging
    /// </summary>
    public enum WakeStopReason
    {
        None,
        Timeout,
        Exception,
        MicUnavailable,
        RecognizerEnded,
        UserDisabled,
        CoordinatorDisposed,
        FocusLost,
        ExternalHandlerDefer,
        WhisperTakeover,
        Unknown
    }

    /// <summary>
    /// JSONL logger for wake word events.
    /// Logs to %AppData%\AtlasAI\logs\wake_word_events.jsonl
    /// </summary>
    public static class VoiceWakeLogger
    {
        private static readonly string LogPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AtlasAI", "logs", "wake_word_events.jsonl");

        private static readonly object _lock = new();

        static VoiceWakeLogger()
        {
            try
            {
                var dir = Path.GetDirectoryName(LogPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
            }
            catch { }
        }

        public static void Log(
            string component,
            string eventType,
            string? stateBefore = null,
            string? stateAfter = null,
            WakeStopReason? reason = null,
            string? micDevice = null,
            int? sampleRate = null,
            int? listeningWindowMs = null,
            int? cooldownMs = null,
            string? exception = null,
            object? extra = null)
        {
            try
            {
                var entry = new
                {
                    timestamp = DateTime.UtcNow.ToString("o"),
                    component,
                    @event = eventType,
                    state_before = stateBefore,
                    state_after = stateAfter,
                    reason = reason?.ToString(),
                    mic_device = micDevice,
                    sample_rate = sampleRate,
                    listening_window_ms = listeningWindowMs,
                    cooldown_ms = cooldownMs,
                    exception,
                    extra
                };

                var json = JsonSerializer.Serialize(entry, new JsonSerializerOptions { WriteIndented = false });
                
                lock (_lock)
                {
                    File.AppendAllText(LogPath, json + Environment.NewLine);
                }

                // Also write to Debug output
                Debug.WriteLine($"[VoiceWakeLog] {component}.{eventType}: {reason?.ToString() ?? "OK"}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[VoiceWakeLogger] Failed to log: {ex.Message}");
            }
        }

        public static void LogStart(string component, string micDevice = "default")
        {
            Log(component, "WakeListeningStartRequested", micDevice: micDevice);
        }

        public static void LogStarted(string component, string stateBefore, string stateAfter)
        {
            Log(component, "WakeListeningStarted", stateBefore, stateAfter);
        }

        public static void LogStopped(string component, WakeStopReason reason, string? stateBefore = null)
        {
            Log(component, "WakeListeningStopped", stateBefore, "Stopped", reason);
        }

        public static void LogTimeout(string component, int timeoutMs)
        {
            Log(component, "WakeListeningTimeoutFired", reason: WakeStopReason.Timeout, listeningWindowMs: timeoutMs);
        }

        public static void LogRecognizerEnded(string component, bool wasExpected)
        {
            Log(component, "RecognizerSessionEnded", reason: wasExpected ? WakeStopReason.None : WakeStopReason.RecognizerEnded);
        }

        public static void LogAudioStopped(string component, WakeStopReason reason)
        {
            Log(component, "AudioCaptureStopped", reason: reason);
        }

        public static void LogSpeechDetected(string component)
        {
            Log(component, "SpeechDetected");
        }

        public static void LogWakeWordMatched(string component, string text, double confidence)
        {
            Log(component, "WakeWordMatched", extra: new { text, confidence });
        }

        public static void LogWakeWordRejected(string component, string text, string reason)
        {
            Log(component, "WakeWordRejected", extra: new { text, reason });
        }

        public static void LogError(string component, Exception ex)
        {
            Log(component, "Error", reason: WakeStopReason.Exception, exception: $"{ex.GetType().Name}: {ex.Message}");
        }

        public static void LogRestart(string component, int attemptNumber, int delayMs)
        {
            Log(component, "WakeListeningRestart", extra: new { attempt = attemptNumber, delay_ms = delayMs });
        }
    }
}
