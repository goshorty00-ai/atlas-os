using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AtlasAI.Core;

namespace AtlasAI.AI
{
    public class OpenAIProvider : IAIProvider
    {
        private static readonly HttpClient httpClient = new HttpClient() { Timeout = TimeSpan.FromMinutes(5) };
        private string apiKey = "";

        public OpenAIProvider()
        {
            LoadApiKeyFromStore();
            
            // Initialize HTTP client with API key
            if (!string.IsNullOrEmpty(apiKey))
            {
                httpClient.DefaultRequestHeaders.Clear();
                httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
            }
        }
        
        private void LoadApiKeyFromStore()
        {
            try
            {
                if (AiKeysStore.TryGetPlaintextKey("openai", out var plaintext) && !string.IsNullOrWhiteSpace(plaintext))
                {
                    apiKey = plaintext.Trim();
                    System.Diagnostics.Debug.WriteLine("OpenAIProvider: Loaded key from AiKeysStore");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"OpenAIProvider: Error loading key: {ex.Message}");
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

        public string DisplayName => "OpenAI (GPT)";
        public AIProviderType ProviderType => AIProviderType.OpenAI;
        public bool IsConfigured => !string.IsNullOrEmpty(apiKey);

        public Task<bool> ConfigureAsync(Dictionary<string, string> config)
        {
            if (config.TryGetValue("ApiKey", out var key))
            {
                var sanitized = (key ?? "").Trim();
                sanitized = AtlasAI.Core.ApiKeySanitizer.SanitizeForHttpHeader(sanitized);
                sanitized = sanitized.Replace("\r", "").Replace("\n", "").Replace("\"", "").Trim();

                apiKey = sanitized;

                httpClient.DefaultRequestHeaders.Clear();
                if (!string.IsNullOrWhiteSpace(apiKey))
                {
                    httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
                    try { TryPersistProtectedKey("openai", apiKey); } catch { }
                }
                else
                {
                    try { AiKeysStore.RemoveKey("openai"); } catch { }
                }

                return Task.FromResult(true);
            }
            return Task.FromResult(false);
        }

        // Valid OpenAI models (January 2026)
        private static readonly HashSet<string> ValidModels = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // GPT-5.2 family (Latest - Dec 2025)
            "gpt-5.2", "gpt-5.2-chat-latest", "gpt-5.2-pro",
            // GPT-5.1 family
            "gpt-5.1", "gpt-5.1-mini", "gpt-5.1-nano",
            // GPT-5 family (Aug 2025)
            "gpt-5", "gpt-5-mini", "gpt-5-nano",
            // GPT-4o family (fallback)
            "gpt-4o", "gpt-4o-mini",
            // Legacy
            "gpt-4-turbo", "gpt-4", "gpt-3.5-turbo",
            // Reasoning models
            "o1", "o1-mini", "o1-preview", "o3-mini", "o4-mini"
        };
        
        public Task<List<AIModel>> GetModelsAsync()
        {
            System.Diagnostics.Debug.WriteLine("OpenAIProvider.GetModelsAsync() called");
            var models = new List<AIModel>
            {
                // GPT 4.0 → 5.2 + Codex aliases used by the embedded UI
                new AIModel { Id = "gpt-4", DisplayName = "GPT-4.0", Description = "Legacy GPT-4", MaxTokens = 128000 },
                new AIModel { Id = "gpt-4-turbo", DisplayName = "GPT-4 Turbo", Description = "Faster GPT-4", MaxTokens = 128000 },
                new AIModel { Id = "gpt-4o", DisplayName = "GPT-4o", Description = "Multimodal", MaxTokens = 128000 },
                new AIModel { Id = "gpt-4.1", DisplayName = "GPT-4.1", Description = "Alias → GPT-4o", MaxTokens = 128000 },

                // GPT-5.2 models (Latest - Dec 2025)
                new AIModel { Id = "gpt-5.2", DisplayName = "GPT-5.2 (Latest)", Description = "Most advanced - thinking mode", MaxTokens = 400000 },
                new AIModel { Id = "gpt-5.2-chat-latest", DisplayName = "GPT-5.2 Chat", Description = "ChatGPT model - instant", MaxTokens = 400000 },
                new AIModel { Id = "gpt-5.2-pro", DisplayName = "GPT-5.2 Pro", Description = "Extended thinking", MaxTokens = 400000 },
                // GPT-5.1 models
                new AIModel { Id = "gpt-5.1", DisplayName = "GPT-5.1", Description = "Previous flagship", MaxTokens = 256000 },
                new AIModel { Id = "gpt-5.1-mini", DisplayName = "GPT-5.1 Mini", Description = "Fast and efficient", MaxTokens = 256000 },
                // GPT-5 (Aug 2025)
                new AIModel { Id = "gpt-5", DisplayName = "GPT-5", Description = "Original GPT-5", MaxTokens = 128000 },
                new AIModel { Id = "gpt-5-mini", DisplayName = "GPT-5 Mini", Description = "Smaller GPT-5", MaxTokens = 128000 },

                // Codex aliases (normalized in SendMessageAsync)
                new AIModel { Id = "gpt-5.1-codex", DisplayName = "Codex 5.1", Description = "Alias → GPT-5.1", MaxTokens = 256000 },
                new AIModel { Id = "gpt-5.2-codex", DisplayName = "Codex 5.2", Description = "Alias → GPT-5.2", MaxTokens = 400000 },
                new AIModel { Id = "gpt-5.3-codex", DisplayName = "Codex 5.3", Description = "Alias → GPT-5.2", MaxTokens = 400000 },

                // GPT-4o mini
                new AIModel { Id = "gpt-4o-mini", DisplayName = "GPT-4o Mini", Description = "Fast and affordable", MaxTokens = 128000 },
                // Reasoning models
                new AIModel { Id = "o4-mini", DisplayName = "o4 Mini", Description = "Latest reasoning", MaxTokens = 128000 },
                new AIModel { Id = "o3-mini", DisplayName = "o3 Mini", Description = "Fast reasoning", MaxTokens = 128000 },
                new AIModel { Id = "o1", DisplayName = "o1", Description = "Advanced reasoning", MaxTokens = 128000 }
            };
            System.Diagnostics.Debug.WriteLine($"OpenAIProvider returning {models.Count} models");
            return Task.FromResult(models);
        }

        public async Task<AIResponse> SendMessageAsync(List<object> messages, string model = "", int maxTokens = 500, CancellationToken ct = default)
        {
            if (!IsConfigured)
                return new AIResponse { Success = false, Error = "🤖 AI not configured. This installation is locked to admin configuration." };

            try
            {
                static string NormalizeModelId(string? raw)
                {
                    var m = (raw ?? "").Trim();
                    if (string.IsNullOrWhiteSpace(m) || m.Equals("auto", StringComparison.OrdinalIgnoreCase))
                        return "";

                    // UI aliases / non-standard IDs we expose in the embedded UI.
                    if (m.Equals("gpt-4.1", StringComparison.OrdinalIgnoreCase))
                        return "gpt-4o";

                    if (m.StartsWith("gpt-5.", StringComparison.OrdinalIgnoreCase) && m.Contains("codex", StringComparison.OrdinalIgnoreCase))
                    {
                        // Treat Codex variants as the closest base GPT model.
                        if (m.StartsWith("gpt-5.1", StringComparison.OrdinalIgnoreCase))
                            return "gpt-5.1";
                        if (m.StartsWith("gpt-5.2", StringComparison.OrdinalIgnoreCase))
                            return "gpt-5.2";
                        if (m.StartsWith("gpt-5.3", StringComparison.OrdinalIgnoreCase))
                            return "gpt-5.2";
                    }

                    return m;
                }

                // Validate model - fallback to gpt-5.2-chat-latest if empty or invalid
                var selectedModel = NormalizeModelId(model);
                if (string.IsNullOrEmpty(selectedModel) || !ValidModels.Contains(selectedModel))
                {
                    System.Diagnostics.Debug.WriteLine($"[OpenAI] Model '{model}' not in valid list, using gpt-5.2-chat-latest");
                    selectedModel = "gpt-5.2-chat-latest";
                }
                
                // Messages already contain system prompt from caller - use them directly
                var openAIMessages = new List<object>(messages);

                // Use max_completion_tokens for GPT-5.x and o-series models, max_tokens for older ones
                object request;
                if (selectedModel.StartsWith("gpt-5") || selectedModel.StartsWith("o1") || selectedModel.StartsWith("o3") || selectedModel.StartsWith("o4"))
                {
                    // GPT-5.x and reasoning models need more tokens for thinking + output
                    var minTokens = Math.Max(maxTokens, 4096);
                    request = new
                    {
                        model = selectedModel,
                        messages = openAIMessages,
                        max_completion_tokens = minTokens
                    };
                }
                else
                {
                    request = new
                    {
                        model = selectedModel,
                        messages = openAIMessages,
                        max_tokens = maxTokens,
                        temperature = 0.7
                    };
                }

                var json = JsonSerializer.Serialize(request);
                System.Diagnostics.Debug.WriteLine($"[OpenAI] Sending request to model: {selectedModel}, messages: {messages.Count}");
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await httpClient.PostAsync("https://api.openai.com/v1/chat/completions", content, ct);
                var responseJson = await response.Content.ReadAsStringAsync();
                System.Diagnostics.Debug.WriteLine($"[OpenAI] Response status: {response.StatusCode}, Length: {responseJson.Length}");

                if (!response.IsSuccessStatusCode)
                {
                    System.Diagnostics.Debug.WriteLine($"[OpenAI] Error response: {responseJson}");

                    try
                    {
                        AppLogger.LogError($"[OpenAI] API call failed. Status: {(int)response.StatusCode} ({response.StatusCode})");
                    }
                    catch { }
                    
                    // Billing/quota error (commonly returned as 429 with code=insufficient_quota)
                    if (responseJson.IndexOf("insufficient_quota", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return new AIResponse
                        {
                            Success = false,
                            Error = "💳 **OpenAI Billing / Quota Issue**\n\n" +
                                   "Your OpenAI account reports `insufficient_quota`.\n\n" +
                                   "💡 **To fix this:**\n" +
                                   "1. Check billing/credits: https://platform.openai.com/account/billing\n" +
                                   "2. Or switch provider to Claude / Gemini in the model selector." 
                        };
                    }

                    if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                    {
                        try { AppLogger.LogError("[OpenAI] Likely invalid API key (401 Unauthorized).", null); } catch { }
                        return new AIResponse 
                        { 
                            Success = false, 
                            Error = "🔑 **Invalid OpenAI API Key**\n\n" +
                                   "Your API key is not valid or has expired.\n\n" +
                                   "💡 **To fix this:**\n" +
                                   "1. Get a valid API key from: https://platform.openai.com/api-keys\n" +
                                   "2. (Admin) Configure AI provider\n" +
                                   "3. Enter your new API key\n" +
                                   "4. Test the connection\n\n" +
                                   "📱 **Alternative:** Switch to Claude in Settings if you have an Anthropic API key."
                        };
                    }
                    
                    // Check for model not found error
                    if (responseJson.Contains("model_not_found") || responseJson.Contains("does not exist"))
                    {
                        try { AppLogger.LogWarning("[OpenAI] Model not found / not available (potential 404 or model_not_found)."); } catch { }
                        return new AIResponse 
                        { 
                            Success = false, 
                            Error = $"🔴 **Model Not Available**\n\nThe model '{selectedModel}' is not available on the configured AI account.\n\nAn administrator must select a different model."
                        };
                    }
                    
                    return new AIResponse 
                    { 
                        Success = false, 
                        Error = $"🔴 **OpenAI API Error**\n\nStatus: {response.StatusCode}\nDetails: {responseJson}" 
                    };
                }

                using var doc = JsonDocument.Parse(responseJson);
                
                // Try to extract content - handle different response formats
                string assistantMessage = "";
                
                try
                {
                    // Standard format: choices[0].message.content
                    if (doc.RootElement.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
                    {
                        var firstChoice = choices[0];
                        if (firstChoice.TryGetProperty("message", out var message))
                        {
                            if (message.TryGetProperty("content", out var msgContent))
                            {
                                assistantMessage = msgContent.GetString() ?? "";
                            }
                        }
                    }
                    
                    // If still empty, try output format (some newer models)
                    if (string.IsNullOrWhiteSpace(assistantMessage) && doc.RootElement.TryGetProperty("output", out var output))
                    {
                        assistantMessage = output.GetString() ?? "";
                    }
                }
                catch (Exception parseEx)
                {
                    System.Diagnostics.Debug.WriteLine($"[OpenAI] Parse error: {parseEx.Message}");
                    System.Diagnostics.Debug.WriteLine($"[OpenAI] Raw response: {responseJson}");
                }
                
                // Check for empty content
                if (string.IsNullOrWhiteSpace(assistantMessage))
                {
                    System.Diagnostics.Debug.WriteLine($"[OpenAI] Empty content in response: {responseJson}");
                    
                    // Show truncated response for debugging
                    var truncatedResponse = responseJson.Length > 500 ? responseJson.Substring(0, 500) + "..." : responseJson;
                    return new AIResponse 
                    { 
                        Success = false, 
                        Error = $"🤔 The AI returned an empty response.\n\nModel: {selectedModel}\nResponse preview: {truncatedResponse}"
                    };
                }

                var tokensUsed = 0;
                if (doc.RootElement.TryGetProperty("usage", out var usage))
                {
                    if (usage.TryGetProperty("completion_tokens", out var completionTokens))
                        tokensUsed = completionTokens.GetInt32();
                }

                return new AIResponse
                { 
                    Success = true,
                    Content = assistantMessage,
                    TokensUsed = tokensUsed,
                    Model = selectedModel
                };
            }
            catch (OperationCanceledException)
            {
                return new AIResponse { Success = false, Error = "⏱️ AI request timed out or was cancelled." };
            }
            catch (Exception ex)
            {
                return new AIResponse { Success = false, Error = $"🔴 **OpenAI Connection Error**\n\n{ex.Message}\n\nCheck your internet connection and API key." };
            }
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

                var response = await SendMessageAsync(testMessages, "", 10);
                return response.Success;
            }
            catch
            {
                return false;
            }
        }
    }
}
