using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using AtlasAI.AI;

namespace AtlasAI.Agent
{
    /// <summary>
    /// Represents an action taken by the agent (for history/undo)
    /// </summary>
    public class AgentAction
    {
        public string Tool { get; set; } = "";
        public Dictionary<string, object> Params { get; set; } = new();
        public string Result { get; set; } = "";
        public bool Success { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public string? UndoData { get; set; } // For undo support (e.g., original file content)
    }
    
    /// <summary>
    /// The Agent Orchestrator - runs the AI in a loop, executing tools until task is complete.
    /// This is what makes Atlas work like Kiro - it can actually DO things, not just talk.
    /// </summary>
    public class AgentOrchestrator
    {
        internal sealed record AgentRoutingProfile(
            string Module,
            string ActivePage,
            AIManager.AITaskBucket? BucketHint,
            string ToolContext,
            string AdditionalInstructions);

        private readonly string _workspacePath;
        private readonly List<AgentMessage> _conversationHistory = new();
        private readonly List<AgentAction> _actionHistory = new();
        private const int MaxIterations = 20; // Safety limit
        
        // Destructive operations that require confirmation
        private static readonly HashSet<string> DestructiveTools = new(StringComparer.OrdinalIgnoreCase)
        {
            "delete_file", "uninstall_software", "uninstall", "run_command", "run_powershell"
        };

        private static readonly HashSet<string> MutatingInferredTools = new(StringComparer.OrdinalIgnoreCase)
        {
            "write_file", "patch_file", "create_file", "create_code_file", "append_file", "install_software",
            "delete_file", "remove_file", "move_file", "run_command", "run_powershell", "create_directory"
        };
        
        public event EventHandler<string>? OnThinking;
        public event EventHandler<string>? OnToolExecuting;
        public event EventHandler<ToolResult>? OnToolResult;
        public event EventHandler<string>? OnResponse;
        public event EventHandler<string>? OnError;

        public string LastProviderUsed { get; private set; } = "";
        public string LastModelUsed { get; private set; } = "";
        public string LastTaskBucketUsed { get; private set; } = "";

        /// <summary>
        /// Optional role-specific prefix injected at the start of the system prompt.
        /// Used by Builder/Designer agent modes.
        /// </summary>
        public string? SystemPromptPrefix { get; set; }
        
        /// <summary>
        /// Event raised when a destructive operation needs confirmation.
        /// Handler should return true to proceed, false to cancel.
        /// </summary>
        public Func<string, string, Task<bool>>? OnConfirmationRequired;
        
        /// <summary>
        /// Get the history of actions taken by the agent
        /// </summary>
        public IReadOnlyList<AgentAction> ActionHistory => _actionHistory.AsReadOnly();

        public AgentOrchestrator(string workspacePath)
        {
            _workspacePath = workspacePath;
        }
        
        /// <summary>
        /// Undo the last file write action
        /// </summary>
        public async Task<string> UndoLastActionAsync()
        {
            // Find the last undoable action (file writes)
            for (int i = _actionHistory.Count - 1; i >= 0; i--)
            {
                var action = _actionHistory[i];
                if (action.Tool == "write_file" && !string.IsNullOrEmpty(action.UndoData))
                {
                    var path = action.Params.GetValueOrDefault("path")?.ToString();
                    if (!string.IsNullOrEmpty(path))
                    {
                        var fullPath = Path.Combine(_workspacePath, path);
                        if (action.UndoData == "__NEW_FILE__")
                        {
                            // File was newly created - delete it
                            if (File.Exists(fullPath))
                            {
                                File.Delete(fullPath);
                                _actionHistory.RemoveAt(i);
                                return $"✅ Undone: Deleted newly created file `{path}`";
                            }
                        }
                        else
                        {
                            // Restore original content
                            await File.WriteAllTextAsync(fullPath, action.UndoData);
                            _actionHistory.RemoveAt(i);
                            return $"✅ Undone: Restored `{path}` to previous version";
                        }
                    }
                }
                else if (action.Tool == "delete_file" && !string.IsNullOrEmpty(action.UndoData))
                {
                    var path = action.Params.GetValueOrDefault("path")?.ToString();
                    if (!string.IsNullOrEmpty(path))
                    {
                        var fullPath = Path.Combine(_workspacePath, path);
                        await File.WriteAllTextAsync(fullPath, action.UndoData);
                        _actionHistory.RemoveAt(i);
                        return $"✅ Undone: Restored deleted file `{path}`";
                    }
                }
            }
            return "❌ No undoable actions found";
        }
        
        /// <summary>
        /// Get a summary of recent actions
        /// </summary>
        public string GetActionSummary(int count = 5)
        {
            if (_actionHistory.Count == 0)
                return "No agent actions recorded yet.";
            
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("📋 **Recent Agent Actions:**\n");
            
            var recent = _actionHistory.TakeLast(count).Reverse();
            foreach (var action in recent)
            {
                var status = action.Success ? "✅" : "❌";
                var time = action.Timestamp.ToString("HH:mm:ss");
                sb.AppendLine($"{status} `{action.Tool}` at {time}");
                if (action.Params.TryGetValue("path", out var path))
                    sb.AppendLine($"   📄 {path}");
            }
            
            return sb.ToString();
        }
        
        /// <summary>
        /// Generate a human-readable description of a tool call for confirmation dialogs
        /// </summary>
        private string GetToolDescription(ToolCall toolCall)
        {
            return toolCall.Tool.ToLower() switch
            {
                "delete_file" => $"Delete file: {toolCall.Params.GetValueOrDefault("path")}",
                "uninstall_software" or "uninstall" => $"Uninstall software: {toolCall.Params.GetValueOrDefault("name")}",
                "run_command" => $"Run command: {toolCall.Params.GetValueOrDefault("command")}",
                "run_powershell" => $"Run PowerShell: {toolCall.Params.GetValueOrDefault("script")?.ToString()?.Substring(0, Math.Min(100, toolCall.Params.GetValueOrDefault("script")?.ToString()?.Length ?? 0))}...",
                _ => $"{toolCall.Tool}: {string.Join(", ", toolCall.Params.Select(p => $"{p.Key}={p.Value}"))}"
            };
        }

        /// <summary>
        /// Run the agent on a task - it will use tools until complete
        /// </summary>
        public async Task<string> RunAsync(string userRequest, CancellationToken ct = default)
        {
            // Add system prompt with tool definitions
            if (_conversationHistory.Count == 0)
            {
                _conversationHistory.Add(new AgentMessage
                {
                    Role = "system",
                    Content = GetSystemPrompt()
                });
            }

            var intakeContext = await BuildTaskIntakeContextAsync(userRequest, ct);
            if (!string.IsNullOrWhiteSpace(intakeContext))
            {
                _conversationHistory.Add(new AgentMessage
                {
                    Role = "system",
                    Content = intakeContext
                });
            }

            // Add user request
            _conversationHistory.Add(new AgentMessage { Role = "user", Content = userRequest });

            var iterations = 0;
            string finalResponse = "";

            while (iterations < MaxIterations)
            {
                iterations++;
                ct.ThrowIfCancellationRequested();
                
                OnThinking?.Invoke(this, $"Thinking... (step {iterations})");

                // Call AI with timeout
                var messages = ConvertToApiFormat();
                
                AIResponse response;
                try
                {
                    var routingRequest = BuildRoutingRequest(userRequest, messages, _workspacePath);

                    // Use a linked CTS with 45s timeout and external cancellation
                    using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(45));
                    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
                    
                    response = await AIManager.SendMessageAsync(routingRequest, linkedCts.Token);
                    
                    if (timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
                    {
                        OnError?.Invoke(this, "AI request timed out after 45 seconds");
                        return "⏱️ AI request timed out. The model may be overloaded. Please try again.";
                    }
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Agent] AI call exception: {ex.Message}");
                    OnError?.Invoke(this, $"AI error: {ex.Message}");
                    return $"❌ AI error: {ex.Message}";
                }

                if (!response.Success || string.IsNullOrEmpty(response.Content))
                {
                    var error = $"AI error: {response.Error ?? "No response"}";
                    OnError?.Invoke(this, error);
                    return error;
                }

                LastProviderUsed = response.Provider.ToString();
                LastModelUsed = response.Model ?? "";
                LastTaskBucketUsed = response.TaskBucket ?? "";

                var aiContent = response.Content;
                Debug.WriteLine($"[Agent] AI response: {aiContent.Substring(0, Math.Min(200, aiContent.Length))}...");

                // Check for tool call
                var toolCall = ExtractToolCall(aiContent);
                
                if (toolCall != null)
                {
                    Debug.WriteLine($"[Agent] Tool call detected: {toolCall.Tool}");
                    
                    // Check if this is a destructive operation that needs confirmation
                    if (DestructiveTools.Contains(toolCall.Tool))
                    {
                        Debug.WriteLine($"[Agent] Destructive tool detected: {toolCall.Tool}, OnConfirmationRequired is {(OnConfirmationRequired != null ? "set" : "null")}");
                        
                        if (OnConfirmationRequired != null)
                        {
                            var description = GetToolDescription(toolCall);
                            Debug.WriteLine($"[Agent] Requesting confirmation for: {description}");
                            var confirmed = await OnConfirmationRequired(toolCall.Tool, description);
                            
                            if (!confirmed)
                            {
                                Debug.WriteLine($"[Agent] User cancelled {toolCall.Tool}");
                                // User cancelled - add to history and continue
                                _conversationHistory.Add(new AgentMessage { Role = "assistant", Content = aiContent });
                                _conversationHistory.Add(new AgentMessage 
                                { 
                                    Role = "user", 
                                    Content = $"⚠️ User cancelled the {toolCall.Tool} operation. Please continue without this action or suggest an alternative." 
                                });
                                continue;
                            }
                            Debug.WriteLine($"[Agent] User confirmed {toolCall.Tool}");
                        }
                    }
                    
                    // Execute the tool
                    OnToolExecuting?.Invoke(this, $"🔧 {GetToolDescription(toolCall)}");
                    
                    // Capture undo data before execution
                    string? undoData = null;
                    if (toolCall.Tool == "write_file" || toolCall.Tool == "delete_file")
                    {
                        var path = toolCall.Params.GetValueOrDefault("path")?.ToString();
                        if (!string.IsNullOrEmpty(path))
                        {
                            var fullPath = Path.Combine(_workspacePath, path);
                            if (File.Exists(fullPath))
                                undoData = await File.ReadAllTextAsync(fullPath);
                            else if (toolCall.Tool == "write_file")
                                undoData = "__NEW_FILE__"; // Mark as newly created
                        }
                    }
                    
                    var result = await AgentTools.ExecuteToolAsync(toolCall.Tool, toolCall.Params, _workspacePath, ct);
                    OnToolResult?.Invoke(this, result);
                    
                    // Track action in history
                    _actionHistory.Add(new AgentAction
                    {
                        Tool = toolCall.Tool,
                        Params = toolCall.Params,
                        Result = result.Output,
                        Success = result.Success,
                        UndoData = undoData
                    });

                    // Add AI response and tool result to history
                    _conversationHistory.Add(new AgentMessage { Role = "assistant", Content = aiContent });

                    var toolOutput = result.Output ?? "";
                    try
                    {
                        toolOutput = toolOutput.Trim();
                        // Keep tool result context useful but bounded (token safety)
                        var maxToolOutputChars = 1800;
                        try
                        {
                            if (string.Equals(toolCall.Tool, "read_file", StringComparison.OrdinalIgnoreCase))
                                maxToolOutputChars = 12000;
                            else if (string.Equals(toolCall.Tool, "search_content", StringComparison.OrdinalIgnoreCase))
                                maxToolOutputChars = 6000;
                        }
                        catch
                        {
                        }
                        if (toolOutput.Length > maxToolOutputChars)
                            toolOutput = toolOutput.Substring(0, maxToolOutputChars) + "\n…(truncated)";
                    }
                    catch
                    {
                        toolOutput = result.Output ?? "";
                    }

                    _conversationHistory.Add(new AgentMessage 
                    { 
                        Role = "user", 
                        Content = $"Tool result ({toolCall.Tool}):\n{toolOutput}" 
                    });

                    // Continue the loop - AI will process the result
                    continue;
                }
                else
                {
                    // No tool call - this is the final response
                    finalResponse = aiContent;
                    _conversationHistory.Add(new AgentMessage { Role = "assistant", Content = aiContent });
                    OnResponse?.Invoke(this, finalResponse);
                    break;
                }
            }

            if (iterations >= MaxIterations)
            {
                finalResponse = "⚠️ Reached maximum iterations. Task may be incomplete.";
                OnError?.Invoke(this, finalResponse);
            }

            return finalResponse;
        }

        internal static AIManager.AIRoutingRequest BuildRoutingRequest(string userRequest, List<object> messages, string workspacePath)
        {
            var profile = BuildRoutingProfile(userRequest);
            return new AIManager.AIRoutingRequest
            {
                Module = profile.Module,
                Messages = messages,
                MaxTokens = 4096,
                BucketHint = profile.BucketHint,
                RuntimeContext = new AIManager.AIRuntimeContext
                {
                    ActiveModule = profile.Module,
                    ActivePage = profile.ActivePage,
                    WorkspacePath = workspacePath,
                    ToolContext = profile.ToolContext,
                    AdditionalInstructions = profile.AdditionalInstructions,
                },
            };
        }

        internal static AgentRoutingProfile BuildRoutingProfile(string userRequest)
        {
            if (LooksLikeCodeCentricTask(userRequest))
            {
                return new AgentRoutingProfile(
                    "tool_runtime_code",
                    "code",
                    AIManager.AITaskBucket.Code,
                    "Agent tools can inspect files, read code, edit files, run commands, query Atlas internal APIs, search the web for current facts, and execute multi-step code or file-analysis tasks.",
                    "This is an active code, refactor, debug, or file-analysis task. Use the available tools and workspace context instead of answering generically, and use web search when freshness matters.");
            }

            return new AgentRoutingProfile(
                "tool_runtime",
                "tools",
                null,
                "Agent tools can inspect files, run commands, call Atlas internal APIs, search the web for current information, and execute multi-step workspace or system tasks.",
                "This is an active tool-using task. Let normal routing choose the best model for the intent, use the available tools and workspace context instead of answering generically, and use web search when freshness matters.");
        }

        private static bool LooksLikeCodeCentricTask(string userRequest)
        {
            var text = (userRequest ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(text))
                return false;

            return Regex.IsMatch(text,
                @"\b(code|coding|repo|repository|workspace|solution|project|compile|build failure|debug|stack trace|exception|patch|implement|implementation|refactor|class|method|function|variable|xaml|csproj|sln|json|yaml|xml|markdown|file analysis|inspect (?:this |the )?(?:repo|repository|file|workspace)|read (?:this |the )?(?:file|repo)|bug|fix|test|unit test|integration test|diff|root cause|log file)\b",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
        }

        /// <summary>
        /// Extract a tool call from AI response
        /// </summary>
        private ToolCall? ExtractToolCall(string content)
        {
            return TryExtractToolCall(content);
        }

        internal static ToolCall? TryExtractToolCall(string content)
        {
            Debug.WriteLine($"[Agent] ExtractToolCall parsing content length: {content.Length}");
            
            // Look for ```tool ... ``` blocks (most common format)
            var toolBlockMatch = Regex.Match(content, @"```tool\s*\n?({[\s\S]*?})\s*```", RegexOptions.IgnoreCase);
            if (toolBlockMatch.Success)
            {
                Debug.WriteLine($"[Agent] Found ```tool block: {toolBlockMatch.Groups[1].Value}");
                return ParseToolJson(toolBlockMatch.Groups[1].Value);
            }

            // Try ```json blocks that look like tool calls
            var jsonBlockMatch = Regex.Match(content, @"```json\s*\n?({[\s\S]*?""tool""[\s\S]*?})\s*```", RegexOptions.IgnoreCase);
            if (jsonBlockMatch.Success)
            {
                Debug.WriteLine($"[Agent] Found ```json block with tool: {jsonBlockMatch.Groups[1].Value}");
                return ParseToolJson(jsonBlockMatch.Groups[1].Value);
            }
            
            // Try ``` blocks without language specifier
            var plainBlockMatch = Regex.Match(content, @"```\s*\n?({[\s\S]*?""tool""[\s\S]*?})\s*```", RegexOptions.IgnoreCase);
            if (plainBlockMatch.Success)
            {
                Debug.WriteLine($"[Agent] Found plain ``` block with tool: {plainBlockMatch.Groups[1].Value}");
                return ParseToolJson(plainBlockMatch.Groups[1].Value);
            }

            // Try inline JSON with "tool" key (nested params)
            var nestedMatch = Regex.Match(content, @"\{\s*""tool""\s*:\s*""([^""]+)""\s*,\s*""params""\s*:\s*(\{[^}]+\})\s*\}", RegexOptions.IgnoreCase);
            if (nestedMatch.Success)
            {
                Debug.WriteLine($"[Agent] Found nested JSON tool call");
                return ParseToolJson(nestedMatch.Value);
            }
            
            // Try flat JSON with "tool" key
            var flatMatch = Regex.Match(content, @"\{\s*""tool""\s*:\s*""[^""]+""[^}]*\}", RegexOptions.IgnoreCase);
            if (flatMatch.Success)
            {
                Debug.WriteLine($"[Agent] Found flat JSON tool call: {flatMatch.Value}");
                return ParseToolJson(flatMatch.Value);
            }
            
            // FALLBACK: Try to infer tool call from natural language response
            var inferredTool = InferToolFromText(content);
            if (inferredTool != null)
            {
                Debug.WriteLine($"[Agent] Inferred tool call from text: {inferredTool.Tool}");
                return inferredTool;
            }
            
            // Check if AI is trying to use a tool but in wrong format
            if (content.Contains("\"tool\"") || content.Contains("'tool'"))
            {
                Debug.WriteLine($"[Agent] WARNING: Content contains 'tool' but no valid JSON found. Content preview: {content.Substring(0, Math.Min(500, content.Length))}");
            }

            Debug.WriteLine($"[Agent] No tool call found in response");
            return null;
        }
        
        /// <summary>
        /// Try to infer a tool call from natural language when AI doesn't use proper format
        /// </summary>
        internal static ToolCall? InferToolFromText(string content)
        {
            var lower = content.ToLowerInvariant();
            
            // Check for list directory patterns
            if (lower.Contains("list") && (lower.Contains("file") || lower.Contains("director") || lower.Contains("folder")))
            {
                return new ToolCall
                {
                    Tool = "list_directory",
                    Params = new Dictionary<string, object>
                    {
                        { "path", "." },
                        { "recursive", false }
                    }
                };
            }

            // Check for read file patterns
            if ((lower.Contains("read") || lower.Contains("open") || lower.Contains("show")) &&
                (lower.Contains(" file") || lower.Contains(".cs") || lower.Contains(".ts") || lower.Contains(".json") || lower.Contains(".md")))
            {
                var path = ExtractPathHint(content) ?? ".";
                return new ToolCall
                {
                    Tool = "read_file",
                    Params = new Dictionary<string, object>
                    {
                        { "path", path }
                    }
                };
            }

            // Check for search patterns
            if (lower.Contains("search") || lower.Contains("find"))
            {
                if (lower.Contains("in file") || lower.Contains("content") || lower.Contains("text"))
                {
                    return new ToolCall
                    {
                        Tool = "search_content",
                        Params = new Dictionary<string, object>
                        {
                            { "query", ExtractSearchQuery(content) ?? "" },
                            { "path", "." },
                            { "file_pattern", "*" }
                        }
                    };
                }

                return new ToolCall
                {
                    Tool = "search_files",
                    Params = new Dictionary<string, object>
                    {
                        { "pattern", ExtractSearchQuery(content) ?? "*" },
                        { "path", "." }
                    }
                };
            }

            // Low-risk mutating inference: create a directory/folder
            if (Regex.IsMatch(lower, @"\b(create|make)\s+(folder|directory)\b", RegexOptions.IgnoreCase))
            {
                var dirPath = ExtractPathHint(content) ?? "new-folder";
                return new ToolCall
                {
                    Tool = "create_directory",
                    Params = new Dictionary<string, object>
                    {
                        { "path", dirPath }
                    }
                };
            }

            // Low-risk mutating inference: create/write file
            if (Regex.IsMatch(lower, @"\b(create|write|save)\s+(a\s+)?file\b", RegexOptions.IgnoreCase))
            {
                var filePath = ExtractPathHint(content) ?? "new-file.txt";
                var code = ExtractCodeBlock(content) ?? string.Empty;
                return new ToolCall
                {
                    Tool = "write_file",
                    Params = new Dictionary<string, object>
                    {
                        { "path", filePath },
                        { "content", code }
                    }
                };
            }

            if (LooksLikeRejectedMutatingInference(lower))
            {
                Debug.WriteLine("[Agent] Rejected natural-language mutating tool inference; structured tool call required.");
                return null;
            }
            
            return null;
        }

        private static string? ExtractPathHint(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
                return null;

            var quoted = Regex.Match(content, @"['""]([^'""]+)['""]");
            if (quoted.Success)
                return quoted.Groups[1].Value.Trim();

            var pathLike = Regex.Match(content, @"\b([\w\-./\\ ]+\.[A-Za-z0-9]{1,8})\b");
            if (pathLike.Success)
                return pathLike.Groups[1].Value.Trim();

            return null;
        }

        private static string? ExtractSearchQuery(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
                return null;

            var quoted = Regex.Match(content, @"['""]([^'""]+)['""]");
            if (quoted.Success)
                return quoted.Groups[1].Value.Trim();

            var trimmed = Regex.Replace(content, @"\s+", " ").Trim();
            if (trimmed.Length > 120)
                trimmed = trimmed.Substring(0, 120);
            return trimmed;
        }

        private static string? ExtractCodeBlock(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
                return null;

            var match = Regex.Match(content, @"```[\w-]*\s*\n([\s\S]*?)\n```", RegexOptions.IgnoreCase);
            if (match.Success)
                return match.Groups[1].Value;

            return null;
        }

        private static bool LooksLikeRejectedMutatingInference(string lowerContent)
        {
            if (string.IsNullOrWhiteSpace(lowerContent))
                return false;

            return Regex.IsMatch(lowerContent,
                @"\b(create|write|save|patch|modify|edit|update|append|install|delete|remove|rm|move|rename|run|execute)\b",
                RegexOptions.IgnoreCase);
        }

        private static ToolCall? ParseToolJson(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (!root.TryGetProperty("tool", out var toolProp))
                    return null;

                var toolCall = new ToolCall { Tool = toolProp.GetString() ?? "" };

                if (root.TryGetProperty("params", out var paramsProp))
                {
                    foreach (var prop in paramsProp.EnumerateObject())
                    {
                        toolCall.Params[prop.Name] = prop.Value.ValueKind switch
                        {
                            JsonValueKind.String => prop.Value.GetString() ?? "",
                            JsonValueKind.Number => prop.Value.GetDouble(),
                            JsonValueKind.True => true,
                            JsonValueKind.False => false,
                            _ => prop.Value.ToString()
                        };
                    }
                }
                // Also check for flat structure (tool + other params at same level)
                else
                {
                    foreach (var prop in root.EnumerateObject())
                    {
                        if (prop.Name == "tool") continue;
                        toolCall.Params[prop.Name] = prop.Value.ValueKind switch
                        {
                            JsonValueKind.String => prop.Value.GetString() ?? "",
                            JsonValueKind.Number => prop.Value.GetDouble(),
                            JsonValueKind.True => true,
                            JsonValueKind.False => false,
                            _ => prop.Value.ToString()
                        };
                    }
                }

                if (MutatingInferredTools.Contains(toolCall.Tool) || string.IsNullOrWhiteSpace(toolCall.Tool))
                    return toolCall;

                return toolCall;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Agent] Failed to parse tool JSON: {ex.Message}");
                return null;
            }
        }

        private List<object> ConvertToApiFormat()
        {
            // Token safety: keep the system prompt plus a limited tail of recent messages.
            // This prevents runaway context growth during multi-iteration tool loops.
            const int MaxNonSystemMessages = 14;

            var messages = new List<object>();
            try
            {
                var system = _conversationHistory.FirstOrDefault(m => string.Equals(m.Role, "system", StringComparison.OrdinalIgnoreCase));
                if (system != null)
                    messages.Add(new { role = system.Role, content = system.Content });

                var tail = _conversationHistory
                    .Where(m => !string.Equals(m.Role, "system", StringComparison.OrdinalIgnoreCase))
                    .TakeLast(MaxNonSystemMessages);

                foreach (var msg in tail)
                    messages.Add(new { role = msg.Role, content = msg.Content });
            }
            catch
            {
                foreach (var msg in _conversationHistory)
                    messages.Add(new { role = msg.Role, content = msg.Content });
            }

            return messages;
        }

        private string GetSystemPrompt()
        {
            var rolePrefix = string.IsNullOrWhiteSpace(SystemPromptPrefix)
                ? "You are Atlas, an AI agent. You execute tasks using tools. You should avoid overusing tools and respond normally when no tool is needed."
                : SystemPromptPrefix + "\n\nYou execute tasks using tools.";
            return $@"{rolePrefix}

    WORKSPACE: {_workspacePath}

    TOOLS AVAILABLE:
    - write_file: Create files. Params: path, content
    - read_file: Read files. Params: path
    - delete_file: Delete files. Params: path
    - list_directory: List folder. Params: path, recursive
    - search_files: Find files. Params: pattern, path
    - search_content: Search inside files. Params: query, path, file_pattern
    - web_search: Search the web for current or uncertain information. Params: query
    - atlas_api: Call Atlas internal read-only APIs. Params: operation, id(optional)
    - install_software: Install apps. Params: name
    - run_command: Run shell commands. Params: command

    HOW TO USE TOOLS:
    When you decide a tool is necessary, respond with ONLY this format:
    ```tool
    {{""tool"": ""write_file"", ""params"": {{""path"": ""test.py"", ""content"": ""print('hi')""}}}}
    ```

    If no tool is needed (e.g., the user asked for an explanation, an audit/review, or a plan), respond normally in plain text.

     REQUIRED WORKFLOWS:
     1. Folder intake workflow:
         - inspect the folder before proposing changes
         - identify project type, likely entry points, and high-level architecture
         - use list_directory recursively only when needed, then narrow with search_files/search_content/read_file
         - summarize the architecture briefly before editing

     2. File or refactor workflow:
         - read the relevant files first
         - build a patch plan before writing
         - make minimal safe edits instead of broad rewrites
         - after edits, report the exact files changed

     3. Editing discipline:
         - for code/debug/refactor/file-analysis tasks, inspect before patching
         - prefer small targeted file writes that preserve existing behavior outside the requested change
         - when a request is ambiguous, gather more file context first instead of guessing

     4. Current-information workflow:
         - when the request depends on current docs, versions, pricing, release notes, or recent events, use web_search before answering
         - when the request asks about Atlas runtime status or integrations, use atlas_api before answering
         - ground the answer in the returned evidence instead of guessing

    EXAMPLES:
    User: create hello.py
    ```tool
    {{""tool"": ""write_file"", ""params"": {{""path"": ""hello.py"", ""content"": ""print('Hello!')""}}}}
    ```

    User: list files
    ```tool
    {{""tool"": ""list_directory"", ""params"": {{""path"": ""."", ""recursive"": false}}}}
    ```

    User: find divinity folders
    ```tool
    {{""tool"": ""search_files"", ""params"": {{""pattern"": ""divinity"", ""path"": ""C:\\Users\\{Environment.UserName}""}}}}
    ```

    RULES:
    1. Use tools only when needed to get facts or change files; otherwise answer in plain text.
    2. Prefer narrow tools first: search_files/read_file over list_directory after the first intake pass.
    3. Avoid noisy output: do NOT dump huge directories into the reply; summarize structure instead.
    4. Never patch files you have not inspected first for the current task.
    5. Never run builds/tests unless the user asked, or you changed code and need verification.
    6. Final implementation answers must include the exact files changed.
    7. Desktop: C:\Users\{Environment.UserName}\Desktop
    8. Documents: C:\Users\{Environment.UserName}\Documents";
        }

        private async Task<string> BuildTaskIntakeContextAsync(string userRequest, CancellationToken ct)
        {
            try
            {
                var profile = DetectTaskProfile(userRequest);
                if (profile == AgentTaskProfile.General)
                    return string.Empty;

                var sb = new StringBuilder();
                sb.AppendLine("TASK INTAKE CONTEXT");
                sb.AppendLine($"Profile: {profile}");
                sb.AppendLine($"Workspace: {_workspacePath}");

                if (profile == AgentTaskProfile.FolderIntake || profile == AgentTaskProfile.FileOrRefactor)
                {
                    var fileIndex = await BuildRecursiveFileIndexAsync(ct);
                    if (!string.IsNullOrWhiteSpace(fileIndex))
                    {
                        sb.AppendLine();
                        sb.AppendLine("Recursive file index:");
                        sb.AppendLine(fileIndex);
                    }

                    var projectType = DetectProjectType();
                    var entryPoints = FindEntryPoints();
                    var architecture = SummarizeArchitecture(projectType, entryPoints);

                    sb.AppendLine();
                    sb.AppendLine($"Detected project type: {projectType}");
                    sb.AppendLine("Likely entry points:");
                    sb.AppendLine(string.IsNullOrWhiteSpace(entryPoints) ? "- none identified" : entryPoints);
                    sb.AppendLine("Architecture summary:");
                    sb.AppendLine(architecture);
                }

                if (profile == AgentTaskProfile.FileOrRefactor)
                {
                    var relevantFiles = FindRelevantFiles(userRequest);
                    if (relevantFiles.Count > 0)
                    {
                        sb.AppendLine();
                        sb.AppendLine("Relevant file previews:");
                        foreach (var file in relevantFiles)
                        {
                            ct.ThrowIfCancellationRequested();
                            sb.AppendLine($"FILE: {file}");
                            sb.AppendLine(await ReadFilePreviewAsync(file, ct));
                        }
                    }

                    sb.AppendLine();
                    sb.AppendLine("Required patch workflow:");
                    sb.AppendLine("1. Inspect relevant files first.");
                    sb.AppendLine("2. State a patch plan.");
                    sb.AppendLine("3. Make minimal safe edits.");
                    sb.AppendLine("4. Report exact files changed.");
                }

                return sb.ToString().Trim();
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Agent] Intake context failed: {ex.Message}");
                return string.Empty;
            }
        }

        private AgentTaskProfile DetectTaskProfile(string userRequest)
        {
            var text = (userRequest ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(text))
                return AgentTaskProfile.General;

            var lower = text.ToLowerInvariant();
            if (Regex.IsMatch(lower, @"\b(folder|directory|project|repo|repository|workspace|solution|codebase|architecture|entry point)\b", RegexOptions.IgnoreCase))
                return AgentTaskProfile.FolderIntake;

            if (Regex.IsMatch(lower, @"\b(file|refactor|patch|modify|edit|rewrite|rename|cleanup|debug|fix|analyze|analyse|inspect)\b", RegexOptions.IgnoreCase) ||
                Regex.IsMatch(text, @"\.(cs|xaml|json|xml|ts|tsx|js|jsx|py|md|yml|yaml|csproj|slnx?)\b", RegexOptions.IgnoreCase))
                return AgentTaskProfile.FileOrRefactor;

            return AgentTaskProfile.General;
        }

        private async Task<string> BuildRecursiveFileIndexAsync(CancellationToken ct)
        {
            var extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".cs", ".xaml", ".csproj", ".sln", ".slnx", ".json", ".xml", ".yml", ".yaml", ".md", ".ts", ".tsx", ".js", ".jsx", ".py"
            };

            var lines = new List<string>();
            foreach (var file in Directory.EnumerateFiles(_workspacePath, "*", SearchOption.AllDirectories))
            {
                ct.ThrowIfCancellationRequested();

                if (ShouldSkipPath(file))
                    continue;

                var ext = Path.GetExtension(file);
                if (!extensions.Contains(ext))
                    continue;

                lines.Add(Path.GetRelativePath(_workspacePath, file).Replace('\\', '/'));
                if (lines.Count >= 200)
                    break;
            }

            if (lines.Count == 0)
                return string.Empty;

            return string.Join("\n", lines);
        }

        private string DetectProjectType()
        {
            try
            {
                if (File.Exists(Path.Combine(_workspacePath, "AtlasAI.csproj")))
                    return ".NET WPF desktop application";
                if (Directory.EnumerateFiles(_workspacePath, "package.json", SearchOption.TopDirectoryOnly).Any())
                    return "Node.js application";
                if (Directory.EnumerateFiles(_workspacePath, "*.sln", SearchOption.TopDirectoryOnly).Any() || Directory.EnumerateFiles(_workspacePath, "*.slnx", SearchOption.TopDirectoryOnly).Any())
                    return ".NET solution";
            }
            catch
            {
            }

            return "mixed workspace";
        }

        private string FindEntryPoints()
        {
            var candidates = new List<string>();
            try
            {
                var commonFiles = new[]
                {
                    "App.xaml",
                    "App.xaml.cs",
                    "MainWindow.xaml",
                    "MainWindow.xaml.cs",
                    "Program.cs",
                    "AtlasAI.csproj",
                    "AtlasAI.slnx"
                };

                foreach (var file in commonFiles)
                {
                    var fullPath = Path.Combine(_workspacePath, file);
                    if (File.Exists(fullPath))
                        candidates.Add(file.Replace('\\', '/'));
                }
            }
            catch
            {
            }

            return candidates.Count == 0 ? string.Empty : string.Join("\n", candidates.Select(x => $"- {x}"));
        }

        private string SummarizeArchitecture(string projectType, string entryPoints)
        {
            var parts = new List<string> { $"Project looks like {projectType}." };

            if (!string.IsNullOrWhiteSpace(entryPoints))
                parts.Add("Entry points were identified from app/bootstrap files and solution metadata.");

            try
            {
                var topDirs = Directory.EnumerateDirectories(_workspacePath)
                    .Select(Path.GetFileName)
                    .Where(name => !string.IsNullOrWhiteSpace(name) && !ShouldSkipDirectoryName(name!))
                    .Take(8)
                    .ToList();

                if (topDirs.Count > 0)
                    parts.Add("Top-level modules: " + string.Join(", ", topDirs));
            }
            catch
            {
            }

            return string.Join(" ", parts);
        }

        private List<string> FindRelevantFiles(string userRequest)
        {
            var text = userRequest ?? string.Empty;
            var requestedNames = Regex.Matches(text, @"[A-Za-z0-9_\-./\\]+\.(cs|xaml|json|xml|ts|tsx|js|jsx|py|md|yml|yaml|csproj|slnx?)", RegexOptions.IgnoreCase)
                .Select(m => m.Value.Replace('\\', '/'))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(6)
                .ToList();

            var results = new List<string>();
            foreach (var requested in requestedNames)
            {
                var direct = Path.Combine(_workspacePath, requested.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(direct))
                {
                    results.Add(Path.GetRelativePath(_workspacePath, direct).Replace('\\', '/'));
                    continue;
                }

                var fileName = Path.GetFileName(requested);
                if (string.IsNullOrWhiteSpace(fileName))
                    continue;

                try
                {
                    var match = Directory.EnumerateFiles(_workspacePath, fileName, SearchOption.AllDirectories)
                        .FirstOrDefault(path => !ShouldSkipPath(path));
                    if (!string.IsNullOrWhiteSpace(match))
                        results.Add(Path.GetRelativePath(_workspacePath, match).Replace('\\', '/'));
                }
                catch
                {
                }
            }

            return results.Distinct(StringComparer.OrdinalIgnoreCase).Take(4).ToList();
        }

        private async Task<string> ReadFilePreviewAsync(string relativePath, CancellationToken ct)
        {
            try
            {
                var fullPath = Path.Combine(_workspacePath, relativePath.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(fullPath))
                    return "(missing)";

                var content = await File.ReadAllTextAsync(fullPath, ct);
                content = content.Replace("\r\n", "\n");
                if (content.Length > 2500)
                    content = content.Substring(0, 2500) + "\n...(truncated)";
                return content.Trim();
            }
            catch (Exception ex)
            {
                return $"(preview failed: {ex.Message})";
            }
        }

        private bool ShouldSkipPath(string path)
        {
            var normalized = path.Replace('/', '\\');
            return normalized.Contains("\\bin\\", StringComparison.OrdinalIgnoreCase) ||
                   normalized.Contains("\\obj\\", StringComparison.OrdinalIgnoreCase) ||
                   normalized.Contains("\\node_modules\\", StringComparison.OrdinalIgnoreCase) ||
                   normalized.Contains("\\.git\\", StringComparison.OrdinalIgnoreCase) ||
                   normalized.Contains("\\dist\\", StringComparison.OrdinalIgnoreCase);
        }

        private static bool ShouldSkipDirectoryName(string name)
        {
            return name.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("obj", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("node_modules", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals(".git", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("dist", StringComparison.OrdinalIgnoreCase);
        }

        private enum AgentTaskProfile
        {
            General,
            FolderIntake,
            FileOrRefactor,
        }

        public void ClearHistory()
        {
            _conversationHistory.Clear();
        }
    }

    public class AgentMessage
    {
        public string Role { get; set; } = "";
        public string Content { get; set; } = "";
    }
}
