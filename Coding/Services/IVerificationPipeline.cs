using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AtlasAI.Coding.Services
{
    /// <summary>
    /// Interface for verification pipeline - runs build/tests/lint after agent actions.
    /// </summary>
    public interface IVerificationPipeline
    {
        /// <summary>
        /// Run build verification.
        /// </summary>
        Task<VerificationResult> RunBuildAsync(string projectPath);
        
        /// <summary>
        /// Run test verification.
        /// </summary>
        Task<VerificationResult> RunTestsAsync(string projectPath);
        
        /// <summary>
        /// Run lint verification.
        /// </summary>
        Task<VerificationResult> RunLintAsync(string projectPath);
        
        /// <summary>
        /// Run all enabled verifications.
        /// </summary>
        Task<VerificationResult> RunAllAsync(string projectPath, VerificationOptions options);
        
        /// <summary>
        /// Attempt auto-repair for a failed verification.
        /// </summary>
        Task<RepairResult> AttemptAutoRepairAsync(VerificationResult failure, string projectPath);
    }
    
    public class VerificationResult
    {
        public bool Success { get; set; }
        public VerificationType Type { get; set; }
        public string Command { get; set; } = "";
        public int ExitCode { get; set; }
        public string Output { get; set; } = "";
        public string ErrorOutput { get; set; } = "";
        public List<ErrorEntry> Errors { get; set; } = new();
        public string Summary { get; set; } = "";
        public TimeSpan Duration { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }
    
    public enum VerificationType
    {
        Build,
        Test,
        Lint,
        All
    }
    
    public class ErrorEntry
    {
        public string FilePath { get; set; } = "";
        public int Line { get; set; }
        public int Column { get; set; }
        public string Code { get; set; } = "";
        public string Message { get; set; } = "";
        public ErrorSeverity Severity { get; set; }
    }
    
    public enum ErrorSeverity
    {
        Error,
        Warning,
        Info
    }
    
    public class VerificationOptions
    {
        public bool RunBuild { get; set; } = true;
        public bool RunTests { get; set; } = false;
        public bool RunLint { get; set; } = false;
        public bool AutoRepairOnce { get; set; } = true;
        public VerificationTiming Timing { get; set; } = VerificationTiming.AtEnd;
        public int TimeoutSeconds { get; set; } = 60;
    }
    
    public enum VerificationTiming
    {
        AfterEachStage,
        AtEnd
    }
    
    public class RepairResult
    {
        public bool Attempted { get; set; }
        public bool Success { get; set; }
        public string PatchApplied { get; set; } = "";
        public List<FileChange> Changes { get; set; } = new();
        public VerificationResult? RerunResult { get; set; }
        public string ErrorMessage { get; set; } = "";
    }
}
