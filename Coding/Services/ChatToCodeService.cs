#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace AtlasAI.Coding.Services
{
    /// <summary>
    /// Chat-to-Code service - handles AI-generated code changes with apply modes.
    /// Supports selection, file, and multi-file apply workflows.
    /// </summary>
    public class ChatToCodeService
    {
        private readonly DiffEngine _diffEngine = DiffEngine.Instance;
        private readonly StageSnapshotStore _stageStore = StageSnapshotStore.Instance;
        
        public static ChatToCodeService Instance { get; } = new();
        
        // Events
        public event Action<PendingChange>? OnChangeReady;
        public event Action<ApplyResult>? OnChangeApplied;
        public event Action<string>? OnError;
        
        // Current pending changes
        private readonly List<PendingChange> _pendingChanges = new();
        public IReadOnlyList<PendingChange> PendingChanges => _pendingChanges.AsReadOnly();
        
        /// <summary>
        /// Parse AI response for code changes and create pending changes.
        /// </summary>
        public List<PendingChange> ParseAIResponse(string response, CodeContext context)
        {
            var changes = new List<PendingChange>();
            
            // Extract code blocks from response
            var codeBlocks = ExtractCodeBlocks(response);
            
            foreach (var block in codeBlocks)
            {
                var change = new PendingChange
                {
                    Id = Guid.NewGuid().ToString(),
                    Language = block.Language,
                    ProposedCode = block.Code,
                    Description = block.Description,
                    CreatedAt = DateTime.UtcNow
                };
                
                // Determine apply mode based on context
                if (context.HasSelection)
                {
                    change.ApplyMode = ApplyMode.Selection;
                    change.FilePath = context.FilePath;
                    change.SelectionStart = context.SelectionStart;
                    change.SelectionEnd = context.SelectionEnd;
                    change.OriginalCode = context.SelectedText;
                    
                    // Compute selection diff
                    if (!string.IsNullOrEmpty(context.FullContent))
                    {
                        change.SelectionDiff = _diffEngine.ComputeSelectionDiff(
                            context.FullContent,
                            context.SelectionStart,
                            context.SelectionEnd,
                            block.Code,
                            context.FilePath);
                    }
                }
                else if (!string.IsNullOrEmpty(context.FilePath))
                {
                    change.ApplyMode = ApplyMode.File;
                    change.FilePath = context.FilePath;
                    change.OriginalCode = context.FullContent;
                    
                    // Compute file diff
                    if (!string.IsNullOrEmpty(context.FullContent))
                    {
                        change.Diff = _diffEngine.ComputeDiff(context.FullContent, block.Code, context.FilePath);
                    }
                }
                else
                {
                    change.ApplyMode = ApplyMode.NewFile;
                    change.FilePath = block.SuggestedFileName ?? "untitled";
                }
                
                changes.Add(change);
                _pendingChanges.Add(change);
                OnChangeReady?.Invoke(change);
            }
            
            return changes;
        }
        
        /// <summary>
        /// Apply a pending change to the editor/file.
        /// </summary>
        public async Task<ApplyResult> ApplyChangeAsync(string changeId, ApplyOptions? options = null)
        {
            var change = _pendingChanges.FirstOrDefault(c => c.Id == changeId);
            if (change == null)
            {
                return new ApplyResult { Success = false, ErrorMessage = "Change not found" };
            }
            
            options ??= new ApplyOptions();
            
            try
            {
                // Create stage for undo support
                Stage? stage = null;
                if (options.CreateStage)
                {
                    stage = _stageStore.BeginStage($"Apply: {change.Description}");
                    if (!string.IsNullOrEmpty(change.FilePath))
                    {
                        _stageStore.CaptureBeforeState(stage, change.FilePath);
                    }
                }
                
                var result = new ApplyResult
                {
                    ChangeId = changeId,
                    ApplyMode = change.ApplyMode,
                    FilePath = change.FilePath
                };
                
                switch (change.ApplyMode)
                {
                    case ApplyMode.Selection:
                        result = await ApplySelectionChangeAsync(change, options);
                        break;
                    case ApplyMode.File:
                        result = await ApplyFileChangeAsync(change, options);
                        break;
                    case ApplyMode.MultiFile:
                        result = await ApplyMultiFileChangeAsync(change, options);
                        break;
                    case ApplyMode.NewFile:
                        result = await ApplyNewFileChangeAsync(change, options);
                        break;
                }
                
                if (result.Success && stage != null)
                {
                    if (!string.IsNullOrEmpty(change.FilePath))
                    {
                        _stageStore.CaptureAfterState(stage, change.FilePath);
                    }
                    _stageStore.CompleteStage(stage);
                    result.StageNumber = stage.Number;
                }
                else if (!result.Success && stage != null)
                {
                    _stageStore.FailStage(stage, result.ErrorMessage ?? "Apply failed");
                }
                
                if (result.Success)
                {
                    change.Status = ChangeStatus.Applied;
                    _pendingChanges.Remove(change);
                }
                
                OnChangeApplied?.Invoke(result);
                return result;
            }
            catch (Exception ex)
            {
                var error = $"Apply failed: {ex.Message}";
                OnError?.Invoke(error);
                return new ApplyResult { Success = false, ErrorMessage = error };
            }
        }
        
        /// <summary>
        /// Preview a change without applying it.
        /// </summary>
        public DiffResult? PreviewChange(string changeId)
        {
            var change = _pendingChanges.FirstOrDefault(c => c.Id == changeId);
            if (change == null) return null;
            
            return change.ApplyMode == ApplyMode.Selection 
                ? change.SelectionDiff?.Diff 
                : change.Diff;
        }
        
        /// <summary>
        /// Reject/dismiss a pending change.
        /// </summary>
        public void RejectChange(string changeId)
        {
            var change = _pendingChanges.FirstOrDefault(c => c.Id == changeId);
            if (change != null)
            {
                change.Status = ChangeStatus.Rejected;
                _pendingChanges.Remove(change);
            }
        }
        
        /// <summary>
        /// Clear all pending changes.
        /// </summary>
        public void ClearPendingChanges()
        {
            _pendingChanges.Clear();
        }
        
        /// <summary>
        /// Get unified diff string for clipboard.
        /// </summary>
        public string GetDiffString(string changeId)
        {
            var change = _pendingChanges.FirstOrDefault(c => c.Id == changeId);
            if (change?.Diff == null && change?.SelectionDiff?.Diff == null) 
                return "";
            
            var diff = change.Diff ?? change.SelectionDiff?.Diff;
            return diff != null ? _diffEngine.GenerateUnifiedDiff(diff) : "";
        }
        
        #region Private Apply Methods
        
        private Task<ApplyResult> ApplySelectionChangeAsync(PendingChange change, ApplyOptions options)
        {
            if (change.SelectionDiff == null)
            {
                return Task.FromResult(new ApplyResult 
                { 
                    Success = false, 
                    ErrorMessage = "No selection diff available" 
                });
            }
            
            // The actual editor update is handled by the caller via callback
            return Task.FromResult(new ApplyResult
            {
                Success = true,
                ChangeId = change.Id,
                ApplyMode = ApplyMode.Selection,
                FilePath = change.FilePath,
                NewContent = change.SelectionDiff.FullModifiedContent,
                SelectionStart = change.SelectionStart,
                SelectionEnd = change.SelectionStart + change.ProposedCode.Length
            });
        }
        
        private async Task<ApplyResult> ApplyFileChangeAsync(PendingChange change, ApplyOptions options)
        {
            if (string.IsNullOrEmpty(change.FilePath))
            {
                return new ApplyResult { Success = false, ErrorMessage = "No file path specified" };
            }
            
            var newContent = change.Diff?.ModifiedContent ?? change.ProposedCode;
            
            if (options.WriteToFile && File.Exists(change.FilePath))
            {
                await File.WriteAllTextAsync(change.FilePath, newContent);
            }
            
            return new ApplyResult
            {
                Success = true,
                ChangeId = change.Id,
                ApplyMode = ApplyMode.File,
                FilePath = change.FilePath,
                NewContent = newContent
            };
        }
        
        private async Task<ApplyResult> ApplyMultiFileChangeAsync(PendingChange change, ApplyOptions options)
        {
            var results = new List<string>();
            
            foreach (var fileChange in change.MultiFileChanges)
            {
                if (options.WriteToFile)
                {
                    var dir = Path.GetDirectoryName(fileChange.FilePath);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }
                    
                    await File.WriteAllTextAsync(fileChange.FilePath, fileChange.NewContent);
                    results.Add(fileChange.FilePath);
                }
            }
            
            return new ApplyResult
            {
                Success = true,
                ChangeId = change.Id,
                ApplyMode = ApplyMode.MultiFile,
                AffectedFiles = results
            };
        }
        
        private async Task<ApplyResult> ApplyNewFileChangeAsync(PendingChange change, ApplyOptions options)
        {
            if (string.IsNullOrEmpty(change.FilePath))
            {
                return new ApplyResult { Success = false, ErrorMessage = "No file path specified" };
            }
            
            if (options.WriteToFile)
            {
                var dir = Path.GetDirectoryName(change.FilePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                
                await File.WriteAllTextAsync(change.FilePath, change.ProposedCode);
            }
            
            return new ApplyResult
            {
                Success = true,
                ChangeId = change.Id,
                ApplyMode = ApplyMode.NewFile,
                FilePath = change.FilePath,
                NewContent = change.ProposedCode
            };
        }
        
        #endregion
        
        #region Code Block Extraction
        
        private List<CodeBlock> ExtractCodeBlocks(string response)
        {
            var blocks = new List<CodeBlock>();
            
            // Match ```language\ncode\n``` patterns
            var regex = new Regex(@"```(\w+)?\s*\n([\s\S]*?)```", RegexOptions.Multiline);
            var matches = regex.Matches(response);
            
            foreach (Match match in matches)
            {
                var language = match.Groups[1].Value;
                var code = match.Groups[2].Value.TrimEnd();
                
                // Try to extract description from text before code block
                var beforeBlock = response.Substring(0, match.Index);
                var lastParagraph = beforeBlock.Split('\n').LastOrDefault(l => !string.IsNullOrWhiteSpace(l)) ?? "";
                
                blocks.Add(new CodeBlock
                {
                    Language = string.IsNullOrEmpty(language) ? "text" : language,
                    Code = code,
                    Description = lastParagraph.Length > 100 ? lastParagraph.Substring(0, 100) + "..." : lastParagraph,
                    SuggestedFileName = InferFileName(language, code)
                });
            }
            
            return blocks;
        }
        
        private string? InferFileName(string language, string code)
        {
            // Try to infer filename from code content
            var classMatch = Regex.Match(code, @"(?:class|interface|struct|enum)\s+(\w+)");
            if (classMatch.Success)
            {
                var ext = language switch
                {
                    "csharp" or "cs" => ".cs",
                    "typescript" or "ts" => ".ts",
                    "javascript" or "js" => ".js",
                    "python" or "py" => ".py",
                    "java" => ".java",
                    _ => ".txt"
                };
                return classMatch.Groups[1].Value + ext;
            }
            
            return null;
        }
        
        #endregion
    }
    
    #region Models
    
    public class CodeContext
    {
        public string? FilePath { get; set; }
        public string? FullContent { get; set; }
        public bool HasSelection { get; set; }
        public int SelectionStart { get; set; }
        public int SelectionEnd { get; set; }
        public string? SelectedText { get; set; }
        public string? Language { get; set; }
        public string? WorkspacePath { get; set; }
    }
    
    public class PendingChange
    {
        public string Id { get; set; } = "";
        public ApplyMode ApplyMode { get; set; }
        public ChangeStatus Status { get; set; } = ChangeStatus.Pending;
        public string? FilePath { get; set; }
        public string? Language { get; set; }
        public string? Description { get; set; }
        public string? Intent { get; set; }
        public string OriginalCode { get; set; } = "";
        public string ProposedCode { get; set; } = "";
        public int SelectionStart { get; set; }
        public int SelectionEnd { get; set; }
        public DiffResult? Diff { get; set; }
        public SelectionDiffResult? SelectionDiff { get; set; }
        public List<CodeFileChange> MultiFileChanges { get; set; } = new();
        public DateTime CreatedAt { get; set; }
    }
    
    public class CodeFileChange
    {
        public string FilePath { get; set; } = "";
        public string OriginalContent { get; set; } = "";
        public string NewContent { get; set; } = "";
        public string? Intent { get; set; }
        public DiffResult? Diff { get; set; }
    }
    
    public enum ApplyMode
    {
        Selection,   // Apply to selected code block
        File,        // Apply to current file
        MultiFile,   // Apply across multiple files (requires confirmation)
        NewFile      // Create new file
    }
    
    public enum ChangeStatus
    {
        Pending,
        Applied,
        Rejected
    }
    
    public class ApplyOptions
    {
        public bool CreateStage { get; set; } = true;
        public bool WriteToFile { get; set; } = true;
        public bool RequireConfirmation { get; set; } = false;
    }
    
    public class ApplyResult
    {
        public bool Success { get; set; }
        public string? ChangeId { get; set; }
        public ApplyMode ApplyMode { get; set; }
        public string? FilePath { get; set; }
        public string? NewContent { get; set; }
        public int SelectionStart { get; set; }
        public int SelectionEnd { get; set; }
        public string? ErrorMessage { get; set; }
        public int? StageNumber { get; set; }
        public List<string> AffectedFiles { get; set; } = new();
    }
    
    public class CodeBlock
    {
        public string Language { get; set; } = "";
        public string Code { get; set; } = "";
        public string? Description { get; set; }
        public string? SuggestedFileName { get; set; }
    }
    
    #endregion
}
