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
using AtlasAI.Core;
using AtlasAI.Settings;

namespace AtlasAI.Understanding
{
    /// <summary>
    /// Converts user text into {intent, entities, confidence}
    /// Handles vague requests, typos, slang, and context references
    /// </summary>
    public class IntentClassifier
    {
        private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(10) };
        private readonly ContextStore _context;
        private string? _apiKey;

        // Intent categories with their patterns
        private static readonly Dictionary<string, string[]> IntentPatterns = new()
        {
            // Media & Entertainment
            ["play_music"] = new[] { "play", "listen", "put on", "throw on", "bump", "blast", "music", "song", "spotify", "youtube music" },
            ["play_video"] = new[] { "watch", "video", "youtube", "stream", "movie" },
            ["media_control"] = new[] { "pause", "stop", "resume", "next", "skip", "previous", "back" },
            ["volume_control"] = new[] { "volume", "louder", "quieter", "mute", "unmute", "turn up", "turn down" },
            
            // Apps & System
            ["open_app"] = new[] { "open", "launch", "start", "run", "fire up" },
            ["close_app"] = new[] { "close", "kill", "quit", "exit", "end", "stop" },
            ["power_control"] = new[] { "shutdown", "restart", "reboot", "sleep", "hibernate", "lock", "log off" },
            ["system_control"] = new[] { "brightness", "wifi", "bluetooth", "airplane", "night light" },
            
            // Files & Folders
            ["file_operation"] = new[] { "create", "delete", "move", "copy", "rename", "file", "folder" },
            ["organize_files"] = new[] { "organize", "sort", "clean up", "tidy", "arrange" },
            ["find_files"] = new[] { "find", "search", "locate", "where is" },
            ["open_folder"] = new[] { "open folder", "go to folder", "show folder", "downloads", "documents", "desktop" },
            
            // Information
            ["web_search"] = new[] { "search", "google", "look up", "what is", "who is", "how to", "find out" },
            ["weather"] = new[] { "weather", "temperature", "forecast", "rain", "sunny", "cold", "hot" },
            ["system_info"] = new[] { "battery", "disk space", "memory", "cpu", "processes", "running" },
            
            // Security
            ["security_scan"] = new[] { "scan", "virus", "malware", "spyware", "security check", "threat" },
            
            // Productivity
            ["screenshot"] = new[] { "screenshot", "capture screen", "screen capture", "snip" },
            ["reminder"] = new[] { "remind", "reminder", "alarm", "timer", "schedule" },
            ["clipboard"] = new[] { "clipboard", "copy", "paste", "copied" },
            
            // AI Features
            ["generate_image"] = new[] { "generate image", "create image", "draw", "paint", "illustrate", "make picture" },
            ["analyze_image"] = new[] { "what is this", "analyze", "describe", "explain image", "read text", "ocr" },
            
            // Code & Development
            ["code_help"] = new[] { "code", "programming", "debug", "error", "function", "script" },
            ["install_software"] = new[] { "install", "download", "get", "setup" },
            
            // General
            ["greeting"] = new[] { "hi", "hello", "hey", "good morning", "good evening", "what's up" },
            ["help"] = new[] { "help", "what can you do", "capabilities", "features" },
            ["unknown"] = Array.Empty<string>()
        };

        // Typo corrections
        private static readonly Dictionary<string, string[]> TypoMap = new()
        {
            ["play"] = new[] { "paly", "plya", "ply", "plau" },
            ["spotify"] = new[] { "spotfy", "spotiffy", "spotifi", "sptify" },
            ["youtube"] = new[] { "youtub", "yotube", "utube", "youube" },
            ["open"] = new[] { "opne", "opn", "oepn", "oen" },
            ["close"] = new[] { "clsoe", "closee", "clos", "colse" },
            ["search"] = new[] { "serach", "seach", "serch", "saerch" },
            ["shutdown"] = new[] { "shutdwon", "shutdonw", "shtdown" },
            ["organize"] = new[] { "orginize", "orgainze", "organiz", "oraganize" },
            ["download"] = new[] { "donwload", "downlaod", "downlod" },
            ["screenshot"] = new[] { "screenshoot", "screenahot", "screnshot" }
        };

        // Slang mappings
        private static readonly Dictionary<string, string> SlangMap = new()
        {
            ["put on"] = "play",
            ["throw on"] = "play",
            ["bump"] = "play",
            ["blast"] = "play",
            ["crank up"] = "play",
            ["fire up"] = "open",
            ["boot up"] = "open",
            ["kill"] = "close",
            ["nuke"] = "delete",
            ["turn it up"] = "volume up",
            ["turn it down"] = "volume down",
            ["shh"] = "mute",
            ["skip"] = "next",
            ["go back"] = "previous"
        };

        public IntentClassifier(ContextStore context)
        {
            _context = context;
            LoadApiKey();
        }

        /// <summary>
        /// Classify user input into structured intent
        /// </summary>
        public async Task<IntentResult> ClassifyAsync(string userInput)
        {
            Debug.WriteLine($"[IntentClassifier] Classifying: '{userInput}'");
            
            // Step 1: Normalize input (fix typos, apply slang mapping)
            var normalized = NormalizeInput(userInput);
            Debug.WriteLine($"[IntentClassifier] Normalized: '{normalized}'");
            
            // Step 2: Resolve context references ("it", "that", "again")
            normalized = _context.ResolveReference(normalized);
            Debug.WriteLine($"[IntentClassifier] After context resolution: '{normalized}'");
            
            // Step 3: Try fast local classification first
            var localResult = TryLocalClassification(normalized, userInput);
            if (localResult.Confidence >= 0.8f)
            {
                Debug.WriteLine($"[IntentClassifier] Local classification: {localResult.Intent} ({localResult.Confidence:P0})");
                return localResult;
            }
            
            // Step 4: Use AI for complex/ambiguous cases
            if (!string.IsNullOrEmpty(_apiKey))
            {
                try
                {
                    var aiResult = await ClassifyWithAIAsync(userInput, normalized);
                    if (aiResult != null && aiResult.Confidence > localResult.Confidence)
                    {
                        Debug.WriteLine($"[IntentClassifier] AI classification: {aiResult.Intent} ({aiResult.Confidence:P0})");
                        return aiResult;
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[IntentClassifier] AI error: {ex.Message}");
                }
            }
            
            return localResult;
        }

        /// <summary>
        /// Fast local pattern-based classification
        /// </summary>
        private IntentResult TryLocalClassification(string normalized, string original)
        {
            var result = new IntentResult();
            var lower = normalized.ToLower();
            var bestMatch = ("unknown", 0f);
            
            foreach (var (intent, patterns) in IntentPatterns)
            {
                foreach (var pattern in patterns)
                {
                    if (lower.Contains(pattern))
                    {
                        var confidence = CalculateConfidence(lower, pattern, intent);
                        if (confidence > bestMatch.Item2)
                        {
                            bestMatch = (intent, confidence);
                        }
                    }
                }
            }
            
            result.Intent = bestMatch.Item1;
            result.Confidence = bestMatch.Item2;
            
            // Extract entities based on intent
            result.Entities = ExtractEntities(normalized, result.Intent);
            
            // Infer goal
            result.InferredGoal = InferGoal(result.Intent, result.Entities);
            
            // Check if confirmation needed
            result.NeedsConfirmation = IsDestructiveAction(result.Intent, result.Entities);
            
            // Determine planned action
            result.PlannedAction = DeterminePlannedAction(result);
            
            return result;
        }

        /// <summary>
        /// AI-powered classification for complex cases
        /// </summary>
        private async Task<IntentResult?> ClassifyWithAIAsync(string original, string normalized)
        {
            var contextSummary = _context.GetContextSummary();
            
            var prompt = $@"Classify this user request into a structured intent.

User said: ""{original}""
Normalized: ""{normalized}""
Context: {contextSummary}

Respond with JSON only:
{{
  ""intent"": ""play_music|media_control|volume_control|open_app|close_app|power_control|file_operation|organize_files|find_files|web_search|weather|security_scan|screenshot|reminder|generate_image|code_help|install_software|greeting|help|unknown"",
  ""entities"": {{
    ""query"": ""main subject"",
    ""app"": ""application name"",
    ""action"": ""specific action"",
    ""target"": ""file/folder/url"",
    ""platform"": ""spotify/youtube/etc""
  }},
  ""confidence"": 0.0-1.0,
  ""inferred_goal"": ""what user wants to achieve"",
  ""needs_confirmation"": true/false,
  ""missing_capability"": ""null or what's missing""
}}";

            var requestBody = new
            {
                model = "gpt-3.5-turbo",
                messages = new[]
                {
                    new { role = "system", content = "You are an intent classifier. Respond only with valid JSON." },
                    new { role = "user", content = prompt }
                },
                max_tokens = 250,
                temperature = 0.1
            };

            var json = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions");
            request.Headers.Add("Authorization", $"Bearer {_apiKey}");
            request.Content = content;

            var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode) return null;

            var responseText = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(responseText);
            var messageContent = doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();

            return ParseAIResponse(messageContent);
        }

        private IntentResult? ParseAIResponse(string? response)
        {
            if (string.IsNullOrEmpty(response)) return null;

            try
            {
                response = response.Trim();
                if (response.StartsWith("```"))
                {
                    var lines = response.Split('\n').Skip(1).TakeWhile(l => !l.StartsWith("```"));
                    response = string.Join("\n", lines);
                }

                using var doc = JsonDocument.Parse(response);
                var root = doc.RootElement;

                var result = new IntentResult
                {
                    Intent = root.TryGetProperty("intent", out var i) ? i.GetString() ?? "unknown" : "unknown",
                    Confidence = root.TryGetProperty("confidence", out var c) ? c.GetSingle() : 0.5f,
                    NeedsConfirmation = root.TryGetProperty("needs_confirmation", out var nc) && nc.GetBoolean(),
                    InferredGoal = root.TryGetProperty("inferred_goal", out var ig) ? ig.GetString() : null,
                    MissingCapability = root.TryGetProperty("missing_capability", out var mc) && mc.ValueKind == JsonValueKind.String ? mc.GetString() : null
                };

                if (root.TryGetProperty("entities", out var entities))
                {
                    foreach (var prop in entities.EnumerateObject())
                    {
                        if (prop.Value.ValueKind == JsonValueKind.String)
                        {
                            var val = prop.Value.GetString();
                            if (!string.IsNullOrEmpty(val) && val != "null")
                                result.Entities[prop.Name] = val;
                        }
                    }
                }

                result.PlannedAction = DeterminePlannedAction(result);
                return result;
            }
            catch
            {
                return null;
            }
        }

        private string NormalizeInput(string input)
        {
            var words = input.ToLower().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var corrected = new List<string>();

            foreach (var word in words)
            {
                var found = false;
                foreach (var (correct, typos) in TypoMap)
                {
                    if (typos.Contains(word))
                    {
                        corrected.Add(correct);
                        found = true;
                        break;
                    }
                }
                if (!found) corrected.Add(word);
            }

            var result = string.Join(" ", corrected);

            // Apply slang mappings
            foreach (var (slang, standard) in SlangMap.OrderByDescending(x => x.Key.Length))
            {
                if (result.Contains(slang))
                    result = result.Replace(slang, standard);
            }

            return result;
        }

        private float CalculateConfidence(string input, string pattern, string intent)
        {
            var baseConfidence = 0.6f;
            
            // Exact match boost
            if (input.StartsWith(pattern) || input.EndsWith(pattern))
                baseConfidence += 0.15f;
            
            // Multiple pattern matches boost
            var patternCount = IntentPatterns[intent].Count(p => input.Contains(p));
            baseConfidence += Math.Min(patternCount * 0.05f, 0.15f);
            
            // Short, clear commands get higher confidence
            if (input.Split(' ').Length <= 4)
                baseConfidence += 0.1f;
            
            return Math.Min(baseConfidence, 0.95f);
        }

        private Dictionary<string, string> ExtractEntities(string input, string intent)
        {
            var entities = new Dictionary<string, string>();
            var lower = input.ToLower();

            switch (intent)
            {
                case "play_music":
                case "play_video":
                    entities["query"] = ExtractAfterKeywords(input, new[] { "play", "listen to", "put on" });
                    entities["platform"] = DetectPlatform(lower);
                    break;
                    
                case "open_app":
                case "close_app":
                    entities["app"] = ExtractAfterKeywords(input, new[] { "open", "close", "launch", "kill", "quit" });
                    break;
                    
                case "file_operation":
                case "organize_files":
                    entities["target"] = ExtractFolderOrFile(input);
                    entities["action"] = ExtractFileAction(lower);
                    break;
                    
                case "web_search":
                    entities["query"] = ExtractAfterKeywords(input, new[] { "search", "google", "look up", "find" });
                    break;
                    
                case "weather":
                    entities["location"] = ExtractLocation(input);
                    break;
                    
                case "power_control":
                    entities["action"] = ExtractPowerAction(lower);
                    break;
                    
                case "media_control":
                    entities["action"] = ExtractMediaAction(lower);
                    break;
                    
                case "volume_control":
                    entities["action"] = ExtractVolumeAction(lower);
                    break;
            }

            return entities.Where(kv => !string.IsNullOrEmpty(kv.Value))
                          .ToDictionary(kv => kv.Key, kv => kv.Value);
        }

        private string ExtractAfterKeywords(string input, string[] keywords)
        {
            foreach (var keyword in keywords)
            {
                var idx = input.ToLower().IndexOf(keyword);
                if (idx >= 0)
                {
                    var result = input.Substring(idx + keyword.Length).Trim();
                    // Remove platform suffixes
                    result = Regex.Replace(result, @"\s+(on|in|using)\s+(spotify|youtube|soundcloud).*$", "", RegexOptions.IgnoreCase);
                    return result;
                }
            }
            return input;
        }

        private string DetectPlatform(string input)
        {
            if (input.Contains("spotify")) return "spotify";
            if (input.Contains("youtube music")) return "youtube_music";
            if (input.Contains("youtube")) return "youtube";
            if (input.Contains("soundcloud")) return "soundcloud";
            if (input.Contains("apple music")) return "apple_music";
            return "";
        }

        private string ExtractFolderOrFile(string input)
        {
            var lower = input.ToLower();
            if (lower.Contains("downloads")) return "downloads";
            if (lower.Contains("documents")) return "documents";
            if (lower.Contains("desktop")) return "desktop";
            if (lower.Contains("pictures")) return "pictures";
            if (lower.Contains("music")) return "music";
            if (lower.Contains("videos")) return "videos";
            
            // Try to extract path
            var pathMatch = Regex.Match(input, @"[A-Za-z]:\\[^\s]+|~?/[^\s]+");
            if (pathMatch.Success) return pathMatch.Value;
            
            return "";
        }

        private string ExtractFileAction(string input)
        {
            if (input.Contains("create") || input.Contains("new")) return "create";
            if (input.Contains("delete") || input.Contains("remove")) return "delete";
            if (input.Contains("move")) return "move";
            if (input.Contains("copy")) return "copy";
            if (input.Contains("rename")) return "rename";
            if (input.Contains("organize") || input.Contains("sort")) return "organize";
            return "";
        }

        private string ExtractLocation(string input)
        {
            var match = Regex.Match(input, @"(?:weather|temperature|forecast)\s+(?:in|for|at)\s+(.+)", RegexOptions.IgnoreCase);
            return match.Success ? match.Groups[1].Value.Trim() : "";
        }

        private string ExtractPowerAction(string input)
        {
            if (input.Contains("shutdown") || input.Contains("shut down") || input.Contains("turn off")) return "shutdown";
            if (input.Contains("restart") || input.Contains("reboot")) return "restart";
            if (input.Contains("sleep")) return "sleep";
            if (input.Contains("hibernate")) return "hibernate";
            if (input.Contains("lock")) return "lock";
            if (input.Contains("log off") || input.Contains("sign out")) return "logoff";
            return "";
        }

        private string ExtractMediaAction(string input)
        {
            if (input.Contains("pause") || input.Contains("stop")) return "pause";
            if (input.Contains("resume") || input.Contains("play") || input.Contains("continue")) return "play";
            if (input.Contains("next") || input.Contains("skip")) return "next";
            if (input.Contains("previous") || input.Contains("back")) return "previous";
            return "";
        }

        private string ExtractVolumeAction(string input)
        {
            if (input.Contains("up") || input.Contains("louder") || input.Contains("increase")) return "up";
            if (input.Contains("down") || input.Contains("quieter") || input.Contains("decrease")) return "down";
            if (input.Contains("mute")) return "mute";
            if (input.Contains("unmute")) return "unmute";
            return "";
        }

        private string InferGoal(string intent, Dictionary<string, string> entities)
        {
            return intent switch
            {
                "play_music" => $"Play {entities.GetValueOrDefault("query", "music")}",
                "open_app" => $"Open {entities.GetValueOrDefault("app", "application")}",
                "close_app" => $"Close {entities.GetValueOrDefault("app", "application")}",
                "organize_files" => $"Organize files in {entities.GetValueOrDefault("target", "folder")}",
                "web_search" => $"Search for {entities.GetValueOrDefault("query", "information")}",
                "weather" => $"Get weather for {entities.GetValueOrDefault("location", "your location")}",
                "power_control" => $"{entities.GetValueOrDefault("action", "Control")} the computer",
                "security_scan" => "Scan system for threats",
                "screenshot" => "Take a screenshot",
                _ => "Help with your request"
            };
        }

        private bool IsDestructiveAction(string intent, Dictionary<string, string> entities)
        {
            // Actions that require confirmation
            if (intent == "power_control") return true;
            if (intent == "file_operation" && entities.GetValueOrDefault("action") == "delete") return true;
            if (intent == "close_app") return false; // Usually safe
            return false;
        }

        private string DeterminePlannedAction(IntentResult result)
        {
            if (result.NeedsConfirmation) return "confirm";
            if (!string.IsNullOrEmpty(result.MissingCapability)) return "guide";
            if (result.Confidence < 0.5f) return "clarify";
            if (result.Confidence >= 0.7f) return "execute";
            return "guide";
        }

        private void LoadApiKey()
        {
            try
            {
                if (SettingsStore.TryGetAiProviderKey("openai", out var key))
                    _apiKey = key;
            }
            catch { }
        }
    }
}
