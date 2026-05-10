#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace AtlasAI.Memory
{
    /// <summary>
    /// Repository Memory - stores non-sensitive project metadata to help Atlas improve over time.
    /// Stores: preferred commands, project type heuristics, common entry points.
    /// Does NOT store: source code, secrets, sensitive data.
    /// </summary>
    public class RepositoryMemory
    {
        private static readonly string MemoryDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AtlasAI", "repository_memory");

        private static RepositoryMemory? _instance;
        public static RepositoryMemory Instance => _instance ??= new RepositoryMemory();

        private readonly Dictionary<string, WorkspaceProfile> _loadedProfiles = new();

        private RepositoryMemory()
        {
            if (!Directory.Exists(MemoryDir))
                Directory.CreateDirectory(MemoryDir);
        }

        #region Workspace Profile Management

        /// <summary>
        /// Get or create a workspace profile for the given path.
        /// </summary>
        public async Task<WorkspaceProfile> GetProfileAsync(string workspacePath)
        {
            var id = GetWorkspaceId(workspacePath);
            
            if (_loadedProfiles.TryGetValue(id, out var cached))
                return cached;

            var profile = await LoadProfileAsync(id) ?? new WorkspaceProfile
            {
                WorkspaceId = id,
                WorkspaceName = Path.GetFileName(workspacePath),
                CreatedAt = DateTime.UtcNow
            };

            _loadedProfiles[id] = profile;
            return profile;
        }

        /// <summary>
        /// Save a workspace profile.
        /// </summary>
        public async Task SaveProfileAsync(WorkspaceProfile profile)
        {
            var filePath = GetProfilePath(profile.WorkspaceId);
            profile.LastAccessedAt = DateTime.UtcNow;

            var json = JsonSerializer.Serialize(profile, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(filePath, json);

            _loadedProfiles[profile.WorkspaceId] = profile;
            System.Diagnostics.Debug.WriteLine($"[RepoMemory] Saved profile for {profile.WorkspaceName}");
        }

        /// <summary>
        /// Reset/delete workspace memory.
        /// </summary>
        public async Task ResetWorkspaceMemoryAsync(string workspacePath)
        {
            var id = GetWorkspaceId(workspacePath);
            var filePath = GetProfilePath(id);

            if (File.Exists(filePath))
            {
                await Task.Run(() => File.Delete(filePath));
            }

            _loadedProfiles.Remove(id);
            System.Diagnostics.Debug.WriteLine($"[RepoMemory] Reset memory for workspace: {workspacePath}");
        }

        #endregion

        #region Command Learning

        /// <summary>
        /// Learn a preferred command (build, test, run, etc.)
        /// </summary>
        public async Task LearnCommandAsync(string workspacePath, string commandType, string command)
        {
            var profile = await GetProfileAsync(workspacePath);
            profile.PreferredCommands[commandType] = command;
            profile.CommandUsageCount.TryGetValue(commandType, out var count);
            profile.CommandUsageCount[commandType] = count + 1;
            await SaveProfileAsync(profile);
        }

        /// <summary>
        /// Get the preferred command for a type (build, test, run, etc.)
        /// </summary>
        public async Task<string?> GetPreferredCommandAsync(string workspacePath, string commandType)
        {
            var profile = await GetProfileAsync(workspacePath);
            return profile.PreferredCommands.TryGetValue(commandType, out var cmd) ? cmd : null;
        }

        /// <summary>
        /// Get all preferred commands for a workspace.
        /// </summary>
        public async Task<Dictionary<string, string>> GetAllCommandsAsync(string workspacePath)
        {
            var profile = await GetProfileAsync(workspacePath);
            return new Dictionary<string, string>(profile.PreferredCommands);
        }

        #endregion

        #region Project Type Detection

        /// <summary>
        /// Set the detected project type.
        /// </summary>
        public async Task SetProjectTypeAsync(string workspacePath, ProjectType projectType, double confidence = 1.0)
        {
            var profile = await GetProfileAsync(workspacePath);
            profile.ProjectType = projectType;
            profile.ProjectTypeConfidence = confidence;
            await SaveProfileAsync(profile);
        }

        /// <summary>
        /// Get the project type for a workspace.
        /// </summary>
        public async Task<(ProjectType Type, double Confidence)> GetProjectTypeAsync(string workspacePath)
        {
            var profile = await GetProfileAsync(workspacePath);
            return (profile.ProjectType, profile.ProjectTypeConfidence);
        }

        /// <summary>
        /// Auto-detect project type from workspace files.
        /// </summary>
        public async Task<ProjectType> DetectProjectTypeAsync(string workspacePath)
        {
            var profile = await GetProfileAsync(workspacePath);
            
            // If already detected with high confidence, return cached
            if (profile.ProjectTypeConfidence > 0.8)
                return profile.ProjectType;

            var detected = await Task.Run(() => DetectProjectTypeFromFiles(workspacePath));
            await SetProjectTypeAsync(workspacePath, detected, 0.9);
            return detected;
        }

        private ProjectType DetectProjectTypeFromFiles(string workspacePath)
        {
            if (!Directory.Exists(workspacePath))
                return ProjectType.Unknown;

            var files = Directory.GetFiles(workspacePath, "*", SearchOption.TopDirectoryOnly);
            var fileNames = files.Select(Path.GetFileName).ToHashSet(StringComparer.OrdinalIgnoreCase);

            // Check for project files
            if (fileNames.Any(f => f?.EndsWith(".csproj") == true || f?.EndsWith(".sln") == true))
            {
                // Check for WPF indicators
                if (Directory.GetFiles(workspacePath, "*.xaml", SearchOption.AllDirectories).Any())
                    return ProjectType.WPF;
                return ProjectType.DotNet;
            }

            if (fileNames.Contains("package.json"))
            {
                // Check for React/Vue/Angular
                var packagePath = Path.Combine(workspacePath, "package.json");
                if (File.Exists(packagePath))
                {
                    var content = File.ReadAllText(packagePath);
                    if (content.Contains("\"react\"")) return ProjectType.React;
                    if (content.Contains("\"vue\"")) return ProjectType.Vue;
                    if (content.Contains("\"@angular/core\"")) return ProjectType.Angular;
                    if (content.Contains("\"next\"")) return ProjectType.NextJS;
                }
                return ProjectType.NodeJS;
            }

            if (fileNames.Contains("requirements.txt") || fileNames.Contains("setup.py") || fileNames.Contains("pyproject.toml"))
            {
                if (fileNames.Contains("manage.py")) return ProjectType.Django;
                if (Directory.GetFiles(workspacePath, "*.ipynb", SearchOption.AllDirectories).Any())
                    return ProjectType.JupyterNotebook;
                return ProjectType.Python;
            }

            if (fileNames.Contains("Cargo.toml")) return ProjectType.Rust;
            if (fileNames.Contains("go.mod")) return ProjectType.Go;
            if (fileNames.Contains("pom.xml") || fileNames.Contains("build.gradle")) return ProjectType.Java;

            return ProjectType.Unknown;
        }

        #endregion

        #region Entry Points

        /// <summary>
        /// Add a common entry point file.
        /// </summary>
        public async Task AddEntryPointAsync(string workspacePath, string relativePath, string description = "")
        {
            var profile = await GetProfileAsync(workspacePath);
            
            if (!profile.EntryPoints.Any(e => e.RelativePath == relativePath))
            {
                profile.EntryPoints.Add(new EntryPoint
                {
                    RelativePath = relativePath,
                    Description = description,
                    AccessCount = 1
                });
                await SaveProfileAsync(profile);
            }
        }

        /// <summary>
        /// Increment access count for an entry point.
        /// </summary>
        public async Task TrackEntryPointAccessAsync(string workspacePath, string relativePath)
        {
            var profile = await GetProfileAsync(workspacePath);
            var entry = profile.EntryPoints.FirstOrDefault(e => e.RelativePath == relativePath);
            
            if (entry != null)
            {
                entry.AccessCount++;
                entry.LastAccessedAt = DateTime.UtcNow;
                await SaveProfileAsync(profile);
            }
        }

        /// <summary>
        /// Get common entry points, sorted by access frequency.
        /// </summary>
        public async Task<List<EntryPoint>> GetEntryPointsAsync(string workspacePath, int limit = 10)
        {
            var profile = await GetProfileAsync(workspacePath);
            return profile.EntryPoints
                .OrderByDescending(e => e.AccessCount)
                .Take(limit)
                .ToList();
        }

        #endregion

        #region Context Building

        /// <summary>
        /// Build context string for AI prompts.
        /// </summary>
        public async Task<string> BuildWorkspaceContextAsync(string workspacePath)
        {
            var profile = await GetProfileAsync(workspacePath);
            var sb = new StringBuilder();

            sb.AppendLine($"## Workspace: {profile.WorkspaceName}");
            
            if (profile.ProjectType != ProjectType.Unknown)
            {
                sb.AppendLine($"Project Type: {profile.ProjectType}");
            }

            if (profile.PreferredCommands.Count > 0)
            {
                sb.AppendLine("\nPreferred Commands:");
                foreach (var (type, cmd) in profile.PreferredCommands)
                {
                    sb.AppendLine($"- {type}: `{cmd}`");
                }
            }

            if (profile.EntryPoints.Count > 0)
            {
                sb.AppendLine("\nKey Files:");
                foreach (var entry in profile.EntryPoints.OrderByDescending(e => e.AccessCount).Take(5))
                {
                    var desc = string.IsNullOrEmpty(entry.Description) ? "" : $" - {entry.Description}";
                    sb.AppendLine($"- {entry.RelativePath}{desc}");
                }
            }

            return sb.ToString();
        }

        #endregion

        #region Helpers

        private string GetWorkspaceId(string workspacePath)
        {
            // Create a stable hash of the workspace path
            using var sha = SHA256.Create();
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(workspacePath.ToLowerInvariant()));
            return Convert.ToHexString(bytes)[..16];
        }

        private string GetProfilePath(string workspaceId)
        {
            return Path.Combine(MemoryDir, $"{workspaceId}.json");
        }

        private async Task<WorkspaceProfile?> LoadProfileAsync(string workspaceId)
        {
            var filePath = GetProfilePath(workspaceId);
            if (!File.Exists(filePath))
                return null;

            try
            {
                var json = await File.ReadAllTextAsync(filePath);
                return JsonSerializer.Deserialize<WorkspaceProfile>(json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RepoMemory] Error loading profile: {ex.Message}");
                return null;
            }
        }

        #endregion
    }

    #region Models

    public class WorkspaceProfile
    {
        public string WorkspaceId { get; set; } = "";
        public string WorkspaceName { get; set; } = "";
        public ProjectType ProjectType { get; set; } = ProjectType.Unknown;
        public double ProjectTypeConfidence { get; set; } = 0;
        public Dictionary<string, string> PreferredCommands { get; set; } = new();
        public Dictionary<string, int> CommandUsageCount { get; set; } = new();
        public List<EntryPoint> EntryPoints { get; set; } = new();
        public DateTime CreatedAt { get; set; }
        public DateTime LastAccessedAt { get; set; }
    }

    public class EntryPoint
    {
        public string RelativePath { get; set; } = "";
        public string Description { get; set; } = "";
        public int AccessCount { get; set; } = 0;
        public DateTime LastAccessedAt { get; set; }
    }

    public enum ProjectType
    {
        Unknown,
        // .NET
        DotNet,
        WPF,
        MAUI,
        Blazor,
        AspNetCore,
        // JavaScript/TypeScript
        NodeJS,
        React,
        Vue,
        Angular,
        NextJS,
        // Python
        Python,
        Django,
        Flask,
        FastAPI,
        JupyterNotebook,
        // Other
        Rust,
        Go,
        Java,
        Kotlin,
        Swift,
        Ruby
    }

    #endregion
}
