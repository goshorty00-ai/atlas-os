using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace AtlasAI.Coding.Services
{
    /// <summary>
    /// Verification pipeline - runs build/tests/lint after agent actions.
    /// Implements auto-repair with single-attempt limit.
    /// </summary>
    public class AgentVerificationPipeline : IVerificationPipeline
    {
        private const int DefaultTimeoutSeconds = 60;
        private bool _repairAttempted = false;

        public static AgentVerificationPipeline Instance { get; } = new();

        private AgentVerificationPipeline() { }

        public async Task<VerificationResult> RunBuildAsync(string projectPath)
        {
            var sw = Stopwatch.StartNew();
            var result = new VerificationResult
            {
                Type = VerificationType.Build,
                Timestamp = DateTime.UtcNow
            };

            try
            {
                var (command, args) = DetectBuildCommand(projectPath);
                result.Command = $"{command} {args}";

                var (exitCode, output, errorOutput) = await RunCommandAsync(command, args, projectPath, DefaultTimeoutSeconds);

                result.ExitCode = exitCode;
                result.Output = output;
                result.ErrorOutput = errorOutput;
                result.Success = exitCode == 0;
                result.Errors = ParseBuildErrors(output + "\n" + errorOutput);
                result.Summary = GenerateSummary(result);
                result.Duration = sw.Elapsed;

                AgentModeLogger.Instance.LogVerification(result);
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorOutput = ex.Message;
                result.Summary = $"Build failed: {ex.Message}";
                AgentModeLogger.Instance.LogError("RunBuildAsync", ex);
            }

            return result;
        }

        public async Task<VerificationResult> RunTestsAsync(string projectPath)
        {
            var sw = Stopwatch.StartNew();
            var result = new VerificationResult
            {
                Type = VerificationType.Test,
                Timestamp = DateTime.UtcNow
            };

            try
            {
                var (command, args) = DetectTestCommand(projectPath);
                result.Command = $"{command} {args}";

                var (exitCode, output, errorOutput) = await RunCommandAsync(command, args, projectPath, DefaultTimeoutSeconds);

                result.ExitCode = exitCode;
                result.Output = output;
                result.ErrorOutput = errorOutput;
                result.Success = exitCode == 0;
                result.Errors = ParseTestErrors(output + "\n" + errorOutput);
                result.Summary = GenerateSummary(result);
                result.Duration = sw.Elapsed;

                AgentModeLogger.Instance.LogVerification(result);
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorOutput = ex.Message;
                result.Summary = $"Tests failed: {ex.Message}";
                AgentModeLogger.Instance.LogError("RunTestsAsync", ex);
            }

            return result;
        }

        public async Task<VerificationResult> RunLintAsync(string projectPath)
        {
            var sw = Stopwatch.StartNew();
            var result = new VerificationResult
            {
                Type = VerificationType.Lint,
                Timestamp = DateTime.UtcNow
            };

            try
            {
                var command = "dotnet";
                var args = "format --verify-no-changes";
                result.Command = $"{command} {args}";

                var (exitCode, output, errorOutput) = await RunCommandAsync(command, args, projectPath, DefaultTimeoutSeconds);

                result.ExitCode = exitCode;
                result.Output = output;
                result.ErrorOutput = errorOutput;
                result.Success = exitCode == 0;
                result.Errors = ParseLintErrors(output + "\n" + errorOutput);
                result.Summary = GenerateSummary(result);
                result.Duration = sw.Elapsed;

                AgentModeLogger.Instance.LogVerification(result);
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorOutput = ex.Message;
                result.Summary = $"Lint failed: {ex.Message}";
                AgentModeLogger.Instance.LogError("RunLintAsync", ex);
            }

            return result;
        }

        public async Task<VerificationResult> RunAllAsync(string projectPath, VerificationOptions options)
        {
            var combinedResult = new VerificationResult
            {
                Type = VerificationType.All,
                Success = true,
                Timestamp = DateTime.UtcNow
            };

            var allErrors = new List<ErrorEntry>();
            var outputs = new List<string>();

            if (options.RunBuild)
            {
                var buildResult = await RunBuildAsync(projectPath);
                if (!buildResult.Success)
                {
                    combinedResult.Success = false;
                    allErrors.AddRange(buildResult.Errors);
                }
                outputs.Add($"[BUILD] {buildResult.Summary}");
                combinedResult.Duration += buildResult.Duration;
            }

            if (options.RunTests && combinedResult.Success)
            {
                var testResult = await RunTestsAsync(projectPath);
                if (!testResult.Success)
                {
                    combinedResult.Success = false;
                    allErrors.AddRange(testResult.Errors);
                }
                outputs.Add($"[TEST] {testResult.Summary}");
                combinedResult.Duration += testResult.Duration;
            }

            if (options.RunLint && combinedResult.Success)
            {
                var lintResult = await RunLintAsync(projectPath);
                if (!lintResult.Success)
                {
                    combinedResult.Success = false;
                    allErrors.AddRange(lintResult.Errors);
                }
                outputs.Add($"[LINT] {lintResult.Summary}");
                combinedResult.Duration += lintResult.Duration;
            }

            combinedResult.Errors = allErrors;
            combinedResult.Output = string.Join("\n", outputs);
            combinedResult.Summary = combinedResult.Success ? "All verifications passed" : $"Verification failed: {allErrors.Count} error(s)";

            if (!combinedResult.Success && options.AutoRepairOnce && !_repairAttempted)
            {
                var repairResult = await AttemptAutoRepairAsync(combinedResult, projectPath);
                if (repairResult.Success && repairResult.RerunResult != null)
                {
                    return repairResult.RerunResult;
                }
            }

            return combinedResult;
        }

        public async Task<RepairResult> AttemptAutoRepairAsync(VerificationResult failure, string projectPath)
        {
            var result = new RepairResult { Attempted = false };

            if (_repairAttempted)
            {
                result.ErrorMessage = "Auto-repair already attempted once. Manual intervention required.";
                AgentModeLogger.Instance.LogRepairAttempt(result);
                return result;
            }

            _repairAttempted = true;
            result.Attempted = true;

            try
            {
                var errorContext = BuildErrorContext(failure);
                var repairPatch = await RequestRepairFromAIAsync(errorContext, projectPath);

                if (string.IsNullOrEmpty(repairPatch))
                {
                    result.Success = false;
                    result.ErrorMessage = "AI could not generate a repair patch";
                    AgentModeLogger.Instance.LogRepairAttempt(result);
                    return result;
                }

                result.PatchApplied = repairPatch;
                var changes = ApplyPatch(repairPatch, projectPath);
                result.Changes = changes;

                var rerunResult = await RunBuildAsync(projectPath);
                result.RerunResult = rerunResult;
                result.Success = rerunResult.Success;

                if (!result.Success)
                {
                    result.ErrorMessage = "Repair patch did not fix the issue";
                }

                AgentModeLogger.Instance.LogRepairAttempt(result);
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
                AgentModeLogger.Instance.LogError("AttemptAutoRepairAsync", ex);
            }

            return result;
        }

        public void ResetRepairState()
        {
            _repairAttempted = false;
        }

        #region Command Detection

        private (string command, string args) DetectBuildCommand(string projectPath)
        {
            if (Directory.GetFiles(projectPath, "*.csproj").Length > 0 ||
                Directory.GetFiles(projectPath, "*.sln").Length > 0)
            {
                return ("dotnet", "build");
            }

            if (File.Exists(Path.Combine(projectPath, "package.json")))
            {
                return ("npm", "run build");
            }

            if (File.Exists(Path.Combine(projectPath, "setup.py")) ||
                File.Exists(Path.Combine(projectPath, "pyproject.toml")))
            {
                return ("python", "-m py_compile .");
            }

            return ("dotnet", "build");
        }

        private (string command, string args) DetectTestCommand(string projectPath)
        {
            if (Directory.GetFiles(projectPath, "*.csproj").Length > 0)
            {
                return ("dotnet", "test --no-build");
            }

            if (File.Exists(Path.Combine(projectPath, "package.json")))
            {
                return ("npm", "test");
            }

            return ("dotnet", "test");
        }

        #endregion

        #region Command Execution

        private async Task<(int exitCode, string output, string errorOutput)> RunCommandAsync(
            string command, string args, string workingDir, int timeoutSeconds)
        {
            var psi = new ProcessStartInfo
            {
                FileName = command,
                Arguments = args,
                WorkingDirectory = workingDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = psi };
            var outputBuilder = new System.Text.StringBuilder();
            var errorBuilder = new System.Text.StringBuilder();

            process.OutputDataReceived += (s, e) => { if (e.Data != null) outputBuilder.AppendLine(e.Data); };
            process.ErrorDataReceived += (s, e) => { if (e.Data != null) errorBuilder.AppendLine(e.Data); };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            var completed = await Task.Run(() => process.WaitForExit(timeoutSeconds * 1000));

            if (!completed)
            {
                process.Kill();
                return (-1, outputBuilder.ToString(), "Process timed out");
            }

            return (process.ExitCode, outputBuilder.ToString(), errorBuilder.ToString());
        }

        #endregion

        #region Error Parsing

        private List<ErrorEntry> ParseBuildErrors(string output)
        {
            var errors = new List<ErrorEntry>();
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

            return errors;
        }

        private List<ErrorEntry> ParseTestErrors(string output)
        {
            var errors = new List<ErrorEntry>();
            var failedPattern = new Regex(@"Failed\s+(.+?)\s*\[", RegexOptions.Multiline);
            foreach (Match match in failedPattern.Matches(output))
            {
                errors.Add(new ErrorEntry
                {
                    Message = $"Test failed: {match.Groups[1].Value}",
                    Severity = ErrorSeverity.Error
                });
            }

            var exceptionPattern = new Regex(@"(System\.\w+Exception):\s*(.+)", RegexOptions.Multiline);
            foreach (Match match in exceptionPattern.Matches(output))
            {
                errors.Add(new ErrorEntry
                {
                    Code = match.Groups[1].Value,
                    Message = match.Groups[2].Value.Trim(),
                    Severity = ErrorSeverity.Error
                });
            }

            return errors;
        }

        private List<ErrorEntry> ParseLintErrors(string output)
        {
            var errors = new List<ErrorEntry>();
            var lintPattern = new Regex(@"(.+?):(\d+):(\d+):\s*(error|warning|info):\s*(.+)", RegexOptions.Multiline);
            foreach (Match match in lintPattern.Matches(output))
            {
                var severity = match.Groups[4].Value switch
                {
                    "error" => ErrorSeverity.Error,
                    "warning" => ErrorSeverity.Warning,
                    _ => ErrorSeverity.Info
                };

                errors.Add(new ErrorEntry
                {
                    FilePath = match.Groups[1].Value.Trim(),
                    Line = int.Parse(match.Groups[2].Value),
                    Column = int.Parse(match.Groups[3].Value),
                    Severity = severity,
                    Message = match.Groups[5].Value.Trim()
                });
            }

            return errors;
        }

        #endregion

        #region Helpers

        private string GenerateSummary(VerificationResult result)
        {
            if (result.Success)
            {
                return $"{result.Type} succeeded in {result.Duration.TotalSeconds:F1}s";
            }

            var errorCount = result.Errors.Count(e => e.Severity == ErrorSeverity.Error);
            var warningCount = result.Errors.Count(e => e.Severity == ErrorSeverity.Warning);

            return $"{result.Type} failed: {errorCount} error(s), {warningCount} warning(s)";
        }

        private string BuildErrorContext(VerificationResult failure)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Verification Type: {failure.Type}");
            sb.AppendLine($"Command: {failure.Command}");
            sb.AppendLine($"Exit Code: {failure.ExitCode}");
            sb.AppendLine();
            sb.AppendLine("Errors:");

            foreach (var error in failure.Errors.Take(10))
            {
                sb.AppendLine($"  [{error.Severity}] {error.FilePath}:{error.Line}:{error.Column}");
                sb.AppendLine($"    {error.Code}: {error.Message}");
            }

            if (failure.Errors.Count > 10)
            {
                sb.AppendLine($"  ... and {failure.Errors.Count - 10} more errors");
            }

            return sb.ToString();
        }

        private async Task<string> RequestRepairFromAIAsync(string errorContext, string projectPath)
        {
            await Task.Delay(100);
            return "";
        }

        private List<FileChange> ApplyPatch(string patch, string projectPath)
        {
            var changes = new List<FileChange>();
            Debug.WriteLine($"[AGENT2] Would apply patch:\n{patch}");
            return changes;
        }

        #endregion
    }
}