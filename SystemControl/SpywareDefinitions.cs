using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace AtlasAI.SystemControl
{
    // Enums and supporting classes first
    public enum ThreatType
    {
        Spyware,
        Adware,
        Keylogger,
        BrowserHijacker,
        Trojan,
        RAT, // Remote Access Trojan
        CryptoMiner,
        Rootkit,
        Worm,
        Virus,
        Ransomware,
        Scareware,
        PUP, // Potentially Unwanted Program
        SuspiciousProcess,
        StartupEntry,
        SuspiciousFile,
        BrowserExtension,
        NetworkConnection,
        RegistryEntry,
        KnownSpyware,
        SecurityIssue
    }

    public enum ThreatSeverity
    {
        Low,
        Medium,
        High,
        Critical
    }

    public class SpywareDefinition
    {
        public string Name { get; set; } = "";
        public ThreatType Type { get; set; }
        public ThreatSeverity Severity { get; set; }
        public string Description { get; set; } = "";
        public string[] ProcessNames { get; set; } = Array.Empty<string>();
        public string[] FileNames { get; set; } = Array.Empty<string>();
        public string[] RegistryKeys { get; set; } = Array.Empty<string>();
        public string Behavior { get; set; } = "";
        public DateTime DateAdded { get; set; } = DateTime.Now;
        public string[] FileHashes { get; set; } = Array.Empty<string>();
        public string[] NetworkSignatures { get; set; } = Array.Empty<string>();
    }

    public class RegistrySignature
    {
        public string KeyPath { get; set; } = "";
        public string ValueName { get; set; } = "";
        public string SuspiciousPattern { get; set; } = "";
        public string Description { get; set; } = "";
    }

    public class SpywareScanResult
    {
        public DateTime ScanStartTime { get; set; }
        public DateTime ScanEndTime { get; set; }
        public TimeSpan ScanDuration { get; set; }
        public List<SpywareThreat> Threats { get; set; } = new();
        public string ScanError { get; set; } = "";
        
        public int TotalThreats => Threats.Count;
        public int HighSeverityThreats => Threats.Count(t => t.Severity == ThreatSeverity.High);
        public int MediumSeverityThreats => Threats.Count(t => t.Severity == ThreatSeverity.Medium);
        public int LowSeverityThreats => Threats.Count(t => t.Severity == ThreatSeverity.Low);
    }

    public class SpywareThreat
    {
        public ThreatType Type { get; set; }
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public string Location { get; set; } = "";
        public string Details { get; set; } = "";
        public ThreatSeverity Severity { get; set; }
        public DateTime DetectedAt { get; set; }
        public int ProcessId { get; set; }
        public long FileSize { get; set; }
        public bool CanQuarantine { get; set; }
    }

    public class NetworkConnection
    {
        public string Protocol { get; set; } = "";
        public string LocalAddress { get; set; } = "";
        public string LocalPort { get; set; } = "";
        public string RemoteAddress { get; set; } = "";
        public string RemotePort { get; set; } = "";
        public string State { get; set; } = "";
    }

    // Main SpywareDefinitions class
    public class SpywareDefinitions
    {
        private static SpywareDefinitions? _instance;
        private readonly Dictionary<string, SpywareDefinition> _definitions;
        private readonly List<string> _suspiciousFileHashes;
        private readonly List<string> _maliciousUrls;
        private readonly List<RegistrySignature> _registrySignatures;
        private DateTime _lastUpdated;

        public static SpywareDefinitions Instance => _instance ??= new SpywareDefinitions();

        private SpywareDefinitions()
        {
            _definitions = new Dictionary<string, SpywareDefinition>();
            _suspiciousFileHashes = new List<string>();
            _maliciousUrls = new List<string>();
            _registrySignatures = new List<RegistrySignature>();
            LoadDefinitions();
        }

        public void LoadDefinitions()
        {
            LoadKnownSpywareDefinitions();
            LoadFileHashes();
            LoadMaliciousUrls();
            LoadRegistrySignatures();
            _lastUpdated = DateTime.Now;
        }

        private void LoadKnownSpywareDefinitions()
        {
            var definitions = new[]
            {
                // Classic Spyware
                new SpywareDefinition
                {
                    Name = "CoolWebSearch",
                    Type = ThreatType.BrowserHijacker,
                    Severity = ThreatSeverity.High,
                    Description = "Browser hijacker that redirects searches and displays unwanted ads",
                    ProcessNames = new[] { "cwshredder", "cws", "coolwebsearch" },
                    FileNames = new[] { "cwshredder.exe", "cws.dll", "coolwwwsearch.dll" },
                    RegistryKeys = new[] { @"HKLM\SOFTWARE\CoolWebSearch", @"HKCU\Software\CoolWebSearch" },
                    Behavior = "Modifies browser homepage, redirects searches, displays pop-ups"
                },
                
                new SpywareDefinition
                {
                    Name = "Gator (GAIN)",
                    Type = ThreatType.Adware,
                    Severity = ThreatSeverity.Medium,
                    Description = "Adware that tracks browsing habits and displays targeted advertisements",
                    ProcessNames = new[] { "gator", "gain", "gmt" },
                    FileNames = new[] { "gator.exe", "gain.exe", "gmt.exe", "cmesys.exe" },
                    RegistryKeys = new[] { @"HKLM\SOFTWARE\Gator.com", @"HKLM\SOFTWARE\GAIN" },
                    Behavior = "Tracks web browsing, displays pop-up ads, collects personal information"
                },

                new SpywareDefinition
                {
                    Name = "Bonzi Buddy",
                    Type = ThreatType.Spyware,
                    Severity = ThreatSeverity.High,
                    Description = "Malicious virtual assistant that spies on user activities",
                    ProcessNames = new[] { "bonzibuddy", "bonzi", "buddy" },
                    FileNames = new[] { "bonzibuddy.exe", "bonzi.exe", "buddy.exe" },
                    RegistryKeys = new[] { @"HKLM\SOFTWARE\BonziBuddy", @"HKCU\Software\BonziBuddy" },
                    Behavior = "Records keystrokes, monitors browsing, transmits personal data"
                },

                new SpywareDefinition
                {
                    Name = "WeatherBug",
                    Type = ThreatType.Spyware,
                    Severity = ThreatSeverity.Medium,
                    Description = "Weather application with hidden tracking capabilities",
                    ProcessNames = new[] { "weatherbug", "weather" },
                    FileNames = new[] { "weatherbug.exe", "weather.exe", "wbug.exe" },
                    RegistryKeys = new[] { @"HKLM\SOFTWARE\WeatherBug" },
                    Behavior = "Tracks location data, monitors system usage, displays ads"
                },

                new SpywareDefinition
                {
                    Name = "Comet Cursor",
                    Type = ThreatType.Spyware,
                    Severity = ThreatSeverity.Low,
                    Description = "Cursor customization software with data collection features",
                    ProcessNames = new[] { "cometcursor", "comet" },
                    FileNames = new[] { "cometcursor.exe", "comet.exe", "cursor.exe" },
                    RegistryKeys = new[] { @"HKLM\SOFTWARE\Comet Systems" },
                    Behavior = "Collects browsing data, displays advertisements"
                },

                // Modern Threats
                new SpywareDefinition
                {
                    Name = "Keylogger Generic",
                    Type = ThreatType.Keylogger,
                    Severity = ThreatSeverity.Critical,
                    Description = "Generic keylogger that records all keyboard input",
                    ProcessNames = new[] { "keylogger", "keylog", "klog", "logger" },
                    FileNames = new[] { "keylogger.exe", "keylog.exe", "klog.exe", "logger.exe" },
                    RegistryKeys = new[] { @"HKLM\SOFTWARE\KeyLogger", @"HKCU\Software\Logger" },
                    Behavior = "Records all keystrokes including passwords and sensitive data"
                },

                new SpywareDefinition
                {
                    Name = "Browser Hijacker Generic",
                    Type = ThreatType.BrowserHijacker,
                    Severity = ThreatSeverity.High,
                    Description = "Generic browser hijacker that modifies browser settings",
                    ProcessNames = new[] { "hijacker", "redirect", "search" },
                    FileNames = new[] { "hijacker.exe", "redirect.exe", "search.exe" },
                    RegistryKeys = new[] { @"HKLM\SOFTWARE\Microsoft\Internet Explorer\Main" },
                    Behavior = "Changes homepage, redirects searches, installs unwanted toolbars"
                },

                new SpywareDefinition
                {
                    Name = "Trojan Downloader",
                    Type = ThreatType.Trojan,
                    Severity = ThreatSeverity.Critical,
                    Description = "Trojan that downloads and installs additional malware",
                    ProcessNames = new[] { "downloader", "dropper", "installer" },
                    FileNames = new[] { "downloader.exe", "dropper.exe", "installer.exe" },
                    RegistryKeys = new[] { @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Run" },
                    Behavior = "Downloads malware, creates backdoors, steals credentials"
                },

                new SpywareDefinition
                {
                    Name = "Cryptocurrency Miner",
                    Type = ThreatType.CryptoMiner,
                    Severity = ThreatSeverity.High,
                    Description = "Unauthorized cryptocurrency mining software",
                    ProcessNames = new[] { "miner", "coinminer", "cryptominer", "xmrig" },
                    FileNames = new[] { "miner.exe", "coinminer.exe", "cryptominer.exe", "xmrig.exe" },
                    RegistryKeys = new[] { @"HKLM\SOFTWARE\Miner" },
                    Behavior = "Uses system resources for cryptocurrency mining without consent"
                },

                new SpywareDefinition
                {
                    Name = "Remote Access Trojan",
                    Type = ThreatType.RAT,
                    Severity = ThreatSeverity.Critical,
                    Description = "Provides unauthorized remote access to the system",
                    ProcessNames = new[] { "rat", "remote", "backdoor", "teamviewer" },
                    FileNames = new[] { "rat.exe", "remote.exe", "backdoor.exe" },
                    RegistryKeys = new[] { @"HKLM\SOFTWARE\Remote" },
                    Behavior = "Allows remote control, steals files, captures screenshots"
                }
            };

            foreach (var def in definitions)
            {
                _definitions[def.Name] = def;
            }
        }

        private void LoadFileHashes()
        {
            // Known malicious file hashes (MD5, SHA1, SHA256)
            _suspiciousFileHashes.AddRange(new[]
            {
                // Example hashes (in real implementation, these would be actual malware hashes)
                "5d41402abc4b2a76b9719d911017c592", // MD5 example
                "aaf4c61ddcc5e8a2dabede0f3b482cd9aea9434d", // SHA1 example
                "2c26b46b68ffc68ff99b453c1d30413413422d706483bfa0f98a5e886266e7ae", // SHA256 example
                
                // Common spyware hashes
                "da39a3ee5e6b4b0d3255bfef95601890afd80709",
                "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
                "cf83e1357eefb8bdf1542850d66d8007d620e4050b5715dc83f4a921d36ce9ce"
            });
        }

        private void LoadMaliciousUrls()
        {
            _maliciousUrls.AddRange(new[]
            {
                "malware-download.com",
                "suspicious-site.net",
                "phishing-example.org",
                "fake-antivirus.com",
                "scam-download.net",
                "malicious-ads.com",
                "trojan-host.org",
                "spyware-central.net"
            });
        }

        private void LoadRegistrySignatures()
        {
            _registrySignatures.AddRange(new[]
            {
                new RegistrySignature
                {
                    KeyPath = @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Run",
                    ValueName = "SystemUpdate",
                    SuspiciousPattern = @".*\\temp\\.*\.exe",
                    Description = "Suspicious startup entry pointing to temp directory"
                },
                new RegistrySignature
                {
                    KeyPath = @"HKCU\Software\Microsoft\Internet Explorer\Main",
                    ValueName = "Start Page",
                    SuspiciousPattern = @"http://.*\.(tk|ml|ga|cf)/.*",
                    Description = "Browser homepage hijacked to suspicious domain"
                },
                new RegistrySignature
                {
                    KeyPath = @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Browser Helper Objects",
                    ValueName = "*",
                    SuspiciousPattern = @".*",
                    Description = "Potentially unwanted browser helper object"
                }
            });
        }

        // Query methods
        public SpywareDefinition? GetDefinitionByName(string name)
        {
            return _definitions.TryGetValue(name, out var definition) ? definition : null;
        }

        public SpywareDefinition? GetDefinitionByProcess(string processName)
        {
            return _definitions.Values.FirstOrDefault(d => 
                d.ProcessNames.Any(p => p.Equals(processName, StringComparison.OrdinalIgnoreCase)));
        }

        public SpywareDefinition? GetDefinitionByFile(string fileName)
        {
            return _definitions.Values.FirstOrDefault(d => 
                d.FileNames.Any(f => f.Equals(fileName, StringComparison.OrdinalIgnoreCase)));
        }

        public List<SpywareDefinition> GetDefinitionsByType(ThreatType type)
        {
            return _definitions.Values.Where(d => d.Type == type).ToList();
        }

        public bool IsKnownMaliciousHash(string hash)
        {
            return _suspiciousFileHashes.Contains(hash, StringComparer.OrdinalIgnoreCase);
        }

        public bool IsKnownMaliciousUrl(string url)
        {
            return _maliciousUrls.Any(malUrl => url.Contains(malUrl, StringComparison.OrdinalIgnoreCase));
        }

        public List<RegistrySignature> GetRegistrySignatures()
        {
            return _registrySignatures.ToList();
        }

        public List<SpywareDefinition> GetAllDefinitions()
        {
            return _definitions.Values.ToList();
        }

        public int GetDefinitionCount()
        {
            return _definitions.Count;
        }

        public DateTime GetLastUpdated()
        {
            return _lastUpdated;
        }

        // Update methods
        public void AddDefinition(SpywareDefinition definition)
        {
            _definitions[definition.Name] = definition;
        }

        public void RemoveDefinition(string name)
        {
            _definitions.Remove(name);
        }

        public void UpdateDefinitionsFromFile(string filePath)
        {
            if (File.Exists(filePath))
            {
                try
                {
                    var json = File.ReadAllText(filePath);
                    var definitions = JsonSerializer.Deserialize<SpywareDefinition[]>(json);
                    if (definitions != null)
                    {
                        foreach (var def in definitions)
                        {
                            _definitions[def.Name] = def;
                        }
                        _lastUpdated = DateTime.Now;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to load definitions from file: {ex.Message}");
                }
            }
        }

        public void SaveDefinitionsToFile(string filePath)
        {
            try
            {
                var json = JsonSerializer.Serialize(_definitions.Values.ToArray(), new JsonSerializerOptions 
                { 
                    WriteIndented = true 
                });
                File.WriteAllText(filePath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save definitions to file: {ex.Message}");
            }
        }
    }

}