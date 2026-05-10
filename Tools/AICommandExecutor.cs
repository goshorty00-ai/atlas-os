using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AtlasAI.AI;

namespace AtlasAI.Tools
{
    /// <summary>
    /// AI-powered command executor that intelligently understands and executes ANY user request
    /// Uses AI to parse intent and parameters, then executes the appropriate system actions
    /// </summary>
    public static class AICommandExecutor
    {
        // Available tools the AI can use
        private static readonly string ToolsDescription = @"
You are Atlas, an AI assistant with FULL computer control. When the user asks you to do something, you MUST execute it.
DO NOT explain how to do things - ACTUALLY DO THEM.

Available actions you can execute (respond with JSON):
{
  ""action"": ""<action_name>"",
  ""params"": { <parameters> }
}

ACTIONS:
- open_app: {""app"": ""name""} - Open any application (notepad, chrome, spotify, discord, steam, vscode, word, excel, etc.)
- open_url: {""url"": ""url""} - Open a website
- open_folder: {""path"": ""path""} - Open a folder in explorer
- create_folder: {""path"": ""path""} - Create a new folder
- delete_file: {""path"": ""path""} - Delete a file or folder
- move_file: {""source"": ""path"", ""dest"": ""path""} - Move a file/folder
- copy_file: {""source"": ""path"", ""dest"": ""path""} - Copy a file/folder
- rename_file: {""path"": ""path"", ""newName"": ""name""} - Rename a file/folder
- find_files: {""path"": ""path"", ""pattern"": ""pattern""} - Search for files
- organize_files: {""path"": ""path""} - Sort files into folders by type (Images, Documents, Videos, etc.)
- list_files: {""path"": ""path""} - List files in a directory
- read_file: {""path"": ""path""} - Read a text file
- write_file: {""path"": ""path"", ""content"": ""text""} - Write to a file
- run_command: {""command"": ""cmd""} - Run a shell command
- set_volume: {""level"": 0-100} - Set system volume
- mute: {} - Toggle mute
- lock: {} - Lock the computer
- shutdown: {""delay"": 60} - Shutdown PC (delay in seconds)
- restart: {""delay"": 60} - Restart PC
- sleep: {} - Put PC to sleep
- screenshot: {} - Take a screenshot
- empty_trash: {} - Empty recycle bin
- battery: {} - Get battery status
- disk_space: {} - Get disk space info
- network_info: {} - Get network/IP info
- processes: {} - List running processes
- kill_process: {""name"": ""process""} - Kill a process
- system_info: {} - Get system information
- open_settings: {""page"": ""wifi|bluetooth|display|sound|apps|update""} - Open Windows settings
- type_text: {""text"": ""text""} - Type text (simulates keyboard)
- press_key: {""key"": ""key""} - Press a key (enter, tab, escape, f1-f12, ctrl+c, alt+tab, etc.)
- click: {""x"": 100, ""y"": 100} - Click at screen position
- brightness: {""level"": 0-100} - Set screen brightness
- wifi: {""action"": ""on|off|status""} - Control WiFi
- bluetooth: {""action"": ""on|off|status""} - Control Bluetooth
- clipboard_copy: {""text"": ""text""} - Copy text to clipboard
- clipboard_paste: {} - Paste from clipboard
- search_web: {""query"": ""search term""} - Search the web
- weather: {""location"": ""city""} - Get weather
- reminder: {""message"": ""text"", ""minutes"": 5} - Set a reminder
- timer: {""minutes"": 5} - Set a timer
- spotify_play: {""query"": ""song name or artist""} - Play a song/artist/album on Spotify
- spotify_pause: {} - Pause Spotify
- spotify_next: {} - Skip to next track
- spotify_prev: {} - Go to previous track
- spotify_setup: {} - Set up Spotify API (opens browser to developer dashboard)
- spotify_auth: {} - Authenticate with Spotify (after setting up API credentials)
- spotify_set_credentials: {""clientId"": ""id"", ""clientSecret"": ""secret""} - Save Spotify API credentials
- play_pause: {} - Play/pause media
- next_track: {} - Next media track
- prev_track: {} - Previous media track
- minimize_all: {} - Minimize all windows
- show_desktop: {} - Show desktop
- switch_window: {} - Alt+Tab
- close_window: {} - Close current window
- maximize_window: {} - Maximize current window
- multi_action: {""actions"": [{""action"": ""..."", ""params"": {...}}, ...]} - Execute multiple actions

PATHS: Use these shortcuts:
- desktop = User's Desktop
- documents = My Documents  
- downloads = Downloads folder
- pictures = Pictures folder
- music = Music folder
- videos = Videos folder

RESPOND ONLY WITH JSON. If you need to do multiple things, use multi_action.
IMPORTANT: Do NOT make up results or pretend you did something. Only return the action JSON.
If the user asks a question that doesn't need an action, respond with:
{""action"": ""none"", ""params"": {}}
";

        /// <summary>
        /// Use AI to understand and execute any user command
        /// </summary>
        public static async Task<string?> ExecuteWithAIAsync(string userMessage, System.Threading.CancellationToken ct = default)
        {
            try
            {
                var msg = userMessage.ToLower().Trim();
                
                // Skip casual conversation - let the main chat handler deal with it
                // This preserves conversation context
                if (IsCasualConversation(msg))
                {
                    Debug.WriteLine($"[AICommandExecutor] Skipping casual conversation: {msg}");
                    return null;
                }
                
                // First try the rule-based executor for common commands (faster)
                var quickResult = await ToolExecutor.TryExecuteToolWithCancellationAsync(userMessage, ct);
                if (quickResult != null)
                    return quickResult;

                // If no match, use AI to understand the intent
                var aiProvider = AIManager.GetActiveProviderInstance();
                if (aiProvider == null || !aiProvider.IsConfigured)
                    return null; // Fall back to regular AI chat

                // Ask AI to parse the command
                var prompt = $"{ToolsDescription}\n\nUser request: {userMessage}\n\nRespond with JSON action:";
                
                var messages = new List<object>
                {
                    new { role = "user", content = prompt }
                };

                var response = await AIManager.SendMessageAsync(messages, 500, ct);
                if (!response.Success || string.IsNullOrEmpty(response.Content))
                    return null;

                // Parse the AI response
                var result = await ParseAndExecuteAsync(response.Content, userMessage);
                return result;
            }
            catch (OperationCanceledException)
            {
                return "CANCELLED · OPERATION STOPPED";
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AICommandExecutor] Error: {ex.Message}");
                return null;
            }
        }
        
        /// <summary>
        /// Check if message is casual conversation that should be handled by main chat (with context)
        /// </summary>
        private static bool IsCasualConversation(string msg)
        {
            // Short responses without action verbs are likely casual conversation
            if (msg.Length < 50)
            {
                var actionVerbs = new[] { "open", "play", "search", "find", "create", "delete", "move", "copy",
                    "scan", "check", "run", "start", "stop", "close", "show", "hide", "set", "change",
                    "turn on", "turn off", "enable", "disable", "install", "uninstall", "download",
                    "generate", "make", "build", "write", "read", "send", "get", "put", "take" };
                
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
                    return true;
            }
            
            // Common casual phrases
            var casualPhrases = new[] { 
                "yeah", "yea", "yes", "no", "nope", "nah", "sure", "ok", "okay", "alright", "cool", "nice",
                "good", "great", "awesome", "amazing", "terrible", "bad", "not bad", "fine", "doing well",
                "cold", "hot", "warm", "freezing", "weather", "tired", "sleepy", "bored", "busy",
                "thanks", "thank you", "cheers", "ta", "bye", "goodbye", "see you", "later",
                "how are you", "how's it going", "what's up", "hello", "hi", "hey",
                "i think", "i feel", "i believe", "i guess", "i suppose",
                "that's", "thats", "it's", "its", "bit cold", "bit hot", "bit tired",
                "all good", "not much", "same here", "me too", "same", "agreed", "exactly", "right",
                "fair enough", "makes sense", "i see", "got it", "understood", "interesting",
                "really", "seriously", "honestly", "actually", "basically", "literally",
                "lol", "haha", "hehe", "lmao", "wow", "damn", "dang", "geez"
            };
            
            foreach (var phrase in casualPhrases)
            {
                if (msg.Contains(phrase))
                    return true;
            }
            
            return false;
        }

        private static async Task<string?> ParseAndExecuteAsync(string aiResponse, string originalRequest)
        {
            try
            {
                // Extract JSON from response (AI might include extra text)
                var jsonMatch = Regex.Match(aiResponse, @"\{[\s\S]*\}", RegexOptions.Multiline);
                if (!jsonMatch.Success)
                    return null;

                var json = jsonMatch.Value;
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (!root.TryGetProperty("action", out var actionElement))
                    return null;

                var action = actionElement.GetString()?.ToLower() ?? "";
                var paramsElement = root.TryGetProperty("params", out var p) ? p : default;

                Debug.WriteLine($"[AICommandExecutor] Action: {action}");

                return action switch
                {
                    "respond" or "none" => null, // Don't let AI fake responses - return null to fall through to regular chat
                    "open_app" => await SystemTool.OpenAppAsync(GetParam(paramsElement, "app")),
                    "open_url" => await SystemTool.OpenUrlAsync(GetParam(paramsElement, "url")),
                    "open_folder" => await OpenFolderAsync(GetParam(paramsElement, "path")),
                    "create_folder" => await SystemTool.CreateFolderAsync(ResolvePath(GetParam(paramsElement, "path"))),
                    "delete_file" => await SystemTool.DeleteWithConfirmationAsync(ResolvePath(GetParam(paramsElement, "path"))), // Uses double confirmation
                    "move_file" => await SystemTool.MoveAsync(ResolvePath(GetParam(paramsElement, "source")), ResolvePath(GetParam(paramsElement, "dest"))),
                    "copy_file" => await SystemTool.CopyAsync(ResolvePath(GetParam(paramsElement, "source")), ResolvePath(GetParam(paramsElement, "dest"))),
                    "rename_file" => await SystemTool.RenameAsync(ResolvePath(GetParam(paramsElement, "path")), GetParam(paramsElement, "newName")),
                    "find_files" => await SystemTool.FindFilesAsync(ResolvePath(GetParam(paramsElement, "path")), GetParam(paramsElement, "pattern")),
                    "organize_files" => await SystemTool.SortFilesByTypeAsync(ResolvePath(GetParam(paramsElement, "path"))),
                    "list_files" => await SystemTool.ListFilesAsync(ResolvePath(GetParam(paramsElement, "path"))),
                    "read_file" => await SystemTool.ReadFileAsync(ResolvePath(GetParam(paramsElement, "path"))),
                    "write_file" => await SystemTool.WriteFileAsync(ResolvePath(GetParam(paramsElement, "path")), GetParam(paramsElement, "content")),
                    "run_command" => await SystemTool.RunCommandAsync(GetParam(paramsElement, "command")),
                    "set_volume" => await SystemTool.SetVolumeAsync(GetIntParam(paramsElement, "level", 50)),
                    "mute" => await SystemTool.ToggleMuteAsync(),
                    "lock" => await SystemTool.LockComputerAsync(),
                    "shutdown" => await SystemTool.ShutdownAsync(GetIntParam(paramsElement, "delay", 60)),
                    "restart" => await SystemTool.RestartAsync(GetIntParam(paramsElement, "delay", 60)),
                    "sleep" => await SystemTool.SleepAsync(),
                    "screenshot" => await SystemTool.TakeScreenshotAsync(),
                    "empty_trash" => await SystemTool.EmptyRecycleBinAsync(),
                    "battery" => await SystemTool.GetBatteryStatusAsync(),
                    "disk_space" => await SystemTool.GetDiskSpaceAsync(),
                    "network_info" => await SystemTool.GetNetworkInfoAsync(),
                    "processes" => await SystemTool.GetProcessesAsync(),
                    "kill_process" => await SystemTool.KillProcessAsync(GetParam(paramsElement, "name")),
                    "system_info" => await SystemTool.GetSystemInfoAsync(),
                    "open_settings" => await SystemTool.OpenSettingsAsync(GetParam(paramsElement, "page")),
                    "type_text" => await SystemTool.TypeTextAsync(GetParam(paramsElement, "text")),
                    "press_key" => await SystemTool.PressKeyAsync(GetParam(paramsElement, "key")),
                    "click" => await ClickAtAsync(GetIntParam(paramsElement, "x", 0), GetIntParam(paramsElement, "y", 0)),
                    "brightness" => await SetBrightnessAsync(GetIntParam(paramsElement, "level", 50)),
                    "wifi" => await ControlWifiAsync(GetParam(paramsElement, "action")),
                    "bluetooth" => await ControlBluetoothAsync(GetParam(paramsElement, "action")),
                    "clipboard_copy" => CopyToClipboard(GetParam(paramsElement, "text")),
                    "clipboard_paste" => await PasteFromClipboardAsync(),
                    "search_web" => await SearchWebAsync(GetParam(paramsElement, "query")),
                    "weather" => await WebSearchTool.GetWeatherAsync(GetParam(paramsElement, "location")),
                    "reminder" => await SystemTool.SetReminderAsync(GetParam(paramsElement, "message"), GetIntParam(paramsElement, "minutes", 5)),
                    "timer" => await SetTimerAsync(GetIntParam(paramsElement, "minutes", 5)),
                    "spotify_play" => await SpotifyTool.PlayAsync(GetParam(paramsElement, "query")),
                    "spotify_pause" => await SpotifyTool.ControlPlaybackAsync("pause"),
                    "spotify_next" => await SpotifyTool.ControlPlaybackAsync("next"),
                    "spotify_prev" => await SpotifyTool.ControlPlaybackAsync("previous"),
                    "spotify_setup" => await SetupSpotifyApiAsync(),
                    "spotify_auth" => await AuthenticateSpotifyAsync(),
                    "spotify_set_credentials" => SaveSpotifyCredentials(GetParam(paramsElement, "clientId"), GetParam(paramsElement, "clientSecret")),
                    "play_pause" => await MediaControlAsync("playpause"),
                    "next_track" => await MediaControlAsync("next"),
                    "prev_track" => await MediaControlAsync("prev"),
                    "minimize_all" => await MinimizeAllAsync(),
                    "show_desktop" => await ShowDesktopAsync(),
                    "switch_window" => await SwitchWindowAsync(),
                    "close_window" => await CloseWindowAsync(),
                    "maximize_window" => await MaximizeWindowAsync(),
                    "multi_action" => await ExecuteMultiActionAsync(paramsElement),
                    _ => null
                };
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AICommandExecutor] Parse error: {ex.Message}");
                return null;
            }
        }

        private static string GetParam(JsonElement element, string name)
        {
            if (element.ValueKind == JsonValueKind.Undefined)
                return "";
            if (element.TryGetProperty(name, out var prop))
                return prop.GetString() ?? "";
            return "";
        }

        private static int GetIntParam(JsonElement element, string name, int defaultValue)
        {
            if (element.ValueKind == JsonValueKind.Undefined)
                return defaultValue;
            if (element.TryGetProperty(name, out var prop))
            {
                if (prop.ValueKind == JsonValueKind.Number)
                    return prop.GetInt32();
                if (prop.ValueKind == JsonValueKind.String && int.TryParse(prop.GetString(), out var val))
                    return val;
            }
            return defaultValue;
        }

        private static string ResolvePath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

            var lower = path.ToLower().Trim();
            return lower switch
            {
                "desktop" => Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                "documents" or "my documents" => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "downloads" => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
                "pictures" or "photos" => Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                "music" => Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
                "videos" => Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
                "home" or "user" => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                _ => path
            };
        }

        // Additional helper methods for new actions
        private static async Task<string> OpenFolderAsync(string path)
        {
            path = ResolvePath(path);
            if (!Directory.Exists(path))
                return $"❌ Folder not found: {path}";
            Process.Start("explorer.exe", path);
            return $"📂 Opened {path}";
        }

        private static async Task<string> SearchWebAsync(string query)
        {
            // Try instant answer first, if no results open browser
            var result = await WebSearchTool.SearchAsync(query);
            return result;
        }

        private static async Task<string> ClickAtAsync(int x, int y)
        {
            // Use PowerShell to click
            var script = $@"
Add-Type -AssemblyName System.Windows.Forms
[System.Windows.Forms.Cursor]::Position = New-Object System.Drawing.Point({x}, {y})
Add-Type -MemberDefinition '[DllImport(""user32.dll"")] public static extern void mouse_event(int dwFlags, int dx, int dy, int dwData, int dwExtraInfo);' -Name Win32 -Namespace System
[System.Win32]::mouse_event(0x0002, 0, 0, 0, 0)
[System.Win32]::mouse_event(0x0004, 0, 0, 0, 0)
";
            await RunPowerShellAsync(script);
            return $"🖱️ Clicked at ({x}, {y})";
        }

        private static async Task<string> SetBrightnessAsync(int level)
        {
            level = Math.Clamp(level, 0, 100);
            var script = $@"(Get-WmiObject -Namespace root/WMI -Class WmiMonitorBrightnessMethods).WmiSetBrightness(1,{level})";
            await RunPowerShellAsync(script);
            return $"🔆 Brightness set to {level}%";
        }

        private static async Task<string> ControlWifiAsync(string action)
        {
            var script = action.ToLower() switch
            {
                "on" => "netsh interface set interface \"Wi-Fi\" enabled",
                "off" => "netsh interface set interface \"Wi-Fi\" disabled",
                _ => "netsh wlan show interfaces"
            };
            return await SystemTool.RunCommandAsync(script);
        }

        private static async Task<string> ControlBluetoothAsync(string action)
        {
            // Bluetooth control via PowerShell
            var script = action.ToLower() switch
            {
                "on" => "Start-Service bthserv",
                "off" => "Stop-Service bthserv",
                _ => "Get-Service bthserv | Select-Object Status"
            };
            await RunPowerShellAsync(script);
            return $"📶 Bluetooth {action}";
        }

        private static string CopyToClipboard(string text)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                System.Windows.Clipboard.SetText(text);
            });
            return "📋 Copied to clipboard";
        }

        private static async Task<string> PasteFromClipboardAsync()
        {
            await SystemTool.PressKeyAsync("ctrl+v");
            return "📋 Pasted from clipboard";
        }

        private static async Task<string> SetTimerAsync(int minutes)
        {
            // Use Windows toast notification for timer
            var script = $@"
$app = 'Atlas AI'
$title = 'Timer Complete!'
$message = '{minutes} minute timer finished'
[Windows.UI.Notifications.ToastNotificationManager, Windows.UI.Notifications, ContentType = WindowsRuntime] | Out-Null
$template = [Windows.UI.Notifications.ToastTemplateType]::ToastText02
$xml = [Windows.UI.Notifications.ToastNotificationManager]::GetTemplateContent($template)
$xml.GetElementsByTagName('text')[0].AppendChild($xml.CreateTextNode($title)) | Out-Null
$xml.GetElementsByTagName('text')[1].AppendChild($xml.CreateTextNode($message)) | Out-Null
Start-Sleep -Seconds ({minutes * 60})
[Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier($app).Show($xml)
";
            // Run in background
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-Command \"{script}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            Process.Start(psi);
            return $"⏱️ Timer set for {minutes} minutes";
        }

        private static async Task<string> MediaControlAsync(string action)
        {
            var key = action switch
            {
                "playpause" => "MediaPlayPause",
                "next" => "MediaNextTrack",
                "prev" => "MediaPreviousTrack",
                "stop" => "MediaStop",
                _ => "MediaPlayPause"
            };
            await RunPowerShellAsync($"$obj = New-Object -ComObject WScript.Shell; $obj.SendKeys([char]0xB3)");
            return $"🎵 Media: {action}";
        }

        private static async Task<string> MinimizeAllAsync()
        {
            await RunPowerShellAsync("(New-Object -ComObject Shell.Application).MinimizeAll()");
            return "📥 Minimized all windows";
        }

        private static async Task<string> ShowDesktopAsync()
        {
            await RunPowerShellAsync("(New-Object -ComObject Shell.Application).ToggleDesktop()");
            return "🖥️ Showing desktop";
        }

        private static async Task<string> SwitchWindowAsync()
        {
            await SystemTool.PressKeyAsync("alt+tab");
            return "🔄 Switched window";
        }

        private static async Task<string> CloseWindowAsync()
        {
            await SystemTool.PressKeyAsync("alt+f4");
            return "❌ Closed window";
        }

        private static async Task<string> MaximizeWindowAsync()
        {
            await RunPowerShellAsync("$obj = New-Object -ComObject WScript.Shell; $obj.SendKeys('% x')");
            return "🔲 Maximized window";
        }

        private static async Task<string> ExecuteMultiActionAsync(JsonElement paramsElement)
        {
            if (!paramsElement.TryGetProperty("actions", out var actionsArray))
                return "❌ No actions specified";

            var results = new List<string>();
            foreach (var actionObj in actionsArray.EnumerateArray())
            {
                var actionJson = actionObj.GetRawText();
                var result = await ParseAndExecuteAsync(actionJson, "");
                if (!string.IsNullOrEmpty(result))
                    results.Add(result);
            }
            return string.Join("\n", results);
        }

        private static async Task RunPowerShellAsync(string script)
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-Command \"{script.Replace("\"", "\\\"")}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            var process = Process.Start(psi);
            if (process != null)
                await process.WaitForExitAsync();
        }

        private static async Task<string> SetupSpotifyApiAsync()
        {
            // Open Spotify Developer Dashboard
            Process.Start(new ProcessStartInfo("https://developer.spotify.com/dashboard") { UseShellExecute = true });
            
            return @"🎵 Spotify API Setup

I've opened the Spotify Developer Dashboard. Here's what you need to do:

1. Log in with your Spotify account
2. Click Create App
3. Fill in:
   - App name: Atlas AI
   - App description: AI Assistant Spotify Control
   - Redirect URI: http://localhost:5543/callback (IMPORTANT!)
4. Check the Web API checkbox
5. Click Save
6. Go to your app's Settings
7. Copy your Client ID and Client Secret

Then tell me: ""Set Spotify credentials [your-client-id] [your-client-secret]""

Or say: ""Authenticate Spotify"" after setting credentials.";
        }

        private static async Task<string> AuthenticateSpotifyAsync()
        {
            return @"✅ Spotify Ready!

Spotify control works automatically. Try:
- ""Play Bohemian Rhapsody""
- ""Play Shape of You by Ed Sheeran""
- ""Pause music"" / ""Next song""";
        }

        private static string SaveSpotifyCredentials(string clientId, string clientSecret)
        {
            return @"ℹ️ Spotify Simplified

Spotify now uses direct control - no API credentials needed!
Just say ""Play [song name]"" to play music.";
        }
    }
}
