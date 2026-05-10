using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AtlasAI.AI;

namespace AtlasAI.Agent
{
    /// <summary>
    /// Intelligent code assistant - can write, modify, and understand code like Kiro
    /// Uses AI to generate high-quality code with proper patterns
    /// </summary>
    public static class CodeAssistant
    {
        /// <summary>
        /// Generate code based on user request
        /// </summary>
        public static async Task<CodeGenerationResult> GenerateCodeAsync(string request, string? language = null, string? context = null, System.Threading.CancellationToken ct = default)
        {
            var prompt = BuildCodePrompt(request, language, context);
            
            var messages = new List<object>
            {
                new { role = "system", content = GetCodeSystemPrompt() },
                new { role = "user", content = prompt }
            };

            var response = await SendCodeRequestAsync(messages, 4000, language, context, ct);
            
            if (!response.Success || string.IsNullOrEmpty(response.Content))
            {
                return new CodeGenerationResult 
                { 
                    Success = false, 
                    Error = response.Error ?? "Failed to generate code" 
                };
            }

            return ParseCodeResponse(response.Content);
        }

        /// <summary>
        /// Modify existing code based on instructions
        /// </summary>
        public static async Task<CodeGenerationResult> ModifyCodeAsync(string existingCode, string instructions, string? language = null, System.Threading.CancellationToken ct = default)
        {
            var prompt = $@"Modify this code according to the instructions.

**Existing Code:**
```{language ?? ""}
{existingCode}
```

**Instructions:** {instructions}

Provide the complete modified code.";

            var messages = new List<object>
            {
                new { role = "system", content = GetCodeSystemPrompt() },
                new { role = "user", content = prompt }
            };

            var response = await SendCodeRequestAsync(messages, 4000, language, instructions, ct);
            
            if (!response.Success || string.IsNullOrEmpty(response.Content))
            {
                return new CodeGenerationResult 
                { 
                    Success = false, 
                    Error = response.Error ?? "Failed to modify code" 
                };
            }

            return ParseCodeResponse(response.Content);
        }

        /// <summary>
        /// Explain code
        /// </summary>
        public static async Task<string> ExplainCodeAsync(string code, string? language = null, System.Threading.CancellationToken ct = default)
        {
            var prompt = $@"Explain this code in simple terms:

```{language ?? ""}
{code}
```

Explain:
1. What it does overall
2. Key parts and how they work
3. Any important patterns or techniques used";

            var messages = new List<object>
            {
                new { role = "user", content = prompt }
            };

            var response = await SendCodeRequestAsync(messages, 2000, language, "Explain the provided code", ct);
            return response.Success ? response.Content ?? "Unable to explain" : "Failed to analyze code";
        }

        /// <summary>
        /// Fix code errors
        /// </summary>
        public static async Task<CodeGenerationResult> FixCodeAsync(string code, string? errorMessage = null, string? language = null, System.Threading.CancellationToken ct = default)
        {
            var prompt = $@"Fix the errors in this code:

```{language ?? ""}
{code}
```
{(errorMessage != null ? $"\n**Error:** {errorMessage}" : "")}

Provide the corrected code with comments explaining what was fixed.";

            var messages = new List<object>
            {
                new { role = "system", content = GetCodeSystemPrompt() },
                new { role = "user", content = prompt }
            };

            var response = await SendCodeRequestAsync(messages, 4000, language, errorMessage, ct);
            
            if (!response.Success || string.IsNullOrEmpty(response.Content))
            {
                return new CodeGenerationResult 
                { 
                    Success = false, 
                    Error = response.Error ?? "Failed to fix code" 
                };
            }

            return ParseCodeResponse(response.Content);
        }

        /// <summary>
        /// Create a complete file with code
        /// </summary>
        public static async Task<string> CreateFileAsync(string filePath, string description, string? workspacePath = null, System.Threading.CancellationToken ct = default)
        {
            var extension = Path.GetExtension(filePath).ToLower();
            var language = GetLanguageFromExtension(extension);
            
            var result = await GenerateCodeAsync(description, language, ct: ct);
            
            if (!result.Success || string.IsNullOrEmpty(result.Code))
            {
                return $"❌ Failed to generate code: {result.Error}";
            }

            // Resolve path
            var fullPath = filePath;
            if (!Path.IsPathRooted(filePath) && !string.IsNullOrEmpty(workspacePath))
            {
                fullPath = Path.Combine(workspacePath, filePath);
            }

            // Create directory if needed
            var dir = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            await File.WriteAllTextAsync(fullPath, result.Code);
            
            return $"Action: create file\nStatus: completed\nPath: {filePath}\nLanguage: {language}\n\n```{language}\n{result.Code}\n```";
        }

        /// <summary>
        /// Refactor code for better quality
        /// </summary>
        public static async Task<CodeGenerationResult> RefactorCodeAsync(string code, string? language = null, System.Threading.CancellationToken ct = default)
        {
            var prompt = $@"Refactor this code to improve:
- Readability
- Performance
- Best practices
- Error handling

```{language ?? ""}
{code}
```

Provide the refactored code with comments explaining improvements.";

            var messages = new List<object>
            {
                new { role = "system", content = GetCodeSystemPrompt() },
                new { role = "user", content = prompt }
            };

            var response = await SendCodeRequestAsync(messages, 4000, language, "Refactor for readability, performance, and robustness", ct);
            
            if (!response.Success || string.IsNullOrEmpty(response.Content))
            {
                return new CodeGenerationResult 
                { 
                    Success = false, 
                    Error = response.Error ?? "Failed to refactor code" 
                };
            }

            return ParseCodeResponse(response.Content);
        }

        /// <summary>
        /// Generate unit tests for code
        /// </summary>
        public static async Task<CodeGenerationResult> GenerateTestsAsync(string code, string? language = null, string? framework = null, System.Threading.CancellationToken ct = default)
        {
            var testFramework = framework ?? GetDefaultTestFramework(language);
            
            var prompt = $@"Generate comprehensive unit tests for this code using {testFramework}:

```{language ?? ""}
{code}
```

Include:
- Happy path tests
- Edge cases
- Error handling tests
- Descriptive test names";

            var messages = new List<object>
            {
                new { role = "system", content = GetCodeSystemPrompt() },
                new { role = "user", content = prompt }
            };

            var response = await SendCodeRequestAsync(messages, 4000, language, testFramework, ct);
            
            if (!response.Success || string.IsNullOrEmpty(response.Content))
            {
                return new CodeGenerationResult 
                { 
                    Success = false, 
                    Error = response.Error ?? "Failed to generate tests" 
                };
            }

            return ParseCodeResponse(response.Content);
        }

        private static string BuildCodePrompt(string request, string? language, string? context)
        {
            var sb = new StringBuilder();
            sb.AppendLine(request);
            
            if (!string.IsNullOrEmpty(language))
            {
                sb.AppendLine($"\nLanguage: {language}");
            }
            
            if (!string.IsNullOrEmpty(context))
            {
                sb.AppendLine($"\nContext:\n{context}");
            }

            return sb.ToString();
        }

        private static Task<AIResponse> SendCodeRequestAsync(List<object> messages, int maxTokens, string? language, string? context, System.Threading.CancellationToken ct)
        {
            var moduleState = string.Empty;
            if (!string.IsNullOrWhiteSpace(language))
                moduleState = $"Target language: {language}.";

            return AIManager.SendMessageAsync(new AIManager.AIRoutingRequest
            {
                Module = "code_assistant",
                Messages = messages,
                MaxTokens = maxTokens,
                BucketHint = AIManager.AITaskBucket.Code,
                RuntimeContext = new AIManager.AIRuntimeContext
                {
                    ActiveModule = "code_assistant",
                    ActivePage = "code",
                    ToolContext = "Code assistant runtime can generate code, explain implementations, produce fixes/refactors/tests, and create file-ready outputs for the current coding task.",
                    ModuleState = moduleState,
                    AdditionalInstructions = string.IsNullOrWhiteSpace(context)
                        ? "Treat this as an implementation-grade coding task. Prefer exact code, root-cause fixes, and concrete reasoning over general guidance."
                        : $"Task context: {context}\nTreat this as an implementation-grade coding task. Prefer exact code, root-cause fixes, and concrete reasoning over general guidance.",
                },
            }, ct);
        }

        private static string GetCodeSystemPrompt()
        {
            return @"You are an expert programmer. Generate clean, efficient, well-documented code.

Rules:
1. Write production-quality code
2. Include helpful comments
3. Follow language best practices
4. Handle errors appropriately
5. Use meaningful variable names
6. Keep code concise but readable

When providing code, wrap it in a code block with the language specified:
```language
code here
```

If you need to explain something, do it briefly before or after the code block.";
        }

        private static CodeGenerationResult ParseCodeResponse(string response)
        {
            var result = new CodeGenerationResult { Success = true };
            
            // Extract code blocks
            var codeBlockMatch = Regex.Match(response, @"```(\w+)?\s*\n([\s\S]*?)```", RegexOptions.Multiline);
            
            if (codeBlockMatch.Success)
            {
                result.Language = codeBlockMatch.Groups[1].Value;
                result.Code = codeBlockMatch.Groups[2].Value.Trim();
                
                // Extract explanation (text before/after code block)
                var beforeCode = response.Substring(0, codeBlockMatch.Index).Trim();
                var afterCode = response.Substring(codeBlockMatch.Index + codeBlockMatch.Length).Trim();
                
                if (!string.IsNullOrEmpty(beforeCode))
                    result.Explanation = beforeCode;
                else if (!string.IsNullOrEmpty(afterCode))
                    result.Explanation = afterCode;
            }
            else
            {
                // No code block found, treat entire response as code
                result.Code = response.Trim();
            }

            return result;
        }

        private static string GetLanguageFromExtension(string extension)
        {
            return extension switch
            {
                ".cs" => "csharp",
                ".py" => "python",
                ".js" => "javascript",
                ".ts" => "typescript",
                ".jsx" => "jsx",
                ".tsx" => "tsx",
                ".java" => "java",
                ".cpp" or ".cc" or ".cxx" => "cpp",
                ".c" => "c",
                ".h" => "c",
                ".hpp" => "cpp",
                ".go" => "go",
                ".rs" => "rust",
                ".rb" => "ruby",
                ".php" => "php",
                ".swift" => "swift",
                ".kt" => "kotlin",
                ".scala" => "scala",
                ".r" => "r",
                ".sql" => "sql",
                ".html" => "html",
                ".css" => "css",
                ".scss" => "scss",
                ".json" => "json",
                ".xml" => "xml",
                ".yaml" or ".yml" => "yaml",
                ".md" => "markdown",
                ".sh" => "bash",
                ".ps1" => "powershell",
                ".bat" or ".cmd" => "batch",
                _ => ""
            };
        }

        private static string GetDefaultTestFramework(string? language)
        {
            return language?.ToLower() switch
            {
                "csharp" or "cs" => "xUnit",
                "python" or "py" => "pytest",
                "javascript" or "js" or "typescript" or "ts" => "Jest",
                "java" => "JUnit",
                "go" => "testing package",
                "rust" => "built-in test framework",
                "ruby" => "RSpec",
                "php" => "PHPUnit",
                _ => "appropriate test framework"
            };
        }
    }

    public class CodeGenerationResult
    {
        public bool Success { get; set; }
        public string? Code { get; set; }
        public string? Language { get; set; }
        public string? Explanation { get; set; }
        public string? Error { get; set; }
    }
}
