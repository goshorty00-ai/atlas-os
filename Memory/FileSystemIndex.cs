using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace AtlasAI.Memory
{
    /// <summary>
    /// Indexes the user's file system so Atlas can find and open any folder/file by name.
    /// Performs deep scanning of user folders to enable natural language file/folder access.
    /// </summary>
    public class FileSystemIndex
    {
        private static FileSystemIndex? _instance;
        public static FileSystemIndex Instance => _instance ??= new FileSystemIndex();
        
        private Dictionary<string, List<string>> _folderIndex = new(); // name -> list of paths
        private Dictionary<string, List<string>> _fileIndex = new();   // name -> list of paths
        private DateTime _lastIndexTime = DateTime.MinValue;
        private bool _isIndexing = false;
        
        public event Action<string>? IndexingProgress;
        
        private static readonly string IndexPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AtlasAI", "file_index.json");
        
        public bool IsIndexed => _folderIndex.Count > 0;
        public int FolderCount => _folderIndex.Values.Sum(v => v.Count);
        public int FileCount => _fileIndex.Values.Sum(v => v.Count);
        public bool IsIndexing => _isIndexing;
        public DateTime LastIndexTime => _lastIndexTime;
        
        public FileSystemIndex()
        {
            LoadIndex();
            
            // Auto-index on first run or if index is old
            if (!IsIndexed || (DateTime.Now - _lastIndexTime).TotalHours > 24)
            {
                _ = IndexAsync(force: true);
            }
        }
        
        /// <summary>
        /// Find a folder by name using smart matching
        /// </summary>
        public string? FindFolder(string searchTerm)
        {
            if (string.IsNullOrWhiteSpace(searchTerm)) return null;
            
            var normalized = NormalizeSearchTerm(searchTerm);
            Debug.WriteLine($"[FileIndex] Searching for folder: '{searchTerm}' -> '{normalized}'");
            
            // 1. Exact match
            if (_folderIndex.TryGetValue(normalized, out var exactMatches))
            {
                var best = SelectBestPath(exactMatches);
                Debug.WriteLine($"[FileIndex] Exact match: {best}");
                return best;
            }
            
            // 2. Try without common words
            var withoutCommon = RemoveCommonWords(normalized);
            if (!string.IsNullOrEmpty(withoutCommon) && _folderIndex.TryGetValue(withoutCommon, out var commonMatches))
            {
                var best = SelectBestPath(commonMatches);
                Debug.WriteLine($"[FileIndex] Match without common words: {best}");
                return best;
            }
            
            // 3. Fuzzy match - with path quality scoring
            var candidates = new List<(string Path, int Score)>();
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            
            foreach (var kvp in _folderIndex)
            {
                var score = CalculateMatchScore(normalized, kvp.Key);
                if (score > 0)
                {
                    foreach (var path in kvp.Value)
                    {
                        var adjustedScore = score;
                        var lower = path.ToLower();
                        
                        // Boost paths directly under user profile
                        if (path.StartsWith(userProfile)) adjustedScore += 20;
                        
                        // Heavily penalize paths in cache/temp/appdata folders
                        if (lower.Contains("\\cache\\") || lower.Contains("\\temp\\")) 
                            adjustedScore -= 50;
                        if (lower.Contains("\\appdata\\local\\") || lower.Contains("\\appdata\\locallow\\"))
                            adjustedScore -= 30;
                        
                        // Penalize very deep paths
                        var depth = path.Split(Path.DirectorySeparatorChar).Length;
                        if (depth > 6) adjustedScore -= (depth - 6) * 10;
                        
                        candidates.Add((path, adjustedScore));
                    }
                }
            }
            
            if (candidates.Count > 0)
            {
                var best = candidates.OrderByDescending(c => c.Score).First();
                Debug.WriteLine($"[FileIndex] Fuzzy match: {best.Path} (score: {best.Score})");
                return best.Path;
            }
            
            Debug.WriteLine($"[FileIndex] No match for: {searchTerm}");
            return null;
        }
        
        /// <summary>
        /// Find a file by name
        /// </summary>
        public string? FindFile(string searchTerm)
        {
            if (string.IsNullOrWhiteSpace(searchTerm)) return null;
            
            var normalized = NormalizeSearchTerm(searchTerm);
            
            if (_fileIndex.TryGetValue(normalized, out var exactMatches))
                return SelectBestPath(exactMatches);
            
            var candidates = new List<(string Path, int Score)>();
            foreach (var kvp in _fileIndex)
            {
                var score = CalculateMatchScore(normalized, kvp.Key);
                if (score > 0)
                {
                    foreach (var path in kvp.Value)
                        candidates.Add((path, score));
                }
            }
            
            return candidates.OrderByDescending(c => c.Score).FirstOrDefault().Path;
        }
        
        /// <summary>
        /// Search folders with scores
        /// </summary>
        public List<(string Name, string Path, int Score)> SearchFolders(string term, int maxResults = 10)
        {
            var normalized = NormalizeSearchTerm(term);
            var results = new List<(string Name, string Path, int Score)>();
            
            foreach (var kvp in _folderIndex)
            {
                var score = CalculateMatchScore(normalized, kvp.Key);
                if (score > 0)
                {
                    foreach (var path in kvp.Value)
                        results.Add((Path.GetFileName(path), path, score));
                }
            }
            
            return results.OrderByDescending(r => r.Score).Take(maxResults).ToList();
        }
        
        private string NormalizeSearchTerm(string term)
        {
            var result = term.ToLower().Trim();
            result = Regex.Replace(result, @"\b(the|my|a|an|folder|directory|dir|file|open|show|go to|navigate to)\b", " ");
            result = Regex.Replace(result, @"[_\-\s]+", " ");
            result = result.Replace("photos", "pictures").Replace("pics", "pictures");
            return result.Trim();
        }
        
        private string RemoveCommonWords(string term)
        {
            var words = term.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var common = new HashSet<string> { "folder", "directory", "files", "my", "the", "a", "an" };
            return string.Join(" ", words.Where(w => !common.Contains(w)));
        }
        
        private int CalculateMatchScore(string search, string folder)
        {
            if (string.IsNullOrEmpty(search) || string.IsNullOrEmpty(folder)) return 0;
            if (search == folder) return 100;
            
            int score = 0;
            if (folder.Contains(search)) score += 80;
            if (search.Contains(folder)) score += 70;
            
            var searchWords = search.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var folderWords = folder.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            
            foreach (var sw in searchWords)
            {
                if (sw.Length < 2) continue;
                foreach (var fw in folderWords)
                {
                    if (fw == sw) score += 30;
                    else if (fw.StartsWith(sw) || sw.StartsWith(fw)) score += 20;
                    else if (fw.Contains(sw) || sw.Contains(fw)) score += 10;
                }
            }
            
            return Math.Max(0, score - Math.Abs(search.Length - folder.Length) / 2);
        }
        
        private string SelectBestPath(List<string> paths)
        {
            if (paths.Count == 1) return paths[0];
            
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            
            // Score each path - prefer:
            // 1. Paths directly under user profile (not in AppData/cache folders)
            // 2. Shallower paths (fewer directory levels)
            // 3. Paths not in cache/temp/appdata folders
            return paths
                .OrderByDescending(p => {
                    int score = 0;
                    var lower = p.ToLower();
                    
                    // Prefer paths directly under user profile
                    if (p.StartsWith(userProfile)) score += 50;
                    
                    // Penalize paths in cache/temp/appdata folders
                    if (lower.Contains("\\cache\\") || lower.Contains("\\temp\\") || 
                        lower.Contains("\\appdata\\") || lower.Contains("\\.")) 
                        score -= 100;
                    
                    // Penalize very deep paths (more than 5 levels from user profile)
                    var depth = p.Split(Path.DirectorySeparatorChar).Length;
                    score -= depth * 5;
                    
                    return score;
                })
                .ThenBy(p => p.Split(Path.DirectorySeparatorChar).Length)
                .First();
        }
        
        /// <summary>
        /// Index the file system - deep scan
        /// </summary>
        public async Task IndexAsync(bool force = false)
        {
            if (_isIndexing) 
            {
                Debug.WriteLine("[FileIndex] Already indexing, skipping");
                return;
            }
            if (!force && (DateTime.Now - _lastIndexTime).TotalHours < 6 && _folderIndex.Count > 0) 
            {
                Debug.WriteLine("[FileIndex] Index is fresh, skipping");
                return;
            }
            
            _isIndexing = true;
            IndexingProgress?.Invoke("Starting file system scan...");
            Debug.WriteLine("[FileIndex] Starting deep indexing...");
            
            var newFolders = new Dictionary<string, List<string>>();
            var newFiles = new Dictionary<string, List<string>>();
            
            try
            {
                await Task.Run(() =>
                {
                    var sw = Stopwatch.StartNew();
                    
                    var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                    Debug.WriteLine($"[FileIndex] User profile: {userProfile}");
                    
                    // FIRST: Directly enumerate user profile subdirectories
                    IndexingProgress?.Invoke("Scanning user folders...");
                    try
                    {
                        var userDirs = Directory.GetDirectories(userProfile);
                        Debug.WriteLine($"[FileIndex] Found {userDirs.Length} directories in user profile");
                        
                        foreach (var dir in userDirs)
                        {
                            var dirName = Path.GetFileName(dir);
                            // Skip hidden folders but still scan the rest
                            if (dirName.StartsWith(".") || dirName.StartsWith("$")) continue;
                            
                            Debug.WriteLine($"[FileIndex] Scanning: {dirName}");
                            IndexDirectory(dir, newFolders, newFiles, maxDepth: 6);
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[FileIndex] Error scanning user profile: {ex.Message}");
                    }
                    
                    Debug.WriteLine($"[FileIndex] After user profile scan: {newFolders.Count} folder names, {newFolders.Values.Sum(v => v.Count)} total paths");
                    
                    // SECOND: Scan special folders with higher depth
                    var specialFolders = new[]
                    {
                        Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                        Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                        Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
                        Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
                    };
                    
                    foreach (var folder in specialFolders)
                    {
                        if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
                        {
                            var folderName = Path.GetFileName(folder);
                            IndexingProgress?.Invoke($"Deep scanning {folderName}...");
                            Debug.WriteLine($"[FileIndex] Deep scanning special folder: {folder}");
                            IndexDirectory(folder, newFolders, newFiles, maxDepth: 8);
                        }
                    }
                    
                    // THIRD: Scan additional common locations
                    var additionalPaths = new[]
                    {
                        Path.Combine(userProfile, "Downloads"),
                        Path.Combine(userProfile, "OneDrive"),
                        Path.Combine(userProfile, "Dropbox"),
                        Path.Combine(userProfile, "Google Drive"),
                        Path.Combine(userProfile, "iCloudDrive"),
                        Path.Combine(userProfile, "iCloud Drive"),
                        Path.Combine(userProfile, "Projects"),
                        Path.Combine(userProfile, "Source"),
                        Path.Combine(userProfile, "Code"),
                        Path.Combine(userProfile, "3D Models"),
                        Path.Combine(userProfile, "3D Objects"),
                    };
                    
                    foreach (var path in additionalPaths)
                    {
                        if (Directory.Exists(path))
                        {
                            var pathName = Path.GetFileName(path);
                            IndexingProgress?.Invoke($"Scanning {pathName}...");
                            IndexDirectory(path, newFolders, newFiles, maxDepth: 8);
                        }
                    }
                    
                    Debug.WriteLine($"[FileIndex] After all user scans: {newFolders.Count} folder names");
                    
                    // FOURTH: Scan drive roots (limited depth)
                    foreach (var drive in DriveInfo.GetDrives())
                    {
                        if (drive.IsReady && drive.DriveType == DriveType.Fixed)
                        {
                            IndexingProgress?.Invoke($"Scanning {drive.Name}...");
                            Debug.WriteLine($"[FileIndex] Scanning drive: {drive.Name}");
                            try
                            {
                                var rootDirs = Directory.GetDirectories(drive.Name);
                                foreach (var dir in rootDirs)
                                {
                                    var name = Path.GetFileName(dir).ToLower();
                                    // Skip system folders
                                    if (name.StartsWith("$") || name == "windows" || 
                                        name == "program files" || name == "program files (x86)" ||
                                        name == "programdata" || name == "recovery" ||
                                        name == "perflogs" || name == "system volume information") continue;
                                    
                                    // Deeper scan for user-related folders
                                    var depth = (name == "users" || name == "games" || name == "projects" || 
                                                 name == "data" || name == "files") ? 6 : 3;
                                    IndexDirectory(dir, newFolders, newFiles, maxDepth: depth);
                                }
                            }
                            catch (Exception ex) 
                            { 
                                Debug.WriteLine($"[FileIndex] Drive error {drive.Name}: {ex.Message}"); 
                            }
                        }
                    }
                    
                    sw.Stop();
                    var fc = newFolders.Values.Sum(v => v.Count);
                    var fic = newFiles.Values.Sum(v => v.Count);
                    Debug.WriteLine($"[FileIndex] COMPLETE: Indexed {fc} folders, {fic} files in {sw.ElapsedMilliseconds}ms");
                    IndexingProgress?.Invoke($"Indexed {fc} folders, {fic} files");
                });
                
                // Update indexes AFTER Task.Run completes
                _folderIndex = newFolders;
                _fileIndex = newFiles;
                _lastIndexTime = DateTime.Now;
                
                Debug.WriteLine($"[FileIndex] Indexes updated: {FolderCount} folders, {FileCount} files");
                
                SaveIndex();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FileIndex] FATAL Error during indexing: {ex.Message}\n{ex.StackTrace}");
            }
            finally
            {
                _isIndexing = false;
            }
        }

        private void IndexDirectory(string path, Dictionary<string, List<string>> folders, Dictionary<string, List<string>> files, int maxDepth, int depth = 0)
        {
            if (depth > maxDepth || string.IsNullOrEmpty(path)) return;
            
            try
            {
                if (!Directory.Exists(path)) return;
                
                var folderName = Path.GetFileName(path);
                if (string.IsNullOrEmpty(folderName)) folderName = path; // Root drive case
                
                var folderNameLower = folderName.ToLower();
                
                // Skip list - but NOT at depth 0 (we want to index the folder we were asked to index)
                var skip = new HashSet<string> { "node_modules", ".git", ".vs", "bin", "obj", "packages", 
                    "$recycle.bin", "system volume information", ".cache", "__pycache__" };
                
                if (depth > 0 && (skip.Contains(folderNameLower) || folderNameLower.StartsWith(".") || folderNameLower.StartsWith("$"))) 
                    return;
                
                // Check if hidden/system - but be lenient
                try
                {
                    var dirInfo = new DirectoryInfo(path);
                    var attrs = dirInfo.Attributes;
                    // Only skip if BOTH hidden AND system (like system folders)
                    if ((attrs & FileAttributes.Hidden) != 0 && (attrs & FileAttributes.System) != 0) 
                        return;
                }
                catch { } // Can't read attributes, continue anyway
                
                // Index this folder with normalized name
                var normalized = Regex.Replace(folderNameLower, @"[_\-]+", " ").Trim();
                if (!string.IsNullOrEmpty(normalized))
                {
                    lock (folders)
                    {
                        if (!folders.ContainsKey(normalized)) folders[normalized] = new List<string>();
                        if (!folders[normalized].Contains(path)) folders[normalized].Add(path);
                        
                        // Also index without spaces for easier matching
                        var noSpaces = normalized.Replace(" ", "");
                        if (noSpaces != normalized && !string.IsNullOrEmpty(noSpaces))
                        {
                            if (!folders.ContainsKey(noSpaces)) folders[noSpaces] = new List<string>();
                            if (!folders[noSpaces].Contains(path)) folders[noSpaces].Add(path);
                        }
                    }
                }
                
                // Index subdirectories
                try 
                { 
                    var subdirs = Directory.GetDirectories(path);
                    foreach (var sub in subdirs) 
                    {
                        IndexDirectory(sub, folders, files, maxDepth, depth + 1); 
                    }
                } 
                catch (UnauthorizedAccessException) { } // Permission denied, skip
                catch { }
                
                // Index files (only at shallow depths to avoid slowdown)
                if (depth <= 4)
                {
                    try
                    {
                        foreach (var file in Directory.GetFiles(path))
                        {
                            var fn = Path.GetFileName(file).ToLower();
                            lock (files)
                            {
                                if (!files.ContainsKey(fn)) files[fn] = new List<string>();
                                if (!files[fn].Contains(file)) files[fn].Add(file);
                            }
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }
        
        private void SaveIndex()
        {
            try
            {
                var dir = Path.GetDirectoryName(IndexPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                
                var json = JsonSerializer.Serialize(new { LastIndexTime = _lastIndexTime, Folders = _folderIndex, Files = _fileIndex }, 
                    new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(IndexPath, json);
            }
            catch (Exception ex) { Debug.WriteLine($"[FileIndex] Save error: {ex.Message}"); }
        }
        
        private void LoadIndex()
        {
            try
            {
                if (!File.Exists(IndexPath)) return;
                
                using var doc = JsonDocument.Parse(File.ReadAllText(IndexPath));
                var root = doc.RootElement;
                
                if (root.TryGetProperty("LastIndexTime", out var t)) _lastIndexTime = t.GetDateTime();
                
                _folderIndex = new Dictionary<string, List<string>>();
                if (root.TryGetProperty("Folders", out var f))
                {
                    foreach (var p in f.EnumerateObject())
                    {
                        var paths = new List<string>();
                        if (p.Value.ValueKind == JsonValueKind.Array)
                            foreach (var i in p.Value.EnumerateArray()) paths.Add(i.GetString() ?? "");
                        else if (p.Value.ValueKind == JsonValueKind.String)
                            paths.Add(p.Value.GetString() ?? "");
                        _folderIndex[p.Name] = paths;
                    }
                }
                
                _fileIndex = new Dictionary<string, List<string>>();
                if (root.TryGetProperty("Files", out var fi))
                {
                    foreach (var p in fi.EnumerateObject())
                    {
                        var paths = new List<string>();
                        if (p.Value.ValueKind == JsonValueKind.Array)
                            foreach (var i in p.Value.EnumerateArray()) paths.Add(i.GetString() ?? "");
                        else if (p.Value.ValueKind == JsonValueKind.String)
                            paths.Add(p.Value.GetString() ?? "");
                        _fileIndex[p.Name] = paths;
                    }
                }
                
                Debug.WriteLine($"[FileIndex] Loaded {FolderCount} folders, {FileCount} files");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FileIndex] Load error: {ex.Message}");
                _folderIndex = new Dictionary<string, List<string>>();
                _fileIndex = new Dictionary<string, List<string>>();
            }
        }
        
        public Task ReindexAsync() => IndexAsync(force: true);
    }
}
