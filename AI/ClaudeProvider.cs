using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using AtlasAI.Core;

namespace AtlasAI.AI
{
    public class ClaudeProvider : IAIProvider
    {
        private readonly HttpClient httpClient;
        private string apiKey = "";

        public ClaudeProvider()
        {
            httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromMinutes(5); // long timeout — agent tasks can run for minutes
            
            LoadApiKeyFromStore();
            
            // Initialize HTTP client with API key
            InitializeHttpClient();
        }
        
        private void InitializeHttpClient()
        {
            if (!string.IsNullOrEmpty(apiKey))
            {
                try
                {
                    httpClient.DefaultRequestHeaders.Clear();
                    httpClient.DefaultRequestHeaders.Add("x-api-key", apiKey);
                    httpClient.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"ClaudeProvider: Error setting headers: {ex.Message}");
                }
            }
        }
        
        private void LoadApiKeyFromStore()
        {
            try
            {
            if (AiKeysStore.TryGetPlaintextKey("claude", out var plaintext) && !string.IsNullOrWhiteSpace(plaintext))
                    apiKey = plaintext.Trim();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ClaudeProvider: Error loading key: {ex.Message}");
            }
        }

        private static void TryPersistProtectedKey(string keyName, string plaintext)
        {
            try
            {
                AiKeysStore.SetPlaintextKey(keyName, plaintext);
            }
            catch
            {
            }
        }

        public bool UpdateApiKey(string newKey)
        {
            if (string.IsNullOrWhiteSpace(newKey)) return false;
            apiKey = newKey.Trim();
            InitializeHttpClient();
            TryPersistProtectedKey("claude", apiKey);
            return true;
        }

        public string DisplayName => "Claude (Anthropic)";
        public AIProviderType ProviderType => AIProviderType.Claude;
        public bool IsConfigured => !string.IsNullOrEmpty(apiKey);

        private static readonly HashSet<string> ValidModels = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // Prefer stable aliases where possible
            "claude-3-5-sonnet-latest",
            "claude-3-5-haiku-latest",
            // Claude 3
            "claude-3-opus-20240229",
            "claude-3-sonnet-20240229",
            "claude-3-haiku-20240307",
        };

        private static string NormalizeModelId(string? model)
        {
            var trimmed = (model ?? "").Trim();
            if (string.IsNullOrWhiteSpace(trimmed) || trimmed.Equals("auto", StringComparison.OrdinalIgnoreCase))
                return AIManager.DefaultClaudeSmartModel;

            // UI alias IDs (the embedded UI uses friendly 4.x IDs). Map them to known-good Anthropic IDs.
            if (trimmed.StartsWith("claude-", StringComparison.OrdinalIgnoreCase) == false &&
                trimmed.StartsWith("claude_", StringComparison.OrdinalIgnoreCase) == false &&
                trimmed.StartsWith("claude", StringComparison.OrdinalIgnoreCase))
            {
                // Leave as-is for other claude-prefixed patterns.
            }

            if (trimmed.Contains("-4-6-", StringComparison.OrdinalIgnoreCase) || trimmed.Contains(" 4.6", StringComparison.OrdinalIgnoreCase) || trimmed.Contains("4.6", StringComparison.OrdinalIgnoreCase))
            {
                if (trimmed.Contains("haiku", StringComparison.OrdinalIgnoreCase))
                    return "claude-3-5-haiku-latest";
                if (trimmed.Contains("opus", StringComparison.OrdinalIgnoreCase))
                    return "claude-3-opus-20240229";
                return "claude-3-5-sonnet-latest";
            }

            if (trimmed.Contains("-4-5-", StringComparison.OrdinalIgnoreCase) || trimmed.Contains(" 4.5", StringComparison.OrdinalIgnoreCase) || trimmed.Contains("4.5", StringComparison.OrdinalIgnoreCase))
            {
                if (trimmed.Contains("haiku", StringComparison.OrdinalIgnoreCase))
                    return "claude-3-5-haiku-latest";
                if (trimmed.Contains("opus", StringComparison.OrdinalIgnoreCase))
                    return "claude-3-opus-20240229";
                return "claude-3-5-sonnet-latest";
            }

            // Older/dated Claude 3.5 IDs: normalize to stable "-latest" aliases.
            if (trimmed.StartsWith("claude-3-5-sonnet-", StringComparison.OrdinalIgnoreCase))
                return "claude-3-5-sonnet-latest";
            if (trimmed.StartsWith("claude-3-5-haiku-", StringComparison.OrdinalIgnoreCase))
                return "claude-3-5-haiku-latest";

            // Claude 4.0 (and other 4.x) aliases: map to the best available 4.5 family.
            if (trimmed.Contains("sonnet-4", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Contains("opus-4", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Contains("haiku-4", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Contains("-4-", StringComparison.OrdinalIgnoreCase))
            {
                if (trimmed.Contains("haiku", StringComparison.OrdinalIgnoreCase))
                    return "claude-3-5-haiku-latest";
                if (trimmed.Contains("opus", StringComparison.OrdinalIgnoreCase))
                    return "claude-3-opus-20240229";
                return "claude-3-5-sonnet-latest";
            }

            if (ValidModels.Contains(trimmed))
                return trimmed;

            // Graceful fallback for UI lists that include newer/preview Claude IDs.
            if (trimmed.StartsWith("claude-", StringComparison.OrdinalIgnoreCase) || trimmed.StartsWith("claude_", StringComparison.OrdinalIgnoreCase))
            {
                if (trimmed.Contains("haiku", StringComparison.OrdinalIgnoreCase))
                    return AIManager.DefaultClaudeCheapModel;

                // Prefer smart default for sonnet/opus or unknown.
                return AIManager.DefaultClaudeSmartModel;
            }

            return AIManager.DefaultClaudeSmartModel;
        }

        private static string GetDefaultSystemPrompt()
        {
            try
            {
                var personalityId = (AtlasAI.Settings.SettingsStore.Current?.PersonalitySelected ?? "Atlas").Trim();
                return personalityId.ToLowerInvariant() switch
                {
                    "unfiltered" => "You are Atlas. Talk like the user's blunt mate. Keep it short, real, and helpful. No generic assistant tone.",
                    "professional" => "You are Atlas in professional mode. Be clear, calm, direct, and concise. No fluff.",
                    "buddy" => "You are Atlas talking like the user's best mate. Keep it natural, quick, and practical.",
                    "funny" => "You are Atlas in funny mode. Be witty, short, and helpful without becoming annoying.",
                    "sarcasm" => "You are Atlas in sarcasm mode. Be dry, sharp, and useful without being mean.",
                    "romantic" => "You are Atlas in romantic mode. Be warm, supportive, and concise.",
                    _ => "You are Atlas. Be conversational, natural, concise, and helpful."
                };
            }
            catch
            {
                return "You are Atlas. Be conversational, natural, concise, and helpful.";
            }
        }

        public Task<bool> ConfigureAsync(Dictionary<string, string> config)
        {
            if (config.TryGetValue("ApiKey", out var key))
            {
                var sanitized = (key ?? "").Trim();
                sanitized = AtlasAI.Core.ApiKeySanitizer.SanitizeForHttpHeader(sanitized);
                sanitized = sanitized.Replace("\r", "").Replace("\n", "").Replace("\"", "").Trim();

                apiKey = sanitized;

                // Always reset headers when changing/clearing keys.
                httpClient.DefaultRequestHeaders.Clear();
                if (!string.IsNullOrWhiteSpace(apiKey))
                {
                    httpClient.DefaultRequestHeaders.Add("x-api-key", apiKey);
                    httpClient.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");

                    // Persist only non-empty keys.
                    try { TryPersistProtectedKey("claude", apiKey); } catch { }
                }
                else
                {
                    try { AiKeysStore.RemoveKey("claude"); } catch { }
                }

                return Task.FromResult(true);
            }
            return Task.FromResult(false);
        }

        public async Task<List<AIModel>> GetModelsAsync()
        {
            try
            {
                if (IsConfigured)
                {
                    var response = await httpClient.GetAsync("https://api.anthropic.com/v1/models").ConfigureAwait(false);
                    var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (response.IsSuccessStatusCode)
                    {
                        var parsed = TryParseModels(json);
                        if (parsed.Count > 0) return parsed;
                    }
                }
            }
            catch
            {
            }

            var models = new List<AIModel>
            {
                // UI-friendly Claude 4.0 → 4.6 aliases (mapped in NormalizeModelId)
                new AIModel { Id = "claude-sonnet-4-20250514", DisplayName = "Claude Sonnet 4.0", Description = "Claude 4 series", MaxTokens = 400000 },
                new AIModel { Id = "claude-opus-4-20250514", DisplayName = "Claude Opus 4.0", Description = "Claude 4 series", MaxTokens = 400000 },
                new AIModel { Id = "claude-haiku-4-20250514", DisplayName = "Claude Haiku 4.0", Description = "Claude 4 series", MaxTokens = 400000 },

                new AIModel { Id = "claude-sonnet-4-5-20251022", DisplayName = "Claude Sonnet 4.5", Description = "Fast & capable", MaxTokens = 400000 },
                new AIModel { Id = "claude-opus-4-5-20251022", DisplayName = "Claude Opus 4.5", Description = "Most powerful", MaxTokens = 400000 },
                new AIModel { Id = "claude-haiku-4-5-20251022", DisplayName = "Claude Haiku 4.5", Description = "Lightning fast", MaxTokens = 400000 },

                new AIModel { Id = "claude-sonnet-4-6-20260301", DisplayName = "Claude Sonnet 4.6", Description = "Latest", MaxTokens = 400000 },
                new AIModel { Id = "claude-opus-4-6-20260301", DisplayName = "Claude Opus 4.6", Description = "Latest", MaxTokens = 400000 },
                new AIModel { Id = "claude-haiku-4-6-20260301", DisplayName = "Claude Haiku 4.6", Description = "Latest", MaxTokens = 400000 },

                // Claude 4.5 models (Latest - January 2026)
                new AIModel { Id = "claude-4-5-sonnet-20260115", DisplayName = "Claude 4.5 Sonnet (Latest)", Description = "Most advanced reasoning & coding", MaxTokens = 400000 },
                new AIModel { Id = "claude-4-5-haiku-20260115", DisplayName = "Claude 4.5 Haiku", Description = "Ultra-fast with enhanced capabilities", MaxTokens = 400000 },
                new AIModel { Id = "claude-4-5-opus-20260115", DisplayName = "Claude 4.5 Opus", Description = "Maximum capability & context", MaxTokens = 400000 },
            };
            return models;
        }

        public async Task<AIResponse> SendMessageAsync(List<object> messages, string model = "", int maxTokens = 500, CancellationToken ct = default)
        {
            if (!IsConfigured)
                return new AIResponse { Success = false, Error = "🤖 AI not configured. This installation is locked to admin configuration." };

            try
            {
                var selectedModel = NormalizeModelId(model);
                
                // Filter out system messages - Claude uses a separate system parameter
                var filteredMessages = new List<object>();
                var systemParts = new List<string>();
                string systemPrompt = GetDefaultSystemPrompt();
                
                foreach (var msg in messages)
                {
                    // Check if this is a system message and extract it
                    var msgJson = JsonSerializer.Serialize(msg);
                    using var msgDoc = JsonDocument.Parse(msgJson);
                    if (msgDoc.RootElement.TryGetProperty("role", out var roleElement))
                    {
                        var role = roleElement.GetString();
                        if (role == "system")
                        {
                            // Extract system content but don't add to messages
                            if (msgDoc.RootElement.TryGetProperty("content", out var contentElement))
                            {
                                var part = (contentElement.GetString() ?? "").Trim();
                                if (!string.IsNullOrWhiteSpace(part))
                                    systemParts.Add(part);
                            }
                            continue; // Skip system messages
                        }
                    }
                    filteredMessages.Add(msg);
                }

                if (systemParts.Count > 0)
                    systemPrompt = string.Join("\n\n", systemParts);

                var temperature = 0.85;
                try
                {
                    if (Regex.IsMatch(systemPrompt, @"SAVAGE LEVEL:\s*5/5", RegexOptions.IgnoreCase))
                        temperature = 0.95;
                    else if (Regex.IsMatch(systemPrompt, @"SAVAGE LEVEL:\s*4/5", RegexOptions.IgnoreCase))
                        temperature = 0.9;
                }
                catch
                {
                }
                
                static bool IsNotFoundModelError(string raw)
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(raw);
                        if (doc.RootElement.TryGetProperty("error", out var err) && err.ValueKind == JsonValueKind.Object)
                        {
                            if (err.TryGetProperty("type", out var typeEl))
                                return string.Equals(typeEl.GetString(), "not_found_error", StringComparison.OrdinalIgnoreCase);
                        }
                    }
                    catch
                    {
                    }

                    return raw.IndexOf("not_found_error", StringComparison.OrdinalIgnoreCase) >= 0;
                }

                async Task<(HttpResponseMessage Response, string Body)> SendOnceAsync(string modelId)
                {
                    var request = new
                    {
                        model = modelId,
                        max_tokens = maxTokens,
                        system = systemPrompt,
                        temperature = temperature,
                        messages = filteredMessages
                    };

                    var json = JsonSerializer.Serialize(request);
                    using var content = new StringContent(json, Encoding.UTF8, "application/json");
                    var response = await httpClient.PostAsync("https://api.anthropic.com/v1/messages", content, ct);
                    var responseJson = await response.Content.ReadAsStringAsync(ct);
                    return (response, responseJson);
                }

                var initial = await SendOnceAsync(selectedModel);
                var response = initial.Response;
                var responseJson = initial.Body;

                if (!response.IsSuccessStatusCode && IsNotFoundModelError(responseJson))
                {
                    // Fallback: some accounts/API versions don't recognize certain model IDs.
                    var hinted = (model ?? string.Empty);
                    var fallbacks = new List<string>();
                    if (hinted.IndexOf("haiku", StringComparison.OrdinalIgnoreCase) >= 0)
                        fallbacks.Add("claude-3-5-haiku-latest");
                    if (hinted.IndexOf("opus", StringComparison.OrdinalIgnoreCase) >= 0)
                        fallbacks.Add("claude-3-opus-20240229");
                    fallbacks.Add("claude-3-5-sonnet-latest");
                    fallbacks.Add(AIManager.DefaultClaudeSmartModel);
                    fallbacks.Add(AIManager.DefaultClaudeCheapModel);

                    foreach (var candidate in fallbacks)
                    {
                        if (string.IsNullOrWhiteSpace(candidate)) continue;
                        if (string.Equals(candidate, selectedModel, StringComparison.OrdinalIgnoreCase)) continue;

                        var attempt = await SendOnceAsync(candidate);
                        if (attempt.Response.IsSuccessStatusCode)
                        {
                            try { response.Dispose(); } catch { }
                            selectedModel = candidate;
                            responseJson = attempt.Body;
                            response = attempt.Response;
                            break;
                        }

                        if (!IsNotFoundModelError(attempt.Body))
                        {
                            try { response.Dispose(); } catch { }
                            responseJson = attempt.Body;
                            response = attempt.Response;
                            break;
                        }

                        try { attempt.Response.Dispose(); } catch { }
                    }
                }

                if (!response.IsSuccessStatusCode)
                {
                    if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                    {
                        return new AIResponse 
                        { 
                            Success = false, 
                            Error = "🔑 **Invalid Claude API Key**\n\n" +
                                   "Your Anthropic API key is not valid or has expired.\n\n" +
                                   "💡 **To fix this:**\n" +
                                   "1. Get a valid API key from: https://console.anthropic.com/\n" +
                                   "2. (Admin) Configure AI provider\n" +
                                   "3. Enter your new API key\n" +
                                   "4. Test the connection\n\n" +
                                   "📱 **Alternative:** Switch to OpenAI in Settings if you have an OpenAI API key."
                        };
                    }
                    return new AIResponse 
                    { 
                        Success = false, 
                        Error = $"🔴 **Claude API Error**\n\nStatus: {response.StatusCode}\nDetails: {responseJson}" 
                    };
                }

                using var doc = JsonDocument.Parse(responseJson);
                string assistantMessage = "";
                try
                {
                    if (doc.RootElement.TryGetProperty("content", out var contentEl) && contentEl.ValueKind == JsonValueKind.Array)
                    {
                        var sb = new StringBuilder();
                        foreach (var block in contentEl.EnumerateArray())
                        {
                            if (block.ValueKind != JsonValueKind.Object) continue;
                            if (block.TryGetProperty("type", out var typeEl))
                            {
                                var t = (typeEl.GetString() ?? "").Trim();
                                if (!string.Equals(t, "text", StringComparison.OrdinalIgnoreCase))
                                    continue;
                            }

                            if (block.TryGetProperty("text", out var textEl))
                            {
                                var part = (textEl.GetString() ?? "").Trim();
                                if (!string.IsNullOrWhiteSpace(part))
                                {
                                    if (sb.Length > 0) sb.Append("\n");
                                    sb.Append(part);
                                }
                            }
                        }
                        assistantMessage = sb.ToString().Trim();
                    }
                }
                catch
                {
                    assistantMessage = "";
                }

                if (string.IsNullOrWhiteSpace(assistantMessage))
                {
                    return new AIResponse
                    {
                        Success = false,
                        Error = "🔴 **Claude returned no text**\n\nThe response didn't contain any text content blocks. This can happen if the model returned tool output or an unsupported format.",
                        Model = selectedModel
                    };
                }

                var tokensUsed = 0;
                if (doc.RootElement.TryGetProperty("usage", out var usage))
                {
                    if (usage.TryGetProperty("output_tokens", out var outputTokens))
                        tokensUsed = outputTokens.GetInt32();
                }

                return new AIResponse
                {
                    Success = true,
                    Content = assistantMessage,
                    TokensUsed = tokensUsed,
                    Model = selectedModel
                };
            }
            catch (Exception ex)
            {
                return new AIResponse { Success = false, Error = $"🔴 **Claude Connection Error**\n\n{ex.Message}\n\nCheck your internet connection and API key." };
            }
        }

        private static List<AIModel> TryParseModels(string json)
        {
            var list = new List<AIModel>();
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
                    return list;

                foreach (var el in data.EnumerateArray())
                {
                    if (el.ValueKind != JsonValueKind.Object) continue;
                    if (!el.TryGetProperty("id", out var idEl)) continue;
                    var id = (idEl.GetString() ?? "").Trim();
                    if (string.IsNullOrWhiteSpace(id)) continue;

                    var display = id;
                    if (el.TryGetProperty("display_name", out var dnEl))
                        display = (dnEl.GetString() ?? id).Trim();

                    var max = 200000;
                    if (el.TryGetProperty("max_tokens", out var maxEl) && maxEl.ValueKind == JsonValueKind.Number)
                        max = maxEl.GetInt32();
                    else if (el.TryGetProperty("context_window", out var cwEl) && cwEl.ValueKind == JsonValueKind.Number)
                        max = cwEl.GetInt32();

                    list.Add(new AIModel { Id = id, DisplayName = display, Description = "Available", MaxTokens = max });
                }
            }
            catch
            {
            }

            return list;
        }

        public async Task<bool> TestConnectionAsync()
        {
            if (!IsConfigured) return false;

            try
            {
                var testMessages = new List<object>
                {
                    new { role = "user", content = "Hello" }
                };

                var response = await SendMessageAsync(testMessages, "", 10, CancellationToken.None);
                return response.Success;
            }
            catch
            {
                return false;
            }
        }
    }
}
