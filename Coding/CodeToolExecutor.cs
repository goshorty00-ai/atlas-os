using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AtlasAI.Coding.Services;

namespace AtlasAI.Coding
{
    /// <summary>
    /// Executes coding tool commands from AI responses
    /// Parses tool calls like: [TOOL:read_file path="src/main.cs"]
    /// </summary>
    public class CodeToolExecutor
    {
        private readonly CodeAssistantService _codeAssistant;
        private Stage? _currentStage;

        public CodeToolExecutor(CodeAssistantService codeAssistant)
        {
            _codeAssistant = codeAssistant;
        }
        
        private string GetArg(Dictionary<string, string> args, string key, string defaultValue = "")
        {
            return args.TryGetValue(key, out var value) ? value : defaultValue;
        }

        /// <summary>
        /// Check if message contains a tool call and execute it
        /// </summary>
        public async Task<(bool handled, string? result)> TryExecuteToolAsync(string aiResponse, System.Threading.CancellationToken ct = default)
        {
            // Look for tool patterns in the response
            var toolMatch = Regex.Match(aiResponse, @"\[TOOL:(\w+)([^\]]*)\]", RegexOptions.IgnoreCase);
            if (!toolMatch.Success)
                return (false, null);

            ct.ThrowIfCancellationRequested();

            var toolName = toolMatch.Groups[1].Value.ToLower();
            var argsString = toolMatch.Groups[2].Value.Trim();
            var args = ParseArgs(argsString);

            // Check if this is a write operation that needs stage tracking
            var isWriteOp = toolName is "write_file" or "writefile" or "replace" or "str_replace" or "delete" or "delete_file";
            
            try
            {
                // Begin stage for write operations
                if (isWriteOp && _currentStage == null)
                {
                    var description = toolName switch
                    {
                        "write_file" or "writefile" => $"Write: {GetArg(args, "path")}",
                        "replace" or "str_replace" => $"Replace in: {GetArg(args, "path")}",
                        "delete" or "delete_file" => $"Delete: {GetArg(args, "path")}",
                        _ => $"Tool: {toolName}"
                    };
                    _currentStage = _codeAssistant.BeginStage(description);
                }
                
                var result = toolName switch
                {
                    "read_file" or "readfile" => await _codeAssistant.ReadFileAsync(GetArg(args, "path"), ct),
                    "write_file" or "writefile" => await _codeAssistant.WriteFileAsync(
                        GetArg(args, "path"),
                        GetArg(args, "content"),
                        ct),
                    "search" or "grep" => await _codeAssistant.SearchAsync(
                        GetArg(args, "pattern"),
                        args.TryGetValue("files", out var f) ? f : null,
                        ct),
                    "find_files" or "findfiles" => _codeAssistant.FindFiles(GetArg(args, "pattern")),
                    "replace" or "str_replace" => await _codeAssistant.ReplaceInFileAsync(
                        GetArg(args, "path"),
                        GetArg(args, "old"),
                        GetArg(args, "new"),
                        ct),
                    "run" or "execute" or "cmd" => await _codeAssistant.RunCommandAsync(
                        GetArg(args, "command"),
                        int.TryParse(GetArg(args, "timeout", "30"), out var t) ? t : 30,
                        ct),
                    "delete" or "delete_file" => await _codeAssistant.DeleteFileAsync(GetArg(args, "path"), ct),
                    "info" or "file_info" => _codeAssistant.GetFileInfo(GetArg(args, "path")),
                    "structure" or "tree" => _codeAssistant.GetProjectStructure(
                        int.TryParse(GetArg(args, "depth", "3"), out var d) ? d : 3),
                    "set_workspace" => SetWorkspace(GetArg(args, "path")),
                    _ => $"❌ Unknown tool: {toolName}"
                };

                ct.ThrowIfCancellationRequested();

                // Complete stage for write operations
                if (isWriteOp && _currentStage != null)
                {
                    if (result.StartsWith("✅"))
                    {
                        _codeAssistant.CompleteStage();
                    }
                    else
                    {
                        _codeAssistant.FailStage(result);
                    }
                    _currentStage = null;
                }

                return (true, result);
            }
            catch (OperationCanceledException)
            {
                if (isWriteOp && _currentStage != null)
                {
                    _codeAssistant.FailStage("Operation cancelled by user.");
                    _currentStage = null;
                }
                throw;
            }
            catch (Exception ex)
            {
                // Fail stage on exception
                if (isWriteOp && _currentStage != null)
                {
                    _codeAssistant.FailStage(ex.Message);
                    _currentStage = null;
                }
                return (true, $"❌ Tool error: {ex.Message}");
            }
        }

        private string SetWorkspace(string path)
        {
            _codeAssistant.SetWorkspace(path);
            return _codeAssistant.HasWorkspace 
                ? $"✅ Workspace set to: {path}\n\n{_codeAssistant.GetProjectStructure(2)}"
                : $"❌ Invalid workspace path: {path}";
        }

        /// <summary>
        /// Parse tool arguments from string like: path="file.cs" content="..."
        /// </summary>
        private System.Collections.Generic.Dictionary<string, string> ParseArgs(string argsString)
        {
            var args = new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            
            // Match key="value" or key='value' patterns
            var matches = Regex.Matches(argsString, @"(\w+)\s*=\s*[""']([^""']*)[""']");
            foreach (Match match in matches)
            {
                args[match.Groups[1].Value] = match.Groups[2].Value;
            }

            // Also try key=value without quotes for simple values
            var simpleMatches = Regex.Matches(argsString, @"(\w+)\s*=\s*([^\s""']+)");
            foreach (Match match in simpleMatches)
            {
                if (!args.ContainsKey(match.Groups[1].Value))
                    args[match.Groups[1].Value] = match.Groups[2].Value;
            }

            return args;
        }

        /// <summary>
        /// Get the system prompt addition for coding capabilities
        /// </summary>
        public static string GetCodingSystemPrompt()
        {
            return @"

## CODING ASSISTANT CAPABILITIES

You have access to coding tools. When the user asks you to work with code or files, use these tools:

### Available Tools (use format: [TOOL:name arg=""value""])

1. **Read a file**: `[TOOL:read_file path=""src/file.cs""]`
2. **Write/create file**: `[TOOL:write_file path=""src/new.cs"" content=""file content here""]`
3. **Search in code**: `[TOOL:search pattern=""functionName"" files=""*.cs""]`
4. **Find files**: `[TOOL:find_files pattern=""Controller""]`
5. **Replace text**: `[TOOL:replace path=""file.cs"" old=""old text"" new=""new text""]`
6. **Run command**: `[TOOL:run command=""dotnet build""]`
7. **Delete file**: `[TOOL:delete path=""file.cs""]`
8. **File info**: `[TOOL:info path=""file.cs""]`
9. **Project structure**: `[TOOL:structure depth=3]`
10. **Set workspace**: `[TOOL:set_workspace path=""C:\Projects\MyApp""]`

### Guidelines:
- When user drops a folder, set it as workspace first
- Read files before modifying them
- Show the user what changes you'll make before applying
- Use search to find relevant code
- For modifications, use replace with exact text matching
- Run build/test commands to verify changes work
";
        }
    }
}
