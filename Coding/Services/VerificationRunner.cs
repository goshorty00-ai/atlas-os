using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace AtlasAI.Coding.Services
{
    /// <summary>
    /// Verification runner - executes build/test/lint commands with live output streaming.
    /// </summary>
    public class VerificationRunner
    {
        public static VerificationRunner Instance { get; } = new();
        
        // Events for live output
        public event Action<string>? OnOutputLine;
        public event Action<string>? OnErrorLine;
        public event Action<VerificationResult>? OnVerificationComplete;
        public event Action<RepairAttempt>? OnRepairStarted;
        public event Action<RepairAttempt>? OnRepairComplete;
        
        private Process? _currentProcess;
        private CancellationTokenSource? _cts;
        
        private VerificationRunner() { }
        
        /// <summary>
        /// Run a verification command with live output streaming.
        /// </summary>
        public async Task<VerificationResult> RunCommandAsync(
            string command, 
            string workingDir, 
            VerificationType type,
            int timeoutSeconds = 120,
            CancellationToken cancellationToken = default)
        {
            var result = new VerificationResult
            {
                Type = type,
                Command = command,
                Timestamp = DateTime.UtcNow
            };
            
            var sw = Stopwatch.StartNew();
            var outputBuilder = new StringBuilder();
            var errorBuilder = new StringBuilder();
            
            try
            {
                // Parse command into executable and arguments
                var (exe, args) = ParseCommand(command);
                
                var psi = new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = args,
                    WorkingDirectory = workingDir,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                
                using var process = new Process { StartInfo = psi };
                _currentProcess = process;
                
                // Stream output in real-time
                process.OutputDataReceived += (s, e) =>
                {
                    if (e.Data != null)
                    {
                        outputBuilder.AppendLine(e.Data);
                        OnOutputLine?.Invoke(e.Data);
                    }
                };
                
                process.ErrorDataReceived += (s, e) =>
                {
                    if (e.Data != null)
                    {
                        errorBuilder.AppendLine(e.Data);
                        OnErrorLine?.Invoke(e.Data);
                    }
                };
                
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                
                // Wait with timeout
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                linkedCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
                
                try
                {
                    await process.WaitForExitAsync(linkedCts.Token);
                    result.ExitCode = process.ExitCode;
                    result.Success = process.ExitCode == 0;
                }
                catch (OperationCanceledException)
                {
                    process.Kill(true);
                    result.ExitCode = -1;
                    result.Success = false;
                    errorBuilder.AppendLine($"Process timed out after {timeoutSeconds} seconds");
                }
                
                _currentProcess = null;
            }
            catch (Exception ex)
            {
                result.ExitCode = -1;
                result.Success = false;
                errorBuilder.AppendLine($"Error: {ex.Message}");
            }
            
            result.Duration = sw.Elapsed;
            result.Output = outputBuilder.ToString();
            result.ErrorOutput = errorBuilder.ToString();
            result.Errors = ParseErrors(result.Output + "\n" + result.ErrorOutput, type);
            result.Summary = GenerateSummary(result);
            
            OnVerificationComplete?.Invoke(result);
            AgentModeLogger.Instance.LogVerification(result);
            
            return result;
        }
        
        /// <summary>
        /// Cancel the current running verification.
        /// </summary>
        public void Cancel()
        {
            try
            {
                _currentProcess?.Kill(true);
            }
            catch (Exception ex)
            {
                AgentModeLogger.Instance.LogError("VerificationRunner.Cancel", ex);
            }
            
            _cts?.Cancel();
        }
        
        /// <summary>
        /// Extract error summary (top N lines + first error block).
        /// </summary>
        public string ExtractErrorSummary(VerificationResult result, int maxLines = 30)
        {
            var sb = new StringBuilder();
            var fullOutput = result.Output + "\n" + result.ErrorOutput;
            var lines = fullOutput.Split('\n');
            
            // Add command and exit code
            sb.AppendLine($"Command: {result.Command}");
            sb.AppendLine($"Exit Code: {result.ExitCode}");
            sb.AppendLine($"Duration: {result.Duration.TotalSeconds:F1}s");
            sb.AppendLine();
            
            // Find first error block
            var errorBlockStart = -1;
            var errorBlockEnd = -1;
            
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i].ToLower();
                if (line.Contains("error") || line.Contains("failed") || line.Contains("exception"))
                {
                    if (errorBlockStart == -1)
                        errorBlockStart = Math.Max(0, i - 2); // Include 2 lines before
                    errorBlockEnd = Math.Min(lines.Length - 1, i + 5); // Include 5 lines after
                }
            }
            
            // Add error block if found
            if (errorBlockStart >= 0)
            {
                sb.AppendLine("=== First Error Block ===");
                for (int i = errorBlockStart; i <= errorBlockEnd && i < lines.Length; i++)
                {
                    sb.AppendLine(lines[i]);
                }
                sb.AppendLine();
            }
            
            // Add parsed errors
            if (result.Errors.Count > 0)
            {
                sb.AppendLine("=== Parsed Errors ===");
                foreach (var error in result.Errors.Take(10))
                {
                    sb.AppendLine($"[{error.Severity}] {error.FilePath}:{error.Line}:{error.Column}");
                    sb.AppendLine($"  {error.Code}: {error.Message}");
                }
                if (result.Errors.Count > 10)
                {
                    sb.AppendLine($"  ... and {result.Errors.Count - 10} more errors");
                }
                sb.AppendLine();
            }
            
            // Add top N lines of output
            sb.AppendLine($"=== Output (first {maxLines} lines) ===");
            for (int i = 0; i < Math.Min(maxLines, lines.Length); i++)
            {
                sb.AppendLine(lines[i]);
            }
            
            if (lines.Length > maxLines)
            {
                sb.AppendLine($"... ({lines.Length - maxLines} more lines)");
            }
            
            return sb.ToString();
        }
        
        private (string exe, string args) ParseCommand(string command)
        {
            command = command.Trim();
            
            // Handle quoted executable
            if (command.StartsWith("\""))
            {
                var endQuote = command.IndexOf("\"", 1);
                if (endQuote > 0)
                {
                    var exe = command.Substring(1, endQuote - 1);
                    var args = command.Length > endQuote + 1 ? command.Substring(endQuote + 1).Trim() : "";
                    return (exe, args);
                }
            }
            
            // Split on first space
            var spaceIndex = command.IndexOf(' ');
            if (spaceIndex > 0)
            {
                return (command.Substring(0, spaceIndex), command.Substring(spaceIndex + 1).Trim());
            }
            
            return (command, "");
        }
        
        private List<ErrorEntry> ParseErrors(string output, VerificationType type)
        {
            var errors = new List<ErrorEntry>();
            
            // .NET error pattern: file(line,col): error CODE: message
            var dotnetPattern = new Regex(@"(.+?)\((\d+),(\d+)\):\s*(error|warning)\s+(\w+):\s*(.+)", RegexOptions.Multiline);
            foreach (Match match in dotnetPattern.Matches(output))
            {
                errors.Add(new ErrorEntry
                {
                    FilePath = match.Groups[1].Value.Trim(),
                    Line = int.Parse(match.Groups[2].Value),
                    Column = int.Parse(match.Groups[3].Value),
                    Severity = match.Groups[4].Value == "error" ? ErrorSeverity.Error : ErrorSeverity.Warning,
                    Code = match.Groups[5].Value,
                    Message = match.Groups[6].Value.Trim()
                });
            }
            
            // TypeScript/ESLint pattern
            var tsPattern = new Regex(@"(.+?):(\d+):(\d+)\s*-\s*(error|warning)\s+(\w+):\s*(.+)", RegexOptions.Multiline);
            foreach (Match match in tsPattern.Matches(output))
            {
                errors.Add(new ErrorEntry
                {
                    FilePath = match.Groups[1].Value.Trim(),
                    Line = int.Parse(match.Groups[2].Value),
                    Column = int.Parse(match.Groups[3].Value),
                    Severity = match.Groups[4].Value == "error" ? ErrorSeverity.Error : ErrorSeverity.Warning,
                    Code = match.Groups[5].Value,
                    Message = match.Groups[6].Value.Trim()
                });
            }
            
            // Test failure patterns
            if (type == VerificationType.Test)
            {
                var failedPattern = new Regex(@"Failed\s+(.+?)\s*\[", RegexOptions.Multiline);
                foreach (Match match in failedPattern.Matches(output))
                {
                    errors.Add(new ErrorEntry
                    {
                        Message = $"Test failed: {match.Groups[1].Value}",
                        Severity = ErrorSeverity.Error
                    });
                }
            }
            
            return errors;
        }
        
        private string GenerateSummary(VerificationResult result)
        {
            if (result.Success)
            {
                return $"{result.Type} succeeded in {result.Duration.TotalSeconds:F1}s";
            }
            
            var errorCount = result.Errors.Count(e => e.Severity == ErrorSeverity.Error);
            var warningCount = result.Errors.Count(e => e.Severity == ErrorSeverity.Warning);
            
            if (errorCount > 0 || warningCount > 0)
            {
                return $"{result.Type} failed: {errorCount} error(s), {warningCount} warning(s)";
            }
            
            return $"{result.Type} failed (exit code {result.ExitCode})";
        }
    }
    
    /// <summary>
    /// Represents an auto-repair attempt.
    /// </summary>
    public class RepairAttempt
    {
        public string ErrorSummary { get; set; } = "";
        public string ProposedPatch { get; set; } = "";
        public List<FileChange> Changes { get; set; } = new();
        public bool Applied { get; set; }
        public VerificationResult? RerunResult { get; set; }
        public string Status { get; set; } = "Pending";
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }
}
