using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AtlasAI.AI;

namespace AtlasAI.Coding.Services
{
    /// <summary>
    /// Auto-repair service - uses AI to generate minimal patches for build failures.
    /// Implements one-shot repair: only one attempt per failure.
    /// </summary>
    public class AutoRepairService
    {
        public static AutoRepairService Instance { get; } = new();

        private bool _repairAttemptedThisSession = false;
        private string _lastFailureHash = "";

        public event Action<string>? OnStatusChanged;
        public event Action<RepairAttempt>? OnRepairProposed;
        public event Action<RepairAttempt>? OnRepairApplied;
        public event Action<RepairAttempt>? OnRepairFailed;

        private AutoRepairService() { }

        /// <summary>
        /// Attempt to auto-repair a verification failure.
        /// Returns null if repair was already attempted or not possible.
        /// </summary>
        public async Task<RepairAttempt?> AttemptRepairAsync(
            VerificationResult failure,
            string projectPath,
            List<FileChange>? recentChanges = null,
            System.Threading.CancellationToken ct = default)
        {
            var failureHash = GenerateFailureHash(failure);

            if (_repairAttemptedThisSession && failureHash == _lastFailureHash)
            {
                OnStatusChanged?.Invoke("Auto-repair already attempted for this failure");
                return null;
            }

            _repairAttemptedThisSession = true;
            _lastFailureHash = failureHash;

            var attempt = new RepairAttempt
            {
                Status = "Analyzing errors...",
                Timestamp = DateTime.UtcNow
            };

            try
            {
                OnStatusChanged?.Invoke("Analyzing build errors...");

                attempt.ErrorSummary = VerificationRunner.Instance.ExtractErrorSummary(
                    failure,
                    VerificationProfile.Instance.MaxErrorLines);

                var context = await BuildRepairContextAsync(failure, projectPath, recentChanges);

                OnStatusChanged?.Invoke("Generating repair patch...");

                var patch = await RequestRepairFromAIAsync(context, ct);

                if (string.IsNullOrEmpty(patch))
                {
                    attempt.Status = "AI could not generate a repair";
                    OnRepairFailed?.Invoke(attempt);
                    return attempt;
                }

                attempt.ProposedPatch = patch;
                attempt.Status = "Repair proposed";
                attempt.Changes = ParsePatchToChanges(patch, projectPath);

                OnRepairProposed?.Invoke(attempt);

                return attempt;
            }
            catch (OperationCanceledException)
            {
                attempt.Status = "CANCELLED · OPERATION STOPPED";
                OnRepairFailed?.Invoke(attempt);
                return attempt;
            }
            catch (Exception ex)
            {
                attempt.Status = $"Repair failed: {ex.Message}";
                OnRepairFailed?.Invoke(attempt);
                AgentModeLogger.Instance.LogError("AttemptRepairAsync", ex);
                return attempt;
            }
        }

        /// <summary>
        /// Apply a proposed repair and re-run verification.
        /// </summary>
        public async Task<RepairAttempt> ApplyRepairAsync(
            RepairAttempt attempt,
            string projectPath)
        {
            try
            {
                OnStatusChanged?.Invoke("Applying repair patch...");

                foreach (var change in attempt.Changes)
                {
                    ApplyFileChange(change, projectPath);
                }

                attempt.Applied = true;
                attempt.Status = "Patch applied, re-running verification...";
                OnRepairApplied?.Invoke(attempt);

                OnStatusChanged?.Invoke("Re-running build...");

                var profile = VerificationProfile.Instance;
                var rerunResult = await VerificationRunner.Instance.RunCommandAsync(
                    profile.BuildCommand,
                    projectPath,
                    VerificationType.Build,
                    profile.TimeoutSeconds);

                attempt.RerunResult = rerunResult;

                if (rerunResult.Success)
                {
                    attempt.Status = "✅ Repair successful!";
                    OnStatusChanged?.Invoke("Auto-repair successful!");
                }
                else
                {
                    attempt.Status = "❌ Repair did not fix the issue";
                    OnStatusChanged?.Invoke("Auto-repair did not fix the issue. Manual intervention required.");
                }

                return attempt;
            }
            catch (Exception ex)
            {
                attempt.Status = $"Apply failed: {ex.Message}";
                OnRepairFailed?.Invoke(attempt);
                AgentModeLogger.Instance.LogError("ApplyRepairAsync", ex);
                return attempt;
            }
        }

        /// <summary>
        /// Reset the repair state for a new task.
        /// </summary>
        public void Reset()
        {
            _repairAttemptedThisSession = false;
            _lastFailureHash = "";
        }

        private string GenerateFailureHash(VerificationResult failure)
        {
            var sb = new StringBuilder();
            foreach (var error in failure.Errors.Take(5))
            {
                sb.Append($"{error.FilePath}:{error.Line}:{error.Code}|");
            }
            return sb.ToString().GetHashCode().ToString("X8");
        }

        private async Task<string> BuildRepairContextAsync(
            VerificationResult failure,
            string projectPath,
            List<FileChange>? recentChanges)
        {
            var sb = new StringBuilder();

            sb.AppendLine("=== BUILD FAILURE CONTEXT ===");
            sb.AppendLine();
            sb.AppendLine("## Error Summary");
            sb.AppendLine(VerificationRunner.Instance.ExtractErrorSummary(failure, 30));
            sb.AppendLine();

            var errorFiles = failure.Errors
                .Where(e => !string.IsNullOrEmpty(e.FilePath))
                .Select(e => e.FilePath)
                .Distinct()
                .Take(3);

            foreach (var filePath in errorFiles)
            {
                var fullPath = Path.IsPathRooted(filePath)
                    ? filePath
                    : Path.Combine(projectPath, filePath);

                if (File.Exists(fullPath))
                {
                    try
                    {
                        var content = await File.ReadAllTextAsync(fullPath);
                        var lines = content.Split('\n');

                        var errorLines = failure.Errors
                            .Where(e => e.FilePath == filePath)
                            .Select(e => e.Line)
                            .Distinct();

                        sb.AppendLine($"## File: {filePath}");

                        foreach (var errorLine in errorLines.Take(3))
                        {
                            var startLine = Math.Max(0, errorLine - 5);
                            var endLine = Math.Min(lines.Length, errorLine + 5);

                            sb.AppendLine($"### Around line {errorLine}:");
                            sb.AppendLine("```");
                            for (int i = startLine; i < endLine; i++)
                            {
                                var marker = i == errorLine - 1 ? ">>> " : "    ";
                                sb.AppendLine($"{marker}{i + 1}: {lines[i]}");
                            }
                            sb.AppendLine("```");
                        }
                        sb.AppendLine();
                    }
                    catch (Exception ex)
                    {
                        AgentModeLogger.Instance.LogError("AutoRepairService.BuildContext", ex);
                    }
                }
            }

            if (recentChanges != null && recentChanges.Count > 0)
            {
                sb.AppendLine("## Recent Changes (potential cause)");
                foreach (var change in recentChanges.Take(5))
                {
                    sb.AppendLine($"- {change.ChangeType}: {change.FilePath}");
                }
                sb.AppendLine();
            }

            return sb.ToString();
        }

        private async Task<string> RequestRepairFromAIAsync(string context, System.Threading.CancellationToken ct = default)
        {
            var prompt = $@"You are a code repair assistant. Analyze the following build failure and provide a MINIMAL fix.

{context}

INSTRUCTIONS:
1. Identify the root cause of the error(s)
2. Provide ONLY the minimal code changes needed to fix the issue
3. Use this exact format for each file change:

[FILE: path/to/file.cs]
[REPLACE]
<<<<<<< OLD
exact old code to replace
=======
new fixed code
>>>>>>> NEW
[END]

RULES:
- Only fix the actual errors, don't refactor or improve other code
- Include enough context in OLD section to uniquely identify the location
- Keep changes as small as possible
- If you cannot determine a fix, respond with: [NO_FIX_POSSIBLE]

Provide your fix now:";

            try
            {
                var messages = new List<object>
                {
                    new { role = "system", content = "You are a precise code repair assistant. You analyze build errors and provide minimal, targeted fixes." },
                    new { role = "user", content = prompt }
                };

                var response = await AIManager.SendMessageAsync(messages, 2048, ct);

                if (response?.Success == true && !string.IsNullOrEmpty(response.Content))
                {
                    if (response.Content.Contains("[NO_FIX_POSSIBLE]"))
                    {
                        return "";
                    }
                    return response.Content;
                }
            }
            catch (Exception ex)
            {
                AgentModeLogger.Instance.LogError("RequestRepairFromAIAsync", ex);
            }

            return "";
        }

        private List<FileChange> ParsePatchToChanges(string patch, string projectPath)
        {
            var changes = new List<FileChange>();

            var filePattern = new Regex(
                @"\[FILE:\s*(.+?)\]\s*\[REPLACE\]\s*<<<<<<< OLD\s*\n([\s\S]*?)\n=======\s*\n([\s\S]*?)\n>>>>>>> NEW\s*\[END\]",
                RegexOptions.Multiline);

            foreach (Match match in filePattern.Matches(patch))
            {
                var filePath = match.Groups[1].Value.Trim();
                var oldCode = match.Groups[2].Value;
                var newCode = match.Groups[3].Value;

                changes.Add(new FileChange
                {
                    FilePath = filePath,
                    ChangeType = FileChangeType.Modify,
                    OldContent = oldCode,
                    NewContent = newCode
                });
            }

            return changes;
        }

        private void ApplyFileChange(FileChange change, string projectPath)
        {
            var fullPath = Path.IsPathRooted(change.FilePath)
                ? change.FilePath
                : Path.Combine(projectPath, change.FilePath);

            if (!File.Exists(fullPath))
            {
                throw new FileNotFoundException($"File not found: {change.FilePath}");
            }

            var content = File.ReadAllText(fullPath);
            var normalizedOld = change.OldContent?.Replace("\r\n", "\n").Trim() ?? "";
            var normalizedContent = content.Replace("\r\n", "\n");

            if (!normalizedContent.Contains(normalizedOld))
            {
                throw new InvalidOperationException($"Could not find old code block in {change.FilePath}");
            }

            var newContent = normalizedContent.Replace(normalizedOld, change.NewContent?.Trim() ?? "");
            File.WriteAllText(fullPath, newContent);

            System.Diagnostics.Debug.WriteLine($"[AutoRepair] Applied change to {change.FilePath}");
        }
    }
}