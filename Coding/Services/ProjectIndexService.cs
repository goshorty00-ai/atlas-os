using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AtlasAI.Coding.Services.Models;

namespace AtlasAI.Coding.Services
{
    /// <summary>
    /// Project indexing service for fast file and symbol search.
    /// Implements semantic retrieval for Agent Mode v2.
    /// </summary>
    public class ProjectIndexService : IProjectIndex
    {
        private const int IndexVersion = 1;
        private const int ChunkMinSize = 300;
        private const int ChunkMaxSize = 800;
        
        private static readonly string IndexDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AtlasAI", "index");
        
        private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".cs", ".xaml", ".csproj", ".sln", ".json", ".xml", ".config",
            ".ts", ".tsx", ".js", ".jsx", ".html", ".css", ".scss",
            ".py", ".java", ".cpp", ".c", ".h", ".hpp",
            ".md", ".txt", ".yaml", ".yml"
        };
        
        private static readonly HashSet<string> ExcludedDirs = new(StringComparer.OrdinalIgnoreCase)
        {
            "bin", "obj", "node_modules", ".git", ".vs", ".vscode", "packages",
            "dist", "build", "out", ".idea", "__pycache__", "publish"
        };
        
        private ProjectIndex _index = new();
        private string _projectPath = "";
        private readonly object _lock = new();
        
        public static ProjectIndexService Instance { get; } = new();
        
        private ProjectIndexService() { }

        #region IProjectIndex Implementation
        
        public async Task BuildIndexAsync(string projectPath)
        {
            var sw = Stopwatch.StartNew();
            _projectPath = projectPath;
            
            lock (_lock)
            {
                _index = new ProjectIndex
                {
                    Version = IndexVersion,
                    WorkspaceHash = ComputeHash(projectPath),
                    ProjectPath = projectPath,
                    LastUpdated = DateTime.UtcNow
                };
            }
            
            try
            {
                var files = GetAllFiles(projectPath);
                
                foreach (var file in files)
                {
                    await IndexFileAsync(file);
                }
                
                sw.Stop();
                AgentModeLogger.Instance.LogIndexUpdate("build", _index.Files.Count, sw.ElapsedMilliseconds);
                Debug.WriteLine($"[AGENT2] Index built: {_index.Files.Count} files, {_index.Symbols.Count} symbols, {_index.Chunks.Count} chunks in {sw.ElapsedMilliseconds}ms");
            }
            catch (Exception ex)
            {
                AgentModeLogger.Instance.LogError("BuildIndexAsync", ex);
                throw;
            }
        }
        
        public async Task UpdateFileAsync(string filePath)
        {
            var sw = Stopwatch.StartNew();
            
            try
            {
                // Remove old entries for this file
                await RemoveFileAsync(filePath);
                
                // Re-index if file exists
                if (File.Exists(filePath))
                {
                    await IndexFileAsync(filePath);
                }
                
                _index.LastUpdated = DateTime.UtcNow;
                sw.Stop();
                AgentModeLogger.Instance.LogIndexUpdate("update", 1, sw.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                AgentModeLogger.Instance.LogError("UpdateFileAsync", ex);
            }
        }
        
        public Task RemoveFileAsync(string filePath)
        {
            lock (_lock)
            {
                _index.Files.RemoveAll(f => f.Path.Equals(filePath, StringComparison.OrdinalIgnoreCase));
                _index.Symbols.RemoveAll(s => s.FilePath.Equals(filePath, StringComparison.OrdinalIgnoreCase));
                _index.Chunks.RemoveAll(c => c.FilePath.Equals(filePath, StringComparison.OrdinalIgnoreCase));
            }
            return Task.CompletedTask;
        }
        
        public Task<List<SearchResult>> SearchAsync(string query, int topK = 10, SearchFilters? filters = null)
        {
            var results = new List<SearchResult>();
            var queryLower = query.ToLowerInvariant();
            var queryTerms = queryLower.Split(new[] { ' ', '_', '-', '.' }, StringSplitOptions.RemoveEmptyEntries);
            
            // Search symbols
            foreach (var symbol in _index.Symbols)
            {
                if (filters?.SymbolKind != null && symbol.Kind != filters.SymbolKind)
                    continue;
                    
                var score = CalculateSymbolScore(symbol, queryTerms, queryLower);
                if (score > 0.1)
                {
                    results.Add(new SearchResult
                    {
                        FilePath = symbol.FilePath,
                        RelativePath = GetRelativePath(symbol.FilePath),
                        Snippet = symbol.Signature,
                        Score = score,
                        Reason = "symbol match",
                        Line = symbol.Line
                    });
                }
            }
            
            // Search chunks
            foreach (var chunk in _index.Chunks)
            {
                if (!PassesFilters(chunk.FilePath, filters))
                    continue;
                    
                var score = CalculateChunkScore(chunk, queryTerms, queryLower);
                if (score > 0.1)
                {
                    results.Add(new SearchResult
                    {
                        FilePath = chunk.FilePath,
                        RelativePath = GetRelativePath(chunk.FilePath),
                        Snippet = TruncateSnippet(chunk.Content, 500),
                        Score = score,
                        Reason = "keyword match",
                        Line = chunk.StartLine
                    });
                }
            }
            
            // Search file names
            foreach (var file in _index.Files)
            {
                if (!PassesFilters(file.Path, filters))
                    continue;
                    
                var score = CalculateFileNameScore(file, queryTerms, queryLower);
                if (score > 0.2)
                {
                    results.Add(new SearchResult
                    {
                        FilePath = file.Path,
                        RelativePath = file.RelativePath,
                        Snippet = $"File: {file.RelativePath} ({file.Language})",
                        Score = score,
                        Reason = "file name match",
                        Line = 1
                    });
                }
            }
            
            // Deduplicate and sort by score
            var grouped = results
                .GroupBy(r => r.FilePath + ":" + r.Line)
                .Select(g => g.OrderByDescending(r => r.Score).First())
                .OrderByDescending(r => r.Score)
                .Take(topK)
                .ToList();
            
            AgentModeLogger.Instance.LogRetrievalQuery(query, topK, grouped);
            return Task.FromResult(grouped);
        }
        
        public List<string> GetIndexedFiles()
        {
            lock (_lock)
            {
                return _index.Files.Select(f => f.Path).ToList();
            }
        }
        
        public IndexStats GetStats()
        {
            lock (_lock)
            {
                return new IndexStats
                {
                    FileCount = _index.Files.Count,
                    SymbolCount = _index.Symbols.Count,
                    ChunkCount = _index.Chunks.Count,
                    LastUpdated = _index.LastUpdated,
                    IndexSizeBytes = EstimateIndexSize()
                };
            }
        }
        
        #endregion

        #region Persistence
        
        public async Task SaveIndexAsync()
        {
            try
            {
                var indexPath = GetIndexPath();
                var dir = Path.GetDirectoryName(indexPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                
                var json = JsonSerializer.Serialize(_index, new JsonSerializerOptions
                {
                    WriteIndented = true
                });
                
                await File.WriteAllTextAsync(indexPath, json);
                Debug.WriteLine($"[AGENT2] Index saved to {indexPath}");
            }
            catch (Exception ex)
            {
                AgentModeLogger.Instance.LogError("SaveIndexAsync", ex);
            }
        }
        
        public async Task<bool> LoadIndexAsync()
        {
            try
            {
                var indexPath = GetIndexPath();
                if (!File.Exists(indexPath))
                    return false;
                
                var json = await File.ReadAllTextAsync(indexPath);
                var loaded = JsonSerializer.Deserialize<ProjectIndex>(json);
                
                if (loaded == null || loaded.Version != IndexVersion)
                {
                    Debug.WriteLine($"[AGENT2] Index version mismatch, rebuilding");
                    return false;
                }
                
                lock (_lock)
                {
                    _index = loaded;
                    _projectPath = loaded.ProjectPath;
                }
                
                Debug.WriteLine($"[AGENT2] Index loaded: {_index.Files.Count} files");
                return true;
            }
            catch (Exception ex)
            {
                AgentModeLogger.Instance.LogError("LoadIndexAsync", ex);
                return false;
            }
        }
        
        private string GetIndexPath()
        {
            var hash = ComputeHash(_projectPath);
            return Path.Combine(IndexDir, hash, "index.json");
        }
        
        #endregion
        
        #region File Indexing
        
        private async Task IndexFileAsync(string filePath)
        {
            try
            {
                var fileInfo = new FileInfo(filePath);
                if (!fileInfo.Exists || fileInfo.Length > 1024 * 1024) // Skip files > 1MB
                    return;
                
                var content = await File.ReadAllTextAsync(filePath);
                var relativePath = GetRelativePath(filePath);
                var language = GetLanguage(filePath);
                
                // Add file entry
                var fileEntry = new FileIndexEntry
                {
                    Path = filePath,
                    RelativePath = relativePath,
                    Size = fileInfo.Length,
                    LastModified = fileInfo.LastWriteTimeUtc,
                    Language = language,
                    ContentHash = ComputeHash(content)
                };
                
                lock (_lock)
                {
                    _index.Files.Add(fileEntry);
                }
                
                // Extract symbols
                var symbols = ExtractSymbols(filePath, content, language);
                lock (_lock)
                {
                    _index.Symbols.AddRange(symbols);
                }
                
                // Create chunks
                var chunks = CreateChunks(filePath, content);
                lock (_lock)
                {
                    _index.Chunks.AddRange(chunks);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AGENT2] Failed to index {filePath}: {ex.Message}");
            }
        }
        
        private List<string> GetAllFiles(string rootPath)
        {
            var files = new List<string>();
            
            try
            {
                foreach (var file in Directory.EnumerateFiles(rootPath))
                {
                    var ext = Path.GetExtension(file);
                    if (SupportedExtensions.Contains(ext))
                        files.Add(file);
                }
                
                foreach (var dir in Directory.EnumerateDirectories(rootPath))
                {
                    var dirName = Path.GetFileName(dir);
                    if (!ExcludedDirs.Contains(dirName))
                        files.AddRange(GetAllFiles(dir));
                }
            }
            catch (UnauthorizedAccessException)
            {
                // Skip inaccessible directories
            }
            
            return files;
        }
        
        #endregion

        #region Symbol Extraction
        
        private List<SymbolEntry> ExtractSymbols(string filePath, string content, string language)
        {
            var symbols = new List<SymbolEntry>();
            var lines = content.Split('\n');
            
            // C# patterns
            if (language == "csharp")
            {
                symbols.AddRange(ExtractCSharpSymbols(filePath, lines));
            }
            // TypeScript/JavaScript patterns
            else if (language == "typescript" || language == "javascript")
            {
                symbols.AddRange(ExtractTypeScriptSymbols(filePath, lines));
            }
            // Python patterns
            else if (language == "python")
            {
                symbols.AddRange(ExtractPythonSymbols(filePath, lines));
            }
            
            return symbols;
        }
        
        private List<SymbolEntry> ExtractCSharpSymbols(string filePath, string[] lines)
        {
            var symbols = new List<SymbolEntry>();
            var classPattern = new Regex(@"^\s*(public|private|internal|protected)?\s*(partial\s+)?(class|interface|struct|enum|record)\s+(\w+)", RegexOptions.Compiled);
            var methodPattern = new Regex(@"^\s*(public|private|internal|protected)?\s*(static\s+)?(async\s+)?(\w+(?:<[^>]+>)?)\s+(\w+)\s*\(", RegexOptions.Compiled);
            var propertyPattern = new Regex(@"^\s*(public|private|internal|protected)?\s*(static\s+)?(\w+(?:<[^>]+>)?)\s+(\w+)\s*\{", RegexOptions.Compiled);
            
            string currentClass = "";
            
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                
                // Match class/interface/struct
                var classMatch = classPattern.Match(line);
                if (classMatch.Success)
                {
                    currentClass = classMatch.Groups[4].Value;
                    symbols.Add(new SymbolEntry
                    {
                        Name = currentClass,
                        Kind = GetSymbolKind(classMatch.Groups[3].Value),
                        FilePath = filePath,
                        Line = i + 1,
                        Signature = line.Trim(),
                        Keywords = ExtractKeywords(currentClass)
                    });
                    continue;
                }
                
                // Match methods
                var methodMatch = methodPattern.Match(line);
                if (methodMatch.Success && !line.Contains("=") && !line.TrimStart().StartsWith("//"))
                {
                    var methodName = methodMatch.Groups[5].Value;
                    if (!IsKeyword(methodName))
                    {
                        symbols.Add(new SymbolEntry
                        {
                            Name = methodName,
                            Kind = SymbolKind.Method,
                            FilePath = filePath,
                            Line = i + 1,
                            Signature = line.Trim(),
                            ParentSymbol = currentClass,
                            Keywords = ExtractKeywords(methodName)
                        });
                    }
                }
                
                // Match properties
                var propMatch = propertyPattern.Match(line);
                if (propMatch.Success && !line.Contains("(") && !line.TrimStart().StartsWith("//"))
                {
                    var propName = propMatch.Groups[4].Value;
                    if (!IsKeyword(propName))
                    {
                        symbols.Add(new SymbolEntry
                        {
                            Name = propName,
                            Kind = SymbolKind.Property,
                            FilePath = filePath,
                            Line = i + 1,
                            Signature = line.Trim(),
                            ParentSymbol = currentClass,
                            Keywords = ExtractKeywords(propName)
                        });
                    }
                }
            }
            
            return symbols;
        }
        
        private List<SymbolEntry> ExtractTypeScriptSymbols(string filePath, string[] lines)
        {
            var symbols = new List<SymbolEntry>();
            var classPattern = new Regex(@"^\s*(export\s+)?(class|interface|type|enum)\s+(\w+)", RegexOptions.Compiled);
            var functionPattern = new Regex(@"^\s*(export\s+)?(async\s+)?function\s+(\w+)", RegexOptions.Compiled);
            var constFuncPattern = new Regex(@"^\s*(export\s+)?const\s+(\w+)\s*=\s*(async\s+)?\(", RegexOptions.Compiled);
            
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                
                var classMatch = classPattern.Match(line);
                if (classMatch.Success)
                {
                    symbols.Add(new SymbolEntry
                    {
                        Name = classMatch.Groups[3].Value,
                        Kind = GetSymbolKind(classMatch.Groups[2].Value),
                        FilePath = filePath,
                        Line = i + 1,
                        Signature = line.Trim(),
                        Keywords = ExtractKeywords(classMatch.Groups[3].Value)
                    });
                    continue;
                }
                
                var funcMatch = functionPattern.Match(line);
                if (funcMatch.Success)
                {
                    symbols.Add(new SymbolEntry
                    {
                        Name = funcMatch.Groups[3].Value,
                        Kind = SymbolKind.Method,
                        FilePath = filePath,
                        Line = i + 1,
                        Signature = line.Trim(),
                        Keywords = ExtractKeywords(funcMatch.Groups[3].Value)
                    });
                    continue;
                }
                
                var constMatch = constFuncPattern.Match(line);
                if (constMatch.Success)
                {
                    symbols.Add(new SymbolEntry
                    {
                        Name = constMatch.Groups[2].Value,
                        Kind = SymbolKind.Method,
                        FilePath = filePath,
                        Line = i + 1,
                        Signature = line.Trim(),
                        Keywords = ExtractKeywords(constMatch.Groups[2].Value)
                    });
                }
            }
            
            return symbols;
        }
        
        private List<SymbolEntry> ExtractPythonSymbols(string filePath, string[] lines)
        {
            var symbols = new List<SymbolEntry>();
            var classPattern = new Regex(@"^class\s+(\w+)", RegexOptions.Compiled);
            var funcPattern = new Regex(@"^(async\s+)?def\s+(\w+)", RegexOptions.Compiled);
            
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                
                var classMatch = classPattern.Match(line);
                if (classMatch.Success)
                {
                    symbols.Add(new SymbolEntry
                    {
                        Name = classMatch.Groups[1].Value,
                        Kind = SymbolKind.Class,
                        FilePath = filePath,
                        Line = i + 1,
                        Signature = line.Trim(),
                        Keywords = ExtractKeywords(classMatch.Groups[1].Value)
                    });
                    continue;
                }
                
                var funcMatch = funcPattern.Match(line);
                if (funcMatch.Success)
                {
                    symbols.Add(new SymbolEntry
                    {
                        Name = funcMatch.Groups[2].Value,
                        Kind = SymbolKind.Method,
                        FilePath = filePath,
                        Line = i + 1,
                        Signature = line.Trim(),
                        Keywords = ExtractKeywords(funcMatch.Groups[2].Value)
                    });
                }
            }
            
            return symbols;
        }
        
        #endregion

        #region Chunking
        
        private List<ChunkEntry> CreateChunks(string filePath, string content)
        {
            var chunks = new List<ChunkEntry>();
            var lines = content.Split('\n');
            
            int startLine = 0;
            var currentChunk = new StringBuilder();
            
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                
                if (currentChunk.Length + line.Length > ChunkMaxSize && currentChunk.Length >= ChunkMinSize)
                {
                    // Save current chunk
                    chunks.Add(CreateChunkEntry(filePath, startLine, i, currentChunk.ToString()));
                    currentChunk.Clear();
                    startLine = i;
                }
                
                currentChunk.AppendLine(line);
            }
            
            // Save remaining content
            if (currentChunk.Length >= ChunkMinSize)
            {
                chunks.Add(CreateChunkEntry(filePath, startLine, lines.Length, currentChunk.ToString()));
            }
            
            return chunks;
        }
        
        private ChunkEntry CreateChunkEntry(string filePath, int startLine, int endLine, string content)
        {
            return new ChunkEntry
            {
                FilePath = filePath,
                StartLine = startLine + 1,
                EndLine = endLine,
                Content = content.Trim(),
                Keywords = ExtractKeywords(content),
                SymbolContext = GetSymbolContext(filePath, startLine)
            };
        }
        
        private string GetSymbolContext(string filePath, int line)
        {
            var symbol = _index.Symbols
                .Where(s => s.FilePath == filePath && s.Line <= line)
                .OrderByDescending(s => s.Line)
                .FirstOrDefault();
            
            return symbol?.Name;
        }
        
        #endregion
        
        #region Scoring
        
        private double CalculateSymbolScore(SymbolEntry symbol, string[] queryTerms, string queryLower)
        {
            double score = 0;
            var nameLower = symbol.Name.ToLowerInvariant();
            
            // Exact match
            if (nameLower == queryLower)
                return 1.0;
            
            // Contains query
            if (nameLower.Contains(queryLower))
                score += 0.7;
            
            // Term matching
            foreach (var term in queryTerms)
            {
                if (nameLower.Contains(term))
                    score += 0.3;
                if (symbol.Keywords.Any(k => k.Contains(term)))
                    score += 0.2;
            }
            
            // Boost for class/interface matches
            if (symbol.Kind == SymbolKind.Class || symbol.Kind == SymbolKind.Interface)
                score *= 1.2;
            
            return Math.Min(score, 1.0);
        }
        
        private double CalculateChunkScore(ChunkEntry chunk, string[] queryTerms, string queryLower)
        {
            double score = 0;
            var contentLower = chunk.Content.ToLowerInvariant();
            
            // Contains full query
            if (contentLower.Contains(queryLower))
                score += 0.5;
            
            // Term matching
            int matchedTerms = 0;
            foreach (var term in queryTerms)
            {
                if (contentLower.Contains(term))
                {
                    matchedTerms++;
                    score += 0.2;
                }
                if (chunk.Keywords.Any(k => k.Contains(term)))
                    score += 0.15;
            }
            
            // Boost for matching multiple terms
            if (queryTerms.Length > 1 && matchedTerms == queryTerms.Length)
                score *= 1.3;
            
            return Math.Min(score, 1.0);
        }
        
        private double CalculateFileNameScore(FileIndexEntry file, string[] queryTerms, string queryLower)
        {
            double score = 0;
            var nameLower = Path.GetFileNameWithoutExtension(file.RelativePath).ToLowerInvariant();
            var pathLower = file.RelativePath.ToLowerInvariant();
            
            // Exact file name match
            if (nameLower == queryLower)
                return 1.0;
            
            // File name contains query
            if (nameLower.Contains(queryLower))
                score += 0.6;
            
            // Path contains query
            if (pathLower.Contains(queryLower))
                score += 0.4;
            
            // Term matching
            foreach (var term in queryTerms)
            {
                if (nameLower.Contains(term))
                    score += 0.25;
                if (pathLower.Contains(term))
                    score += 0.15;
            }
            
            return Math.Min(score, 1.0);
        }
        
        #endregion
        
        #region Helpers
        
        private bool PassesFilters(string filePath, SearchFilters? filters)
        {
            if (filters == null) return true;
            
            if (filters.FileExtensions != null && filters.FileExtensions.Count > 0)
            {
                var ext = Path.GetExtension(filePath);
                if (!filters.FileExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
                    return false;
            }
            
            if (filters.ExcludePaths != null)
            {
                foreach (var exclude in filters.ExcludePaths)
                {
                    if (filePath.Contains(exclude, StringComparison.OrdinalIgnoreCase))
                        return false;
                }
            }
            
            return true;
        }
        
        private string GetRelativePath(string fullPath)
        {
            if (string.IsNullOrEmpty(_projectPath))
                return fullPath;
            
            if (fullPath.StartsWith(_projectPath, StringComparison.OrdinalIgnoreCase))
            {
                var relative = fullPath.Substring(_projectPath.Length).TrimStart('\\', '/');
                return relative.Replace('\\', '/');
            }
            
            return fullPath;
        }
        
        private static string GetLanguage(string filePath)
        {
            var ext = Path.GetExtension(filePath).ToLowerInvariant();
            return ext switch
            {
                ".cs" => "csharp",
                ".xaml" => "xaml",
                ".csproj" or ".sln" => "xml",
                ".ts" or ".tsx" => "typescript",
                ".js" or ".jsx" => "javascript",
                ".py" => "python",
                ".java" => "java",
                ".cpp" or ".c" or ".h" or ".hpp" => "cpp",
                ".json" => "json",
                ".xml" or ".config" => "xml",
                ".html" => "html",
                ".css" or ".scss" => "css",
                ".md" => "markdown",
                ".yaml" or ".yml" => "yaml",
                _ => "text"
            };
        }
        
        private static SymbolKind GetSymbolKind(string keyword)
        {
            return keyword.ToLowerInvariant() switch
            {
                "class" => SymbolKind.Class,
                "interface" => SymbolKind.Interface,
                "struct" => SymbolKind.Struct,
                "enum" => SymbolKind.Enum,
                "type" => SymbolKind.Class,
                "record" => SymbolKind.Class,
                _ => SymbolKind.Class
            };
        }
        
        private static List<string> ExtractKeywords(string text)
        {
            // Split camelCase and PascalCase
            var words = Regex.Split(text, @"(?<!^)(?=[A-Z])|[_\-\s]+")
                .Where(w => w.Length > 2)
                .Select(w => w.ToLowerInvariant())
                .Distinct()
                .ToList();
            
            return words;
        }
        
        private static bool IsKeyword(string name)
        {
            var keywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "if", "else", "for", "while", "do", "switch", "case", "break", "continue",
                "return", "try", "catch", "finally", "throw", "new", "this", "base",
                "true", "false", "null", "void", "var", "get", "set", "value"
            };
            return keywords.Contains(name);
        }
        
        private static string ComputeHash(string input)
        {
            using var sha = SHA256.Create();
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(input));
            return Convert.ToHexString(bytes)[..16].ToLowerInvariant();
        }
        
        private static string TruncateSnippet(string text, int maxLength)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
                return text;
            return text[..maxLength] + "...";
        }
        
        private long EstimateIndexSize()
        {
            // Rough estimate based on entry counts
            return _index.Files.Count * 200 + _index.Symbols.Count * 150 + _index.Chunks.Count * 600;
        }
        
        #endregion
    }
}
