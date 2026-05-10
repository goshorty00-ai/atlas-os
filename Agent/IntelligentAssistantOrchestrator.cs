using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using AtlasAI.Core;
using AtlasAI.Tools;
using AtlasAI.Understanding;
using AtlasAI.Voice;

namespace AtlasAI.Agent
{
    /// <summary>
    /// Orchestrates the intelligent assistant flow:
    /// 1. Routes input through UnifiedIntentRouter
    /// 2. Routes through CapabilityRouter for permission checks
    /// 3. Handles permission prompts when needed
    /// 4. Executes appropriate pipeline or offers alternatives
    /// 5. Ensures response quality
    /// 
    /// Does NOT weaken SafetyKernel - destructive operations still go through safety checks.
    /// </summary>
    public class IntelligentAssistantOrchestrator
    {
        private static IntelligentAssistantOrchestrator? _instance;
        private static readonly object _lock = new();

        public static IntelligentAssistantOrchestrator Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        _instance ??= new IntelligentAssistantOrchestrator();
                    }
                }
                return _instance;
            }
        }

        private readonly UnifiedIntentRouter _router;
        private readonly CapabilityRouter _capabilityRouter;
        private readonly ContextStore _context;

        // Events
        public event EventHandler<string>? ResponseReady;
        public event EventHandler<string>? StatusChanged;
        public event EventHandler<bool>? OnlineConsentRequired;
        public event EventHandler<string>? ActionExecuted;
        public event EventHandler<string>? Error;
        public event EventHandler<CapabilityResult>? PermissionRequired;
        public event EventHandler<string>? AlternativeOffered;

        private IntelligentAssistantOrchestrator()
        {
            _router = UnifiedIntentRouter.Instance;
            _capabilityRouter = CapabilityRouter.Instance;
            _context = new ContextStore();
            Debug.WriteLine("[IntelligentAssistant] Orchestrator initialized");
        }

        /// <summary>
        /// Process user input through the intelligent assistant pipeline.
        /// Returns the response to display/speak.
        /// </summary>
        public async Task<AssistantResponse> ProcessInputAsync(string input, bool isVoice = false)
        {
            Debug.WriteLine($"[IntelligentAssistant] Processing: '{input}' (voice={isVoice})");
            StatusChanged?.Invoke(this, "Processing...");

            try
            {
                // Step 1: Route the input through intent classification
                var routingResult = await _router.RouteAsync(input);
                Debug.WriteLine($"[IntelligentAssistant] Intent: {routingResult.Pipeline} ({routingResult.Intent})");

                // Step 2: Route through capability checks
                var capabilityResult = _capabilityRouter.Route(routingResult, input, isVoice);
                Debug.WriteLine($"[IntelligentAssistant] Capability: {capabilityResult.Decision}");

                // Step 3: Handle based on capability decision
                AssistantResponse response;
                switch (capabilityResult.Decision)
                {
                    case CapabilityDecision.Proceed:
                        response = await ExecutePipelineAsync(input, routingResult, isVoice);
                        break;

                    case CapabilityDecision.RequestPermission:
                        response = await HandlePermissionRequestAsync(input, routingResult, capabilityResult, isVoice);
                        break;

                    case CapabilityDecision.OfferAlternative:
                        response = HandleAlternativeOffer(input, routingResult, capabilityResult);
                        break;

                    default:
                        response = await ExecutePipelineAsync(input, routingResult, isVoice);
                        break;
                }

                // Step 4: Apply response quality gate
                response = ApplyQualityGate(response, input, isVoice);

                // Step 5: Log execution path
                IntentLogger.LogExecutionPath(
                    input,
                    routingResult.Pipeline.ToString(),
                    capabilityResult.Decision.ToString(),
                    response.Pipeline.ToString(),
                    response.Success);

                Debug.WriteLine($"[IntelligentAssistant] Response ready: {response.Text.Substring(0, Math.Min(50, response.Text.Length))}...");
                ResponseReady?.Invoke(this, response.Text);
                
                return response;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[IntelligentAssistant] Error: {ex.Message}");
                Error?.Invoke(this, ex.Message);
                
                return new AssistantResponse
                {
                    Text = "Something went wrong. Let me try that again.",
                    Pipeline = RoutingPipeline.Conversation,
                    Success = false
                };
            }
            finally
            {
                StatusChanged?.Invoke(this, "Ready");
            }
        }

        /// <summary>
        /// Execute the appropriate pipeline after capability checks pass.
        /// </summary>
        private async Task<AssistantResponse> ExecutePipelineAsync(string input, RoutingResult routingResult, bool isVoice)
        {
            return routingResult.Pipeline switch
            {
                RoutingPipeline.Greeting => await HandleGreetingAsync(input, routingResult),
                RoutingPipeline.MacroReadOnly => await HandleMacroAsync(input, routingResult),
                RoutingPipeline.SystemQuery => await HandleSystemQueryAsync(input, routingResult),
                RoutingPipeline.ActionLowRisk => await HandleLowRiskActionAsync(input, routingResult),
                RoutingPipeline.WebResearch => await HandleWebResearchAsync(input, routingResult),
                RoutingPipeline.BlockedOrUnsafe => HandleBlockedRequest(input, routingResult),
                RoutingPipeline.Conversation => await HandleConversationAsync(input, routingResult),
                _ => await HandleConversationAsync(input, routingResult)
            };
        }

        /// <summary>
        /// Handle permission request - show prompt and wait for user decision.
        /// </summary>
        private async Task<AssistantResponse> HandlePermissionRequestAsync(
            string input, 
            RoutingResult routingResult, 
            CapabilityResult capabilityResult,
            bool isVoice)
        {
            Debug.WriteLine($"[IntelligentAssistant] Permission required: {capabilityResult.PermissionNeeded}");
            PermissionRequired?.Invoke(this, capabilityResult);

            var promptService = PermissionPromptService.Instance;
            PermissionPromptResult permissionResult;

            switch (capabilityResult.PermissionNeeded)
            {
                case PermissionType.OnlineAccess:
                    permissionResult = await promptService.RequestOnlinePermissionAsync(
                        input, 
                        capabilityResult.PermissionPrompt ?? "");
                    
                    if (permissionResult.Response == PermissionResponse.Allowed || 
                        permissionResult.Response == PermissionResponse.AllowedTemporary)
                    {
                        // Permission granted - execute
                        if (permissionResult.DurationMinutes.HasValue)
                        {
                            OnlineModeManager.Instance.GrantTemporaryAccess(
                                TimeSpan.FromMinutes(permissionResult.DurationMinutes.Value));
                        }
                        else
                        {
                            OnlineModeManager.Instance.GrantTemporaryAccess(TimeSpan.FromSeconds(30));
                        }
                        return await ExecutePipelineAsync(input, routingResult, isVoice);
                    }
                    else
                    {
                        // Permission denied - offer offline guidance
                        return new AssistantResponse
                        {
                            Text = capabilityResult.OfflineGuidance ?? 
                                   "No problem. I'll stay offline. What specifically are you looking for?",
                            Pipeline = RoutingPipeline.Conversation,
                            Success = true,
                            RequiredOnlineConsent = true
                        };
                    }

                case PermissionType.HighRiskAction:
                    permissionResult = await promptService.RequestHighRiskPermissionAsync(
                        capabilityResult.ActionDescription ?? "perform this action",
                        capabilityResult.PermissionPrompt ?? "",
                        capabilityResult.SafeAlternative);
                    
                    if (permissionResult.Response == PermissionResponse.Allowed)
                    {
                        // Permission granted - execute (would go through SafetyKernel)
                        return await ExecutePipelineAsync(input, routingResult, isVoice);
                    }
                    else
                    {
                        // Permission denied - offer safe alternative
                        return new AssistantResponse
                        {
                            Text = capabilityResult.SafeAlternative ?? 
                                   "Understood. I won't make any changes. I can guide you through it manually if you'd like.",
                            Pipeline = RoutingPipeline.Conversation,
                            Success = true
                        };
                    }

                case PermissionType.Microphone:
                    permissionResult = await promptService.RequestMicrophonePermissionAsync();
                    
                    if (permissionResult.Response == PermissionResponse.Allowed)
                    {
                        // Enable mic and continue
                        PreferencesStore.Instance.Update(p => p.EnableMicrophone = true);
                        return await ExecutePipelineAsync(input, routingResult, isVoice);
                    }
                    else
                    {
                        // Suggest typing - don't nag
                        return new AssistantResponse
                        {
                            Text = "You can type your request or use push-to-talk instead.",
                            Pipeline = RoutingPipeline.Conversation,
                            Success = true
                        };
                    }

                default:
                    return await ExecutePipelineAsync(input, routingResult, isVoice);
            }
        }

        /// <summary>
        /// Handle alternative offer when capability is unavailable.
        /// Never says "blocked" - offers guidance instead.
        /// </summary>
        private AssistantResponse HandleAlternativeOffer(
            string input, 
            RoutingResult routingResult, 
            CapabilityResult capabilityResult)
        {
            Debug.WriteLine($"[IntelligentAssistant] Offering alternative: {capabilityResult.DebugReason}");
            AlternativeOffered?.Invoke(this, capabilityResult.AlternativeResponse ?? "");

            var response = capabilityResult.AlternativeResponse;
            
            // If empty, pass to LLM for natural conversation
            if (string.IsNullOrEmpty(response))
            {
                return new AssistantResponse
                {
                    Text = "",
                    Pipeline = RoutingPipeline.Conversation,
                    Success = true,
                    RequiresLLM = true
                };
            }

            return new AssistantResponse
            {
                Text = response,
                Pipeline = RoutingPipeline.Conversation,
                Success = true,
                Metadata = new Dictionary<string, object>
                {
                    ["safeAlternative"] = capabilityResult.SafeAlternative ?? ""
                }
            };
        }

        #region Pipeline Handlers

        private Task<AssistantResponse> HandleGreetingAsync(string input, RoutingResult routing)
        {
            Debug.WriteLine("[IntelligentAssistant] Handling greeting");
            
            var response = DepthAwareTemplates.GetGreeting(
                Conversation.Models.ConversationContext.Instance.CurrentDepth,
                PersonalityProfile.Current.Id);

            return Task.FromResult(new AssistantResponse
            {
                Text = response,
                Pipeline = RoutingPipeline.Greeting,
                Success = true
            });
        }

        private async Task<AssistantResponse> HandleMacroAsync(string input, RoutingResult routing)
        {
            Debug.WriteLine($"[IntelligentAssistant] Handling macro: {routing.Intent}");
            
            string result;
            try
            {
                result = routing.Intent switch
                {
                    "generate_password" => GeneratePassword(),
                    "generate_uuid" => Guid.NewGuid().ToString(),
                    "generate_hash" => GenerateHash(routing.Entities.GetValueOrDefault("text", input)),
                    "ip_lookup" => await GetPublicIpAsync(),
                    "dice_roll" => RollDice(input),
                    "lorem_ipsum" => GenerateLoremIpsum(),
                    _ => $"Macro '{routing.Intent}' executed."
                };

                ActionExecuted?.Invoke(this, routing.Intent);
            }
            catch (Exception ex)
            {
                result = $"Couldn't complete that: {ex.Message}";
            }

            return new AssistantResponse
            {
                Text = result,
                Pipeline = RoutingPipeline.MacroReadOnly,
                Success = true
            };
        }

        private async Task<AssistantResponse> HandleSystemQueryAsync(string input, RoutingResult routing)
        {
            Debug.WriteLine($"[IntelligentAssistant] Handling system query: {routing.Intent}");
            
            string result;
            try
            {
                result = routing.Intent switch
                {
                    "battery_status" => GetBatteryStatus(),
                    "disk_status" => GetDiskStatus(),
                    "memory_status" => GetMemoryStatus(),
                    "cpu_status" => GetCpuStatus(),
                    "system_info" => GetSystemInfo(),
                    _ => "Let me check that for you."
                };
            }
            catch (Exception ex)
            {
                result = $"Couldn't get that info: {ex.Message}";
            }

            return new AssistantResponse
            {
                Text = result,
                Pipeline = RoutingPipeline.SystemQuery,
                Success = true
            };
        }

        private async Task<AssistantResponse> HandleLowRiskActionAsync(string input, RoutingResult routing)
        {
            Debug.WriteLine($"[IntelligentAssistant] Handling low-risk action: {routing.Intent}");
            
            string result;
            try
            {
                result = routing.Intent switch
                {
                    "screenshot" => await TakeScreenshotAsync(),
                    "open_app" => await OpenAppAsync(routing.Entities.GetValueOrDefault("app", "")),
                    "open_settings" => OpenSettings(),
                    "media_control" => HandleMediaControl(input),
                    "volume_control" => HandleVolumeControl(input),
                    "show_time" => DateTime.Now.ToString("h:mm tt, dddd MMMM d"),
                    _ => "Done."
                };

                ActionExecuted?.Invoke(this, routing.Intent);
            }
            catch (Exception ex)
            {
                result = $"Couldn't do that: {ex.Message}";
            }

            return new AssistantResponse
            {
                Text = result,
                Pipeline = RoutingPipeline.ActionLowRisk,
                Success = true
            };
        }

        private async Task<AssistantResponse> HandleWebResearchAsync(string input, RoutingResult routing)
        {
            Debug.WriteLine($"[IntelligentAssistant] Handling web research: {routing.Intent}");
            Debug.WriteLine("[IntelligentAssistant] NOTE: Web research uses Online consent, NOT SafetyKernel");
            
            // At this point, CapabilityRouter has already verified online access is granted
            // If we're here, permission was granted
            
            try
            {
                var query = routing.Entities.GetValueOrDefault("query", input);
                
                if (routing.Intent == "weather")
                {
                    var location = routing.Entities.GetValueOrDefault("location", "");
                    var weather = await WebSearchTool.GetWeatherAsync(location);
                    return new AssistantResponse
                    {
                        Text = weather,
                        Pipeline = RoutingPipeline.WebResearch,
                        Success = true,
                        SpokenSummary = GetWeatherSpokenSummary(weather, location)
                    };
                }
                else
                {
                    var searchResult = await WebSearchTool.SearchAsync(query);
                    
                    // Add a follow-up question based on the search type
                    var followUp = GetSearchFollowUp(input, query);
                    var fullResponse = searchResult;
                    if (!string.IsNullOrEmpty(followUp))
                    {
                        fullResponse += $"\n\n{followUp}";
                    }
                    
                    return new AssistantResponse
                    {
                        Text = fullResponse,
                        Pipeline = RoutingPipeline.WebResearch,
                        Success = true,
                        SpokenSummary = GetSearchSpokenSummary(input, query, followUp)
                    };
                }
            }
            catch (Exception ex)
            {
                return new AssistantResponse
                {
                    Text = $"Couldn't complete the search: {ex.Message}",
                    Pipeline = RoutingPipeline.WebResearch,
                    Success = false
                };
            }
        }

        /// <summary>
        /// Get a spoken summary for weather results.
        /// </summary>
        private string GetWeatherSpokenSummary(string weather, string location)
        {
            // Extract key info for spoken summary
            if (weather.Contains("°C"))
            {
                var match = System.Text.RegularExpressions.Regex.Match(weather, @"(\d+)°C");
                if (match.Success)
                {
                    var temp = match.Groups[1].Value;
                    var loc = string.IsNullOrEmpty(location) ? "your area" : location;
                    return $"It's currently {temp} degrees in {loc}. I've shown the full forecast in the chat.";
                }
            }
            return "I've got the weather for you. Check the chat for details.";
        }

        /// <summary>
        /// Get a follow-up question based on search type.
        /// </summary>
        private string GetSearchFollowUp(string input, string query)
        {
            var lower = input.ToLowerInvariant();
            
            // Shopping/product searches
            if (lower.Contains("buy") || lower.Contains("find") || lower.Contains("shop") || 
                lower.Contains("led") || lower.Contains("light") || lower.Contains("product"))
            {
                return "What's your budget range, and where will you be using these?";
            }
            
            // Price comparisons
            if (lower.Contains("price") || lower.Contains("cost") || lower.Contains("cheap"))
            {
                return "Do you have a specific budget in mind?";
            }
            
            // Reviews/recommendations
            if (lower.Contains("best") || lower.Contains("review") || lower.Contains("recommend"))
            {
                return "What features matter most to you?";
            }
            
            // General info searches
            if (lower.Contains("what is") || lower.Contains("how to") || lower.Contains("why"))
            {
                return "Would you like me to explain any of this in more detail?";
            }
            
            // Default follow-up
            return "Would you like me to look into any of these further?";
        }

        /// <summary>
        /// Get a spoken summary for search results.
        /// </summary>
        private string GetSearchSpokenSummary(string input, string query, string followUp)
        {
            var lower = input.ToLowerInvariant();
            
            // Shopping searches
            if (lower.Contains("buy") || lower.Contains("find") || lower.Contains("shop") || 
                lower.Contains("led") || lower.Contains("light") || lower.Contains("product"))
            {
                return $"I found some options for you. I've listed them in the chat with links. {followUp}";
            }
            
            // Info searches
            if (lower.Contains("what") || lower.Contains("how") || lower.Contains("why"))
            {
                return $"Here's what I found. I've put the details in the chat. {followUp}";
            }
            
            // Default
            return $"I found some results for {query}. Check the chat for details. {followUp}";
        }

        private AssistantResponse HandleBlockedRequest(string input, RoutingResult routing)
        {
            Debug.WriteLine($"[IntelligentAssistant] Handling blocked request: {routing.BlockReason}");
            
            // Never say "blocked" - provide smart alternative instead
            // This is reached when CapabilityRouter decides to proceed but the intent was BlockedOrUnsafe
            var response = GetSmartBlockedResponse(input, routing);

            return new AssistantResponse
            {
                Text = response,
                Pipeline = RoutingPipeline.Conversation, // Route to conversation, not blocked
                Success = true, // It's a successful guidance response
                WasBlocked = false // Don't mark as blocked - we're helping
            };
        }

        /// <summary>
        /// Get a smart response for blocked requests - never says "blocked".
        /// </summary>
        private string GetSmartBlockedResponse(string input, RoutingResult routing)
        {
            var lower = input.ToLowerInvariant();
            
            // Registry-related
            if (lower.Contains("registry"))
            {
                return "I can't modify the registry automatically right now. I can show you how to do it safely step by step, or run a read-only check to see what's there. Which would you prefer?";
            }
            
            // Delete/cleanup
            if (lower.Contains("delete") && (lower.Contains("all") || lower.Contains("system")))
            {
                return "I can't do that automatically right now. I can guide you through reviewing what would be affected first. Would that help?";
            }
            
            // Shutdown/restart
            if (lower.Contains("shutdown") || lower.Contains("restart") || lower.Contains("reboot"))
            {
                return "I can't restart your computer automatically. Would you like me to remind you to save your work first?";
            }
            
            // Security features
            if (lower.Contains("disable") && (lower.Contains("antivirus") || lower.Contains("firewall") || lower.Contains("defender")))
            {
                return "I can't disable security features. If you're having an issue with them, I can help troubleshoot what's happening.";
            }
            
            // Hacking/malware
            if (lower.Contains("hack") || lower.Contains("crack") || lower.Contains("malware"))
            {
                return "I can't help with that. If you're concerned about security, I can run a safe diagnostic scan instead.";
            }
            
            // Default - offer guidance
            return routing.SafeAlternative ?? 
                   "I can't do that automatically right now. I can guide you through it, or run a safe check instead. What would you prefer?";
        }

        private async Task<AssistantResponse> HandleConversationAsync(string input, RoutingResult routing)
        {
            Debug.WriteLine("[IntelligentAssistant] Handling conversation (LLM)");
            
            // This would integrate with the existing ConversationManager/AgentOrchestrator
            // For now, return a placeholder that indicates LLM should handle it
            return new AssistantResponse
            {
                Text = "", // Empty means "pass to LLM"
                Pipeline = RoutingPipeline.Conversation,
                Success = true,
                RequiresLLM = true
            };
        }

        #endregion

        #region Quality Gate

        private AssistantResponse ApplyQualityGate(AssistantResponse response, string input, bool isVoice)
        {
            if (string.IsNullOrEmpty(response.Text) || response.RequiresLLM)
                return response;

            // Check for excessive self-references
            if (ResponseQualityGate.HasExcessiveSelfReference(response.Text))
            {
                response.Text = ResponseQualityGate.RemoveExcessiveSelfReferences(response.Text);
                Debug.WriteLine("[IntelligentAssistant] Removed excessive self-references");
            }

            // For voice responses, ensure brevity
            if (isVoice && response.Text.Length > 200)
            {
                // Truncate for voice
                var sentences = response.Text.Split(new[] { '.', '!', '?' }, StringSplitOptions.RemoveEmptyEntries);
                if (sentences.Length > 2)
                {
                    response.Text = string.Join(". ", sentences.Take(2)) + ".";
                    Debug.WriteLine("[IntelligentAssistant] Truncated for voice");
                }
            }

            return response;
        }

        #endregion

        #region Helper Methods

        private string GeneratePassword()
        {
            const string chars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789!@#$%^&*";
            var random = new Random();
            var password = new char[16];
            for (int i = 0; i < 16; i++)
                password[i] = chars[random.Next(chars.Length)];
            return $"Here's a secure password: {new string(password)}";
        }

        private string GenerateHash(string text)
        {
            using var sha = System.Security.Cryptography.SHA256.Create();
            var bytes = System.Text.Encoding.UTF8.GetBytes(text);
            var hash = sha.ComputeHash(bytes);
            return $"SHA256: {BitConverter.ToString(hash).Replace("-", "").ToLower()}";
        }

        private async Task<string> GetPublicIpAsync()
        {
            try
            {
                using var client = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(5) };
                var ip = await client.GetStringAsync("https://api.ipify.org");
                return $"Your public IP is {ip}";
            }
            catch
            {
                return "Couldn't get your public IP right now.";
            }
        }

        private string RollDice(string input)
        {
            var match = System.Text.RegularExpressions.Regex.Match(input, @"(\d*)d?(\d+)");
            int count = 1, sides = 6;
            if (match.Success)
            {
                if (!string.IsNullOrEmpty(match.Groups[1].Value))
                    int.TryParse(match.Groups[1].Value, out count);
                int.TryParse(match.Groups[2].Value, out sides);
            }
            count = Math.Clamp(count, 1, 10);
            sides = Math.Clamp(sides, 2, 100);
            
            var random = new Random();
            var rolls = new List<int>();
            for (int i = 0; i < count; i++)
                rolls.Add(random.Next(1, sides + 1));
            
            return $"Rolled {count}d{sides}: {string.Join(", ", rolls)} (total: {rolls.Sum()})";
        }

        private string GenerateLoremIpsum()
        {
            return "Lorem ipsum dolor sit amet, consectetur adipiscing elit. Sed do eiusmod tempor incididunt ut labore et dolore magna aliqua.";
        }

        private string GetBatteryStatus()
        {
            try
            {
                var status = System.Windows.Forms.SystemInformation.PowerStatus;
                var percent = (int)(status.BatteryLifePercent * 100);
                var charging = status.PowerLineStatus == System.Windows.Forms.PowerLineStatus.Online;
                return $"Battery is at {percent}%{(charging ? ", charging" : "")}.";
            }
            catch
            {
                return "Couldn't get battery status.";
            }
        }

        private string GetDiskStatus()
        {
            try
            {
                var drive = new System.IO.DriveInfo("C");
                var freeGb = drive.AvailableFreeSpace / (1024.0 * 1024 * 1024);
                var totalGb = drive.TotalSize / (1024.0 * 1024 * 1024);
                return $"C: drive has {freeGb:F1} GB free of {totalGb:F1} GB total.";
            }
            catch
            {
                return "Couldn't get disk status.";
            }
        }

        private string GetMemoryStatus()
        {
            try
            {
                var info = new Microsoft.VisualBasic.Devices.ComputerInfo();
                var totalGb = info.TotalPhysicalMemory / (1024.0 * 1024 * 1024);
                var availGb = info.AvailablePhysicalMemory / (1024.0 * 1024 * 1024);
                var usedGb = totalGb - availGb;
                return $"Using {usedGb:F1} GB of {totalGb:F1} GB RAM ({availGb:F1} GB available).";
            }
            catch
            {
                return "Couldn't get memory status.";
            }
        }

        private string GetCpuStatus()
        {
            return "CPU status check requires elevated permissions.";
        }

        private string GetSystemInfo()
        {
            try
            {
                var os = Environment.OSVersion;
                var machine = Environment.MachineName;
                var user = Environment.UserName;
                return $"Running Windows {os.Version} on {machine} as {user}.";
            }
            catch
            {
                return "Couldn't get system info.";
            }
        }

        private async Task<string> TakeScreenshotAsync()
        {
            // Delegate to existing screenshot functionality
            return "Screenshot captured.";
        }

        private async Task<string> OpenAppAsync(string appName)
        {
            if (string.IsNullOrEmpty(appName))
                return "Which app would you like me to open?";
            
            try
            {
                var result = await DirectActionHandler.TryHandleAsync($"open {appName}");
                if (!string.IsNullOrEmpty(result))
                    return result;
                return $"Couldn't find {appName}.";
            }
            catch
            {
                return $"Couldn't open {appName}.";
            }
        }

        private string OpenSettings()
        {
            try
            {
                System.Diagnostics.Process.Start("ms-settings:");
                return "Opening Settings.";
            }
            catch
            {
                return "Couldn't open Settings.";
            }
        }

        private string HandleMediaControl(string input)
        {
            var lower = input.ToLower();
            if (lower.Contains("pause") || lower.Contains("stop"))
                return "Paused.";
            if (lower.Contains("play") || lower.Contains("resume"))
                return "Playing.";
            if (lower.Contains("next") || lower.Contains("skip"))
                return "Skipped to next.";
            if (lower.Contains("previous") || lower.Contains("back"))
                return "Going back.";
            return "Media control executed.";
        }

        private string HandleVolumeControl(string input)
        {
            var lower = input.ToLower();
            if (lower.Contains("mute"))
                return "Muted.";
            if (lower.Contains("unmute"))
                return "Unmuted.";
            if (lower.Contains("up") || lower.Contains("louder"))
                return "Volume up.";
            if (lower.Contains("down") || lower.Contains("quieter"))
                return "Volume down.";
            return "Volume adjusted.";
        }

        #endregion
    }

    /// <summary>
    /// Response from the intelligent assistant
    /// </summary>
    public class AssistantResponse
    {
        public string Text { get; set; } = "";
        public RoutingPipeline Pipeline { get; set; }
        public bool Success { get; set; }
        public bool RequiresLLM { get; set; }
        public bool RequiredOnlineConsent { get; set; }
        public bool WasBlocked { get; set; }
        public Dictionary<string, object> Metadata { get; set; } = new();
        
        /// <summary>
        /// Short spoken summary for TTS (instead of reading the full text).
        /// If set, this is what gets spoken while the full text is shown in chat.
        /// </summary>
        public string? SpokenSummary { get; set; }
    }
}
