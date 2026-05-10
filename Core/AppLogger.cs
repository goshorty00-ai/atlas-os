using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace AtlasAI.Core
{
    /// <summary>
    /// Central in-app logger for displaying debug information to the user.
    /// Captures logs from various subsystems and broadcasts them to the Debug Console.
    /// </summary>
    public static class AppLogger
    {
        // Event for UI to subscribe to
        public static event EventHandler<string>? OnLog;
        
        private static readonly object _lock = new object();
        private static readonly StringBuilder _buffer = new StringBuilder();
        private const int MaxBufferLength = 50000;

        private static readonly string LogsDir = Path.Combine(AtlasPaths.RoamingDir, "logs");

        private static string _currentLogPath = "";

        public static string GetCurrentLogFilePath()
        {
            try
            {
                var today = DateTime.UtcNow.ToString("yyyyMMdd");
                var desired = Path.Combine(LogsDir, "app_log_" + today + ".txt");
                if (!string.Equals(_currentLogPath, desired, StringComparison.OrdinalIgnoreCase))
                    _currentLogPath = desired;
                return _currentLogPath;
            }
            catch
            {
                return _currentLogPath ?? "";
            }
        }

        /// <summary>
        /// Log a message to the in-app debug console
        /// </summary>
        public static void Log(string message)
        {
            try
            {
                var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
                var formattedMessage = $"[{timestamp}] {message}";
                
                // Write to system debug output first
                Debug.WriteLine(formattedMessage);
                
                lock (_lock)
                {
                    // Add to internal buffer
                    _buffer.AppendLine(formattedMessage);
                    
                    // Trim buffer if too large
                    if (_buffer.Length > MaxBufferLength)
                    {
                        _buffer.Remove(0, _buffer.Length - MaxBufferLength);
                        // Adjust to next newline to avoid partial lines
                        var nextLine = _buffer.ToString().IndexOf('\n');
                        if (nextLine >= 0)
                        {
                            _buffer.Remove(0, nextLine + 1);
                        }
                    }
                }

                // Best-effort disk log for diagnostics.
                TryWriteToFile(formattedMessage);
                
                // Notify subscribers (UI)
                OnLog?.Invoke(null, formattedMessage);
            }
            catch
            {
                // Never crash due to logging
            }
        }

        private static void TryWriteToFile(string formattedMessage)
        {
            try
            {
                if (!Directory.Exists(LogsDir)) Directory.CreateDirectory(LogsDir);
                var path = GetCurrentLogFilePath();
                if (string.IsNullOrWhiteSpace(path)) return;
                File.AppendAllText(path, formattedMessage + Environment.NewLine);
            }
            catch
            {
                // Fail soft.
            }
        }
        
        /// <summary>
        /// Log an error message
        /// </summary>
        public static void LogError(string message, Exception? ex = null)
        {
            var msg = $"❌ ERROR: {message}";
            if (ex != null)
            {
                msg += $"\n   Details: {ex.Message}";
            }
            Log(msg);
        }
        
        /// <summary>
        /// Log a success/info message
        /// </summary>
        public static void LogInfo(string message)
        {
            Log($"ℹ️ {message}");
        }
        
        /// <summary>
        /// Log a warning message
        /// </summary>
        public static void LogWarning(string message)
        {
            Log($"⚠️ {message}");
        }

        /// <summary>
        /// Get current log history
        /// </summary>
        public static string GetHistory()
        {
            lock (_lock)
            {
                return _buffer.ToString();
            }
        }
    }
}
