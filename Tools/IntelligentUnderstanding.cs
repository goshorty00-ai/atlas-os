using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AtlasAI.Settings;
using AtlasAI.Core;

namespace AtlasAI.Tools
{
    /// <summary>
    /// Super intelligent understanding system that adapts to how users speak.
    /// Handles typos, slang, context, and vague requests.
    /// </summary>
    public static class IntelligentUnderstanding
    {
        private static readonly HttpClient httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        private static string? _apiKey;
        private static List<ConversationContext> _recentContext = new();
        private static Dictionary<string, string> _userPreferences = new();
        private static readonly string PrefsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AtlasAI", "user_prefs.json");

        // Common typos and variations mapping
        private static readonly Dictionary<string, string[]> TypoMap = new()
        {
            // Music/Media
            { "play", new[] { "paly", "plya", "pla", "ply", "plau", "plaay", "plat", "pley", "palya" } },
            { "spotify", new[] { "spotfy", "spotiffy", "spotifi", "sptify", "spotiy", "spotfiy", "sptoify", "spotifiy", "spoitfy" } },
            { "youtube", new[] { "youtub", "yotube", "utube", "youube", "youtbe", "yuotube", "youttube", "youtubee" } },
            { "music", new[] { "musci", "muisc", "msuic", "musik", "mucis", "muscic", "musuc" } },
            { "song", new[] { "snog", "sogn", "songg", "snong", "soong", "sonf" } },
            { "pause", new[] { "paus", "pasue", "puase", "pausee", "pauze", "pawse" } },
            { "volume", new[] { "volum", "volumne", "voulme", "vlume", "vol", "volune", "volumee" } },
            
            // Apps
            { "chrome", new[] { "chrom", "crhome", "chorme", "gogle", "google", "chrme", "chromee" } },
            { "discord", new[] { "discrod", "disocrd", "dicord", "discor", "disord", "discordd" } },
            { "notepad", new[] { "notpad", "notepadd", "notepd", "notepda", "noetpad" } },
            { "settings", new[] { "setings", "settigns", "settngs", "sttings", "settingss", "setttings" } },
            { "browser", new[] { "broswer", "brwoser", "browsr", "broswe", "browsre" } },
            { "calculator", new[] { "calculater", "calcualtor", "calcultor", "calc", "calulator" } },
            
            // Actions
            { "open", new[] { "opne", "opn", "oepn", "oen", "openn", "oopen", "opem" } },
            { "close", new[] { "clsoe", "closee", "clos", "cloe", "closse", "colse" } },
            { "search", new[] { "serach", "seach", "serch", "saerch", "searhc", "searh" } },
            { "shutdown", new[] { "shutdwon", "shutdonw", "shtdown", "shutdow", "shutdwn", "shutown" } },
            { "restart", new[] { "restrat", "restar", "restrt", "rstart", "resart", "restaret" } },
            { "organize", new[] { "orginize", "orgainze", "organiz", "oraganize", "ogranize", "organise", "orgnaize" } },
            
            // System
            { "computer", new[] { "compter", "computr", "comuter", "pc", "laptop", "compueter", "computre" } },
            { "screen", new[] { "scren", "screne", "sceen", "scrn", "screeen", "scree" } },
            { "file", new[] { "fiel", "flie", "fil", "filee", "fle", "fiels" } },
            { "folder", new[] { "foldr", "fodler", "floder", "flder", "foler", "folderr" } },
            { "download", new[] { "donwload", "downlaod", "downlod", "donload", "dwonload", "downoad" } },
            { "desktop", new[] { "destkop", "dekstop", "destop", "desktp", "desktoop" } },
            
            // Weather
            { "weather", new[] { "wether", "wheather", "waether", "weathr", "weahter", "weathe" } },
            { "temperature", new[] { "temprature", "temperture", "tempature", "temp", "temperatur" } },
        };

        // Slang and casual speech mapping
        private static readonly Dictionary<string, string> SlangMap = new()
        {
            // Music requests
            { "put on", "play" },
            { "throw on", "play" },
            { "bump", "play" },
            { "blast", "play" },
            { "crank up", "play" },
            { "gimme", "play" },
            { "lemme hear", "play" },
            { "i wanna hear", "play" },
            { "i want to hear", "play" },
            { "can you play", "play" },
            { "could you play", "play" },
            { "would you play", "play" },
            
            // Volume
            { "turn it up", "volume up" },
            { "louder", "volume up" },
            { "crank it", "volume up" },
            { "turn it down", "volume down" },
            { "quieter", "volume down" },
            { "shh", "mute" },
            { "shut up", "mute" },
            { "silence", "mute" },
            
            // Media control
            { "skip", "next" },
            { "skip this", "next" },
            { "next one", "next" },
            { "go back", "previous" },
            { "last one", "previous" },
            { "stop it", "stop" },
            { "hold on", "pause" },
            { "wait", "pause" },
            
            // Apps
            { "fire up", "open" },
            { "boot up", "open" },
            { "start up", "open" },
            { "launch", "open" },
            { "run", "open" },
            { "kill", "close" },
            { "exit", "close" },
            { "quit", "close" },
            { "end", "close" },
            
            // System
            { "turn off", "shutdown" },
            { "power off", "shutdown" },
            { "shut it down", "shutdown" },
            { "reboot", "restart" },
            { "lock it", "lock" },
            { "lock up", "lock" },
            
            // General
            { "what's", "what is" },
            { "where's", "where is" },
            { "who's", "who is" },
            { "how's", "how is" },
            { "gonna", "going to" },
            { "wanna", "want to" },
            { "gotta", "got to" },
            { "kinda", "kind of" },
            { "sorta", "sort of" },
            { "dunno", "don't know" },
            { "lemme", "let me" },
            { "gimme", "give me" },
        };

        // Context clues for understanding vague requests
        private static readonly Dictionary<string, string[]> ContextClues = new()
        {
            { "music", new[] { "song", "track", "album", "artist", "band", "playlist", "tune", "beat", "jam" } },
            { "video", new[] { "watch", "youtube", "clip", "movie", "show", "stream" } },
            { "app", new[] { "program", "application", "software", "browser", "game" } },
            { "file", new[] { "document", "folder", "directory", "path", "download" } },
            { "system", new[] { "computer", "pc", "laptop", "machine", "device" } },
        };

        public class UnderstandingResult
        {
            public string NormalizedInput { get; set; } = "";
            public string InferredIntent { get; set; } = "";
            public Dictionary<string, string> ExtractedEntities { get; set; } = new();
            public float Confidence { get; set; }
            public string? Clarification { get; set; }
            public bool NeedsClarification { get; set; }
            public string? SuggestedAction { get; set; }
        }

        /// <summary>
        /// Main entry point - understand what the user REALLY means
        /// </summary>
        public static async Task<UnderstandingResult> UnderstandAsync(string userInput, List<string>? attachedPaths = null)
        {
            LoadApiKey();
            LoadUserPreferences();

            var result = new UnderstandingResult();
            
            // Step 1: Fix typos and normalize
            var normalized = NormalizeInput(userInput);
            result.NormalizedInput = normalized;
            Debug.WriteLine($"[Understand] Normalized: '{userInput}' -> '{normalized}'");

            // Step 2: Apply slang/casual speech mapping
            normalized = ApplySlangMapping(normalized);
            Debug.WriteLine($"[Understand] After slang: '{normalized}'");

            // Step 3: Check recent context for references
            normalized = ResolveContextReferences(normalized);
            Debug.WriteLine($"[Understand] After context: '{normalized}'");

            // Step 4: Try quick local understanding first
            var quickResult = TryQuickUnderstanding(normalized, attachedPaths);
            if (quickResult != null && quickResult.Confidence > 0.8f)
            {
                return quickResult;
            }

            // Step 5: Use AI for complex understanding (with timeout to prevent hanging)
            if (!string.IsNullOrEmpty(_apiKey))
            {
                try
                {
                    // Use a 5-second timeout to prevent UI from hanging
                    using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(5));
                    var aiTask = DeepUnderstandAsync(userInput, normalized);
                    var completedTask = await Task.WhenAny(aiTask, Task.Delay(5000, cts.Token));
                    
                    if (completedTask == aiTask && !aiTask.IsFaulted)
                    {
                        cts.Cancel(); // Cancel the delay task
                        var aiResult = await aiTask;
                        if (aiResult != null)
                        {
                            return aiResult;
                        }
                    }
                    else
                    {
                        Debug.WriteLine("[Understand] AI understanding timed out, using quick result");
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Understand] AI understanding failed: {ex.Message}");
                }
            }

            // Step 6: Return best guess with clarification if needed
            result.Confidence = quickResult?.Confidence ?? 0.3f;
            result.InferredIntent = quickResult?.InferredIntent ?? "unknown";
            result.ExtractedEntities = quickResult?.ExtractedEntities ?? new();
            
            if (result.Confidence < 0.5f)
            {
                result.NeedsClarification = true;
                result.Clarification = GenerateClarificationQuestion(normalized);
            }

            return result;
        }

        /// <summary>
        /// Fix common typos using fuzzy matching
        /// </summary>
        private static string NormalizeInput(string input)
        {
            var words = input.ToLower().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var corrected = new List<string>();

            foreach (var word in words)
            {
                var found = false;
                foreach (var (correct, typos) in TypoMap)
                {
                    if (typos.Contains(word) || LevenshteinDistance(word, correct) <= 2)
                    {
                        corrected.Add(correct);
                        found = true;
                        break;
                    }
                }
                if (!found)
                {
                    corrected.Add(word);
                }
            }

            return string.Join(" ", corrected);
        }

        /// <summary>
        /// Convert casual speech to standard commands
        /// </summary>
        private static string ApplySlangMapping(string input)
        {
            var result = input;
            foreach (var (slang, standard) in SlangMap.OrderByDescending(x => x.Key.Length))
            {
                if (result.Contains(slang, StringComparison.OrdinalIgnoreCase))
                {
                    result = Regex.Replace(result, Regex.Escape(slang), standard, RegexOptions.IgnoreCase);
                }
            }
            return result;
        }

        /// <summary>
        /// Resolve references like "it", "that", "the same" using conversation context
        /// </summary>
        private static string ResolveContextReferences(string input)
        {
            if (_recentContext.Count == 0) return input;

            var lower = input.ToLower();
            
            // Handle "it", "that", "this"
            if (Regex.IsMatch(lower, @"\b(play it|open it|close it|do it|that one|this one|the same)\b"))
            {
                var lastEntity = _recentContext.LastOrDefault()?.MainEntity;
                if (!string.IsNullOrEmpty(lastEntity))
                {
                    input = Regex.Replace(input, @"\b(it|that|this|the same)\b", lastEntity, RegexOptions.IgnoreCase);
                }
            }

            // Handle "again"
            if (lower.Contains("again") && _recentContext.Count > 0)
            {
                var lastAction = _recentContext.LastOrDefault()?.Action;
                var lastEntity = _recentContext.LastOrDefault()?.MainEntity;
                if (!string.IsNullOrEmpty(lastAction) && !string.IsNullOrEmpty(lastEntity))
                {
                    return $"{lastAction} {lastEntity}";
                }
            }

            return input;
        }

        /// <summary>
        /// Quick local understanding without AI
        /// </summary>
        private static UnderstandingResult? TryQuickUnderstanding(string input, List<string>? attachedPaths = null)
        {
            var result = new UnderstandingResult { NormalizedInput = input };
            var lower = input.ToLower();
            
            Debug.WriteLine($"[Understand] TryQuickUnderstanding: '{input}'");
            Debug.WriteLine($"[Understand] Attached paths: {(attachedPaths?.Count > 0 ? string.Join(", ", attachedPaths) : "none")}");

            // ==================== IMAGE ANALYSIS ====================
            // PRIORITY: Check if user attached an image and is asking about it
            var hasAttachedImages = attachedPaths?.Any(p => IsImageFile(p)) == true;
            if (hasAttachedImages)
            {
                // Check if user is asking about the image
                var isAskingAboutImage = Regex.IsMatch(lower, @"\b(what|explain|describe|analyze|tell me|show me|read|ocr|text|mean|this|that|it)\b") ||
                                         Regex.IsMatch(lower, @"\b(what does|what is|what's|whats|can you|could you)\b") ||
                                         lower.Contains("?") ||
                                         lower.Length < 50; // Short messages with images are usually about the image
                
                if (isAskingAboutImage)
                {
                    result.InferredIntent = "analyze_image";
                    result.ExtractedEntities["image_path"] = attachedPaths!.First(p => IsImageFile(p));
                    result.ExtractedEntities["question"] = input;
                    result.Confidence = 0.95f;
                    Debug.WriteLine($"[Understand] Detected analyze_image: path='{result.ExtractedEntities["image_path"]}', question='{input}'");
                    return result;
                }
            }

            // Music patterns - be more flexible
            if (Regex.IsMatch(lower, @"\b(play|listen|hear)\b") ||
                Regex.IsMatch(lower, @"\b(music|song|track|album|spotify|youtube)\b"))
            {
                // Check if it's actually a play request
                if (Regex.IsMatch(lower, @"\b(play|listen|hear|put on)\b"))
                {
                    result.InferredIntent = "play_music";
                    result.ExtractedEntities["query"] = ExtractMusicQuery(input);
                    result.ExtractedEntities["platform"] = DetectPlatform(input);
                    result.Confidence = 0.85f;
                    Debug.WriteLine($"[Understand] Detected play_music: query='{result.ExtractedEntities["query"]}', platform='{result.ExtractedEntities["platform"]}'");
                    return result;
                }
            }

            // Media control
            if (Regex.IsMatch(lower, @"\b(pause|stop|next|previous|skip|resume)\b"))
            {
                result.InferredIntent = "media_control";
                result.ExtractedEntities["action"] = ExtractMediaAction(lower);
                result.Confidence = 0.9f;
                Debug.WriteLine($"[Understand] Detected media_control: action='{result.ExtractedEntities["action"]}'");
                return result;
            }

            // Volume control
            if (Regex.IsMatch(lower, @"\b(volume|mute|unmute|louder|quieter)\b"))
            {
                result.InferredIntent = "volume_control";
                result.ExtractedEntities["action"] = ExtractVolumeAction(lower);
                result.Confidence = 0.9f;
                Debug.WriteLine($"[Understand] Detected volume_control: action='{result.ExtractedEntities["action"]}'");
                return result;
            }

            // Open folder - CHECK THIS BEFORE open_app to catch "open downloads folder" etc.
            // Also handle system folders like "program data", "appdata", etc.
            var isSystemFolderRequest = Regex.IsMatch(lower, @"\b(program\s*data|programdata|appdata|app\s*data|roaming|local\s*appdata|temp|windows|system32|program\s*files)\b");
            if (isSystemFolderRequest && Regex.IsMatch(lower, @"\b(open|go to|show|browse|access)\b"))
            {
                result.InferredIntent = "open_system_folder";
                result.ExtractedEntities["folder"] = ExtractSystemFolderTarget(input);
                result.Confidence = 0.95f;
                Debug.WriteLine($"[Understand] Detected open_system_folder: folder='{result.ExtractedEntities["folder"]}'");
                return result;
            }
            
            if (Regex.IsMatch(lower, @"\b(open|go to|show|browse)\b.*\b(folder|directory|downloads|documents|desktop|pictures|music|videos)\b"))
            {
                // Check if it's specifically asking to open a folder location
                if (Regex.IsMatch(lower, @"\b(downloads|documents|desktop|pictures|music|videos|folder|directory)\b"))
                {
                    result.InferredIntent = "open_folder";
                    result.ExtractedEntities["folder"] = ExtractFolderTarget(input);
                    result.Confidence = 0.95f;
                    Debug.WriteLine($"[Understand] Detected open_folder: folder='{result.ExtractedEntities["folder"]}'");
                    return result;
                }
            }
            
            // App control
            if (Regex.IsMatch(lower, @"\b(open|close|launch|start|kill|quit)\b\s+\w+"))
            {
                var isClose = Regex.IsMatch(lower, @"\b(close|kill|quit|exit)\b");
                result.InferredIntent = isClose ? "close_app" : "open_app";
                result.ExtractedEntities["app"] = ExtractAppName(input);
                result.Confidence = 0.85f;
                Debug.WriteLine($"[Understand] Detected {result.InferredIntent}: app='{result.ExtractedEntities["app"]}'");
                return result;
            }

            // Power control
            if (Regex.IsMatch(lower, @"\b(shutdown|restart|reboot|sleep|lock|hibernate)\b"))
            {
                result.InferredIntent = "power_control";
                result.ExtractedEntities["action"] = ExtractPowerAction(lower);
                result.Confidence = 0.9f;
                Debug.WriteLine($"[Understand] Detected power_control: action='{result.ExtractedEntities["action"]}'");
                return result;
            }

            // AI Image Generation - MUST be before web_search to catch "generate image" requests
            if (Regex.IsMatch(lower, @"\b(generate|create|make|draw|paint|illustrate|design)\b.*\b(image|picture|photo|art|illustration|drawing)\b") ||
                Regex.IsMatch(lower, @"^(draw|paint)\s+"))
            {
                result.InferredIntent = "generate_image";
                result.ExtractedEntities["prompt"] = ExtractImagePrompt(input);
                result.Confidence = 0.95f;
                Debug.WriteLine($"[Understand] Detected generate_image: prompt='{result.ExtractedEntities["prompt"]}'");
                return result;
            }

            // Web search
            if (Regex.IsMatch(lower, @"\b(search|google|look up|find|what is|who is|how to)\b"))
            {
                result.InferredIntent = "web_search";
                result.ExtractedEntities["query"] = ExtractSearchQuery(input);
                result.Confidence = 0.8f;
                Debug.WriteLine($"[Understand] Detected web_search: query='{result.ExtractedEntities["query"]}'");
                return result;
            }

            // Weather
            if (Regex.IsMatch(lower, @"\b(weather|temperature|forecast|rain|sunny|cold|hot)\b"))
            {
                result.InferredIntent = "weather";
                result.ExtractedEntities["location"] = ExtractLocation(input);
                result.Confidence = 0.85f;
                Debug.WriteLine($"[Understand] Detected weather: location='{result.ExtractedEntities["location"]}'");
                return result;
            }

            // File operations - check for attached files context
            var hasAttachedFiles = attachedPaths?.Count > 0;
            if (Regex.IsMatch(lower, @"\b(organize|sort|clean|tidy)\b.*\b(file|folder|desktop|download)\b") ||
                (hasAttachedFiles && Regex.IsMatch(lower, @"\b(put.*into|organize|sort|clean|tidy|arrange)\b")))
            {
                result.InferredIntent = "organize_files";
                result.ExtractedEntities["target"] = ExtractFolderTarget(input);
                result.Confidence = 0.85f;
                Debug.WriteLine($"[Understand] Detected organize_files: target='{result.ExtractedEntities["target"]}', hasAttached={hasAttachedFiles}");
                return result;
            }

            // Screenshot
            if (Regex.IsMatch(lower, @"\b(screenshot|capture|screen)\b"))
            {
                result.InferredIntent = "screenshot";
                result.Confidence = 0.9f;
                Debug.WriteLine($"[Understand] Detected screenshot");
                return result;
            }
            
            // System scan - virus/malware/spyware scan
            if (Regex.IsMatch(lower, @"\b(scan|check|analyze)\b.*\b(system|computer|pc|virus|malware|spyware|files|security)\b") ||
                Regex.IsMatch(lower, @"\b(virus|malware|spyware|security)\b.*\b(scan|check)\b"))
            {
                result.InferredIntent = "system_scan";
                result.ExtractedEntities["scan_type"] = lower.Contains("deep") || lower.Contains("full") ? "deep" : "quick";
                result.Confidence = 0.95f;
                Debug.WriteLine($"[Understand] Detected system_scan: type='{result.ExtractedEntities["scan_type"]}'");
                return result;
            }
            
            Debug.WriteLine($"[Understand] No quick understanding match found");
            return null;
        }

        /// <summary>
        /// Deep AI-powered understanding for complex requests
        /// </summary>
        private static async Task<UnderstandingResult?> DeepUnderstandAsync(string original, string normalized)
        {
            try
            {
                var contextSummary = GetContextSummary();
                var prefsContext = GetPreferencesContext();

                var prompt = $@"You are an intelligent assistant that understands what users REALLY mean, even with typos, slang, or vague requests.

User said: ""{original}""
Normalized: ""{normalized}""
{contextSummary}
{prefsContext}

Analyze what the user wants and respond with JSON:
{{
  ""intent"": ""play_music|media_control|volume_control|open_app|close_app|web_search|weather|file_operation|power_control|screenshot|reminder|unknown"",
  ""confidence"": 0.0-1.0,
  ""entities"": {{
    ""query"": ""main subject/search term"",
    ""app"": ""application name"",
    ""action"": ""specific action"",
    ""platform"": ""spotify/youtube/etc"",
    ""target"": ""file/folder path""
  }},
  ""clarification"": ""question to ask if unclear (null if clear)"",
  ""suggested_response"": ""what to say/do""
}}

Examples of understanding vague requests:
- ""that song from yesterday"" -> use context to find what was played
- ""the usual"" -> check user preferences
- ""you know what I mean"" -> infer from context
- ""do the thing"" -> check recent actions
- ""make it louder"" -> volume up
- ""put something on"" -> play music (ask what genre if no preference)";

                var requestBody = new
                {
                    model = "gpt-3.5-turbo",
                    messages = new[]
                    {
                        new { role = "system", content = "You understand natural language perfectly, including typos, slang, and context. Respond only with JSON." },
                        new { role = "user", content = prompt }
                    },
                    max_tokens = 300,
                    temperature = 0.2
                };

                var json = JsonSerializer.Serialize(requestBody);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions");
                request.Headers.Add("Authorization", $"Bearer {_apiKey}");
                request.Content = content;

                var response = await httpClient.SendAsync(request);
                var responseText = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode) return null;

                using var doc = JsonDocument.Parse(responseText);
                var messageContent = doc.RootElement
                    .GetProperty("choices")[0]
                    .GetProperty("message")
                    .GetProperty("content")
                    .GetString();

                return ParseAIResponse(messageContent, normalized);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Understand] AI error: {ex.Message}");
                return null;
            }
        }

        private static UnderstandingResult? ParseAIResponse(string? response, string normalized)
        {
            if (string.IsNullOrEmpty(response)) return null;

            try
            {
                // Clean markdown
                response = response.Trim();
                if (response.StartsWith("```"))
                {
                    var lines = response.Split('\n').Skip(1).TakeWhile(l => !l.StartsWith("```"));
                    response = string.Join("\n", lines);
                }

                using var doc = JsonDocument.Parse(response);
                var root = doc.RootElement;

                var result = new UnderstandingResult
                {
                    NormalizedInput = normalized,
                    InferredIntent = root.TryGetProperty("intent", out var i) ? i.GetString() ?? "unknown" : "unknown",
                    Confidence = root.TryGetProperty("confidence", out var c) ? c.GetSingle() : 0.5f
                };

                if (root.TryGetProperty("entities", out var entities))
                {
                    foreach (var prop in entities.EnumerateObject())
                    {
                        if (prop.Value.ValueKind == JsonValueKind.String)
                        {
                            var val = prop.Value.GetString();
                            if (!string.IsNullOrEmpty(val) && val != "null")
                                result.ExtractedEntities[prop.Name] = val;
                        }
                    }
                }

                if (root.TryGetProperty("clarification", out var clar) && clar.ValueKind == JsonValueKind.String)
                {
                    var clarText = clar.GetString();
                    if (!string.IsNullOrEmpty(clarText) && clarText != "null")
                    {
                        result.NeedsClarification = true;
                        result.Clarification = clarText;
                    }
                }

                if (root.TryGetProperty("suggested_response", out var sug) && sug.ValueKind == JsonValueKind.String)
                {
                    result.SuggestedAction = sug.GetString();
                }

                return result;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Add context from recent interactions
        /// </summary>
        public static void AddContext(string action, string entity, string? result = null)
        {
            _recentContext.Add(new ConversationContext
            {
                Action = action,
                MainEntity = entity,
                Result = result,
                Timestamp = DateTime.Now
            });

            // Keep only last 10 contexts
            if (_recentContext.Count > 10)
                _recentContext.RemoveAt(0);
        }

        /// <summary>
        /// Learn user preferences
        /// </summary>
        public static void LearnPreference(string key, string value)
        {
            _userPreferences[key] = value;
            SaveUserPreferences();
        }

        #region Helper Methods

        private static string ExtractMusicQuery(string input)
        {
            // First check if user just wants to open a platform (e.g., "play spotify" = open spotify)
            var lower = input.ToLower().Trim();
            var platformNames = new[] { "spotify", "youtube", "soundcloud", "apple music", "itunes", "amazon music", "deezer", "tidal", "pandora" };
            
            // If the input is just "play [platform]", return empty to indicate "just open the app"
            foreach (var platform in platformNames)
            {
                if (lower == $"play {platform}" || lower == $"open {platform}" || lower == $"launch {platform}")
                {
                    return ""; // Empty query means just open the app
                }
            }
            
            // Handle "open spotify, play X" or "open spotify and play X" pattern FIRST
            var openPlayMatch = Regex.Match(input, @"open\s+\w+[,\s]+(?:and\s+)?(?:play|listen to|hear)\s+(.+)", RegexOptions.IgnoreCase);
            if (openPlayMatch.Success)
            {
                var query = openPlayMatch.Groups[1].Value.Trim();
                Debug.WriteLine($"[ExtractMusicQuery] Matched 'open X, play Y' pattern: '{query}'");
                // Clean up trailing punctuation
                query = query.TrimEnd('.', '!', '?');
                if (!string.IsNullOrEmpty(query))
                    return query;
            }
            
            // Extract the actual query
            var match = Regex.Match(input, @"(?:play|listen to|hear)\s+(.+?)(?:\s+on\s+|\s*$)", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                var query = match.Groups[1].Value.Trim();
                Debug.WriteLine($"[ExtractMusicQuery] Matched standard pattern: '{query}'");
                // Remove platform name from query if it's at the end
                foreach (var platform in platformNames)
                {
                    if (query.ToLower().EndsWith($" on {platform}"))
                        query = query.Substring(0, query.Length - platform.Length - 4).Trim();
                    else if (query.ToLower() == platform)
                        return ""; // Just the platform name, no actual query
                }
                return query;
            }
            Debug.WriteLine($"[ExtractMusicQuery] No pattern matched, returning input: '{input}'");
            return input;
        }

        private static string DetectPlatform(string input)
        {
            var lower = input.ToLower();
            if (lower.Contains("spotify")) return "spotify";
            if (lower.Contains("youtube music")) return "youtube_music";
            if (lower.Contains("youtube")) return "youtube";
            if (lower.Contains("soundcloud")) return "soundcloud";
            if (lower.Contains("apple")) return "apple_music";
            return _userPreferences.GetValueOrDefault("default_music_platform", "spotify");
        }

        private static string ExtractMediaAction(string input)
        {
            if (input.Contains("pause") || input.Contains("stop")) return "pause";
            if (input.Contains("resume") || input.Contains("continue")) return "play";
            if (input.Contains("next") || input.Contains("skip")) return "next";
            if (input.Contains("previous") || input.Contains("back")) return "previous";
            return "pause";
        }

        private static string ExtractVolumeAction(string input)
        {
            if (input.Contains("up") || input.Contains("louder") || input.Contains("increase")) return "up";
            if (input.Contains("down") || input.Contains("quieter") || input.Contains("decrease")) return "down";
            if (input.Contains("mute")) return "mute";
            if (input.Contains("unmute")) return "unmute";
            return "up";
        }

        private static string ExtractAppName(string input)
        {
            var match = Regex.Match(input, @"(?:open|close|launch|start|kill|quit)\s+(.+)", RegexOptions.IgnoreCase);
            return match.Success ? match.Groups[1].Value.Trim() : "";
        }

        private static string ExtractPowerAction(string input)
        {
            if (input.Contains("shutdown") || input.Contains("shut down") || input.Contains("turn off")) return "shutdown";
            if (input.Contains("restart") || input.Contains("reboot")) return "restart";
            if (input.Contains("sleep")) return "sleep";
            if (input.Contains("lock")) return "lock";
            if (input.Contains("hibernate")) return "hibernate";
            return "shutdown";
        }

        private static string ExtractSearchQuery(string input)
        {
            var patterns = new[] { @"search\s+(?:for\s+)?(.+)", @"google\s+(.+)", @"look up\s+(.+)", @"what is\s+(.+)", @"who is\s+(.+)" };
            foreach (var pattern in patterns)
            {
                var match = Regex.Match(input, pattern, RegexOptions.IgnoreCase);
                if (match.Success) return match.Groups[1].Value.Trim();
            }
            return input;
        }
        
        /// <summary>
        /// Extract image generation prompt from user input
        /// </summary>
        private static string ExtractImagePrompt(string input)
        {
            var lower = input.ToLower();
            
            // Remove common prefixes
            var prefixes = new[]
            {
                "generate me an image of ", "generate an image of ", "generate image of ", 
                "generate me a picture of ", "generate a picture of ", "generate picture of ",
                "create me an image of ", "create an image of ", "create image of ", 
                "create me a picture of ", "create a picture of ", "create picture of ",
                "draw me an image of ", "draw an image of ", "draw image of ", 
                "draw me a picture of ", "draw a picture of ", "draw picture of ",
                "make me an image of ", "make an image of ", "make image of ", 
                "make me a picture of ", "make a picture of ", "make picture of ",
                "paint me ", "paint a ", "paint ",
                "illustrate ", "design ",
                "generate me ", "generate ", "create me ", "create ", "draw me ", "draw ", "make me ", "make "
            };

            var prompt = input;
            foreach (var prefix in prefixes)
            {
                if (lower.StartsWith(prefix))
                {
                    prompt = input.Substring(prefix.Length);
                    break;
                }
            }

            // Clean up
            return prompt.Trim().TrimEnd('.', '!', '?');
        }

        private static string ExtractLocation(string input)
        {
            var match = Regex.Match(input, @"(?:weather|temperature|forecast)\s+(?:in|for|at)\s+(.+)", RegexOptions.IgnoreCase);
            return match.Success ? match.Groups[1].Value.Trim() : "";
        }

        private static string ExtractFolderTarget(string input)
        {
            if (input.Contains("desktop")) return "desktop";
            if (input.Contains("download")) return "downloads";
            if (input.Contains("document")) return "documents";
            if (input.Contains("picture") || input.Contains("photo")) return "pictures";
            if (input.Contains("music")) return "music";
            if (input.Contains("video")) return "videos";
            return "desktop";
        }
        
        /// <summary>
        /// Extract system folder target from input (program data, appdata, etc.)
        /// </summary>
        private static string ExtractSystemFolderTarget(string input)
        {
            var lower = input.ToLower();
            
            // ProgramData
            if (lower.Contains("program data") || lower.Contains("programdata"))
                return "programdata";
            
            // AppData variants
            if (lower.Contains("appdata local") || lower.Contains("local appdata") || lower.Contains("localappdata"))
                return "localappdata";
            if (lower.Contains("appdata roaming") || lower.Contains("roaming"))
                return "roaming";
            if (lower.Contains("appdata") || lower.Contains("app data"))
                return "appdata";
            
            // Temp
            if (lower.Contains("temp"))
                return "temp";
            
            // Windows system folders
            if (lower.Contains("system32"))
                return "system32";
            if (lower.Contains("windows"))
                return "windows";
            
            // Program Files
            if (lower.Contains("program files x86") || lower.Contains("program files (x86)"))
                return "programfilesx86";
            if (lower.Contains("program files"))
                return "programfiles";
            
            return "programdata"; // Default
        }
        
        /// <summary>
        /// Check if a file path is an image file
        /// </summary>
        private static bool IsImageFile(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            var ext = Path.GetExtension(path).ToLower();
            return ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".gif" || 
                   ext == ".bmp" || ext == ".webp" || ext == ".tiff" || ext == ".ico";
        }

        private static string GenerateClarificationQuestion(string input)
        {
            if (input.Contains("play") && !Regex.IsMatch(input, @"play\s+\w+"))
                return "What would you like me to play?";
            if (input.Contains("open") && !Regex.IsMatch(input, @"open\s+\w+"))
                return "What app would you like me to open?";
            if (input.Contains("search") && !Regex.IsMatch(input, @"search\s+\w+"))
                return "What would you like me to search for?";
            return "Could you tell me more about what you'd like me to do?";
        }

        private static string GetContextSummary()
        {
            if (_recentContext.Count == 0) return "";
            var recent = _recentContext.TakeLast(3).Select(c => $"- {c.Action} {c.MainEntity}");
            return $"Recent actions:\n{string.Join("\n", recent)}";
        }

        private static string GetPreferencesContext()
        {
            if (_userPreferences.Count == 0) return "";
            var prefs = _userPreferences.Select(p => $"- {p.Key}: {p.Value}");
            return $"User preferences:\n{string.Join("\n", prefs)}";
        }

        private static int LevenshteinDistance(string s1, string s2)
        {
            var n = s1.Length;
            var m = s2.Length;
            var d = new int[n + 1, m + 1];

            for (var i = 0; i <= n; i++) d[i, 0] = i;
            for (var j = 0; j <= m; j++) d[0, j] = j;

            for (var i = 1; i <= n; i++)
            {
                for (var j = 1; j <= m; j++)
                {
                    var cost = s1[i - 1] == s2[j - 1] ? 0 : 1;
                    d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
                }
            }
            return d[n, m];
        }

        private static void LoadApiKey()
        {
            if (_apiKey != null) return;
            try
            {
                if (SettingsStore.TryGetAiProviderKey("openai", out var key))
                    _apiKey = key;
            }
            catch { }
        }

        private static void LoadUserPreferences()
        {
            try
            {
                if (File.Exists(PrefsPath))
                {
                    var json = File.ReadAllText(PrefsPath);
                    _userPreferences = JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new();
                }
            }
            catch { }
        }

        private static void SaveUserPreferences()
        {
            try
            {
                var dir = Path.GetDirectoryName(PrefsPath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir!);
                var json = JsonSerializer.Serialize(_userPreferences, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(PrefsPath, json);
            }
            catch { }
        }

        #endregion

        private class ConversationContext
        {
            public string Action { get; set; } = "";
            public string MainEntity { get; set; } = "";
            public string? Result { get; set; }
            public DateTime Timestamp { get; set; }
        }
    }
}
