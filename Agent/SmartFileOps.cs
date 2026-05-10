using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace AtlasAI.Agent
{
    /// <summary>
    /// Smart file operations - safer file handling with preview and undo.
    /// </summary>
    public static class SmartFileOps
    {
        /// <summary>
        /// Create a file with safety checks
        /// </summary>
        public static async Task<FileOpResult> CreateFileAsync(string path, string content, bool overwrite = false)
        {
            try
            {
                var fullPath = GetFullPath(path);
                var dir = Path.GetDirectoryName(fullPath);
                
                // Safety check
                if (File.Exists(fullPath) && !overwrite)
                {
                    return new FileOpResult
                    {
                        Success = false,
                        Message = $"File already exists: {Path.GetFileName(fullPath)}",
                        RequiresConfirmation = true,
                        ConfirmationMessage = $"Overwrite {Path.GetFileName(fullPath)}?"
                    };
                }
                
                // Backup if overwriting
                if (File.Exists(fullPath))
                {
                    await AgentSafetyManager.Instance.BackupFileAsync(fullPath);
                }
                
                // Create directory if needed
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                
                // Write file
                await File.WriteAllTextAsync(fullPath, content);
                
                // Track for undo
                AgentSafetyManager.Instance.TrackFileCreation(fullPath);
                
                return new FileOpResult
                {
                    Success = true,
                    Message = $"✓ Created {Path.GetFileName(fullPath)}",
                    Path = fullPath
                };
            }
            catch (Exception ex)
            {
                return new FileOpResult
                {
                    Success = false,
                    Message = $"❌ Error: {ex.Message}"
                };
            }
        }
        
        /// <summary>
        /// Delete a file with safety checks
        /// </summary>
        public static async Task<FileOpResult> DeleteFileAsync(string path, bool confirmed = false)
        {
            try
            {
                var fullPath = GetFullPath(path);
                
                if (!File.Exists(fullPath))
                {
                    return new FileOpResult
                    {
                        Success = false,
                        Message = $"File not found: {path}"
                    };
                }
                
                var fileInfo = new FileInfo(fullPath);
                
                // Safety check for important files
                if (!confirmed && IsImportantFile(fullPath))
                {
                    return new FileOpResult
                    {
                        Success = false,
                        RequiresConfirmation = true,
                        ConfirmationMessage = $"⚠️ Delete {Path.GetFileName(fullPath)}? This looks important."
                    };
                }
                
                // Backup before delete
                await AgentSafetyManager.Instance.BackupFileAsync(fullPath);
                
                // Delete
                File.Delete(fullPath);
                
                return new FileOpResult
                {
                    Success = true,
                    Message = $"✓ Deleted {Path.GetFileName(fullPath)}",
                    Path = fullPath
                };
            }
            catch (Exception ex)
            {
                return new FileOpResult
                {
                    Success = false,
                    Message = $"❌ Error: {ex.Message}"
                };
            }
        }
        
        /// <summary>
        /// Move/rename a file
        /// </summary>
        public static async Task<FileOpResult> MoveFileAsync(string source, string destination)
        {
            try
            {
                var srcPath = GetFullPath(source);
                var dstPath = GetFullPath(destination);
                
                if (!File.Exists(srcPath))
                {
                    return new FileOpResult
                    {
                        Success = false,
                        Message = $"Source not found: {source}"
                    };
                }
                
                if (File.Exists(dstPath))
                {
                    return new FileOpResult
                    {
                        Success = false,
                        RequiresConfirmation = true,
                        ConfirmationMessage = $"Destination exists. Overwrite {Path.GetFileName(dstPath)}?"
                    };
                }
                
                // Create destination directory if needed
                var dstDir = Path.GetDirectoryName(dstPath);
                if (!string.IsNullOrEmpty(dstDir) && !Directory.Exists(dstDir))
                {
                    Directory.CreateDirectory(dstDir);
                }
                
                // Track for undo
                AgentSafetyManager.Instance.TrackFileMove(srcPath, dstPath);
                
                File.Move(srcPath, dstPath);
                
                return new FileOpResult
                {
                    Success = true,
                    Message = $"✓ Moved to {Path.GetFileName(dstPath)}",
                    Path = dstPath
                };
            }
            catch (Exception ex)
            {
                return new FileOpResult
                {
                    Success = false,
                    Message = $"❌ Error: {ex.Message}"
                };
            }
        }
        
        /// <summary>
        /// Copy a file
        /// </summary>
        public static async Task<FileOpResult> CopyFileAsync(string source, string destination)
        {
            try
            {
                var srcPath = GetFullPath(source);
                var dstPath = GetFullPath(destination);
                
                if (!File.Exists(srcPath))
                {
                    return new FileOpResult
                    {
                        Success = false,
                        Message = $"Source not found: {source}"
                    };
                }
                
                // Create destination directory if needed
                var dstDir = Path.GetDirectoryName(dstPath);
                if (!string.IsNullOrEmpty(dstDir) && !Directory.Exists(dstDir))
                {
                    Directory.CreateDirectory(dstDir);
                }
                
                File.Copy(srcPath, dstPath, overwrite: false);
                
                // Track for undo
                AgentSafetyManager.Instance.TrackFileCreation(dstPath);
                
                return new FileOpResult
                {
                    Success = true,
                    Message = $"✓ Copied to {Path.GetFileName(dstPath)}",
                    Path = dstPath
                };
            }
            catch (Exception ex)
            {
                return new FileOpResult
                {
                    Success = false,
                    Message = $"❌ Error: {ex.Message}"
                };
            }
        }
        
        /// <summary>
        /// Read file content with preview
        /// </summary>
        public static async Task<FileOpResult> ReadFileAsync(string path, int maxLines = 100)
        {
            try
            {
                var fullPath = GetFullPath(path);
                
                if (!File.Exists(fullPath))
                {
                    return new FileOpResult
                    {
                        Success = false,
                        Message = $"File not found: {path}"
                    };
                }
                
                var lines = await File.ReadAllLinesAsync(fullPath);
                var content = lines.Length > maxLines
                    ? string.Join("\n", lines.Take(maxLines)) + $"\n... ({lines.Length - maxLines} more lines)"
                    : string.Join("\n", lines);
                
                return new FileOpResult
                {
                    Success = true,
                    Message = $"📄 {Path.GetFileName(fullPath)} ({lines.Length} lines)",
                    Content = content,
                    Path = fullPath
                };
            }
            catch (Exception ex)
            {
                return new FileOpResult
                {
                    Success = false,
                    Message = $"❌ Error: {ex.Message}"
                };
            }
        }
        
        /// <summary>
        /// List directory contents
        /// </summary>
        public static async Task<FileOpResult> ListDirectoryAsync(string path)
        {
            try
            {
                var fullPath = GetFullPath(path);
                
                if (!Directory.Exists(fullPath))
                {
                    return new FileOpResult
                    {
                        Success = false,
                        Message = $"Directory not found: {path}"
                    };
                }
                
                var dirs = Directory.GetDirectories(fullPath).Select(d => $"📁 {Path.GetFileName(d)}");
                var files = Directory.GetFiles(fullPath).Select(f => $"📄 {Path.GetFileName(f)}");
                
                var content = string.Join("\n", dirs.Concat(files));
                
                return new FileOpResult
                {
                    Success = true,
                    Message = $"📂 {Path.GetFileName(fullPath)}",
                    Content = content,
                    Path = fullPath
                };
            }
            catch (Exception ex)
            {
                return new FileOpResult
                {
                    Success = false,
                    Message = $"❌ Error: {ex.Message}"
                };
            }
        }
        
        /// <summary>
        /// Search for files
        /// </summary>
        public static async Task<FileOpResult> SearchFilesAsync(string directory, string pattern)
        {
            try
            {
                var fullPath = GetFullPath(directory);
                
                if (!Directory.Exists(fullPath))
                {
                    return new FileOpResult
                    {
                        Success = false,
                        Message = $"Directory not found: {directory}"
                    };
                }
                
                var files = Directory.GetFiles(fullPath, pattern, SearchOption.AllDirectories)
                    .Take(50)
                    .Select(f => f.Replace(fullPath, "").TrimStart('\\', '/'))
                    .ToList();
                
                return new FileOpResult
                {
                    Success = true,
                    Message = $"Found {files.Count} files matching '{pattern}'",
                    Content = string.Join("\n", files),
                    Path = fullPath
                };
            }
            catch (Exception ex)
            {
                return new FileOpResult
                {
                    Success = false,
                    Message = $"❌ Error: {ex.Message}"
                };
            }
        }
        
        /// <summary>
        /// Open file in default application
        /// </summary>
        public static async Task<FileOpResult> OpenFileAsync(string path)
        {
            try
            {
                var fullPath = GetFullPath(path);
                
                if (!File.Exists(fullPath) && !Directory.Exists(fullPath))
                {
                    return new FileOpResult
                    {
                        Success = false,
                        Message = $"Not found: {path}"
                    };
                }
                
                Process.Start(new ProcessStartInfo(fullPath) { UseShellExecute = true });
                
                return new FileOpResult
                {
                    Success = true,
                    Message = $"✓ Opened {Path.GetFileName(fullPath)}",
                    Path = fullPath
                };
            }
            catch (Exception ex)
            {
                return new FileOpResult
                {
                    Success = false,
                    Message = $"❌ Error: {ex.Message}"
                };
            }
        }
        
        private static string GetFullPath(string path)
        {
            if (Path.IsPathRooted(path))
                return path;
            
            // Expand ~ to user profile
            if (path.StartsWith("~"))
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), path.Substring(1).TrimStart('/', '\\'));
            
            // Expand common shortcuts
            if (path.StartsWith("desktop", StringComparison.OrdinalIgnoreCase))
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), path.Substring(7).TrimStart('/', '\\'));
            if (path.StartsWith("documents", StringComparison.OrdinalIgnoreCase))
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), path.Substring(9).TrimStart('/', '\\'));
            if (path.StartsWith("downloads", StringComparison.OrdinalIgnoreCase))
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", path.Substring(9).TrimStart('/', '\\'));
            
            return Path.GetFullPath(path);
        }
        
        private static bool IsImportantFile(string path)
        {
            var name = Path.GetFileName(path).ToLowerInvariant();
            var ext = Path.GetExtension(path).ToLowerInvariant();
            
            // System files
            if (path.Contains("Windows") || path.Contains("System32") || path.Contains("Program Files"))
                return true;
            
            // Config files
            var importantNames = new[] { ".env", "config", "settings", "appsettings", "web.config", "app.config", ".gitignore", "package.json", "csproj" };
            if (importantNames.Any(n => name.Contains(n)))
                return true;
            
            // Important extensions
            var importantExts = new[] { ".exe", ".dll", ".sys", ".reg", ".bat", ".ps1", ".sh" };
            if (importantExts.Contains(ext))
                return true;
            
            return false;
        }
    }
    
    public class FileOpResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public string? Content { get; set; }
        public string? Path { get; set; }
        public bool RequiresConfirmation { get; set; }
        public string? ConfirmationMessage { get; set; }
    }
}
