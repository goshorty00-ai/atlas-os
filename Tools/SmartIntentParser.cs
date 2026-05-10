using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using AtlasAI.Core;
using AtlasAI.Settings;

namespace AtlasAI.Tools
{
    /// <summary>
    /// AI-powered intent parser that understands natural language commands
    /// Uses Claude 3.5 Sonnet for superior intent understanding
    /// </summary>
    public static class SmartIntentParser
    {
        private static readonly HttpClient httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        private static string? _claudeApiKey;
        private static string? _openaiApiKey;

        public enum IntentType
        {
            // Media & Entertainment
            PlayMusic,          // Play song/music on any platform
            PlayVideo,          // Play video on YouTube
            MediaControl,       // Pause, resume, next, previous, stop
            
            // Apps & Navigation
            OpenApp,            // Open an application
            CloseApp,           // Close/kill an application
            Navigation,         // Open URL, go to website
            
            // Information & Search
            WebSearch,          // Search the web
            Weather,            // Get weather info
            GetInfo,            // Get system info, battery, disk, network, processes
            
            // System Control
            SystemControl,      // Volume, mute, brightness
            PowerControl,       // Shutdown, restart, sleep, lock, hibernate
            
            // File Operations
            FileOperation,      // Create, delete, move, copy, rename files/folders
            OrganizeFiles,      // Sort files by type into folders
            ConsolidateFiles,   // Flatten subfolders into one folder
            FindFiles,          // Search for files
            ListFiles,          // List directory contents
            
            // Productivity
            SetReminder,        // Set a reminder/alarm/timer
            TakeScreenshot,     // Capture screen
            TypeText,           // Type text (simulate keyboard)
            PressKey,           // Press keyboard shortcut
            OpenSettings,       // Open Windows settings
            EmptyTrash,         // Empty recycle bin
            RunCommand,         // Run shell command
            
            // Communication (future)
            SendMessage,        // Send email/message
            
            Unknown             // Let AI handle conversationally
        }

        public class ParsedIntent
        {
            public IntentType Intent { get; set; } = IntentType.Unknown;
            public string? Platform { get; set; }      // spotify, youtube, soundcloud, etc.
            public string? Query { get; set; }         // song name, search query, app name
            public string? Target { get; set; }        // file path, URL, contact
            public string? Action { get; set; }        // play, pause, next, open, close
            public Dictionary<string, string> Parameters { get; set; } = new();
            public float Confidence { get; set; } = 0;
            public string? RawResponse { get; set; }
        }

        /// <summary>
        /// Parse user message using AI to understand intent
        /// </summary>
        public static async Task<ParsedIntent> ParseIntentAsync(string userMessage)
        {
            LoadApiKeys();
            
            // Try Claude first (better understanding), fall back to OpenAI
            if (!string.IsNullOrEmpty(_claudeApiKey))
            {
                try
                {
                    var prompt = BuildIntentPrompt(userMessage);
                    var response = await CallClaudeAsync(prompt);
                    
                    if (!string.IsNullOrEmpty(response))
                    {
                        var parsed = ParseGptResponse(response);
                        parsed.RawResponse = response;
                        Debug.WriteLine($"[SmartIntent] Claude parsed: {parsed.Intent} - {parsed.Query} on {parsed.Platform}");
                        return parsed;
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[SmartIntent] Claude parse error: {ex.Message}");
                }
            }
            
            // Fallback to OpenAI if Claude fails
            if (!string.IsNullOrEmpty(_openaiApiKey))
            {
                try
                {
                    var prompt = BuildIntentPrompt(userMessage);
                    var response = await CallGptAsync(prompt);
                    
                    if (!string.IsNullOrEmpty(response))
                    {
                        var parsed = ParseGptResponse(response);
                        parsed.RawResponse = response;
                        Debug.WriteLine($"[SmartIntent] GPT parsed: {parsed.Intent} - {parsed.Query} on {parsed.Platform}");
                        return parsed;
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[SmartIntent] GPT parse error: {ex.Message}");
                }
            }

            Debug.WriteLine("[SmartIntent] No API keys, falling back to keyword matching");
            return FallbackParse(userMessage);
        }

        private static string BuildIntentPrompt(string userMessage)
        {
            return $@"You are an AI assistant intent parser. Analyze the user's command and extract the intent. Respond ONLY with valid JSON.

User command: ""{userMessage}""

Available intents:
- play_music: Play songs/music (Spotify, YouTube Music, SoundCloud, Apple Music, Amazon Music, Deezer, Tidal, Pandora)
- play_video: Watch videos on YouTube
- media_control: Pause, resume, next track, previous track, stop music
- open_app: Open/launch an application (Chrome, Notepad, Discord, Steam, VS Code, etc.)
- close_app: Close/kill an application or process
- navigation: Open a URL/website in browser
- web_search: Search the web for information
- weather: Get weather forecast
- get_info: Get system info (battery, disk space, memory, network, running processes)
- system_control: Volume up/down, mute/unmute
- power_control: Shutdown, restart, sleep, lock, hibernate computer
- file_operation: Create, delete, move, copy, rename files or folders
- organize_files: Sort/organize files by type into folders (Images, Documents, Videos, etc.)
- consolidate_files: Flatten subfolders - move all files from subfolders into one folder
- find_files: Search for files by name
- list_files: List contents of a directory
- set_reminder: Set a reminder, alarm, or timer
- take_screenshot: Capture the screen
- type_text: Type text (simulate keyboard input)
- press_key: Press keyboard shortcut (Ctrl+C, Alt+Tab, etc.)
- open_settings: Open Windows settings (wifi, bluetooth, display, sound, etc.)
- empty_trash: Empty the recycle bin
- run_command: Execute a shell/cmd command
- unknown: General conversation, questions, or unclear intent

Respond with this JSON format:
{{
  ""intent"": ""one of the intents above"",
  ""platform"": ""spotify/youtube/soundcloud/apple_music/amazon_music/deezer/tidal/pandora/youtube_music or null"",
  ""query"": ""song name, search query, app name, file name, or main subject"",
  ""action"": ""specific action like: play/pause/next/previous/stop/open/close/create/delete/move/copy/rename/volume_up/volume_down/mute/shutdown/restart/sleep/lock or null"",
  ""target"": ""file path, URL, destination folder, or specific target"",
  ""parameters"": {{""key"": ""value""}},
  ""confidence"": 0.0 to 1.0
}}

Examples:
- ""put on some ed sheeran"" -> intent: play_music, query: ""ed sheeran"", confidence: 0.95
- ""play shape of you on spotify"" -> intent: play_music, query: ""shape of you"", platform: ""spotify"", confidence: 0.98
- ""watch cat videos"" -> intent: play_video, query: ""cat videos"", platform: ""youtube"", confidence: 0.95
- ""pause the music"" -> intent: media_control, action: ""pause"", confidence: 0.98
- ""skip this song"" -> intent: media_control, action: ""next"", confidence: 0.95
- ""open chrome"" -> intent: open_app, query: ""chrome"", confidence: 0.98
- ""close discord"" -> intent: close_app, query: ""discord"", confidence: 0.95
- ""go to google.com"" -> intent: navigation, target: ""google.com"", confidence: 0.98
- ""search for pizza places near me"" -> intent: web_search, query: ""pizza places near me"", confidence: 0.95
- ""what's the weather in london"" -> intent: weather, query: ""london"", confidence: 0.95
- ""how much battery do i have"" -> intent: get_info, query: ""battery"", confidence: 0.95
- ""show running processes"" -> intent: get_info, query: ""processes"", confidence: 0.95
- ""turn the volume up"" -> intent: system_control, action: ""volume_up"", confidence: 0.95
- ""mute"" -> intent: system_control, action: ""mute"", confidence: 0.98
- ""shut down the computer"" -> intent: power_control, action: ""shutdown"", confidence: 0.95
- ""lock my pc"" -> intent: power_control, action: ""lock"", confidence: 0.95
- ""create a folder called Projects"" -> intent: file_operation, action: ""create"", query: ""Projects"", confidence: 0.95
- ""delete the file test.txt"" -> intent: file_operation, action: ""delete"", target: ""test.txt"", confidence: 0.95
- ""move report.pdf to Documents"" -> intent: file_operation, action: ""move"", query: ""report.pdf"", target: ""Documents"", confidence: 0.95
- ""organize my downloads folder"" -> intent: organize_files, target: ""downloads"", confidence: 0.95
- ""sort files on desktop"" -> intent: organize_files, target: ""desktop"", confidence: 0.95
- ""organize D:\Music"" -> intent: organize_files, target: ""D:\Music"", confidence: 0.98
- ""organize my music folder on D drive"" -> intent: organize_files, target: ""D:\Music"", confidence: 0.95
- ""sort files in E:\Downloads"" -> intent: organize_files, target: ""E:\Downloads"", confidence: 0.98
- ""put all songs in one folder"" -> intent: consolidate_files, confidence: 0.90
- ""flatten the subfolders"" -> intent: consolidate_files, confidence: 0.95
- ""find files named report"" -> intent: find_files, query: ""report"", confidence: 0.95
- ""what's in my documents folder"" -> intent: list_files, target: ""documents"", confidence: 0.95
- ""remind me in 10 minutes to call mom"" -> intent: set_reminder, query: ""call mom"", parameters: {{""minutes"": 10}}, confidence: 0.95
- ""take a screenshot"" -> intent: take_screenshot, confidence: 0.98
- ""type hello world"" -> intent: type_text, query: ""hello world"", confidence: 0.95
- ""press ctrl+c"" -> intent: press_key, query: ""ctrl+c"", confidence: 0.95
- ""open wifi settings"" -> intent: open_settings, query: ""wifi"", confidence: 0.95
- ""empty the recycle bin"" -> intent: empty_trash, confidence: 0.98
- ""run ipconfig"" -> intent: run_command, query: ""ipconfig"", confidence: 0.95
- ""how are you"" -> intent: unknown, confidence: 0.3
- ""tell me a joke"" -> intent: unknown, confidence: 0.2";
        }

        private static async Task<string?> CallGptAsync(string prompt)
        {
            var requestBody = new
            {
                model = "gpt-5.2", // Latest GPT model for best intent understanding
                messages = new[]
                {
                    new { role = "system", content = "You are an intent parser. Respond only with valid JSON." },
                    new { role = "user", content = prompt }
                },
                max_tokens = 200,
                temperature = 0.1
            };

            var json = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions");
            request.Headers.Add("Authorization", $"Bearer {_openaiApiKey}");
            request.Content = content;

            var response = await httpClient.SendAsync(request);
            var responseText = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                Debug.WriteLine($"[SmartIntent] OpenAI API error: {responseText}");
                return null;
            }

            using var doc = JsonDocument.Parse(responseText);
            var messageContent = doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();

            return messageContent;
        }
        
        /// <summary>
        /// Call Claude 4.5 for superior intent understanding
        /// </summary>
        private static async Task<string?> CallClaudeAsync(string prompt)
        {
            var requestBody = new
            {
                model = "claude-3-5-sonnet-20241022",
                max_tokens = 200,
                messages = new[]
                {
                    new { role = "user", content = prompt }
                },
                system = "You are an intent parser. Respond only with valid JSON. Be very accurate at understanding what the user wants even from casual/informal language."
            };

            var json = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var request = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages");
            request.Headers.Add("x-api-key", _claudeApiKey);
            request.Headers.Add("anthropic-version", "2023-06-01");
            request.Content = content;

            var response = await httpClient.SendAsync(request);
            var responseText = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                Debug.WriteLine($"[SmartIntent] Claude API error: {responseText}");
                return null;
            }

            using var doc = JsonDocument.Parse(responseText);
            var messageContent = doc.RootElement
                .GetProperty("content")[0]
                .GetProperty("text")
                .GetString();

            return messageContent;
        }

        private static ParsedIntent ParseGptResponse(string response)
        {
            var result = new ParsedIntent();
            
            try
            {
                // Clean up response - remove markdown code blocks if present
                response = response.Trim();
                if (response.StartsWith("```"))
                {
                    var lines = response.Split('\n');
                    var jsonLines = new List<string>();
                    bool inJson = false;
                    foreach (var line in lines)
                    {
                        if (line.StartsWith("```") && !inJson) { inJson = true; continue; }
                        if (line.StartsWith("```") && inJson) break;
                        if (inJson) jsonLines.Add(line);
                    }
                    response = string.Join("\n", jsonLines);
                }

                using var doc = JsonDocument.Parse(response);
                var root = doc.RootElement;

                if (root.TryGetProperty("intent", out var intentProp))
                {
                    result.Intent = ParseIntentType(intentProp.GetString() ?? "");
                }

                if (root.TryGetProperty("platform", out var platformProp) && platformProp.ValueKind != JsonValueKind.Null)
                {
                    result.Platform = platformProp.GetString();
                }

                if (root.TryGetProperty("query", out var queryProp) && queryProp.ValueKind != JsonValueKind.Null)
                {
                    result.Query = queryProp.GetString();
                }

                if (root.TryGetProperty("action", out var actionProp) && actionProp.ValueKind != JsonValueKind.Null)
                {
                    result.Action = actionProp.GetString();
                }

                if (root.TryGetProperty("target", out var targetProp) && targetProp.ValueKind != JsonValueKind.Null)
                {
                    result.Target = targetProp.GetString();
                }

                if (root.TryGetProperty("confidence", out var confProp))
                {
                    result.Confidence = confProp.GetSingle();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SmartIntent] Parse error: {ex.Message}");
            }

            return result;
        }

        private static IntentType ParseIntentType(string intent)
        {
            return intent.ToLower().Replace("_", "") switch
            {
                // Media
                "playmusic" => IntentType.PlayMusic,
                "playvideo" => IntentType.PlayVideo,
                "mediacontrol" => IntentType.MediaControl,
                
                // Apps
                "openapp" => IntentType.OpenApp,
                "closeapp" => IntentType.CloseApp,
                "navigation" => IntentType.Navigation,
                
                // Information
                "websearch" => IntentType.WebSearch,
                "weather" => IntentType.Weather,
                "getinfo" => IntentType.GetInfo,
                
                // System
                "systemcontrol" => IntentType.SystemControl,
                "powercontrol" => IntentType.PowerControl,
                
                // Files
                "fileoperation" => IntentType.FileOperation,
                "organizefiles" => IntentType.OrganizeFiles,
                "consolidatefiles" => IntentType.ConsolidateFiles,
                "findfiles" => IntentType.FindFiles,
                "listfiles" => IntentType.ListFiles,
                
                // Productivity
                "setreminder" => IntentType.SetReminder,
                "takescreenshot" => IntentType.TakeScreenshot,
                "typetext" => IntentType.TypeText,
                "presskey" => IntentType.PressKey,
                "opensettings" => IntentType.OpenSettings,
                "emptytrash" => IntentType.EmptyTrash,
                "runcommand" => IntentType.RunCommand,
                
                // Communication
                "sendmessage" => IntentType.SendMessage,
                
                _ => IntentType.Unknown
            };
        }

        /// <summary>
        /// Fallback keyword-based parsing when AI is unavailable
        /// </summary>
        private static ParsedIntent FallbackParse(string message)
        {
            var msg = message.ToLower();
            var result = new ParsedIntent { Confidence = 0.5f };

            // Media control (check first - specific actions)
            if (ContainsAny(msg, "pause", "stop music", "stop playing"))
            {
                result.Intent = IntentType.MediaControl;
                result.Action = "pause";
                return result;
            }
            if (ContainsAny(msg, "resume", "unpause", "continue playing"))
            {
                result.Intent = IntentType.MediaControl;
                result.Action = "play";
                return result;
            }
            if (ContainsAny(msg, "next song", "skip", "next track"))
            {
                result.Intent = IntentType.MediaControl;
                result.Action = "next";
                return result;
            }
            if (ContainsAny(msg, "previous song", "last song", "go back", "previous track"))
            {
                result.Intent = IntentType.MediaControl;
                result.Action = "previous";
                return result;
            }

            // Music/Video detection
            if (ContainsAny(msg, "play ", "put on ", "listen to "))
            {
                if (ContainsAny(msg, "video", "youtube", "watch"))
                {
                    result.Intent = IntentType.PlayVideo;
                    result.Platform = "youtube";
                }
                else
                {
                    result.Intent = IntentType.PlayMusic;
                    // Detect platform
                    if (msg.Contains("spotify")) result.Platform = "spotify";
                    else if (msg.Contains("soundcloud")) result.Platform = "soundcloud";
                    else if (msg.Contains("youtube music")) result.Platform = "youtube_music";
                    else if (msg.Contains("youtube")) result.Platform = "youtube";
                    else if (msg.Contains("apple") || msg.Contains("itunes")) result.Platform = "apple_music";
                }
                result.Query = ExtractQuery(message, new[] { "play", "put on", "listen to" });
                return result;
            }

            // Power control
            if (ContainsAny(msg, "shutdown", "shut down", "turn off computer", "power off"))
            {
                result.Intent = IntentType.PowerControl;
                result.Action = "shutdown";
                return result;
            }
            if (ContainsAny(msg, "restart", "reboot"))
            {
                result.Intent = IntentType.PowerControl;
                result.Action = "restart";
                return result;
            }
            if (ContainsAny(msg, "sleep", "hibernate", "standby"))
            {
                result.Intent = IntentType.PowerControl;
                result.Action = "sleep";
                return result;
            }
            if (ContainsAny(msg, "lock computer", "lock pc", "lock screen", "lock my"))
            {
                result.Intent = IntentType.PowerControl;
                result.Action = "lock";
                return result;
            }

            // System control (volume, mute)
            if (ContainsAny(msg, "volume", "mute", "unmute"))
            {
                result.Intent = IntentType.SystemControl;
                if (ContainsAny(msg, "up", "increase", "louder")) result.Action = "volume_up";
                else if (ContainsAny(msg, "down", "decrease", "quieter")) result.Action = "volume_down";
                else if (ContainsAny(msg, "mute", "unmute")) result.Action = "mute";
                return result;
            }

            // Weather
            if (ContainsAny(msg, "weather", "temperature", "forecast"))
            {
                result.Intent = IntentType.Weather;
                result.Query = ExtractLocation(message);
                return result;
            }

            // Close app
            if (ContainsAny(msg, "close ", "kill ", "end task", "stop process"))
            {
                result.Intent = IntentType.CloseApp;
                result.Query = ExtractQuery(message, new[] { "close", "kill", "end task", "stop process" });
                return result;
            }

            // Open app
            if (ContainsAny(msg, "open ", "launch ", "start ", "run "))
            {
                // Check if it's a URL
                if (ContainsAny(msg, ".com", ".org", ".net", "http", "www"))
                {
                    result.Intent = IntentType.Navigation;
                    result.Target = ExtractQuery(message, new[] { "open", "go to", "navigate to" });
                }
                else if (ContainsAny(msg, "settings"))
                {
                    result.Intent = IntentType.OpenSettings;
                    if (msg.Contains("wifi") || msg.Contains("network")) result.Query = "wifi";
                    else if (msg.Contains("bluetooth")) result.Query = "bluetooth";
                    else if (msg.Contains("display")) result.Query = "display";
                    else if (msg.Contains("sound")) result.Query = "sound";
                }
                else
                {
                    result.Intent = IntentType.OpenApp;
                    result.Query = ExtractQuery(message, new[] { "open", "launch", "start", "run" });
                }
                return result;
            }

            // File operations
            if (ContainsAny(msg, "organize", "sort files", "tidy up", "clean up"))
            {
                result.Intent = IntentType.OrganizeFiles;
                result.Target = ExtractFolderName(msg);
                return result;
            }
            if (ContainsAny(msg, "consolidate", "flatten", "one folder", "1 folder", "single folder"))
            {
                result.Intent = IntentType.ConsolidateFiles;
                result.Target = ExtractFolderName(msg);
                return result;
            }
            if (ContainsAny(msg, "create folder", "make folder", "new folder"))
            {
                result.Intent = IntentType.FileOperation;
                result.Action = "create";
                result.Query = ExtractQuery(message, new[] { "create folder", "make folder", "new folder" });
                return result;
            }
            if (ContainsAny(msg, "delete ", "remove ") && !msg.Contains("how"))
            {
                result.Intent = IntentType.FileOperation;
                result.Action = "delete";
                result.Target = ExtractQuery(message, new[] { "delete", "remove" });
                return result;
            }
            if (ContainsAny(msg, "find file", "search file", "locate ", "where is"))
            {
                result.Intent = IntentType.FindFiles;
                result.Query = ExtractQuery(message, new[] { "find file", "search file", "locate", "where is" });
                return result;
            }
            if (ContainsAny(msg, "list files", "show files", "what files", "what's in"))
            {
                result.Intent = IntentType.ListFiles;
                result.Target = ExtractFolderName(msg);
                return result;
            }

            // Web search
            if (ContainsAny(msg, "search ", "google ", "look up ", "find info", "what is", "who is", "how to"))
            {
                result.Intent = IntentType.WebSearch;
                result.Query = ExtractQuery(message, new[] { "search for", "search", "google", "look up", "find" });
                return result;
            }

            // Screenshot
            if (ContainsAny(msg, "screenshot", "capture screen", "screen capture"))
            {
                result.Intent = IntentType.TakeScreenshot;
                return result;
            }

            // Empty trash
            if (ContainsAny(msg, "empty recycle", "empty trash", "clear recycle"))
            {
                result.Intent = IntentType.EmptyTrash;
                return result;
            }

            // Get info
            if (ContainsAny(msg, "battery", "power status", "charge level"))
            {
                result.Intent = IntentType.GetInfo;
                result.Query = "battery";
                return result;
            }
            if (ContainsAny(msg, "disk space", "storage", "free space", "hard drive"))
            {
                result.Intent = IntentType.GetInfo;
                result.Query = "disk";
                return result;
            }
            if (ContainsAny(msg, "running processes", "task list", "what's running"))
            {
                result.Intent = IntentType.GetInfo;
                result.Query = "processes";
                return result;
            }
            if (ContainsAny(msg, "network info", "ip address", "my ip"))
            {
                result.Intent = IntentType.GetInfo;
                result.Query = "network";
                return result;
            }
            if (ContainsAny(msg, "system info", "computer info", "pc info"))
            {
                result.Intent = IntentType.GetInfo;
                result.Query = "system";
                return result;
            }

            // Type text
            if (msg.StartsWith("type "))
            {
                result.Intent = IntentType.TypeText;
                result.Query = message.Substring(5).Trim();
                return result;
            }

            // Press key - but NOT if it's a troubleshooting request about keys not working
            if (ContainsAny(msg, "press ", "hit ") && 
                !ContainsAny(msg, "not working", "isn't working", "isnt working", "doesn't work", "doesnt work", 
                             "broken", "fix ", "help ", "problem", "issue", "trouble"))
            {
                result.Intent = IntentType.PressKey;
                result.Query = ExtractQuery(message, new[] { "press", "hit" });
                return result;
            }

            // Run command
            if (msg.StartsWith("run ") || msg.StartsWith("execute ") || msg.StartsWith("cmd "))
            {
                result.Intent = IntentType.RunCommand;
                result.Query = ExtractQuery(message, new[] { "run", "execute", "cmd" });
                return result;
            }

            return result;
        }

        private static string? ExtractFolderName(string msg)
        {
            if (msg.Contains("desktop")) return "desktop";
            if (msg.Contains("documents")) return "documents";
            if (msg.Contains("downloads")) return "downloads";
            if (msg.Contains("pictures") || msg.Contains("photos")) return "pictures";
            if (msg.Contains("music")) return "music";
            if (msg.Contains("videos")) return "videos";
            return null;
        }

        private static string ExtractQuery(string message, string[] prefixes)
        {
            var msg = message;
            foreach (var prefix in prefixes)
            {
                var idx = msg.ToLower().IndexOf(prefix);
                if (idx >= 0)
                {
                    msg = msg.Substring(idx + prefix.Length).Trim();
                    break;
                }
            }
            // Remove platform names from end
            msg = System.Text.RegularExpressions.Regex.Replace(msg, 
                @"\s+(on|in|using)\s+(spotify|youtube|soundcloud|apple music|itunes|amazon|deezer|tidal|pandora).*$", 
                "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            return msg.Trim();
        }

        private static string? ExtractLocation(string message)
        {
            var match = System.Text.RegularExpressions.Regex.Match(message, 
                @"(?:weather|temperature|forecast)\s+(?:in|for|at)\s+(.+)", 
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            return match.Success ? match.Groups[1].Value.Trim() : null;
        }

        private static bool ContainsAny(string text, params string[] keywords)
        {
            foreach (var keyword in keywords)
                if (text.Contains(keyword)) return true;
            return false;
        }

        private static void LoadApiKeys()
        {
            if (_claudeApiKey != null || _openaiApiKey != null) return;
            
            try
            {
                if (SettingsStore.TryGetAiProviderKey("claude", out var claudeKey))
                {
                    _claudeApiKey = claudeKey;
                    Debug.WriteLine("[SmartIntent] Claude API key loaded");
                }

                if (SettingsStore.TryGetAiProviderKey("openai", out var openaiKey))
                {
                    _openaiApiKey = openaiKey;
                    Debug.WriteLine("[SmartIntent] OpenAI API key loaded");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SmartIntent] Error loading API keys: {ex.Message}");
            }
        }

        /// <summary>
        /// Execute the parsed intent
        /// </summary>
        public static async Task<string?> ExecuteIntentAsync(ParsedIntent intent)
        {
            Debug.WriteLine($"[SmartIntent] Executing: {intent.Intent} - Query: {intent.Query} - Action: {intent.Action}");
            
            switch (intent.Intent)
            {
                // ==================== MEDIA ====================
                case IntentType.PlayMusic:
                    if (!string.IsNullOrEmpty(intent.Query))
                    {
                        var platform = ParsePlatform(intent.Platform);
                        return await MediaPlayerTool.PlayAsync(intent.Query, platform);
                    }
                    break;

                case IntentType.PlayVideo:
                    if (!string.IsNullOrEmpty(intent.Query))
                    {
                        return await MediaPlayerTool.PlayAsync(intent.Query, MediaPlayerTool.Platform.YouTube);
                    }
                    break;

                case IntentType.MediaControl:
                    return await ExecuteMediaControlAsync(intent.Action);

                // ==================== APPS ====================
                case IntentType.OpenApp:
                    if (!string.IsNullOrEmpty(intent.Query))
                    {
                        return await SystemTool.OpenAppAsync(intent.Query);
                    }
                    break;

                case IntentType.CloseApp:
                    if (!string.IsNullOrEmpty(intent.Query))
                    {
                        return await SystemTool.KillProcessAsync(intent.Query);
                    }
                    break;

                case IntentType.Navigation:
                    if (!string.IsNullOrEmpty(intent.Target))
                    {
                        return await SystemTool.OpenUrlAsync(intent.Target);
                    }
                    else if (!string.IsNullOrEmpty(intent.Query))
                    {
                        return await SystemTool.OpenUrlAsync(intent.Query);
                    }
                    break;

                // ==================== INFORMATION ====================
                case IntentType.WebSearch:
                    if (!string.IsNullOrEmpty(intent.Query))
                    {
                        return await WebSearchTool.SearchAsync(intent.Query);
                    }
                    break;

                case IntentType.Weather:
                    return await WebSearchTool.GetWeatherAsync(intent.Query ?? "auto");

                case IntentType.GetInfo:
                    return await GetSystemInfoAsync(intent.Query);

                // ==================== SYSTEM CONTROL ====================
                case IntentType.SystemControl:
                    return await ExecuteSystemControlAsync(intent.Action);

                case IntentType.PowerControl:
                    return await ExecutePowerControlAsync(intent.Action);

                // ==================== FILE OPERATIONS ====================
                case IntentType.FileOperation:
                    return await ExecuteFileOperationAsync(intent);

                case IntentType.OrganizeFiles:
                    // FIRST: Check for dropped/attached folders
                    var droppedPathsOrg = ChatWindow.LastDroppedPaths;
                    if (droppedPathsOrg != null && droppedPathsOrg.Count > 0)
                    {
                        var results = new List<string>();
                        foreach (var droppedPath in droppedPathsOrg)
                        {
                            if (Directory.Exists(droppedPath))
                            {
                                results.Add(await SystemTool.SortFilesByTypeAsync(droppedPath));
                            }
                        }
                        if (results.Count > 0)
                        {
                            ChatWindow.LastDroppedPaths?.Clear();
                            return string.Join("\n\n", results);
                        }
                    }
                    
                    // SECOND: Try to resolve from target/query
                    var organizePath = ResolvePathFromTarget(intent.Target) ?? 
                                       ResolvePathFromTarget(intent.Query) ?? "";
                    
                    // If still empty, check if target looks like a drive path mentioned verbally
                    if (string.IsNullOrEmpty(organizePath) && !string.IsNullOrEmpty(intent.Target))
                    {
                        // Handle cases like "D:\Music" or "D drive music"
                        var target = intent.Target;
                        if (target.Length >= 2 && char.IsLetter(target[0]) && target[1] == ':')
                        {
                            organizePath = target;
                        }
                    }
                    
                    if (string.IsNullOrEmpty(organizePath))
                    {
                        return "📁 Please attach a folder using 📎 or specify the full path (e.g., 'organize D:\\Music')";
                    }
                    
                    return await SystemTool.SortFilesByTypeAsync(organizePath);

                case IntentType.ConsolidateFiles:
                    var consolidatePath = ResolvePathFromTarget(intent.Target) ?? 
                                          ResolvePathFromTarget(intent.Query) ?? "";
                    if (string.IsNullOrEmpty(consolidatePath))
                    {
                        // Check for dropped paths
                        var droppedPaths = ChatWindow.LastDroppedPaths;
                        if (droppedPaths != null && droppedPaths.Count > 0)
                        {
                            var results = new List<string>();
                            foreach (var path in droppedPaths)
                            {
                                if (Directory.Exists(path))
                                {
                                    results.Add(await SystemTool.ConsolidateFilesAsync(path));
                                }
                            }
                            if (results.Count > 0)
                                return string.Join("\n\n", results);
                        }
                        return "❌ Please attach a folder using 📎 or specify the path";
                    }
                    return await SystemTool.ConsolidateFilesAsync(consolidatePath);

                case IntentType.FindFiles:
                    if (!string.IsNullOrEmpty(intent.Query))
                    {
                        var searchPath = ResolvePathFromTarget(intent.Target) ?? "";
                        return await SystemTool.FindFilesAsync(searchPath, intent.Query);
                    }
                    break;

                case IntentType.ListFiles:
                    var listPath = ResolvePathFromTarget(intent.Target) ?? 
                                   ResolvePathFromTarget(intent.Query) ?? "";
                    return await SystemTool.ListFilesAsync(listPath);

                // ==================== PRODUCTIVITY ====================
                case IntentType.SetReminder:
                    if (!string.IsNullOrEmpty(intent.Query))
                    {
                        int minutes = 10; // default
                        if (intent.Parameters.TryGetValue("minutes", out var minStr) && int.TryParse(minStr, out var m))
                            minutes = m;
                        return await SystemTool.SetReminderAsync(intent.Query, minutes);
                    }
                    break;

                case IntentType.TakeScreenshot:
                    return await SystemTool.TakeScreenshotAsync();

                case IntentType.TypeText:
                    if (!string.IsNullOrEmpty(intent.Query))
                    {
                        return await SystemTool.TypeTextAsync(intent.Query);
                    }
                    break;

                case IntentType.PressKey:
                    if (!string.IsNullOrEmpty(intent.Query))
                    {
                        return await SystemTool.PressKeyAsync(intent.Query);
                    }
                    break;

                case IntentType.OpenSettings:
                    return await SystemTool.OpenSettingsAsync(intent.Query ?? "");

                case IntentType.EmptyTrash:
                    return await SystemTool.EmptyRecycleBinAsync();

                case IntentType.RunCommand:
                    if (!string.IsNullOrEmpty(intent.Query))
                    {
                        return await SystemTool.RunCommandAsync(intent.Query);
                    }
                    break;
            }

            return null; // Let the AI handle it conversationally
        }

        /// <summary>
        /// Execute media control actions (pause, play, next, previous)
        /// </summary>
        private static async Task<string?> ExecuteMediaControlAsync(string? action)
        {
            return action?.ToLower() switch
            {
                "pause" or "stop" => await SpotifyTool.ControlPlaybackAsync("pause"),
                "play" or "resume" => await SpotifyTool.ControlPlaybackAsync("play"),
                "next" or "skip" => await SpotifyTool.ControlPlaybackAsync("next"),
                "previous" or "back" => await SpotifyTool.ControlPlaybackAsync("previous"),
                _ => null
            };
        }

        /// <summary>
        /// Execute power control actions (shutdown, restart, sleep, lock)
        /// </summary>
        private static async Task<string?> ExecutePowerControlAsync(string? action)
        {
            return action?.ToLower() switch
            {
                "shutdown" or "poweroff" => await SystemTool.ShutdownAsync(60),
                "restart" or "reboot" => await SystemTool.RestartAsync(60),
                "sleep" or "hibernate" => await SystemTool.SleepAsync(),
                "lock" => await SystemTool.LockComputerAsync(),
                _ => null
            };
        }

        /// <summary>
        /// Execute file operations (create, delete, move, copy, rename)
        /// </summary>
        private static async Task<string?> ExecuteFileOperationAsync(ParsedIntent intent)
        {
            var action = intent.Action?.ToLower();
            var query = intent.Query;
            var target = intent.Target;

            switch (action)
            {
                case "create":
                    if (!string.IsNullOrEmpty(query))
                    {
                        var path = query.Contains("\\") || query.Contains("/") 
                            ? query 
                            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), query);
                        return await SystemTool.CreateFolderAsync(path);
                    }
                    break;

                case "delete":
                    // DISABLED: Delete operations now handled by Agent with confirmation dialog
                    // var deletePath = ResolvePathFromTarget(target) ?? ResolvePathFromTarget(query);
                    // if (!string.IsNullOrEmpty(deletePath))
                    // {
                    //     return await SystemTool.DeleteAsync(deletePath);
                    // }
                    return null; // Let agent handle it
                    break;

                case "move":
                    if (!string.IsNullOrEmpty(query) && !string.IsNullOrEmpty(target))
                    {
                        var sourcePath = ResolvePathFromTarget(query) ?? query;
                        var destPath = ResolvePathFromTarget(target) ?? target;
                        return await SystemTool.MoveAsync(sourcePath, destPath);
                    }
                    break;

                case "copy":
                    if (!string.IsNullOrEmpty(query) && !string.IsNullOrEmpty(target))
                    {
                        var sourcePath = ResolvePathFromTarget(query) ?? query;
                        var destPath = ResolvePathFromTarget(target) ?? target;
                        return await SystemTool.CopyAsync(sourcePath, destPath);
                    }
                    break;

                case "rename":
                    if (!string.IsNullOrEmpty(query) && !string.IsNullOrEmpty(target))
                    {
                        var path = ResolvePathFromTarget(query) ?? query;
                        return await SystemTool.RenameAsync(path, target);
                    }
                    break;
            }

            return null;
        }

        /// <summary>
        /// Resolve common folder names to actual paths
        /// </summary>
        private static string? ResolvePathFromTarget(string? target)
        {
            if (string.IsNullOrEmpty(target)) return null;
            
            var lower = target.ToLower().Trim();
            
            // Check if it's already a full path
            if (target.Contains(":\\") || target.StartsWith("/"))
                return target;
            
            // Resolve common folder names
            return lower switch
            {
                "desktop" => Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                "documents" or "my documents" => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "downloads" or "download" => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
                "pictures" or "photos" => Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                "music" => Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
                "videos" => Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
                "home" or "user" => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                _ => null
            };
        }

        private static MediaPlayerTool.Platform? ParsePlatform(string? platform)
        {
            if (string.IsNullOrEmpty(platform)) return null;
            
            return platform.ToLower().Replace("_", "") switch
            {
                "spotify" => MediaPlayerTool.Platform.Spotify,
                "youtube" => MediaPlayerTool.Platform.YouTube,
                "youtubemusic" => MediaPlayerTool.Platform.YouTubeMusic,
                "soundcloud" => MediaPlayerTool.Platform.SoundCloud,
                "applemusic" => MediaPlayerTool.Platform.AppleMusic,
                "amazonmusic" => MediaPlayerTool.Platform.AmazonMusic,
                "deezer" => MediaPlayerTool.Platform.Deezer,
                "tidal" => MediaPlayerTool.Platform.Tidal,
                "pandora" => MediaPlayerTool.Platform.Pandora,
                _ => null
            };
        }

        private static async Task<string?> ExecuteSystemControlAsync(string? action)
        {
            return action?.ToLower() switch
            {
                "volume_up" => await SystemTool.SetVolumeAsync(80),
                "volume_down" => await SystemTool.SetVolumeAsync(30),
                "mute" => await SystemTool.ToggleMuteAsync(),
                "shutdown" => await SystemTool.ShutdownAsync(60),
                "restart" => await SystemTool.RestartAsync(60),
                "sleep" => await SystemTool.SleepAsync(),
                "lock" => await SystemTool.LockComputerAsync(),
                _ => null
            };
        }

        private static async Task<string?> GetSystemInfoAsync(string? query)
        {
            var q = query?.ToLower() ?? "";
            
            if (q.Contains("battery") || q.Contains("power") || q.Contains("charge"))
                return await SystemTool.GetBatteryStatusAsync();
            
            if (q.Contains("disk") || q.Contains("storage") || q.Contains("space") || q.Contains("drive"))
                return await SystemTool.GetDiskSpaceAsync();
            
            if (q.Contains("network") || q.Contains("ip") || q.Contains("wifi") || q.Contains("internet"))
                return await SystemTool.GetNetworkInfoAsync();
            
            if (q.Contains("process") || q.Contains("running") || q.Contains("task"))
                return await SystemTool.GetProcessesAsync();
            
            // Default to general system info
            return await SystemTool.GetSystemInfoAsync();
        }
    }
}
