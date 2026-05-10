using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AtlasAI.Coding.Services
{
    /// <summary>
    /// Interface for agent skills - repeatable playbooks for common tasks.
    /// </summary>
    public interface IAgentSkill
    {
        /// <summary>
        /// Unique name of the skill.
        /// </summary>
        string Name { get; }
        
        /// <summary>
        /// Human-readable description.
        /// </summary>
        string Description { get; }
        
        /// <summary>
        /// Keywords that trigger this skill.
        /// </summary>
        List<string> TriggerKeywords { get; }
        
        /// <summary>
        /// Execute the skill with given context.
        /// </summary>
        Task<SkillResult> ExecuteAsync(SkillContext context);
        
        /// <summary>
        /// Calculate confidence that this skill matches the user request.
        /// </summary>
        double MatchConfidence(string userRequest, AgentContext context);
    }
    
    public class SkillContext
    {
        public string UserRequest { get; set; } = "";
        public string ProjectPath { get; set; } = "";
        public List<string> OpenFiles { get; set; } = new();
        public string ActiveFile { get; set; } = "";
        public string Selection { get; set; } = "";
        public List<SearchResult> RelevantFiles { get; set; } = new();
        public ContextManifest ContextManifest { get; set; } = new();
    }
    
    public class AgentContext
    {
        public string ProjectPath { get; set; } = "";
        public List<string> OpenFiles { get; set; } = new();
        public string ActiveFile { get; set; } = "";
        public string RecentTerminalOutput { get; set; } = "";
        public List<ProblemEntry> Problems { get; set; } = new();
    }
    
    public class SkillResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public List<FileChange> FileChanges { get; set; } = new();
        public string Explanation { get; set; } = "";
        public List<string> NextSteps { get; set; } = new();
        public VerificationResult? VerificationResult { get; set; }
    }
    
    public class FileChange
    {
        public string FilePath { get; set; } = "";
        public FileChangeType ChangeType { get; set; }
        public string? OldContent { get; set; }
        public string? NewContent { get; set; }
        public int? StartLine { get; set; }
        public int? EndLine { get; set; }
    }
    
    public enum FileChangeType
    {
        Create,
        Modify,
        Delete,
        Rename
    }
    
    public class ProblemEntry
    {
        public string FilePath { get; set; } = "";
        public int Line { get; set; }
        public int Column { get; set; }
        public string Message { get; set; } = "";
        public ProblemSeverity Severity { get; set; }
        public string Code { get; set; } = "";
    }
    
    public enum ProblemSeverity
    {
        Error,
        Warning,
        Info,
        Hint
    }
}
