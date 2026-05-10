using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace AtlasAI.Agent
{
    /// <summary>
    /// JSONL logger for intent classification, routing decisions, and permissions.
    /// Logs to %AppData%\AtlasAI\logs\intent_routing.jsonl
    /// </summary>
    public static class IntentLogger
    {
        private static readonly string LogPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AtlasAI", "logs", "intent_routing.jsonl");

        private static readonly object _lock = new();

        /// <summary>
        /// Log an intent classification result.
        /// </summary>
        public static void LogIntentClassification(string input, RoutingResult result)
        {
            Log("IntentClassified", new
            {
                input = TruncateInput(input),
                pipeline = result.Pipeline.ToString(),
                intent = result.Intent,
                confidence = result.Confidence,
                requiresOnlineConsent = result.RequiresOnlineConsent,
                wasBlocked = result.Pipeline == RoutingPipeline.BlockedOrUnsafe,
                debugReason = result.DebugReason
            });
        }

        /// <summary>
        /// Log a permission grant/deny.
        /// </summary>
        public static void LogPermission(string permissionType, bool granted, string reason = "")
        {
            Log("PermissionDecision", new
            {
                permission = permissionType,
                granted,
                reason
            });
        }

        /// <summary>
        /// Log online consent request and result.
        /// </summary>
        public static void LogOnlineConsent(string query, string decision, int? durationMinutes = null)
        {
            Log("OnlineConsent", new
            {
                query = TruncateInput(query),
                decision,
                durationMinutes
            });
        }

        /// <summary>
        /// Log a blocked/unsafe request.
        /// </summary>
        public static void LogBlockedRequest(string input, string blockReason, string? safeAlternative)
        {
            Log("BlockedRequest", new
            {
                input = TruncateInput(input),
                reason = blockReason,
                safeAlternative
            });
        }

        /// <summary>
        /// Log wake word acceptance/rejection.
        /// </summary>
        public static void LogWakeWord(string text, double confidence, bool accepted, string reason = "")
        {
            Log("WakeWord", new
            {
                text,
                confidence,
                accepted,
                reason
            });
        }

        /// <summary>
        /// Log voice state transition.
        /// </summary>
        public static void LogVoiceStateTransition(string fromState, string toState, string reason)
        {
            Log("VoiceStateTransition", new
            {
                from = fromState,
                to = toState,
                reason
            });
        }

        /// <summary>
        /// Log microphone ownership change.
        /// </summary>
        public static void LogMicOwnership(string owner, string previousOwner, string reason)
        {
            Log("MicOwnershipChange", new
            {
                owner,
                previousOwner,
                reason
            });
        }

        /// <summary>
        /// Log response quality gate result.
        /// </summary>
        public static void LogQualityGate(string input, bool passed, int specificityScore, string? reason = null)
        {
            Log("QualityGate", new
            {
                input = TruncateInput(input),
                passed,
                specificityScore,
                reason
            });
        }

        /// <summary>
        /// Log conversation depth change.
        /// </summary>
        public static void LogDepthChange(string fromDepth, string toDepth, string trigger)
        {
            Log("ConversationDepth", new
            {
                from = fromDepth,
                to = toDepth,
                trigger
            });
        }

        /// <summary>
        /// Log capability routing decision.
        /// </summary>
        public static void LogCapabilityDecision(string input, string intent, string decision, string permissionNeeded, string reason)
        {
            Log("CapabilityDecision", new
            {
                input = TruncateInput(input),
                intent,
                decision,
                permissionNeeded,
                reason
            });
        }

        /// <summary>
        /// Log user permission response.
        /// </summary>
        public static void LogPermissionResponse(string permissionType, string userDecision, string context = "")
        {
            Log("PermissionResponse", new
            {
                permission = permissionType,
                decision = userDecision,
                context
            });
        }

        /// <summary>
        /// Log final execution path after all routing.
        /// </summary>
        public static void LogExecutionPath(string input, string intent, string capabilityDecision, string finalAction, bool executed)
        {
            Log("ExecutionPath", new
            {
                input = TruncateInput(input),
                intent,
                capabilityDecision,
                finalAction,
                executed
            });
        }

        private static void Log(string eventType, object data)
        {
            try
            {
                lock (_lock)
                {
                    var logDir = Path.GetDirectoryName(LogPath);
                    if (!string.IsNullOrEmpty(logDir) && !Directory.Exists(logDir))
                        Directory.CreateDirectory(logDir);

                    var entry = new
                    {
                        timestamp = DateTime.UtcNow.ToString("o"),
                        @event = eventType,
                        data
                    };

                    var json = JsonSerializer.Serialize(entry);
                    File.AppendAllText(LogPath, json + Environment.NewLine);
                }
            }
            catch (Exception ex)
            {
                // Fail silently - logging should never crash the app
                Debug.WriteLine($"[IntentLogger] Log error: {ex.Message}");
            }
        }

        private static string TruncateInput(string input)
        {
            if (string.IsNullOrEmpty(input)) return "";
            return input.Length > 100 ? input.Substring(0, 100) + "..." : input;
        }

        /// <summary>
        /// Get the log file path for debugging.
        /// </summary>
        public static string GetLogPath() => LogPath;

        /// <summary>
        /// Clear the log file.
        /// </summary>
        public static void ClearLog()
        {
            try
            {
                lock (_lock)
                {
                    if (File.Exists(LogPath))
                        File.Delete(LogPath);
                }
            }
            catch { }
        }
    }
}
