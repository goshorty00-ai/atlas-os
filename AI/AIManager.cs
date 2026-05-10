using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using AtlasAI.Core;
using AtlasAI.Monetization;
using AtlasAI.Settings;

namespace AtlasAI.AI
{
    public static class AIManager
    {
        public enum AITaskBucket
        {
            Chat,
            Code,
            Generation,
        }

        public sealed class AIRuntimeContext
        {
            public string ActiveModule { get; init; } = "";
            public string ActivePage { get; init; } = "";
            public string WorkspacePath { get; init; } = "";
            public string SmartHomeContext { get; init; } = "";
            public string ToolContext { get; init; } = "";
            public string ModuleState { get; init; } = "";
            public string AdditionalInstructions { get; init; } = "";
        }

        public sealed class AIRoutingRequest
        {
            public string Module { get; init; } = "Unknown";
            public List<object> Messages { get; init; } = new();
            public int MaxTokens { get; init; } = 500;
            public AITaskBucket? BucketHint { get; init; }
            public AIProviderType? PreferredProviderOverride { get; init; }
            public string PreferredModelOverride { get; init; } = "";
            public AIRuntimeContext? RuntimeContext { get; init; }
        }

        private sealed class AIRoutePlan
        {
            public required AITaskBucket Bucket { get; init; }
            public required List<AIProviderType> ProviderOrder { get; init; }
            public AIProviderType SelectedProvider { get; init; }
            public AIProviderType PreferredProvider { get; init; }
            public bool AutoRoutingEnabled { get; init; }
            public string RoutingBehavior => AutoRoutingEnabled ? "bucket-router" : "selected-provider-first";
            public string Summary => $"bucket={Bucket.ToString().ToLowerInvariant()}; selected={SelectedProvider}; preferred={PreferredProvider}; order={string.Join('>', ProviderOrder)}; autoRouting={(AutoRoutingEnabled ? "on" : "off")}; behavior={RoutingBehavior}";
        }

        // This app is used interactively and can generate large prompts (audits, code context, etc.).
        // Keep the non-currency soft caps high enough to avoid false lockouts.
        private const int DailyChatTokenCap = 500_000;
        private const int DailyTtsCharacterCap = 250_000;
        private const int DailyHeavyCallCap = 200;
        private const int DefaultRouterTokenThreshold = 512;
        private const int DefaultRouterComplexityThreshold = 9;
        private const decimal DefaultDailySpendCap = 5.00m;
        private const string RoutingModeThresholds = "thresholds";
        private const string RoutingModeManual = "manual";
        private const string RoutingModeCheap = "cheap";
        private const string RoutingModeSmart = "smart";
        private const string CostModeBalanced = "balanced";
        private const string CostModeBudget = "budget";
        private const string CostModePerformance = "performance";

        private static readonly string[] CodeRoutingKeywords =
        {
            "code", "coding", "repo", "repository", "workspace", "solution", "compile", "build", "debug",
            "stack trace", "exception", "patch", "implement", "implementation", "refactor", "class", "method",
            "function", "variable", "xaml", "csproj", "sln", "json", "yaml", "xml", "markdown", "file",
            "folder", "directory", "bug", "fix", "test", "unit test", "integration test", "trace", "diff",
            "debugging", "implementation plan", "implementation planning", "plan the implementation", "codebase",
            "explain the code", "code explanation", "file analysis", "analyze this file", "analyse this file",
            "patch generation", "generate a patch", "read this file", "inspect this file", "root cause"
        };

        private static readonly string[] GenerationRoutingKeywords =
        {
            "generate", "generation", "create image", "make image", "design", "caption", "campaign", "content",
            "thumbnail", "poster", "artwork", "visual", "multimodal", "creative", "copywriting", "ad copy",
            "social post", "instagram", "tiktok", "youtube script", "prompt", "storyboard", "mockup", "brand",
            "creative prompt", "prompt generation", "visual generation", "image prompt", "concept art", "moodboard",
            "video prompt", "audio prompt", "image generation", "video generation", "creative brief", "media-heavy",
            "gallery", "lookbook", "shot list", "voiceover script"
        };

        private static readonly string[] GenerationRoutingModuleHints =
        {
            "campaign",
            "content",
            "lyrics",
            "social",
            "creative",
            "multimodal",
        };

        // Default Model Configurations
        public const string DefaultOpenAICheapModel = "gpt-4o-mini";
        public const string DefaultOpenAISmartModel = "gpt-4o"; // Updated to stable 4o
        public const string DefaultClaudeCheapModel = "claude-3-haiku-20240307";
        // Prefer "-latest" to avoid 404s when dated IDs are unavailable.
        public const string DefaultClaudeSmartModel = "claude-3-5-sonnet-latest";
        // Gemini model IDs change frequently; avoid "*-latest" aliases which can 404 on some API versions.
        public const string DefaultGeminiCheapModel = "gemini-2.5-flash";
        public const string DefaultGeminiSmartModel = "gemini-2.5-pro";

        private static readonly object budgetLock = new();
        private static DateOnly budgetDay = DateOnly.FromDateTime(DateTime.UtcNow);
        private static decimal spendUsedToday;
        private static int chatTokensUsedToday;
        private static int ttsCharactersUsedToday;
        private static int heavyCallsUsedToday;

        private static readonly Dictionary<AIProviderType, IAIProvider> providers = new();
        private static AIProviderType activeProvider = AIProviderType.OpenAI;
        private static bool autoModeEnabled;
        private static string routingMode = RoutingModeThresholds;
        private static string costMode = CostModeBalanced;
        private static int routerTokenThreshold = DefaultRouterTokenThreshold;
        private static int routerComplexityThreshold = DefaultRouterComplexityThreshold;
        private static decimal dailySpendCap = DefaultDailySpendCap;
        private static readonly Dictionary<AIProviderType, string> manualModelByProvider = new();
        private static readonly Dictionary<AIProviderType, string> autoCheapModelByProvider = new();
        private static readonly Dictionary<AIProviderType, string> autoSmartModelByProvider = new();
        
        public static event Action<AIProviderType>? ProviderChanged;

        private static string GetKeyName(AIProviderType providerType)
        {
            return providerType switch
            {
                AIProviderType.OpenAI => "openai",
                AIProviderType.Claude => "claude",
                AIProviderType.Gemini => "gemini",
                _ => providerType.ToString().ToLowerInvariant()
            };
        }

        private static async Task<bool> TryAutoConfigureProviderFromStoreAsync(AIProviderType providerType)
        {
            try
            {
                if (!providers.TryGetValue(providerType, out var provider) || provider == null)
                    return false;
                if (provider.IsConfigured)
                    return true;

                if (!AtlasAI.Core.AiKeysStore.TryGetPlaintextKey(GetKeyName(providerType), out var key))
                    return false;
                if (string.IsNullOrWhiteSpace(key))
                    return false;

                return await provider.ConfigureAsync(new Dictionary<string, string> { ["ApiKey"] = key });
            }
            catch
            {
                return false;
            }
        }

        private static bool IsTransientFailure(AIResponse? response)
        {
            try
            {
                if (response == null) return true;
                var e = (response.Error ?? "").Trim();
                if (string.IsNullOrWhiteSpace(e)) return false;

                // Heuristics across providers.
                if (e.Contains("503", StringComparison.OrdinalIgnoreCase)) return true;
                if (e.Contains("429", StringComparison.OrdinalIgnoreCase)) return true;
                if (e.Contains("ServiceUnavailable", StringComparison.OrdinalIgnoreCase)) return true;
                if (e.Contains("UNAVAILABLE", StringComparison.OrdinalIgnoreCase)) return true;
                if (e.Contains("timeout", StringComparison.OrdinalIgnoreCase)) return true;
                if (e.Contains("temporarily", StringComparison.OrdinalIgnoreCase)) return true;
                if (e.Contains("try again", StringComparison.OrdinalIgnoreCase)) return true;

                return false;
            }
            catch
            {
                return false;
            }
        }

        static AIManager()
        {
            // Ensure any legacy/plaintext key files are upgraded to DPAPI-protected secrets.
            AiKeysStore.UpgradeAll();

            // Register providers
            providers[AIProviderType.Claude] = new ClaudeProvider();
            providers[AIProviderType.OpenAI] = new OpenAIProvider();
            providers[AIProviderType.Gemini] = new GeminiProvider();

            try { SettingsStore.SettingsChanged += (_, __) => LoadSettings(); } catch { }
            
            LoadSettings();
        }

        public static async Task<bool> ConfigureProviderAsync(AIProviderType providerType, Dictionary<string, string> config)
        {
            if (providers.TryGetValue(providerType, out var provider))
            {
                var success = await provider.ConfigureAsync(config);
                if (success)
                {
                    SaveSettings();
                }
                return success;
            }
            return false;
        }

        public static async Task<bool> SetActiveProviderAsync(AIProviderType providerType)
        {
            System.Diagnostics.Debug.WriteLine($"Setting active provider to: {providerType}");
            if (providers.TryGetValue(providerType, out var provider))
            {
                activeProvider = providerType;
				// If this provider isn't configured yet, try to hydrate it from the admin-provisioned key store.
				try { await TryAutoConfigureProviderFromStoreAsync(providerType); } catch { }
                SaveSettings();
                ProviderChanged?.Invoke(providerType);
                System.Diagnostics.Debug.WriteLine($"Active provider set successfully. IsConfigured: {provider.IsConfigured}");
                return true;
            }
            System.Diagnostics.Debug.WriteLine($"Failed to find provider: {providerType}");
            return false;
        }

        public static AIProviderType GetActiveProvider() => activeProvider;

        public static IAIProvider? GetProvider(AIProviderType providerType)
        {
            return providers.TryGetValue(providerType, out var provider) ? provider : null;
        }

        public static IAIProvider? GetActiveProviderInstance()
        {
            var provider = GetProvider(activeProvider);
            System.Diagnostics.Debug.WriteLine($"Getting active provider: {activeProvider}, Found: {provider != null}, Configured: {provider?.IsConfigured}");
            return provider;
        }

        public static List<IAIProvider> GetAllProviders()
        {
            return providers.Values.ToList();
        }

        public static List<IAIProvider> GetConfiguredProviders()
        {
            return providers.Values.Where(p => p.IsConfigured).ToList();
        }

        public static string GetSelectedModel() => GetManualSelectedModel(activeProvider);

        public static string GetSelectedModel(AIProviderType providerType) => GetManualSelectedModel(providerType);

        public static void SetSelectedModel(string modelId)
        {
            SetSelectedModel(activeProvider, modelId);
        }

        public static void SetSelectedModel(AIProviderType providerType, string modelId)
        {
            manualModelByProvider[providerType] = (modelId ?? "").Trim();
            SaveSettings();
        }

        public static bool GetAutoModeEnabled() => autoModeEnabled;

        public static void SetAutoModeEnabled(bool enabled)
        {
            autoModeEnabled = enabled;
            SaveSettings();
        }

        public static string GetRoutingMode() => routingMode;

        public static void SetRoutingMode(string mode)
        {
            routingMode = NormalizeRoutingMode(mode);
            SaveSettings();
        }

        public static string GetCostMode() => costMode;

        public static void SetCostMode(string mode)
        {
            costMode = NormalizeCostMode(mode);
            SaveSettings();
        }

        public static int GetRouterTokenThreshold() => routerTokenThreshold;

        public static void SetRouterTokenThreshold(int threshold)
        {
            routerTokenThreshold = Math.Max(1, threshold);
            SaveSettings();
        }

        public static int GetRouterComplexityThreshold() => routerComplexityThreshold;

        public static void SetRouterComplexityThreshold(int threshold)
        {
            routerComplexityThreshold = Math.Max(1, threshold);
            SaveSettings();
        }

        public static decimal GetDailySpendCap() => dailySpendCap;

        public static void SetDailySpendCap(decimal cap)
        {
            dailySpendCap = cap < 0m ? 0m : decimal.Round(cap, 4, MidpointRounding.AwayFromZero);
            SaveSettings();
        }

        public static decimal GetDailySpendUsed()
        {
            lock (budgetLock)
            {
                EnsureBudgetWindowLocked();
                return spendUsedToday;
            }
        }

        public static decimal? GetRemainingDailyBudget()
        {
            lock (budgetLock)
            {
                EnsureBudgetWindowLocked();
                if (dailySpendCap <= 0m)
                    return null;

                var remaining = dailySpendCap - spendUsedToday;
                if (remaining < 0m)
                    remaining = 0m;

                return decimal.Round(remaining, 4, MidpointRounding.AwayFromZero);
            }
        }

        public static int GetChatTokensUsedToday()
        {
            lock (budgetLock)
            {
                EnsureBudgetWindowLocked();
                return chatTokensUsedToday;
            }
        }

        public static int GetHeavyCallsUsedToday()
        {
            lock (budgetLock)
            {
                EnsureBudgetWindowLocked();
                return heavyCallsUsedToday;
            }
        }

        public static string GetAutoCheapModel(AIProviderType providerType)
        {
            if (autoCheapModelByProvider.TryGetValue(providerType, out var v) && !string.IsNullOrWhiteSpace(v))
                return v;
            return GetDefaultCheapModel(providerType);
        }

        public static void SetAutoCheapModel(AIProviderType providerType, string modelId)
        {
            autoCheapModelByProvider[providerType] = (modelId ?? "").Trim();
            SaveSettings();
        }

        public static string GetAutoSmartModel(AIProviderType providerType)
        {
            if (autoSmartModelByProvider.TryGetValue(providerType, out var v) && !string.IsNullOrWhiteSpace(v))
                return v;

            return GetDefaultSmartModel(providerType);
        }

        public static void SetAutoSmartModel(AIProviderType providerType, string modelId)
        {
            autoSmartModelByProvider[providerType] = (modelId ?? "").Trim();
            SaveSettings();
        }

        public static string GetManualSelectedModel(AIProviderType providerType)
        {
            if (manualModelByProvider.TryGetValue(providerType, out var v) && !string.IsNullOrWhiteSpace(v))
                return v;

            return GetDefaultSmartModel(providerType);
        }

        private static string GetDefaultCheapModel(AIProviderType providerType)
        {
            return providerType switch
            {
                AIProviderType.OpenAI => DefaultOpenAICheapModel,
                AIProviderType.Gemini => DefaultGeminiCheapModel,
                _ => DefaultClaudeCheapModel
            };
        }

        private static string GetDefaultSmartModel(AIProviderType providerType)
        {
            return providerType switch
            {
                AIProviderType.OpenAI => DefaultOpenAISmartModel,
                AIProviderType.Gemini => DefaultGeminiSmartModel,
                _ => DefaultClaudeSmartModel
            };
        }

        private static (string cheap, string smart) GetAutoModels(AIProviderType providerType)
        {
            return (GetAutoCheapModel(providerType), GetAutoSmartModel(providerType));
        }

        public static (string cheap, string smart) GetAutoModelsForActiveProvider() => GetAutoModels(activeProvider);

        private static string GetEffectiveModel(string module, List<object> messages, int maxTokens)
        {
            return GetEffectiveModelForProvider(activeProvider, module, messages, maxTokens);
        }

        private static string GetEffectiveModelForProvider(AIProviderType providerType, string module, List<object> messages, int maxTokens)
        {
            if (!autoModeEnabled)
                return GetManualSelectedModel(providerType);

            var (cheap, smart) = GetAutoModels(providerType);
            return GetEffectiveRoutingBehavior() switch
            {
                RoutingModeManual => GetManualSelectedModel(providerType),
                RoutingModeCheap => cheap,
                RoutingModeSmart => smart,
                _ => IsComplexRequest(module, TryExtractLatestUserText(messages), maxTokens, messages) ? smart : cheap
            };
        }

        private static bool IsComplexRequest(string module, string userText, int maxTokens, List<object> messages)
        {
            var complexityScore = 0;

            if (maxTokens >= Math.Max(routerTokenThreshold, 1))
                complexityScore += 3;

            var m = (module ?? "").Trim();
            if (m.Contains("code", StringComparison.OrdinalIgnoreCase) ||
                m.Contains("ide", StringComparison.OrdinalIgnoreCase) ||
                m.Contains("repair", StringComparison.OrdinalIgnoreCase) ||
                m.Contains("verify", StringComparison.OrdinalIgnoreCase))
                complexityScore += 4;

            if (messages != null && messages.Count >= 18)
                complexityScore += 3;

            var t = (userText ?? "").Trim();
            if (t.Length >= 700)
                complexityScore += 2;
            if (t.Count(c => c == '\n') >= 8)
                complexityScore += 1;
            if (t.Contains("```", StringComparison.Ordinal))
                complexityScore += 3;

            var keywords = new[]
            {
                "exception", "stack trace", "compile", "build", "refactor", "regression", "optimize",
                "xaml", "csproj", "nullreference", "crash", "debug", "fix", "error", "typecheck"
            };
            var keywordHits = 0;
            for (var i = 0; i < keywords.Length; i++)
            {
                if (t.Contains(keywords[i], StringComparison.OrdinalIgnoreCase))
                    keywordHits++;
            }

            complexityScore += Math.Min(4, keywordHits);

            return complexityScore >= Math.Max(routerComplexityThreshold, 1);
        }

        private static string TryExtractLatestUserText(List<object> messages)
        {
            if (messages == null || messages.Count == 0) return "";

            for (var i = messages.Count - 1; i >= 0; i--)
            {
                var msg = messages[i];
                if (msg == null) continue;
                try
                {
                    var json = JsonSerializer.Serialize(msg);
                    using var doc = JsonDocument.Parse(json);
                    if (!doc.RootElement.TryGetProperty("role", out var roleEl)) continue;
                    var role = roleEl.GetString() ?? "";
                    if (!string.Equals(role, "user", StringComparison.OrdinalIgnoreCase)) continue;
                    if (!doc.RootElement.TryGetProperty("content", out var contentEl)) continue;
                    return contentEl.ValueKind == JsonValueKind.String ? (contentEl.GetString() ?? "") : contentEl.ToString();
                }
                catch
                {
                }
            }

            return "";
        }

        public static async Task<AIResponse> SendMessageAsync(List<object> messages, int maxTokens = 500, System.Threading.CancellationToken ct = default)
        {
            return await SendMessageAsync(new AIRoutingRequest
            {
                Module = "Unknown",
                Messages = messages ?? new List<object>(),
                MaxTokens = maxTokens,
            }, ct);
        }

        public static async Task<AIResponse> SendMessageAsync(string module, List<object> messages, int maxTokens = 500, System.Threading.CancellationToken ct = default)
        {
            return await SendMessageAsync(new AIRoutingRequest
            {
                Module = string.IsNullOrWhiteSpace(module) ? "Unknown" : module,
                Messages = messages ?? new List<object>(),
                MaxTokens = maxTokens,
            }, ct);
        }

        public static async Task<AIResponse> SendMessageAsync(AIRoutingRequest request, System.Threading.CancellationToken ct = default)
        {
            request ??= new AIRoutingRequest();
            var normalizedRequest = NormalizeRequest(request);
            var routePlan = BuildRoutePlan(normalizedRequest);
            var messages = AugmentMessagesWithRuntimeContext(normalizedRequest, routePlan.Bucket);
            var requestId = AIDebugLogger.GenerateRequestId();
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            AIResponse? lastFailure = null;

            foreach (var providerType in routePlan.ProviderOrder)
            {
                if (!providers.TryGetValue(providerType, out var provider) || provider == null)
                    continue;

                if (!provider.IsConfigured)
                {
                    try { await TryAutoConfigureProviderFromStoreAsync(providerType); } catch { }
                    if (!provider.IsConfigured)
                        continue;
                }

                var modelToUse = ResolveModelForRequest(providerType, normalizedRequest, routePlan, messages);
                var usageAuthorization = AtlasEconomyService.Instance.AuthorizeUsage(normalizedRequest.Module, "chat", normalizedRequest.MaxTokens, providerType, modelToUse);

                if (!usageAuthorization.Allowed)
                {
                    lastFailure = new AIResponse
                    {
                        Success = false,
                        Error = usageAuthorization.Reason,
                        Provider = providerType,
                        TaskBucket = routePlan.Bucket.ToString().ToLowerInvariant(),
                        RouteSummary = routePlan.Summary,
                    };
                    continue;
                }

                if (!TrySpend(normalizedRequest.Module, "chat", normalizedRequest.MaxTokens, providerType, modelToUse, out var denyReason))
                {
                    lastFailure = new AIResponse
                    {
                        Success = false,
                        Error = denyReason,
                        Provider = providerType,
                        TaskBucket = routePlan.Bucket.ToString().ToLowerInvariant(),
                        RouteSummary = routePlan.Summary,
                    };
                    continue;
                }

                var providerName = providerType.ToString();
                var promptLength = messages.Sum(m => m?.ToString()?.Length ?? 0);
                var successfulUsageAuthorization = usageAuthorization;
                
                // Extract system prompt and user message for detailed logging
                string systemPrompt = "";
                string userMessage = "";
                foreach (var msg in messages)
                {
                    var json = System.Text.Json.JsonSerializer.Serialize(msg);
                    if (json.Contains("\"role\":\"system\"") || json.Contains("\"role\": \"system\""))
                        systemPrompt = json;
                    else if (json.Contains("\"role\":\"user\"") || json.Contains("\"role\": \"user\""))
                        userMessage = json;
                }
                
                // STEP 30: Enhanced AI debug logging
                AIDebugLogger.LogRequest(
                    requestId,
                    providerName,
                    modelToUse,
                    0.7, // Default temperature
                    systemPrompt,
                    userMessage,
                    promptLength
                );
                
                // Legacy logging for compatibility
                Voice.StabilizationLogger.LogLLMRequest(providerName, modelToUse, promptLength);
                System.Diagnostics.Debug.WriteLine($"[AIManager] Sending to {providerName}/{modelToUse}, prompt length: {promptLength}");
                
                try
                {
                    var response = await provider.SendMessageAsync(messages, modelToUse, normalizedRequest.MaxTokens, ct);

                    // One-shot retry for transient errors (e.g., Gemini high-demand 503).
                    if ((response == null || !response.Success) && IsTransientFailure(response))
                    {
                        try
                        {
                            await Task.Delay(900, ct);
                            var retry = await provider.SendMessageAsync(messages, modelToUse, normalizedRequest.MaxTokens, ct);
                            if (retry != null)
                                response = retry;
                        }
                        catch
                        {
                        }
                    }
                    stopwatch.Stop();

                    // Treat known placeholder content as a failure so we can fall back.
                    try
                    {
                        var content = (response?.Content ?? "").Trim();
                        if (response != null && response.Success &&
                            (string.IsNullOrWhiteSpace(content) ||
                             string.Equals(content, "Sorry, I couldn't generate a response.", StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(content, "Sorry, I could not generate a response.", StringComparison.OrdinalIgnoreCase)))
                        {
                            response.Success = false;
                            response.Error = "Active AI provider returned no usable text.";
                        }
                    }
                    catch
                    {
                    }

                    if (response != null && response.Success)
                    {
                        try
                        {
                            AtlasEconomyService.Instance.CommitUsage(successfulUsageAuthorization);
                            RecordSpend(normalizedRequest.Module, "chat", normalizedRequest.MaxTokens, providerType, modelToUse);

                            var latestUserText = TryExtractLatestUserText(messages);
                            // Preserve strict JSON contract for Quiz responses; post-processing can rewrite text and break JSON.
                            if (!string.Equals(normalizedRequest.Module, "Quiz", StringComparison.OrdinalIgnoreCase))
                            {
                                response.Content = ResponsePostProcessor.CleanAssistantText(response.Content, latestUserText);
                            }
                            else
                            {
                                response.Content = (response.Content ?? string.Empty).Trim();
                            }
                            response.Provider = providerType;
                            response.Model = string.IsNullOrWhiteSpace(response.Model) ? modelToUse : response.Model;
                            response.TaskBucket = routePlan.Bucket.ToString().ToLowerInvariant();
                            response.RouteSummary = routePlan.Summary + $"; effective={providerType}; model={response.Model}";
                        }
                        catch
                        {
                        }
                    }
                    
                    // STEP 30: Log response with latency
                    AIDebugLogger.LogResponse(
                        requestId,
                        response?.Success == true ? "success" : "error",
                        response?.Content?.Length ?? 0,
                        stopwatch.ElapsedMilliseconds,
                        response?.Content ?? response?.Error ?? ""
                    );
                    
                    // Legacy logging
                    Voice.StabilizationLogger.LogLLMResponse(providerName, response?.Content?.Length ?? 0, response?.Success == true, response?.Error);
                    System.Diagnostics.Debug.WriteLine($"[AIManager] Response: Success={response?.Success == true}, Length={response?.Content?.Length ?? 0}, Error={response?.Error ?? "none"}");
                    
                    if (response != null && response.Success)
                        return response;

                    lastFailure = response ?? new AIResponse
                    {
                        Success = false,
                        Error = "AI temporarily unavailable: no response was returned.",
                        Provider = providerType,
                        TaskBucket = routePlan.Bucket.ToString().ToLowerInvariant(),
                        RouteSummary = routePlan.Summary,
                    };
                }
                catch (Exception ex)
                {
                    stopwatch.Stop();
                    
                    // STEP 30: Log error with user-facing reason
                    AIDebugLogger.LogError(
                        requestId,
                        ex.GetType().Name,
                        ex.Message,
                        "AI temporarily unavailable - please try again"
                    );
                    
                    Voice.StabilizationLogger.LogLLMResponse(providerName, 0, false, ex.Message);
                    
                    lastFailure = new AIResponse
                    {
                        Success = false,
                        Error = $"AI temporarily unavailable: {ex.Message}",
                        Provider = providerType,
                        TaskBucket = routePlan.Bucket.ToString().ToLowerInvariant(),
                        RouteSummary = routePlan.Summary,
                    };
                }
            }
            
            // Provide helpful guidance when no API key is configured
            var providerDisplayName = routePlan.PreferredProvider switch
            {
                AIProviderType.Claude => "Claude (Anthropic)",
                AIProviderType.OpenAI => "OpenAI",
                AIProviderType.Gemini => "Gemini (Google)",
                _ => "AI"
            };
            
            AIDebugLogger.LogError(
                requestId,
                "NotConfigured",
                lastFailure?.Error ?? "No API key configured",
                $"{providerDisplayName} API key required"
            );
            
            Voice.StabilizationLogger.LogLLMResponse(routePlan.PreferredProvider.ToString(), 0, false, lastFailure?.Error ?? "Not configured");
            return lastFailure ?? new AIResponse
            {
                Success = false,
                Error = "🤖 **AI Not Configured**\n\n" +
                        "AI features are disabled on this installation. " +
                        "Only an administrator can configure AI providers and API keys.\n\n" +
                        "Basic features like screenshots, system scan, and commands still work without AI.",
                Provider = routePlan.PreferredProvider,
                TaskBucket = routePlan.Bucket.ToString().ToLowerInvariant(),
                RouteSummary = routePlan.Summary,
            };
        }

        private static AIRoutingRequest NormalizeRequest(AIRoutingRequest request)
        {
            return new AIRoutingRequest
            {
                Module = string.IsNullOrWhiteSpace(request.Module) ? "Unknown" : request.Module.Trim(),
                Messages = request.Messages ?? new List<object>(),
                MaxTokens = request.MaxTokens <= 0 ? 500 : request.MaxTokens,
                BucketHint = request.BucketHint,
                PreferredProviderOverride = request.PreferredProviderOverride,
                PreferredModelOverride = (request.PreferredModelOverride ?? string.Empty).Trim(),
                RuntimeContext = request.RuntimeContext,
            };
        }

        private static AIRoutePlan BuildRoutePlan(AIRoutingRequest request)
        {
            var bucket = ClassifyTaskBucket(request);
            var selectedProvider = request.PreferredProviderOverride ?? activeProvider;
            var autoRoutingEnabled = IsAutoRoutingEnabled(request);
            var preferredProvider = autoRoutingEnabled ? GetDefaultProviderOrder(bucket).First() : selectedProvider;
            var order = BuildProviderOrder(bucket, preferredProvider, autoRoutingEnabled);

            return new AIRoutePlan
            {
                Bucket = bucket,
                SelectedProvider = selectedProvider,
                PreferredProvider = preferredProvider,
                ProviderOrder = order,
                AutoRoutingEnabled = autoRoutingEnabled,
            };
        }

        private static bool IsAutoRoutingEnabled(AIRoutingRequest request)
        {
            if (request.PreferredProviderOverride.HasValue)
                return false;

            if (!autoModeEnabled)
                return false;

            return !string.Equals(NormalizeRoutingMode(routingMode), RoutingModeManual, StringComparison.OrdinalIgnoreCase);
        }

        private static List<AIProviderType> BuildProviderOrder(AITaskBucket bucket, AIProviderType preferredProvider, bool autoRoutingEnabled)
        {
            if (!autoRoutingEnabled)
                return new List<AIProviderType> { preferredProvider };

            var order = new List<AIProviderType> { preferredProvider };
            foreach (var providerType in GetDefaultProviderOrder(bucket))
            {
                if (!order.Contains(providerType))
                    order.Add(providerType);
            }

            return order;
        }

        private static IReadOnlyList<AIProviderType> GetDefaultProviderOrder(AITaskBucket bucket)
        {
            return bucket switch
            {
                AITaskBucket.Chat => new[] { AIProviderType.OpenAI, AIProviderType.Claude, AIProviderType.Gemini },
                AITaskBucket.Code => new[] { AIProviderType.Claude, AIProviderType.OpenAI, AIProviderType.Gemini },
                AITaskBucket.Generation => new[] { AIProviderType.Gemini, AIProviderType.OpenAI, AIProviderType.Claude },
                _ => new[] { AIProviderType.OpenAI, AIProviderType.Claude, AIProviderType.Gemini },
            };
        }

        private static AITaskBucket ClassifyTaskBucket(AIRoutingRequest request)
        {
            if (request.BucketHint.HasValue)
                return request.BucketHint.Value;

            var module = (request.Module ?? string.Empty).Trim().ToLowerInvariant();
            var latestUserText = TryExtractLatestUserText(request.Messages).Trim();
            var combinedText = $"{module}\n{latestUserText}";

            if (ContainsRoutingKeyword(combinedText, CodeRoutingKeywords) ||
                (!string.IsNullOrWhiteSpace(request.RuntimeContext?.WorkspacePath) && ContainsRoutingKeyword(latestUserText, CodeRoutingKeywords)) ||
                module.Contains("code", StringComparison.Ordinal) ||
                module.Contains("repair", StringComparison.Ordinal) ||
                module.Contains("agent", StringComparison.Ordinal) ||
                module.Contains("repo", StringComparison.Ordinal) ||
                module.Contains("debug", StringComparison.Ordinal) ||
                module.Contains("patch", StringComparison.Ordinal) ||
                module.Contains("implementation", StringComparison.Ordinal) ||
                latestUserText.Contains("```", StringComparison.Ordinal))
            {
                return AITaskBucket.Code;
            }

            if (ContainsRoutingKeyword(combinedText, GenerationRoutingKeywords) ||
                ContainsRoutingKeyword(module, GenerationRoutingModuleHints))
            {
                return AITaskBucket.Generation;
            }

            return AITaskBucket.Chat;
        }

        private static bool ContainsRoutingKeyword(string text, IReadOnlyList<string> keywords)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            for (var i = 0; i < keywords.Count; i++)
            {
                if (text.Contains(keywords[i], StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private static string ResolveModelForRequest(AIProviderType providerType, AIRoutingRequest request, AIRoutePlan routePlan, List<object> messages)
        {
            if (request.PreferredProviderOverride == providerType && !string.IsNullOrWhiteSpace(request.PreferredModelOverride))
                return request.PreferredModelOverride;

            if (!routePlan.AutoRoutingEnabled && providerType == routePlan.PreferredProvider)
                return GetManualSelectedModel(providerType);

            return GetEffectiveModelForProvider(providerType, request.Module, messages, request.MaxTokens);
        }

        private static List<object> AugmentMessagesWithRuntimeContext(AIRoutingRequest request, AITaskBucket bucket)
        {
            var contextText = BuildRuntimeContextSystemMessage(request, bucket);
            if (string.IsNullOrWhiteSpace(contextText))
                return request.Messages;

            var augmented = new List<object>(request.Messages.Count + 1)
            {
                new { role = "system", content = contextText }
            };
            augmented.AddRange(request.Messages);
            return augmented;
        }

        private static string BuildRuntimeContextSystemMessage(AIRoutingRequest request, AITaskBucket bucket)
        {
            if (request.RuntimeContext == null)
                return string.Empty;

            var sections = new List<string>
            {
                $"Runtime routing bucket: {bucket.ToString().ToLowerInvariant()}.",
            };

            if (!string.IsNullOrWhiteSpace(request.RuntimeContext.ActiveModule))
                sections.Add($"Active module: {request.RuntimeContext.ActiveModule}.");

            if (!string.IsNullOrWhiteSpace(request.RuntimeContext.ActivePage) && !string.Equals(request.RuntimeContext.ActivePage, request.RuntimeContext.ActiveModule, StringComparison.OrdinalIgnoreCase))
                sections.Add($"Active page: {request.RuntimeContext.ActivePage}.");

            if (!string.IsNullOrWhiteSpace(request.RuntimeContext.WorkspacePath))
                sections.Add($"Workspace path: {request.RuntimeContext.WorkspacePath}. Use repo/file grounded reasoning when the user asks for implementation, debugging, patch planning, or file analysis.");

            if (!string.IsNullOrWhiteSpace(request.RuntimeContext.SmartHomeContext))
                sections.Add($"Smart-home context: {request.RuntimeContext.SmartHomeContext}");

            if (!string.IsNullOrWhiteSpace(request.RuntimeContext.ToolContext))
                sections.Add($"Runtime tools/context: {request.RuntimeContext.ToolContext}");

            if (!string.IsNullOrWhiteSpace(request.RuntimeContext.ModuleState))
                sections.Add($"Module state: {request.RuntimeContext.ModuleState}");

            if (!string.IsNullOrWhiteSpace(request.RuntimeContext.AdditionalInstructions))
                sections.Add(request.RuntimeContext.AdditionalInstructions);

            sections.Add("If the runtime has already attempted an action, respond with a concise structured outcome that states action, status, and next step instead of pretending the action is still pending.");
            sections.Add("If no tool was executed but the next operational step is obvious from the active module or page context, prefer a structured recommendation with action, status, result, and next step over generic prose.");

            return string.Join("\n\n", sections.Where(static section => !string.IsNullOrWhiteSpace(section)));
        }

        public static bool TrySpend(string module, string kind, int units, out string reason)
        {
            return TrySpend(module, kind, units, activeProvider, GetBudgetModelFor(activeProvider, kind), out reason);
        }

        private static bool TrySpend(string module, string kind, int units, AIProviderType providerType, string modelId, out string reason)
        {
            reason = "";
            module = (module ?? "").Trim();
            kind = (kind ?? "").Trim();
            if (string.IsNullOrWhiteSpace(module)) module = "Unknown";
            if (string.IsNullOrWhiteSpace(kind)) kind = "chat";
            if (units <= 0) return true;

            lock (budgetLock)
            {
                EnsureBudgetWindowLocked();

                var estimatedSpend = EstimateSpend(providerType, modelId, kind, units);

                if (string.Equals(kind, "chat", StringComparison.OrdinalIgnoreCase))
                {
                    if (chatTokensUsedToday + units > DailyChatTokenCap)
                    {
                        reason = "AI limit reached for today (chat).";
                        return false;
                    }

                    if (dailySpendCap > 0m && spendUsedToday + estimatedSpend > dailySpendCap)
                    {
                        reason = $"AI daily spend cap reached ({spendUsedToday:0.####}/{dailySpendCap:0.####}).";
                        return false;
                    }

                    return true;
                }

                if (string.Equals(kind, "tts", StringComparison.OrdinalIgnoreCase))
                {
                    if (ttsCharactersUsedToday + units > DailyTtsCharacterCap)
                    {
                        reason = "AI limit reached for today (tts).";
                        return false;
                    }

                    if (dailySpendCap > 0m && spendUsedToday + estimatedSpend > dailySpendCap)
                    {
                        reason = $"AI daily spend cap reached ({spendUsedToday:0.####}/{dailySpendCap:0.####}).";
                        return false;
                    }

                    return true;
                }

                if (string.Equals(kind, "heavy", StringComparison.OrdinalIgnoreCase))
                {
                    if (heavyCallsUsedToday + units > DailyHeavyCallCap)
                    {
                        reason = "AI limit reached for today (heavy).";
                        return false;
                    }

                    if (dailySpendCap > 0m && spendUsedToday + estimatedSpend > dailySpendCap)
                    {
                        reason = $"AI daily spend cap reached ({spendUsedToday:0.####}/{dailySpendCap:0.####}).";
                        return false;
                    }

                    return true;
                }

                if (chatTokensUsedToday + units > DailyChatTokenCap)
                {
                    reason = "AI limit reached for today.";
                    return false;
                }

                if (dailySpendCap > 0m && spendUsedToday + estimatedSpend > dailySpendCap)
                {
                    reason = $"AI daily spend cap reached ({spendUsedToday:0.####}/{dailySpendCap:0.####}).";
                    return false;
                }

                return true;
            }
        }

        private static void RecordSpend(string module, string kind, int units, AIProviderType providerType, string modelId)
        {
            module = (module ?? "").Trim();
            kind = (kind ?? "").Trim();
            if (string.IsNullOrWhiteSpace(module)) module = "Unknown";
            if (string.IsNullOrWhiteSpace(kind)) kind = "chat";
            if (units <= 0) return;

            DateTime usageDayUtc;
            decimal spendUsed;
            int chatTokens;
            int ttsCharacters;
            int heavyCalls;

            lock (budgetLock)
            {
                EnsureBudgetWindowLocked();

                if (string.Equals(kind, "chat", StringComparison.OrdinalIgnoreCase))
                    chatTokensUsedToday += units;
                else if (string.Equals(kind, "tts", StringComparison.OrdinalIgnoreCase))
                    ttsCharactersUsedToday += units;
                else if (string.Equals(kind, "heavy", StringComparison.OrdinalIgnoreCase))
                    heavyCallsUsedToday += units;
                else
                    chatTokensUsedToday += units;

                spendUsedToday = decimal.Round(spendUsedToday + EstimateSpend(providerType, modelId, kind, units), 4, MidpointRounding.AwayFromZero);
                usageDayUtc = budgetDay.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
                spendUsed = spendUsedToday;
                chatTokens = chatTokensUsedToday;
                ttsCharacters = ttsCharactersUsedToday;
                heavyCalls = heavyCallsUsedToday;
            }

            PersistBudgetState(usageDayUtc, spendUsed, chatTokens, ttsCharacters, heavyCalls);
        }

        public static async Task<bool> TestActiveProviderAsync()
        {
            var provider = GetActiveProviderInstance();
            if (provider != null && provider.IsConfigured)
            {
                return await provider.TestConnectionAsync();
            }
            return false;
        }

        private static void LoadSettings()
        {
            try
            {
                var settings = SettingsStore.Current;

                if (Enum.TryParse<AIProviderType>(settings.AiRuntime.ActiveProvider, out var persistedProvider))
                    activeProvider = persistedProvider;

                autoModeEnabled = settings.AiRuntime.AutoModeEnabled;
                routingMode = NormalizeRoutingMode(settings.AiRuntime.RoutingMode);
                costMode = NormalizeCostMode(settings.AiRuntime.CostMode);
                routerTokenThreshold = Math.Max(1, settings.AiRuntime.RouterTokenThreshold);
                routerComplexityThreshold = Math.Max(1, settings.AiRuntime.RouterComplexityThreshold);
                dailySpendCap = settings.AiRuntime.DailySpendCap < 0m ? 0m : decimal.Round(settings.AiRuntime.DailySpendCap, 4, MidpointRounding.AwayFromZero);

                manualModelByProvider.Clear();
                foreach (var kvp in settings.AiRuntime.ManualModels)
                {
                    if (!Enum.TryParse<AIProviderType>(kvp.Key, out var providerType))
                        continue;

                    manualModelByProvider[providerType] = kvp.Value ?? "";
                }

                autoCheapModelByProvider.Clear();
                autoSmartModelByProvider.Clear();
                foreach (var kvp in settings.AiRuntime.AutoModels)
                {
                    if (!Enum.TryParse<AIProviderType>(kvp.Key, out var providerType) || kvp.Value == null)
                        continue;

                    autoCheapModelByProvider[providerType] = kvp.Value.Cheap ?? "";
                    autoSmartModelByProvider[providerType] = kvp.Value.Smart ?? "";
                }

                lock (budgetLock)
                {
                    budgetDay = DateOnly.FromDateTime((settings.AiRuntime.Usage?.UsageDayUtc ?? DateTime.UtcNow).ToUniversalTime());
                    spendUsedToday = settings.AiRuntime.Usage?.SpendUsed ?? 0m;
                    chatTokensUsedToday = settings.AiRuntime.Usage?.ChatTokensUsed ?? 0;
                    ttsCharactersUsedToday = settings.AiRuntime.Usage?.TtsCharactersUsed ?? 0;
                    heavyCallsUsedToday = settings.AiRuntime.Usage?.HeavyCallsUsed ?? 0;
                    EnsureBudgetWindowLocked();
                }
            }
            catch { }
        }

        private static void SaveSettings()
        {
            try
            {
                DateTime usageDayUtc;
                decimal spendUsed;
                int chatTokens;
                int ttsCharacters;
                int heavyCalls;

                lock (budgetLock)
                {
                    EnsureBudgetWindowLocked();
                    usageDayUtc = budgetDay.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
                    spendUsed = spendUsedToday;
                    chatTokens = chatTokensUsedToday;
                    ttsCharacters = ttsCharactersUsedToday;
                    heavyCalls = heavyCallsUsedToday;
                }

                SettingsStore.Update(settings =>
                {
                    settings.AiRuntime.ActiveProvider = activeProvider.ToString();
                    settings.AiRuntime.AutoModeEnabled = autoModeEnabled;
                    settings.AiRuntime.RoutingMode = routingMode;
                    settings.AiRuntime.CostMode = costMode;
                    settings.AiRuntime.RouterTokenThreshold = routerTokenThreshold;
                    settings.AiRuntime.RouterComplexityThreshold = routerComplexityThreshold;
                    settings.AiRuntime.DailySpendCap = dailySpendCap;
                    settings.AiRuntime.Usage.UsageDayUtc = usageDayUtc;
                    settings.AiRuntime.Usage.SpendUsed = spendUsed;
                    settings.AiRuntime.Usage.ChatTokensUsed = chatTokens;
                    settings.AiRuntime.Usage.TtsCharactersUsed = ttsCharacters;
                    settings.AiRuntime.Usage.HeavyCallsUsed = heavyCalls;

                    settings.ModelAutoModeEnabled = autoModeEnabled;
                    settings.MaxCostPerDay = dailySpendCap;
                    settings.RouterTokenThreshold = routerTokenThreshold;
                    settings.RouterComplexityThreshold = routerComplexityThreshold;
                    settings.ModelTier = GetLegacyModelTier();

                    settings.AiRuntime.ManualModels.Clear();
                    foreach (var kvp in manualModelByProvider)
                    {
                        settings.AiRuntime.ManualModels[kvp.Key.ToString()] = kvp.Value ?? "";
                    }

                    settings.AiRuntime.AutoModels.Clear();
                    foreach (AIProviderType providerType in Enum.GetValues(typeof(AIProviderType)))
                    {
                        settings.AiRuntime.AutoModels[providerType.ToString()] = new AtlasAutoModelSettings
                        {
                            Cheap = GetAutoCheapModel(providerType),
                            Smart = GetAutoSmartModel(providerType)
                        };
                    }
                });
            }
            catch { }
        }

        private static void PersistBudgetState(DateTime usageDayUtc, decimal spendUsed, int chatTokens, int ttsCharacters, int heavyCalls)
        {
            try
            {
                SettingsStore.Update(settings =>
                {
                    settings.AiRuntime.DailySpendCap = dailySpendCap;
                    settings.MaxCostPerDay = dailySpendCap;
                    settings.AiRuntime.Usage.UsageDayUtc = usageDayUtc;
                    settings.AiRuntime.Usage.SpendUsed = spendUsed;
                    settings.AiRuntime.Usage.ChatTokensUsed = chatTokens;
                    settings.AiRuntime.Usage.TtsCharactersUsed = ttsCharacters;
                    settings.AiRuntime.Usage.HeavyCallsUsed = heavyCalls;
                });
            }
            catch
            {
            }
        }

        private static void EnsureBudgetWindowLocked()
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            if (today == budgetDay)
                return;

            budgetDay = today;
            spendUsedToday = 0m;
            chatTokensUsedToday = 0;
            ttsCharactersUsedToday = 0;
            heavyCallsUsedToday = 0;
        }

        private static decimal EstimateSpend(AIProviderType providerType, string modelId, string kind, int units)
        {
            if (units <= 0)
                return 0m;

            if (string.Equals(kind, "tts", StringComparison.OrdinalIgnoreCase))
                return decimal.Round(units * 0.00001m, 4, MidpointRounding.AwayFromZero);

            if (string.Equals(kind, "heavy", StringComparison.OrdinalIgnoreCase))
                return decimal.Round(units * 0.025m, 4, MidpointRounding.AwayFromZero);

            var normalizedModel = (modelId ?? string.Empty).Trim().ToLowerInvariant();
            decimal perThousand = providerType switch
            {
                AIProviderType.OpenAI when normalizedModel.Contains("mini", StringComparison.Ordinal) => 0.0008m,
                AIProviderType.OpenAI when normalizedModel.Contains("pro", StringComparison.Ordinal) => 0.0150m,
                AIProviderType.OpenAI => 0.0050m,
                AIProviderType.Claude when normalizedModel.Contains("haiku", StringComparison.Ordinal) => 0.0010m,
                AIProviderType.Claude when normalizedModel.Contains("opus", StringComparison.Ordinal) => 0.0150m,
                AIProviderType.Claude => 0.0060m,
                AIProviderType.Gemini when normalizedModel.Contains("flash", StringComparison.Ordinal) => 0.0005m,
                AIProviderType.Gemini => 0.0030m,
                _ => 0.0050m
            };

            return decimal.Round((units / 1000m) * perThousand, 4, MidpointRounding.AwayFromZero);
        }

        private static string GetBudgetModelFor(AIProviderType providerType, string kind)
        {
            if (!string.Equals(kind, "chat", StringComparison.OrdinalIgnoreCase))
                return string.Empty;

            if (!autoModeEnabled)
                return GetManualSelectedModel(providerType);

            return GetEffectiveRoutingBehavior() switch
            {
                RoutingModeManual => GetManualSelectedModel(providerType),
                RoutingModeCheap => GetAutoCheapModel(providerType),
                RoutingModeSmart => GetAutoSmartModel(providerType),
                _ => GetAutoSmartModel(providerType)
            };
        }

        private static string GetEffectiveRoutingBehavior()
        {
            var normalizedRouting = NormalizeRoutingMode(routingMode);
            if (normalizedRouting != RoutingModeThresholds)
                return normalizedRouting;

            var effectiveCostMode = AtlasEconomyService.Instance.ResolveEffectiveCostMode(costMode);
            return NormalizeCostMode(effectiveCostMode) switch
            {
                CostModeBudget => RoutingModeCheap,
                CostModePerformance => RoutingModeSmart,
                _ => RoutingModeThresholds
            };
        }

        private static string NormalizeRoutingMode(string value)
        {
            return (value ?? string.Empty).Trim().ToLowerInvariant() switch
            {
                RoutingModeManual => RoutingModeManual,
                RoutingModeCheap => RoutingModeCheap,
                "cheaponly" => RoutingModeCheap,
                RoutingModeSmart => RoutingModeSmart,
                "smartonly" => RoutingModeSmart,
                _ => RoutingModeThresholds
            };
        }

        private static string NormalizeCostMode(string value)
        {
            return (value ?? string.Empty).Trim().ToLowerInvariant() switch
            {
                CostModeBudget => CostModeBudget,
                CostModePerformance => CostModePerformance,
                _ => CostModeBalanced
            };
        }

        private static ModelTier GetLegacyModelTier()
        {
            return GetEffectiveRoutingBehavior() switch
            {
                RoutingModeCheap => ModelTier.Cheap,
                RoutingModeSmart => ModelTier.Best,
                _ => ModelTier.Auto
            };
        }

        public sealed class AIUsageEvent
        {
            public DateTime TimestampUtc { get; init; }
            public AIProviderType Provider { get; init; }
            public string Model { get; init; } = "";
            public bool Success { get; init; }
            public long LatencyMs { get; init; }
        }

        public static List<AIUsageEvent> GetRecentUsage()
        {
            try
            {
                var logPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "AtlasAI", "logs", "ai_debug.jsonl");

                if (!File.Exists(logPath))
                    return new List<AIUsageEvent>();

                var lines = File.ReadAllLines(logPath);
                if (lines.Length == 0)
                    return new List<AIUsageEvent>();

                // Parse only the tail to keep this lightweight for UI polling.
                var start = Math.Max(0, lines.Length - 400);

                // Track request IDs -> request metadata so we can correlate success/latency.
                var requestMeta = new Dictionary<string, (DateTime tsUtc, AIProviderType provider, string model)>(StringComparer.OrdinalIgnoreCase);
                var responseMeta = new Dictionary<string, (bool success, long latencyMs)>(StringComparer.OrdinalIgnoreCase);

                for (var i = start; i < lines.Length; i++)
                {
                    var line = (lines[i] ?? "").Trim();
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    try
                    {
                        using var doc = JsonDocument.Parse(line);
                        var root = doc.RootElement;
                        if (!root.TryGetProperty("type", out var typeEl) || typeEl.ValueKind != JsonValueKind.String)
                            continue;

                        var type = (typeEl.GetString() ?? "").Trim();
                        if (!root.TryGetProperty("requestId", out var ridEl) || ridEl.ValueKind != JsonValueKind.String)
                            continue;
                        var requestId = (ridEl.GetString() ?? "").Trim();
                        if (string.IsNullOrWhiteSpace(requestId)) continue;

                        if (string.Equals(type, "AIRequest", StringComparison.OrdinalIgnoreCase))
                        {
                            var providerStr = root.TryGetProperty("provider", out var pEl) && pEl.ValueKind == JsonValueKind.String
                                ? (pEl.GetString() ?? "")
                                : "";
                            var model = root.TryGetProperty("model", out var mEl) && mEl.ValueKind == JsonValueKind.String
                                ? (mEl.GetString() ?? "")
                                : "";

                            var tsUtc = DateTime.UtcNow;
                            if (root.TryGetProperty("startUtc", out var sEl) && sEl.ValueKind == JsonValueKind.String)
                            {
                                if (DateTime.TryParse(sEl.GetString(), out var parsed))
                                    tsUtc = parsed.ToUniversalTime();
                            }
                            else if (root.TryGetProperty("ts", out var tsEl) && tsEl.ValueKind == JsonValueKind.String)
                            {
                                if (DateTime.TryParse(tsEl.GetString(), out var parsed2))
                                    tsUtc = parsed2.ToUniversalTime();
                            }

                            if (!Enum.TryParse<AIProviderType>((providerStr ?? "").Trim(), ignoreCase: true, out var provider))
                                provider = activeProvider;

                            requestMeta[requestId] = (tsUtc, provider, (model ?? "").Trim());
                        }
                        else if (string.Equals(type, "AIResponse", StringComparison.OrdinalIgnoreCase))
                        {
                            var ok = true;
                            if (root.TryGetProperty("finishReason", out var frEl) && frEl.ValueKind == JsonValueKind.String)
                            {
                                var fr = (frEl.GetString() ?? "").Trim();
                                ok = !string.Equals(fr, "error", StringComparison.OrdinalIgnoreCase);
                            }
                            var latency = root.TryGetProperty("latencyMs", out var lEl) && lEl.ValueKind == JsonValueKind.Number
                                ? lEl.GetInt64()
                                : 0;
                            responseMeta[requestId] = (ok, latency);
                        }
                        else if (string.Equals(type, "AIError", StringComparison.OrdinalIgnoreCase))
                        {
                            responseMeta[requestId] = (false, 0);
                        }
                    }
                    catch
                    {
                        // ignore malformed lines
                    }
                }

                // Convert to events (most recent first)
                var events = new List<AIUsageEvent>();
                foreach (var kvp in requestMeta)
                {
                    var requestId = kvp.Key;
                    var (tsUtc, provider, model) = kvp.Value;
                    var (success, latency) = responseMeta.TryGetValue(requestId, out var r) ? r : (true, 0L);

                    events.Add(new AIUsageEvent
                    {
                        TimestampUtc = tsUtc,
                        Provider = provider,
                        Model = model,
                        Success = success,
                        LatencyMs = latency
                    });
                }

                return events
                    .OrderByDescending(e => e.TimestampUtc)
                    .Take(120)
                    .ToList();
            }
            catch
            {
                return new List<AIUsageEvent>();
            }
        }

        public static AIUsageEvent? GetLatestUsage()
        {
            try
            {
                return GetRecentUsage().FirstOrDefault();
            }
            catch
            {
                return null;
            }
        }

        public static string GetCurrentEffectiveModelForUi()
        {
            try
            {
                var latestUsage = GetLatestUsage();
                if (latestUsage != null && latestUsage.Provider == activeProvider && !string.IsNullOrWhiteSpace(latestUsage.Model))
                    return latestUsage.Model;

                if (!autoModeEnabled)
                    return GetManualSelectedModel(activeProvider);

                return GetEffectiveRoutingBehavior() switch
                {
                    RoutingModeManual => GetManualSelectedModel(activeProvider),
                    RoutingModeCheap => GetAutoCheapModel(activeProvider),
                    RoutingModeSmart => GetAutoSmartModel(activeProvider),
                    _ => string.Empty
                };
            }
            catch
            {
                return string.Empty;
            }
        }

        public static string GetRealtimeUsageShort()
        {
            try
            {
                lock (budgetLock)
                {
                    EnsureBudgetWindowLocked();

                    return $"{activeProvider} | routing:{GetEffectiveRoutingBehavior()}/{costMode} | spend:{spendUsedToday:0.####}/{dailySpendCap:0.####} | chat:{chatTokensUsedToday}/{DailyChatTokenCap} tokens | tts:{ttsCharactersUsedToday}/{DailyTtsCharacterCap} chars | heavy:{heavyCallsUsedToday}/{DailyHeavyCallCap}";
                }
            }
            catch
            {
                return "";
            }
        }
    }
}
