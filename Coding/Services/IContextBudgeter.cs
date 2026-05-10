using System;
using System.Collections.Generic;

namespace AtlasAI.Coding.Services
{
    /// <summary>
    /// Interface for context budgeting - constructs LLM context within token limits.
    /// </summary>
    public interface IContextBudgeter
    {
        /// <summary>
        /// Maximum context size in characters.
        /// </summary>
        int MaxContextChars { get; }

        /// <summary>
        /// Build context for LLM from request.
        /// </summary>
        ContextResult BuildContext(AgentContextRequest request);

        /// <summary>
        /// Get the manifest of what was included in the last context.
        /// </summary>
        ContextManifest GetManifest();
    }

    public class AgentContextRequest
    {
        public string UserGoal { get; set; } = "";
        public List<string> Constraints { get; set; } = new();
        public List<OpenFileContext> OpenFiles { get; set; } = new();
        public List<SearchResult> RetrievedSnippets { get; set; } = new();
        public string ProblemsPanelSummary { get; set; } = "";
        public string TerminalOutput { get; set; } = "";
        public string CurrentFilePath { get; set; } = "";
        public string CurrentSelection { get; set; } = "";
    }

    public class OpenFileContext
    {
        public string Path { get; set; } = "";
        public string Content { get; set; } = "";
        public string Language { get; set; } = "";
        public bool IsActive { get; set; }
    }

    public class ContextResult
    {
        public string FullContext { get; set; } = "";
        public int CharCount { get; set; }
        public ContextManifest Manifest { get; set; } = new();
        public bool WasTruncated { get; set; }
        public List<string> TruncatedItems { get; set; } = new();
    }

    public class ContextManifest
    {
        public List<IncludedFile> IncludedFiles { get; set; } = new();
        public List<SnippetReference> IncludedSnippets { get; set; } = new();
        public bool HasProblems { get; set; }
        public bool HasTerminalOutput { get; set; }
        public int TotalChars { get; set; }
        public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
    }

    public class IncludedFile
    {
        public string Path { get; set; } = "";
        public int CharCount { get; set; }
        public bool WasTruncated { get; set; }
    }

    public class SnippetReference
    {
        public string FilePath { get; set; } = "";
        public int StartLine { get; set; }
        public int EndLine { get; set; }
        public string Reason { get; set; } = "";
        public double Score { get; set; }
    }
}
/// 