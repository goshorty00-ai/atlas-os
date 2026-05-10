using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AtlasAI.SmartAssistant.Models;

namespace AtlasAI.SmartAssistant.Services
{
    /// <summary>
    /// Analyzes project context and detects errors in code files
    /// </summary>
    public class ProjectContextAnalyzer
    {
        private ProjectContext? _currentProject;
        private readonly List<ProjectContext> _recentProjects = new();
        private FileSystemWatcher? _watcher;
        
        public event EventHandler<ProjectContext>? ProjectDetected;
        public event EventHandler<DetectedError>? ErrorDetected;
        public event EventHandler<List<DetectedError>>? ErrorsCleared;
        
        public ProjectContext? CurrentProject => _currentProject;
        
        /// <summary>
        /// Analyze a directory for project context
        /// </summary>
        public async Task<ProjectContext?> AnalyzeDirectoryAsync(string path)
        {
            if (!Directory.Exists(path)) return null;
            
            var context = new ProjectContext
            {
                ProjectPath = path,
                ProjectName = Path.GetFileName(path),
                LastScanned = DateTime.Now
            };
            
            // Detect project type
            context.Type = await DetectProjectTypeAsync(path);
            
            // Get recent files
            context.RecentFiles = GetRecentFiles(path, 20);
            
            // Scan for errors
            context.Errors = await ScanForErrorsAsync(path, context.Type);
            
            _currentProject = context;
            _recentProjects.Add(context);
            
            // Start watching for changes
            StartWatching(path);
            
            ProjectDetected?.Invoke(this, context);
            Debug.WriteLine($"[ProjectAnalyzer] Analyzed project: {context.ProjectName} ({context.Type}), {context.Errors.Count} errors");
            
            return context;
        }
        
        /// <summary>
        /// Scan a specific file for errors
        /// </summary>
        public async Task<List<DetectedError>> ScanFileAsync(string filePath)
        {
            if (!File.Exists(filePath)) return new();
            
            var extension = Path.GetExtension(filePath).ToLowerInvariant();
            var projectType = extension switch
            {
                ".cs" => ProjectType.CSharp,
                ".py" => ProjectType.Python,
                ".js" => ProjectType.JavaScript,
                ".ts" => ProjectType.TypeScript,
                _ => ProjectType.Unknown
            };
            
            return await ScanFileForErrorsAsync(filePath, projectType);
        }
        
        /// <summary>
        /// Get suggested fix for an error
        /// </summary>
        public string? GetSuggestedFix(DetectedError error)
        {
            // Common error patterns and fixes
            var fixes = new Dictionary<string, string>
            {
                { "missing semicolon", "Add a semicolon at the end of the line" },
                { "undefined variable", "Declare the variable before using it, or check for typos" },
                { "missing import", "Add the required import/using statement at the top of the file" },
                { "type mismatch", "Check that the types match, or add an explicit cast" },
                { "null reference", "Add a null check before accessing the object" },
                { "missing return", "Add a return statement to the method" },
                { "unreachable code", "Remove the unreachable code or fix the control flow" },
                { "unused variable", "Remove the unused variable or use it somewhere" },
                { "missing bracket", "Add the missing opening or closing bracket" },
                { "syntax error", "Check the syntax around the indicated line" }
            };
            
            foreach (var (pattern, fix) in fixes)
            {
                if (error.Message.ToLowerInvariant().Contains(pattern))
                {
                    return fix;
                }
            }
            
            return null;
        }
        
        /// <summary>
        /// Get project summary for AI context
        /// </summary>
        public string GetProjectSummary()
        {
            if (_currentProject == null) return "No project currently loaded.";
            
            var summary = $"Project: {_currentProject.ProjectName}\n";
            summary += $"Type: {_currentProject.Type}\n";
            summary += $"Path: {_currentProject.ProjectPath}\n";
            summary += $"Recent files: {string.Join(", ", _currentProject.RecentFiles.Take(5).Select(Path.GetFileName))}\n";
            
            if (_currentProject.Errors.Count > 0)
            {
                summary += $"\nErrors ({_currentProject.Errors.Count}):\n";
                foreach (var error in _currentProject.Errors.Take(5))
                {
                    summary += $"  - {Path.GetFileName(error.FilePath)}:{error.Line} - {error.Message}\n";
                }
            }
            
            return summary;
        }
        
        /// <summary>
        /// Stop watching the current project
        /// </summary>
        public void StopWatching()
        {
            _watcher?.Dispose();
            _watcher = null;
        }
        
        private async Task<ProjectType> DetectProjectTypeAsync(string path)
        {
            // Check for project files
            if (Directory.GetFiles(path, "*.csproj", SearchOption.AllDirectories).Any() ||
                Directory.GetFiles(path, "*.sln", SearchOption.AllDirectories).Any())
            {
                return ProjectType.CSharp;
            }
            
            if (File.Exists(Path.Combine(path, "requirements.txt")) ||
                File.Exists(Path.Combine(path, "setup.py")) ||
                Directory.GetFiles(path, "*.py", SearchOption.AllDirectories).Any())
            {
                return ProjectType.Python;
            }
            
            if (File.Exists(Path.Combine(path, "tsconfig.json")))
            {
                return ProjectType.TypeScript;
            }
            
            if (File.Exists(Path.Combine(path, "package.json")))
            {
                return ProjectType.JavaScript;
            }
            
            return ProjectType.Unknown;
        }
        
        private List<string> GetRecentFiles(string path, int count)
        {
            try
            {
                var extensions = new[] { ".cs", ".py", ".js", ".ts", ".jsx", ".tsx", ".json", ".xml", ".yaml", ".yml" };
                
                return Directory.GetFiles(path, "*.*", SearchOption.AllDirectories)
                    .Where(f => extensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                    .Where(f => !f.Contains("\\bin\\") && !f.Contains("\\obj\\") && !f.Contains("\\node_modules\\"))
                    .OrderByDescending(f => File.GetLastWriteTime(f))
                    .Take(count)
                    .ToList();
            }
            catch
            {
                return new();
            }
        }
        
        private async Task<List<DetectedError>> ScanForErrorsAsync(string path, ProjectType type)
        {
            var errors = new List<DetectedError>();
            
            var extensions = type switch
            {
                ProjectType.CSharp => new[] { ".cs" },
                ProjectType.Python => new[] { ".py" },
                ProjectType.JavaScript => new[] { ".js", ".jsx" },
                ProjectType.TypeScript => new[] { ".ts", ".tsx" },
                _ => Array.Empty<string>()
            };
            
            if (extensions.Length == 0) return errors;
            
            try
            {
                var files = Directory.GetFiles(path, "*.*", SearchOption.AllDirectories)
                    .Where(f => extensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                    .Where(f => !f.Contains("\\bin\\") && !f.Contains("\\obj\\") && !f.Contains("\\node_modules\\"))
                    .Take(100); // Limit for performance
                
                foreach (var file in files)
                {
                    var fileErrors = await ScanFileForErrorsAsync(file, type);
                    errors.AddRange(fileErrors);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ProjectAnalyzer] Error scanning: {ex.Message}");
            }
            
            return errors;
        }
        
        private async Task<List<DetectedError>> ScanFileForErrorsAsync(string filePath, ProjectType type)
        {
            var errors = new List<DetectedError>();
            
            try
            {
                var content = await File.ReadAllTextAsync(filePath);
                var lines = content.Split('\n');
                
                for (int i = 0; i < lines.Length; i++)
                {
                    var line = lines[i];
                    var lineNum = i + 1;
                    
                    // Common error patterns
                    var detectedErrors = type switch
                    {
                        ProjectType.CSharp => DetectCSharpErrors(line, lineNum, filePath),
                        ProjectType.Python => DetectPythonErrors(line, lineNum, filePath),
                        ProjectType.JavaScript or ProjectType.TypeScript => DetectJsErrors(line, lineNum, filePath),
                        _ => new List<DetectedError>()
                    };
                    
                    errors.AddRange(detectedErrors);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ProjectAnalyzer] Error scanning file {filePath}: {ex.Message}");
            }
            
            return errors;
        }
        
        private List<DetectedError> DetectCSharpErrors(string line, int lineNum, string filePath)
        {
            var errors = new List<DetectedError>();
            
            // TODO: marker
            if (line.Contains("TODO:", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add(new DetectedError
                {
                    FilePath = filePath,
                    Line = lineNum,
                    Message = "TODO marker found",
                    Severity = ErrorSeverity.Info
                });
            }
            
            // FIXME marker
            if (line.Contains("FIXME:", StringComparison.OrdinalIgnoreCase) || line.Contains("BUG:", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add(new DetectedError
                {
                    FilePath = filePath,
                    Line = lineNum,
                    Message = "FIXME/BUG marker found",
                    Severity = ErrorSeverity.Warning
                });
            }
            
            // Empty catch block
            if (Regex.IsMatch(line, @"catch\s*\([^)]*\)\s*\{\s*\}"))
            {
                errors.Add(new DetectedError
                {
                    FilePath = filePath,
                    Line = lineNum,
                    Message = "Empty catch block - exceptions are being silently swallowed",
                    Severity = ErrorSeverity.Warning,
                    SuggestedFix = "Add error handling or logging in the catch block"
                });
            }
            
            return errors;
        }
        
        private List<DetectedError> DetectPythonErrors(string line, int lineNum, string filePath)
        {
            var errors = new List<DetectedError>();
            
            // Bare except
            if (Regex.IsMatch(line, @"^\s*except\s*:"))
            {
                errors.Add(new DetectedError
                {
                    FilePath = filePath,
                    Line = lineNum,
                    Message = "Bare except clause - catches all exceptions including KeyboardInterrupt",
                    Severity = ErrorSeverity.Warning,
                    SuggestedFix = "Specify the exception type, e.g., 'except Exception:'"
                });
            }
            
            // TODO/FIXME
            if (line.Contains("TODO:", StringComparison.OrdinalIgnoreCase) || line.Contains("FIXME:", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add(new DetectedError
                {
                    FilePath = filePath,
                    Line = lineNum,
                    Message = "TODO/FIXME marker found",
                    Severity = ErrorSeverity.Info
                });
            }
            
            return errors;
        }
        
        private List<DetectedError> DetectJsErrors(string line, int lineNum, string filePath)
        {
            var errors = new List<DetectedError>();
            
            // console.log in production code
            if (line.Contains("console.log(") && !filePath.Contains("test"))
            {
                errors.Add(new DetectedError
                {
                    FilePath = filePath,
                    Line = lineNum,
                    Message = "console.log found - consider removing for production",
                    Severity = ErrorSeverity.Info,
                    SuggestedFix = "Remove console.log or use a proper logging library"
                });
            }
            
            // var instead of let/const
            if (Regex.IsMatch(line, @"\bvar\s+\w+\s*="))
            {
                errors.Add(new DetectedError
                {
                    FilePath = filePath,
                    Line = lineNum,
                    Message = "Using 'var' - consider using 'let' or 'const' instead",
                    Severity = ErrorSeverity.Info,
                    SuggestedFix = "Replace 'var' with 'let' or 'const'"
                });
            }
            
            return errors;
        }
        
        private void StartWatching(string path)
        {
            StopWatching();
            
            try
            {
                _watcher = new FileSystemWatcher(path)
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName,
                    IncludeSubdirectories = true,
                    EnableRaisingEvents = true
                };
                
                _watcher.Changed += async (s, e) =>
                {
                    if (_currentProject != null && IsCodeFile(e.FullPath))
                    {
                        var errors = await ScanFileAsync(e.FullPath);
                        foreach (var error in errors)
                        {
                            ErrorDetected?.Invoke(this, error);
                        }
                    }
                };
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ProjectAnalyzer] Error starting watcher: {ex.Message}");
            }
        }
        
        private bool IsCodeFile(string path)
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            return new[] { ".cs", ".py", ".js", ".ts", ".jsx", ".tsx" }.Contains(ext);
        }
    }
}
