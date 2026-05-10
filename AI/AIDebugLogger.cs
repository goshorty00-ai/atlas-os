using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AtlasAI.AI
{
    /// <summary>
    /// STEP 30: AI Debug Logger - Proves LLM is actually being called.
    /// Logs to: %AppData%\AtlasAI\logs\ai_debug.jsonl
    /// 
    /// Every user chat turn produces exactly one of:
    /// - AIRequest + AIResponse, or
    /// - AIRequest + AIError + fallback response
    /// </summary>
    public static class AIDebugLogger
    {
        private static readonly string LogPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AtlasAI", "logs", "ai_debug.jsonl");

        private static readonly object _lock = new();
        private static int _requestCounter = 0;

        static AIDebugLogger()
        {
            try
            {
                var dir = Path.GetDirectoryName(LogPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
            }
            catch { }
        }

        /// <summary>
        /// Generate a unique request ID for correlation
        /// </summary>
        public static string GenerateRequestId()
        {
            _requestCounter++;
            return $"req_{DateTime.UtcNow:yyyyMMdd_HHmmss}_{_requestCounter:D4}";
        }

        /// <summary>
        /// Log AI request start
        /// </summary>
        public static void LogRequest(
            string requestId,
            string provider,
            string model,
            double temperature,
            string systemPrompt,
            string userMessage,
            int contextChars)
        {
            try
            {
                var systemPromptHash = ComputeHash(systemPrompt);
                var entry = new
                {
                    ts = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
                    type = "AIRequest",
                    requestId,
                    provider,
                    model,
                    temperature,
                    systemPromptHash,
                    userMessageLen = userMessage?.Length ?? 0,
                    contextChars,
                    startUtc = DateTime.UtcNow.ToString("o")
                };

                WriteEntry(entry);

                // Console output with [STAB] prefix for debugging
                Debug.WriteLine($"[STAB] AI.Request: {requestId} | {provider}/{model} | prompt:{systemPromptHash[..8]} | user:{userMessage?.Length ?? 0}chars | ctx:{contextChars}chars");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AIDebugLogger] LogRequest failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Log AI response received
        /// </summary>
        public static void LogResponse(
            string requestId,
            string finishReason,
            int outputChars,
            long latencyMs,
            string responseContent)
        {
            try
            {
                // Safe snippet: first 120 chars of response
                var safeSnippet = responseContent?.Length > 120
                    ? responseContent.Substring(0, 120) + "..."
                    : responseContent ?? "";

                // Sanitize for JSON
                safeSnippet = safeSnippet.Replace("\n", " ").Replace("\r", "").Replace("\"", "'");

                var entry = new
                {
                    ts = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
                    type = "AIResponse",
                    requestId,
                    finishReason,
                    outputChars,
                    latencyMs,
                    responsePreview = safeSnippet
                };

                WriteEntry(entry);

                // Console output
                Debug.WriteLine($"[STAB] AI.Response: {requestId} | {finishReason} | {outputChars}chars | {latencyMs}ms | \"{safeSnippet.Substring(0, Math.Min(60, safeSnippet.Length))}...\"");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AIDebugLogger] LogResponse failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Log AI error
        /// /// </summary>
        public static void LogError(
            string requestId,
            string exceptionType,
            string exceptionMessage,
            string userFacingReason)
        {
            try
            {
                var entry = new
                {
                    ts = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
                    type = "AIError",
                    requestId,
                    exceptionType,
                    exceptionMessage = exceptionMessage?.Length > 200 
                        ? exceptionMessage.Substring(0, 200) + "..." 
                        : exceptionMessage,
                    userFacingReason
                };

                WriteEntry(entry);

                // Console output
                Debug.WriteLine($"[STAB] AI.Error: {requestId} | {exceptionType} | {userFacingReason}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AIDebugLogger] LogError failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Log quality gate evaluation (on assistant output, not input)
        /// </summary>
        public static void LogQualityGate(
            string requestId,
            string candidatePreview,
            bool passed,
            string? rejectionReason,
            int specificityScore,
            int retryCount)
        {
            try
            {
                var preview = candidatePreview?.Length > 100
                    ? candidatePreview.Substring(0, 100) + "..."
                    : candidatePreview ?? "";
                preview = preview.Replace("\n", " ").Replace("\r", "").Replace("\"", "'");

                var entry = new
                {
                    ts = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
                    type = "QualityGate",
                    requestId,
                    candidatePreview = preview,
                    passed,
                    rejectionReason,
                    specificityScore,
                    retryCount
                };

                WriteEntry(entry);

                var status = passed ? "✅ PASSED" : $"❌ REJECTED ({rejectionReason})";
                Debug.WriteLine($"[STAB] QualityGate: {requestId} | {status} | score:{specificityScore} | retry:{retryCount}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AIDebugLogger] LogQualityGate failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Log TTS request with text hash for sync verification
        /// </summary>
        public static void LogTTSRequest(string textHash, int textLength, string voiceId)
        {
            try
            {
                var entry = new
                {
                    ts = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
                    type = "TTSRequested",
                    textHash,
                    textLength,
                    voiceId
                };

                WriteEntry(entry);
                Debug.WriteLine($"[STAB] TTS.Request: hash:{textHash} | len:{textLength} | voice:{voiceId}");
            }
            catch { }
        }

        /// <summary>
        /// Log UI message posted with text hash for sync verification
        /// </summary>
        public static void LogUIMessagePosted(string textHash, int textLength, string sender)
        {
            try
            {
                var entry = new
                {
                    ts = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
                    type = "UIMessagePosted",
                    textHash,
                    textLength,
                    sender
                };

                WriteEntry(entry);
                Debug.WriteLine($"[STAB] UI.Message: hash:{textHash} | len:{textLength} | sender:{sender}");
            }
            catch { }
        }

        /// <summary>
        /// Log TTS/UI text mismatch
        /// </summary>
        public static void LogTextMismatch(string uiHash, string ttsHash, string context)
        {
            try
            {
                var entry = new
                {
                    ts = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
                    type = "TTSTextMismatch",
                    uiHash,
                    ttsHash,
                    context
                };

                WriteEntry(entry);
                Debug.WriteLine($"[STAB] ⚠️ TEXT MISMATCH: UI:{uiHash} != TTS:{ttsHash} | {context}");
            }
            catch { }
        }

        /// <summary>
        /// Compute short hash of text for comparison
        /// </summary>
        public static string ComputeHash(string text)
        {
            if (string.IsNullOrEmpty(text)) return "empty";
            
            using var sha = SHA256.Create();
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(text));
            return Convert.ToHexString(bytes).Substring(0, 16).ToLowerInvariant();
        }

        private static void WriteEntry(object entry)
        {
            try
            {
                var json = JsonSerializer.Serialize(entry, new JsonSerializerOptions { WriteIndented = false });
                lock (_lock)
                {
                    File.AppendAllText(LogPath, json + Environment.NewLine);
                }
            }
            catch { }
        }
    }
}
