using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AtlasAI.Coding.Services
{
    /// <summary>
    /// Verification profile settings - configurable per-project verification commands.
    /// </summary>
    public class VerificationProfile
    {
        private static readonly string SettingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AtlasAI", "verification_profile.json");
        
        [JsonPropertyName("buildCommand")]
        public string BuildCommand { get; set; } = "dotnet build";
        
        [JsonPropertyName("testCommand")]
        public string TestCommand { get; set; } = "dotnet test --no-build";
        
        [JsonPropertyName("lintCommand")]
        public string LintCommand { get; set; } = "dotnet format --verify-no-changes";
        
        [JsonPropertyName("runAfterEachStage")]
        public bool RunAfterEachStage { get; set; } = false;
        
        [JsonPropertyName("runAtEnd")]
        public bool RunAtEnd { get; set; } = true;
        
        [JsonPropertyName("enableBuild")]
        public bool EnableBuild { get; set; } = true;
        
        [JsonPropertyName("enableTests")]
        public bool EnableTests { get; set; } = false;
        
        [JsonPropertyName("enableLint")]
        public bool EnableLint { get; set; } = false;
        
        [JsonPropertyName("autoRepairEnabled")]
        public bool AutoRepairEnabled { get; set; } = true;
        
        [JsonPropertyName("timeoutSeconds")]
        public int TimeoutSeconds { get; set; } = 120;
        
        [JsonPropertyName("maxErrorLines")]
        public int MaxErrorLines { get; set; } = 30;
        
        public static VerificationProfile Instance { get; private set; } = new();
        
        static VerificationProfile()
        {
            Load();
        }
        
        public static void Load()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    var json = File.ReadAllText(SettingsPath);
                    Instance = JsonSerializer.Deserialize<VerificationProfile>(json) ?? new();
                }
            }
            catch
            {
                Instance = new VerificationProfile();
            }
        }
        
        public void Save()
        {
            try
            {
                var dir = Path.GetDirectoryName(SettingsPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                
                var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SettingsPath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[VerificationProfile] Save error: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Auto-detect commands based on project type.
        /// </summary>
        public void AutoDetect(string projectPath)
        {
            if (string.IsNullOrEmpty(projectPath) || !Directory.Exists(projectPath))
                return;
            
            // .NET project
            if (Directory.GetFiles(projectPath, "*.csproj").Length > 0 ||
                Directory.GetFiles(projectPath, "*.sln").Length > 0)
            {
                BuildCommand = "dotnet build";
                TestCommand = "dotnet test --no-build";
                LintCommand = "dotnet format --verify-no-changes";
                return;
            }
            
            // Node.js project
            if (File.Exists(Path.Combine(projectPath, "package.json")))
            {
                BuildCommand = "npm run build";
                TestCommand = "npm test";
                LintCommand = "npm run lint";
                return;
            }
            
            // Python project
            if (File.Exists(Path.Combine(projectPath, "pyproject.toml")) ||
                File.Exists(Path.Combine(projectPath, "setup.py")))
            {
                BuildCommand = "python -m py_compile .";
                TestCommand = "pytest";
                LintCommand = "flake8 .";
                return;
            }
            
            // Rust project
            if (File.Exists(Path.Combine(projectPath, "Cargo.toml")))
            {
                BuildCommand = "cargo build";
                TestCommand = "cargo test";
                LintCommand = "cargo clippy";
                return;
            }
            
            // Go project
            if (File.Exists(Path.Combine(projectPath, "go.mod")))
            {
                BuildCommand = "go build ./...";
                TestCommand = "go test ./...";
                LintCommand = "golint ./...";
            }
        }
    }
}
