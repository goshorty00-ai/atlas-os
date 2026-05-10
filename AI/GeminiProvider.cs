using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AtlasAI.Core;

namespace AtlasAI.AI
{
    public class GeminiProvider : IAIProvider
    {
        private static readonly HttpClient httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        private string apiKey = "";

        private static readonly object ModelsLock = new();
        private static DateTime modelsCacheAtUtc = DateTime.MinValue;
        private static List<AIModel>? modelsCache;
        private static readonly TimeSpan ModelsCacheTtl = TimeSpan.FromMinutes(30);

        private static readonly HashSet<string> ValidModels = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // Common Gemini model IDs. Avoid "*-latest" aliases which can 404 depending on API version.
            "gemini-2.0-flash",
            "gemini-2.5-pro",
            "gemini-2.5-flash",
            "gemini-2.5-flash-lite",
            "gemini-1.5-flash",
            "gemini-1.5-pro",
            // Preview IDs exposed in the UI (best-effort).
            "gemini-3-flash-preview",
            "gemini-3-pro-preview",
            "gemini-3.1-pro-preview",
            "gemini-3.1-flash-lite-preview",
        };

        public GeminiProvider()
        {
            LoadApiKeyFromStore();
        }

        public string DisplayName => "Gemini (Google)";
        public AIProviderType ProviderType => AIProviderType.Gemini;
        public bool IsConfigured => !string.IsNullOrWhiteSpace(apiKey);

        private void LoadApiKeyFromStore()
        {
            try
            {
            if (AiKeysStore.TryGetPlaintextKey("gemini", out var plaintext) && !string.IsNullOrWhiteSpace(plaintext))
                    apiKey = plaintext.Trim();
            }
            catch
            {
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

        public Task<bool> ConfigureAsync(Dictionary<string, string> config)
        {
            if (!config.TryGetValue("ApiKey", out var key))
                return Task.FromResult(false);

            var sanitized = (key ?? "").Trim();
            sanitized = AtlasAI.Core.ApiKeySanitizer.SanitizeForHttpHeader(sanitized);
            sanitized = sanitized.Replace("\r", "").Replace("\n", "").Replace("\"", "").Trim();

            apiKey = sanitized;

            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                try { TryPersistProtectedKey("gemini", apiKey); } catch { }
            }
            else
            {
                try { AiKeysStore.RemoveKey("gemini"); } catch { }
            }

            return Task.FromResult(true);
        }

        public Task<List<AIModel>> GetModelsAsync()
        {
            // If Gemini isn't configured yet, return a minimal static list.
            if (!IsConfigured)
            {
                var models = new List<AIModel>
                {
                    new AIModel { Id = "gemini-2.5-flash", DisplayName = "Gemini 2.5 Flash", Description = "Best price/perf", MaxTokens = 128000 },
                    new AIModel { Id = "gemini-2.5-flash-lite", DisplayName = "Gemini 2.5 Flash-Lite", Description = "Fastest · budget", MaxTokens = 128000 },
                    new AIModel { Id = "gemini-2.5-pro", DisplayName = "Gemini 2.5 Pro", Description = "Strong reasoning", MaxTokens = 128000 },
                    // Legacy fallback models (may be unsupported on some keys).
                    new AIModel { Id = "gemini-2.0-flash", DisplayName = "Gemini 2.0 Flash (Deprecated)", Description = "Legacy", MaxTokens = 128000 },
                    new AIModel { Id = "gemini-1.5-flash", DisplayName = "Gemini 1.5 Flash", Description = "Legacy", MaxTokens = 128000 },
                    new AIModel { Id = "gemini-1.5-pro", DisplayName = "Gemini 1.5 Pro", Description = "Legacy", MaxTokens = 128000 },
                };

                return Task.FromResult(models);
            }

            return GetModelsFromApiWithCacheAsync();
        }

        private async Task<List<AIModel>> GetModelsFromApiWithCacheAsync()
        {
            try
            {
                lock (ModelsLock)
                {
                    if (modelsCache != null && (DateTime.UtcNow - modelsCacheAtUtc) < ModelsCacheTtl)
                        return new List<AIModel>(modelsCache);
                }

                var models = await FetchGenerateContentModelsAsync();
                if (models.Count == 0)
                {
                    // Fall back to the static list if the models endpoint isn't available for this key.
                    return new List<AIModel>
                    {
                        new AIModel { Id = "gemini-2.5-flash", DisplayName = "Gemini 2.5 Flash", Description = "Best price/perf", MaxTokens = 128000 },
                        new AIModel { Id = "gemini-2.5-flash-lite", DisplayName = "Gemini 2.5 Flash-Lite", Description = "Fastest · budget", MaxTokens = 128000 },
                        new AIModel { Id = "gemini-2.5-pro", DisplayName = "Gemini 2.5 Pro", Description = "Strong reasoning", MaxTokens = 128000 },
                    };
                }

                lock (ModelsLock)
                {
                    modelsCache = models;
                    modelsCacheAtUtc = DateTime.UtcNow;
                }

                return new List<AIModel>(models);
            }
            catch
            {
                return new List<AIModel>
                {
                    new AIModel { Id = "gemini-2.5-flash", DisplayName = "Gemini 2.5 Flash", Description = "Best price/perf", MaxTokens = 128000 },
                    new AIModel { Id = "gemini-2.5-flash-lite", DisplayName = "Gemini 2.5 Flash-Lite", Description = "Fastest · budget", MaxTokens = 128000 },
                    new AIModel { Id = "gemini-2.5-pro", DisplayName = "Gemini 2.5 Pro", Description = "Strong reasoning", MaxTokens = 128000 },
                };
            }
        }

        private async Task<List<AIModel>> FetchGenerateContentModelsAsync()
        {
            var byId = new Dictionary<string, AIModel>(StringComparer.OrdinalIgnoreCase);

            foreach (var apiVersion in new[] { "v1beta", "v1" })
            {
                try
                {
                    var url = $"https://generativelanguage.googleapis.com/{apiVersion}/models?key={Uri.EscapeDataString(apiKey)}";
                    using var req = new HttpRequestMessage(HttpMethod.Get, url);
                    var resp = await httpClient.SendAsync(req);
                    var json = await resp.Content.ReadAsStringAsync();
                    if (!resp.IsSuccessStatusCode)
                        continue;

                    using var doc = JsonDocument.Parse(json);
                    if (!doc.RootElement.TryGetProperty("models", out var modelsEl) || modelsEl.ValueKind != JsonValueKind.Array)
                        continue;

                    foreach (var m in modelsEl.EnumerateArray())
                    {
                        if (m.ValueKind != JsonValueKind.Object)
                            continue;

                        var name = "";
                        var displayName = "";
                        var description = "";
                        var maxTokens = 128000;

                        try
                        {
                            if (m.TryGetProperty("name", out var nameEl))
                                name = (nameEl.GetString() ?? "").Trim();
                            if (m.TryGetProperty("displayName", out var dnEl))
                                displayName = (dnEl.GetString() ?? "").Trim();
                            if (m.TryGetProperty("description", out var descEl))
                                description = (descEl.GetString() ?? "").Trim();
                            if (m.TryGetProperty("inputTokenLimit", out var tokEl) && tokEl.TryGetInt32(out var tok))
                                maxTokens = tok;
                        }
                        catch
                        {
                        }

                        var id = NormalizeModelNameToId(name);
                        if (string.IsNullOrWhiteSpace(id))
                            continue;

                        // Filter to models that actually support generateContent.
                        if (!SupportsGenerateContent(m))
                            continue;

                        if (!byId.TryGetValue(id, out var existing))
                        {
                            byId[id] = new AIModel
                            {
                                Id = id,
                                DisplayName = string.IsNullOrWhiteSpace(displayName) ? id : displayName,
                                Description = description,
                                MaxTokens = maxTokens,
                            };
                        }
                        else
                        {
                            // Prefer richer metadata if available.
                            if (string.IsNullOrWhiteSpace(existing.DisplayName) || existing.DisplayName.Equals(existing.Id, StringComparison.OrdinalIgnoreCase))
                                existing.DisplayName = string.IsNullOrWhiteSpace(displayName) ? existing.Id : displayName;
                            if (string.IsNullOrWhiteSpace(existing.Description))
                                existing.Description = description;
                            if (existing.MaxTokens < maxTokens)
                                existing.MaxTokens = maxTokens;
                        }
                    }
                }
                catch
                {
                }
            }

            var list = new List<AIModel>(byId.Values);
            list.Sort((a, b) => string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase));
            return list;
        }

        private static bool SupportsGenerateContent(JsonElement modelObj)
        {
            try
            {
                if (modelObj.TryGetProperty("supportedGenerationMethods", out var methods) && methods.ValueKind == JsonValueKind.Array)
                {
                    foreach (var m in methods.EnumerateArray())
                    {
                        var s = (m.GetString() ?? "").Trim();
                        if (s.Equals("generateContent", StringComparison.OrdinalIgnoreCase))
                            return true;
                    }
                    return false;
                }
            }
            catch
            {
            }

            // If the API doesn't return supported methods, be permissive.
            return true;
        }

        private static string NormalizeModelNameToId(string? name)
        {
            var trimmed = (name ?? "").Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
                return "";

            // API returns names like "models/gemini-2.5-pro".
            if (trimmed.StartsWith("models/", StringComparison.OrdinalIgnoreCase))
                trimmed = trimmed.Substring("models/".Length);

            return trimmed.Trim();
        }

        private static string NormalizeModelId(string? model)
        {
            var trimmed = (model ?? "").Trim();
            trimmed = NormalizeModelNameToId(trimmed);
            if (string.IsNullOrWhiteSpace(trimmed) || trimmed.Equals("auto", StringComparison.OrdinalIgnoreCase))
                return AIManager.DefaultGeminiSmartModel;

            // Legacy settings that used "*-latest" aliases.
            if (trimmed.Equals("gemini-1.5-pro-latest", StringComparison.OrdinalIgnoreCase))
                return "gemini-1.5-pro";
            if (trimmed.Equals("gemini-1.5-flash-latest", StringComparison.OrdinalIgnoreCase))
                return "gemini-1.5-flash";

            // Keep known-good IDs.
            if (ValidModels.Contains(trimmed))
                return trimmed;

            // Graceful fallback for UI lists that include newer/preview Gemini IDs.
            if (trimmed.StartsWith("gemini-", StringComparison.OrdinalIgnoreCase))
            {
                // Allow newer Gemini IDs; send-path will fall back if unavailable.
                return trimmed;
            }

            return AIManager.DefaultGeminiSmartModel;
        }

        private static bool IsTransientStatus(HttpStatusCode code)
        {
            return code == HttpStatusCode.ServiceUnavailable
                || code == HttpStatusCode.TooManyRequests
                || code == HttpStatusCode.GatewayTimeout
                || code == HttpStatusCode.BadGateway
                || code == HttpStatusCode.RequestTimeout
                || code == HttpStatusCode.InternalServerError;
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

        public async Task<AIResponse> SendMessageAsync(List<object> messages, string model = "", int maxTokens = 500, CancellationToken ct = default)
        {
            if (!IsConfigured)
                return new AIResponse { Success = false, Error = "🤖 AI not configured. This installation is locked to admin configuration." };

            var selectedModel = NormalizeModelId(model);

            try
            {
                var systemPrompt = GetDefaultSystemPrompt();
                var contents = new List<object>();

                if (messages != null)
                {
                    foreach (var msg in messages)
                    {
                        if (msg == null) continue;
                        try
                        {
                            var msgJson = JsonSerializer.Serialize(msg);
                            using var msgDoc = JsonDocument.Parse(msgJson);
                            if (!msgDoc.RootElement.TryGetProperty("role", out var roleEl)) continue;
                            var role = (roleEl.GetString() ?? "").Trim().ToLowerInvariant();

                            if (!msgDoc.RootElement.TryGetProperty("content", out var contentEl)) continue;
                            var text = contentEl.ValueKind == JsonValueKind.String ? (contentEl.GetString() ?? "") : contentEl.ToString();
                            text = (text ?? "").Trim();
                            if (string.IsNullOrWhiteSpace(text)) continue;

                            if (role == "system")
                            {
                                systemPrompt = text;
                                continue;
                            }

                            var geminiRole = role == "assistant" ? "model" : "user";
                            contents.Add(new
                            {
                                role = geminiRole,
                                parts = new[] { new { text } }
                            });
                        }
                        catch
                        {
                        }
                    }
                }

                if (contents.Count == 0)
                {
                    contents.Add(new { role = "user", parts = new[] { new { text = "Hello" } } });
                }

                var request = new
                {
                    systemInstruction = new { parts = new[] { new { text = systemPrompt } } },
                    contents = contents,
                    generationConfig = new { maxOutputTokens = maxTokens, temperature = 0.7 }
                };

                var requestJson = JsonSerializer.Serialize(request);

                var modelCandidates = new List<string> { selectedModel };

                // If the preferred model 404s, fall back to the other latest tier.
                if (selectedModel.Contains("pro", StringComparison.OrdinalIgnoreCase))
                {
                    if (!modelCandidates.Exists(m => m.Equals("gemini-2.0-flash", StringComparison.OrdinalIgnoreCase)))
                        modelCandidates.Add("gemini-2.0-flash");
                    if (!modelCandidates.Exists(m => m.Equals("gemini-1.5-flash", StringComparison.OrdinalIgnoreCase)))
                        modelCandidates.Add("gemini-1.5-flash");
                }
                if (selectedModel.Contains("flash", StringComparison.OrdinalIgnoreCase))
                {
                    if (!modelCandidates.Exists(m => m.Equals("gemini-2.5-pro", StringComparison.OrdinalIgnoreCase)))
                        modelCandidates.Add("gemini-2.5-pro");
                    if (!modelCandidates.Exists(m => m.Equals("gemini-1.5-pro", StringComparison.OrdinalIgnoreCase)))
                        modelCandidates.Add("gemini-1.5-pro");
                }

                string responseJson = "";
                HttpResponseMessage? response = null;
                var usedModel = selectedModel;
                var lastStatus = (HttpStatusCode?)null;
                var usedApiVersion = "v1beta";

                var apiVersions = new[] { "v1beta", "v1" };

                foreach (var candidateModel in modelCandidates)
                {
                    foreach (var apiVersion in apiVersions)
                    {
                        usedModel = candidateModel;
                        usedApiVersion = apiVersion;
                        var url = $"https://generativelanguage.googleapis.com/{apiVersion}/models/{Uri.EscapeDataString(candidateModel)}:generateContent?key={Uri.EscapeDataString(apiKey)}";

                        const int maxAttemptsPerModel = 2;
                        for (var attempt = 0; attempt < maxAttemptsPerModel; attempt++)
                        {
                            using var httpContent = new StringContent(requestJson, Encoding.UTF8, "application/json");
                            response = await httpClient.PostAsync(url, httpContent, ct);
                            responseJson = await response.Content.ReadAsStringAsync(ct);
                            lastStatus = response.StatusCode;

                            if (response.IsSuccessStatusCode)
                                break;

                            // NotFound can mean: wrong API version OR model name not available. Try next API version first.
                            if (response.StatusCode == HttpStatusCode.NotFound)
                                break;

                            // Transient overload; retry once.
                            if (IsTransientStatus(response.StatusCode) && attempt < maxAttemptsPerModel - 1)
                            {
                                try { await Task.Delay(TimeSpan.FromMilliseconds(750 * (attempt + 1)), ct); } catch { }
                                continue;
                            }

                            // Non-transient; stop attempting.
                            break;
                        }

                        if (response != null && response.IsSuccessStatusCode)
                            break;

                        // If transient, another API version won't help; fall back to next model tier.
                        if (response != null && IsTransientStatus(response.StatusCode))
                            break;
                    }

                    if (response != null && response.IsSuccessStatusCode)
                        break;

                    // On transient errors, fall back to the next model tier.
                    if (response != null && IsTransientStatus(response.StatusCode))
                        continue;

                    // If NotFound, try the next model tier.
                    if (response != null && response.StatusCode == HttpStatusCode.NotFound)
                        continue;

                    // For other non-transient errors, stop.
                    if (response != null)
                        break;
                }

                if (response == null)
                    return new AIResponse { Success = false, Error = "🔴 **Gemini Connection Error**\n\nNo response returned." };

                if (!response.IsSuccessStatusCode)
                {
                    if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized || response.StatusCode == System.Net.HttpStatusCode.Forbidden)
                    {
                        return new AIResponse
                        {
                            Success = false,
                            Error = "🔑 **Invalid Gemini API Key**\n\n" +
                                   "Your Gemini API key is not valid or is missing access.\n\n" +
                                   "This installation is locked to admin configuration.\n\n" +
                                   "💡 **Admin fix:** Update `%APPDATA%\\AtlasAI\\ai_keys.json` with a valid `gemini` key (DPAPI-wrapped is supported)."
                        };
                    }

                    if (IsTransientStatus(response.StatusCode))
                    {
                        return new AIResponse
                        {
                            Success = false,
                            Error = "🟠 **Gemini is busy (temporary)**\n\n" +
                                   "Google returned a temporary overload/availability error. Atlas already retries briefly and falls back across Gemini tiers.\n\n" +
                                   "Try again in a few seconds, or switch model Flash/Pro.\n\n" +
                                   $"Status: {response.StatusCode}",
                            Model = usedModel
                        };
                    }

                    if (response.StatusCode == HttpStatusCode.NotFound)
                    {
                        return new AIResponse
                        {
                            Success = false,
                            Error = "🔴 **Gemini model not available**\n\n" +
                                   "Google returned 404 NotFound for the selected model. This commonly means the model name is not available to this API key, or the API version differs.\n\n" +
                                   $"Tried API: {usedApiVersion}\n" +
                                   $"Last model: {usedModel}\n\n" +
                                   "**Admin fix:** Verify the `gemini` key has Generative Language API access and supports the selected model tier.",
                            Model = usedModel
                        };
                    }

                    return new AIResponse
                    {
                        Success = false,
                        Error = $"🔴 **Gemini API Error**\n\nStatus: {response.StatusCode}\nDetails: {responseJson}"
                    };
                }

                JsonDocument doc;
                try
                {
                    doc = JsonDocument.Parse(responseJson);
                }
                catch (Exception ex)
                {
                    try
                    {
                        var preview = (responseJson ?? "").Trim();
                        if (preview.Length > 2000) preview = preview.Substring(0, 2000);
                        AppLogger.LogWarning($"[Gemini] Non-JSON or invalid JSON response. model={usedModel} apiVersionFallback=enabled status={(int)response.StatusCode} ({response.StatusCode}) preview={preview}");
                        AppLogger.LogError("[Gemini] JSON parse failed", ex);
                    }
                    catch
                    {
                    }

                    return new AIResponse
                    {
                        Success = false,
                        Error = "🔴 **Gemini returned an invalid response**\n\n" +
                               "The request succeeded, but the response was not valid JSON (or was truncated).\n\n" +
                               $"Log: {AppLogger.GetCurrentLogFilePath()}",
                        Model = usedModel
                    };
                }

                using (doc)
                {
                    var assistantText = "";
                    var finishReason = "";
                    var promptBlockReason = "";
                    var nonTextPartKinds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    var partValueKinds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    var candidateCount = 0;
                    var sawPartsArray = false;
                    var partsLength = -1;

                    try
                    {
                        if (doc.RootElement.TryGetProperty("promptFeedback", out var promptFeedback) &&
                            promptFeedback.ValueKind == JsonValueKind.Object &&
                            promptFeedback.TryGetProperty("blockReason", out var br))
                        {
                            promptBlockReason = (br.GetString() ?? "").Trim();
                        }

                    // Some API errors are returned as a JSON body even when status code isn't helpful.
                    if (doc.RootElement.TryGetProperty("error", out var errEl) && errEl.ValueKind == JsonValueKind.Object)
                    {
                        var msg = "";
                        try
                        {
                            if (errEl.TryGetProperty("message", out var msgEl))
                                msg = (msgEl.GetString() ?? "").Trim();
                        }
                        catch
                        {
                        }

                        if (!string.IsNullOrWhiteSpace(msg))
                        {
                            return new AIResponse
                            {
                                Success = false,
                                Error = $"🔴 **Gemini API Error**\n\n{msg}",
                                Model = usedModel
                            };
                        }
                    }

                    if (doc.RootElement.TryGetProperty("candidates", out var candidates) &&
                        candidates.ValueKind == JsonValueKind.Array &&
                        candidates.GetArrayLength() > 0)
                    {
                        candidateCount = candidates.GetArrayLength();
                        var textSb = new StringBuilder();

                        foreach (var candidate in candidates.EnumerateArray())
                        {
                            if (candidate.ValueKind != JsonValueKind.Object)
                                continue;

                            // Keep the first finishReason we see (normally candidate[0]).
                            if (string.IsNullOrWhiteSpace(finishReason) && candidate.TryGetProperty("finishReason", out var fr))
                                finishReason = (fr.GetString() ?? "").Trim();

                            // Some variants include a direct text field at the candidate level.
                            if (candidate.TryGetProperty("text", out var candidateTextEl))
                            {
                                var candidateText = (candidateTextEl.ValueKind == JsonValueKind.String ? (candidateTextEl.GetString() ?? "") : candidateTextEl.ToString()).Trim();
                                if (!string.IsNullOrWhiteSpace(candidateText))
                                {
                                    if (textSb.Length > 0) textSb.Append("\n");
                                    textSb.Append(candidateText);
                                    continue;
                                }
                            }

                            if (!candidate.TryGetProperty("content", out var content))
                                continue;

                            // Some responses may return content as a string.
                            if (content.ValueKind == JsonValueKind.String)
                            {
                                var contentText = (content.GetString() ?? "").Trim();
                                if (!string.IsNullOrWhiteSpace(contentText))
                                {
                                    if (textSb.Length > 0) textSb.Append("\n");
                                    textSb.Append(contentText);
                                    continue;
                                }
                            }

                            if (content.ValueKind != JsonValueKind.Object)
                                continue;

                            // Some variants include content.text.
                            if (content.TryGetProperty("text", out var contentTextEl))
                            {
                                var contentText = (contentTextEl.ValueKind == JsonValueKind.String ? (contentTextEl.GetString() ?? "") : contentTextEl.ToString()).Trim();
                                if (!string.IsNullOrWhiteSpace(contentText))
                                {
                                    if (textSb.Length > 0) textSb.Append("\n");
                                    textSb.Append(contentText);
                                    continue;
                                }
                            }

                            if (!content.TryGetProperty("parts", out var parts))
                                continue;

                            if (parts.ValueKind == JsonValueKind.Array)
                            {
                                sawPartsArray = true;
                                partsLength = parts.GetArrayLength();

                                foreach (var part in parts.EnumerateArray())
                                {
                                    partValueKinds.Add(part.ValueKind.ToString());

                                    if (part.ValueKind == JsonValueKind.String)
                                    {
                                        var chunk = (part.GetString() ?? "").Trim();
                                        if (!string.IsNullOrWhiteSpace(chunk))
                                        {
                                            if (textSb.Length > 0) textSb.Append("\n");
                                            textSb.Append(chunk);
                                        }
                                        continue;
                                    }

                                    if (part.ValueKind != JsonValueKind.Object) continue;

                                    if (part.TryGetProperty("text", out var textEl))
                                    {
                                        var chunk = (textEl.ValueKind == JsonValueKind.String ? (textEl.GetString() ?? "") : textEl.ToString()).Trim();
                                        if (!string.IsNullOrWhiteSpace(chunk))
                                        {
                                            if (textSb.Length > 0) textSb.Append("\n");
                                            textSb.Append(chunk);
                                        }
                                        continue;
                                    }

                                    // Track non-text parts so we can emit a more actionable error.
                                    if (part.TryGetProperty("functionCall", out _)) nonTextPartKinds.Add("functionCall");
                                    else if (part.TryGetProperty("functionResponse", out _)) nonTextPartKinds.Add("functionResponse");
                                    else if (part.TryGetProperty("inlineData", out _)) nonTextPartKinds.Add("inlineData");
                                    else if (part.TryGetProperty("fileData", out _)) nonTextPartKinds.Add("fileData");
                                    else
                                    {
                                        foreach (var prop in part.EnumerateObject())
                                        {
                                            nonTextPartKinds.Add(prop.Name);
                                            break;
                                        }
                                    }
                                }

                                continue;
                            }

                            // Sometimes parts is a single object/string instead of an array.
                            partValueKinds.Add(parts.ValueKind.ToString());
                            if (parts.ValueKind == JsonValueKind.String)
                            {
                                var chunk = (parts.GetString() ?? "").Trim();
                                if (!string.IsNullOrWhiteSpace(chunk))
                                {
                                    if (textSb.Length > 0) textSb.Append("\n");
                                    textSb.Append(chunk);
                                }
                            }
                            else if (parts.ValueKind == JsonValueKind.Object)
                            {
                                if (parts.TryGetProperty("text", out var textEl))
                                {
                                    var chunk = (textEl.ValueKind == JsonValueKind.String ? (textEl.GetString() ?? "") : textEl.ToString()).Trim();
                                    if (!string.IsNullOrWhiteSpace(chunk))
                                    {
                                        if (textSb.Length > 0) textSb.Append("\n");
                                        textSb.Append(chunk);
                                    }
                                }
                                else
                                {
                                    foreach (var prop in parts.EnumerateObject())
                                    {
                                        nonTextPartKinds.Add(prop.Name);
                                        break;
                                    }
                                }
                            }
                        }

                        assistantText = textSb.ToString();
                    }
                    }
                    catch
                    {
                        assistantText = "";
                    }

                var tokensUsed = 0;
                try
                {
                    if (doc.RootElement.TryGetProperty("usageMetadata", out var usage) &&
                        usage.TryGetProperty("candidatesTokenCount", out var tok))
                        tokensUsed = tok.GetInt32();
                }
                catch
                {
                }

                assistantText = assistantText.Trim();
                if (string.IsNullOrWhiteSpace(assistantText))
                {
                    try
                    {
                        var preview = responseJson.Length > 4000 ? responseJson.Substring(0, 4000) : responseJson;
                        AppLogger.LogWarning($"[Gemini] Empty text response. model={usedModel} finishReason={finishReason} promptBlockReason={promptBlockReason} candidates={candidateCount} sawParts={sawPartsArray} partsLen={partsLength} partKinds={string.Join(",", partValueKinds)} nonTextKinds={string.Join(",", nonTextPartKinds)} preview={preview}");
                    }
                    catch
                    {
                    }

                    if (!string.IsNullOrWhiteSpace(promptBlockReason))
                    {
                        return new AIResponse
                        {
                            Success = false,
                            Error = $"🛑 **Gemini blocked the prompt**\n\nReason: {promptBlockReason}\n\nTry rephrasing your request.",
                            Model = usedModel
                        };
                    }

                    if (!string.IsNullOrWhiteSpace(finishReason) &&
                        finishReason.Equals("SAFETY", StringComparison.OrdinalIgnoreCase))
                    {
                        return new AIResponse
                        {
                            Success = false,
                            Error = "🛑 **Gemini blocked the response (safety)**\n\nTry rephrasing your request. If this happens repeatedly for normal queries, the model selection may be invalid for your API key.",
                            Model = usedModel
                        };
                    }

                    if (doc.RootElement.TryGetProperty("candidates", out var candsEl) &&
                        (candsEl.ValueKind != JsonValueKind.Array || candsEl.GetArrayLength() == 0))
                    {
                        return new AIResponse
                        {
                            Success = false,
                            Error = "🔴 **Gemini returned no candidates**\n\nThe API succeeded, but no candidates were returned. This can happen if the prompt was blocked or the request format/model is unsupported.",
                            Model = usedModel
                        };
                    }

                    var nonTextKinds = nonTextPartKinds.Count > 0
                        ? string.Join(", ", nonTextPartKinds)
                        : "(none)";

                    var partKindsSummary = partValueKinds.Count > 0
                        ? string.Join(", ", partValueKinds)
                        : "(unknown)";

                    var logPath = "";
                    try { logPath = AppLogger.GetCurrentLogFilePath(); } catch { }

                    return new AIResponse
                    {
                        Success = false,
                        Error = $"🔴 **Gemini returned no text**\n\nThe response didn't contain any text parts. Non-text parts: {nonTextKinds}. Part value kinds: {partKindsSummary}.\n\nLog: {logPath}\n\nIf this happens for normal prompts, try switching the Gemini model (Flash/Pro) or verify the `gemini` key has access to the selected model.",
                        Model = usedModel
                    };
                }

                    return new AIResponse
                    {
                        Success = true,
                        Content = assistantText,
                        TokensUsed = tokensUsed,
                        Model = usedModel
                    };
                }
            }
            catch (Exception ex)
            {
                return new AIResponse { Success = false, Error = $"🔴 **Gemini Connection Error**\n\n{ex.Message}\n\nCheck your internet connection and API key." };
            }
        }

        public async Task<bool> TestConnectionAsync()
        {
            if (!IsConfigured) return false;
            try
            {
                var testMessages = new List<object> { new { role = "user", content = "Hello" } };
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
