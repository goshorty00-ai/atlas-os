using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AtlasAI.Voice;

namespace AtlasAI.Tools
{
    /// <summary>
    /// Handles requests DIRECTLY without going through AI chat.
    /// Like Kiro - understands what you mean and just does it.
    /// </summary>
    public static class DirectActionHandler
    {
        // Context memory - remembers recent topics for "that", "it", "this" references
        private static string? _lastMentionedApp;
        private static string? _lastMentionedFile;
        private static string? _lastMentionedProcess;
        private static string? _lastMentionedUrl;
        private static string? _lastAction;
        
        // Reference to InAppAssistantManager for Atlas overlay control
        private static InAppAssistant.InAppAssistantManager? _inAppAssistant;
        
        /// <summary>
        /// Set the InAppAssistantManager reference for overlay control
        /// </summary>
        public static void SetInAppAssistant(InAppAssistant.InAppAssistantManager? manager)
        {
            _inAppAssistant = manager;
        }
        
        // Common app aliases - what people say vs what to actually open/kill
        private static readonly Dictionary<string, string> AppAliases = new(StringComparer.OrdinalIgnoreCase)
        {
            // Browsers
            { "chrome", "chrome" }, { "google", "chrome" }, { "browser", "chrome" },
            { "firefox", "firefox" }, { "edge", "msedge" }, { "brave", "brave" },
            
            // Communication
            { "discord", "discord" }, { "slack", "slack" }, { "teams", "teams" },
            { "zoom", "zoom" }, { "skype", "skype" },
            
            // Media
            { "spotify", "spotify" }, { "music", "spotify" },
            { "vlc", "vlc" }, { "youtube", "chrome https://youtube.com" },
            
            // Dev tools
            { "code", "code" }, { "vscode", "code" }, { "vs code", "code" },
            { "visual studio", "devenv" }, { "vs", "devenv" },
            { "terminal", "wt" }, { "cmd", "cmd" }, { "powershell", "powershell" },
            
            // Productivity
            { "notepad", "notepad" }, { "notes", "notepad" },
            { "word", "winword" }, { "excel", "excel" }, { "powerpoint", "powerpnt" },
            { "outlook", "outlook" }, { "mail", "outlook" },
            
            // System
            { "explorer", "explorer" }, { "files", "explorer" },
            { "settings", "ms-settings:" }, { "control panel", "control" },
            { "task manager", "taskmgr" }, { "taskman", "taskmgr" },
            
            // Games
            { "steam", "steam" }, { "epic", "epicgameslauncher" },
            
            // Utils
            { "calculator", "calc" }, { "calc", "calc" },
            { "paint", "mspaint" }, { "snip", "snippingtool" },
        };

        /// <summary>
        /// Check if a string is a known app alias
        /// </summary>
        public static bool IsKnownApp(string name) => AppAliases.ContainsKey(name);
        
        // Process name mappings for killing
        private static readonly Dictionary<string, string[]> ProcessNames = new(StringComparer.OrdinalIgnoreCase)
        {
            { "chrome", new[] { "chrome", "Google Chrome" } },
            { "firefox", new[] { "firefox" } },
            { "edge", new[] { "msedge", "Microsoft Edge" } },
            { "discord", new[] { "Discord", "discord" } },
            { "spotify", new[] { "Spotify" } },
            { "steam", new[] { "steam", "steamwebhelper" } },
            { "vscode", new[] { "Code" } },
            { "code", new[] { "Code" } },
            { "teams", new[] { "Teams", "ms-teams" } },
            { "slack", new[] { "slack" } },
            { "zoom", new[] { "Zoom" } },
            { "nvidia overlay", new[] { "NVIDIA Overlay", "nvcontainer", "NVIDIA Share" } },
            { "geforce", new[] { "NVIDIA GeForce Experience", "nvcontainer" } },
            { "afterburner", new[] { "MSIAfterburner", "RTSS" } },
            { "atlas overlay", new[] { "ATLAS_OVERLAY" } }, // Special marker for Atlas overlay
            { "overlay", new[] { "ATLAS_OVERLAY", "NVIDIA Overlay", "RTSS", "MSIAfterburner" } }, // Try Atlas first
        };

        /// <summary>
        /// Try to handle the request directly. Returns response if handled, null if should go to AI.
        /// </summary>
        public static async Task<string?> TryHandleAsync(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return null;
            
            var text = input.Trim();
            var lower = text.ToLowerInvariant();
            
            // === QUICK RESPONSES - Instant replies for greetings/thanks/etc ===
            var quickResponse = Agent.QuickResponseMode.TryGetQuickResponse(text);
            if (quickResponse != null)
            {
                return quickResponse;
            }
            
            // === VOICE MACROS ===
            // Check for macro recording commands
            if (lower == "start recording" || lower == "record macro")
                return Agent.VoiceMacros.Instance.StartRecording();
            if (lower == "stop recording")
                return Agent.VoiceMacros.Instance.StopRecording();
            if (lower.StartsWith("save macro as ") || lower.StartsWith("save as "))
            {
                var name = lower.StartsWith("save macro as ") ? text.Substring(14) : text.Substring(8);
                return Agent.VoiceMacros.Instance.SaveMacro(name.Trim());
            }
            if (lower == "list macros" || lower == "show macros" || lower == "my macros")
                return Agent.VoiceMacros.Instance.ListMacros();
            
            // Try to run a saved macro
            var macroResult = await Agent.VoiceMacros.Instance.TryRunMacroAsync(text);
            if (macroResult != null) return macroResult;
            
            // === SMART AUTOMATIONS ===
            if (lower.StartsWith("when i say") || lower.StartsWith("when ") && lower.Contains(" do "))
            {
                var (trigger, actions, schedule) = Agent.SmartAutomation.Instance.ParseAutomationRequest(text);
                if (trigger != null && actions != null)
                    return Agent.SmartAutomation.Instance.CreateAutomation(trigger, actions, schedule);
            }
            if (lower == "list automations" || lower == "show automations" || lower == "my automations")
                return Agent.SmartAutomation.Instance.ListAutomations();
            
            // Try to trigger an automation
            var automationResult = await Agent.SmartAutomation.Instance.TryTriggerAsync(text);
            if (automationResult != null) return automationResult;
            
            // === REMINDERS ===
            if (lower.StartsWith("remind me"))
                return Agent.SmartReminders.Instance.CreateReminder(text);
            if (lower == "list reminders" || lower == "show reminders" || lower == "my reminders")
                return Agent.SmartReminders.Instance.ListReminders();
            if (lower.StartsWith("cancel reminder"))
                return Agent.SmartReminders.Instance.CancelReminder(text.Substring(16).Trim());
            
            // === NOTES & LISTS ===
            var (noteAction, listName, noteContent) = Agent.QuickNotes.Instance.ParseNoteCommand(text);
            if (noteAction != null)
            {
                return noteAction switch
                {
                    "add_note" => Agent.QuickNotes.Instance.AddNote(noteContent!),
                    "add_to_list" => Agent.QuickNotes.Instance.AddToList(listName!, noteContent!),
                    "get_list" => Agent.QuickNotes.Instance.GetList(listName!),
                    "remove_from_list" => Agent.QuickNotes.Instance.RemoveFromList(listName!, noteContent!),
                    "clear_list" => Agent.QuickNotes.Instance.ClearList(listName!),
                    "show_notes" => Agent.QuickNotes.Instance.GetRecentNotes(),
                    "show_lists" => Agent.QuickNotes.Instance.ListAllLists(),
                    _ => null
                };
            }
            
            // === FOCUS MODE ===
            var focusCmd = Agent.FocusMode.Instance.ParseFocusCommand(text);
            if (focusCmd.HasValue)
            {
                return focusCmd.Value.Action switch
                {
                    "start" => Agent.FocusMode.Instance.StartFocus(focusCmd.Value.Minutes),
                    "pomodoro" => Agent.FocusMode.Instance.StartPomodoro(),
                    "end" => Agent.FocusMode.Instance.EndFocus(),
                    "extend" => Agent.FocusMode.Instance.ExtendFocus(focusCmd.Value.Minutes),
                    "status" => Agent.FocusMode.Instance.GetStatus(),
                    _ => null
                };
            }
            
            // === QUICK CALCULATOR ===
            var calcResult = Agent.QuickCalculator.TryCalculate(text);
            if (calcResult != null) return calcResult;
            
            // === PASSWORD GENERATOR ===
            var passwordResult = await Agent.PasswordGenerator.TryHandleAsync(text);
            if (passwordResult != null) return passwordResult;
            
            // === COLOR TOOLS ===
            var colorResult = await Agent.ColorTools.TryHandleAsync(text);
            if (colorResult != null) return colorResult;
            
            // === HASH GENERATOR ===
            var hashResult = await Agent.HashGenerator.TryHandleAsync(text);
            if (hashResult != null) return hashResult;
            
            // === QR CODE GENERATOR ===
            var qrResult = await Agent.QRCodeGenerator.TryHandleAsync(text);
            if (qrResult != null) return qrResult;
            
            // === LOREM IPSUM ===
            var loremResult = await Agent.LoremIpsum.TryHandleAsync(text);
            if (loremResult != null) return loremResult;
            
            // === JSON FORMATTER ===
            var jsonResult = await Agent.JsonFormatter.TryHandleAsync(text);
            if (jsonResult != null) return jsonResult;
            
            // === REGEX TESTER ===
            var regexResult = await Agent.RegexTester.TryHandleAsync(text);
            if (regexResult != null) return regexResult;
            
            // === NETWORK TOOLS ===
            var networkResult = await Agent.NetworkTools.TryHandleAsync(text);
            if (networkResult != null) return networkResult;
            
            // === BASE64 TOOL ===
            var base64Result = await Agent.Base64Tool.TryHandleAsync(text);
            if (base64Result != null) return base64Result;
            
            // === EPOCH CONVERTER ===
            var epochResult = await Agent.EpochConverter.TryHandleAsync(text);
            if (epochResult != null) return epochResult;
            
            // === CRON HELPER ===
            var cronResult = await Agent.CronHelper.TryHandleAsync(text);
            if (cronResult != null) return cronResult;
            
            // === MARKDOWN PREVIEW ===
            var markdownResult = await Agent.MarkdownPreview.TryHandleAsync(text);
            if (markdownResult != null) return markdownResult;
            
            // === UUID GENERATOR ===
            var uuidResult = await Agent.UuidGenerator.TryHandleAsync(text);
            if (uuidResult != null) return uuidResult;
            
            // === IP LOOKUP ===
            var ipResult = await Agent.IpLookup.TryHandleAsync(text);
            if (ipResult != null) return ipResult;
            
            // === WORD COUNTER ===
            var wordCountResult = await Agent.WordCounter.TryHandleAsync(text);
            if (wordCountResult != null) return wordCountResult;
            
            // === URL TOOLS ===
            var urlResult = await Agent.UrlTools.TryHandleAsync(text);
            if (urlResult != null) return urlResult;
            
            // === DICE ROLLER ===
            var diceResult = await Agent.DiceRoller.TryHandleAsync(text);
            if (diceResult != null) return diceResult;
            
            // === TRANSLATOR ===
            var translateResult = await Agent.Translator.TryHandleAsync(text);
            if (translateResult != null) return translateResult;
            
            // === CURRENCY CONVERTER ===
            var currencyResult = await Agent.CurrencyConverter.TryHandleAsync(text);
            if (currencyResult != null) return currencyResult;
            
            // === SYSTEM CLEANER ===
            var cleanerResult = await Agent.SystemCleaner.TryHandleAsync(text);
            if (cleanerResult != null) return cleanerResult;
            
            // === PROCESS MANAGER ===
            var processResult = await Agent.ProcessManager.TryHandleAsync(text);
            if (processResult != null) return processResult;
            
            // === SCREEN CAPTURE ===
            var captureResult = await Agent.ScreenCapture.TryHandleAsync(text);
            if (captureResult != null) return captureResult;
            
            // === QUICK MATH ===
            var mathResult = await Agent.QuickMath.TryHandleAsync(text);
            if (mathResult != null) return mathResult;
            
            // === POWERSHELL RUNNER (system commands like Kiro) ===
            var powershellResult = await Agent.PowerShellRunner.TryHandleAsync(text);
            if (powershellResult != null) return powershellResult;
            
            // === SECURITY AGENT ===
            var securityResult = await Agent.SecurityAgent.Instance.TryHandleAsync(text);
            if (securityResult != null) return securityResult;
            
            // === QUICK COMMANDS (system info) ===
            var quickCmdResult = await Agent.QuickCommands.TryHandleAsync(text);
            if (quickCmdResult != null) return quickCmdResult;
            
            // === TEXT TRANSFORMS ===
            var transformResult = await Agent.TextTransform.TryTransformAsync(text);
            if (transformResult != null) return transformResult;
            
            // === TIMERS & STOPWATCH ===
            var timerCmd = Agent.QuickTimer.Instance.ParseTimerCommand(text);
            if (timerCmd.HasValue)
            {
                return timerCmd.Value.Action switch
                {
                    "set" => Agent.QuickTimer.Instance.SetTimer(timerCmd.Value.Seconds, timerCmd.Value.Name),
                    "cancel" => Agent.QuickTimer.Instance.CancelTimer(timerCmd.Value.Name),
                    "status" => Agent.QuickTimer.Instance.GetTimerStatus(),
                    "start_stopwatch" => Agent.QuickTimer.Instance.StartStopwatch(),
                    "stop_stopwatch" => Agent.QuickTimer.Instance.StopStopwatch(),
                    "reset_stopwatch" => Agent.QuickTimer.Instance.ResetStopwatch(),
                    "lap" => Agent.QuickTimer.Instance.LapStopwatch(),
                    _ => null
                };
            }
            
            // === DAILY BRIEFING ===
            if (lower == "good morning" || lower == "morning briefing" || lower == "briefing")
                return await Agent.DailyBriefing.Instance.GetMorningBriefingAsync();
            if (lower == "evening summary" || lower == "daily summary" || lower == "end of day")
                return await Agent.DailyBriefing.Instance.GetEveningSummaryAsync();
            if (lower == "status" || lower == "quick status")
                return Agent.DailyBriefing.Instance.GetQuickStatus();
            
            // === CONTEXT AWARENESS ===
            if (lower == "context" || lower == "what am i doing" || lower == "current context")
                return Agent.ContextAwareness.Instance.GetContextSummary();
            if (lower == "usage" || lower == "app usage" || lower == "screen time")
                return Agent.ContextAwareness.Instance.GetUsageSummary();
            if (lower == "activity" || lower == "activity log" || lower == "what did i do")
                return Agent.ContextAwareness.Instance.GetActivityLog();
            if (lower == "suggestions" || lower == "what should i do")
            {
                var suggestions = Agent.ContextAwareness.Instance.GetSmartSuggestions();
                return suggestions.Any() 
                    ? "💡 **Suggestions:**\n" + string.Join("\n", suggestions.Select(s => $"• {s}"))
                    : "No suggestions right now. Keep doing what you're doing! 👍";
            }
            
            // === MEDIA CENTRE (must be checked BEFORE web search) ===
            // Route movie/genre/TV queries to media centre, not web search
            if (IsMediaCentreIntent(lower))
            {
                return await HandleMediaCentreQueryAsync(text, lower);
            }
            
            // === WEB SEARCH (check early to avoid misrouting) ===
            if (IsSearchIntent(lower))
            {
                var query = ExtractSearchQuery(text, lower);
                if (!string.IsNullOrEmpty(query))
                {
                    return await WebSearchTool.SearchAsync(query);
                }
            }
            
            // === QUICK LAUNCHER ===
            // Skip if it looks like a web search intent (already handled above)
            if ((lower.StartsWith("find ") || lower.StartsWith("launch ")) && !IsSearchIntent(lower))
            {
                var query = lower.StartsWith("find ") ? text.Substring(5) : text.Substring(7);
                var results = Agent.QuickLauncher.Instance.Search(query.Trim(), 5);
                if (results.Any())
                {
                    if (results.Count == 1)
                        return await Agent.QuickLauncher.Instance.LaunchItemAsync(results[0]);
                    
                    var sb = new System.Text.StringBuilder();
                    sb.AppendLine($"🔍 Found {results.Count} matches:\n");
                    for (int i = 0; i < results.Count; i++)
                        sb.AppendLine($"{i + 1}. {results[i].Name}");
                    sb.AppendLine("\nSay the name to launch it.");
                    return sb.ToString();
                }
            }
            
            // === CONTEXTUAL REFERENCES ===
            // Handle "that", "it", "this" by looking at recent context
            text = ResolveContextualReferences(text, lower);
            lower = text.ToLowerInvariant();
            
            // === DIRECT COMMANDS - No AI needed ===
            
            // === SHELL COMMANDS - Must be checked BEFORE app opening ===
            // Commands like ipconfig, ping, etc. should run as commands, not open apps
            var shellCommands = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "ipconfig", "ping", "systeminfo", "hostname", "whoami", "netstat", "tasklist", 
                "dir", "tree", "ver", "nslookup", "tracert", "arp", "route", "cls", "help",
                "chkdsk", "sfc", "dism", "gpresult", "getmac", "pathping", "netsh", "wmic",
                "diskpart", "bcdedit", "bootrec", "cipher", "compact", "convert", "defrag",
                "driverquery", "fc", "find", "findstr", "fsutil", "icacls", "net", "sc",
                "shutdown", "systeminfo", "takeown", "where", "xcopy", "robocopy", "attrib"
            };
            
            // Check if this is a shell command (direct or with "open/run" prefix)
            var shellCommandResult = await TryHandleShellCommandAsync(text, lower, shellCommands);
            if (shellCommandResult != null)
                return shellCommandResult;
            
            // Kill/Close/Stop/Get rid of
            if (IsKillIntent(lower))
            {
                var target = ExtractKillTarget(text, lower);
                if (!string.IsNullOrEmpty(target))
                {
                    return await KillProcessAsync(target);
                }
            }
            
            // Open/Launch/Start
            if (IsOpenIntent(lower))
            {
                var target = ExtractOpenTarget(text, lower);
                if (!string.IsNullOrEmpty(target))
                {
                    return await OpenAppAsync(target);
                }
            }
            
            // Play music - "play X", "put on X", "spotify X"
            // BUT NOT movie/film/TV requests
            if (IsPlayMusicIntent(lower) && !IsMediaCentreIntent(lower))
            {
                var query = ExtractMusicQuery(text, lower);
                if (!string.IsNullOrEmpty(query))
                {
                    AudioDuckingManager.SkipNextRestore();
                    return await SpotifyTool.PlayAsync(query);
                }
            }
            
            // Media Centre intent already handled above (before web search)
            
            // Media controls
            if (IsMediaControlIntent(lower))
            {
                return await HandleMediaControlAsync(lower);
            }
            
            // Volume
            if (IsVolumeIntent(lower))
            {
                return await HandleVolumeAsync(lower);
            }
            
            // System power
            if (IsPowerIntent(lower))
            {
                return await HandlePowerAsync(lower);
            }
            
            // Weather
            if (lower.Contains("weather"))
            {
                var location = ExtractLocation(text) ?? "Middlesbrough";
                return await WebSearchTool.GetWeatherAsync(location);
            }
            
            // Screenshot
            if (lower.Contains("screenshot") || lower.Contains("screen capture") || lower.Contains("snip"))
            {
                return await SystemTool.TakeScreenshotAsync();
            }
            
            // Process Manager
            if (lower == "process manager" || lower == "processes" || lower == "show processes" ||
                lower == "manage processes" || lower == "task manager" || lower == "startup manager" ||
                lower == "manage startup" || lower == "scheduled tasks" || lower == "manage tasks")
            {
                return OpenProcessManager();
            }
            
            // Scan My Apps - discover installed programs
            if (lower == "scan my apps" || lower == "scan apps" || lower == "discover apps" ||
                lower == "find my apps" || lower == "index apps" || lower == "refresh apps" ||
                lower == "what apps do i have" || lower == "list my apps" || lower == "my programs" ||
                lower == "installed apps" || lower == "installed programs")
            {
                return await ScanInstalledAppsAsync();
            }
            
            // Scan Mode - orbiting scan icons
            if (lower == "scan mode" || lower == "show scans" || lower == "scan orbit" ||
                lower == "security scan mode" || lower == "toggle scan mode" || lower == "scanning mode")
            {
                return ToggleScanMode();
            }
            
            // Security notifications toggle
            if (lower == "notifications on" || lower == "enable notifications" || 
                lower == "turn on notifications" || lower == "security alerts on")
            {
                return ToggleSecurityNotifications(true);
            }
            if (lower == "notifications off" || lower == "disable notifications" || 
                lower == "turn off notifications" || lower == "security alerts off" || lower == "mute alerts")
            {
                return ToggleSecurityNotifications(false);
            }
            
            // Health scan
            if (lower == "health scan" || lower == "system health" || lower == "check health" ||
                lower == "how is my system" || lower == "system status" || lower == "run health check")
            {
                return RunHealthScan();
            }
            
            // System shortcuts (desktop, settings, lock, etc.)
            var shortcutResult = await Agent.SystemShortcuts.TryHandleAsync(lower);
            if (shortcutResult != null)
            {
                return shortcutResult;
            }
            
            // Undo commands
            if (lower == "undo" || lower == "undo that" || lower == "undo last" || 
                lower.StartsWith("undo ") || lower.Contains("undo last action"))
            {
                return await HandleUndoAsync(lower);
            }
            
            // Window management
            if (IsWindowIntent(lower))
            {
                return await HandleWindowCommandAsync(text, lower);
            }
            
            // List windows
            if (lower == "list windows" || lower == "show windows" || lower == "what windows" || lower == "what's open")
            {
                return Agent.WindowManager.ListWindows();
            }
            
            // Single word app names - just open them
            if (AppAliases.ContainsKey(lower))
            {
                return await OpenAppAsync(lower);
            }
            
            return null; // Let AI handle it
        }
        
        private static bool IsWindowIntent(string lower)
        {
            return lower.StartsWith("minimize ") || lower.StartsWith("maximise ") || lower.StartsWith("maximize ") ||
                   lower.StartsWith("focus ") || lower.StartsWith("switch to ") ||
                   lower.StartsWith("snap ") || lower.StartsWith("move ") && lower.Contains(" to ") ||
                   lower.Contains("side by side") || lower.StartsWith("restore ");
        }
        
        private static async Task<string> HandleWindowCommandAsync(string text, string lower)
        {
            // Minimize
            if (lower.StartsWith("minimize "))
            {
                var target = text.Substring(9).Trim();
                return await Agent.WindowManager.MinimizeWindowAsync(target);
            }
            
            // Maximize
            if (lower.StartsWith("maximize ") || lower.StartsWith("maximise "))
            {
                var target = text.Substring(9).Trim();
                return await Agent.WindowManager.MaximizeWindowAsync(target);
            }
            
            // Focus/Switch to
            if (lower.StartsWith("focus "))
            {
                var target = text.Substring(6).Trim();
                return await Agent.WindowManager.FocusWindowAsync(target);
            }
            if (lower.StartsWith("switch to "))
            {
                var target = text.Substring(10).Trim();
                return await Agent.WindowManager.FocusWindowAsync(target);
            }
            
            // Restore
            if (lower.StartsWith("restore "))
            {
                var target = text.Substring(8).Trim();
                return await Agent.WindowManager.RestoreWindowAsync(target);
            }
            
            // Move to position
            if (lower.StartsWith("move ") && lower.Contains(" to "))
            {
                var parts = text.Split(new[] { " to " }, 2, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2)
                {
                    var target = parts[0].Substring(5).Trim(); // Remove "move "
                    var position = Agent.WindowManager.ParsePosition(parts[1]);
                    if (position.HasValue)
                        return await Agent.WindowManager.MoveWindowAsync(target, position.Value);
                }
            }
            
            // Snap side by side
            if (lower.StartsWith("snap ") || lower.Contains("side by side"))
            {
                var match = System.Text.RegularExpressions.Regex.Match(text, @"snap\s+(.+?)\s+and\s+(.+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (match.Success)
                    return await Agent.WindowManager.SnapWindowsAsync(match.Groups[1].Value, match.Groups[2].Value);
                
                match = System.Text.RegularExpressions.Regex.Match(text, @"(.+?)\s+and\s+(.+?)\s+side by side", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (match.Success)
                    return await Agent.WindowManager.SnapWindowsAsync(match.Groups[1].Value, match.Groups[2].Value);
            }
            
            return "❌ Couldn't understand window command";
        }
        
        private static async Task<string> HandleUndoAsync(string lower)
        {
            var safety = Agent.AgentSafetyManager.Instance;
            
            if (!safety.CanUndo)
                return "❌ Nothing to undo";
            
            // Check for "undo all" or "undo X"
            if (lower.Contains("undo all") || lower.Contains("undo everything"))
            {
                return await safety.UndoMultipleAsync(10);
            }
            
            // Check for "undo 3" etc
            var match = System.Text.RegularExpressions.Regex.Match(lower, @"undo\s+(\d+)");
            if (match.Success && int.TryParse(match.Groups[1].Value, out var count))
            {
                return await safety.UndoMultipleAsync(Math.Min(count, 20));
            }
            
            // Single undo
            var (success, message) = await safety.UndoLastAsync();
            return message;
        }
        
        /// <summary>
        /// Handle shell commands like ipconfig, ping, etc. - BEFORE app opening
        /// </summary>
        private static async Task<string?> TryHandleShellCommandAsync(string text, string lower, HashSet<string> shellCommands)
        {
            // Direct command: "ipconfig" or "ipconfig /all"
            foreach (var cmd in shellCommands)
            {
                var cmdLower = cmd.ToLower();
                
                // Exact match or command with arguments
                if (lower == cmdLower || lower.StartsWith(cmdLower + " ") || lower.StartsWith(cmdLower + "/"))
                {
                    return await SystemTool.RunCommandAsync(text.Trim());
                }
                
                // "open ipconfig", "run ipconfig", "show ipconfig", "execute ipconfig"
                var prefixes = new[] { "open ", "run ", "show ", "execute " };
                foreach (var prefix in prefixes)
                {
                    if (lower == prefix + cmdLower)
                    {
                        return await SystemTool.RunCommandAsync(cmd);
                    }
                    if (lower.StartsWith(prefix + cmdLower + " ") || lower.StartsWith(prefix + cmdLower + "/"))
                    {
                        // Extract the full command with args
                        var fullCmd = text.Substring(prefix.Length).Trim();
                        return await SystemTool.RunCommandAsync(fullCmd);
                    }
                }
            }
            
            return null;
        }
        
        #region Intent Detection

        private static bool IsKillIntent(string lower)
        {
            return lower.StartsWith("kill ") || lower.StartsWith("close ") || 
                   lower.StartsWith("stop ") || lower.StartsWith("end ") ||
                   lower.StartsWith("quit ") || lower.StartsWith("exit ") ||
                   lower.Contains("get rid of") || lower.Contains("shut down ") ||
                   lower.Contains("turn off ") || lower.Contains("disable ");
        }

        private static bool IsOpenIntent(string lower)
        {
            return lower.StartsWith("open ") || lower.StartsWith("launch ") ||
                   lower.StartsWith("start ") || lower.StartsWith("run ");
        }

        private static bool IsPlayMusicIntent(string lower)
        {
            // Don't match movie/film/TV requests
            if (IsMediaCentreIntent(lower)) return false;
            return lower.StartsWith("play ") || lower.StartsWith("put on ") ||
                   lower.StartsWith("spotify ") || lower.StartsWith("listen to ") ||
                   lower.Contains("play some ") || lower.Contains("play me ");
        }

        private static bool IsMediaCentreIntent(string lower)
        {
            var mediaWords = new[] { "movie", "movies", "film", "films", "horror", "comedy", "thriller",
                "action movie", "action film", "sci-fi", "scifi", "science fiction",
                "documentary", "documentaries", "drama", "dramas", "romance film", "romantic",
                "western", "westerns", "anime", "cartoon", "animated",
                "tv show", "tv series", "series", "sitcom", "crime", "mystery",
                "best rated", "top rated", "highest rated", "best new",
                "media centre", "media center", "nedia centre", "nedia center",
                "superhero", "marvel", "dc", "war movie", "war film",
                "fantasy", "adventure", "family movie", "family film",
                "psychological", "slasher", "zombie", "vampire", "dystopian",
                "true crime", "biographical", "biopic", "period drama",
                "what to watch", "something to watch", "recommend me",
                "show me something", "find me something to watch" };
            return mediaWords.Any(w => lower.Contains(w));
        }

        private static bool IsMediaControlIntent(string lower)
        {
            return lower == "pause" || lower == "stop" || lower == "resume" ||
                   lower == "next" || lower == "skip" || lower == "previous" ||
                   lower == "back" || lower.Contains("pause music") ||
                   lower.Contains("stop music") || lower.Contains("next song") ||
                   lower.Contains("skip song") || lower.Contains("previous song");
        }

        private static bool IsVolumeIntent(string lower)
        {
            return lower.Contains("volume") || lower == "mute" || lower == "unmute" ||
                   lower.Contains("louder") || lower.Contains("quieter");
        }

        private static bool IsPowerIntent(string lower)
        {
            return lower.Contains("shutdown") || lower.Contains("shut down") ||
                   lower.Contains("restart") || lower.Contains("reboot") ||
                   lower.Contains("sleep") || lower.Contains("hibernate") ||
                   lower.Contains("lock computer") || lower.Contains("lock pc") ||
                   lower.Contains("lock screen");
        }

        private static bool IsSearchIntent(string lower)
        {
            // Direct search commands
            if (lower.StartsWith("search ") || lower.StartsWith("google ") ||
                lower.StartsWith("look up ") || lower.StartsWith("find info "))
                return true;
            
            // "find me X online", "find me some X", "find some X", "find X online"
            if (lower.StartsWith("find me ") || lower.StartsWith("find some "))
                return true;
            
            // "find X online" - must have parentheses for correct precedence
            if (lower.StartsWith("find ") && lower.Contains(" online"))
                return true;
            
            // "look for X online", "search for X"
            if (lower.StartsWith("look for ") || lower.StartsWith("search for "))
                return true;
            
            // "what is X", "who is X", "where is X" - questions that need web search
            if ((lower.StartsWith("what is ") || lower.StartsWith("who is ") || 
                 lower.StartsWith("where is ") || lower.StartsWith("when did ") ||
                 lower.StartsWith("how to ") || lower.StartsWith("how do ")) && 
                !lower.Contains("my ") && !lower.Contains("this "))
                return true;
            
            return false;
        }

        #endregion

        #region Target Extraction

        private static string ExtractKillTarget(string text, string lower)
        {
            // Remove kill/close/stop prefix
            var patterns = new[] { "get rid of ", "turn off ", "shut down ", "kill ", "close ", "stop ", "end ", "quit ", "exit ", "disable " };
            var target = text;
            foreach (var p in patterns)
            {
                if (lower.StartsWith(p))
                {
                    target = text.Substring(p.Length).Trim();
                    break;
                }
                var idx = lower.IndexOf(p);
                if (idx >= 0)
                {
                    target = text.Substring(idx + p.Length).Trim();
                    break;
                }
            }
            
            // Remove "the" prefix
            if (target.StartsWith("the ", StringComparison.OrdinalIgnoreCase))
                target = target.Substring(4);
            
            // Store for context
            _lastMentionedProcess = target;
            _lastAction = "kill";
            
            return target;
        }

        private static string ExtractOpenTarget(string text, string lower)
        {
            var patterns = new[] { "open ", "launch ", "start ", "run " };
            var target = text;
            foreach (var p in patterns)
            {
                if (lower.StartsWith(p))
                {
                    target = text.Substring(p.Length).Trim();
                    break;
                }
            }
            
            _lastMentionedApp = target;
            _lastAction = "open";
            
            return target;
        }

        private static string ExtractMusicQuery(string text, string lower)
        {
            var patterns = new[] { "play me ", "play some ", "put on some ", "listen to ", "play ", "put on ", "spotify " };
            var query = text;
            foreach (var p in patterns)
            {
                if (lower.StartsWith(p))
                {
                    query = text.Substring(p.Length).Trim();
                    break;
                }
                var idx = lower.IndexOf(p);
                if (idx >= 0)
                {
                    query = text.Substring(idx + p.Length).Trim();
                    break;
                }
            }
            
            // Remove "on spotify" suffix
            query = Regex.Replace(query, @"\s+on\s+spotify.*$", "", RegexOptions.IgnoreCase);
            
            return query;
        }

        private static string ExtractSearchQuery(string text, string lower)
        {
            var patterns = new[] { 
                "search for ", "search ", "google ", "look up ", "find info ",
                "find me some ", "find me ", "find some ", "look for ",
                "what is ", "who is ", "where is ", "when did ", "how to ", "how do "
            };
            var query = text;
            foreach (var p in patterns)
            {
                if (lower.StartsWith(p))
                {
                    query = text.Substring(p.Length).Trim();
                    break;
                }
            }
            
            // Remove trailing "online" if present
            if (query.EndsWith(" online", StringComparison.OrdinalIgnoreCase))
                query = query.Substring(0, query.Length - 7).Trim();
            
            return query;
        }

        private static string? ExtractLocation(string text)
        {
            var match = Regex.Match(text, @"weather\s+(?:in|for|at)\s+(.+)", RegexOptions.IgnoreCase);
            return match.Success ? match.Groups[1].Value.Trim() : null;
        }

        #endregion

        #region Context Resolution

        private static string ResolveContextualReferences(string text, string lower)
        {
            // "that overlay" -> "nvidia overlay" if we recently talked about it
            if (lower.Contains("that ") || lower.Contains("the ") || lower.Contains("this "))
            {
                if (lower.Contains("overlay") && string.IsNullOrEmpty(_lastMentionedProcess))
                {
                    // Default overlay is NVIDIA
                    return text.Replace("that overlay", "nvidia overlay", StringComparison.OrdinalIgnoreCase)
                               .Replace("the overlay", "nvidia overlay", StringComparison.OrdinalIgnoreCase)
                               .Replace("this overlay", "nvidia overlay", StringComparison.OrdinalIgnoreCase);
                }
                
                // "close that" -> close last mentioned app/process
                if ((lower == "close that" || lower == "kill that" || lower == "stop that") && !string.IsNullOrEmpty(_lastMentionedProcess))
                {
                    return $"close {_lastMentionedProcess}";
                }
                
                // "open that" -> open last mentioned app
                if ((lower == "open that" || lower == "start that") && !string.IsNullOrEmpty(_lastMentionedApp))
                {
                    return $"open {_lastMentionedApp}";
                }
            }
            
            return text;
        }

        #endregion

        #region Actions

        private static async Task<string> KillProcessAsync(string target)
        {
            var lower = target.ToLowerInvariant();
            
            // Special handling for Atlas overlay
            if (lower.Contains("overlay"))
            {
                // Try to kill Atlas overlay first
                if (_inAppAssistant != null)
                {
                    if (_inAppAssistant.IsOverlayVisible)
                    {
                        _inAppAssistant.DisableOverlay(); // Completely close it
                        Agent.ProactiveAssistant.Instance.RecordAction("close", "atlas overlay", true);
                        return "✓ Killed Atlas overlay";
                    }
                    
                    // If specifically asking for Atlas overlay and it's not visible
                    if (lower.Contains("atlas"))
                    {
                        return "❌ Atlas overlay wasn't running";
                    }
                }
                
                // Otherwise try external overlays (NVIDIA, etc.)
            }
            
            var result = await Agent.EnhancedAppControl.CloseAppAsync(target);
            
            // Track for proactive learning
            Agent.ProactiveAssistant.Instance.RecordAction("close", target, result.StartsWith("✓"));
            
            return result;
        }

        private static async Task<string> HandleMediaCentreQueryAsync(string text, string lower)
        {
            try
            {
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    // Navigate to media centre tab
                    foreach (System.Windows.Window w in System.Windows.Application.Current.Windows)
                    {
                        if (w is AtlasAI.CommandCenterWindow ccw && ccw.IsLoaded)
                        {
                            ccw.NavigateToTab("AI MEDIA CENTRE", "Media");
                            break;
                        }
                    }

                    // Inject query into media centre AI
                    var vm = AtlasAI.Views.ViewModels.MediaCentreViewModel.Instance;
                    if (vm != null)
                    {
                        vm.MediaAiCommandText = text;
                        // Execute the SendMediaAi command
                        if (vm.SendMediaAiCommand?.CanExecute(null) == true)
                            vm.SendMediaAiCommand.Execute(null);
                    }
                });
                return "✓ Opening Media Centre";
            }
            catch (Exception ex)
            {
                return $"❌ Error opening Media Centre: {ex.Message}";
            }
        }

        private static string OpenProcessManager()
        {
            try
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    var window = new UI.ProcessManagerWindow();
                    window.Show();
                });
                return "✓ Opening Process Manager";
            }
            catch (System.Exception ex)
            {
                return $"❌ Error: {ex.Message}";
            }
        }

        private static string ToggleScanMode()
        {
            try
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    var mainWindow = System.Windows.Application.Current.MainWindow as AtlasAI.ChatWindow;
                    mainWindow?.ToggleScanOrbit();
                });
                return "✓ Toggling Scan Mode";
            }
            catch (System.Exception ex)
            {
                return $"❌ Error: {ex.Message}";
            }
        }

        private static string ToggleSecurityNotifications(bool enabled)
        {
            try
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    var mainWindow = System.Windows.Application.Current.MainWindow as AtlasAI.ChatWindow;
                    mainWindow?.ToggleSecurityNotifications(enabled);
                });
                return enabled ? "✓ Security notifications enabled" : "✓ Security notifications disabled";
            }
            catch (System.Exception ex)
            {
                return $"❌ Error: {ex.Message}";
            }
        }

        private static string RunHealthScan()
        {
            try
            {
                System.Windows.Application.Current.Dispatcher.Invoke(async () =>
                {
                    var mainWindow = System.Windows.Application.Current.MainWindow as AtlasAI.ChatWindow;
                    if (mainWindow != null)
                        await mainWindow.RunManualHealthScanAsync();
                });
                return "✓ Running health scan...";
            }
            catch (System.Exception ex)
            {
                return $"❌ Error: {ex.Message}";
            }
        }

        private static async Task<string> ScanInstalledAppsAsync()
        {
            try
            {
                await SystemControl.InstalledAppsManager.Instance.ScanAllAppsAsync();
                var count = SystemControl.InstalledAppsManager.Instance.AppCount;
                var apps = SystemControl.InstalledAppsManager.Instance.GetAllApps();
                
                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"✓ Scanned {count} installed applications!\n");
                sb.AppendLine("**Some apps I found:**");
                
                // Show top 15 apps
                var topApps = apps.Take(15).Select(a => a.Name).OrderBy(n => n);
                foreach (var app in topApps)
                {
                    sb.AppendLine($"  • {app}");
                }
                
                if (count > 15)
                    sb.AppendLine($"\n...and {count - 15} more.");
                
                sb.AppendLine("\n💡 Now you can say \"open [app name]\" to launch any of them!");
                
                return sb.ToString();
            }
            catch (System.Exception ex)
            {
                return $"❌ Error scanning apps: {ex.Message}";
            }
        }

        private static async Task<string> OpenAppAsync(string target)
        {
            var result = await Agent.EnhancedAppControl.OpenAppAsync(target);
            
            // Track for proactive learning
            Agent.ProactiveAssistant.Instance.RecordAction("open", target, result.StartsWith("✓"));
            
            return result;
        }

        private static async Task<string?> HandleMediaControlAsync(string lower)
        {
            if (lower.Contains("pause") || lower == "stop")
                return await SpotifyTool.ControlPlaybackAsync("pause");
            if (lower.Contains("resume") || lower == "play")
                return await SpotifyTool.ControlPlaybackAsync("play");
            if (lower.Contains("next") || lower.Contains("skip"))
                return await SpotifyTool.ControlPlaybackAsync("next");
            if (lower.Contains("previous") || lower.Contains("back"))
                return await SpotifyTool.ControlPlaybackAsync("previous");
            return null;
        }

        private static async Task<string?> HandleVolumeAsync(string lower)
        {
            if (lower.Contains("up") || lower.Contains("louder"))
                return await SystemTool.SetVolumeAsync(80);
            if (lower.Contains("down") || lower.Contains("quieter"))
                return await SystemTool.SetVolumeAsync(30);
            if (lower.Contains("mute"))
                return await SystemTool.ToggleMuteAsync();
            return null;
        }

        private static async Task<string?> HandlePowerAsync(string lower)
        {
            if (lower.Contains("shutdown") || lower.Contains("shut down"))
                return await SystemTool.ShutdownAsync(60);
            if (lower.Contains("restart") || lower.Contains("reboot"))
                return await SystemTool.RestartAsync(60);
            if (lower.Contains("sleep") || lower.Contains("hibernate"))
                return await SystemTool.SleepAsync();
            if (lower.Contains("lock"))
                return await SystemTool.LockComputerAsync();
            return null;
        }

        #endregion
    }
}
