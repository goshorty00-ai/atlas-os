using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AtlasAI.SystemControl
{
    /// <summary>
    /// Online threat database that fetches real malware signatures from public sources
    /// Similar to how Norton/McAfee update their definitions
    /// </summary>
    public class OnlineThreatDatabase
    {
        private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(30) };
        private static readonly string _cacheDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AtlasAI", "ThreatDB");
        private static readonly string _hashDbPath = Path.Combine(_cacheDir, "malware_hashes.json");
        private static readonly string _signaturesPath = Path.Combine(_cacheDir, "signatures.json");
        private static readonly string _lastUpdatePath = Path.Combine(_cacheDir, "last_update.txt");
        
        // Known malware hash databases (free, public sources)
        private static readonly string[] HashSources = new[]
        {
            "https://bazaar.abuse.ch/export/txt/sha256/recent/",  // MalwareBazaar recent hashes
            "https://urlhaus.abuse.ch/downloads/text_recent/",    // URLhaus malicious URLs
        };
        
        // Cached data
        private static HashSet<string> _malwareHashes = new(StringComparer.OrdinalIgnoreCase);
        private static List<ThreatSignature> _signatures = new();
        private static DateTime _lastUpdate = DateTime.MinValue;
        private static bool _isInitialized = false;

        public static int TotalSignatures => _signatures.Count + _malwareHashes.Count;
        public static DateTime LastUpdateTime => _lastUpdate;
        public static bool IsInitialized => _isInitialized;

        /// <summary>
        /// Initialize the database - load from cache or download
        /// </summary>
        public static async Task InitializeAsync(CancellationToken ct = default)
        {
            try
            {
                Directory.CreateDirectory(_cacheDir);
                
                // Load cached data
                await LoadCachedDataAsync();
                
                // Check if we need to update (older than 24 hours)
                if ((DateTime.Now - _lastUpdate).TotalHours > 24)
                {
                    Debug.WriteLine("[ThreatDB] Cache is stale, updating...");
                    await UpdateDefinitionsAsync(ct);
                }
                
                _isInitialized = true;
                Debug.WriteLine($"[ThreatDB] Initialized with {TotalSignatures} signatures");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ThreatDB] Init error: {ex.Message}");
                // Load built-in definitions as fallback
                LoadBuiltInDefinitions();
                _isInitialized = true;
            }
        }

        /// <summary>
        /// Update definitions from online sources
        /// </summary>
        public static async Task<OnlineUpdateResult> UpdateDefinitionsAsync(CancellationToken ct = default)
        {
            var result = new OnlineUpdateResult();
            
            try
            {
                Directory.CreateDirectory(_cacheDir);
                int newHashes = 0;
                int newSignatures = 0;

                // 1. Fetch from MalwareBazaar (real malware hashes)
                try
                {
                    Debug.WriteLine("[ThreatDB] Fetching from MalwareBazaar...");
                    var response = await _httpClient.GetStringAsync(
                        "https://bazaar.abuse.ch/export/txt/sha256/recent/", ct);
                    
                    var lines = response.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                    foreach (var line in lines)
                    {
                        if (line.StartsWith("#")) continue;
                        var hash = line.Trim();
                        if (hash.Length == 64 && !_malwareHashes.Contains(hash))
                        {
                            _malwareHashes.Add(hash);
                            newHashes++;
                        }
                    }
                    result.SourcesUpdated++;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[ThreatDB] MalwareBazaar error: {ex.Message}");
                }

                // 2. Fetch from abuse.ch Feodo Tracker (banking trojans)
                try
                {
                    Debug.WriteLine("[ThreatDB] Fetching from Feodo Tracker...");
                    var response = await _httpClient.GetStringAsync(
                        "https://feodotracker.abuse.ch/downloads/ipblocklist_recommended.txt", ct);
                    
                    // This gives us C2 IPs - we'll use it for network scanning later
                    result.SourcesUpdated++;
                }
                catch { }

                // 3. Add curated known-bad process names and file patterns
                await UpdateKnownThreatsAsync(ct);
                newSignatures = _signatures.Count;

                // Save to cache
                await SaveCacheAsync();
                
                _lastUpdate = DateTime.Now;
                await File.WriteAllTextAsync(_lastUpdatePath, _lastUpdate.ToString("O"), ct);

                result.Success = true;
                result.NewHashesAdded = newHashes;
                result.NewSignaturesAdded = newSignatures;
                result.TotalDefinitions = TotalSignatures;
                result.Message = $"✅ Updated! Added {newHashes} new malware hashes.\nTotal definitions: {TotalSignatures:N0}";
                
                Debug.WriteLine($"[ThreatDB] Update complete: {newHashes} hashes, {newSignatures} signatures");
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"❌ Update failed: {ex.Message}";
                Debug.WriteLine($"[ThreatDB] Update error: {ex.Message}");
            }

            return result;
        }

        /// <summary>
        /// Check if a file hash is known malware
        /// </summary>
        public static bool IsKnownMalwareHash(string sha256Hash)
        {
            return _malwareHashes.Contains(sha256Hash);
        }

        /// <summary>
        /// Calculate SHA256 hash of a file
        /// </summary>
        public static async Task<string?> GetFileHashAsync(string filePath, CancellationToken ct = default)
        {
            try
            {
                if (!File.Exists(filePath)) return null;
                
                using var sha256 = SHA256.Create();
                using var stream = File.OpenRead(filePath);
                var hashBytes = await sha256.ComputeHashAsync(stream, ct);
                return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Check if a process name matches known threats
        /// </summary>
        public static ThreatSignature? MatchProcess(string processName, string? filePath = null)
        {
            var nameLower = processName.ToLowerInvariant();
            
            return _signatures.FirstOrDefault(s => 
                s.Type == SignatureType.Process && 
                (s.Pattern.Equals(nameLower, StringComparison.OrdinalIgnoreCase) ||
                 (!string.IsNullOrEmpty(s.PathPattern) && filePath?.Contains(s.PathPattern, StringComparison.OrdinalIgnoreCase) == true)));
        }

        /// <summary>
        /// Check if a file matches known threat signatures
        /// </summary>
        public static ThreatSignature? MatchFile(string filePath)
        {
            var fileName = Path.GetFileName(filePath).ToLowerInvariant();
            var fullPath = filePath.ToLowerInvariant();
            
            return _signatures.FirstOrDefault(s =>
                s.Type == SignatureType.File &&
                (s.Pattern.Equals(fileName, StringComparison.OrdinalIgnoreCase) ||
                 (!string.IsNullOrEmpty(s.PathPattern) && fullPath.Contains(s.PathPattern, StringComparison.OrdinalIgnoreCase))));
        }

        /// <summary>
        /// Check if a registry key matches known threats
        /// </summary>
        public static ThreatSignature? MatchRegistry(string keyPath, string? valueName = null)
        {
            return _signatures.FirstOrDefault(s =>
                s.Type == SignatureType.Registry &&
                keyPath.Contains(s.Pattern, StringComparison.OrdinalIgnoreCase));
        }

        private static async Task LoadCachedDataAsync()
        {
            try
            {
                // Load hashes
                if (File.Exists(_hashDbPath))
                {
                    var json = await File.ReadAllTextAsync(_hashDbPath);
                    var hashes = JsonSerializer.Deserialize<List<string>>(json);
                    if (hashes != null)
                        _malwareHashes = new HashSet<string>(hashes, StringComparer.OrdinalIgnoreCase);
                }

                // Load signatures
                if (File.Exists(_signaturesPath))
                {
                    var json = await File.ReadAllTextAsync(_signaturesPath);
                    _signatures = JsonSerializer.Deserialize<List<ThreatSignature>>(json) ?? new();
                }

                // Load last update time
                if (File.Exists(_lastUpdatePath))
                {
                    var timeStr = await File.ReadAllTextAsync(_lastUpdatePath);
                    if (DateTime.TryParse(timeStr, out var time))
                        _lastUpdate = time;
                }

                Debug.WriteLine($"[ThreatDB] Loaded {_malwareHashes.Count} hashes, {_signatures.Count} signatures from cache");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ThreatDB] Cache load error: {ex.Message}");
            }
        }

        private static async Task SaveCacheAsync()
        {
            try
            {
                var hashJson = JsonSerializer.Serialize(_malwareHashes.ToList());
                await File.WriteAllTextAsync(_hashDbPath, hashJson);

                var sigJson = JsonSerializer.Serialize(_signatures, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(_signaturesPath, sigJson);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ThreatDB] Cache save error: {ex.Message}");
            }
        }

        private static async Task UpdateKnownThreatsAsync(CancellationToken ct)
        {
            // Clear and rebuild signatures with REAL known threats only
            _signatures.Clear();
            
            // === REAL MALWARE - Verified threats ===
            
            // Cryptocurrency miners (unwanted)
            AddSignature(SignatureType.Process, "xmrig", "XMRig Cryptominer", ThreatLevel.High, "Cryptocurrency mining software - uses CPU/GPU resources");
            AddSignature(SignatureType.Process, "minerd", "CPU Miner", ThreatLevel.High, "Cryptocurrency miner");
            AddSignature(SignatureType.Process, "cgminer", "CGMiner", ThreatLevel.Medium, "Cryptocurrency mining software");
            AddSignature(SignatureType.Process, "bfgminer", "BFGMiner", ThreatLevel.Medium, "Cryptocurrency mining software");
            AddSignature(SignatureType.Process, "nicehash", "NiceHash Miner", ThreatLevel.Low, "Cryptocurrency mining (may be intentional)");
            
            // Known RATs (Remote Access Trojans)
            AddSignature(SignatureType.Process, "darkcomet", "DarkComet RAT", ThreatLevel.Critical, "Remote access trojan - allows remote control");
            AddSignature(SignatureType.Process, "njrat", "njRAT", ThreatLevel.Critical, "Remote access trojan");
            AddSignature(SignatureType.Process, "nanocore", "NanoCore RAT", ThreatLevel.Critical, "Remote access trojan");
            AddSignature(SignatureType.Process, "quasar", "Quasar RAT", ThreatLevel.Critical, "Remote access trojan");
            AddSignature(SignatureType.Process, "asyncrat", "AsyncRAT", ThreatLevel.Critical, "Remote access trojan");
            AddSignature(SignatureType.Process, "remcos", "Remcos RAT", ThreatLevel.Critical, "Remote access trojan");
            AddSignature(SignatureType.Process, "orcus", "Orcus RAT", ThreatLevel.Critical, "Remote access trojan");
            
            // Known keyloggers
            AddSignature(SignatureType.Process, "ardamax", "Ardamax Keylogger", ThreatLevel.Critical, "Keylogger - records keystrokes");
            AddSignature(SignatureType.Process, "spyrix", "Spyrix Keylogger", ThreatLevel.Critical, "Keylogger software");
            AddSignature(SignatureType.Process, "refog", "Refog Keylogger", ThreatLevel.High, "Monitoring/keylogger software");
            AddSignature(SignatureType.Process, "revealer", "Revealer Keylogger", ThreatLevel.Critical, "Keylogger software");
            
            // Known adware/PUPs (Potentially Unwanted Programs)
            AddSignature(SignatureType.Process, "conduit", "Conduit Toolbar", ThreatLevel.Medium, "Browser hijacker/adware");
            AddSignature(SignatureType.Process, "ask toolbar", "Ask Toolbar", ThreatLevel.Low, "Potentially unwanted toolbar");
            AddSignature(SignatureType.Process, "babylon", "Babylon Toolbar", ThreatLevel.Medium, "Browser hijacker");
            AddSignature(SignatureType.Process, "delta-homes", "Delta Homes", ThreatLevel.Medium, "Browser hijacker");
            AddSignature(SignatureType.Process, "mywebsearch", "MyWebSearch", ThreatLevel.Medium, "Adware/browser hijacker");
            AddSignature(SignatureType.Process, "sweetim", "SweetIM", ThreatLevel.Medium, "Adware");
            AddSignature(SignatureType.Process, "iminent", "Iminent Toolbar", ThreatLevel.Medium, "Adware toolbar");
            
            // Known spyware
            AddSignature(SignatureType.Process, "gator", "Gator/GAIN", ThreatLevel.High, "Known advertising spyware");
            AddSignature(SignatureType.Process, "180solutions", "180Solutions", ThreatLevel.High, "Adware/spyware");
            AddSignature(SignatureType.Process, "coolwebsearch", "CoolWebSearch", ThreatLevel.High, "Browser hijacker/spyware");
            AddSignature(SignatureType.Process, "hotbar", "Hotbar", ThreatLevel.Medium, "Adware");
            
            // Ransomware indicators
            AddSignature(SignatureType.File, "readme.txt", "Potential Ransomware Note", ThreatLevel.Low, "Check if this appeared suddenly with encryption", "@readme");
            AddSignature(SignatureType.File, "decrypt_instructions", "Ransomware Instructions", ThreatLevel.High, "Ransomware indicator");
            AddSignature(SignatureType.File, "your_files_are_encrypted", "Ransomware Note", ThreatLevel.Critical, "Ransomware detected");
            
            // Suspicious startup locations (only flag if unknown executables)
            AddSignature(SignatureType.Registry, @"Software\Microsoft\Windows\CurrentVersion\Run", "Startup Entry", ThreatLevel.Info, "Check for unknown entries", checkUnknown: true);
            
            await Task.CompletedTask;
        }

        private static void AddSignature(SignatureType type, string pattern, string name, ThreatLevel level, string description, string? pathPattern = null, bool checkUnknown = false)
        {
            _signatures.Add(new ThreatSignature
            {
                Type = type,
                Pattern = pattern.ToLowerInvariant(),
                Name = name,
                Level = level,
                Description = description,
                PathPattern = pathPattern,
                CheckUnknownOnly = checkUnknown
            });
        }

        private static void LoadBuiltInDefinitions()
        {
            Debug.WriteLine("[ThreatDB] Loading built-in definitions...");
            _ = UpdateKnownThreatsAsync(CancellationToken.None);
        }
    }

    public class ThreatSignature
    {
        public SignatureType Type { get; set; }
        public string Pattern { get; set; } = "";
        public string Name { get; set; } = "";
        public ThreatLevel Level { get; set; }
        public string Description { get; set; } = "";
        public string? PathPattern { get; set; }
        public bool CheckUnknownOnly { get; set; }
    }

    public enum SignatureType
    {
        Process,
        File,
        Registry,
        Network,
        Hash
    }

    public enum ThreatLevel
    {
        Info,
        Low,
        Medium,
        High,
        Critical
    }

    public class OnlineUpdateResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public int NewHashesAdded { get; set; }
        public int NewSignaturesAdded { get; set; }
        public int TotalDefinitions { get; set; }
        public int SourcesUpdated { get; set; }
    }
}
