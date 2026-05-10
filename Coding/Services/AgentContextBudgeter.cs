using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace AtlasAI.Coding.Services
{
    /// <summary>
    /// Context budgeting service - constructs LLM context within token limits.
    /// Implements smart truncation with priority ordering.
    /// </summary>
    public class AgentContextBudgeter : IContextBudgeter
    {
        private const int DefaultMaxChars = 25000;
        private ContextManifest _lastManifest = new();

        public int MaxContextChars { get; set; } = DefaultMaxChars;

        public static AgentContextBudgeter Instance { get; } = new();

        private AgentContextBudgeter() { }

        public ContextResult BuildContext(AgentContextRequest request)
        {
            var builder = new StringBuilder();
            var manifest = new ContextManifest();
            var truncatedItems = new List<string>();
            int remaining = MaxContextChars;

            // Priority 1: User goal (always included)
            var goalSection = BuildGoalSection(request);
            builder.Append(goalSection);
            remaining -= goalSection.Length;

            // Priority 2: Current selection/active file context
            if (!string.IsNullOrEmpty(request.CurrentSelection))
            {
                var selectionSection = BuildSelectionSection(request);
                if (selectionSection.Length <= remaining)
                {
                    builder.Append(selectionSection);
                    remaining -= selectionSection.Length;
                }
                else
                {
                    truncatedItems.Add("Current selection");
                }
            }

            // Priority 3: Open files (full content)
            foreach (var file in request.OpenFiles.OrderByDescending(f => f.IsActive))
            {
                var fileSection = BuildFileSection(file);
                if (fileSection.Length <= remaining)
                {
                    builder.Append(fileSection);
                    remaining -= fileSection.Length;
                    manifest.IncludedFiles.Add(new IncludedFile
                    {
                        Path = file.Path,
                        CharCount = fileSection.Length,
                        WasTruncated = false
                    });
                }
                else if (remaining > 500)
                {
                    // Truncate file content
                    var truncated = TruncateFileSection(file, remaining - 100);
                    builder.Append(truncated);
                    remaining -= truncated.Length;
                    manifest.IncludedFiles.Add(new IncludedFile
                    {
                        Path = file.Path,
                        CharCount = truncated.Length,
                        WasTruncated = true
                    });
                    truncatedItems.Add($"File: {file.Path}");
                }
                else
                {
                    truncatedItems.Add($"File: {file.Path} (omitted)");
                }
            }

            // Priority 4: Retrieved snippets
            foreach (var snippet in request.RetrievedSnippets.OrderByDescending(s => s.Score))
            {
                var snippetSection = BuildSnippetSection(snippet);
                if (snippetSection.Length <= remaining)
                {
                    builder.Append(snippetSection);
                    remaining -= snippetSection.Length;
                    manifest.IncludedSnippets.Add(new SnippetReference
                    {
                        FilePath = snippet.FilePath,
                        StartLine = snippet.Line,
                        EndLine = snippet.Line + 20,
                        Reason = snippet.Reason,
                        Score = snippet.Score
                    });
                }
                else
                {
                    truncatedItems.Add($"Snippet: {snippet.RelativePath}");
                }
            }

            // Priority 5: Problems panel
            if (!string.IsNullOrEmpty(request.ProblemsPanelSummary))
            {
                var problemsSection = BuildProblemsSection(request.ProblemsPanelSummary);
                if (problemsSection.Length <= remaining)
                {
                    builder.Append(problemsSection);
                    remaining -= problemsSection.Length;
                    manifest.HasProblems = true;
                }
                else if (remaining > 200)
                {
                    var truncated = TruncateSection("PROBLEMS", request.ProblemsPanelSummary, remaining - 50);
                    builder.Append(truncated);
                    remaining -= truncated.Length;
                    manifest.HasProblems = true;
                    truncatedItems.Add("Problems panel");
                }
            }

            // Priority 6: Terminal output (lowest priority)
            if (!string.IsNullOrEmpty(request.TerminalOutput))
            {
                var terminalSection = BuildTerminalSection(request.TerminalOutput);
                if (terminalSection.Length <= remaining)
                {
                    builder.Append(terminalSection);
                    remaining -= terminalSection.Length;
                    manifest.HasTerminalOutput = true;
                }
                else if (remaining > 200)
                {
                    var truncated = TruncateSection("TERMINAL OUTPUT", request.TerminalOutput, remaining - 50);
                    builder.Append(truncated);
                    manifest.HasTerminalOutput = true;
                    truncatedItems.Add("Terminal output");
                }
            }

            // Add context manifest section
            var manifestSection = BuildManifestSection(manifest);
            builder.Append(manifestSection);

            manifest.TotalChars = builder.Length;
            manifest.GeneratedAt = DateTime.UtcNow;
            _lastManifest = manifest;

            AgentModeLogger.Instance.LogContextManifest(manifest);

            return new ContextResult
            {
                FullContext = builder.ToString(),
                CharCount = builder.Length,
                Manifest = manifest,
                WasTruncated = truncatedItems.Count > 0,
                TruncatedItems = truncatedItems
            };
        }

        public ContextManifest GetManifest() => _lastManifest;

        #region Section Builders

        private string BuildGoalSection(AgentContextRequest request)
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== USER GOAL ===");
            sb.AppendLine(request.UserGoal);

            if (request.Constraints.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Constraints:");
                foreach (var constraint in request.Constraints)
                {
                    sb.AppendLine($"- {constraint}");
                }
            }
            sb.AppendLine();
            return sb.ToString();
        }

        private string BuildSelectionSection(AgentContextRequest request)
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== CURRENT SELECTION ===");
            sb.AppendLine($"File: {request.CurrentFilePath}");
            sb.AppendLine("```");
            sb.AppendLine(request.CurrentSelection);
            sb.AppendLine("```");
            sb.AppendLine();
            return sb.ToString();
        }

        private string BuildFileSection(OpenFileContext file)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"=== FILE: {file.Path} ===");
            if (file.IsActive)
                sb.AppendLine("[ACTIVE]");
            sb.AppendLine($"Language: {file.Language}");
            sb.AppendLine("```" + file.Language);
            sb.AppendLine(file.Content);
            sb.AppendLine("```");
            sb.AppendLine();
            return sb.ToString();
        }

        private string TruncateFileSection(OpenFileContext file, int maxLength)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"=== FILE: {file.Path} (TRUNCATED) ===");
            if (file.IsActive)
                sb.AppendLine("[ACTIVE]");
            sb.AppendLine($"Language: {file.Language}");
            sb.AppendLine("```" + file.Language);

            var contentMax = maxLength - sb.Length - 50;
            if (contentMax > 0 && file.Content.Length > contentMax)
            {
                sb.AppendLine(file.Content[..contentMax]);
                sb.AppendLine("... [truncated]");
            }
            else
            {
                sb.AppendLine(file.Content);
            }

            sb.AppendLine("```");
            sb.AppendLine();
            return sb.ToString();
        }

        private string BuildSnippetSection(SearchResult snippet)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"=== SNIPPET: {snippet.RelativePath}:{snippet.Line} ===");
            sb.AppendLine($"Reason: {snippet.Reason} (score: {snippet.Score:F2})");
            sb.AppendLine("```");
            sb.AppendLine(snippet.Snippet);
            sb.AppendLine("```");
            sb.AppendLine();
            return sb.ToString();
        }

        private string BuildProblemsSection(string problems)
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== PROBLEMS ===");
            sb.AppendLine(problems);
            sb.AppendLine();
            return sb.ToString();
        }

        private string BuildTerminalSection(string output)
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== TERMINAL OUTPUT ===");
            sb.AppendLine("```");
            sb.AppendLine(output);
            sb.AppendLine("```");
            sb.AppendLine();
            return sb.ToString();
        }

        private string TruncateSection(string title, string content, int maxLength)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"=== {title} (TRUNCATED) ===");

            var contentMax = maxLength - sb.Length - 30;
            if (contentMax > 0 && content.Length > contentMax)
            {
                sb.AppendLine(content[..contentMax]);
                sb.AppendLine("... [truncated]");
            }
            else
            {
                sb.AppendLine(content);
            }
            sb.AppendLine();
            return sb.ToString();
        }

        private string BuildManifestSection(ContextManifest manifest)
        {
            var sb = new StringBuilder();
            sb.AppendLine();
            sb.AppendLine("=== CONTEXT MANIFEST ===");
            sb.AppendLine($"Generated: {manifest.GeneratedAt:O}");

            if (manifest.IncludedFiles.Count > 0)
            {
                sb.AppendLine("Included files:");
                foreach (var file in manifest.IncludedFiles)
                {
                    var truncated = file.WasTruncated ? " [truncated]" : "";
                    sb.AppendLine($"  - {file.Path} ({file.CharCount} chars){truncated}");
                }
            }

            if (manifest.IncludedSnippets.Count > 0)
            {
                sb.AppendLine("Included snippets:");
                foreach (var snippet in manifest.IncludedSnippets)
                {
                    sb.AppendLine($"  - {snippet.FilePath}:{snippet.StartLine} ({snippet.Reason}, score: {snippet.Score:F2})");
                }
            }

            sb.AppendLine($"Has problems: {manifest.HasProblems}");
            sb.AppendLine($"Has terminal: {manifest.HasTerminalOutput}");
            sb.AppendLine("=== END MANIFEST ===");

            return sb.ToString();
        }

        #endregion
    }
}