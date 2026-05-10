using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using AtlasAI.AI;
using AtlasAI.Integrations;
using AtlasAI.Tools;

namespace AtlasAI.Agent
{
    /// <summary>
    /// Tool definitions that the AI can call - similar to how Kiro works
    /// </summary>
    public static class AgentTools
    {
        // Tool definitions for the AI system prompt
        public static string GetToolDefinitions() => @"
You have access to the following tools to help complete tasks. Use them by responding with a JSON tool call.

## FILE TOOLS:

### read_file
Read the contents of a file.
Parameters: { ""path"": ""relative/path/to/file"" }

### write_file  
Create or overwrite a file with content.
Parameters: { ""path"": ""relative/path/to/file"", ""content"": ""file contents"" }

### append_file
Append content to an existing file.
Parameters: { ""path"": ""relative/path/to/file"", ""content"": ""content to append"" }

### list_directory
List files and folders in a directory.
Parameters: { ""path"": ""relative/path/to/dir"", ""recursive"": false }

### search_files
Search for files matching a pattern.
Parameters: { ""pattern"": ""*.cs"", ""path"": ""."" }

### search_content
Search for text content in files (like grep).
Parameters: { ""query"": ""search term"", ""path"": ""."", ""file_pattern"": ""*.cs"" }

### web_search
Search the web when the answer depends on current or uncertain information.
Parameters: { ""query"": ""search term"" }

### atlas_api
Call Atlas internal read-only runtime APIs for routing and integration status.
Parameters: { ""operation"": ""get_ai_routing_status|get_routing_status|list_integrations|get_integration|get_integration_summary"", ""id"": ""optional integration id"" }

### create_directory
Create a new directory.
Parameters: { ""path"": ""relative/path/to/new/dir"" }

### delete_file
Delete a file.
Parameters: { ""path"": ""relative/path/to/file"" }

### move_file
Move or rename a file.
Parameters: { ""source"": ""old/path"", ""destination"": ""new/path"" }

### get_file_info
Get metadata about a file (size, modified date, etc).
Parameters: { ""path"": ""relative/path/to/file"" }

## SOFTWARE INSTALLATION TOOLS:

### install_software
Install any software using winget, pip, npm, or choco. Just say what you want!
Parameters: { ""name"": ""python"" } or { ""name"": ""discord"" } or { ""name"": ""numpy"" }
Examples: python, node, git, vscode, discord, spotify, chrome, numpy, pandas, typescript

### uninstall_software
Uninstall software.
Parameters: { ""name"": ""software name"" }

### check_installed
Check if software is installed.
Parameters: { ""name"": ""python"" }

## CODE TOOLS:

### generate_code
Generate code based on a description.
Parameters: { ""request"": ""create a function that..., ""language"": ""python"" }

### modify_code
Modify existing code with instructions.
Parameters: { ""code"": ""existing code"", ""instructions"": ""add error handling"", ""language"": ""python"" }

### fix_code
Fix errors in code.
Parameters: { ""code"": ""broken code"", ""error"": ""error message (optional)"", ""language"": ""python"" }

### explain_code
Explain what code does.
Parameters: { ""code"": ""code to explain"", ""language"": ""python"" }

### create_code_file
Generate and save a code file.
Parameters: { ""path"": ""src/utils.py"", ""description"": ""utility functions for string manipulation"" }

### refactor_code
Improve code quality.
Parameters: { ""code"": ""code to refactor"", ""language"": ""python"" }

### generate_tests
Generate unit tests for code.
Parameters: { ""code"": ""code to test"", ""language"": ""python"", ""framework"": ""pytest"" }

## COMMAND TOOLS:

### run_command
Execute a shell command.
Parameters: { ""command"": ""dotnet build"", ""working_dir"": ""."" }

### run_powershell
Execute a PowerShell script.
Parameters: { ""script"": ""Get-Process | Select-Object -First 5"" }

## How to use tools:

When you need to use a tool, respond with ONLY a JSON block like this:
```tool
{""tool"": ""install_software"", ""params"": {""name"": ""python""}}
```

After I execute the tool, I'll give you the result and you can continue.

## Important Rules:
1. Use tools to actually make changes - don't just describe what to do
2. For installations, just use install_software with the name
3. Read files before modifying them to understand context
4. After writing files, verify your changes worked
5. For multi-step tasks, execute one tool at a time
6. Always use relative paths from the workspace root
";

        // Execute a tool call and return the result
        public static async Task<ToolResult> ExecuteToolAsync(string toolName, Dictionary<string, object> parameters, string workspacePath, CancellationToken ct = default)
        {
            try
            {
                ct.ThrowIfCancellationRequested();

                return toolName.ToLower() switch
                {
                    // File tools
                    "read_file" => await ReadFileAsync(parameters, workspacePath, ct),
                    "write_file" => await WriteFileAsync(parameters, workspacePath, ct),
                    "append_file" => await AppendFileAsync(parameters, workspacePath, ct),
                    "list_directory" => await ListDirectoryAsync(parameters, workspacePath, ct),
                    "search_files" => await SearchFilesAsync(parameters, workspacePath, ct),
                    "search_content" => await SearchContentAsync(parameters, workspacePath, ct),
                    "web_search" => await WebSearchAsync(parameters, ct),
                    "atlas_api" or "call_internal_api" => await CallAtlasApiAsync(parameters, ct),
                    "create_directory" => await CreateDirectoryAsync(parameters, workspacePath),
                    "delete_file" => await DeleteFileAsync(parameters, workspacePath, ct),
                    "move_file" => await MoveFileAsync(parameters, workspacePath),
                    "get_file_info" => await GetFileInfoAsync(parameters, workspacePath),
                    
                    // Software installation tools
                    "install_software" or "install" => await InstallSoftwareAsync(parameters, ct),
                    "uninstall_software" or "uninstall" => await UninstallSoftwareAsync(parameters, ct),
                    "check_installed" => await CheckInstalledAsync(parameters, ct),
                    
                    // Code tools
                    "generate_code" => await GenerateCodeAsync(parameters, ct),
                    "modify_code" => await ModifyCodeAsync(parameters, ct),
                    "fix_code" => await FixCodeAsync(parameters, ct),
                    "explain_code" => await ExplainCodeAsync(parameters, ct),
                    "create_code_file" => await CreateCodeFileAsync(parameters, workspacePath, ct),
                    "refactor_code" => await RefactorCodeAsync(parameters, ct),
                    "generate_tests" => await GenerateTestsAsync(parameters, ct),
                    
                    // Command tools
                    "run_command" => await RunCommandAsync(parameters, workspacePath, ct),
                    "run_powershell" => await RunPowerShellAsync(parameters, ct),
                    
                    _ => new ToolResult { Success = false, Output = $"Unknown tool: {toolName}" }
                };
            }
            catch (OperationCanceledException)
            {
                return new ToolResult { Success = false, Output = "Tool execution cancelled" };
            }
            catch (Exception ex)
            {
                return new ToolResult { Success = false, Output = $"Tool error: {ex.Message}" };
            }
        }
        
        // ==================== SOFTWARE INSTALLATION ====================
        
        private static async Task<ToolResult> InstallSoftwareAsync(Dictionary<string, object> p, CancellationToken ct = default)
        {
            var name = GetParam(p, "name");
            if (string.IsNullOrEmpty(name))
                return new ToolResult { Success = false, Output = "Software name is required" };
            
            var result = await SoftwareInstaller.InstallAsync(name);
            return new ToolResult { Success = !result.StartsWith("❌"), Output = result };
        }
        
        private static async Task<ToolResult> UninstallSoftwareAsync(Dictionary<string, object> p, CancellationToken ct = default)
        {
            var name = GetParam(p, "name");
            if (string.IsNullOrEmpty(name))
                return new ToolResult { Success = false, Output = "Software name is required" };
            
            var result = await SoftwareInstaller.UninstallAsync(name);
            return new ToolResult { Success = !result.StartsWith("❌"), Output = result };
        }
        
        private static async Task<ToolResult> CheckInstalledAsync(Dictionary<string, object> p, CancellationToken ct = default)
        {
            var name = GetParam(p, "name");
            if (string.IsNullOrEmpty(name))
                return new ToolResult { Success = false, Output = "Software name is required" };
            
            var isInstalled = await SoftwareInstaller.IsInstalledAsync(name);
            return new ToolResult 
            { 
                Success = true, 
                Output = isInstalled ? $"✅ {name} is installed" : $"❌ {name} is NOT installed" 
            };
        }
        
        // ==================== CODE TOOLS ====================
        
        private static async Task<ToolResult> GenerateCodeAsync(Dictionary<string, object> p, CancellationToken ct = default)
        {
            var request = GetParam(p, "request");
            var language = GetParam(p, "language");
            
            if (string.IsNullOrEmpty(request))
                return new ToolResult { Success = false, Output = "Request description is required" };
            
            var result = await CodeAssistant.GenerateCodeAsync(request, language, ct: ct);
            
            if (!result.Success)
                return new ToolResult { Success = false, Output = result.Error ?? "Failed to generate code" };
            
            var output = new StringBuilder();
            if (!string.IsNullOrEmpty(result.Explanation))
                output.AppendLine(result.Explanation).AppendLine();
            output.AppendLine($"```{result.Language ?? ""}");
            output.AppendLine(result.Code);
            output.AppendLine("```");
            
            return new ToolResult { Success = true, Output = output.ToString() };
        }
        
        private static async Task<ToolResult> ModifyCodeAsync(Dictionary<string, object> p, CancellationToken ct = default)
        {
            var code = GetParam(p, "code");
            var instructions = GetParam(p, "instructions");
            var language = GetParam(p, "language");
            
            if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(instructions))
                return new ToolResult { Success = false, Output = "Code and instructions are required" };
            
            var result = await CodeAssistant.ModifyCodeAsync(code, instructions, language, ct: ct);
            
            if (!result.Success)
                return new ToolResult { Success = false, Output = result.Error ?? "Failed to modify code" };
            
            return new ToolResult { Success = true, Output = $"```{result.Language ?? ""}\n{result.Code}\n```" };
        }
        
        private static async Task<ToolResult> FixCodeAsync(Dictionary<string, object> p, CancellationToken ct = default)
        {
            var code = GetParam(p, "code");
            var error = GetParam(p, "error");
            var language = GetParam(p, "language");
            
            if (string.IsNullOrEmpty(code))
                return new ToolResult { Success = false, Output = "Code is required" };
            
            var result = await CodeAssistant.FixCodeAsync(code, error, language, ct: ct);
            
            if (!result.Success)
                return new ToolResult { Success = false, Output = result.Error ?? "Failed to fix code" };
            
            return new ToolResult { Success = true, Output = $"```{result.Language ?? ""}\n{result.Code}\n```" };
        }
        
        private static async Task<ToolResult> ExplainCodeAsync(Dictionary<string, object> p, CancellationToken ct = default)
        {
            var code = GetParam(p, "code");
            var language = GetParam(p, "language");
            
            if (string.IsNullOrEmpty(code))
                return new ToolResult { Success = false, Output = "Code is required" };
            
            var explanation = await CodeAssistant.ExplainCodeAsync(code, language, ct: ct);
            return new ToolResult { Success = true, Output = explanation };
        }
        
        private static async Task<ToolResult> CreateCodeFileAsync(Dictionary<string, object> p, string workspace, CancellationToken ct = default)
        {
            var path = GetParam(p, "path");
            var description = GetParam(p, "description");
            
            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(description))
                return new ToolResult { Success = false, Output = "Path and description are required" };
            
            var result = await CodeAssistant.CreateFileAsync(path, description, workspace, ct: ct);
            return new ToolResult { Success = !result.StartsWith("❌"), Output = result };
        }
        
        private static async Task<ToolResult> RefactorCodeAsync(Dictionary<string, object> p, CancellationToken ct = default)
        {
            var code = GetParam(p, "code");
            var language = GetParam(p, "language");
            
            if (string.IsNullOrEmpty(code))
                return new ToolResult { Success = false, Output = "Code is required" };
            
            var result = await CodeAssistant.RefactorCodeAsync(code, language, ct: ct);
            
            if (!result.Success)
                return new ToolResult { Success = false, Output = result.Error ?? "Failed to refactor code" };
            
            return new ToolResult { Success = true, Output = $"```{result.Language ?? ""}\n{result.Code}\n```" };
        }
        
        private static async Task<ToolResult> GenerateTestsAsync(Dictionary<string, object> p, CancellationToken ct = default)
        {
            var code = GetParam(p, "code");
            var language = GetParam(p, "language");
            var framework = GetParam(p, "framework");
            
            if (string.IsNullOrEmpty(code))
                return new ToolResult { Success = false, Output = "Code is required" };
            
            var result = await CodeAssistant.GenerateTestsAsync(code, language, framework, ct: ct);
            
            if (!result.Success)
                return new ToolResult { Success = false, Output = result.Error ?? "Failed to generate tests" };
            
            return new ToolResult { Success = true, Output = $"```{result.Language ?? ""}\n{result.Code}\n```" };
        }
        
        private static async Task<ToolResult> RunPowerShellAsync(Dictionary<string, object> p, CancellationToken ct = default)
        {
            var script = GetParam(p, "script");
            if (string.IsNullOrEmpty(script))
                return new ToolResult { Success = false, Output = "Script is required" };
            
            // SAFETY GATE: Check with SafetyKernel before executing PowerShell
            var safetyCheck = await AtlasAI.Core.SafetyKernel.Instance.CheckAndBlockAsync(
                AtlasAI.Core.OperationType.CommandExecution,
                AtlasAI.Core.OperationRisk.High,
                $"Execute PowerShell script",
                new Dictionary<string, object>
                {
                    ["script"] = script.Length > 200 ? script.Substring(0, 200) + "..." : script
                });

            if (safetyCheck.Decision == AtlasAI.Core.SafetyDecision.Blocked)
            {
                return new ToolResult 
                { 
                    Success = false, 
                    Output = safetyCheck.Message + "\n\n💡 PowerShell execution is disabled in Safety Mode."
                };
            }

            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{script.Replace("\"", "\\\"")}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
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

            // Handle cancellation
            using var reg = ct.Register(() => { try { process.Kill(); } catch { } });

            var completed = await Task.Run(() => process.WaitForExit(60000), ct);
            if (!completed)
            {
                process.Kill();
                return new ToolResult { Success = false, Output = "⚠️ Script timed out after 60s" };
            }

            var result = output.ToString();
            if (error.Length > 0)
                result += "\n--- Errors ---\n" + error.ToString();

            return new ToolResult 
            { 
                Success = process.ExitCode == 0, 
                Output = string.IsNullOrWhiteSpace(result) ? "(no output)" : result 
            };
        }

        private static string GetParam(Dictionary<string, object> p, string key, string defaultVal = "")
        {
            if (p.TryGetValue(key, out var val))
                return val?.ToString() ?? defaultVal;
            return defaultVal;
        }

        private static bool GetBoolParam(Dictionary<string, object> p, string key, bool defaultVal = false)
        {
            if (p.TryGetValue(key, out var val))
            {
                if (val is bool b) return b;
                if (val is JsonElement je && je.ValueKind == JsonValueKind.True) return true;
                if (val is JsonElement je2 && je2.ValueKind == JsonValueKind.False) return false;
                return bool.TryParse(val?.ToString(), out var result) && result;
            }
            return defaultVal;
        }

        private static string ResolvePath(string relativePath, string workspacePath)
        {
            if (string.IsNullOrWhiteSpace(workspacePath))
                throw new SecurityException("Workspace path is required");

            var workspaceRoot = Path.GetFullPath(workspacePath);
            var candidate = relativePath?.Trim();
            if (string.IsNullOrWhiteSpace(candidate) || candidate == ".")
                return workspaceRoot;

            if (candidate.StartsWith("\\\\", StringComparison.Ordinal) || candidate.StartsWith("//", StringComparison.Ordinal))
                throw new SecurityException("UNC paths are not allowed");

            if (Regex.IsMatch(candidate, @"^[A-Za-z]:"))
                throw new SecurityException("Drive-qualified paths are not allowed");

            if (Path.IsPathRooted(candidate))
                throw new SecurityException("Rooted paths are not allowed");

            var normalizedCandidate = candidate
                .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
                .TrimStart(Path.DirectorySeparatorChar);

            var resolvedPath = Path.GetFullPath(Path.Combine(workspaceRoot, normalizedCandidate));
            if (!IsPathUnderWorkspaceRoot(resolvedPath, workspaceRoot))
                throw new SecurityException("Path escapes the workspace root");

            return resolvedPath;
        }

        private static bool IsPathUnderWorkspaceRoot(string candidatePath, string workspaceRoot)
        {
            var comparison = OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

            var normalizedRoot = Path.GetFullPath(workspaceRoot)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var normalizedCandidate = Path.GetFullPath(candidatePath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var rootPrefix = normalizedRoot + Path.DirectorySeparatorChar;

            return string.Equals(normalizedCandidate, normalizedRoot, comparison)
                || normalizedCandidate.StartsWith(rootPrefix, comparison);
        }

        private static async Task<ToolResult> ReadFileAsync(Dictionary<string, object> p, string workspace, CancellationToken ct = default)
        {
            var path = ResolvePath(GetParam(p, "path"), workspace);
            if (!File.Exists(path))
                return new ToolResult { Success = false, Output = $"File not found: {GetParam(p, "path")}" };

            var content = await File.ReadAllTextAsync(path, ct);
            // Truncate very large files
            if (content.Length > 50000)
                content = content.Substring(0, 50000) + "\n\n[... truncated, file too large ...]";
            
            return new ToolResult { Success = true, Output = content };
        }

        private static async Task<ToolResult> WriteFileAsync(Dictionary<string, object> p, string workspace, CancellationToken ct = default)
        {
            var relativePath = GetParam(p, "path");
            var path = ResolvePath(relativePath, workspace);
            var content = GetParam(p, "content");
            
            // Track for undo
            var safety = AgentSafetyManager.Instance;
            string? originalContent = null;
            bool isNewFile = !File.Exists(path);
            
            if (!isNewFile)
            {
                try { originalContent = await File.ReadAllTextAsync(path, ct); }
                catch { }
            }
            
            // Create directory if needed
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            await File.WriteAllTextAsync(path, content, ct);
            
            // Push undo action
            safety.PushUndo(new AgentUndoAction
            {
                Type = isNewFile ? UndoType.FileCreated : UndoType.FileModified,
                Description = isNewFile ? $"Created {relativePath}" : $"Modified {relativePath}",
                TargetPath = path,
                OriginalContent = originalContent
            });
            
            return new ToolResult { Success = true, Output = $"✅ Written {content.Length} chars to {relativePath}" };
        }

        private static async Task<ToolResult> AppendFileAsync(Dictionary<string, object> p, string workspace, CancellationToken ct = default)
        {
            var path = ResolvePath(GetParam(p, "path"), workspace);
            var content = GetParam(p, "content");
            
            await File.AppendAllTextAsync(path, content, ct);
            return new ToolResult { Success = true, Output = $"✅ Appended {content.Length} chars to {GetParam(p, "path")}" };
        }

        private static async Task<ToolResult> ListDirectoryAsync(Dictionary<string, object> p, string workspace, CancellationToken ct = default)
        {
            var path = ResolvePath(GetParam(p, "path", "."), workspace);
            var recursive = GetBoolParam(p, "recursive", false);

            if (!Directory.Exists(path))
                return new ToolResult { Success = false, Output = $"Directory not found: {GetParam(p, "path")}" };

            var sb = new StringBuilder();
            var option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            
            var directories = Directory.GetDirectories(path, "*", option).Take(100);
            var files = Directory.GetFiles(path, "*", option).Take(200);

            foreach (var dir in directories)
            {
                ct.ThrowIfCancellationRequested();
                var rel = Path.GetRelativePath(workspace, dir);
                sb.AppendLine($"📁 {rel}/");
            }
            foreach (var file in files)
            {
                ct.ThrowIfCancellationRequested();
                var rel = Path.GetRelativePath(workspace, file);
                var info = new FileInfo(file);
                sb.AppendLine($"📄 {rel} ({FormatSize(info.Length)})");
            }

            return new ToolResult { Success = true, Output = sb.ToString() };
        }

        private static async Task<ToolResult> SearchFilesAsync(Dictionary<string, object> p, string workspace, CancellationToken ct = default)
        {
            var pattern = GetParam(p, "pattern", "*");
            var path = ResolvePath(GetParam(p, "path", "."), workspace);

            if (!Directory.Exists(path))
                return new ToolResult { Success = false, Output = "Directory not found" };

            var files = Directory.GetFiles(path, pattern, SearchOption.AllDirectories)
                .Take(100)
                .Select(f => Path.GetRelativePath(workspace, f));

            return new ToolResult { Success = true, Output = string.Join("\n", files) };
        }

        private static async Task<ToolResult> SearchContentAsync(Dictionary<string, object> p, string workspace, CancellationToken ct = default)
        {
            var query = GetParam(p, "query");
            var path = ResolvePath(GetParam(p, "path", "."), workspace);
            var filePattern = GetParam(p, "file_pattern", "*");

            if (string.IsNullOrEmpty(query))
                return new ToolResult { Success = false, Output = "Query is required" };

            var results = new StringBuilder();
            var matchCount = 0;

            foreach (var file in Directory.GetFiles(path, filePattern, SearchOption.AllDirectories).Take(500))
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var lines = await File.ReadAllLinesAsync(file, ct);
                    for (int i = 0; i < lines.Length; i++)
                    {
                        if (lines[i].Contains(query, StringComparison.OrdinalIgnoreCase))
                        {
                            var rel = Path.GetRelativePath(workspace, file);
                            results.AppendLine($"{rel}:{i + 1}: {lines[i].Trim()}");
                            matchCount++;
                            if (matchCount >= 50) break;
                        }
                    }
                }
                catch { }
                if (matchCount >= 50) break;
            }

            return new ToolResult 
            { 
                Success = true, 
                Output = matchCount > 0 ? results.ToString() : "No matches found" 
            };
        }

        private static async Task<ToolResult> WebSearchAsync(Dictionary<string, object> p, CancellationToken ct = default)
        {
            var query = GetParam(p, "query");
            if (string.IsNullOrWhiteSpace(query))
                return new ToolResult { Success = false, Output = "Query is required" };

            var result = await WebSearchTool.SearchAsync(query, ct);
            return new ToolResult
            {
                Success = !string.IsNullOrWhiteSpace(result) && !result.StartsWith("CANCELLED", StringComparison.OrdinalIgnoreCase),
                Output = string.IsNullOrWhiteSpace(result) ? "No web result returned" : result
            };
        }

        private static Task<ToolResult> CallAtlasApiAsync(Dictionary<string, object> p, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();

            var operation = GetParam(p, "operation").Trim().ToLowerInvariant();
            var integrationId = GetParam(p, "id").Trim();
            if (string.IsNullOrWhiteSpace(operation))
                return Task.FromResult(new ToolResult { Success = false, Output = "Operation is required" });

            try
            {
                switch (operation)
                {
                    case "get_ai_routing_status":
                    case "get_routing_status":
                    {
                        var activeProvider = AIManager.GetActiveProvider();
                        var output = string.Join("\n", new[]
                        {
                            $"Active provider: {activeProvider}",
                            $"Active manual model: {AIManager.GetSelectedModel(activeProvider)}",
                            $"Auto mode enabled: {AIManager.GetAutoModeEnabled()}",
                            $"Routing mode: {AIManager.GetRoutingMode()}",
                            $"OpenAI cheap/smart: {AIManager.GetAutoCheapModel(AIProviderType.OpenAI)} / {AIManager.GetAutoSmartModel(AIProviderType.OpenAI)}",
                            $"Claude cheap/smart: {AIManager.GetAutoCheapModel(AIProviderType.Claude)} / {AIManager.GetAutoSmartModel(AIProviderType.Claude)}",
                            $"Gemini cheap/smart: {AIManager.GetAutoCheapModel(AIProviderType.Gemini)} / {AIManager.GetAutoSmartModel(AIProviderType.Gemini)}"
                        });

                        return Task.FromResult(new ToolResult { Success = true, Output = output });
                    }

                    case "list_integrations":
                    {
                        IntegrationRegistry.Initialize();
                        IntegrationRegistry.Refresh();
                        var integrations = IntegrationRegistry.GetAll()
                            .OrderBy(i => i.Category)
                            .ThenBy(i => i.Name)
                            .Select(i => $"{i.Id} | {i.Name} | category={i.Category} | status={i.Status} | configured={(i.IsConfigured ? "yes" : "no")}");

                        return Task.FromResult(new ToolResult
                        {
                            Success = true,
                            Output = string.Join("\n", integrations)
                        });
                    }

                    case "get_integration":
                    {
                        if (string.IsNullOrWhiteSpace(integrationId))
                            return Task.FromResult(new ToolResult { Success = false, Output = "Integration id is required for get_integration" });

                        IntegrationRegistry.Initialize();
                        IntegrationRegistry.Refresh();
                        var integration = IntegrationRegistry.GetById(integrationId);
                        if (integration == null)
                            return Task.FromResult(new ToolResult { Success = false, Output = $"Integration not found: {integrationId}" });

                        var details = string.Join("\n", new[]
                        {
                            $"Id: {integration.Id}",
                            $"Name: {integration.Name}",
                            $"Category: {integration.Category}",
                            $"Status: {integration.Status}",
                            $"Configured: {(integration.IsConfigured ? "yes" : "no")}",
                            $"Requires API key: {(integration.RequiresApiKey ? "yes" : "no")}",
                            $"Requires app: {(integration.RequiresApp ? "yes" : "no")}",
                            $"Description: {integration.Description}",
                            $"Capabilities: {string.Join(", ", integration.Capabilities ?? Array.Empty<string>())}",
                            $"Examples: {string.Join(" | ", integration.ExampleCommands ?? Array.Empty<string>())}"
                        });

                        return Task.FromResult(new ToolResult { Success = true, Output = details });
                    }

                    case "get_integration_summary":
                    {
                        IntegrationRegistry.Initialize();
                        IntegrationRegistry.Refresh();
                        var summary = IntegrationRegistry.GetSummary();
                        var output = string.Join("\n", new[]
                        {
                            $"Total integrations: {summary.TotalIntegrations}",
                            $"Available: {summary.AvailableCount}",
                            $"Configured: {summary.ConfiguredCount}",
                            $"Coming soon: {summary.ComingSoonCount}",
                            $"Categories: {string.Join(", ", summary.Categories)}"
                        });

                        return Task.FromResult(new ToolResult { Success = true, Output = output });
                    }

                    default:
                        return Task.FromResult(new ToolResult { Success = false, Output = $"Unsupported Atlas API operation: {operation}" });
                }
            }
            catch (Exception ex)
            {
                return Task.FromResult(new ToolResult { Success = false, Output = $"Atlas API error: {ex.Message}" });
            }
        }

        private static async Task<ToolResult> RunCommandAsync(Dictionary<string, object> p, string workspace, CancellationToken ct = default)
        {
            var command = GetParam(p, "command");
            var workingDir = ResolvePath(GetParam(p, "working_dir", "."), workspace);

            // SAFETY GATE: Check with SafetyKernel before executing commands
            var safetyCheck = await AtlasAI.Core.SafetyKernel.Instance.CheckAndBlockAsync(
                AtlasAI.Core.OperationType.CommandExecution,
                AtlasAI.Core.OperationRisk.High,
                $"Execute command: {command}",
                new Dictionary<string, object>
                {
                    ["command"] = command,
                    ["workingDir"] = workingDir
                });

            if (safetyCheck.Decision == AtlasAI.Core.SafetyDecision.Blocked)
            {
                return new ToolResult 
                { 
                    Success = false, 
                    Output = safetyCheck.Message + "\n\n💡 Command execution is disabled in Safety Mode."
                };
            }

            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c {command}",
                WorkingDirectory = workingDir,
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

            // Handle cancellation
            using var reg = ct.Register(() => { try { process.Kill(); } catch { } });

            // Timeout after 120 seconds
            var completed = await Task.Run(() => process.WaitForExit(120000), ct);
            if (!completed)
            {
                process.Kill();
                return new ToolResult { Success = false, Output = "⚠️ Command timed out after 120s" };
            }

            var result = output.ToString();
            if (error.Length > 0)
                result += "\n--- Errors ---\n" + error.ToString();

            return new ToolResult 
            { 
                Success = process.ExitCode == 0, 
                Output = string.IsNullOrWhiteSpace(result) ? "(no output)" : result 
            };
        }

        private static Task<ToolResult> CreateDirectoryAsync(Dictionary<string, object> p, string workspace)
        {
            var relativePath = GetParam(p, "path");
            var path = ResolvePath(relativePath, workspace);
            
            bool alreadyExists = Directory.Exists(path);
            Directory.CreateDirectory(path);
            
            // Track for undo (only if newly created)
            if (!alreadyExists)
            {
                AgentSafetyManager.Instance.PushUndo(new AgentUndoAction
                {
                    Type = UndoType.DirectoryCreated,
                    Description = $"Created directory {relativePath}",
                    TargetPath = path
                });
            }
            
            return Task.FromResult(new ToolResult { Success = true, Output = $"✅ Created directory: {relativePath}" });
        }

        private static async Task<ToolResult> DeleteFileAsync(Dictionary<string, object> p, string workspace, CancellationToken ct = default)
        {
            var relativePath = GetParam(p, "path");
            var path = ResolvePath(relativePath, workspace);
            
            if (!File.Exists(path))
                return new ToolResult { Success = false, Output = $"File not found: {relativePath}" };
            
            // Backup content for undo
            var safety = AgentSafetyManager.Instance;
            string? originalContent = null;
            try { originalContent = await File.ReadAllTextAsync(path, ct); }
            catch { }
            
            // Delete the file
            File.Delete(path);
            
            // Push undo action
            safety.PushUndo(new AgentUndoAction
            {
                Type = UndoType.FileDeleted,
                Description = $"Deleted {relativePath}",
                TargetPath = path,
                OriginalContent = originalContent
            });
            
            return new ToolResult { Success = true, Output = $"✅ Deleted {relativePath}" };
        }

        private static Task<ToolResult> MoveFileAsync(Dictionary<string, object> p, string workspace)
        {
            var source = ResolvePath(GetParam(p, "source"), workspace);
            var dest = ResolvePath(GetParam(p, "destination"), workspace);
            
            if (!File.Exists(source))
                return Task.FromResult(new ToolResult { Success = false, Output = "Source file not found" });

            var destDir = Path.GetDirectoryName(dest);
            if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
                Directory.CreateDirectory(destDir);

            File.Move(source, dest, overwrite: true);
            return Task.FromResult(new ToolResult { Success = true, Output = $"✅ Moved to: {GetParam(p, "destination")}" });
        }

        private static Task<ToolResult> GetFileInfoAsync(Dictionary<string, object> p, string workspace)
        {
            var path = ResolvePath(GetParam(p, "path"), workspace);
            if (!File.Exists(path))
                return Task.FromResult(new ToolResult { Success = false, Output = "File not found" });

            var info = new FileInfo(path);
            var output = $@"Path: {GetParam(p, "path")}
Size: {FormatSize(info.Length)}
Created: {info.CreationTime}
Modified: {info.LastWriteTime}
Extension: {info.Extension}";

            return Task.FromResult(new ToolResult { Success = true, Output = output });
        }

        private static string FormatSize(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            return $"{bytes / (1024.0 * 1024.0):F1} MB";
        }
    }

    public class ToolResult
    {
        public bool Success { get; set; }
        public string Output { get; set; } = "";
    }

    public class ToolCall
    {
        public string Tool { get; set; } = "";
        public Dictionary<string, object> Params { get; set; } = new();
    }
}
