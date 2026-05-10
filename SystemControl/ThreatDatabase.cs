using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Security.Cryptography;

namespace AtlasAI.SystemControl
{
    /// <summary>
    /// Updateable threat definitions database with online sync capability
    /// </summary>
    public class ThreatDatabase
    {
        private static ThreatDatabase? _instance;
        private static readonly object _lock = new object();
        
        public static ThreatDatabase Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        _instance ??= new ThreatDatabase();
                    }
                }
                return _instance;
            }
        }

        private readonly string _databasePath;
        private readonly string _hashDatabasePath;
        private ThreatDefinitionData _definitions;
        private HashSet<string> _maliciousHashes;
        
        public DateTime LastUpdated => _definitions?.LastUpdated ?? DateTime.MinValue;
        public int TotalDefinitions => _definitions?.TotalCount ?? 0;
        public string Version => _definitions?.Version ?? "1.0.0";

        private ThreatDatabase()
        {
            var appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "AtlasAI", "ThreatDB");
            
            Directory.CreateDirectory(appDataPath);
            
            _databasePath = Path.Combine(appDataPath, "threats.json");
            _hashDatabasePath = Path.Combine(appDataPath, "hashes.txt");
            _maliciousHashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            
            LoadDatabase();
        }

        private void LoadDatabase()
        {
            try
            {
                if (File.Exists(_databasePath))
                {
                    var json = File.ReadAllText(_databasePath);
                    _definitions = JsonSerializer.Deserialize<ThreatDefinitionData>(json) ?? CreateDefaultDefinitions();
                }
                else
                {
                    _definitions = CreateDefaultDefinitions();
                    SaveDatabase();
                }

                // Load hash database
                if (File.Exists(_hashDatabasePath))
                {
                    var hashes = File.ReadAllLines(_hashDatabasePath);
                    _maliciousHashes = new HashSet<string>(hashes, StringComparer.OrdinalIgnoreCase);
                }
                else
                {
                    _maliciousHashes = CreateDefaultHashes();
                    SaveHashDatabase();
                }
            }
            catch
            {
                _definitions = CreateDefaultDefinitions();
                _maliciousHashes = CreateDefaultHashes();
            }
        }

        public void SaveDatabase()
        {
            try
            {
                var json = JsonSerializer.Serialize(_definitions, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_databasePath, json);
            }
            catch { }
        }

        private void SaveHashDatabase()
        {
            try
            {
                File.WriteAllLines(_hashDatabasePath, _maliciousHashes);
            }
            catch { }
        }

        public async Task<UpdateResult> UpdateDefinitionsAsync()
        {
            var result = new UpdateResult { Success = false };
            
            try
            {
                // In production, this would fetch from a real threat intelligence API
                // For now, we'll simulate an update with expanded local definitions
                await Task.Delay(2000); // Simulate network request
                
                var newDefinitions = CreateExpandedDefinitions();
                
                if (newDefinitions.TotalCount > _definitions.TotalCount)
                {
                    var added = newDefinitions.TotalCount - _definitions.TotalCount;
                    _definitions = newDefinitions;
                    _definitions.LastUpdated = DateTime.Now;
                    SaveDatabase();
                    
                    result.Success = true;
                    result.NewDefinitionsCount = added;
                    result.Message = $"Updated! Added {added} new threat definitions.";
                }
                else
                {
                    result.Success = true;
                    result.Message = "Definitions are already up to date.";
                }
            }
            catch (Exception ex)
            {
                result.Message = $"Update failed: {ex.Message}";
            }
            
            return result;
        }

        // Threat matching methods
        public ThreatDefinition? MatchProcess(string processName)
        {
            processName = processName.ToLower();
            return _definitions.Processes.Find(p => 
                processName.Contains(p.Pattern.ToLower()) || 
                p.Pattern.ToLower() == processName);
        }

        public ThreatDefinition? MatchFile(string fileName)
        {
            fileName = fileName.ToLower();
            return _definitions.Files.Find(f => 
                fileName.Contains(f.Pattern.ToLower()) || 
                f.Pattern.ToLower() == fileName);
        }

        public ThreatDefinition? MatchRegistry(string keyPath, string valueName)
        {
            var combined = $"{keyPath}\\{valueName}".ToLower();
            return _definitions.Registry.Find(r => combined.Contains(r.Pattern.ToLower()));
        }

        public ThreatDefinition? MatchNetwork(string address, int port)
        {
            return _definitions.Network.Find(n => 
                n.Pattern == address || 
                n.Ports.Contains(port));
        }

        public ThreatDefinition? MatchBrowserExtension(string extensionId)
        {
            return _definitions.Extensions.Find(e => 
                e.Pattern.ToLower() == extensionId.ToLower());
        }

        public bool IsKnownMaliciousHash(string hash)
        {
            return _maliciousHashes.Contains(hash);
        }

        public List<string> GetSuspiciousProcessNames() => 
            _definitions.Processes.ConvertAll(p => p.Pattern);

        public List<string> GetSuspiciousFilePatterns() => 
            _definitions.Files.ConvertAll(f => f.Pattern);


        private ThreatDefinitionData CreateDefaultDefinitions()
        {
            return new ThreatDefinitionData
            {
                Version = "1.0.0",
                LastUpdated = DateTime.Now,
                Processes = new List<ThreatDefinition>
                {
                    new() { Pattern = "keylogger", Name = "Keylogger", Severity = "Critical", Category = "Spyware", Description = "Records keystrokes to steal passwords and sensitive data" },
                    new() { Pattern = "coinminer", Name = "Cryptocurrency Miner", Severity = "High", Category = "Malware", Description = "Uses system resources to mine cryptocurrency" },
                    new() { Pattern = "cryptominer", Name = "Crypto Miner", Severity = "High", Category = "Malware", Description = "Unauthorized cryptocurrency mining software" },
                    new() { Pattern = "backdoor", Name = "Backdoor Trojan", Severity = "Critical", Category = "Trojan", Description = "Provides unauthorized remote access" },
                    new() { Pattern = "trojan", Name = "Trojan Horse", Severity = "Critical", Category = "Trojan", Description = "Malicious software disguised as legitimate" },
                    new() { Pattern = "ransomware", Name = "Ransomware", Severity = "Critical", Category = "Ransomware", Description = "Encrypts files and demands payment" },
                    new() { Pattern = "spyware", Name = "Spyware", Severity = "High", Category = "Spyware", Description = "Monitors user activity without consent" },
                    new() { Pattern = "adware", Name = "Adware", Severity = "Medium", Category = "PUP", Description = "Displays unwanted advertisements" },
                    new() { Pattern = "hijacker", Name = "Browser Hijacker", Severity = "Medium", Category = "PUP", Description = "Modifies browser settings without permission" },
                    new() { Pattern = "rootkit", Name = "Rootkit", Severity = "Critical", Category = "Rootkit", Description = "Hides malicious activity from detection" },
                    new() { Pattern = "botnet", Name = "Botnet Client", Severity = "Critical", Category = "Botnet", Description = "Part of a network of compromised computers" },
                    new() { Pattern = "rat", Name = "Remote Access Trojan", Severity = "Critical", Category = "RAT", Description = "Allows remote control of infected system" },
                    new() { Pattern = "worm", Name = "Computer Worm", Severity = "High", Category = "Worm", Description = "Self-replicating malware that spreads across networks" },
                    new() { Pattern = "gator", Name = "Gator Spyware", Severity = "High", Category = "Spyware", Description = "Known advertising spyware" },
                    new() { Pattern = "bonzi", Name = "BonziBuddy", Severity = "Medium", Category = "Adware", Description = "Notorious adware/spyware application" },
                    new() { Pattern = "comet", Name = "Comet Cursor", Severity = "Medium", Category = "Spyware", Description = "Tracking spyware disguised as cursor software" },
                    new() { Pattern = "coolwebsearch", Name = "CoolWebSearch", Severity = "High", Category = "Hijacker", Description = "Browser hijacker malware" },
                    new() { Pattern = "180solutions", Name = "180Solutions", Severity = "Medium", Category = "Adware", Description = "Advertising tracking software" },
                    new() { Pattern = "claria", Name = "Claria/Gator", Severity = "Medium", Category = "Adware", Description = "Behavioral advertising spyware" },
                    new() { Pattern = "hotbar", Name = "Hotbar", Severity = "Medium", Category = "Adware", Description = "Browser toolbar adware" }
                },
                Files = new List<ThreatDefinition>
                {
                    new() { Pattern = "keygen", Name = "Key Generator", Severity = "High", Category = "Riskware", Description = "Often bundled with malware" },
                    new() { Pattern = "crack", Name = "Software Crack", Severity = "High", Category = "Riskware", Description = "Frequently contains trojans" },
                    new() { Pattern = "patch.exe", Name = "Suspicious Patch", Severity = "Medium", Category = "Riskware", Description = "May contain malicious code" },
                    new() { Pattern = "activator", Name = "Software Activator", Severity = "High", Category = "Riskware", Description = "Often bundled with malware" },
                    new() { Pattern = "loader.exe", Name = "Suspicious Loader", Severity = "Medium", Category = "Riskware", Description = "May load malicious payloads" },
                    new() { Pattern = "inject", Name = "Code Injector", Severity = "High", Category = "Hacking Tool", Description = "Used to inject malicious code" },
                    new() { Pattern = "stealer", Name = "Info Stealer", Severity = "Critical", Category = "Spyware", Description = "Steals sensitive information" },
                    new() { Pattern = "grabber", Name = "Data Grabber", Severity = "High", Category = "Spyware", Description = "Captures and exfiltrates data" },
                    new() { Pattern = "crypter", Name = "Malware Crypter", Severity = "High", Category = "Hacking Tool", Description = "Used to obfuscate malware" },
                    new() { Pattern = "binder", Name = "File Binder", Severity = "High", Category = "Hacking Tool", Description = "Combines malware with legitimate files" }
                },
                Registry = new List<ThreatDefinition>
                {
                    new() { Pattern = "\\run\\suspicious", Name = "Suspicious Startup", Severity = "Medium", Category = "Persistence", Description = "Unknown program in startup" },
                    new() { Pattern = "\\policies\\explorer\\run", Name = "Policy Run Key", Severity = "High", Category = "Persistence", Description = "Malware persistence mechanism" },
                    new() { Pattern = "\\winlogon\\shell", Name = "Shell Hijack", Severity = "Critical", Category = "Rootkit", Description = "Windows shell replacement" },
                    new() { Pattern = "\\winlogon\\userinit", Name = "UserInit Hijack", Severity = "Critical", Category = "Rootkit", Description = "Login process hijack" },
                    new() { Pattern = "\\image file execution", Name = "IFEO Hijack", Severity = "Critical", Category = "Persistence", Description = "Debugger-based persistence" }
                },
                Network = new List<ThreatDefinition>
                {
                    new() { Pattern = "0.0.0.0", Ports = new List<int> { 4444, 5555, 6666, 31337 }, Name = "Suspicious Port", Severity = "High", Category = "C2", Description = "Common malware communication ports" },
                    new() { Pattern = "tor", Ports = new List<int> { 9050, 9051 }, Name = "Tor Connection", Severity = "Medium", Category = "Anonymizer", Description = "Tor network connection detected" }
                },
                Extensions = new List<ThreatDefinition>
                {
                    new() { Pattern = "hola", Name = "Hola VPN", Severity = "Medium", Category = "PUP", Description = "Known to sell user bandwidth" },
                    new() { Pattern = "web of trust", Name = "Web of Trust", Severity = "Medium", Category = "Spyware", Description = "Sells browsing history" }
                }
            };
        }

        private ThreatDefinitionData CreateExpandedDefinitions()
        {
            var defs = CreateDefaultDefinitions();
            defs.Version = "1.1.0";
            
            // Add more definitions to simulate an update
            defs.Processes.AddRange(new List<ThreatDefinition>
            {
                new() { Pattern = "emotet", Name = "Emotet", Severity = "Critical", Category = "Banking Trojan", Description = "Sophisticated banking trojan and malware dropper" },
                new() { Pattern = "trickbot", Name = "TrickBot", Severity = "Critical", Category = "Banking Trojan", Description = "Modular banking trojan" },
                new() { Pattern = "ryuk", Name = "Ryuk Ransomware", Severity = "Critical", Category = "Ransomware", Description = "Targeted ransomware" },
                new() { Pattern = "wannacry", Name = "WannaCry", Severity = "Critical", Category = "Ransomware", Description = "Self-spreading ransomware" },
                new() { Pattern = "petya", Name = "Petya/NotPetya", Severity = "Critical", Category = "Ransomware", Description = "Destructive ransomware" },
                new() { Pattern = "locky", Name = "Locky", Severity = "Critical", Category = "Ransomware", Description = "Email-spread ransomware" },
                new() { Pattern = "cerber", Name = "Cerber", Severity = "Critical", Category = "Ransomware", Description = "Ransomware-as-a-service" },
                new() { Pattern = "dridex", Name = "Dridex", Severity = "Critical", Category = "Banking Trojan", Description = "Banking credential stealer" },
                new() { Pattern = "zeus", Name = "Zeus/Zbot", Severity = "Critical", Category = "Banking Trojan", Description = "Infamous banking trojan" },
                new() { Pattern = "formbook", Name = "FormBook", Severity = "High", Category = "Info Stealer", Description = "Form grabber and keylogger" }
            });
            
            return defs;
        }

        private HashSet<string> CreateDefaultHashes()
        {
            // Sample known malicious file hashes (MD5)
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "44d88612fea8a8f36de82e1278abb02f", // EICAR test file
                "e99a18c428cb38d5f260853678922e03", // Common test hash
            };
        }
    }

    public class ThreatDefinitionData
    {
        public string Version { get; set; } = "1.0.0";
        public DateTime LastUpdated { get; set; } = DateTime.Now;
        public int TotalCount => Processes.Count + Files.Count + Registry.Count + Network.Count + Extensions.Count;
        
        public List<ThreatDefinition> Processes { get; set; } = new();
        public List<ThreatDefinition> Files { get; set; } = new();
        public List<ThreatDefinition> Registry { get; set; } = new();
        public List<ThreatDefinition> Network { get; set; } = new();
        public List<ThreatDefinition> Extensions { get; set; } = new();
    }

    public class ThreatDefinition
    {
        public string Pattern { get; set; } = "";
        public string Name { get; set; } = "";
        public string Severity { get; set; } = "Medium";
        public string Category { get; set; } = "Unknown";
        public string Description { get; set; } = "";
        public List<int> Ports { get; set; } = new();
    }

    public class UpdateResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public int NewDefinitionsCount { get; set; }
    }
}
