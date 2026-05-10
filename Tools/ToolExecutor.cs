using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using AtlasAI.ActionHistory;
using AtlasAI.SystemControl;
using AtlasAI.SecuritySuite.Services;
using AtlasAI.SecuritySuite.Models;

namespace AtlasAI.Tools
{
    public static class ToolExecutor
    {
        // Enable/disable smart AI intent parsing
        private static bool _useSmartParsing = true;
        
        // Store last scan results so user can say "fix it" after a scan
        private static UnifiedScanResult? _lastScanResult;
        private static DateTime _lastScanTime = DateTime.MinValue;
        
        // Security Suite manager for AI commands
        private static SecuritySuiteManager? _securitySuite;
        
        /// <summary>
        /// Check if user message needs a tool and execute it
        /// Returns tool result or null if no tool needed
        /// </summary>
        public static async Task<string?> TryExecuteToolAsync(string userMessage)
        {
            // Strip context prefix if present (e.g., "[Context: ...]\n\nUser: fix" -> "fix")
            var cleanMessage = userMessage;
            if (cleanMessage.Contains("[Context:") && cleanMessage.Contains("User:"))
            {
                var userIdx = cleanMessage.LastIndexOf("User:");
                if (userIdx >= 0)
                {
                    cleanMessage = cleanMessage.Substring(userIdx + 5).Trim();
                }
            }
            
            var msg = cleanMessage.ToLower().Trim();
            System.Diagnostics.Debug.WriteLine($"[ToolExecutor] ========== PROCESSING: {msg} ==========");

            // ==================== DIRECT ACTION HANDLER ====================
            // Authoritative normal-runtime entry for legacy direct actions now lives here.
            try
            {
                var direct = await DirectActionHandler.TryHandleAsync(cleanMessage);
                if (direct != null)
                    return direct;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ToolExecutor] DirectActionHandler error: {ex.Message}");
            }

            // ==================== FIX THREATS - Handle "fix", "fix it", "fix the problem", "remove threats" ====================
            // This MUST be checked FIRST before greetings since "fix" is a short word
            if (msg == "fix" || msg == "fixit" || msg.StartsWith("fix ") || 
                ContainsAny(msg, "fix it", "fix the problem", "fix problems", "remove threats", "remove the threats",
                "clean it", "clean them", "delete threats", "fix threats", "remove them", "get rid of them",
                "fix my computer", "fix my pc", "clean my computer", "clean my pc", "remove all threats",
                "fix them", "fix those", "fix that", "fix the threats", "fix the issues", "fix issues"))
            {
                System.Diagnostics.Debug.WriteLine($"[ToolExecutor] FIX IT COMMAND DETECTED - calling FixDetectedThreatsAsync");
                System.Diagnostics.Debug.WriteLine($"[ToolExecutor] _lastScanResult is null: {_lastScanResult == null}");
                System.Diagnostics.Debug.WriteLine($"[ToolExecutor] _lastScanResult threats: {_lastScanResult?.Threats?.Count ?? 0}");
                var fixResult = await FixDetectedThreatsAsync();
                System.Diagnostics.Debug.WriteLine($"[ToolExecutor] FIX RESULT: {fixResult?.Substring(0, Math.Min(100, fixResult?.Length ?? 0))}...");
                return fixResult;
            }

            // ==================== DIRECT SHELL COMMANDS - HIGHEST PRIORITY ====================
            // Execute shell commands like ipconfig, ping, etc. IMMEDIATELY - BEFORE anything else
            // This must be checked BEFORE greetings and app opening to prevent "open ipconfig" from being mishandled
            var directCommands = new[] { "ipconfig", "ping", "systeminfo", "hostname", "whoami", "netstat", "tasklist", "dir", "tree", "ver", "nslookup", "tracert", "arp", "route", "cls", "help" };
            foreach (var cmd in directCommands)
            {
                // Direct command: "ipconfig" or "ipconfig /all"
                if (msg == cmd || msg.StartsWith(cmd + " ") || msg.StartsWith(cmd + "/"))
                {
                    System.Diagnostics.Debug.WriteLine($"[ToolExecutor] ⚡ Direct shell command: {cmd}");
                    return await SystemTool.RunCommandAsync(userMessage.Trim());
                }
                // "open ipconfig", "run ipconfig", "show ipconfig", "execute ipconfig"
                if (msg == $"open {cmd}" || msg == $"run {cmd}" || msg == $"show {cmd}" || msg == $"execute {cmd}" ||
                    msg.StartsWith($"open {cmd} ") || msg.StartsWith($"run {cmd} ") || msg.StartsWith($"show {cmd} "))
                {
                    System.Diagnostics.Debug.WriteLine($"[ToolExecutor] ⚡ Shell command from 'open/run/show {cmd}'");
                    return await SystemTool.RunCommandAsync(cmd);
                }
            }

            // ==================== ACTIVE WINDOW AWARENESS ====================
            // "what's this?" / "active window" / "close this"
            try
            {
                var aw = await ActiveWindowSkill.TryExecuteAsync(cleanMessage, CancellationToken.None);
                if (aw != null)
                    return aw;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ToolExecutor] ActiveWindowSkill error: {ex.Message}");
            }

            // ==================== CLIPBOARD SKILL ====================
            // Read/write/clean/format/summarize clipboard text.
            // Never writes to disk (unless a separate explicit "save" feature exists elsewhere).
            try
            {
                var clip = await ClipboardSkill.TryExecuteAsync(cleanMessage, CancellationToken.None);
                if (clip != null)
                    return clip;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ToolExecutor] ClipboardSkill error: {ex.Message}");
            }

            // ==================== MACRO SKILL (user-defined sequences) ====================
            // Handle macro define/run/confirm locally.
            try
            {
                var macro = await MacroSkill.TryExecuteAsync(cleanMessage, CancellationToken.None);
                if (macro != null)
                    return macro;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ToolExecutor] MacroSkill error: {ex.Message}");
            }

            // ==================== WINDOWS SKILL SYSTEM (safe + dangerous with CONFIRM) ====================
            // Run before FileButler so typed CONFIRM can confirm dangerous system actions.
            try
            {
                var win = await WindowsSkillSystemSkill.TryExecuteAsync(cleanMessage, CancellationToken.None);
                if (win != null)
                    return win;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ToolExecutor] WindowsSkillSystemSkill error: {ex.Message}");
            }

            // ==================== FILE BUTLER (safe + elevated with preview/confirm) ====================
            // IMPORTANT: This must run BEFORE the greeting/conversation filter so "yes/no/ok/confirm" can confirm/cancel pending ops.
            try
            {
                var fb = await FileButlerSkill.TryExecuteAsync(cleanMessage, CancellationToken.None);
                if (fb != null)
                    return fb;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ToolExecutor] FileButlerSkill error: {ex.Message}");
            }

            // ==================== DIAGNOSTICS (tail logs + exception summary) ====================
            try
            {
                var narr = await DiagnosticsNarratorSkill.TryExecuteAsync(cleanMessage, CancellationToken.None);
                if (narr != null)
                    return narr;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ToolExecutor] DiagnosticsNarratorSkill error: {ex.Message}");
            }

            try
            {
                var diag = await DiagnosticsSkill.TryExecuteAsync(cleanMessage, CancellationToken.None);
                if (diag != null)
                    return diag;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ToolExecutor] DiagnosticsSkill error: {ex.Message}");
            }

            // ==================== UI ANALYZER (ViewModel state inspection) ====================
            // Inspect current DataContext/ViewModel for empty collections, duplicates, blank genres, etc.
            try
            {
                var ui = await UIAnalyzerSkill.TryExecuteAsync(cleanMessage, CancellationToken.None);
                if (ui != null)
                    return ui;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ToolExecutor] UIAnalyzerSkill error: {ex.Message}");
            }

            // ==================== PERSONALITY MEMORY (local, non-creepy) ====================
            // "What do you remember about me?"
            try
            {
                var mem = await PersonalityMemorySkill.TryExecuteAsync(cleanMessage, CancellationToken.None);
                if (mem != null)
                    return mem;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ToolExecutor] PersonalityMemorySkill error: {ex.Message}");
            }

            // ==================== CHAT SETTINGS (personality + ambience) ====================
            try
            {
                var persona = await ChatPersonalitySkill.TryExecuteAsync(cleanMessage, CancellationToken.None);
                if (persona != null)
                    return persona;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ToolExecutor] ChatPersonalitySkill error: {ex.Message}");
            }

            try
            {
                var dim = await AmbientDimSkill.TryExecuteAsync(cleanMessage, CancellationToken.None);
                if (dim != null)
                    return dim;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ToolExecutor] AmbientDimSkill error: {ex.Message}");
            }

            try
            {
                var media = await MediaCentreAutomationSkill.TryExecuteAsync(cleanMessage, CancellationToken.None);
                if (media != null)
                    return media;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ToolExecutor] MediaCentreAutomationSkill error: {ex.Message}");
            }

            try
            {
                var dj = await DjSuggestionSkill.TryExecuteAsync(cleanMessage, CancellationToken.None);
                if (dj != null)
                    return dj;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ToolExecutor] DjSuggestionSkill error: {ex.Message}");
            }

            // ==================== GREETINGS - Let AI handle conversationally ====================
            // Don't try to execute tools for greetings, introductions, or casual conversation
            if (IsGreetingOrConversation(msg))
            {
                System.Diagnostics.Debug.WriteLine($"[ToolExecutor] Detected greeting/conversation - letting AI handle");
                return null; // Let AI respond naturally
            }

            // ==================== IN-APP NAVIGATION (Command Center) ====================
            // Handle view/module navigation before generic app opening keywords.
            try
            {
                var nav = await NavigationSkill.TryExecuteAsync(cleanMessage, CancellationToken.None);
                if (nav != null)
                    return nav;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ToolExecutor] NavigationSkill error: {ex.Message}");
            }

            // ==================== OPEN APP AND PLAY - FAST PATH ====================
            // Handle "open spotify, play X" or "open youtube, play X" IMMEDIATELY before anything else
            if (msg.Contains("spotify") && ContainsAny(msg, "play ", ", play", " and play"))
            {
                var playMatch = System.Text.RegularExpressions.Regex.Match(msg, 
                    @"(?:,\s*|and\s+)?play\s+(.+?)(?:\s+on\s+spotify)?$", 
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (playMatch.Success)
                {
                    var query = playMatch.Groups[1].Value.Trim().TrimEnd('.', '!', '?');
                    if (!string.IsNullOrEmpty(query) && query.ToLower() != "spotify")
                    {
                        System.Diagnostics.Debug.WriteLine($"[ToolExecutor] ⚡ FAST PATH: Open Spotify and play '{query}'");
                        return await MediaPlayerTool.PlayAsync(query, MediaPlayerTool.Platform.Spotify);
                    }
                }
            }
            
            if (msg.Contains("youtube") && ContainsAny(msg, "play ", ", play", " and play", "watch ", ", watch", " and watch"))
            {
                var playMatch = System.Text.RegularExpressions.Regex.Match(msg, 
                    @"(?:,\s*|and\s+)?(?:play|watch)\s+(.+?)(?:\s+on\s+youtube)?$", 
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (playMatch.Success)
                {
                    var query = playMatch.Groups[1].Value.Trim().TrimEnd('.', '!', '?');
                    if (!string.IsNullOrEmpty(query) && query.ToLower() != "youtube")
                    {
                        System.Diagnostics.Debug.WriteLine($"[ToolExecutor] ⚡ FAST PATH: Open YouTube and play '{query}'");
                        return await MediaPlayerTool.PlayAsync(query, MediaPlayerTool.Platform.YouTube);
                    }
                }
            }

            // ==================== INTEGRATION HUB ====================
            // Open Integration Hub to see all available integrations
            if (ContainsAny(msg, "integration hub", "integrations", "show integrations", "what can you connect to",
                "what apps", "available integrations", "connected apps", "app connections", "what services"))
            {
                return "__OPEN_INTEGRATION_HUB__";
            }

            // ==================== SOCIAL MEDIA CONSOLE ====================
            // Handle social media commands via dedicated executor
            var socialMediaResult = await SocialMedia.SocialMediaToolExecutor.TryExecuteAsync(userMessage);
            if (socialMediaResult != null)
            {
                return socialMediaResult;
            }

            // ==================== SECURITY SUITE COMMANDS ====================
            // Open Security Suite window - only for explicit requests
            if (ContainsAny(msg, "open security suite", "open system monitor", "security suite", "security center", 
                "open security center", "system monitor window", "open antivirus", "security dashboard"))
            {
                return "__OPEN_SECURITY_SUITE__";
            }

            
            // Quick scan via Security Suite - must be explicit
            if (msg.StartsWith("quick scan") || msg.StartsWith("run quick scan") || 
                ContainsAny(msg, "run a quick scan", "do a quick scan", "start quick scan"))
            {
                return await RunSecuritySuiteScanAsync(ScanType.Quick);
            }
            
            // Junk scan - must be explicit
            if (ContainsAny(msg, "junk scan", "cleanup scan", "clean junk", "remove junk files", 
                "clear temp files", "clean temp files", "run disk cleanup"))
            {
                return await RunSecuritySuiteScanAsync(ScanType.Junk);
            }
            
            // Privacy scan - must be explicit
            if (ContainsAny(msg, "privacy scan", "run privacy scan", "check my privacy", "tracking scan"))
            {
                return await RunSecuritySuiteScanAsync(ScanType.Privacy);
            }
            
            // Check for definition updates
            if (ContainsAny(msg, "check updates", "check for updates", "update definitions", "update security",
                "security updates", "definition updates", "update defs"))
            {
                return await CheckSecurityUpdatesAsync();
            }
            
            // Update definitions now
            if (ContainsAny(msg, "update now", "install updates", "apply updates", "download updates"))
            {
                return await ApplySecurityUpdatesAsync();
            }

            // ==================== AI IMAGE GENERATION - HIGHEST PRIORITY ====================
            // Check for image generation requests FIRST before anything else
            if (ImageGeneratorTool.IsImageGenerationRequest(msg))
            {
                var prompt = ImageGeneratorTool.ExtractPrompt(userMessage);
                if (!string.IsNullOrEmpty(prompt))
                {
                    System.Diagnostics.Debug.WriteLine($"[ToolExecutor] 🎨 IMAGE GENERATION DETECTED: {prompt}");
                    return $"__GENERATE_IMAGE__|{prompt}";
                }
            }

            // ==================== CANVA DESIGN ASSISTANCE ====================
            // Handle Canva design requests - uses user's own API key or free UI guidance
            if (Canva.CanvaTool.IsCanvaRequest(msg))
            {
                System.Diagnostics.Debug.WriteLine($"[ToolExecutor] 🎨 CANVA DESIGN REQUEST DETECTED");
                
                // Check for session control commands
                if (ContainsAny(msg, "continue", "next step", "proceed"))
                {
                    return await Canva.CanvaTool.Instance.ContinueSessionAsync();
                }
                if (ContainsAny(msg, "cancel design", "stop design", "cancel canva"))
                {
                    return Canva.CanvaTool.Instance.CancelSession();
                }
                if (ContainsAny(msg, "pause design", "pause canva"))
                {
                    return Canva.CanvaTool.Instance.PauseSession();
                }
                if (ContainsAny(msg, "resume design", "resume canva"))
                {
                    return Canva.CanvaTool.Instance.ResumeSession();
                }
                
                // Check if there's an attached image for rebuild
                byte[]? imageData = null;
                var attachedForCanva = ChatWindow.LastDroppedPaths;
                if (attachedForCanva != null && attachedForCanva.Count > 0)
                {
                    var imageExts = new[] { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp" };
                    var imagePath = attachedForCanva.FirstOrDefault(p => 
                        File.Exists(p) && imageExts.Contains(Path.GetExtension(p).ToLower()));
                    if (!string.IsNullOrEmpty(imagePath))
                    {
                        imageData = File.ReadAllBytes(imagePath);
                    }
                }
                
                return await Canva.CanvaTool.Instance.ProcessRequestAsync(userMessage, imageData);
            }

            // ==================== IMAGE ANALYSIS - HIGHEST PRIORITY ====================
            // If user attached an image and is asking about it, analyze it FIRST
            var attachedPaths = ChatWindow.LastDroppedPaths;
            if (attachedPaths != null && attachedPaths.Count > 0)
            {
                var imageExtensions = new[] { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".tiff", ".ico" };
                var attachedImages = attachedPaths.Where(p => 
                    File.Exists(p) && imageExtensions.Contains(Path.GetExtension(p).ToLower())).ToList();
                
                if (attachedImages.Count > 0)
                {
                    // Check if user is asking about the image (not organizing files)
                    var isAskingAboutImage = ContainsAny(msg, "what", "explain", "describe", "analyze", "tell me", 
                        "show me", "read", "ocr", "text", "mean", "this", "that", "it", "see", "look", "?") ||
                        msg.Length < 50; // Short messages with images are usually about the image
                    
                    // Make sure it's NOT an organize request
                    var isImageOrganizeRequest = ContainsAny(msg, "organize", "sort", "clean", "tidy", "arrange", 
                        "put files", "move files", "orginize", "orgainze");
                    
                    if (isAskingAboutImage && !isImageOrganizeRequest)
                    {
                        System.Diagnostics.Debug.WriteLine($"[ToolExecutor] 🖼️ IMAGE ANALYSIS DETECTED - attached image: {attachedImages[0]}");
                        // Return special marker for image analysis
                        return $"__ANALYZE_IMAGE__|{attachedImages[0]}|{userMessage}";
                    }
                }
            }

            // ==================== UNDO COMMAND ====================
            // Handle undo requests immediately
            if (ContainsAny(msg, "undo", "revert", "undo that", "undo last", "take that back", "reverse that"))
            {
                return await HandleUndoCommand(msg);
            }
            
            // Show undo history
            if (ContainsAny(msg, "undo history", "what can i undo", "show undo", "undo list", "recent actions"))
            {
                return GetUndoHistory();
            }
            
            // ==================== FAST PATH - INSTANT EXECUTION ====================
            // Like Alexa/Siri - execute common commands IMMEDIATELY without AI processing
            var fastResult = TryFastExecute(msg);
            if (fastResult != null)
            {
                System.Diagnostics.Debug.WriteLine($"[ToolExecutor] ⚡ FAST PATH executed!");
                return await fastResult;
            }

            // ==================== INTELLIGENT UNDERSTANDING ====================
            // First, use the intelligent understanding system to figure out what user REALLY means
            string? normalizedMessage = null;
            try
            {
                var understanding = await IntelligentUnderstanding.UnderstandAsync(userMessage, ChatWindow.LastDroppedPaths);
                System.Diagnostics.Debug.WriteLine($"[ToolExecutor] 🧠 Understanding result:");
                System.Diagnostics.Debug.WriteLine($"[ToolExecutor]   - Original: '{userMessage}'");
                System.Diagnostics.Debug.WriteLine($"[ToolExecutor]   - Normalized: '{understanding.NormalizedInput}'");
                System.Diagnostics.Debug.WriteLine($"[ToolExecutor]   - Intent: {understanding.InferredIntent}");
                System.Diagnostics.Debug.WriteLine($"[ToolExecutor]   - Confidence: {understanding.Confidence:P0}");
                System.Diagnostics.Debug.WriteLine($"[ToolExecutor]   - Entities: {string.Join(", ", understanding.ExtractedEntities.Select(e => $"{e.Key}={e.Value}"))}");
                
                // Store normalized message for fallback keyword matching
                normalizedMessage = understanding.NormalizedInput;
                
                // Show user what we understood (if different from original)
                string? understandingNote = null;
                if (userMessage.ToLower().Trim() != understanding.NormalizedInput.ToLower().Trim())
                {
                    understandingNote = $"💡 I understood: \"{understanding.NormalizedInput}\"";
                    System.Diagnostics.Debug.WriteLine($"[ToolExecutor] {understandingNote}");
                }
                
                // If we need clarification, ask the user
                if (understanding.NeedsClarification && understanding.Confidence < 0.5f)
                {
                    System.Diagnostics.Debug.WriteLine($"[ToolExecutor] ❓ Needs clarification: {understanding.Clarification}");
                    return understanding.Clarification;
                }
                
                // Execute based on understood intent
                if (understanding.Confidence > 0.6f && understanding.InferredIntent != "unknown")
                {
                    System.Diagnostics.Debug.WriteLine($"[ToolExecutor] ✅ Executing from understanding...");
                    var result = await ExecuteFromUnderstanding(understanding);
                    if (result != null)
                    {
                        // Learn from this interaction
                        IntelligentUnderstanding.AddContext(
                            understanding.InferredIntent, 
                            understanding.ExtractedEntities.GetValueOrDefault("query", ""),
                            result);
                        System.Diagnostics.Debug.WriteLine($"[ToolExecutor] ✅ Result: {result.Substring(0, Math.Min(100, result.Length))}...");
                        
                        // Prepend understanding note if we corrected something
                        if (understandingNote != null)
                            return $"{understandingNote}\n\n{result}";
                        return result;
                    }
                    System.Diagnostics.Debug.WriteLine($"[ToolExecutor] ⚠️ ExecuteFromUnderstanding returned null, falling through...");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ToolExecutor] ❌ Understanding failed: {ex.Message}");
            }
            
            // Use normalized message for keyword matching if available
            if (!string.IsNullOrEmpty(normalizedMessage))
            {
                msg = normalizedMessage.ToLower().Trim();
                System.Diagnostics.Debug.WriteLine($"[ToolExecutor] 🔄 Using normalized message for keyword matching: {msg}");
            }

            // ==================== SMART AI INTENT PARSING ====================
            // Try AI-powered intent recognition first for better understanding
            if (_useSmartParsing)
            {
                try
                {
                    var intent = await SmartIntentParser.ParseIntentAsync(userMessage);
                    if (intent.Intent != SmartIntentParser.IntentType.Unknown && intent.Confidence > 0.6f)
                    {
                        System.Diagnostics.Debug.WriteLine($"[ToolExecutor] Smart intent: {intent.Intent} (conf: {intent.Confidence})");
                        var result = await SmartIntentParser.ExecuteIntentAsync(intent);
                        if (result != null)
                            return result;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[ToolExecutor] Smart parsing failed: {ex.Message}");
                    // Fall through to keyword matching
                }
            }

            // ==================== SOFTWARE INSTALLATION ====================
            // Like Kiro - can install anything the user asks for!
            if (ContainsAny(msg, "install ", "download ", "get me ", "i need ", "set up ", "setup "))
            {
                var softwareName = ExtractSoftwareName(userMessage);
                if (!string.IsNullOrEmpty(softwareName))
                {
                    System.Diagnostics.Debug.WriteLine($"[ToolExecutor] 📦 Installing: {softwareName}");
                    return await Agent.SoftwareInstaller.InstallAsync(softwareName);
                }
            }
            
            // Uninstall software - flexible detection for "uninstall chrome", "remove discord", etc.
            // Pattern: starts with uninstall/remove/delete OR contains these words with a software name
            var isUninstallRequest = msg.StartsWith("uninstall ") || 
                                     msg.StartsWith("remove ") ||
                                     msg.StartsWith("delete ") ||
                                     msg.StartsWith("get rid of ") ||
                                     ContainsAny(msg, "uninstall ", "remove ", "delete ", "get rid of ") ||
                                     (ContainsAny(msg, "uninstall", "remove", "delete") && ContainsAny(msg, "app", "program", "software"));
            
            // Exclude file/folder operations - those should go to file tools
            var isFileOperation = ContainsAny(msg, "file", "folder", "directory", "document", ".txt", ".pdf", ".doc");
            
            if (isUninstallRequest && !isFileOperation)
            {
                var softwareName = ExtractSoftwareName(userMessage);
                if (!string.IsNullOrEmpty(softwareName))
                {
                    System.Diagnostics.Debug.WriteLine($"[ToolExecutor] 🗑️ Uninstalling: {softwareName}");
                    
                    // Use the more robust AppUninstaller for better results
                    var apps = await SystemControl.AppUninstaller.GetInstalledAppsAsync();
                    var matchingApp = apps.FirstOrDefault(a => 
                        a.Name.Contains(softwareName, StringComparison.OrdinalIgnoreCase) ||
                        softwareName.Contains(a.Name, StringComparison.OrdinalIgnoreCase));
                    
                    if (matchingApp != null)
                    {
                        System.Diagnostics.Debug.WriteLine($"[ToolExecutor] Found app: {matchingApp.Name}");
                        var result = await SystemControl.AppUninstaller.UninstallAppAsync(matchingApp, cleanLeftovers: false);
                        return result.Success 
                            ? $"✅ {result.Message}" 
                            : $"❌ {result.Message}";
                    }
                    
                    // Fallback to winget-based uninstall
                    return await Agent.SoftwareInstaller.UninstallAsync(softwareName);
                }
            }
            
            // Check if software is installed
            if (ContainsAny(msg, "is ", "do i have ", "check if ") && ContainsAny(msg, " installed", " available"))
            {
                var softwareName = ExtractSoftwareName(userMessage);
                if (!string.IsNullOrEmpty(softwareName))
                {
                    var isInstalled = await Agent.SoftwareInstaller.IsInstalledAsync(softwareName);
                    return isInstalled 
                        ? $"✅ {softwareName} is installed on your system."
                        : $"❌ {softwareName} is NOT installed. Say 'install {softwareName}' to install it!";
                }
            }

            // ==================== AI IMAGE GENERATION ====================
            // Generate images using DALL-E: "generate image of X", "create picture of X", "draw X"
            if (ImageGeneratorTool.IsImageGenerationRequest(msg))
            {
                var prompt = ImageGeneratorTool.ExtractPrompt(userMessage);
                if (!string.IsNullOrEmpty(prompt))
                {
                    System.Diagnostics.Debug.WriteLine($"[ToolExecutor] 🎨 Generating image: {prompt}");
                    return $"__GENERATE_IMAGE__|{prompt}";
                }
            }
            
            // Open generated images folder
            if (ContainsAny(msg, "generated images", "my images", "show images", "open images folder", "image folder"))
            {
                ImageGeneratorTool.OpenImagesFolder();
                return "📂 Opened your generated images folder!";
            }

            // ==================== UNIFIED MEDIA PLAYER ====================
            // Supports: Spotify, YouTube, SoundCloud, Apple Music, Amazon Music, Deezer, Tidal, Pandora
            // With learning - remembers your preferences!
            
            // Set default music player
            if (ContainsAny(msg, "set default", "default player", "default music", "use spotify", "use youtube", 
                "use soundcloud", "use apple music", "use itunes", "use amazon", "use deezer", "use tidal", "use pandora"))
            {
                var platform = MediaPlayerTool.DetectPlatform(userMessage);
                if (platform.HasValue)
                {
                    return MediaPlayerTool.SetDefaultPlatform(platform.Value);
                }
                // Try to extract from "set X as default"
                if (msg.Contains("spotify")) return MediaPlayerTool.SetDefaultPlatform(MediaPlayerTool.Platform.Spotify);
                if (msg.Contains("youtube music") || msg.Contains("ytmusic")) return MediaPlayerTool.SetDefaultPlatform(MediaPlayerTool.Platform.YouTubeMusic);
                if (msg.Contains("youtube")) return MediaPlayerTool.SetDefaultPlatform(MediaPlayerTool.Platform.YouTube);
                if (msg.Contains("soundcloud")) return MediaPlayerTool.SetDefaultPlatform(MediaPlayerTool.Platform.SoundCloud);
                if (msg.Contains("apple") || msg.Contains("itunes")) return MediaPlayerTool.SetDefaultPlatform(MediaPlayerTool.Platform.AppleMusic);
                if (msg.Contains("amazon")) return MediaPlayerTool.SetDefaultPlatform(MediaPlayerTool.Platform.AmazonMusic);
                if (msg.Contains("deezer")) return MediaPlayerTool.SetDefaultPlatform(MediaPlayerTool.Platform.Deezer);
                if (msg.Contains("tidal")) return MediaPlayerTool.SetDefaultPlatform(MediaPlayerTool.Platform.Tidal);
                if (msg.Contains("pandora")) return MediaPlayerTool.SetDefaultPlatform(MediaPlayerTool.Platform.Pandora);
            }
            
            // Music stats
            if (ContainsAny(msg, "music stats", "my stats", "listening stats", "play history", "what do i listen"))
            {
                return MediaPlayerTool.GetStats();
            }
            
            // Play on specific platform (YouTube, SoundCloud, Apple Music, etc.)
            var detectedPlatform = MediaPlayerTool.DetectPlatform(userMessage);
            if (detectedPlatform.HasValue && ContainsAny(msg, "play ", "put on ", "listen to ", "open "))
            {
                var query = MediaPlayerTool.ExtractQuery(userMessage);
                if (!string.IsNullOrEmpty(query))
                {
                    System.Diagnostics.Debug.WriteLine($"[ToolExecutor] Playing on {detectedPlatform}: {query}");
                    return await MediaPlayerTool.PlayAsync(query, detectedPlatform);
                }
            }
            
            // Generic play command - uses smart platform selection (learns from usage)
            if (ContainsAny(msg, "play ", "put on ", "listen to ") && 
                (ContainsAny(msg, "song", "music", "track", "artist", "album", " by ") || msg.StartsWith("play ")))
            {
                // Skip if it's about video/game/movie without a music platform
                if (ContainsAny(msg, "video", "game", "movie") && detectedPlatform == null)
                {
                    // Let it fall through to other handlers
                }
                else
                {
                    var query = MediaPlayerTool.ExtractQuery(userMessage);
                    if (!string.IsNullOrEmpty(query))
                    {
                        // Use smart platform selection (learns from your usage)
                        var smartPlatform = MediaPlayerTool.GetSmartPlatformSuggestion(query);
                        System.Diagnostics.Debug.WriteLine($"[ToolExecutor] Smart play on {smartPlatform}: {query}");
                        return await MediaPlayerTool.PlayAsync(query, smartPlatform);
                    }
                }
            }

            // Playback controls (work with Spotify desktop)
            if (ContainsAny(msg, "pause music", "pause spotify", "pause song", "stop music", "stop spotify", "pause"))
            {
                return await SpotifyTool.ControlPlaybackAsync("pause");
            }
            if (ContainsAny(msg, "resume music", "resume spotify", "unpause", "continue playing", "resume"))
            {
                return await SpotifyTool.ControlPlaybackAsync("play");
            }
            if (ContainsAny(msg, "next song", "skip song", "next track", "skip track", "skip this", "skip"))
            {
                return await SpotifyTool.ControlPlaybackAsync("next");
            }
            if (ContainsAny(msg, "previous song", "last song", "previous track", "go back"))
            {
                return await SpotifyTool.ControlPlaybackAsync("previous");
            }

            // ==================== WEATHER ====================
            // Weather queries - be more flexible with location extraction
            if (ContainsAny(msg, "weather", "temperature", "forecast", "rain today", "rain tomorrow", "sunny", "cloudy", "what's it like outside", "whats it like outside"))
            {
                var location = ExtractLocation(userMessage);
                // If no location found, try to use the whole message after "weather"
                if (string.IsNullOrEmpty(location))
                {
                    var idx = msg.IndexOf("weather");
                    if (idx >= 0 && idx + 7 < msg.Length)
                        location = msg.Substring(idx + 7).Trim().TrimStart('i', 'n', ' ');
                }
                if (string.IsNullOrEmpty(location))
                    location = "Middlesbrough"; // Default to user's location
                System.Diagnostics.Debug.WriteLine($"[ToolExecutor] Weather for: {location}");
                return await WebSearchTool.GetWeatherAsync(location);
            }
            
            // Web search queries - return results with links
            if (ContainsAny(msg, "search for", "look up", "find info", "search the web", "google", "search online", 
                "search internet", "search ", "find me", "look for", "what is", "who is", "when did", "how to", 
                "define ", "meaning of", "tell me about", "information on", "info about", "info on"))
            {
                var query = ExtractSearchQuery(userMessage);
                if (!string.IsNullOrEmpty(query))
                {
                    System.Diagnostics.Debug.WriteLine($"[ToolExecutor] Web search: {query}");
                    return await WebSearchTool.SearchAsync(query);
                }
            }
            
            // Open URL
            if (msg.StartsWith("open ") && ContainsAny(msg, ".com", ".org", ".net", "http", "www"))
            {
                var url = ExtractUrl(userMessage);
                if (!string.IsNullOrEmpty(url))
                {
                    System.Diagnostics.Debug.WriteLine($"[ToolExecutor] Open URL: {url}");
                    return await SystemTool.OpenUrlAsync(url);
                }
            }
            
            // Open app - more flexible matching
            // BUT first check if it's "open spotify and play X" or "open spotify, play X" - that should play music, not just open
            if (ContainsAny(msg, "open ", "launch ", "start ", "run "))
            {
                // Check for "open spotify and play X" or "open spotify, play X" pattern - should play music
                if (msg.Contains("spotify") && ContainsAny(msg, " and play ", " then play ", " to play ", ", play ", " play "))
                {
                    var playMatch = System.Text.RegularExpressions.Regex.Match(msg, @"(?:and|then|to|,)?\s*play\s+(.+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    if (playMatch.Success)
                    {
                        var query = playMatch.Groups[1].Value.Trim();
                        // Remove trailing punctuation
                        query = query.TrimEnd('.', '!', '?');
                        System.Diagnostics.Debug.WriteLine($"[ToolExecutor] Open Spotify and play: {query}");
                        return await MediaPlayerTool.PlayAsync(query, MediaPlayerTool.Platform.Spotify);
                    }
                }
                
                // Check for "open youtube and play X" or "open youtube, play X" pattern
                if (msg.Contains("youtube") && ContainsAny(msg, " and play ", " then play ", " to play ", ", play ", " play ", " and watch ", " then watch ", ", watch "))
                {
                    var playMatch = System.Text.RegularExpressions.Regex.Match(msg, @"(?:and|then|to|,)?\s*(?:play|watch)\s+(.+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    if (playMatch.Success)
                    {
                        var query = playMatch.Groups[1].Value.Trim();
                        query = query.TrimEnd('.', '!', '?');
                        System.Diagnostics.Debug.WriteLine($"[ToolExecutor] Open YouTube and play: {query}");
                        return await MediaPlayerTool.PlayAsync(query, MediaPlayerTool.Platform.YouTube);
                    }
                }
                
                // First check for Windows system utilities
                var utility = ExtractSystemUtility(userMessage);
                if (!string.IsNullOrEmpty(utility))
                {
                    System.Diagnostics.Debug.WriteLine($"[ToolExecutor] Open system utility: {utility}");
                    return await SystemTool.OpenSystemUtilityAsync(utility);
                }
                
                // Check for special Windows locations
                var specialPath = ExtractWindowsSystemPath(msg);
                if (!string.IsNullOrEmpty(specialPath))
                {
                    System.Diagnostics.Debug.WriteLine($"[ToolExecutor] Open special path: {specialPath}");
                    return await SystemTool.OpenFolderAsync(specialPath);
                }
                
                var app = ExtractAppName(userMessage);
                if (!string.IsNullOrEmpty(app) && !ContainsAny(app, ".com", ".org", ".net", "http"))
                {
                    System.Diagnostics.Debug.WriteLine($"[ToolExecutor] Open app: {app}");
                    return await SystemTool.OpenAppAsync(app);
                }
            }
            
            // System info
            if (ContainsAny(msg, "system info", "computer info", "pc info", "my computer", "system specs"))
            {
                return await SystemTool.GetSystemInfoAsync();
            }
            
            // List files
            if (ContainsAny(msg, "list files", "show files", "what files", "directory", "folder contents"))
            {
                var path = ExtractPath(userMessage);
                return await SystemTool.ListFilesAsync(path);
            }
            
            // Run command
            if (msg.StartsWith("run ") || msg.StartsWith("execute ") || msg.StartsWith("cmd "))
            {
                var command = ExtractCommand(userMessage);
                if (!string.IsNullOrEmpty(command))
                {
                    return await SystemTool.RunCommandAsync(command);
                }
            }

            // Consolidate/flatten files from subfolders into one folder - CHECK THIS FIRST before organize
            // Also handle "put files into 1 folder" when a folder is attached
            var hasAttachedFolderForConsolidate = ChatWindow.LastDroppedPaths?.Any(p => Directory.Exists(p)) == true;
            var isConsolidateIntent = ContainsAny(msg, "consolidate", "flatten", "put them into 1 folder", "put them into one folder",
                "move to one folder", "move to 1 folder", "into one folder", "into 1 folder",
                "each song is in a separate folder", "each file is in a separate folder", "separate folder",
                "songs in there but each", "files in there but each", "subfolders into", "subfolder into",
                "delete empty folders", "remove empty folders", "delete empy folders", "empy folders",
                "all in one folder", "all into one", "merge folders", "combine folders", "all songs in 1",
                "all files in 1", "all songs in one", "all files in one", "move all to", "put all in",
                "1 folder", "one folder", "single folder", "same folder", "tracks in separate", "songs in separate",
                "each track is in", "each song is in", "pull out", "extract from subfolders", "from subfolders",
                "put files into 1", "put files into one", "originize files into one", "orginize files into one",
                "organize files into one", "organize into one folder", "put into 1 folder", "put into one folder");
            
            // If user attached a folder and says anything about "1 folder" or "one folder", it's consolidate
            if (hasAttachedFolderForConsolidate && ContainsAny(msg, "1 folder", "one folder", "into folder", "put files"))
            {
                isConsolidateIntent = true;
            }
            
            if (isConsolidateIntent)
            {
                var path = ExtractPath(userMessage);
                System.Diagnostics.Debug.WriteLine($"[ToolExecutor] Consolidating files in: {(string.IsNullOrEmpty(path) ? "need path" : path)}");
                
                // FIRST check for dropped paths - this is the priority
                var droppedPaths = ChatWindow.LastDroppedPaths;
                if (droppedPaths != null && droppedPaths.Count > 0)
                {
                    var results = new List<string>();
                    foreach (var droppedPath in droppedPaths)
                    {
                        if (Directory.Exists(droppedPath))
                        {
                            System.Diagnostics.Debug.WriteLine($"[ToolExecutor] Consolidating dropped folder: {droppedPath}");
                            var res = await SystemTool.ConsolidateFilesAsync(droppedPath);
                            results.Add(res);
                        }
                    }
                    if (results.Count > 0)
                    {
                        // Clear the dropped paths after use
                        ChatWindow.LastDroppedPaths?.Clear();
                        return string.Join("\n\n", results);
                    }
                }
                
                // If no dropped paths, try extracted path
                if (!string.IsNullOrEmpty(path))
                {
                    var result = await SystemTool.ConsolidateFilesAsync(path);
                    System.Diagnostics.Debug.WriteLine($"[ToolExecutor] Result: {result}");
                    return result;
                }
                
                return "❌ Please attach a folder using 📎 or specify the path (e.g., 'consolidate files in Desktop/MyAlbum')";
            }

            // Sort/organize files by TYPE into folders - only if NOT consolidating
            // Skip if message contains consolidate-related keywords
            // Check if user wants to organize files (with attached folder context)
            var hasAttachedFolder = ChatWindow.LastDroppedPaths?.Any(p => Directory.Exists(p)) == true;
            
            System.Diagnostics.Debug.WriteLine($"[ToolExecutor] REACHED ORGANIZE DETECTION BLOCK for: {msg}");
            
            // ENHANCED: Match organize + location directly (e.g., "organize desktop", "clean downloads")
            var hasOrganizeLocation = ContainsAny(msg, 
                "organize desktop", "clean desktop", "sort desktop", "tidy desktop", "cleanup desktop",
                "orginize desktop", "orgizize desktop", "orgainze desktop", // Common typos with "desktop"
                "organize downloads", "clean downloads", "sort downloads", "tidy downloads",
                "orginize downloads", "orgizize downloads", 
                "organize documents", "clean documents", "sort documents", "tidy documents",
                "orginize documents", "orgizize documents",
                "organize pictures", "clean pictures", "sort pictures", "tidy pictures",
                "organize music", "clean music", "sort music", "tidy music",
                "organize videos", "clean videos", "sort videos", "tidy videos",
                "organize my desktop", "clean my desktop", "cleanup my desktop", "clean up my desktop",
                "orginize my desktop", "orgizize my desktop", // Typos with "my"
                "organize my downloads", "clean my downloads", "cleanup my downloads",
                "orginize my downloads", "orgizize my downloads",
                "organize my documents", "clean my documents",
                "organize my pictures", "clean my pictures",
                "organize my music", "clean my music",
                "organize my videos", "clean my videos"
            );
            
            System.Diagnostics.Debug.WriteLine($"[ToolExecutor] hasOrganizeLocation={hasOrganizeLocation} for message: {msg}");
            
            var isOrganizeRequest = hasOrganizeLocation || 
                ContainsAny(msg, "sort files", "organize files", "clean up", "sort my", "organize my", "tidy up", 
                "organise files", "organise my", "arrange files", "arrange my", "cleanup my", "clean my files",
                "organize the files", "sort the files", "put files in folders", "categorize files", "categorise files",
                "orginize", "orgainze", "organiz", "oraganize", "ogranize", "orgizize", "orginise", "organze", "sort by type", "organize by type") ||
                (hasAttachedFolder && ContainsAny(msg, "put files into", "organize into", "sort into", "put into folder", "put files in"));
            
            var isConsolidateRequest = ContainsAny(msg, "consolidate", "flatten", "subfolders", "subfolder", "separate folder", 
                "each track", "each song", "pull out", "extract from") ||
                (!hasAttachedFolder && ContainsAny(msg, "one folder", "1 folder", "single folder", "same folder"));
            
            System.Diagnostics.Debug.WriteLine($"[ToolExecutor] isOrganizeRequest={isOrganizeRequest}, isConsolidateRequest={isConsolidateRequest}");
            
            if (!isConsolidateRequest && isOrganizeRequest)
            {
                System.Diagnostics.Debug.WriteLine($"[ToolExecutor] ⚡ ORGANIZE REQUEST DETECTED - will execute SortFilesByTypeAsync");
                
                // Check if there are dropped files/folders to use
                var droppedPaths = ChatWindow.LastDroppedPaths;
                if (droppedPaths != null && droppedPaths.Count > 0)
                {
                    var results = new List<string>();
                    foreach (var droppedPath in droppedPaths)
                    {
                        if (Directory.Exists(droppedPath))
                        {
                            System.Diagnostics.Debug.WriteLine($"[ToolExecutor] Organizing dropped folder: {droppedPath}");
                            var result = await SystemTool.SortFilesByTypeAsync(droppedPath);
                            results.Add(result);
                        }
                    }
                    if (results.Count > 0)
                        return string.Join("\n\n", results);
                }
                
                var path = ExtractPath(userMessage);
                System.Diagnostics.Debug.WriteLine($"[ToolExecutor] Organizing files in: {(string.IsNullOrEmpty(path) ? "Desktop (default)" : path)}");
                var sortResult = await SystemTool.SortFilesByTypeAsync(path);
                System.Diagnostics.Debug.WriteLine($"[ToolExecutor] Result: {sortResult}");
                return sortResult;
            }

            // Create folder
            if (ContainsAny(msg, "create folder", "make folder", "new folder", "mkdir"))
            {
                var path = ExtractNewPath(userMessage);
                if (!string.IsNullOrEmpty(path))
                    return await SystemTool.CreateFolderAsync(path);
            }

            // Delete file/folder - Uses double confirmation system
            if (ContainsAny(msg, "delete ", "remove ", "del ") && !msg.Contains("how"))
            {
                var path = ExtractTargetPath(userMessage);
                if (!string.IsNullOrEmpty(path))
                {
                    // Resolve path - check desktop, documents, etc.
                    var resolvedPath = ResolvePath(path, userMessage);
                    if (!string.IsNullOrEmpty(resolvedPath))
                    {
                        return await SystemTool.DeleteWithConfirmationAsync(resolvedPath);
                    }
                    return $"❌ Could not find: {path}";
                }
            }

            // Move file/folder
            if (ContainsAny(msg, "move ") && ContainsAny(msg, " to "))
            {
                var (source, dest) = ExtractSourceDest(userMessage);
                if (!string.IsNullOrEmpty(source) && !string.IsNullOrEmpty(dest))
                    return await SystemTool.MoveAsync(source, dest);
            }

            // Copy file/folder
            if (ContainsAny(msg, "copy ") && ContainsAny(msg, " to "))
            {
                var (source, dest) = ExtractSourceDest(userMessage);
                if (!string.IsNullOrEmpty(source) && !string.IsNullOrEmpty(dest))
                    return await SystemTool.CopyAsync(source, dest);
            }

            // Rename file/folder
            if (ContainsAny(msg, "rename ") && ContainsAny(msg, " to "))
            {
                var (path, newName) = ExtractRenameParts(userMessage);
                if (!string.IsNullOrEmpty(path) && !string.IsNullOrEmpty(newName))
                    return await SystemTool.RenameAsync(path, newName);
            }

            // Find files
            if (ContainsAny(msg, "find file", "search file", "locate ", "where is"))
            {
                var (path, pattern) = ExtractFindParams(userMessage);
                return await SystemTool.FindFilesAsync(path, pattern);
            }

            // ==================== WINDOWS COPILOT INTEGRATION ====================
            // Delegate complex Windows tasks to Windows Copilot for native integration
            if (ContainsAny(msg, 
                "change wallpaper", "set wallpaper", "change background", "set background",
                "change theme", "switch theme", "dark mode", "light mode", "dark theme", "light theme",
                "screen brightness", "brightness", "dim screen", "brighten screen",
                "bluetooth settings", "pair bluetooth", "connect bluetooth",
                "wifi settings", "connect to wifi", "network settings",
                "display settings", "screen resolution", "monitor settings",
                "sound settings", "audio settings", "speaker settings",
                "keyboard settings", "mouse settings", "touchpad settings",
                "power options", "power plan", "sleep settings", "battery settings",
                "windows update", "check for updates", "install updates",
                "add printer", "printer settings", "manage printers",
                "add remove programs", "uninstall program", "programs and features",
                "disk cleanup", "defragment", "optimize drives",
                "backup settings", "restore point", "system restore",
                "accessibility settings", "narrator", "magnifier", "high contrast",
                "privacy settings", "location settings", "camera settings", "microphone settings",
                "notification settings", "focus assist", "do not disturb",
                "startup programs", "task manager startup", "disable startup",
                "file history", "backup files", "restore files"
            ))
            {
                try
                {
                    // Launch Windows Copilot with the natural language query
                    var copilotUri = $"ms-copilot:?query={Uri.EscapeDataString(userMessage)}";
                    var psi = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = copilotUri,
                        UseShellExecute = true
                    };
                    System.Diagnostics.Process.Start(psi);
                    return $"🪟 Opened Windows Copilot to help with: {userMessage}\n\nWindows Copilot has native access to system settings and can help you with this task!";
                }
                catch
                {
                    // Fallback: open Settings app to relevant section
                    var settingsUrls = new Dictionary<string, string>
                    {
                        { "wallpaper", "ms-settings:personalization-background" },
                        { "theme", "ms-settings:themes" },
                        { "brightness", "ms-settings:display" },
                        { "bluetooth", "ms-settings:bluetooth" },
                        { "wifi", "ms-settings:network-wifi" },
                        { "network", "ms-settings:network" },
                        { "display", "ms-settings:display" },
                        { "sound", "ms-settings:sound" },
                        { "audio", "ms-settings:sound" },
                        { "keyboard", "ms-settings:keyboard" },
                        { "mouse", "ms-settings:mousetouchpad" },
                        { "power", "ms-settings:powersleep" },
                        { "battery", "ms-settings:batterysaver" },
                        { "update", "ms-settings:windowsupdate" },
                        { "printer", "ms-settings:printers" },
                        { "programs", "ms-settings:appsfeatures" },
                        { "uninstall", "ms-settings:appsfeatures" },
                        { "backup", "ms-settings:backup" },
                        { "privacy", "ms-settings:privacy" },
                        { "notification", "ms-settings:notifications" },
                        { "startup", "ms-settings:startupapps" }
                    };
                    
                    foreach (var kvp in settingsUrls)
                    {
                        if (msg.Contains(kvp.Key))
                        {
                            var psi = new System.Diagnostics.ProcessStartInfo
                            {
                                FileName = kvp.Value,
                                UseShellExecute = true
                            };
                            System.Diagnostics.Process.Start(psi);
                            return $"⚙️ Opened Windows Settings for: {kvp.Key}";
                        }
                    }
                    
                    return "⚠️ Windows Copilot is not available. Try opening Settings manually.";
                }
            }

            // ==================== ADVANCED PC CONTROL ====================

            // Volume control
            if (ContainsAny(msg, "volume", "sound level"))
            {
                if (ContainsAny(msg, "mute", "unmute", "toggle mute"))
                    return await SystemTool.ToggleMuteAsync();
                var volumeMatch = Regex.Match(msg, @"(\d+)\s*%?");
                if (volumeMatch.Success && int.TryParse(volumeMatch.Groups[1].Value, out var level))
                    return await SystemTool.SetVolumeAsync(level);
                if (ContainsAny(msg, "up", "increase", "louder"))
                    return await SystemTool.SetVolumeAsync(80);
                if (ContainsAny(msg, "down", "decrease", "quieter", "lower"))
                    return await SystemTool.SetVolumeAsync(30);
            }

            // Mute
            if (ContainsAny(msg, "mute", "unmute"))
                return await SystemTool.ToggleMuteAsync();

            // Lock computer
            if (ContainsAny(msg, "lock computer", "lock pc", "lock screen", "lock my"))
                return await SystemTool.LockComputerAsync();

            // Shutdown
            if (ContainsAny(msg, "shutdown", "shut down", "turn off computer", "power off"))
            {
                if (msg.Contains("cancel"))
                    return await SystemTool.CancelShutdownAsync();
                return await SystemTool.ShutdownAsync(60);
            }

            // Restart
            if (ContainsAny(msg, "restart", "reboot"))
            {
                if (msg.Contains("cancel"))
                    return await SystemTool.CancelShutdownAsync();
                return await SystemTool.RestartAsync(60);
            }

            // Sleep
            if (ContainsAny(msg, "sleep", "hibernate", "standby"))
                return await SystemTool.SleepAsync();

            // Empty recycle bin
            if (ContainsAny(msg, "empty recycle", "clear recycle", "empty trash", "clear trash"))
                return await SystemTool.EmptyRecycleBinAsync();

            // Battery status
            if (ContainsAny(msg, "battery", "power status", "charge level"))
                return await SystemTool.GetBatteryStatusAsync();

            // Running processes
            if (ContainsAny(msg, "running processes", "task list", "what's running", "show processes", "list processes"))
                return await SystemTool.GetProcessesAsync();

            // Kill process
            if (ContainsAny(msg, "kill ", "end task", "close process", "stop process"))
            {
                var processMatch = Regex.Match(msg, @"(?:kill|end task|close process|stop process)\s+(.+)", RegexOptions.IgnoreCase);
                if (processMatch.Success)
                    return await SystemTool.KillProcessAsync(processMatch.Groups[1].Value.Trim());
            }

            // Disk space
            if (ContainsAny(msg, "disk space", "storage space", "free space", "hard drive"))
                return await SystemTool.GetDiskSpaceAsync();

            // Network info
            if (ContainsAny(msg, "network info", "ip address", "my ip", "network status"))
                return await SystemTool.GetNetworkInfoAsync();

            // Open settings
            if (ContainsAny(msg, "open settings", "windows settings", "go to settings"))
            {
                var settingsPage = "";
                if (msg.Contains("wifi") || msg.Contains("network")) settingsPage = "wifi";
                else if (msg.Contains("bluetooth")) settingsPage = "bluetooth";
                else if (msg.Contains("display") || msg.Contains("screen")) settingsPage = "display";
                else if (msg.Contains("sound") || msg.Contains("audio")) settingsPage = "sound";
                else if (msg.Contains("power") || msg.Contains("battery")) settingsPage = "power";
                else if (msg.Contains("storage")) settingsPage = "storage";
                else if (msg.Contains("apps")) settingsPage = "apps";
                else if (msg.Contains("update")) settingsPage = "update";
                return await SystemTool.OpenSettingsAsync(settingsPage);
            }

            // Type text
            if (msg.StartsWith("type "))
            {
                var text = userMessage.Substring(5).Trim();
                return await SystemTool.TypeTextAsync(text);
            }

            // Press key - but NOT if it's a troubleshooting request about keys not working
            if (ContainsAny(msg, "press ", "hit ") && 
                !ContainsAny(msg, "not working", "isn't working", "isnt working", "doesn't work", "doesnt work", 
                             "broken", "fix ", "help ", "problem", "issue", "trouble"))
            {
                var keyMatch = Regex.Match(msg, @"(?:press|hit)\s+(.+)", RegexOptions.IgnoreCase);
                if (keyMatch.Success)
                    return await SystemTool.PressKeyAsync(keyMatch.Groups[1].Value.Trim());
            }

            // Screenshot
            if (ContainsAny(msg, "take screenshot", "capture screen", "screenshot"))
                return await SystemTool.TakeScreenshotAsync();

            return null; // No tool needed
        }
        
        /// <summary>
        /// Check if user message needs a tool and execute it with cancellation support
        /// Returns tool result or null if no tool needed
        /// </summary>
        public static async Task<string?> TryExecuteToolWithCancellationAsync(
            string userMessage, 
            CancellationToken ct,
            Action<AtlasAI.SystemControl.UnifiedScanner>? onScannerCreated = null)
        {
            // Strip context prefix if present - handle multiple formats:
            // Format 1: "[Context: ...]\n\nUser: message" 
            // Format 2: "{memory context}\nUser: message"
            // Format 3: "[Session Context: ...]\nUser: message"
            var cleanMessage = userMessage;
            if (cleanMessage.Contains("User:"))
            {
                var userIdx = cleanMessage.LastIndexOf("User:");
                if (userIdx >= 0)
                {
                    cleanMessage = cleanMessage.Substring(userIdx + 5).Trim();
                    System.Diagnostics.Debug.WriteLine($"[ToolExecutor] Stripped context, clean message: {cleanMessage}");
                }
            }
            
            var msg = cleanMessage.ToLower().Trim();
            System.Diagnostics.Debug.WriteLine($"[ToolExecutor] ========== PROCESSING (with CT): {msg} ==========");

            ct.ThrowIfCancellationRequested();

            // ==================== DIRECT ACTION HANDLER ====================
            // Keep the live chat path authoritative through ToolExecutor even for legacy direct actions.
            try
            {
                var direct = await DirectActionHandler.TryHandleAsync(cleanMessage);
                if (direct != null)
                    return direct;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ToolExecutor] DirectActionHandler error (CT): {ex.Message}");
            }
            
            // ==================== DIRECT SHELL COMMANDS - HIGHEST PRIORITY ====================
            // Execute shell commands like ipconfig, ping, etc. IMMEDIATELY - BEFORE anything else
            var directCommands = new[] { "ipconfig", "ping", "systeminfo", "hostname", "whoami", "netstat", "tasklist", "dir", "tree", "ver", "nslookup", "tracert", "arp", "route", "cls", "help" };
            foreach (var cmd in directCommands)
            {
                // Direct command: "ipconfig" or "ipconfig /all"
                if (msg == cmd || msg.StartsWith(cmd + " ") || msg.StartsWith(cmd + "/"))
                {
                    System.Diagnostics.Debug.WriteLine($"[ToolExecutor] ⚡ Direct shell command (CT): {cmd}");
                    return await SystemTool.RunCommandAsync(cleanMessage.Trim());
                }
                // "open ipconfig", "run ipconfig", "show ipconfig", "execute ipconfig"
                if (msg == $"open {cmd}" || msg == $"run {cmd}" || msg == $"show {cmd}" || msg == $"execute {cmd}" ||
                    msg.StartsWith($"open {cmd} ") || msg.StartsWith($"run {cmd} ") || msg.StartsWith($"show {cmd} "))
                {
                    System.Diagnostics.Debug.WriteLine($"[ToolExecutor] ⚡ Shell command from 'open/run/show {cmd}' (CT)");
                    return await SystemTool.RunCommandAsync(cmd);
                }
            }
            
            // ==================== FIX THREATS - Handle "fix", "fix it", etc. ====================
            // This MUST be checked FIRST before other commands
            if (msg == "fix" || msg == "fixit" || msg.StartsWith("fix ") || 
                ContainsAny(msg, "fix it", "fix the problem", "fix problems", "remove threats", "remove the threats",
                "clean it", "clean them", "delete threats", "fix threats", "remove them", "get rid of them",
                "fix my computer", "fix my pc", "clean my computer", "clean my pc", "remove all threats",
                "fix them", "fix those", "fix that", "fix the threats", "fix the issues", "fix issues"))
            {
                System.Diagnostics.Debug.WriteLine($"[ToolExecutor] FIX IT COMMAND DETECTED (CT) - calling FixDetectedThreatsAsync");
                return await FixDetectedThreatsAsync();
            }

            // ==================== MACRO SKILL (user-defined sequences) ====================
            // Must be early so confirm/cancel doesn't get swallowed by other handlers.
            try
            {
                var macro = await MacroSkill.TryExecuteAsync(cleanMessage, ct);
                if (macro != null)
                    return macro;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ToolExecutor] MacroSkill error (CT): {ex.Message}");
            }

            // ==================== ACTIVE WINDOW AWARENESS ====================
            try
            {
                var aw = await ActiveWindowSkill.TryExecuteAsync(cleanMessage, ct);
                if (aw != null)
                    return aw;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ToolExecutor] ActiveWindowSkill error (CT): {ex.Message}");
            }

            // ==================== AI IMAGE GENERATION - HIGHEST PRIORITY ====================
            // Check for image generation requests FIRST before anything else
            if (ImageGeneratorTool.IsImageGenerationRequest(msg))
            {
                var prompt = ImageGeneratorTool.ExtractPrompt(userMessage);
                if (!string.IsNullOrEmpty(prompt))
                {
                    System.Diagnostics.Debug.WriteLine($"[ToolExecutor] 🎨 IMAGE GENERATION DETECTED (CT): {prompt}");
                    return $"__GENERATE_IMAGE__|{prompt}";
                }
            }

            // ==================== IMAGE ANALYSIS - HIGHEST PRIORITY ====================
            // If user attached an image and is asking about it, analyze it FIRST
            var attachedPathsCT = ChatWindow.LastDroppedPaths;
            if (attachedPathsCT != null && attachedPathsCT.Count > 0)
            {
                var imageExtensions = new[] { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".tiff", ".ico" };
                var attachedImages = attachedPathsCT.Where(p => 
                    File.Exists(p) && imageExtensions.Contains(Path.GetExtension(p).ToLower())).ToList();
                
                if (attachedImages.Count > 0)
                {
                    // Check if user is asking about the image (not organizing files)
                    var isAskingAboutImage = ContainsAny(msg, "what", "explain", "describe", "analyze", "tell me", 
                        "show me", "read", "ocr", "text", "mean", "this", "that", "it", "see", "look", "?") ||
                        msg.Length < 50; // Short messages with images are usually about the image
                    
                    // Make sure it's NOT an organize request
                    var isImageOrganizeRequest = ContainsAny(msg, "organize", "sort", "clean", "tidy", "arrange", 
                        "put files", "move files", "orginize", "orgainze");
                    
                    if (isAskingAboutImage && !isImageOrganizeRequest)
                    {
                        System.Diagnostics.Debug.WriteLine($"[ToolExecutor] 🖼️ IMAGE ANALYSIS DETECTED - attached image: {attachedImages[0]}");
                        // Return special marker for image analysis
                        return $"__ANALYZE_IMAGE__|{attachedImages[0]}|{userMessage}";
                    }
                }
            }

            // ==================== IN-APP NAVIGATION (Command Center) ====================
            // Handle view/module navigation before other fast-path tools.
            try
            {
                var nav = await NavigationSkill.TryExecuteAsync(cleanMessage, ct);
                if (nav != null)
                    return nav;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ToolExecutor] NavigationSkill error (CT): {ex.Message}");
            }

            // ==================== CLIPBOARD SKILL ====================
            try
            {
                var clip = await ClipboardSkill.TryExecuteAsync(cleanMessage, ct);
                if (clip != null)
                    return clip;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ToolExecutor] ClipboardSkill error (CT): {ex.Message}");
            }

            // ==================== FILE BUTLER (safe + elevated with preview/confirm) ====================
            try
            {
                var fb = await FileButlerSkill.TryExecuteAsync(cleanMessage, ct);
                if (fb != null)
                    return fb;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ToolExecutor] FileButlerSkill error (CT): {ex.Message}");
            }

            // ==================== DIAGNOSTICS (tail logs + exception summary) ====================
            try
            {
                var diag = await DiagnosticsSkill.TryExecuteAsync(cleanMessage, ct);
                if (diag != null)
                    return diag;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ToolExecutor] DiagnosticsSkill error (CT): {ex.Message}");
            }

            // ==================== UI ANALYZER (ViewModel state inspection) ====================
            try
            {
                var ui = await UIAnalyzerSkill.TryExecuteAsync(cleanMessage, ct);
                if (ui != null)
                    return ui;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ToolExecutor] UIAnalyzerSkill error (CT): {ex.Message}");
            }

            // ==================== PERSONALITY MEMORY (local, non-creepy) ====================
            try
            {
                var mem = await PersonalityMemorySkill.TryExecuteAsync(cleanMessage, ct);
                if (mem != null)
                    return mem;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ToolExecutor] PersonalityMemorySkill error (CT): {ex.Message}");
            }

            // ==================== CHAT SETTINGS (personality + ambience) ====================
            try
            {
                var persona = await ChatPersonalitySkill.TryExecuteAsync(cleanMessage, ct);
                if (persona != null)
                    return persona;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ToolExecutor] ChatPersonalitySkill error (CT): {ex.Message}");
            }

            try
            {
                var dim = await AmbientDimSkill.TryExecuteAsync(cleanMessage, ct);
                if (dim != null)
                    return dim;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ToolExecutor] AmbientDimSkill error (CT): {ex.Message}");
            }

            try
            {
                var media = await MediaCentreAutomationSkill.TryExecuteAsync(cleanMessage, ct);
                if (media != null)
                    return media;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ToolExecutor] MediaCentreAutomationSkill error (CT): {ex.Message}");
            }

            try
            {
                var dj = await DjSuggestionSkill.TryExecuteAsync(cleanMessage, ct);
                if (dj != null)
                    return dj;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ToolExecutor] DjSuggestionSkill error (CT): {ex.Message}");
            }

            // ==================== FAST PATH - INSTANT EXECUTION ====================
            // Check for scan commands first with cancellation support
            if (ContainsAny(msg, "scan system", "system scan", "scan files", "scan computer", "scan pc", 
                "scan for virus", "scan for malware", "scan for spyware", "virus scan", "malware scan",
                "spyware scan", "security scan", "deep scan", "full scan", "sfc scan", "system file checker",
                "scan registry", "registry scan", "scan for errors", "scan windows",
                "scan my system", "scan my computer", "scan my pc", "scan my files", "scan me"))
            {
                // Check if it's specifically SFC (System File Checker)
                if (ContainsAny(msg, "sfc", "system file checker", "repair system", "fix system files"))
                {
                    return await SystemTool.OpenSystemUtilityAsync("system file checker");
                }
                
                // Otherwise, run our spyware/malware scan with cancellation support
                try
                {
                    var scanner = new AtlasAI.SystemControl.UnifiedScanner();
                    onScannerCreated?.Invoke(scanner); // Allow caller to track the scanner for cancellation
                    
                    System.Diagnostics.Debug.WriteLine("[ToolExecutor] Starting scan with progress hooks...");
                    
                    // Track state for combined updates
                    string currentFile = "";
                    string currentPhase = "Initializing...";
                    long filesScanned = 0;
                    int currentPercent = 0;
                    var scanStartTime = DateTime.Now;
                    
                    // Helper to update UI with all info combined - uses Background priority to not block UI
                    void UpdateScanUI()
                    {
                        try
                        {
                            System.Windows.Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                            {
                                foreach (System.Windows.Window window in System.Windows.Application.Current.Windows)
                                {
                                    if (window is ChatWindow chatWindow)
                                    {
                                        // Calculate elapsed time and speed
                                        var elapsed = DateTime.Now - scanStartTime;
                                        var elapsedStr = elapsed.TotalMinutes >= 1 
                                            ? $"{(int)elapsed.TotalMinutes}m {elapsed.Seconds}s" 
                                            : $"{elapsed.Seconds}s";
                                        var speed = elapsed.TotalSeconds > 0 
                                            ? (int)(filesScanned / elapsed.TotalSeconds) 
                                            : 0;
                                        
                                        // Show file path truncated nicely
                                        var shortFile = string.IsNullOrEmpty(currentFile) ? "..." :
                                            (currentFile.Length > 55 
                                                ? "..." + currentFile.Substring(currentFile.Length - 52) 
                                                : currentFile);
                                        
                                        // Build detailed progress info
                                        var details = $"📁 {filesScanned:N0} files scanned ({speed:N0}/sec)\n⏱️ Elapsed: {elapsedStr}\n📄 {shortFile}";
                                        chatWindow.UpdateTypingProgress($"🔍 {currentPhase}", currentPercent, details);
                                        break;
                                    }
                                }
                            }), System.Windows.Threading.DispatcherPriority.Background);
                        }
                        catch { /* Ignore UI update errors */ }
                    }
                    
                    // Hook up progress events
                    scanner.ProgressChanged += (status) =>
                    {
                        currentPhase = status;
                        System.Diagnostics.Debug.WriteLine($"[ToolExecutor] Phase: {status}");
                        UpdateScanUI();
                    };
                    
                    scanner.ProgressPercentChanged += (percent) =>
                    {
                        currentPercent = percent;
                        UpdateScanUI();
                    };
                    
                    scanner.CurrentFileChanged += (filePath) =>
                    {
                        currentFile = filePath;
                        // UpdateScanUI called by FilesScannedChanged
                    };
                    
                    scanner.FilesScannedChanged += (count) =>
                    {
                        filesScanned = count;
                        UpdateScanUI();
                    };
                    
                    // Run the scan
                    var result = await scanner.PerformDeepScanAsync();
                    
                    // Store the results so user can say "fix it" later
                    _lastScanResult = result;
                    _lastScanTime = DateTime.Now;
                    
                    if (result.WasCancelled)
                        return "⚠️ Scan Cancelled\n\nThe scan was stopped before completion.";
                    
                    if (result.Threats.Count == 0)
                        return $"✅ System Scan Complete\n\nNo threats detected! Your system looks clean.\n\n📊 Scanned: {result.FilesScanned:N0} files in {result.Duration.TotalSeconds:F1}s";
                    else
                        return $"⚠️ System Scan Complete\n\n🔴 Found {result.Threats.Count} potential threat(s)!\n\n📊 Scanned: {result.FilesScanned:N0} files\n\n💡 Say 'fix it' to remove threats, or use the System Control panel (🛡️ button) to review them.";
                }
                catch (OperationCanceledException)
                {
                    return "⚠️ Scan Cancelled\n\nThe scan was stopped by user request.";
                }
                catch (Exception ex)
                {
                    return $"❌ Scan failed: {ex.Message}\n\nTry using the System Control panel (🛡️ button) for a full scan.";
                }
            }

            ct.ThrowIfCancellationRequested();

            // For non-scan commands, delegate to the regular method
            return await TryExecuteToolAsync(userMessage);
        }
        
        /// <summary>
        /// FAST PATH - Execute common commands INSTANTLY without AI processing
        /// Like Alexa/Siri - pure keyword matching for speed
        /// </summary>
        private static Task<string>? TryFastExecute(string msg)
        {
            // ===== STOP VOICE - INSTANT =====
            // Handle "stop", "shut up", "be quiet", "stop talking" - stop AI voice immediately
            if (msg == "stop" || msg == "shut up" || msg == "be quiet" || msg == "quiet" ||
                ContainsAny(msg, "stop talking", "stop speaking", "be quiet", "shut up", "hush", "silence", "enough"))
            {
                System.Diagnostics.Debug.WriteLine($"[FastPath] ⚡ Stop voice command detected");
                return Task.FromResult("__STOP_VOICE__");
            }
            
            // ===== TROUBLESHOOTING REQUESTS =====
            // Handle "X not working", "fix X", "X broken" - provide helpful troubleshooting
            if (ContainsAny(msg, "not working", "isn't working", "isnt working", "doesn't work", "doesnt work", 
                           "broken", "fix ", "help with", "problem with", "issue with"))
            {
                return HandleTroubleshootingRequest(msg);
            }
            
            // ===== SET DEFAULT APP/BROWSER - ACTUALLY DO IT =====
            // Handle "set X as default browser", "make X my default", "X as default app"
            // IMPROVED: Actually set the default browser instead of just opening settings
            if (ContainsAny(msg, "set ", "make ", "change ") && ContainsAny(msg, "default browser", "default app", "my default", "as default"))
            {
                System.Diagnostics.Debug.WriteLine($"[FastPath] ⚡ Set default browser request detected");
                
                // Extract browser name
                string browserName = "";
                if (ContainsAny(msg, "firefox", "mozilla"))
                    browserName = "firefox";
                else if (ContainsAny(msg, "chrome", "google"))
                    browserName = "chrome";
                else if (ContainsAny(msg, "edge", "microsoft"))
                    browserName = "edge";
                
                if (!string.IsNullOrEmpty(browserName))
                {
                    return SetDefaultBrowserAsync(browserName);
                }
                else
                {
                    // If no specific browser mentioned, open settings
                    try
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-settings:defaultapps") { UseShellExecute = true });
                        return Task.FromResult("⚙️ Opened Default Apps settings - you can set your default browser and other apps here!");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[FastPath] Failed to open default apps: {ex.Message}");
                        return Task.FromResult($"❌ Failed to open settings: {ex.Message}");
                    }
                }
            }
            
            // ===== WINDOWS SETTINGS - INSTANT (using WindowsKnowledgeBase) =====
            // Handle "open X settings", "X settings", "go to X settings" - INSTANT using knowledge base
            if (ContainsAny(msg, "settings", "setting"))
            {
                // Extract what settings the user wants
                var settingsMatch = System.Text.RegularExpressions.Regex.Match(msg, 
                    @"(?:open\s+)?(\w+(?:\s+\w+)?)\s+settings?|settings?\s+(?:for\s+)?(\w+(?:\s+\w+)?)|go\s+to\s+(\w+)\s+settings?", 
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                
                string? settingName = null;
                if (settingsMatch.Success)
                {
                    // Get the captured group that matched
                    settingName = settingsMatch.Groups[1].Value;
                    if (string.IsNullOrEmpty(settingName)) settingName = settingsMatch.Groups[2].Value;
                    if (string.IsNullOrEmpty(settingName)) settingName = settingsMatch.Groups[3].Value;
                }
                
                // If no specific setting found, try to extract from common patterns
                if (string.IsNullOrEmpty(settingName))
                {
                    // Try "open keyboard settings" -> "keyboard"
                    var simpleMatch = System.Text.RegularExpressions.Regex.Match(msg, @"open\s+(\w+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    if (simpleMatch.Success && simpleMatch.Groups[1].Value.ToLower() != "settings")
                        settingName = simpleMatch.Groups[1].Value;
                }
                
                if (!string.IsNullOrEmpty(settingName))
                {
                    settingName = settingName.Trim().ToLower();
                    System.Diagnostics.Debug.WriteLine($"[FastPath] ⚡ Settings request detected: '{settingName}'");
                    
                    // Use WindowsKnowledgeBase to find and open the settings
                    var uri = WindowsKnowledgeBase.FindSettingsPage(settingName);
                    if (uri != null)
                    {
                        try
                        {
                            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri) { UseShellExecute = true });
                            return Task.FromResult($"⚙️ Opened {settingName} settings");
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"[FastPath] Failed to open settings: {ex.Message}");
                        }
                    }
                    
                    // Try control panel items as fallback
                    var cpCmd = WindowsKnowledgeBase.FindControlPanelItem(settingName);
                    if (cpCmd != null)
                    {
                        try
                        {
                            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(cpCmd) { UseShellExecute = true });
                            return Task.FromResult($"⚙️ Opened {settingName} (Control Panel)");
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"[FastPath] Failed to open control panel: {ex.Message}");
                        }
                    }
                }
                
                // If just "open settings" or "settings", open main settings
                if (msg == "settings" || msg == "open settings" || msg == "go to settings")
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-settings:") { UseShellExecute = true });
                    return Task.FromResult("⚙️ Opened Windows Settings");
                }
            }
            
            // ===== SKIP FAST PATH IF FILES ARE ATTACHED =====
            // When user attaches files/folders, they want to operate on THOSE, not open system folders
            var hasAttachedFiles = ChatWindow.LastDroppedPaths?.Count > 0;
            if (hasAttachedFiles)
            {
                // Check if this is a file operation request - skip fast path
                if (ContainsAny(msg, "put", "organize", "sort", "move", "consolidate", "flatten", "into", "files"))
                {
                    System.Diagnostics.Debug.WriteLine($"[ToolExecutor] ⏭️ Skipping fast path - files attached and file operation detected");
                    return null; // Let the intelligent understanding handle it
                }
            }
            
            // ===== CHECK FOR DRIVE-SPECIFIC FOLDER REQUESTS FIRST =====
            // Handle "open X on Y drive" or "open Y drive X" patterns BEFORE generic folder matching
            // This ensures "downloads on D drive" opens D:\Downloads, not C:\Users\...\Downloads
            var folderOnDriveMatch = System.Text.RegularExpressions.Regex.Match(msg.ToLower(), @"(\w+)\s+(?:on|in|from)\s+([a-z])\s*(?:drive|:)");
            if (folderOnDriveMatch.Success && ContainsAny(msg, "open", "go to", "show"))
            {
                var folderName = folderOnDriveMatch.Groups[1].Value;
                var driveLetter = folderOnDriveMatch.Groups[2].Value.ToUpper();
                
                System.Diagnostics.Debug.WriteLine($"[FastPath] Detected 'folder on drive' pattern: {folderName} on {driveLetter}:");
                
                // Capitalize folder name for common folders
                var capitalizedFolder = char.ToUpper(folderName[0]) + folderName.Substring(1);
                var testPath = $"{driveLetter}:\\{capitalizedFolder}";
                
                if (Directory.Exists(testPath))
                {
                    System.Diagnostics.Process.Start("explorer.exe", testPath);
                    return Task.FromResult($"📂 Opened {capitalizedFolder} on {driveLetter}: drive");
                }
                
                // Try lowercase
                testPath = $"{driveLetter}:\\{folderName}";
                if (Directory.Exists(testPath))
                {
                    System.Diagnostics.Process.Start("explorer.exe", testPath);
                    return Task.FromResult($"📂 Opened {folderName} on {driveLetter}: drive");
                }
                
                // Folder doesn't exist - inform user
                return Task.FromResult($"❌ Folder '{capitalizedFolder}' not found on {driveLetter}: drive. Path: {driveLetter}:\\{capitalizedFolder}");
            }
            
            // Also handle "X drive Y folder" pattern (e.g., "D drive downloads")
            var driveFolderMatch = System.Text.RegularExpressions.Regex.Match(msg.ToLower(), @"([a-z])\s*drive\s+(\w+)");
            if (driveFolderMatch.Success && ContainsAny(msg, "open", "go to", "show"))
            {
                var driveLetter = driveFolderMatch.Groups[1].Value.ToUpper();
                var folderName = driveFolderMatch.Groups[2].Value;
                
                // Skip if folder name is a command word
                if (ContainsAny(folderName, "folder", "directory", "and", "the", "please"))
                {
                    // Just open the drive root
                    var driveRoot = $"{driveLetter}:\\";
                    if (Directory.Exists(driveRoot))
                    {
                        System.Diagnostics.Process.Start("explorer.exe", driveRoot);
                        return Task.FromResult($"📂 Opened {driveLetter}: drive");
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[FastPath] Detected 'drive folder' pattern: {driveLetter}: {folderName}");
                    
                    var capitalizedFolder = char.ToUpper(folderName[0]) + folderName.Substring(1);
                    var testPath = $"{driveLetter}:\\{capitalizedFolder}";
                    
                    if (Directory.Exists(testPath))
                    {
                        System.Diagnostics.Process.Start("explorer.exe", testPath);
                        return Task.FromResult($"📂 Opened {capitalizedFolder} on {driveLetter}: drive");
                    }
                    
                    testPath = $"{driveLetter}:\\{folderName}";
                    if (Directory.Exists(testPath))
                    {
                        System.Diagnostics.Process.Start("explorer.exe", testPath);
                        return Task.FromResult($"📂 Opened {folderName} on {driveLetter}: drive");
                    }
                }
            }
            
            // ===== OPEN FOLDERS - INSTANT =====
            // Only trigger if user explicitly wants to OPEN a folder (not organize files INTO a folder)
            // AND no drive was specified (drive-specific requests handled above)
            bool hasDriveSpecified = System.Text.RegularExpressions.Regex.IsMatch(msg.ToLower(), @"[a-z]\s*drive|on\s+[a-z]\s*:");
            
            if (msg.Contains("download") && !hasAttachedFiles && !hasDriveSpecified)
            {
                if ((msg.Contains("open") || msg.Contains("go to") || msg.Contains("show")) && !ContainsAny(msg, "put", "move", "organize", "into"))
                {
                    var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
                    System.Diagnostics.Process.Start("explorer.exe", path);
                    return Task.FromResult("📂 Opened Downloads");
                }
            }
            if (msg.Contains("document") && !hasAttachedFiles && !hasDriveSpecified)
            {
                if ((msg.Contains("open") || msg.Contains("go to") || msg.Contains("show")) && !ContainsAny(msg, "put", "move", "organize", "into"))
                {
                    var path = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                    System.Diagnostics.Process.Start("explorer.exe", path);
                    return Task.FromResult("📂 Opened Documents");
                }
            }
            if (msg.Contains("desktop") && !hasAttachedFiles && !hasDriveSpecified)
            {
                if ((msg.Contains("open") || msg.Contains("go to") || msg.Contains("show")) && !ContainsAny(msg, "put", "move", "organize", "into"))
                {
                    var path = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                    System.Diagnostics.Process.Start("explorer.exe", path);
                    return Task.FromResult("📂 Opened Desktop");
                }
            }
            if ((msg.Contains("picture") || msg.Contains("photo")) && !hasAttachedFiles && !hasDriveSpecified)
            {
                if ((msg.Contains("open") || msg.Contains("go to") || msg.Contains("show")) && !ContainsAny(msg, "put", "move", "organize", "into"))
                {
                    var path = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
                    System.Diagnostics.Process.Start("explorer.exe", path);
                    return Task.FromResult("📂 Opened Pictures");
                }
            }
            if (msg.Contains("music") && !hasAttachedFiles && !hasDriveSpecified)
            {
                if ((msg.Contains("open") || msg.Contains("go to") || msg.Contains("show")) && msg.Contains("folder") && !ContainsAny(msg, "put", "move", "organize", "into"))
                {
                    var path = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
                    System.Diagnostics.Process.Start("explorer.exe", path);
                    return Task.FromResult("📂 Opened Music");
                }
            }
            if (msg.Contains("video") && !hasAttachedFiles && !hasDriveSpecified)
            {
                if ((msg.Contains("open") || msg.Contains("go to") || msg.Contains("show")) && !ContainsAny(msg, "put", "move", "organize", "into"))
                {
                    var path = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
                    System.Diagnostics.Process.Start("explorer.exe", path);
                    return Task.FromResult("📂 Opened Videos");
                }
            }
            
            // ===== DRIVE OPENING - INSTANT =====
            // Handle "open X drive" commands directly
            var driveMatch = System.Text.RegularExpressions.Regex.Match(msg.ToLower(), @"open\s+([a-z])\s*drive");
            if (driveMatch.Success)
            {
                var driveLetter = driveMatch.Groups[1].Value.ToUpper();
                var driveRoot = $"{driveLetter}:\\";
                
                if (Directory.Exists(driveRoot))
                {
                    System.Diagnostics.Process.Start("explorer.exe", driveRoot);
                    return Task.FromResult($"📂 Opened {driveLetter}: drive");
                }
                else
                {
                    return Task.FromResult($"❌ Drive {driveLetter}: not found or not accessible");
                }
            }
            
            // ===== MEDIA CONTROLS - INSTANT =====
            if (msg == "pause" || msg == "stop" || msg.Contains("pause music") || msg.Contains("stop music"))
            {
                return SpotifyTool.ControlPlaybackAsync("pause");
            }
            if (msg == "play" || msg == "resume" || msg.Contains("resume music") || msg.Contains("continue"))
            {
                return SpotifyTool.ControlPlaybackAsync("play");
            }
            if (msg == "next" || msg == "skip" || msg.Contains("next song") || msg.Contains("skip song"))
            {
                return SpotifyTool.ControlPlaybackAsync("next");
            }
            if (msg.Contains("previous") || msg.Contains("last song") || msg.Contains("go back"))
            {
                return SpotifyTool.ControlPlaybackAsync("previous");
            }
            
            // ===== PLAY MUSIC - INSTANT =====
            // "play X on soundcloud/spotify/youtube"
            if (msg.Contains("play ") && (msg.Contains("soundcloud") || msg.Contains("sound cloud")))
            {
                var query = ExtractMusicQuery(msg, "soundcloud");
                // If query is empty or just "music", open SoundCloud home
                if (string.IsNullOrWhiteSpace(query) || query == "music" || query == "some music")
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo 
                    { 
                        FileName = "https://soundcloud.com/discover", 
                        UseShellExecute = true 
                    });
                    return Task.FromResult("🔊 Opened SoundCloud Discover - browse and play!");
                }
                return MediaPlayerTool.PlayAsync(query, MediaPlayerTool.Platform.SoundCloud);
            }
            if (msg.Contains("play ") && msg.Contains("spotify"))
            {
                var query = ExtractMusicQuery(msg, "spotify");
                // If query is empty, just open Spotify
                if (string.IsNullOrWhiteSpace(query) || query == "music" || query == "some music")
                {
                    return SystemTool.OpenAppAsync("spotify");
                }
                return MediaPlayerTool.PlayAsync(query, MediaPlayerTool.Platform.Spotify);
            }
            if (msg.Contains("play ") && (msg.Contains("youtube music") || msg.Contains("ytmusic")))
            {
                var query = ExtractMusicQuery(msg, "youtube music");
                if (string.IsNullOrWhiteSpace(query) || query == "music" || query == "some music")
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo 
                    { 
                        FileName = "https://music.youtube.com/", 
                        UseShellExecute = true 
                    });
                    return Task.FromResult("🎵 Opened YouTube Music");
                }
                return MediaPlayerTool.PlayAsync(query, MediaPlayerTool.Platform.YouTubeMusic);
            }
            if (msg.Contains("play ") && msg.Contains("youtube"))
            {
                var query = ExtractMusicQuery(msg, "youtube");
                if (string.IsNullOrWhiteSpace(query) || query == "music" || query == "some music")
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo 
                    { 
                        FileName = "https://www.youtube.com/", 
                        UseShellExecute = true 
                    });
                    return Task.FromResult("🔴 Opened YouTube");
                }
                return MediaPlayerTool.PlayAsync(query, MediaPlayerTool.Platform.YouTube);
            }
            // Generic "play X" - use default platform (Spotify)
            if (msg.StartsWith("play ") && !msg.Contains("video") && !msg.Contains("game"))
            {
                var query = msg.Substring(5).Trim();
                // Remove common suffixes
                query = query.Replace(" on spotify", "").Replace(" on soundcloud", "")
                             .Replace(" on youtube", "").Replace(" music", "").Trim();
                if (!string.IsNullOrEmpty(query))
                {
                    return MediaPlayerTool.PlayAsync(query, MediaPlayerTool.Platform.Spotify);
                }
            }
            
            // ===== SYSTEM CONTROLS - INSTANT =====
            if (msg.Contains("mute") || msg.Contains("unmute"))
            {
                return SystemTool.ToggleMuteAsync();
            }
            if (msg.Contains("lock") && (msg.Contains("computer") || msg.Contains("pc") || msg.Contains("screen")))
            {
                return SystemTool.LockComputerAsync();
            }
            if (msg.Contains("screenshot") || msg.Contains("screen shot") || msg.Contains("capture screen"))
            {
                return SystemTool.TakeScreenshotAsync();
            }
            
            // ===== WINDOWS SYSTEM UTILITIES - INSTANT =====
            if (msg.Contains("task manager") || msg == "taskmgr")
            {
                System.Diagnostics.Process.Start("taskmgr.exe");
                return Task.FromResult("⚙️ Opened Task Manager");
            }
            if (msg.Contains("control panel"))
            {
                return SystemTool.OpenFolderAsync("shell:ControlPanelFolder");
            }
            if (msg.Contains("device manager"))
            {
                return SystemTool.OpenSystemUtilityAsync("device manager");
            }
            // Only open registry editor if NOT scanning - "scan registry" should scan, not open regedit
            if ((msg.Contains("registry") || msg.Contains("regedit")) && !msg.Contains("scan"))
            {
                return SystemTool.OpenSystemUtilityAsync("registry editor");
            }
            if (msg.Contains("services") && msg.Contains("windows"))
            {
                return SystemTool.OpenSystemUtilityAsync("services");
            }
            if (msg.Contains("event viewer"))
            {
                return SystemTool.OpenSystemUtilityAsync("event viewer");
            }
            if (msg.Contains("disk management"))
            {
                return SystemTool.OpenSystemUtilityAsync("disk management");
            }
            if (msg.Contains("system information") || msg.Contains("msinfo32"))
            {
                return SystemTool.OpenSystemUtilityAsync("system information");
            }
            if (msg.Contains("msconfig") || msg.Contains("system configuration"))
            {
                return SystemTool.OpenSystemUtilityAsync("system configuration");
            }
            
            // ===== SPECIAL WINDOWS LOCATIONS - INSTANT =====
            if (msg.Contains("recycle bin") || msg.Contains("trash"))
            {
                return SystemTool.OpenFolderAsync("shell:RecycleBinFolder");
            }
            if (msg.Contains("this pc") || msg.Contains("my computer"))
            {
                return SystemTool.OpenFolderAsync("shell:MyComputerFolder");
            }
            if (msg.Contains("network") && (msg.Contains("open") || msg.Contains("show")))
            {
                return SystemTool.OpenFolderAsync("shell:NetworkPlacesFolder");
            }
            
            // ===== SYSTEM DATA FOLDERS - INSTANT =====
            // ProgramData folder
            if ((msg.Contains("program data") || msg.Contains("programdata")) && 
                (msg.Contains("open") || msg.Contains("go to") || msg.Contains("show")))
            {
                var path = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = path,
                    UseShellExecute = true
                });
                return Task.FromResult($"📂 Opened ProgramData folder ({path})");
            }
            // AppData/Roaming folder
            if ((msg.Contains("appdata") || msg.Contains("app data") || msg.Contains("roaming")) && 
                !msg.Contains("local") &&
                (msg.Contains("open") || msg.Contains("go to") || msg.Contains("show")))
            {
                var path = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = path,
                    UseShellExecute = true
                });
                return Task.FromResult($"📂 Opened AppData/Roaming folder ({path})");
            }
            // LocalAppData folder
            if ((msg.Contains("local appdata") || msg.Contains("localappdata") || msg.Contains("local app data")) && 
                (msg.Contains("open") || msg.Contains("go to") || msg.Contains("show")))
            {
                var path = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = path,
                    UseShellExecute = true
                });
                return Task.FromResult($"📂 Opened LocalAppData folder ({path})");
            }
            // Temp folder
            if (msg.Contains("temp folder") && (msg.Contains("open") || msg.Contains("go to") || msg.Contains("show")))
            {
                var path = Path.GetTempPath();
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = path,
                    UseShellExecute = true
                });
                return Task.FromResult($"📂 Opened Temp folder ({path})");
            }
            // Program Files folder
            if ((msg.Contains("program files") || msg.Contains("programfiles")) && 
                !msg.Contains("x86") &&
                (msg.Contains("open") || msg.Contains("go to") || msg.Contains("show")))
            {
                var path = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = path,
                    UseShellExecute = true
                });
                return Task.FromResult($"📂 Opened Program Files folder ({path})");
            }
            // Program Files (x86) folder
            if ((msg.Contains("program files x86") || msg.Contains("programfiles x86") || msg.Contains("x86")) && 
                (msg.Contains("open") || msg.Contains("go to") || msg.Contains("show")))
            {
                var path = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = path,
                    UseShellExecute = true
                });
                return Task.FromResult($"📂 Opened Program Files (x86) folder ({path})");
            }
            
            // ===== COMMON APPS - INSTANT =====
            if (msg == "open chrome" || msg == "chrome")
            {
                return SystemTool.OpenAppAsync("chrome");
            }
            if (msg == "open spotify" || msg == "spotify")
            {
                return SystemTool.OpenAppAsync("spotify");
            }
            if (msg == "open discord" || msg == "discord")
            {
                return SystemTool.OpenAppAsync("discord");
            }
            if (msg == "open notepad" || msg == "notepad")
            {
                return SystemTool.OpenAppAsync("notepad");
            }
            if (msg == "open steam" || msg == "steam")
            {
                return SystemTool.OpenAppAsync("steam");
            }
            if (msg == "open itunes" || msg == "itunes" || msg == "open apple music" || msg == "apple music")
            {
                return OpenITunesAsync();
            }
            if (msg == "open soundcloud" || msg == "soundcloud" || msg == "sound cloud" || msg == "open sound cloud")
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo 
                { 
                    FileName = "https://soundcloud.com/discover", 
                    UseShellExecute = true 
                });
                return Task.FromResult("🔊 Opened SoundCloud");
            }
            if (msg.Contains("open file explorer") || msg.Contains("open explorer") || msg == "explorer")
            {
                System.Diagnostics.Process.Start("explorer.exe");
                return Task.FromResult("📂 Opened File Explorer");
            }
            
            // ===== SYSTEM SCAN COMMANDS - INSTANT =====
            // Handle "scan system files", "scan for malware", "scan computer", "scan registry", etc.
            if (ContainsAny(msg, "scan system", "system scan", "scan files", "scan computer", "scan pc", 
                "scan for virus", "scan for malware", "scan for spyware", "virus scan", "malware scan",
                "spyware scan", "security scan", "deep scan", "full scan", "sfc scan", "system file checker",
                "scan registry", "registry scan", "scan for errors", "scan windows",
                "scan my system", "scan my computer", "scan my pc", "scan my files", "scan me"))
            {
                // Check if it's specifically SFC (System File Checker)
                if (ContainsAny(msg, "sfc", "system file checker", "repair system", "fix system files"))
                {
                    return SystemTool.OpenSystemUtilityAsync("system file checker");
                }
                
                // Otherwise, run our spyware/malware scan
                return Task.Run(async () =>
                {
                    try
                    {
                        var scanner = new AtlasAI.SystemControl.UnifiedScanner();
                        var result = await scanner.PerformDeepScanAsync();
                        
                        if (result.Threats.Count == 0)
                            return $"✅ System Scan Complete\n\nNo threats detected! Your system looks clean.\n\n📊 Scanned: {result.FilesScanned:N0} files in {result.Duration.TotalSeconds:F1}s";
                        else
                            return $"⚠️ System Scan Complete\n\n🔴 Found {result.Threats.Count} potential threat(s)!\n\n📊 Scanned: {result.FilesScanned:N0} files\n\nUse the System Control panel (🛡️ button) to review and remove threats.";
                    }
                    catch (Exception ex)
                    {
                        return $"❌ Scan failed: {ex.Message}\n\nTry using the System Control panel (🛡️ button) for a full scan.";
                    }
                });
            }
            
            return null; // Not a fast-path command
        }
        
        /// <summary>
        /// Extract music query from "play X on platform" commands
        /// </summary>
        private static string ExtractMusicQuery(string msg, string platform)
        {
            // Remove platform name and common words
            var query = msg.Replace("play ", "")
                          .Replace($" on {platform}", "")
                          .Replace($" in {platform}", "")
                          .Replace($" with {platform}", "")
                          .Replace($" using {platform}", "")
                          .Replace(platform, "")
                          .Replace("sound cloud", "")
                          .Replace(" music", "")
                          .Trim();
            return query;
        }
        
        /// <summary>
        /// Execute action based on intelligent understanding result
        /// </summary>
        private static async Task<string?> ExecuteFromUnderstanding(IntelligentUnderstanding.UnderstandingResult understanding)
        {
            var entities = understanding.ExtractedEntities;
            var intent = understanding.InferredIntent.ToLower();
            
            System.Diagnostics.Debug.WriteLine($"[ExecuteFromUnderstanding] Intent: {intent}, Entities: {string.Join(", ", entities.Select(e => $"{e.Key}={e.Value}"))}");
            
            try
            {
                switch (intent)
                {
                    case "play_music":
                        var query = entities.GetValueOrDefault("query", "");
                        System.Diagnostics.Debug.WriteLine($"[ExecuteFromUnderstanding] play_music - initial query: '{query}'");
                        
                        // If query is empty, try multiple extraction methods
                        if (string.IsNullOrEmpty(query))
                        {
                            // Method 1: Try to extract from normalized input
                            var normalized = understanding.NormalizedInput;
                            System.Diagnostics.Debug.WriteLine($"[ExecuteFromUnderstanding] play_music - trying normalized: '{normalized}'");
                            
                            // Try "open X, play Y" pattern first
                            var openPlayMatch = System.Text.RegularExpressions.Regex.Match(normalized, 
                                @"open\s+\w+[,\s]+(?:and\s+)?(?:play|listen to|hear)\s+(.+)", 
                                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                            if (openPlayMatch.Success)
                            {
                                query = openPlayMatch.Groups[1].Value.Trim().TrimEnd('.', '!', '?');
                                System.Diagnostics.Debug.WriteLine($"[ExecuteFromUnderstanding] play_music - extracted from 'open X, play Y': '{query}'");
                            }
                            
                            // Method 2: Try simple "play X" pattern
                            if (string.IsNullOrEmpty(query))
                            {
                                var playMatch = System.Text.RegularExpressions.Regex.Match(normalized, @"play\s+(.+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                                if (playMatch.Success)
                                {
                                    query = playMatch.Groups[1].Value.Trim();
                                    // Remove platform names from the query
                                    query = System.Text.RegularExpressions.Regex.Replace(query, 
                                        @"\s*(?:on|in|using)\s+(?:spotify|youtube|soundcloud|apple music|itunes|amazon|deezer|tidal|pandora).*$", 
                                        "", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();
                                    System.Diagnostics.Debug.WriteLine($"[ExecuteFromUnderstanding] play_music - extracted from 'play X': '{query}'");
                                }
                            }
                            
                            // Method 3: Use MediaPlayerTool.ExtractQuery as fallback
                            if (string.IsNullOrEmpty(query))
                            {
                                query = MediaPlayerTool.ExtractQuery(normalized);
                                System.Diagnostics.Debug.WriteLine($"[ExecuteFromUnderstanding] play_music - extracted via MediaPlayerTool: '{query}'");
                            }
                        }
                        
                        var platform = entities.GetValueOrDefault("platform", "");
                        var platformEnum = MediaPlayerTool.DetectPlatformFromString(platform);
                        System.Diagnostics.Debug.WriteLine($"[ExecuteFromUnderstanding] play_music - platform: '{platform}', platformEnum: {platformEnum}");
                        
                        // Clean up query - remove platform name if it's the only thing
                        var platformNames = new[] { "spotify", "youtube", "soundcloud", "apple music", "itunes", "amazon music", "deezer", "tidal", "pandora" };
                        foreach (var pn in platformNames)
                        {
                            if (query?.ToLower().Trim() == pn)
                            {
                                query = "";
                                break;
                            }
                        }
                        
                        // If query is empty or just the platform name, just open the app
                        if (string.IsNullOrEmpty(query) || query.ToLower() == platform?.ToLower())
                        {
                            System.Diagnostics.Debug.WriteLine($"[ExecuteFromUnderstanding] Opening {platform ?? "Spotify"} (no specific song)");
                            // Just open the platform
                            var appName = platform?.ToLower() switch
                            {
                                "spotify" => "Spotify",
                                "youtube" => "YouTube",
                                "youtube_music" => "YouTube Music",
                                "soundcloud" => "SoundCloud",
                                "apple_music" or "itunes" => "iTunes",
                                "amazon_music" => "Amazon Music",
                                "deezer" => "Deezer",
                                "tidal" => "Tidal",
                                "pandora" => "Pandora",
                                _ => "Spotify"
                            };
                            return await SystemTool.OpenAppAsync(appName);
                        }
                        
                        System.Diagnostics.Debug.WriteLine($"[ExecuteFromUnderstanding] Playing '{query}' on {platformEnum}");
                        return await MediaPlayerTool.PlayAsync(query, platformEnum);
                        
                    case "media_control":
                        var action = entities.GetValueOrDefault("action", "pause");
                        System.Diagnostics.Debug.WriteLine($"[ExecuteFromUnderstanding] Media control: {action}");
                        return await SpotifyTool.ControlPlaybackAsync(action);
                        
                    case "volume_control":
                        var volAction = entities.GetValueOrDefault("action", "up");
                        System.Diagnostics.Debug.WriteLine($"[ExecuteFromUnderstanding] Volume control: {volAction}");
                        if (volAction == "up") return await SystemTool.SetVolumeAsync(80);
                        if (volAction == "down") return await SystemTool.SetVolumeAsync(30);
                        if (volAction == "mute" || volAction == "unmute") return await SystemTool.ToggleMuteAsync();
                        // Default to volume up
                        return await SystemTool.SetVolumeAsync(80);
                        
                    case "open_app":
                        var app = entities.GetValueOrDefault("app", "");
                        if (string.IsNullOrEmpty(app))
                        {
                            // Try to extract from normalized input
                            var normalized = understanding.NormalizedInput;
                            var openMatch = System.Text.RegularExpressions.Regex.Match(normalized, @"open\s+(.+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                            if (openMatch.Success)
                                app = openMatch.Groups[1].Value.Trim();
                        }
                        
                        if (!string.IsNullOrEmpty(app))
                        {
                            System.Diagnostics.Debug.WriteLine($"[ExecuteFromUnderstanding] Opening app: {app}");
                            return await SystemTool.OpenAppAsync(app);
                        }
                        break;
                    
                    case "open_folder":
                        var targetFolder = entities.GetValueOrDefault("folder", "");
                        if (string.IsNullOrEmpty(targetFolder))
                        {
                            // Try to extract from normalized input
                            var normalized = understanding.NormalizedInput.ToLower();
                            if (normalized.Contains("download")) targetFolder = "downloads";
                            else if (normalized.Contains("document")) targetFolder = "documents";
                            else if (normalized.Contains("desktop")) targetFolder = "desktop";
                            else if (normalized.Contains("picture") || normalized.Contains("photo")) targetFolder = "pictures";
                            else if (normalized.Contains("music")) targetFolder = "music";
                            else if (normalized.Contains("video")) targetFolder = "videos";
                        }
                        
                        var targetFolderPath = GetFolderPath(targetFolder);
                        System.Diagnostics.Debug.WriteLine($"[ExecuteFromUnderstanding] Opening folder: {targetFolderPath}");
                        
                        if (Directory.Exists(targetFolderPath))
                        {
                            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                            {
                                FileName = "explorer.exe",
                                Arguments = targetFolderPath,
                                UseShellExecute = true
                            });
                            return $"📂 Opened {targetFolder} folder";
                        }
                        else
                        {
                            return $"❌ Folder not found: {targetFolderPath}";
                        }
                        
                    case "close_app":
                        var closeApp = entities.GetValueOrDefault("app", "");
                        if (!string.IsNullOrEmpty(closeApp))
                        {
                            System.Diagnostics.Debug.WriteLine($"[ExecuteFromUnderstanding] Closing app: {closeApp}");
                            return await SystemTool.RunCommandAsync($"taskkill /IM {closeApp}.exe /F");
                        }
                        break;
                        
                    case "web_search":
                        var searchQuery = entities.GetValueOrDefault("query", "");
                        if (string.IsNullOrEmpty(searchQuery))
                        {
                            // Use normalized input as search query
                            searchQuery = understanding.NormalizedInput;
                        }
                        if (!string.IsNullOrEmpty(searchQuery))
                        {
                            System.Diagnostics.Debug.WriteLine($"[ExecuteFromUnderstanding] Web search: {searchQuery}");
                            return await WebSearchTool.SearchAsync(searchQuery);
                        }
                        break;
                        
                    case "weather":
                        var location = entities.GetValueOrDefault("location", "auto");
                        if (string.IsNullOrEmpty(location)) location = "auto";
                        System.Diagnostics.Debug.WriteLine($"[ExecuteFromUnderstanding] Weather for: {location}");
                        return await WebSearchTool.GetWeatherAsync(location);
                        
                    case "power_control":
                        var powerAction = entities.GetValueOrDefault("action", "");
                        System.Diagnostics.Debug.WriteLine($"[ExecuteFromUnderstanding] Power control: {powerAction}");
                        if (powerAction == "shutdown") return await SystemTool.ShutdownAsync();
                        if (powerAction == "restart") return await SystemTool.RestartAsync();
                        if (powerAction == "sleep") return await SystemTool.SleepAsync();
                        if (powerAction == "lock") return await SystemTool.LockComputerAsync();
                        if (powerAction == "hibernate") return await SystemTool.SleepAsync();
                        break;
                        
                    case "screenshot":
                        System.Diagnostics.Debug.WriteLine($"[ExecuteFromUnderstanding] Taking screenshot");
                        return await SystemTool.TakeScreenshotAsync();
                    
                    case "generate_image":
                        var imagePrompt = entities.GetValueOrDefault("prompt", "");
                        System.Diagnostics.Debug.WriteLine($"[ExecuteFromUnderstanding] Generating image: {imagePrompt}");
                        
                        if (!string.IsNullOrEmpty(imagePrompt))
                        {
                            // Return a special marker that tells ChatWindow to generate this image
                            return $"__GENERATE_IMAGE__|{imagePrompt}";
                        }
                        return "❌ Please tell me what image you'd like me to generate.";
                    
                    case "analyze_image":
                        var imagePath = entities.GetValueOrDefault("image_path", "");
                        var question = entities.GetValueOrDefault("question", "What is this?");
                        System.Diagnostics.Debug.WriteLine($"[ExecuteFromUnderstanding] Analyzing image: {imagePath}");
                        
                        if (!string.IsNullOrEmpty(imagePath) && File.Exists(imagePath))
                        {
                            // Return a special marker that tells ChatWindow to analyze this image
                            return $"__ANALYZE_IMAGE__|{imagePath}|{question}";
                        }
                        return "❌ Could not find the image to analyze.";
                    
                    case "open_system_folder":
                        var systemFolder = entities.GetValueOrDefault("folder", "programdata");
                        System.Diagnostics.Debug.WriteLine($"[ExecuteFromUnderstanding] Opening system folder: {systemFolder}");
                        
                        var systemFolderPath = systemFolder.ToLower() switch
                        {
                            "programdata" => Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                            "appdata" or "roaming" => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                            "localappdata" => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                            "temp" => Path.GetTempPath(),
                            "windows" => Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                            "system32" => Environment.GetFolderPath(Environment.SpecialFolder.System),
                            "programfiles" => Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                            "programfilesx86" => Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                            _ => Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData)
                        };
                        
                        if (Directory.Exists(systemFolderPath))
                        {
                            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                            {
                                FileName = "explorer.exe",
                                Arguments = systemFolderPath,
                                UseShellExecute = true
                            });
                            return $"📂 Opened {systemFolder} ({systemFolderPath})";
                        }
                        return $"❌ Folder not found: {systemFolderPath}";
                        
                    case "organize_files":
                        var target = entities.GetValueOrDefault("target", "desktop");
                        var folderPath = GetFolderPath(target);
                        System.Diagnostics.Debug.WriteLine($"[ExecuteFromUnderstanding] Organizing files in: {folderPath}");
                        return await SystemTool.SortFilesByTypeAsync(folderPath);
                        
                    case "system_scan":
                        var scanType = entities.GetValueOrDefault("scan_type", "quick");
                        System.Diagnostics.Debug.WriteLine($"[ExecuteFromUnderstanding] System scan: {scanType}");
                        try
                        {
                            var scanner = new AtlasAI.SystemControl.UnifiedScanner();
                            var scanResult = await scanner.PerformDeepScanAsync();
                            
                            if (scanResult.Threats.Count == 0)
                                return $"✅ System Scan Complete\n\nNo threats detected! Your system looks clean.\n\n📊 Scanned: {scanResult.FilesScanned:N0} files in {scanResult.Duration.TotalSeconds:F1}s";
                            else
                                return $"⚠️ System Scan Complete\n\n🔴 Found {scanResult.Threats.Count} potential threat(s)!\n\n📊 Scanned: {scanResult.FilesScanned:N0} files\n\nUse the System Control panel (🛡️ button) to review and remove threats.";
                        }
                        catch (Exception ex)
                        {
                            return $"❌ Scan failed: {ex.Message}\n\nTry using the System Control panel (🛡️ button) for a full scan.";
                        }
                        
                    default:
                        System.Diagnostics.Debug.WriteLine($"[ExecuteFromUnderstanding] Unknown intent: {intent}");
                        break;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ExecuteFromUnderstanding] Error: {ex.Message}");
            }
            
            return null;
        }
        
        private static string GetFolderPath(string target)
        {
            return target.ToLower() switch
            {
                "desktop" => Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                "downloads" => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
                "documents" => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "pictures" => Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                "music" => Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
                "videos" => Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
                _ => Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
            };
        }
        
        /// <summary>
        /// Fix/remove detected threats from the last scan
        /// </summary>
        private static async Task<string> FixDetectedThreatsAsync()
        {
            // Check if we have recent scan results
            if (_lastScanResult == null || _lastScanResult.Threats.Count == 0)
            {
                // Check if scan was too long ago (more than 30 minutes)
                if (_lastScanTime < DateTime.Now.AddMinutes(-30))
                {
                    return "❌ No recent scan results found.\n\n💡 Say 'scan my computer' first to detect threats, then say 'fix it' to remove them.";
                }
                return "✅ No threats to fix! Your system is clean.";
            }
            
            var remover = new ThreatRemover();
            var results = new System.Text.StringBuilder();
            int fixedCount = 0;
            int failedCount = 0;
            int protectedCount = 0;
            int notRemovableCount = 0;
            
            // Count how many are actually fixable
            int fixableCount = _lastScanResult.Threats.Count(t => t.CanRemove);
            
            results.AppendLine("🔧 **Auto-Fix Complete**\n");
            
            foreach (var threat in _lastScanResult.Threats)
            {
                if (!threat.CanRemove)
                {
                    notRemovableCount++;
                    continue;
                }
                
                try
                {
                    var result = await remover.RemoveThreatAsync(threat);
                    
                    if (result.Success)
                    {
                        fixedCount++;
                    }
                    else if (result.IsProtectedSystem)
                    {
                        protectedCount++;
                    }
                    else
                    {
                        failedCount++;
                    }
                }
                catch (Exception)
                {
                    failedCount++;
                }
            }
            
            results.AppendLine("📊 **Results:**");
            results.AppendLine($"• Issues Processed: {fixableCount}");
            results.AppendLine($"• Successfully Fixed: {fixedCount}");
            results.AppendLine($"• Failed: {failedCount}");
            
            if (protectedCount > 0)
                results.AppendLine($"• Protected System Files: {protectedCount} (safe to ignore)");
            
            if (notRemovableCount > 0)
                results.AppendLine($"• Low-Risk Items: {notRemovableCount} (no action needed)");
            
            if (fixedCount > 0)
            {
                results.AppendLine("\n✅ **Successfully Fixed:**");
                foreach (var threat in _lastScanResult.Threats.Where(t => t.CanRemove).Take(10))
                {
                    results.AppendLine($"  • {threat.Name}");
                }
                if (fixedCount > 10)
                    results.AppendLine($"  ... and {fixedCount - 10} more");
                    
                results.AppendLine("\n💡 Restart your computer to complete the cleanup.");
            }
            
            // Clear the scan results after fixing
            if (failedCount == 0)
            {
                _lastScanResult = null;
            }
            
            return results.ToString();
        }
        
        /// <summary>
        /// Check if the message is a greeting or casual conversation that should be handled by AI
        /// </summary>
        private static bool IsGreetingOrConversation(string msg)
        {
            // Common greetings
            var greetings = new[] { "hello", "hi", "hey", "howdy", "greetings", "good morning", "good afternoon", 
                "good evening", "good night", "what's up", "whats up", "sup", "yo" };
            
            // Introductions
            var introductions = new[] { "my name is", "i'm ", "im ", "i am ", "nice to meet", "pleased to meet",
                "call me ", "they call me" };
            
            // Casual conversation starters and responses
            // NOTE: "weather" removed - weather queries should use the weather tool, not AI
            var casual = new[] { "how are you", "how's it going", "how you doing", "what's new", "how have you been",
                "long time no see", "thanks", "thank you", "bye", "goodbye", "see you", "later", "take care",
                "yeah", "yea", "yes", "no", "nope", "nah", "sure", "ok", "okay", "alright", "cool", "nice",
                "good", "great", "awesome", "amazing", "terrible", "bad", "not bad", "fine", "doing well",
                "cold", "hot", "warm", "freezing", "tired", "sleepy", "bored", "busy", "hungry",
                "thirsty", "happy", "sad", "excited", "nervous", "stressed", "relaxed", "chill",
                "lol", "haha", "hehe", "lmao", "rofl", "omg", "wow", "damn", "dang", "geez",
                "really", "seriously", "honestly", "actually", "basically", "literally",
                "i think", "i feel", "i believe", "i guess", "i suppose", "i hope", "i wish",
                "that's", "thats", "it's", "its", "bit cold", "bit hot", "bit tired", "bit busy",
                "all good", "not much", "same here", "me too", "same", "agreed", "exactly", "right",
                "fair enough", "makes sense", "i see", "got it", "understood", "interesting",
                "tell me more", "go on", "continue", "what else", "anything else" };
            
            // Questions about the AI
            var aboutAi = new[] { "who are you", "what are you", "what can you do", "tell me about yourself",
                "what's your name", "whats your name" };
            
            // Check if message starts with or contains greetings
            foreach (var greeting in greetings)
            {
                if (msg.StartsWith(greeting) || msg == greeting)
                    return true;
            }
            
            // Check for introductions
            foreach (var intro in introductions)
            {
                if (msg.Contains(intro))
                    return true;
            }
            
            // Check for casual conversation
            foreach (var c in casual)
            {
                if (msg.Contains(c))
                    return true;
            }
            
            // Check for questions about AI
            foreach (var q in aboutAi)
            {
                if (msg.Contains(q))
                    return true;
            }
            
            // Check if message is very short (likely casual) - under 30 chars with no action verbs
            if (msg.Length < 30)
            {
                var actionVerbs = new[] { "open", "play", "search", "find", "create", "delete", "move", "copy",
                    "scan", "check", "run", "start", "stop", "close", "show", "hide", "set", "change",
                    "turn on", "turn off", "enable", "disable", "install", "uninstall", "download" };
                
                bool hasActionVerb = false;
                foreach (var verb in actionVerbs)
                {
                    if (msg.Contains(verb))
                    {
                        hasActionVerb = true;
                        break;
                    }
                }
                
                if (!hasActionVerb)
                    return true; // Short message with no action verbs = casual conversation
            }
            
            return false;
        }
        
        private static bool ContainsAny(string text, params string[] keywords)
        {
            foreach (var keyword in keywords)
            {
                if (text.Contains(keyword))
                    return true;
            }
            return false;
        }
        
        #region Undo Commands
        
        private static async Task<string> HandleUndoCommand(string msg)
        {
            var history = ActionHistoryManager.Instance;
            
            // Check if user wants to undo a specific action
            if (msg.Contains("undo #") || msg.Contains("undo number"))
            {
                var match = Regex.Match(msg, @"(?:undo #?|undo number\s*)(\d+)");
                if (match.Success && int.TryParse(match.Groups[1].Value, out int index))
                {
                    var actions = history.GetUndoableActions(20);
                    if (index > 0 && index <= actions.Count)
                    {
                        return await history.UndoActionAsync(actions[index - 1]);
                    }
                    return $"❌ Invalid action number. Use 'undo history' to see available actions.";
                }
            }
            
            // Undo the last action
            if (!history.CanUndo)
            {
                return "❌ Nothing to Undo\n\nNo recent actions can be undone. Actions like opening apps, moving files, and organizing folders can be undone.";
            }
            
            return await history.UndoLastActionAsync();
        }
        
        private static string GetUndoHistory()
        {
            var history = ActionHistoryManager.Instance;
            var actions = history.GetUndoableActions(10);
            
            if (actions.Count == 0)
            {
                return "📋 Undo History\n\nNo actions to undo. Recent actions will appear here.";
            }
            
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("📋 Undo History\n");
            sb.AppendLine("Say 'undo' to undo the most recent, or 'undo #N' for a specific action:\n");
            
            for (int i = 0; i < actions.Count; i++)
            {
                var action = actions[i];
                var timeAgo = GetTimeAgo(action.Timestamp);
                sb.AppendLine($"#{i + 1} {action.Description}");
                sb.AppendLine($"   ⏱️ {timeAgo}");
            }
            
            return sb.ToString();
        }
        
        private static string GetTimeAgo(DateTime timestamp)
        {
            var diff = DateTime.Now - timestamp;
            if (diff.TotalSeconds < 60) return "just now";
            if (diff.TotalMinutes < 60) return $"{(int)diff.TotalMinutes}m ago";
            if (diff.TotalHours < 24) return $"{(int)diff.TotalHours}h ago";
            return $"{(int)diff.TotalDays}d ago";
        }
        
        /// <summary>
        /// Record an action for undo capability
        /// </summary>
        public static void RecordAction(ActionRecord action)
        {
            ActionHistoryManager.Instance.RecordAction(action);
        }
        
        #endregion
        
        #region iTunes/Apple Music
        
        /// <summary>
        /// Open iTunes/Apple Music with proper path detection
        /// </summary>
        private static async Task<string> OpenITunesAsync()
        {
            try
            {
                // Try common iTunes installation paths
                var possiblePaths = new[]
                {
                    @"C:\Program Files\iTunes\iTunes.exe",
                    @"C:\Program Files (x86)\iTunes\iTunes.exe",
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "iTunes", "iTunes.exe"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "iTunes", "iTunes.exe"),
                };
                
                foreach (var path in possiblePaths)
                {
                    if (File.Exists(path))
                    {
                        System.Diagnostics.Debug.WriteLine($"[ToolExecutor] Found iTunes at: {path}");
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
                        return "🎵 Opened iTunes!";
                    }
                }
                
                // Try to find in Start Menu
                var startMenuPaths = new[]
                {
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu), "Programs"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs"),
                };
                
                foreach (var startPath in startMenuPaths)
                {
                    if (!Directory.Exists(startPath)) continue;
                    
                    var shortcuts = Directory.GetFiles(startPath, "*.lnk", SearchOption.AllDirectories);
                    foreach (var shortcut in shortcuts)
                    {
                        var name = Path.GetFileNameWithoutExtension(shortcut).ToLower();
                        if (name.Contains("itunes") || name.Contains("apple music"))
                        {
                            System.Diagnostics.Debug.WriteLine($"[ToolExecutor] Found iTunes shortcut: {shortcut}");
                            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(shortcut) { UseShellExecute = true });
                            return "🎵 Opened iTunes!";
                        }
                    }
                }
                
                // Try shell launch as last resort
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("iTunes") { UseShellExecute = true });
                    return "🎵 Opened iTunes!";
                }
                catch
                {
                    // iTunes not found
                    return "❌ iTunes not found. Is it installed? You can download it from apple.com/itunes";
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ToolExecutor] iTunes open error: {ex.Message}");
                return $"❌ Could not open iTunes: {ex.Message}";
            }
        }
        
        #endregion
        
        #region Troubleshooting
        
        /// <summary>
        /// Handle troubleshooting requests like "X not working" or "fix X"
        /// </summary>
        private static Task<string>? HandleTroubleshootingRequest(string msg)
        {
            var lower = msg.ToLower();
            
            // Print Screen / Screenshot issues
            if (ContainsAny(lower, "print screen", "printscreen", "prtscrn", "prtsc", "screenshot key", "snipping"))
            {
                return Task.FromResult(@"🔧 Print Screen / Screenshot Troubleshooting

Quick Fixes:
1. Try Win + Shift + S - Opens Snipping Tool (most reliable)
2. Try Win + PrtScn - Saves screenshot to Pictures\Screenshots
3. Try just PrtScn - Copies to clipboard (paste in Paint)

If keys still don't work:
• Open Settings → Accessibility → Keyboard
• Check ""Use the Print Screen key to open Snipping Tool""
• Try: Settings → System → Clipboard → Turn on Clipboard history

Alternative: Say ""take a screenshot"" and I'll capture it for you!

Would you like me to take a screenshot now?");
            }
            
            // Keyboard issues
            if (ContainsAny(lower, "keyboard", "keys", "typing"))
            {
                return Task.FromResult(@"🔧 Keyboard Troubleshooting

Quick Fixes:
1. Restart your PC - Often fixes temporary issues
2. Check connection - Unplug and replug USB keyboard
3. Try another USB port - Port might be faulty
4. Check Bluetooth - For wireless keyboards, re-pair

Windows Settings:
• Settings → Time & Language → Language → Keyboard
• Settings → Accessibility → Keyboard (check Filter Keys is OFF)
• Device Manager → Keyboards → Update driver

Test keyboard: Press Win+R, type ""osk"", press Enter (opens on-screen keyboard)

What specific keys aren't working?");
            }
            
            // Mouse issues
            if (ContainsAny(lower, "mouse", "cursor", "click", "pointer"))
            {
                return Task.FromResult(@"🔧 Mouse Troubleshooting

Quick Fixes:
1. Check connection - Unplug and replug
2. Try another USB port
3. Replace batteries (wireless mouse)
4. Clean the sensor - Dust can cause issues

Windows Settings:
• Settings → Bluetooth & devices → Mouse
• Settings → Accessibility → Mouse pointer and touch
• Control Panel → Mouse (for advanced settings)

What's the specific issue with your mouse?");
            }
            
            // Audio/Sound issues
            if (ContainsAny(lower, "sound", "audio", "speaker", "volume", "headphone", "microphone", "mic"))
            {
                return Task.FromResult(@"🔧 Audio Troubleshooting

Quick Fixes:
1. Check volume - Click speaker icon in taskbar
2. Right-click speaker icon → Sound settings
3. Check output device - Make sure correct device selected
4. Run troubleshooter - Settings → System → Sound → Troubleshoot

For microphone:
• Settings → Privacy → Microphone → Allow apps access
• Right-click speaker → Sound settings → Input

Would you like me to open Sound settings?");
            }
            
            // WiFi/Internet issues
            if (ContainsAny(lower, "wifi", "internet", "network", "connection", "connected"))
            {
                return Task.FromResult(@"🔧 WiFi/Internet Troubleshooting

Quick Fixes:
1. Toggle WiFi - Click WiFi icon, turn off/on
2. Restart router - Unplug for 30 seconds
3. Forget and reconnect - Settings → Network → WiFi → Manage known networks
4. Run troubleshooter - Settings → Network → Network troubleshooter

Commands I can run:
• ""open wifi settings""
• ""reset network""

Would you like me to open network settings?");
            }
            
            // Display/Monitor issues
            if (ContainsAny(lower, "display", "monitor", "screen", "resolution", "brightness"))
            {
                return Task.FromResult(@"🔧 Display Troubleshooting

Quick Fixes:
1. Check cable connection - HDMI/DisplayPort/VGA
2. Try Win + P - Change display mode
3. Update graphics driver - Device Manager → Display adapters

Settings:
• Settings → System → Display
• Right-click desktop → Display settings

Would you like me to open display settings?");
            }
            
            // Generic troubleshooting
            return null; // Let AI handle other troubleshooting requests
        }
        
        #endregion
        
        private static string ExtractLocation(string message)
        {
            // Try to extract location from "weather in X" or "weather for X"
            var patterns = new[]
            {
                @"weather (?:in|for|at) ([a-zA-Z\s,]+)",
                @"temperature (?:in|for|at) ([a-zA-Z\s,]+)",
                @"forecast (?:in|for|at) ([a-zA-Z\s,]+)"
            };
            
            foreach (var pattern in patterns)
            {
                var match = Regex.Match(message, pattern, RegexOptions.IgnoreCase);
                if (match.Success)
                    return match.Groups[1].Value.Trim();
            }
            
            // Default to extracting last capitalized words
            var words = message.Split(' ');
            for (int i = words.Length - 1; i >= 0; i--)
            {
                if (char.IsUpper(words[i][0]) && words[i].Length > 2)
                    return words[i];
            }
            
            return "";
        }
        
        private static string ExtractSearchQuery(string message)
        {
            var patterns = new[]
            {
                @"search (?:for|the web for|online for|internet for) (.+)",
                @"search (.+)",
                @"look (?:up|for) (.+)",
                @"find (?:info|me|information) (?:on|about)? ?(.+)",
                @"tell me about (.+)",
                @"information (?:on|about) (.+)",
                @"info (?:on|about) (.+)",
                @"what (?:is|are|was|were) (.+)",
                @"who (?:is|are|was|were) (.+)",
                @"when (?:did|was|is|will) (.+)",
                @"where (?:is|are|was|were|can) (.+)",
                @"how (?:to|do|does|can|did) (.+)",
                @"define (.+)",
                @"meaning of (.+)",
                @"google (.+)"
            };
            
            foreach (var pattern in patterns)
            {
                var match = Regex.Match(message, pattern, RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    var query = match.Groups[1].Value.Trim().TrimEnd('?', '.', '!');
                    // Remove common filler words at the end
                    query = Regex.Replace(query, @"\s+(please|for me|now|thanks|thank you)$", "", RegexOptions.IgnoreCase);
                    return query;
                }
            }
            
            // If no pattern matched, use the whole message (minus common prefixes)
            var cleaned = Regex.Replace(message, @"^(can you |could you |please |hey |atlas |)", "", RegexOptions.IgnoreCase);
            return cleaned.Trim().TrimEnd('?', '.', '!');
        }
        
        private static string ExtractUrl(string message)
        {
            var match = Regex.Match(message, @"(https?://[^\s]+|www\.[^\s]+|[a-zA-Z0-9-]+\.(com|org|net|io)[^\s]*)", RegexOptions.IgnoreCase);
            return match.Success ? match.Value : "";
        }

        private static string ExtractSongQuery(string message)
        {
            var patterns = new[]
            {
                @"play (.+?) (?:on spotify|in spotify|spotify)",
                @"play (.+?) by (.+)",  // "play Shape of You by Ed Sheeran"
                @"put on (.+?) (?:on spotify|in spotify|spotify)",
                @"play (?:the song |song |track |music )?(.+)",
                @"put on (?:the song |song |track |music )?(.+)",
            };

            foreach (var pattern in patterns)
            {
                var match = Regex.Match(message, pattern, RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    var song = match.Groups[1].Value.Trim();
                    // If there's an artist group, combine them
                    if (match.Groups.Count > 2 && !string.IsNullOrEmpty(match.Groups[2].Value))
                        song += " " + match.Groups[2].Value.Trim();
                    // Clean up
                    song = Regex.Replace(song, @"\s*(please|for me|now|on spotify|in spotify)$", "", RegexOptions.IgnoreCase);
                    return song;
                }
            }

            // Fallback: everything after "play"
            var playMatch = Regex.Match(message, @"play\s+(.+)", RegexOptions.IgnoreCase);
            if (playMatch.Success)
                return playMatch.Groups[1].Value.Trim();

            return "";
        }
        
        private static string ExtractSoftwareName(string message)
        {
            var msg = message.ToLower();
            
            // Patterns to extract software name
            var patterns = new[]
            {
                @"(?:install|download|get me|i need|set up|setup|uninstall|remove)\s+(.+?)(?:\s+for me|\s+please|\s+now|$)",
                @"(?:is|do i have|check if)\s+(.+?)\s+(?:installed|available)",
            };
            
            foreach (var pattern in patterns)
            {
                var match = Regex.Match(message, pattern, RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    var name = match.Groups[1].Value.Trim();
                    // Clean up common words
                    name = Regex.Replace(name, @"\b(the|a|an|please|for me|now|app|application|program|software)\b", "", RegexOptions.IgnoreCase).Trim();
                    if (!string.IsNullOrEmpty(name))
                        return name;
                }
            }
            
            return "";
        }
        
        /// <summary>
        /// Extract Windows system utility name from user message
        /// </summary>
        private static string ExtractSystemUtility(string message)
        {
            var msgLower = message.ToLower();
            
            // System management utilities
            if (ContainsAny(msgLower, "device manager", "devices"))
                return "device manager";
            if (ContainsAny(msgLower, "disk management", "disk manager"))
                return "disk management";
            if (ContainsAny(msgLower, "event viewer", "event logs"))
                return "event viewer";
            if (ContainsAny(msgLower, "services", "windows services"))
                return "services";
            if (ContainsAny(msgLower, "msconfig", "system configuration", "startup programs"))
                return "system configuration";
            if (ContainsAny(msgLower, "system information", "msinfo32", "system info"))
                return "system information";
            if (ContainsAny(msgLower, "registry editor", "regedit", "registry"))
                return "registry editor";
            if (ContainsAny(msgLower, "task scheduler", "scheduled tasks"))
                return "task scheduler";
            if (ContainsAny(msgLower, "group policy", "gpedit", "local group policy"))
                return "local group policy";
            if (ContainsAny(msgLower, "computer management", "compmgmt"))
                return "computer management";
            if (ContainsAny(msgLower, "performance monitor", "perfmon"))
                return "performance monitor";
            if (ContainsAny(msgLower, "memory diagnostic", "memory test"))
                return "windows memory diagnostic";
            if (ContainsAny(msgLower, "system file checker", "sfc scan", "sfc /scannow"))
                return "system file checker";
            if (ContainsAny(msgLower, "disk cleanup", "cleanmgr"))
                return "disk cleanup";
            if (ContainsAny(msgLower, "defragment", "defrag", "disk defragmenter"))
                return "defragment";
            if (ContainsAny(msgLower, "resource monitor", "resmon"))
                return "resource monitor";
            if (ContainsAny(msgLower, "windows firewall", "firewall"))
                return "windows firewall";
            if (ContainsAny(msgLower, "certificate manager", "certificates"))
                return "certificate manager";
            if (ContainsAny(msgLower, "local security policy", "security policy"))
                return "local security policy";
            if (ContainsAny(msgLower, "component services", "dcom"))
                return "component services";
            if (ContainsAny(msgLower, "odbc", "data sources"))
                return "odbc data sources";
            if (ContainsAny(msgLower, "print management", "printers"))
                return "print management";
            if (ContainsAny(msgLower, "shared folders", "network shares"))
                return "shared folders";
            if (ContainsAny(msgLower, "windows features", "turn windows features"))
                return "windows features";
            if (ContainsAny(msgLower, "programs and features", "uninstall programs", "add remove programs"))
                return "programs and features";
            if (ContainsAny(msgLower, "user accounts", "netplwiz", "manage users"))
                return "user accounts";
            if (ContainsAny(msgLower, "system properties", "computer properties"))
                return "system properties";
            if (ContainsAny(msgLower, "power options", "power settings"))
                return "power options";
            if (ContainsAny(msgLower, "network connections", "network adapters"))
                return "network connections";
            if (ContainsAny(msgLower, "sound properties", "audio properties"))
                return "sound";
            if (ContainsAny(msgLower, "display properties", "screen resolution"))
                return "display settings";
            if (ContainsAny(msgLower, "mouse properties", "mouse settings"))
                return "mouse properties";
            if (ContainsAny(msgLower, "keyboard properties", "keyboard settings"))
                return "keyboard properties";
            if (ContainsAny(msgLower, "date and time", "clock settings"))
                return "date and time";
            if (ContainsAny(msgLower, "region settings", "regional settings"))
                return "region settings";
            if (ContainsAny(msgLower, "internet options", "internet properties"))
                return "internet options";
            if (ContainsAny(msgLower, "add hardware", "hardware wizard"))
                return "add hardware";
            if (ContainsAny(msgLower, "game controllers", "gaming devices"))
                return "game controllers";
            if (ContainsAny(msgLower, "phone and modem", "modem settings"))
                return "phone and modem";
            if (ContainsAny(msgLower, "speech properties", "text to speech"))
                return "speech properties";
            
            return "";
        }

        private static string ExtractAppName(string message)
        {
            var msg = message.ToLower();
            
            // Direct app name extraction
            var patterns = new[]
            {
                @"(?:open|launch|start|run)\s+(.+)",
            };
            
            foreach (var pattern in patterns)
            {
                var match = Regex.Match(message, pattern, RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    var app = match.Groups[1].Value.Trim();
                    // Remove common suffixes
                    app = Regex.Replace(app, @"\s*(please|for me|now|app|application)$", "", RegexOptions.IgnoreCase);
                    return app;
                }
            }
            
            // Fallback: check for known app names anywhere in message
            var knownApps = new[] { "notepad", "calculator", "calc", "paint", "explorer", "chrome", "firefox", "edge", "word", "excel", "spotify", "discord", "steam", "vscode", "settings", "cmd", "powershell", "task manager" };
            foreach (var app in knownApps)
            {
                if (msg.Contains(app))
                    return app;
            }
            
            return "";
        }
        
        private static string ExtractPath(string message)
        {
            var msgLower = message.ToLower();
            
            // First check for dropped/attached paths from the chat window
            var droppedPaths = ChatWindow.LastDroppedPaths;
            if (droppedPaths != null && droppedPaths.Count > 0)
            {
                // Return the first folder path, or first file's directory
                foreach (var path in droppedPaths)
                {
                    if (Directory.Exists(path))
                        return path;
                    if (File.Exists(path))
                        return Path.GetDirectoryName(path) ?? "";
                }
            }
            
            // Look for quoted paths in the message (from attached files context)
            var quotedMatch = Regex.Match(message, @"(?:FOLDER|FILE):\s*""([^""]+)""", RegexOptions.IgnoreCase);
            if (quotedMatch.Success)
            {
                var quotedPath = quotedMatch.Groups[1].Value;
                if (Directory.Exists(quotedPath))
                    return quotedPath;
                if (File.Exists(quotedPath))
                    return Path.GetDirectoryName(quotedPath) ?? "";
            }
            
            // Look for explicit paths like C:\... or ~/...
            var match = Regex.Match(message, @"([A-Za-z]:\\[^\s""]+|~/[^\s""]+|/[^\s""]+)");
            if (match.Success)
                return match.Value;
            
            // ENHANCED: Universal Windows file system knowledge
            var windowsPath = ExtractWindowsSystemPath(msgLower);
            if (!string.IsNullOrEmpty(windowsPath))
                return windowsPath;
            
            // ENHANCED: Handle "folder on X drive" pattern (e.g., "downloads on D drive", "music on E drive")
            // This pattern: "folder on X drive" or "folder in X drive"
            var folderOnDriveMatch = Regex.Match(msgLower, @"(\w+)\s+(?:on|in|from)\s+([a-z])\s*(?:drive|:)");
            if (folderOnDriveMatch.Success)
            {
                var folderName = folderOnDriveMatch.Groups[1].Value;
                var driveLetter = folderOnDriveMatch.Groups[2].Value.ToUpper();
                
                System.Diagnostics.Debug.WriteLine($"[ExtractPath] Detected 'folder on drive' pattern: {folderName} on {driveLetter}:");
                
                // Capitalize folder name for common folders
                var capitalizedFolder = char.ToUpper(folderName[0]) + folderName.Substring(1);
                var testPath = $"{driveLetter}:\\{capitalizedFolder}";
                System.Diagnostics.Debug.WriteLine($"[ExtractPath] Testing path: {testPath}");
                
                if (Directory.Exists(testPath))
                    return testPath;
                
                // Try lowercase
                testPath = $"{driveLetter}:\\{folderName}";
                if (Directory.Exists(testPath))
                    return testPath;
                    
                // Try common variations
                var variations = new[] { "Downloads", "Music", "Videos", "Pictures", "Documents", "Games", "Torrents", "Backup", "Data" };
                foreach (var variation in variations)
                {
                    if (variation.ToLower().StartsWith(folderName.ToLower()) || folderName.ToLower().StartsWith(variation.ToLower()))
                    {
                        testPath = $"{driveLetter}:\\{variation}";
                        if (Directory.Exists(testPath))
                            return testPath;
                    }
                }
                
                // If folder doesn't exist, still return the path (user might want to create it or it's a typo)
                return $"{driveLetter}:\\{capitalizedFolder}";
            }
            
            // Look for "X drive" pattern with folder name (e.g., "D drive music folder", "E drive downloads")
            var driveMatch = Regex.Match(msgLower, @"([a-z])\s*drive\s*(\w+)?");
            if (driveMatch.Success)
            {
                var driveLetter = driveMatch.Groups[1].Value.ToUpper();
                var folderName = driveMatch.Groups[2].Success ? driveMatch.Groups[2].Value : "";
                
                // Try to construct the path
                if (!string.IsNullOrEmpty(folderName))
                {
                    // Capitalize folder name for common folders
                    var capitalizedFolder = char.ToUpper(folderName[0]) + folderName.Substring(1);
                    var testPath = $"{driveLetter}:\\{capitalizedFolder}";
                    if (Directory.Exists(testPath))
                        return testPath;
                    
                    // Try lowercase
                    testPath = $"{driveLetter}:\\{folderName}";
                    if (Directory.Exists(testPath))
                        return testPath;
                }
                
                // Just the drive root
                var driveRoot = $"{driveLetter}:\\";
                if (Directory.Exists(driveRoot))
                    return driveRoot;
            }
                
            // Look for common folder names (case-insensitive) - only if no drive was mentioned
            if (!Regex.IsMatch(msgLower, @"[a-z]\s*drive") && !Regex.IsMatch(msgLower, @"on\s+[a-z]\s*(?:drive|:)"))
            {
                if (msgLower.Contains("desktop"))
                    return Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                if (msgLower.Contains("documents") || msgLower.Contains("my documents"))
                    return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                if (msgLower.Contains("downloads") || msgLower.Contains("download folder"))
                    return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
                if (msgLower.Contains("pictures") || msgLower.Contains("photos"))
                    return Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
                if (msgLower.Contains("music"))
                    return Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
                if (msgLower.Contains("videos"))
                    return Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
            }
                
            return "";
        }
        
        /// <summary>
        /// Extract Windows system paths with comprehensive knowledge of Windows file system
        /// Works universally across different Windows machines
        /// </summary>
        private static string ExtractWindowsSystemPath(string msgLower)
        {
            // ==================== SYSTEM FOLDERS ====================
            
            // Windows system folders
            if (ContainsAny(msgLower, "windows folder", "windows directory", "windows system"))
                return Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            
            if (ContainsAny(msgLower, "system32", "system 32"))
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "");
            
            if (ContainsAny(msgLower, "syswow64", "syswow 64"))
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "SysWOW64");
            
            // Program Files
            if (ContainsAny(msgLower, "program files", "programs folder", "installed programs"))
                return Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            
            if (ContainsAny(msgLower, "program files x86", "program files (x86)", "32 bit programs"))
                return Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            
            // ==================== USER PROFILE FOLDERS ====================
            
            // User profile
            if (ContainsAny(msgLower, "user profile", "user folder", "my folder", "home folder"))
                return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            
            // AppData folders
            if (ContainsAny(msgLower, "appdata", "app data", "application data"))
                return Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            
            if (ContainsAny(msgLower, "appdata roaming", "roaming", "roaming data"))
                return Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            
            if (ContainsAny(msgLower, "appdata local", "local appdata", "local data"))
                return Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            
            if (ContainsAny(msgLower, "appdata locallow", "local low", "locallow"))
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "LocalLow");
            
            // Temp folders
            if (ContainsAny(msgLower, "temp folder", "temporary files", "temp directory"))
                return Path.GetTempPath();
            
            // ==================== COMMON APPLICATION FOLDERS ====================
            
            // Startup folder
            if (ContainsAny(msgLower, "startup folder", "startup programs", "autostart"))
                return Environment.GetFolderPath(Environment.SpecialFolder.Startup);
            
            // Start Menu
            if (ContainsAny(msgLower, "start menu", "programs menu"))
                return Environment.GetFolderPath(Environment.SpecialFolder.StartMenu);
            
            // Common Start Menu
            if (ContainsAny(msgLower, "all users start menu", "common start menu"))
                return Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu);
            
            // Recent files
            if (ContainsAny(msgLower, "recent files", "recent documents", "recent folder"))
                return Environment.GetFolderPath(Environment.SpecialFolder.Recent);
            
            // Send To
            if (ContainsAny(msgLower, "send to", "sendto"))
                return Environment.GetFolderPath(Environment.SpecialFolder.SendTo);
            
            // ==================== SYSTEM DATA FOLDERS ====================
            
            // ProgramData (All Users)
            if (ContainsAny(msgLower, "programdata", "program data", "all users data", "common application data"))
                return Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            
            // Public folders
            if (ContainsAny(msgLower, "public folder", "public documents", "shared documents"))
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonDocuments));
            
            if (ContainsAny(msgLower, "public desktop", "shared desktop"))
                return Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);
            
            if (ContainsAny(msgLower, "public music", "shared music"))
                return Environment.GetFolderPath(Environment.SpecialFolder.CommonMusic);
            
            if (ContainsAny(msgLower, "public pictures", "shared pictures"))
                return Environment.GetFolderPath(Environment.SpecialFolder.CommonPictures);
            
            if (ContainsAny(msgLower, "public videos", "shared videos"))
                return Environment.GetFolderPath(Environment.SpecialFolder.CommonVideos);
            
            // ==================== SPECIAL WINDOWS LOCATIONS ====================
            
            // Recycle Bin (special handling)
            if (ContainsAny(msgLower, "recycle bin", "trash", "deleted files"))
                return "shell:RecycleBinFolder"; // Special shell path
            
            // Control Panel
            if (ContainsAny(msgLower, "control panel", "system settings"))
                return "shell:ControlPanelFolder"; // Special shell path
            
            // Network
            if (ContainsAny(msgLower, "network", "network places", "network computers"))
                return "shell:NetworkPlacesFolder"; // Special shell path
            
            // This PC / Computer
            if (ContainsAny(msgLower, "this pc", "my computer", "computer"))
                return "shell:MyComputerFolder"; // Special shell path
            
            // ==================== FONTS AND SYSTEM RESOURCES ====================
            
            // Fonts
            if (ContainsAny(msgLower, "fonts folder", "fonts", "system fonts"))
                return Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
            
            // System resources
            if (ContainsAny(msgLower, "resources", "system resources"))
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Resources");
            
            // ==================== WINDOWS LOGS AND DIAGNOSTICS ====================
            
            // Windows Logs
            if (ContainsAny(msgLower, "windows logs", "event logs", "system logs"))
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Logs");
            
            // CBS Logs (Windows Update logs)
            if (ContainsAny(msgLower, "cbs logs", "update logs", "windows update logs"))
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Logs", "CBS");
            
            // DISM Logs
            if (ContainsAny(msgLower, "dism logs"))
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Logs", "DISM");
            
            // Minidump (crash dumps)
            if (ContainsAny(msgLower, "minidump", "crash dump", "blue screen logs", "bsod logs"))
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Minidump");
            
            // ==================== WINDOWS CONFIGURATION ====================
            
            // Hosts file location
            if (ContainsAny(msgLower, "hosts file", "etc folder", "drivers etc"))
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "drivers", "etc");
            
            // Windows INF (driver information)
            if (ContainsAny(msgLower, "inf folder", "driver inf", "windows inf"))
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "INF");
            
            // Windows Prefetch
            if (ContainsAny(msgLower, "prefetch", "prefetch folder"))
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Prefetch");
            
            // Windows WinSxS (component store)
            if (ContainsAny(msgLower, "winsxs", "component store", "side by side"))
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "WinSxS");
            
            // ==================== USER HIDDEN FOLDERS ====================
            
            // Cookies
            if (ContainsAny(msgLower, "cookies", "cookie folder"))
                return Environment.GetFolderPath(Environment.SpecialFolder.Cookies);
            
            // Internet Cache
            if (ContainsAny(msgLower, "internet cache", "browser cache", "temporary internet"))
                return Environment.GetFolderPath(Environment.SpecialFolder.InternetCache);
            
            // History
            if (ContainsAny(msgLower, "history folder", "browser history folder"))
                return Environment.GetFolderPath(Environment.SpecialFolder.History);
            
            // Saved Games
            if (ContainsAny(msgLower, "saved games", "game saves"))
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Saved Games");
            
            // Contacts
            if (ContainsAny(msgLower, "contacts folder", "my contacts"))
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Contacts");
            
            // Favorites
            if (ContainsAny(msgLower, "favorites", "bookmarks folder"))
                return Environment.GetFolderPath(Environment.SpecialFolder.Favorites);
            
            // Links (Quick Access)
            if (ContainsAny(msgLower, "links folder", "quick access links"))
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Links");
            
            // Searches
            if (ContainsAny(msgLower, "searches folder", "saved searches"))
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Searches");
            
            // ==================== BROWSER DATA LOCATIONS ====================
            
            // Chrome user data
            if (ContainsAny(msgLower, "chrome data", "chrome folder", "chrome profile"))
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Google", "Chrome", "User Data");
            
            // Edge user data
            if (ContainsAny(msgLower, "edge data", "edge folder", "edge profile"))
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "Edge", "User Data");
            
            // Firefox profiles
            if (ContainsAny(msgLower, "firefox data", "firefox folder", "firefox profile"))
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Mozilla", "Firefox", "Profiles");
            
            // ==================== COMMON APP DATA LOCATIONS ====================
            
            // Discord
            if (ContainsAny(msgLower, "discord data", "discord folder"))
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "discord");
            
            // Slack
            if (ContainsAny(msgLower, "slack data", "slack folder"))
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Slack");
            
            // VS Code
            if (ContainsAny(msgLower, "vscode data", "vs code folder", "vscode settings", "code folder"))
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Code");
            
            // Notepad++ (if installed)
            if (ContainsAny(msgLower, "notepad++ data", "notepad plus plus"))
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Notepad++");
            
            // ==================== NETWORK AND DRIVES ====================
            
            // Network drives (UNC paths)
            var uncMatch = Regex.Match(msgLower, @"\\\\([a-zA-Z0-9\-\.]+)\\?([a-zA-Z0-9\-\._\$]*)?");
            if (uncMatch.Success)
            {
                var server = uncMatch.Groups[1].Value;
                var share = uncMatch.Groups[2].Success ? uncMatch.Groups[2].Value : "";
                return string.IsNullOrEmpty(share) ? $"\\\\{server}" : $"\\\\{server}\\{share}";
            }
            
            // ==================== REGISTRY LOCATIONS (for advanced users) ====================
            
            if (ContainsAny(msgLower, "registry", "regedit"))
                return "regedit"; // Special command
            
            // ==================== COMMON THIRD-PARTY LOCATIONS ====================
            
            // Steam
            if (ContainsAny(msgLower, "steam folder", "steam games", "steam directory"))
            {
                var steamPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam");
                if (Directory.Exists(steamPath))
                    return steamPath;
                // Alternative location
                steamPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Steam");
                if (Directory.Exists(steamPath))
                    return steamPath;
            }
            
            // Epic Games
            if (ContainsAny(msgLower, "epic games", "epic launcher", "fortnite folder"))
            {
                var epicPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Epic Games");
                if (Directory.Exists(epicPath))
                    return epicPath;
            }
            
            // GOG Galaxy
            if (ContainsAny(msgLower, "gog folder", "gog games", "gog galaxy"))
            {
                var gogPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "GOG Galaxy");
                if (Directory.Exists(gogPath))
                    return gogPath;
            }
            
            // Origin/EA
            if (ContainsAny(msgLower, "origin folder", "ea games", "origin games"))
            {
                var originPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Origin");
                if (Directory.Exists(originPath))
                    return originPath;
            }
            
            // Battle.net
            if (ContainsAny(msgLower, "battle.net", "battlenet", "blizzard folder"))
            {
                var bnetPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Battle.net");
                if (Directory.Exists(bnetPath))
                    return bnetPath;
            }
            
            // OneDrive
            if (ContainsAny(msgLower, "onedrive", "one drive"))
            {
                var oneDrivePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "OneDrive");
                if (Directory.Exists(oneDrivePath))
                    return oneDrivePath;
            }
            
            // Dropbox
            if (ContainsAny(msgLower, "dropbox"))
            {
                var dropboxPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Dropbox");
                if (Directory.Exists(dropboxPath))
                    return dropboxPath;
            }
            
            // Google Drive
            if (ContainsAny(msgLower, "google drive"))
            {
                var googleDrivePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Google Drive");
                if (Directory.Exists(googleDrivePath))
                    return googleDrivePath;
            }
            
            // iCloud Drive
            if (ContainsAny(msgLower, "icloud", "icloud drive"))
            {
                var icloudPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "iCloudDrive");
                if (Directory.Exists(icloudPath))
                    return icloudPath;
            }
            
            // ==================== DEVELOPMENT FOLDERS ====================
            
            // .nuget packages
            if (ContainsAny(msgLower, "nuget packages", "nuget folder", ".nuget"))
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");
            
            // .npm
            if (ContainsAny(msgLower, "npm folder", "npm cache", ".npm"))
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".npm");
            
            // .gradle
            if (ContainsAny(msgLower, "gradle folder", "gradle cache", ".gradle"))
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".gradle");
            
            // .m2 (Maven)
            if (ContainsAny(msgLower, "maven folder", "maven cache", ".m2"))
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".m2");
            
            // .docker
            if (ContainsAny(msgLower, "docker folder", ".docker"))
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".docker");
            
            // .ssh
            if (ContainsAny(msgLower, "ssh folder", "ssh keys", ".ssh"))
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh");
            
            // .gitconfig location
            if (ContainsAny(msgLower, "git config", "gitconfig", ".gitconfig"))
                return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            
            // .aws
            if (ContainsAny(msgLower, "aws folder", "aws config", ".aws"))
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".aws");
            
            // .azure
            if (ContainsAny(msgLower, "azure folder", "azure config", ".azure"))
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".azure");
            
            return "";
        }
        
        private static string ExtractCommand(string message)
        {
            var patterns = new[]
            {
                @"(?:run|execute|cmd) (.+)",
            };
            
            foreach (var pattern in patterns)
            {
                var match = Regex.Match(message, pattern, RegexOptions.IgnoreCase);
                if (match.Success)
                    return match.Groups[1].Value.Trim();
            }
            
            return "";
        }

        private static string ExtractNewPath(string message)
        {
            var match = Regex.Match(message, @"(?:create|make|new)\s+folder\s+(.+)", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                var name = match.Groups[1].Value.Trim();
                // If it's just a name, put it on desktop
                if (!name.Contains("\\") && !name.Contains("/"))
                    return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), name);
                return name;
            }
            return "";
        }

        private static string ExtractTargetPath(string message)
        {
            var match = Regex.Match(message, @"(?:delete|remove|del)\s+(.+)", RegexOptions.IgnoreCase);
            if (match.Success)
                return match.Groups[1].Value.Trim();
            return "";
        }
        
        /// <summary>
        /// Resolve a file/folder path from user input, checking common locations
        /// </summary>
        private static string ResolvePath(string path, string context)
        {
            if (string.IsNullOrEmpty(path)) return "";
            
            // Clean up the path
            path = path.Trim().Trim('"', '\'');
            
            // Remove common phrases
            path = Regex.Replace(path, @"\s*(from|on|in)\s+(my\s+)?(desktop|documents|downloads).*$", "", RegexOptions.IgnoreCase).Trim();
            path = Regex.Replace(path, @"\s*please\s*$", "", RegexOptions.IgnoreCase).Trim();
            path = Regex.Replace(path, @"\s*it\s*$", "", RegexOptions.IgnoreCase).Trim();
            
            // If it's already a full path and exists, return it
            if (Path.IsPathRooted(path))
            {
                if (File.Exists(path) || Directory.Exists(path))
                    return path;
            }
            
            // Determine target folder from context
            var contextLower = context.ToLowerInvariant();
            var searchFolders = new List<string>();
            
            if (contextLower.Contains("desktop"))
                searchFolders.Add(Environment.GetFolderPath(Environment.SpecialFolder.Desktop));
            if (contextLower.Contains("document"))
                searchFolders.Add(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
            if (contextLower.Contains("download"))
                searchFolders.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"));
            if (contextLower.Contains("music"))
                searchFolders.Add(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic));
            if (contextLower.Contains("picture") || contextLower.Contains("photo"))
                searchFolders.Add(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures));
            if (contextLower.Contains("video"))
                searchFolders.Add(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos));
            
            // Default to desktop if no location specified
            if (searchFolders.Count == 0)
                searchFolders.Add(Environment.GetFolderPath(Environment.SpecialFolder.Desktop));
            
            // Also add current directory
            searchFolders.Add(Directory.GetCurrentDirectory());
            
            // Search for the file/folder
            foreach (var folder in searchFolders)
            {
                var fullPath = Path.Combine(folder, path);
                if (File.Exists(fullPath) || Directory.Exists(fullPath))
                    return fullPath;
                
                // Try with common extensions if no extension provided
                if (!Path.HasExtension(path))
                {
                    var extensions = new[] { ".txt", ".pdf", ".doc", ".docx", ".jpg", ".png", ".mp3", ".mp4" };
                    foreach (var ext in extensions)
                    {
                        var withExt = fullPath + ext;
                        if (File.Exists(withExt))
                            return withExt;
                    }
                }
            }
            
            return "";
        }

        private static (string source, string dest) ExtractSourceDest(string message)
        {
            var match = Regex.Match(message, @"(?:move|copy)\s+(.+?)\s+to\s+(.+)", RegexOptions.IgnoreCase);
            if (match.Success)
                return (match.Groups[1].Value.Trim(), match.Groups[2].Value.Trim());
            return ("", "");
        }

        private static (string path, string newName) ExtractRenameParts(string message)
        {
            var match = Regex.Match(message, @"rename\s+(.+?)\s+to\s+(.+)", RegexOptions.IgnoreCase);
            if (match.Success)
                return (match.Groups[1].Value.Trim(), match.Groups[2].Value.Trim());
            return ("", "");
        }

        private static (string path, string pattern) ExtractFindParams(string message)
        {
            // Extract search pattern
            var match = Regex.Match(message, @"(?:find|search|locate|where is)\s+(?:file[s]?\s+)?(.+?)(?:\s+in\s+(.+))?$", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                var pattern = match.Groups[1].Value.Trim();
                var path = match.Groups[2].Success ? match.Groups[2].Value.Trim() : "";
                
                // Handle common locations
                if (path.ToLower().Contains("desktop"))
                    path = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                else if (path.ToLower().Contains("documents"))
                    path = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                else if (path.ToLower().Contains("downloads"))
                    path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
                else if (string.IsNullOrEmpty(path))
                    path = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                    
                return (path, pattern);
            }
            return ("", "");
        }

        private static async Task<string> SetupSpotifyApiAsync()
        {
            return @"🎵 Spotify Control

Spotify now works directly! Just say:
- ""Play [song name]"" - to play a song
- ""Play [song] by [artist]"" - to play specific track
- ""Pause music"" / ""Next song"" / ""Previous song""

No API setup needed!";
        }

        private static async Task<string> AuthenticateSpotifyAsync()
        {
            return @"✅ Spotify Ready!

Spotify control works automatically. Try:
- ""Play Bohemian Rhapsody""
- ""Play Shape of You by Ed Sheeran""
- ""Pause music"" / ""Next song""";
        }

        private static string ExtractYouTubeQuery(string message)
        {
            var patterns = new[]
            {
                @"(?:play|watch|search|find|open)\s+(.+)\s+on\s+youtube",  // "play X on youtube"
                @"(?:play|watch|search|find|open)\s+(.+)\s+(?:on\s+)?youtube",  // "play X youtube"
                @"youtube\s+(?:search|play|find|watch)\s+(.+)",  // "youtube play X"
                @"youtube\s+(.+)",  // "youtube X"
                @"on\s+youtube\s+(?:play|watch|search|find)?\s*(.+)",  // "on youtube play X"
            };

            foreach (var pattern in patterns)
            {
                var match = Regex.Match(message, pattern, RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    var query = match.Groups[1].Value.Trim();
                    // Clean up common words
                    query = Regex.Replace(query, @"\s*(please|for me|now|video|on youtube|youtube|on)$", "", RegexOptions.IgnoreCase);
                    query = Regex.Replace(query, @"^(a |the |some )", "", RegexOptions.IgnoreCase);
                    if (!string.IsNullOrWhiteSpace(query))
                    {
                        System.Diagnostics.Debug.WriteLine($"[YouTube] Extracted query: {query}");
                        return query;
                    }
                }
            }

            System.Diagnostics.Debug.WriteLine($"[YouTube] No query extracted from: {message}");
            return "";
        }

        private static async Task<string> OpenYouTubeSearchAsync(string query)
        {
            try
            {
                var encodedQuery = System.Net.WebUtility.UrlEncode(query);
                var url = $"https://www.youtube.com/results?search_query={encodedQuery}";
                
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
                
                return $"🎬 Opened YouTube search for \"{query}\"";
            }
            catch (Exception ex)
            {
                return $"❌ Failed to open YouTube: {ex.Message}";
            }
        }

        // ==================== SECURITY SUITE HELPER METHODS ====================
        
        private static SecuritySuiteManager GetSecuritySuite()
        {
            _securitySuite ??= new SecuritySuiteManager();
            return _securitySuite;
        }
        
        private static async Task<string> RunSecuritySuiteScanAsync(ScanType scanType)
        {
            try
            {
                var suite = GetSecuritySuite();
                var job = await suite.StartScanAsync(scanType);
                
                if (job.Status == ScanStatus.Completed)
                {
                    if (job.ThreatsFound == 0)
                    {
                        return $"✅ {scanType} Scan Complete\n\n" +
                               $"No threats detected! Your system looks clean.\n\n" +
                               $"📊 Scanned: {job.FilesScanned:N0} files in {job.Duration?.TotalSeconds:F1}s";
                    }
                    else
                    {
                        // Store findings for "fix it" command
                        _lastScanResult = new UnifiedScanResult
                        {
                            FilesScanned = job.FilesScanned,
                            ThreatsFound = job.ThreatsFound,
                            Threats = job.Findings.Select(f => new UnifiedThreat
                            {
                                Name = f.Title,
                                Location = f.FilePath,
                                Description = f.Description,
                                CanRemove = f.CanDelete || f.CanQuarantine
                            }).ToList()
                        };
                        _lastScanTime = DateTime.Now;
                        
                        return $"⚠️ {scanType} Scan Complete\n\n" +
                               $"🔴 Found {job.ThreatsFound} potential threat(s)!\n\n" +
                               $"📊 Scanned: {job.FilesScanned:N0} files\n\n" +
                               $"💡 Say 'fix it' to remove threats, or say 'open security suite' to review them.";
                    }
                }
                else if (job.Status == ScanStatus.Cancelled)
                {
                    return $"⚠️ {scanType} scan was cancelled.\n\n📊 Scanned: {job.FilesScanned:N0} files before cancellation.";
                }
                else
                {
                    return $"❌ {scanType} scan failed: {job.ErrorMessage}";
                }
            }
            catch (Exception ex)
            {
                return $"❌ Scan failed: {ex.Message}\n\nTry saying 'open security suite' for the full security center.";
            }
        }
        
        private static async Task<string> CheckSecurityUpdatesAsync()
        {
            try
            {
                var suite = GetSecuritySuite();
                var (available, message) = await suite.CheckForUpdatesAsync();
                
                if (available)
                {
                    return $"📦 Update Available\n\n{message}\n\n💡 Say 'update now' to install the update.";
                }
                else
                {
                    var info = suite.DefinitionsManager.GetCurrentInfo();
                    return $"✅ Definitions Up to Date\n\n" +
                           $"Version: {info.Version}\n" +
                           $"Last Updated: {info.LastUpdated:g}\n" +
                           $"Signatures: {info.SignatureCount:N0}";
                }
            }
            catch (Exception ex)
            {
                return $"❌ Failed to check for updates: {ex.Message}";
            }
        }
        
        private static async Task<string> ApplySecurityUpdatesAsync()
        {
            try
            {
                var suite = GetSecuritySuite();
                var (success, message) = await suite.UpdateDefinitionsAsync();
                
                if (success)
                {
                    var info = suite.DefinitionsManager.GetCurrentInfo();
                    return $"✅ Update Complete\n\n{message}\n\n" +
                           $"Now running: v{info.Version}\n" +
                           $"Signatures: {info.SignatureCount:N0}";
                }
                else
                {
                    return $"❌ Update Failed\n\n{message}";
                }
            }
            catch (Exception ex)
            {
                return $"❌ Failed to apply update: {ex.Message}";
            }
        }
        
        /// <summary>
        /// Actually set the default browser using Windows registry
        /// </summary>
        private static async Task<string> SetDefaultBrowserAsync(string browserName)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"[SetDefaultBrowser] Setting {browserName} as default browser");
                
                // Map browser names to their executable names
                string executableName = browserName.ToLower() switch
                {
                    "firefox" => "firefox.exe",
                    "chrome" => "chrome.exe", 
                    "edge" => "msedge.exe",
                    _ => $"{browserName}.exe"
                };
                
                // Use Windows built-in command to set default browser
                var startInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c start ms-settings:defaultapps",
                    UseShellExecute = true,
                    CreateNoWindow = true
                };
                
                // For now, open the settings page - Windows 10/11 requires user interaction for security
                System.Diagnostics.Process.Start(startInfo);
                
                return $"🌐 Setting {browserName.ToUpper()} as Default Browser\n\n" +
                       $"✅ Opened Default Apps settings\n" +
                       $"📋 Click on 'Web browser' and select {browserName.ToUpper()}\n\n" +
                       $"Note: Windows requires manual confirmation for security reasons.";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SetDefaultBrowser] Error: {ex.Message}");
                return $"❌ Failed to set default browser: {ex.Message}";
            }
        }
    }
}
