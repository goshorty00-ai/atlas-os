using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AtlasAI.Coding.Services;

namespace AtlasAI.Coding
{
    /// <summary>
    /// Code Assistant Service - Provides IDE-like coding capabilities
    /// Similar to Kiro/Cursor AI coding features
    /// </summary>
    public class CodeAssistantService
    {
        private string? _workspacePath;
        private readonly List<string> _recentFiles = new();
        private readonly Dictionary<string, string> _fileCache = new();
        
        // Stage tracking for undo support
        private Stage? _currentStage;
        private readonly StageSnapshotStore _stageStore = StageSnapshotStore.Instance;
        
        // File extensions we consider as code
        private static readonly HashSet<string> CodeExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".cs", ".js", ".ts", ".jsx", ".tsx", ".py", ".java", ".cpp", ".c", ".h", ".hpp",
            ".html", ".htm", ".css", ".scss", ".less", ".json", ".xml", ".yaml", ".yml",
            ".sql", ".sh", ".bat", ".ps1", ".cmd", ".md", ".txt", ".config", ".csproj",
            ".sln", ".vue", ".svelte", ".php", ".rb", ".go", ".rs", ".swift", ".kt"
        };
        
        // Folders to ignore when scanning
        private static readonly HashSet<string> IgnoreFolders = new(StringComparer.OrdinalIgnoreCase)
        {
            "node_modules", "bin", "obj", ".git", ".vs", ".vscode", ".idea",
            "packages", "dist", "build", "__pycache__", ".next", "coverage"
        };

        public string? WorkspacePath => _workspacePath;
        public bool HasWorkspace => !string.IsNullOrEmpty(_workspacePath) && Directory.Exists(_workspacePath);

        /// <summary>
        /// Set the current workspace/project folder
        /// </summary>
        public void SetWorkspace(string path)
        {
            if (Directory.Exists(path))
            {
                _workspacePath = path;
                _fileCache.Clear();
                _stageStore.SetWorkspace(path);
                Debug.WriteLine($"[CodeAssistant] Workspace set to: {path}");
            }
        }
        
        /// <summary>
        /// Begin a new stage for tracking file modifications
        /// </summary>
        public Stage BeginStage(string description)
        {
            _currentStage = _stageStore.BeginStage(description);
            return _currentStage;
        }
        
        /// <summary>
        /// Complete the current stage
        /// </summary>
        public void CompleteStage(VerificationResult? verificationResult = null)
        {
            if (_currentStage != null)
            {
                _stageStore.CompleteStage(_currentStage, verificationResult);
                _currentStage = null;
            }
        }
        
        /// <summary>
        /// Fail the current stage
        /// </summary>
        public void FailStage(string error)
        {
            if (_currentStage != null)
            {
                _stageStore.FailStage(_currentStage, error);
                _currentStage = null;
            }
        }

        /// <summary>
        /// Get project structure as a tree
        /// </summary>
        public string GetProjectStructure(int maxDepth = 3)
        {
            if (!HasWorkspace) return "No workspace set. Drop a folder to set workspace.";
            
            var sb = new StringBuilder();
            sb.AppendLine($"📁 {Path.GetFileName(_workspacePath)}");
            BuildTree(sb, _workspacePath!, "", maxDepth, 0);
            return sb.ToString();
        }

        private void BuildTree(StringBuilder sb, string path, string indent, int maxDepth, int currentDepth)
        {
            if (currentDepth >= maxDepth) return;
            
            try
            {
                var dirs = Directory.GetDirectories(path)
                    .Where(d => !IgnoreFolders.Contains(Path.GetFileName(d)))
                    .OrderBy(d => Path.GetFileName(d))
                    .ToList();
                    
                var files = Directory.GetFiles(path)
                    .Where(f => CodeExtensions.Contains(Path.GetExtension(f)))
                    .OrderBy(f => Path.GetFileName(f))
                    .ToList();

                foreach (var dir in dirs)
                {
                    var name = Path.GetFileName(dir);
                    sb.AppendLine($"{indent}├── 📁 {name}/");
                    BuildTree(sb, dir, indent + "│   ", maxDepth, currentDepth + 1);
                }

                foreach (var file in files)
                {
                    var name = Path.GetFileName(file);
                    var icon = GetFileIcon(Path.GetExtension(file));
                    sb.AppendLine($"{indent}├── {icon} {name}");
                }
            }
            catch { }
        }

        private string GetFileIcon(string ext)
        {
            return ext.ToLower() switch
            {
                ".cs" => "🟣",      // C#
                ".js" or ".jsx" => "🟡",  // JavaScript
                ".ts" or ".tsx" => "🔵",  // TypeScript
                ".py" => "🐍",      // Python
                ".json" => "📋",    // JSON
                ".html" or ".htm" => "🌐", // HTML
                ".css" or ".scss" => "🎨", // CSS
                ".md" => "📝",      // Markdown
                _ => "📄"
            };
        }

        /// <summary>
        /// Read a file's contents
        /// </summary>
        public async Task<string> ReadFileAsync(string relativePath, System.Threading.CancellationToken ct = default)
        {
            var fullPath = GetFullPath(relativePath);
            if (fullPath == null || !File.Exists(fullPath))
                return $"❌ File not found: {relativePath}";

            try
            {
                var content = await File.ReadAllTextAsync(fullPath, ct);
                _recentFiles.Remove(fullPath);
                _recentFiles.Insert(0, fullPath);
                if (_recentFiles.Count > 20) _recentFiles.RemoveAt(20);
                
                return $"📄 **{Path.GetFileName(fullPath)}**\n```{GetLanguage(fullPath)}\n{content}\n```";
            }
            catch (OperationCanceledException)
            {
                return "❌ Operation cancelled.";
            }
            catch (Exception ex)
            {
                return $"❌ Error reading file: {ex.Message}";
            }
        }

        /// <summary>
        /// Write/create a file
        /// </summary>
        public async Task<string> WriteFileAsync(string relativePath, string content, System.Threading.CancellationToken ct = default)
        {
            var fullPath = GetFullPath(relativePath);
            if (fullPath == null)
                return "❌ Invalid path or no workspace set";

            try
            {
                var dir = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                var existed = File.Exists(fullPath);
                
                // Capture before state for stage tracking
                if (_currentStage != null)
                {
                    _stageStore.CaptureBeforeState(_currentStage, fullPath);
                }
                
                await File.WriteAllTextAsync(fullPath, content, ct);
                
                // Capture after state for stage tracking
                if (_currentStage != null)
                {
                    _stageStore.CaptureAfterState(_currentStage, fullPath);
                }
                
                return existed 
                    ? $"✅ Updated: {relativePath}" 
                    : $"✅ Created: {relativePath}";
            }
            catch (OperationCanceledException)
            {
                return "❌ Operation cancelled.";
            }
            catch (Exception ex)
            {
                return $"❌ Error writing file: {ex.Message}";
            }
        }

        /// <summary>
        /// Search for text/pattern in files
        /// </summary>
        public async Task<string> SearchAsync(string pattern, string? filePattern = null, System.Threading.CancellationToken ct = default)
        {
            if (!HasWorkspace) return "No workspace set.";

            var results = new List<(string file, int line, string text)>();
            var regex = new Regex(pattern, RegexOptions.IgnoreCase);
            var searchPattern = filePattern ?? "*.*";

            try
            {
                await Task.Run(() =>
                {
                    foreach (var file in GetAllCodeFiles())
                    {
                        ct.ThrowIfCancellationRequested();

                        if (!string.IsNullOrEmpty(filePattern) && !file.EndsWith(filePattern.TrimStart('*')))
                            continue;

                        try
                        {
                            var lines = File.ReadAllLines(file);
                            for (int i = 0; i < lines.Length; i++)
                            {
                                if (regex.IsMatch(lines[i]))
                                {
                                    results.Add((GetRelativePath(file), i + 1, lines[i].Trim()));
                                    if (results.Count >= 50) return;
                                }
                            }
                        }
                        catch { }
                    }
                }, ct);
            }
            catch (OperationCanceledException)
            {
                return "❌ Search cancelled.";
            }

            if (results.Count == 0)
                return $"🔍 No matches found for: {pattern}";

            var sb = new StringBuilder();
            sb.AppendLine($"🔍 Found {results.Count} matches for: `{pattern}`\n");
            
            foreach (var (file, line, text) in results.Take(30))
            {
                var truncated = text.Length > 80 ? text.Substring(0, 80) + "..." : text;
                sb.AppendLine($"**{file}:{line}** - `{truncated}`");
            }
            
            if (results.Count > 30)
                sb.AppendLine($"\n... and {results.Count - 30} more matches");

            return sb.ToString();
        }

        /// <summary>
        /// Find files by name pattern
        /// </summary>
        public string FindFiles(string pattern)
        {
            if (!HasWorkspace) return "No workspace set.";

            var matches = GetAllCodeFiles()
                .Where(f => Path.GetFileName(f).Contains(pattern, StringComparison.OrdinalIgnoreCase))
                .Select(f => GetRelativePath(f))
                .Take(20)
                .ToList();

            if (matches.Count == 0)
                return $"📂 No files found matching: {pattern}";

            var sb = new StringBuilder();
            sb.AppendLine($"📂 Files matching `{pattern}`:\n");
            foreach (var file in matches)
            {
                sb.AppendLine($"  • {file}");
            }
            return sb.ToString();
        }

        /// <summary>
        /// Replace text in a file (like strReplace)
        /// </summary>
        public async Task<string> ReplaceInFileAsync(string relativePath, string oldText, string newText, System.Threading.CancellationToken ct = default)
        {
            var fullPath = GetFullPath(relativePath);
            if (fullPath == null || !File.Exists(fullPath))
                return $"❌ File not found: {relativePath}";

            try
            {
                var content = await File.ReadAllTextAsync(fullPath, ct);
                
                if (!content.Contains(oldText))
                    return $"❌ Text not found in {relativePath}. Make sure the text matches exactly (including whitespace).";

                var occurrences = Regex.Matches(content, Regex.Escape(oldText)).Count;
                if (occurrences > 1)
                    return $"⚠️ Found {occurrences} occurrences. Please provide more context to make the match unique.";

                // Capture before state for stage tracking
                if (_currentStage != null)
                {
                    _stageStore.CaptureBeforeState(_currentStage, fullPath);
                }

                var newContent = content.Replace(oldText, newText);
                await File.WriteAllTextAsync(fullPath, newContent, ct);
                
                // Capture after state for stage tracking
                if (_currentStage != null)
                {
                    _stageStore.CaptureAfterState(_currentStage, fullPath);
                }
                
                return $"✅ Replaced in {relativePath}";
            }
            catch (OperationCanceledException)
            {
                return "❌ Operation cancelled.";
            }
            catch (Exception ex)
            {
                return $"❌ Error: {ex.Message}";
            }
        }

        /// <summary>
        /// Run a shell command
        /// </summary>
        public async Task<string> RunCommandAsync(string command, int timeoutSeconds = 30, System.Threading.CancellationToken ct = default)
        {
            // SAFETY GATE: Check with SafetyKernel before executing commands
            var safetyCheck = await AtlasAI.Core.SafetyKernel.Instance.CheckAndBlockAsync(
                AtlasAI.Core.OperationType.CommandExecution,
                AtlasAI.Core.OperationRisk.High,
                $"Execute command: {command}",
                new Dictionary<string, object>
                {
                    ["command"] = command,
                    ["timeout"] = timeoutSeconds
                });

            if (safetyCheck.Decision == AtlasAI.Core.SafetyDecision.Blocked)
            {
                return safetyCheck.Message + "\n\n💡 Command execution is disabled in Safety Mode.";
            }

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c {command}",
                    WorkingDirectory = _workspacePath ?? Environment.CurrentDirectory,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = new Process { StartInfo = psi };
                var output = new StringBuilder();
                var error = new StringBuilder();

                process.OutputDataReceived += (s, e) => { if (e.Data != null) output.AppendLine(e.Data); };
                process.ErrorDataReceived += (s, e) => { if (e.Data != null) error.AppendLine(e.Data); };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                var completed = await Task.Run(() => process.WaitForExit(timeoutSeconds * 1000), ct);
                
                if (!completed)
                {
                    process.Kill();
                    return $"⏱️ Command timed out after {timeoutSeconds}s";
                }

                var result = new StringBuilder();
                result.AppendLine($"💻 `{command}`");
                result.AppendLine($"Exit code: {process.ExitCode}\n");
                
                if (output.Length > 0)
                {
                    var outputText = output.ToString();
                    if (outputText.Length > 2000)
                        outputText = outputText.Substring(0, 2000) + "\n... [truncated]";
                    result.AppendLine("```");
                    result.AppendLine(outputText);
                    result.AppendLine("```");
                }
                
                if (error.Length > 0)
                {
                    result.AppendLine("**Errors:**");
                    result.AppendLine("```");
                    result.AppendLine(error.ToString());
                    result.AppendLine("```");
                }

                return result.ToString();
            }
            catch (OperationCanceledException)
            {
                return "❌ Command execution cancelled.";
            }
            catch (Exception ex)
            {
                return $"❌ Command failed: {ex.Message}";
            }
        }

        /// <summary>
        /// Delete a file
        /// </summary>
        public async Task<string> DeleteFileAsync(string relativePath, System.Threading.CancellationToken ct = default)
        {
            var fullPath = GetFullPath(relativePath);
            if (fullPath == null || !File.Exists(fullPath))
                return $"❌ File not found: {relativePath}";

            // SAFETY GATE: Check before file delete
            var safetyCheck = await AtlasAI.Core.SafetyKernel.Instance.CheckAndBlockAsync(
                AtlasAI.Core.OperationType.FileDelete,
                AtlasAI.Core.OperationRisk.Medium,
                $"Delete file: {relativePath}",
                new Dictionary<string, object>
                {
                    ["path"] = fullPath
                });
            
            if (safetyCheck.Decision == AtlasAI.Core.SafetyDecision.Blocked)
            {
                return safetyCheck.Message + "\n\n💡 File deletion is disabled in Safety Mode.";
            }

            try
            {
                ct.ThrowIfCancellationRequested();
                File.Delete(fullPath);
                return $"🗑️ Deleted: {relativePath}";
            }
            catch (OperationCanceledException)
            {
                return "❌ Deletion cancelled.";
            }
            catch (Exception ex)
            {
                return $"❌ Error deleting: {ex.Message}";
            }
        }
        
        /// <summary>
        /// Delete a file (sync wrapper for backward compatibility)
        /// </summary>
        public string DeleteFile(string relativePath)
        {
            return DeleteFileAsync(relativePath).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Get file info
        /// </summary>
        public string GetFileInfo(string relativePath)
        {
            var fullPath = GetFullPath(relativePath);
            if (fullPath == null || !File.Exists(fullPath))
                return $"❌ File not found: {relativePath}";

            var info = new FileInfo(fullPath);
            var lines = File.ReadAllLines(fullPath).Length;
            
            return $"📄 **{info.Name}**\n" +
                   $"  • Size: {FormatSize(info.Length)}\n" +
                   $"  • Lines: {lines}\n" +
                   $"  • Modified: {info.LastWriteTime:g}\n" +
                   $"  • Path: {relativePath}";
        }

        // Helper methods
        private string? GetFullPath(string relativePath)
        {
            if (string.IsNullOrEmpty(relativePath)) return null;
            
            // If it's already absolute, use it
            if (Path.IsPathRooted(relativePath))
                return relativePath;
                
            if (!HasWorkspace) return null;
            return Path.Combine(_workspacePath!, relativePath);
        }

        private string GetRelativePath(string fullPath)
        {
            if (!HasWorkspace) return fullPath;
            return Path.GetRelativePath(_workspacePath!, fullPath);
        }

        private IEnumerable<string> GetAllCodeFiles()
        {
            if (!HasWorkspace) yield break;
            
            foreach (var file in Directory.EnumerateFiles(_workspacePath!, "*.*", SearchOption.AllDirectories))
            {
                var dir = Path.GetDirectoryName(file) ?? "";
                if (IgnoreFolders.Any(f => dir.Contains(Path.DirectorySeparatorChar + f + Path.DirectorySeparatorChar) ||
                                          dir.EndsWith(Path.DirectorySeparatorChar + f)))
                    continue;
                    
                if (CodeExtensions.Contains(Path.GetExtension(file)))
                    yield return file;
            }
        }

        private string GetLanguage(string path)
        {
            return Path.GetExtension(path).ToLower() switch
            {
                ".cs" => "csharp",
                ".js" or ".jsx" => "javascript",
                ".ts" or ".tsx" => "typescript",
                ".py" => "python",
                ".json" => "json",
                ".xml" or ".csproj" => "xml",
                ".html" or ".htm" => "html",
                ".css" => "css",
                ".yaml" or ".yml" => "yaml",
                ".md" => "markdown",
                ".sql" => "sql",
                ".sh" => "bash",
                ".ps1" => "powershell",
                _ => ""
            };
        }

        private string FormatSize(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            return $"{bytes / (1024.0 * 1024):F1} MB";
        }
    }
}
