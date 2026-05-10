#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace AtlasAI.Coding.Services
{
    /// <summary>
    /// Diff engine for computing and applying code changes.
    /// Supports selection-based, file-based, and multi-file patches.
    /// </summary>
    public class DiffEngine
    {
        public static DiffEngine Instance { get; } = new();
        
        /// <summary>
        /// Compute a unified diff between two strings.
        /// </summary>
        public DiffResult ComputeDiff(string original, string modified, string? filePath = null)
        {
            var originalLines = original.Split('\n').Select(l => l.TrimEnd('\r')).ToArray();
            var modifiedLines = modified.Split('\n').Select(l => l.TrimEnd('\r')).ToArray();
            
            var hunks = ComputeHunks(originalLines, modifiedLines);
            
            return new DiffResult
            {
                FilePath = filePath ?? "untitled",
                OriginalContent = original,
                ModifiedContent = modified,
                Hunks = hunks,
                AddedLines = hunks.Sum(h => h.Additions.Count),
                RemovedLines = hunks.Sum(h => h.Deletions.Count),
                HasChanges = hunks.Count > 0
            };
        }
        
        /// <summary>
        /// Compute diff for a selection within a file.
        /// </summary>
        public SelectionDiffResult ComputeSelectionDiff(
            string fullContent, 
            int selectionStart, 
            int selectionEnd,
            string modifiedSelection,
            string? filePath = null)
        {
            var beforeSelection = fullContent.Substring(0, selectionStart);
            var originalSelection = fullContent.Substring(selectionStart, selectionEnd - selectionStart);
            var afterSelection = fullContent.Substring(selectionEnd);
            
            var newFullContent = beforeSelection + modifiedSelection + afterSelection;
            var diff = ComputeDiff(fullContent, newFullContent, filePath);
            
            return new SelectionDiffResult
            {
                FilePath = filePath ?? "untitled",
                SelectionStart = selectionStart,
                SelectionEnd = selectionEnd,
                OriginalSelection = originalSelection,
                ModifiedSelection = modifiedSelection,
                FullOriginalContent = fullContent,
                FullModifiedContent = newFullContent,
                Diff = diff,
                SelectionStartLine = CountLines(beforeSelection),
                SelectionEndLine = CountLines(beforeSelection + originalSelection)
            };
        }
        
        /// <summary>
        /// Apply a diff to content.
        /// </summary>
        public string ApplyDiff(string original, DiffResult diff)
        {
            return diff.ModifiedContent;
        }
        
        /// <summary>
        /// Apply a selection diff to content.
        /// </summary>
        public string ApplySelectionDiff(SelectionDiffResult selectionDiff)
        {
            return selectionDiff.FullModifiedContent;
        }
        
        /// <summary>
        /// Generate a unified diff string for display.
        /// </summary>
        public string GenerateUnifiedDiff(DiffResult diff)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"--- a/{diff.FilePath}");
            sb.AppendLine($"+++ b/{diff.FilePath}");
            
            foreach (var hunk in diff.Hunks)
            {
                sb.AppendLine($"@@ -{hunk.OriginalStart},{hunk.OriginalLength} +{hunk.ModifiedStart},{hunk.ModifiedLength} @@");
                
                foreach (var line in hunk.Lines)
                {
                    sb.AppendLine(line.ToString());
                }
            }
            
            return sb.ToString();
        }
        
        private List<DiffHunk> ComputeHunks(string[] original, string[] modified)
        {
            var hunks = new List<DiffHunk>();
            var lcs = ComputeLCS(original, modified);
            
            int origIdx = 0, modIdx = 0, lcsIdx = 0;
            DiffHunk? currentHunk = null;
            
            while (origIdx < original.Length || modIdx < modified.Length)
            {
                bool inLcs = lcsIdx < lcs.Count && 
                             origIdx < original.Length && 
                             modIdx < modified.Length &&
                             original[origIdx] == lcs[lcsIdx] && 
                             modified[modIdx] == lcs[lcsIdx];
                
                if (inLcs)
                {
                    // Context line
                    if (currentHunk != null)
                    {
                        currentHunk.Lines.Add(new DiffLine(DiffLineType.Context, original[origIdx], origIdx + 1, modIdx + 1));
                        
                        // Check if we should close the hunk (3 context lines after changes)
                        var lastChange = currentHunk.Lines.LastOrDefault(l => l.Type != DiffLineType.Context);
                        if (lastChange != null)
                        {
                            var contextAfter = currentHunk.Lines.Count - currentHunk.Lines.IndexOf(lastChange) - 1;
                            if (contextAfter >= 3)
                            {
                                FinalizeHunk(currentHunk);
                                hunks.Add(currentHunk);
                                currentHunk = null;
                            }
                        }
                    }
                    
                    origIdx++;
                    modIdx++;
                    lcsIdx++;
                }
                else
                {
                    // Start new hunk if needed
                    if (currentHunk == null)
                    {
                        currentHunk = new DiffHunk
                        {
                            OriginalStart = Math.Max(1, origIdx - 2),
                            ModifiedStart = Math.Max(1, modIdx - 2)
                        };
                        
                        // Add context before
                        for (int i = Math.Max(0, origIdx - 3); i < origIdx; i++)
                        {
                            if (i < original.Length)
                            {
                                currentHunk.Lines.Add(new DiffLine(DiffLineType.Context, original[i], i + 1, i + 1));
                            }
                        }
                    }
                    
                    // Deletion
                    if (origIdx < original.Length && (lcsIdx >= lcs.Count || original[origIdx] != lcs[lcsIdx]))
                    {
                        currentHunk.Lines.Add(new DiffLine(DiffLineType.Deletion, original[origIdx], origIdx + 1, null));
                        currentHunk.Deletions.Add(origIdx + 1);
                        origIdx++;
                    }
                    // Addition
                    else if (modIdx < modified.Length && (lcsIdx >= lcs.Count || modified[modIdx] != lcs[lcsIdx]))
                    {
                        currentHunk.Lines.Add(new DiffLine(DiffLineType.Addition, modified[modIdx], null, modIdx + 1));
                        currentHunk.Additions.Add(modIdx + 1);
                        modIdx++;
                    }
                }
            }
            
            if (currentHunk != null)
            {
                FinalizeHunk(currentHunk);
                hunks.Add(currentHunk);
            }
            
            return hunks;
        }
        
        private void FinalizeHunk(DiffHunk hunk)
        {
            hunk.OriginalLength = hunk.Lines.Count(l => l.Type != DiffLineType.Addition);
            hunk.ModifiedLength = hunk.Lines.Count(l => l.Type != DiffLineType.Deletion);
        }
        
        private List<string> ComputeLCS(string[] a, string[] b)
        {
            int m = a.Length, n = b.Length;
            var dp = new int[m + 1, n + 1];
            
            for (int i = 1; i <= m; i++)
            {
                for (int j = 1; j <= n; j++)
                {
                    if (a[i - 1] == b[j - 1])
                        dp[i, j] = dp[i - 1, j - 1] + 1;
                    else
                        dp[i, j] = Math.Max(dp[i - 1, j], dp[i, j - 1]);
                }
            }
            
            var lcs = new List<string>();
            int x = m, y = n;
            while (x > 0 && y > 0)
            {
                if (a[x - 1] == b[y - 1])
                {
                    lcs.Insert(0, a[x - 1]);
                    x--; y--;
                }
                else if (dp[x - 1, y] > dp[x, y - 1])
                    x--;
                else
                    y--;
            }
            
            return lcs;
        }
        
        private int CountLines(string text)
        {
            if (string.IsNullOrEmpty(text)) return 1;
            return text.Count(c => c == '\n') + 1;
        }
    }
    
    #region Models
    
    public class DiffResult
    {
        public string FilePath { get; set; } = "";
        public string OriginalContent { get; set; } = "";
        public string ModifiedContent { get; set; } = "";
        public List<DiffHunk> Hunks { get; set; } = new();
        public int AddedLines { get; set; }
        public int RemovedLines { get; set; }
        public bool HasChanges { get; set; }
    }
    
    public class SelectionDiffResult
    {
        public string FilePath { get; set; } = "";
        public int SelectionStart { get; set; }
        public int SelectionEnd { get; set; }
        public int SelectionStartLine { get; set; }
        public int SelectionEndLine { get; set; }
        public string OriginalSelection { get; set; } = "";
        public string ModifiedSelection { get; set; } = "";
        public string FullOriginalContent { get; set; } = "";
        public string FullModifiedContent { get; set; } = "";
        public DiffResult Diff { get; set; } = new();
    }
    
    public class DiffHunk
    {
        public int OriginalStart { get; set; }
        public int OriginalLength { get; set; }
        public int ModifiedStart { get; set; }
        public int ModifiedLength { get; set; }
        public List<DiffLine> Lines { get; set; } = new();
        public List<int> Additions { get; set; } = new();
        public List<int> Deletions { get; set; } = new();
    }
    
    public class DiffLine
    {
        public DiffLineType Type { get; set; }
        public string Content { get; set; }
        public int? OriginalLineNumber { get; set; }
        public int? ModifiedLineNumber { get; set; }
        
        public DiffLine(DiffLineType type, string content, int? origLine, int? modLine)
        {
            Type = type;
            Content = content;
            OriginalLineNumber = origLine;
            ModifiedLineNumber = modLine;
        }
        
        public override string ToString()
        {
            var prefix = Type switch
            {
                DiffLineType.Addition => "+",
                DiffLineType.Deletion => "-",
                _ => " "
            };
            return $"{prefix}{Content}";
        }
    }
    
    public enum DiffLineType
    {
        Context,
        Addition,
        Deletion
    }
    
    #endregion
}
