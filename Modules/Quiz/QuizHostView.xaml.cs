using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using AtlasAI.AI;
using AtlasAI.Voice;
using Microsoft.Web.WebView2.Core;

namespace AtlasAI.Modules.Quiz
{
    public partial class QuizHostView : UserControl
    {
        private const string CleanMalformedJsonError = "AI returned malformed quiz JSON. Try again or adjust the quiz settings.";
        private const string CleanValidationError = "AI generated quiz data failed validation. Try again or adjust settings.";
        private const string CleanGenerationError = "AI quiz generation failed. Try again or adjust the quiz settings.";
        private const string CleanProviderUnavailableError = "AI provider is unavailable right now. Try again in a moment.";

        public QuizHostView()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            try
            {
                await EnsureInitializedAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[QuizHost] Initialization failed: {ex.Message}");
            }
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            try
            {
                if (QuizWebView?.CoreWebView2 != null)
                {
                    QuizWebView.CoreWebView2.NavigationCompleted -= CoreWebView2_NavigationCompleted;
                    QuizWebView.CoreWebView2.WebMessageReceived -= CoreWebView2_WebMessageReceived;
                }
            }
            catch
            {
            }
        }

        private async Task EnsureInitializedAsync()
        {
            if (QuizWebView?.CoreWebView2 != null)
                return;

            var userDataFolder = GetQuizUserDataFolder();
            Directory.CreateDirectory(userDataFolder);
            var environment = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
            await QuizWebView.EnsureCoreWebView2Async(environment);

            var settings = QuizWebView.CoreWebView2.Settings;
            settings.AreDefaultContextMenusEnabled = true;
            settings.AreDevToolsEnabled = true;
            settings.AreBrowserAcceleratorKeysEnabled = true;

            var dist = FindQuizDist();
            if (string.IsNullOrWhiteSpace(dist))
            {
                try { MissingUiOverlay.Visibility = Visibility.Visible; } catch { }
                System.Diagnostics.Debug.WriteLine("[QuizHost] AI Quiz dist not found.");
                return;
            }

            try { MissingUiOverlay.Visibility = Visibility.Collapsed; } catch { }

            QuizWebView.CoreWebView2.NavigationCompleted -= CoreWebView2_NavigationCompleted;
            QuizWebView.CoreWebView2.NavigationCompleted += CoreWebView2_NavigationCompleted;
            QuizWebView.CoreWebView2.WebMessageReceived -= CoreWebView2_WebMessageReceived;
            QuizWebView.CoreWebView2.WebMessageReceived += CoreWebView2_WebMessageReceived;

            QuizWebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                "quiz-ui",
                dist,
                CoreWebView2HostResourceAccessKind.Allow);

            long indexWriteTicks = 0;
            try
            {
                var indexPath = Path.Combine(dist, "index.html");
                if (File.Exists(indexPath))
                    indexWriteTicks = File.GetLastWriteTimeUtc(indexPath).Ticks;
            }
            catch
            {
            }

            var version = (indexWriteTicks != 0 ? indexWriteTicks : DateTime.UtcNow.Ticks).ToString();
            var url = $"https://quiz-ui/index.html?mode=quiz&v={version}";
            System.Diagnostics.Debug.WriteLine($"[QuizHost] Navigating to {url}");
            QuizWebView.CoreWebView2.Navigate(url);
        }

        private async void CoreWebView2_WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                var json = e.WebMessageAsJson;
                if (string.IsNullOrWhiteSpace(json))
                    return;

                using var document = JsonDocument.Parse(json);
                var root = document.RootElement;

                if (!root.TryGetProperty("type", out var typeElement))
                    return;

                var type = (typeElement.GetString() ?? string.Empty).Trim();
                var payload = root.TryGetProperty("payload", out var payloadElement)
                    ? payloadElement
                    : default;

                if (string.Equals(type, "quiz-generate-questions", StringComparison.OrdinalIgnoreCase))
                {
                    var request = ParseGenerateRequest(payload);
                    var result = await GenerateQuestionsAsync(request, CancellationToken.None);
                    Post("quiz-generate-questions-result", result);
                    return;
                }

                if (string.Equals(type, "quiz-log", StringComparison.OrdinalIgnoreCase))
                {
                    var message = payload.TryGetProperty("message", out var messageElement)
                        ? (messageElement.GetString() ?? string.Empty).Trim()
                        : string.Empty;
                    if (!string.IsNullOrWhiteSpace(message))
                    {
                        System.Diagnostics.Debug.WriteLine($"[AIQuiz] {message}");
                    }
                    return;
                }

                if (string.Equals(type, "quiz-speak", StringComparison.OrdinalIgnoreCase))
                {
                    var speakRequest = ParseSpeakRequest(payload);
                    if (string.IsNullOrWhiteSpace(speakRequest.Text))
                    {
                        Post("quiz-speak-result", new
                        {
                            ok = false,
                            requestId = speakRequest.RequestId,
                            error = "AI Voice is not available on this device.",
                        });
                        return;
                    }

                    var spoke = await TrySpeakAsync(speakRequest.Text, speakRequest.Reason);
                    Post("quiz-speak-result", new
                    {
                        ok = spoke,
                        requestId = speakRequest.RequestId,
                        error = spoke ? string.Empty : "AI Voice is not available on this device.",
                    });
                    return;
                }

                if (string.Equals(type, "quiz-stop-speech", StringComparison.OrdinalIgnoreCase))
                {
                    var reason = payload.TryGetProperty("reason", out var reasonElement)
                        ? (reasonElement.GetString() ?? string.Empty).Trim()
                        : string.Empty;
                    SpeechCoordinator.Instance.CancelCurrentSpeech();
                    System.Diagnostics.Debug.WriteLine($"[AIQuiz] Speech stop requested. reason={reason}");
                    return;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AIQuiz] WebMessage handler failed: {ex.GetType().Name}");
                Post("quiz-generate-questions-result", new
                {
                    ok = false,
                    questions = Array.Empty<object>(),
                    source = "Atlas AI",
                    error = CleanGenerationError,
                });
            }
        }

        private static QuizGenerateRequest ParseGenerateRequest(JsonElement payload)
        {
            var topic = (payload.TryGetProperty("topic", out var topicElement) ? topicElement.GetString() : "").NullSafeTrim();
            var difficulty = (payload.TryGetProperty("difficulty", out var difficultyElement) ? difficultyElement.GetString() : "Mixed").NullSafeTrim();
            var count = payload.TryGetProperty("count", out var countElement) && countElement.TryGetInt32(out var countValue)
                ? countValue
                : 5;
            var rounds = payload.TryGetProperty("rounds", out var roundsElement) && roundsElement.TryGetInt32(out var roundsValue)
                ? roundsValue
                : 1;
            var multipleChoice = payload.TryGetProperty("multipleChoice", out var mcElement) && mcElement.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? mcElement.GetBoolean()
                : true;
            var timeLimit = payload.TryGetProperty("timeLimit", out var tlElement) && tlElement.TryGetInt32(out var tlValue)
                ? tlValue
                : 30;
            var points = payload.TryGetProperty("points", out var pointsElement) && pointsElement.TryGetInt32(out var pointsValue)
                ? pointsValue
                : 100;

            var safeCount = Math.Clamp(count, 1, 50);

            return new QuizGenerateRequest
            {
                Topic = string.IsNullOrWhiteSpace(topic) ? "General Knowledge" : topic,
                Difficulty = string.IsNullOrWhiteSpace(difficulty) ? "Mixed" : difficulty,
                Count = safeCount,
                Rounds = Math.Clamp(rounds, 1, Math.Max(1, safeCount)),
                MultipleChoice = multipleChoice,
                TimeLimit = Math.Clamp(timeLimit, 10, 180),
                Points = Math.Clamp(points, 10, 1000),
            };
        }

        private static QuizSpeakRequest ParseSpeakRequest(JsonElement payload)
        {
            var requestId = (payload.TryGetProperty("requestId", out var requestIdElement) ? requestIdElement.GetString() : string.Empty).NullSafeTrim();
            var text = (payload.TryGetProperty("text", out var textElement) ? textElement.GetString() : string.Empty).NullSafeTrim();
            var reason = (payload.TryGetProperty("reason", out var reasonElement) ? reasonElement.GetString() : "quiz").NullSafeTrim();

            return new QuizSpeakRequest
            {
                RequestId = requestId,
                Text = text,
                Reason = string.IsNullOrWhiteSpace(reason) ? "quiz" : reason,
            };
        }

        private static async Task<bool> TrySpeakAsync(string text, string reason)
        {
            try
            {
                return await SpeechCoordinator.Instance.SpeakConversationAsync(text, null, reason);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AIQuiz] quiz-speak failed: {ex.GetType().Name}");
                return false;
            }
        }

        private static async Task<object> GenerateQuestionsAsync(QuizGenerateRequest request, CancellationToken cancellationToken)
        {
            var messages = BuildGenerationPrompt(request);
            var aiResponse = await AIManager.SendMessageAsync("Quiz", messages, 2400, cancellationToken);

            if (aiResponse == null || !aiResponse.Success)
            {
                System.Diagnostics.Debug.WriteLine("[AIQuiz] AI provider unavailable or request failed.");
                return new
                {
                    ok = false,
                    questions = Array.Empty<object>(),
                    source = "Atlas AI",
                    error = CleanProviderUnavailableError,
                };
            }

            var providerLabel = $"Atlas AI ({aiResponse.Provider}{(string.IsNullOrWhiteSpace(aiResponse.Model) ? "" : $"/{aiResponse.Model}")})";

            if (!TryParseQuestionsFromContent(aiResponse.Content, request, out var questions, out var validationError))
            {
                // One retry with a strict correction prompt
                var retryMessages = BuildRetryPrompt(request);
                var retryResponse = await AIManager.SendMessageAsync("Quiz", retryMessages, 2400, cancellationToken);

                if (retryResponse == null || !retryResponse.Success)
                {
                    System.Diagnostics.Debug.WriteLine($"[AIQuiz] Retry provider failure. reason={validationError}");
                    return new
                    {
                        ok = false,
                        questions = Array.Empty<object>(),
                        source = providerLabel,
                        error = CleanProviderUnavailableError,
                    };
                }

                if (!TryParseQuestionsFromContent(retryResponse.Content, request, out questions, out validationError))
                {
                    var isMalformed = IsMalformedJsonFailure(validationError);
                    System.Diagnostics.Debug.WriteLine($"[AIQuiz] Validation failed after retry. reason={validationError}");
                    return new
                    {
                        ok = false,
                        questions = Array.Empty<object>(),
                        source = providerLabel,
                        error = isMalformed ? CleanMalformedJsonError : CleanValidationError,
                    };
                }
            }

            return new
            {
                ok = true,
                questions,
                source = providerLabel,
                error = "",
            };
        }

        private static List<object> BuildGenerationPrompt(QuizGenerateRequest request)
        {
            var system =
                "You are a premium pub quiz host AI for a cinematic quiz game. " +
                "You generate high-quality, family-friendly trivia questions. " +
                "No profanity or offensive content. " +
                "CRITICAL: Return a JSON array ONLY. No markdown. No code fences. No prose before or after. " +
                "The raw response must start with [ and end with ].";

            var user =
                $"Generate EXACTLY {request.Count} quiz questions as a JSON array.\n" +
                $"Topic: {request.Topic}\n" +
                $"Difficulty: {request.Difficulty}\n" +
                $"Rounds: {request.Rounds} (distribute questions evenly, round field is 1-based up to {request.Rounds})\n" +
                $"Time limit: {request.TimeLimit} seconds per question\n" +
                $"Points: {request.Points} per question\n\n" +
                "RULES:\n" +
                "1. Every question must be unique — no repeated wording.\n" +
                "2. No near-duplicate questions that ask the same fact with minor wording changes.\n" +
                "3. Avoid overused obvious examples repeated across multiple questions.\n" +
                "4. Each question must have EXACTLY 4 options (for multiple choice).\n" +
                "5. correctAnswer is a 0-based integer (0=A, 1=B, 2=C, 3=D).\n" +
                $"6. CRITICAL: Across all {request.Count} questions, correctAnswer values MUST be spread across 0, 1, 2 and 3. Do NOT put the correct answer in the same position for every question.\n" +
                "7. All 4 options must be plausible — no obviously absurd wrong answers.\n" +
                "8. Include a short explanation (1-2 sentences) for each answer.\n" +
                "9. No duplicate options within a single question.\n" +
                "10. Family-friendly wording only; no profanity.\n" +
                "11. Vary sub-categories within the selected topic when suitable (for example, different eras, people, events, locations).\n" +
                "12. Avoid predictable answer index patterns.\n\n" +
                "Each item in the JSON array must have exactly these fields:\n" +
                "{\"id\":\"q1\",\"round\":1,\"category\":\"Topic Name\",\"difficulty\":\"Hard\"," +
                "\"question\":\"Question text?\",\"options\":[\"A text\",\"B text\",\"C text\",\"D text\"]," +
                "\"correctAnswer\":2,\"explanation\":\"Short explanation.\",\"points\":100,\"timeLimit\":30}\n\n" +
                "Return the JSON array only. Start your response with [";

            return new List<object>
            {
                new { role = "system", content = system },
                new { role = "user", content = user },
            };
        }

        private static List<object> BuildRetryPrompt(QuizGenerateRequest request)
        {
            var system =
                "You are a premium pub quiz host AI. " +
                "Return a JSON array ONLY. Start with [ and end with ]. No markdown, no code fences, no prose. " +
                "No profanity or offensive content.";

            var user =
                "Your previous response was not a valid JSON array.\n" +
                $"Please regenerate EXACTLY {request.Count} questions on the topic \"{request.Topic}\" at {request.Difficulty} difficulty.\n" +
                "Requirements:\n" +
                "- Exactly 4 options per question\n" +
                "- correctAnswer is 0-based integer (0, 1, 2, or 3)\n" +
                $"- Spread correctAnswer values across 0/1/2/3 — not all the same index across {request.Count} questions\n" +
                "- No duplicate question text\n" +
                "- No near-duplicate question text\n" +
                "- No duplicate options within one question\n" +
                "- Avoid repeatedly using the same obvious examples\n" +
                "- Vary sub-categories within the selected topic where possible\n" +
                "- Include explanation field\n" +
                "- Family-friendly wording only; no profanity\n" +
                "- round field between 1 and " + request.Rounds + "\n" +
                "Return JSON array only starting with [";

            return new List<object>
            {
                new { role = "system", content = system },
                new { role = "user", content = user },
            };
        }

        private static bool TryParseQuestionsFromContent(
            string content,
            QuizGenerateRequest request,
            out List<object> questions,
            out string error)
        {
            questions = new List<object>();
            error = "";

            var json = ExtractJsonArray(content);
            if (string.IsNullOrWhiteSpace(json))
            {
                error = "AI response did not contain a JSON array.";
                return false;
            }

            JsonArray? array;
            try
            {
                array = JsonNode.Parse(json) as JsonArray;
            }
            catch (Exception)
            {
                System.Diagnostics.Debug.WriteLine($"[AIQuiz] Parse failed. contentLength={(content ?? string.Empty).Length}");
                error = CleanMalformedJsonError;
                return false;
            }

            if (array == null || array.Count == 0)
            {
                error = "AI returned no questions.";
                return false;
            }

            // Accept if count is within 2 of requested (AI sometimes returns count ± 1)
            if (array.Count < Math.Max(1, request.Count - 2))
            {
                error = $"AI returned only {array.Count} questions, expected at least {Math.Max(1, request.Count - 2)}.";
                return false;
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var normalizedSeen = new List<string>();
            var tokenSets = new List<HashSet<string>>();
            var categoryCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < array.Count; i++)
            {
                if (array[i] is not JsonObject obj)
                {
                    error = $"Question #{i + 1} is not an object.";
                    return false;
                }

                var questionText = obj["question"]?.GetValue<string>()?.Trim() ?? "";
                if (string.IsNullOrWhiteSpace(questionText))
                {
                    error = $"Question #{i + 1} has empty question text.";
                    return false;
                }

                if (!seen.Add(questionText))
                {
                    error = $"Duplicate question detected at item #{i + 1}.";
                    return false;
                }

                var normalizedQuestion = NormalizeForComparison(questionText);
                if (normalizedSeen.Any(existing => string.Equals(existing, normalizedQuestion, StringComparison.OrdinalIgnoreCase)))
                {
                    error = $"Duplicate question detected at item #{i + 1}.";
                    return false;
                }

                var currentTokens = BuildTokenSet(normalizedQuestion);
                foreach (var existingTokens in tokenSets)
                {
                    var similarity = CalculateJaccardSimilarity(currentTokens, existingTokens);
                    if (similarity >= 0.90)
                    {
                        error = $"Near-duplicate question detected at item #{i + 1}.";
                        return false;
                    }
                }

                normalizedSeen.Add(normalizedQuestion);
                tokenSets.Add(currentTokens);

                var round = TryGetInt(obj, "round", 1);
                if (round < 1 || round > request.Rounds)
                {
                    error = $"Question #{i + 1} has invalid round {round}.";
                    return false;
                }

                var points = TryGetInt(obj, "points", request.Points);
                if (points <= 0)
                {
                    error = $"Question #{i + 1} has invalid points value.";
                    return false;
                }

                var timeLimit = TryGetInt(obj, "timeLimit", request.TimeLimit);
                if (timeLimit <= 0)
                {
                    error = $"Question #{i + 1} has invalid time limit value.";
                    return false;
                }

                var options = new List<string>();
                if (obj["options"] is JsonArray optionsArray)
                {
                    options = optionsArray
                        .Select(x => x?.GetValue<string>()?.Trim() ?? "")
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .ToList();
                }

                if (request.MultipleChoice && options.Count < 2)
                {
                    error = $"Question #{i + 1} has insufficient options for multiple choice.";
                    return false;
                }

                if (!request.MultipleChoice && options.Count < 1)
                {
                    error = $"Question #{i + 1} has no answer options.";
                    return false;
                }

                var correctAnswerIndex = TryGetInt(obj, "correctAnswer", -1);
                if (correctAnswerIndex < 0 || correctAnswerIndex >= options.Count)
                {
                    var correctAnswerText = obj["correctAnswer"]?.GetValue<string>()?.Trim() ?? "";
                    if (string.IsNullOrWhiteSpace(correctAnswerText))
                    {
                        error = $"Question #{i + 1} has empty correct answer.";
                        return false;
                    }

                    var foundIndex = options.FindIndex(x => string.Equals(x, correctAnswerText, StringComparison.OrdinalIgnoreCase));
                    if (foundIndex < 0)
                    {
                        options.Insert(0, correctAnswerText);
                        foundIndex = 0;
                    }

                    correctAnswerIndex = foundIndex;
                }

                var mapped = new
                {
                    id = (obj["id"]?.GetValue<string>()?.Trim()).NullSafeOr($"ai-q-{i + 1}"),
                    round,
                    category = (obj["category"]?.GetValue<string>()?.Trim()).NullSafeOr(request.Topic),
                    difficulty = (obj["difficulty"]?.GetValue<string>()?.Trim()).NullSafeOr(request.Difficulty),
                    question = questionText,
                    options,
                    correctAnswer = correctAnswerIndex,
                    explanation = (obj["explanation"]?.GetValue<string>()?.Trim()).NullSafeOr("No explanation provided."),
                    points,
                    timeLimit,
                };

                var categoryKey = NormalizeForComparison((string)mapped.category);
                if (!string.IsNullOrWhiteSpace(categoryKey))
                {
                    if (!categoryCounts.ContainsKey(categoryKey))
                        categoryCounts[categoryKey] = 0;
                    categoryCounts[categoryKey]++;
                }

                questions.Add(mapped);
            }

            if (questions.Count >= 6 && !TopicAppearsSingleCategory(request.Topic) && categoryCounts.Count > 0)
            {
                var maxCategoryCount = categoryCounts.Values.Max();
                if (maxCategoryCount > Math.Ceiling(questions.Count * 0.75))
                {
                    error = "category_distribution_invalid";
                    return false;
                }
            }

            var distribution = CalculateAnswerDistribution(questions);
            System.Diagnostics.Debug.WriteLine($"[AIQuiz] answerDistribution: A={distribution[0]} B={distribution[1]} C={distribution[2]} D={distribution[3]}");

            if (!IsAnswerDistributionAcceptable(distribution, questions.Count))
            {
                if (!TryRebalanceAnswerDistribution(questions, out var rebalancedQuestions))
                {
                    error = "answer_distribution_invalid";
                    return false;
                }

                questions = rebalancedQuestions;
                distribution = CalculateAnswerDistribution(questions);
                System.Diagnostics.Debug.WriteLine($"[AIQuiz] answerDistribution: A={distribution[0]} B={distribution[1]} C={distribution[2]} D={distribution[3]}");

                if (!IsAnswerDistributionAcceptable(distribution, questions.Count))
                {
                    error = "answer_distribution_invalid";
                    return false;
                }
            }

            if (questions.Count > request.Count)
            {
                questions = questions.Take(request.Count).ToList();
            }

            return true;
        }

        private static string NormalizeForComparison(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            var chars = text
                .ToLowerInvariant()
                .Select(ch => char.IsLetterOrDigit(ch) || char.IsWhiteSpace(ch) ? ch : ' ')
                .ToArray();

            return string.Join(" ", new string(chars)
                .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
        }

        private static HashSet<string> BuildTokenSet(string normalizedText)
        {
            if (string.IsNullOrWhiteSpace(normalizedText))
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            return normalizedText
                .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(token => token.Length > 2)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        private static double CalculateJaccardSimilarity(HashSet<string> left, HashSet<string> right)
        {
            if (left.Count == 0 || right.Count == 0)
                return 0;

            var intersection = left.Count(token => right.Contains(token));
            var union = left.Count + right.Count - intersection;
            return union == 0 ? 0 : (double)intersection / union;
        }

        private static bool TopicAppearsSingleCategory(string topic)
        {
            if (string.IsNullOrWhiteSpace(topic))
                return true;

            var normalized = topic.ToLowerInvariant();
            var hasMultiTopicHints =
                normalized.Contains(",", StringComparison.Ordinal) ||
                normalized.Contains(" and ", StringComparison.Ordinal) ||
                normalized.Contains("/", StringComparison.Ordinal) ||
                normalized.Contains("|", StringComparison.Ordinal) ||
                normalized.Contains(" plus ", StringComparison.Ordinal) ||
                normalized.Contains(" mixed ", StringComparison.Ordinal);

            return !hasMultiTopicHints;
        }

        private static bool IsMalformedJsonFailure(string error)
        {
            if (string.IsNullOrWhiteSpace(error))
                return false;

            if (string.Equals(error, CleanMalformedJsonError, StringComparison.OrdinalIgnoreCase))
                return true;

            return error.Contains("json array", StringComparison.OrdinalIgnoreCase);
        }

        private static int[] CalculateAnswerDistribution(List<object> questions)
        {
            var counts = new[] { 0, 0, 0, 0 };
            foreach (var q in questions)
            {
                try
                {
                    var idx = (int)((dynamic)q).correctAnswer;
                    if (idx >= 0 && idx < 4)
                        counts[idx]++;
                }
                catch
                {
                }
            }

            return counts;
        }

        private static bool IsAnswerDistributionAcceptable(int[] counts, int questionCount)
        {
            if (questionCount <= 0)
                return true;

            var max = counts.Max();

            if (questionCount >= 4 && max >= questionCount)
                return false;

            if (questionCount >= 8 && max > questionCount * 0.5)
                return false;

            return true;
        }

        private static bool TryRebalanceAnswerDistribution(List<object> questions, out List<object> rebalanced)
        {
            rebalanced = new List<object>();
            if (questions == null || questions.Count == 0)
                return false;

            var rng = new Random();
            var targetIndexes = new List<int>(questions.Count);
            for (var i = 0; i < questions.Count; i++)
                targetIndexes.Add(i % 4);

            for (var i = targetIndexes.Count - 1; i > 0; i--)
            {
                var j = rng.Next(i + 1);
                (targetIndexes[i], targetIndexes[j]) = (targetIndexes[j], targetIndexes[i]);
            }

            for (var i = 0; i < questions.Count; i++)
            {
                dynamic dq = questions[i];
                var options = ((List<string>)dq.options).ToList();
                var correctIndex = (int)dq.correctAnswer;

                if (options.Count < 2 || correctIndex < 0 || correctIndex >= options.Count)
                    return false;

                var correctText = options[correctIndex];
                var wrongOptions = options
                    .Where((opt, idx) => idx != correctIndex)
                    .ToList();

                for (var k = wrongOptions.Count - 1; k > 0; k--)
                {
                    var j = rng.Next(k + 1);
                    (wrongOptions[k], wrongOptions[j]) = (wrongOptions[j], wrongOptions[k]);
                }

                var target = targetIndexes[i] % options.Count;
                var newOptions = new List<string>(options.Count);
                var wrongCursor = 0;

                for (var pos = 0; pos < options.Count; pos++)
                {
                    if (pos == target)
                    {
                        newOptions.Add(correctText);
                    }
                    else
                    {
                        if (wrongCursor >= wrongOptions.Count)
                            return false;

                        newOptions.Add(wrongOptions[wrongCursor]);
                        wrongCursor++;
                    }
                }

                rebalanced.Add(new
                {
                    id = (string)dq.id,
                    round = (int)dq.round,
                    category = (string)dq.category,
                    difficulty = (string)dq.difficulty,
                    question = (string)dq.question,
                    options = newOptions,
                    correctAnswer = target,
                    explanation = (string)dq.explanation,
                    points = (int)dq.points,
                    timeLimit = (int)dq.timeLimit,
                });
            }

            return true;
        }

        private static string ExtractJsonArray(string content)
        {
            var text = StripCommonWrappers((content ?? string.Empty).Trim());
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            if (text.StartsWith("[") && text.EndsWith("]"))
                return text;

            var first = text.IndexOf('[');
            var last = text.LastIndexOf(']');
            if (first >= 0 && last > first)
                return text.Substring(first, last - first + 1);

            return string.Empty;
        }

        private static string StripCommonWrappers(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            var normalized = text.Replace("\r\n", "\n").Trim();
            if (!normalized.StartsWith("```", StringComparison.Ordinal))
                return normalized;

            var firstNewLine = normalized.IndexOf('\n');
            if (firstNewLine >= 0)
                normalized = normalized.Substring(firstNewLine + 1);

            normalized = normalized.Trim();
            if (normalized.EndsWith("```", StringComparison.Ordinal))
                normalized = normalized.Substring(0, normalized.Length - 3);

            return normalized.Trim();
        }

        private static int TryGetInt(JsonObject obj, string name, int fallback)
        {
            try
            {
                if (!obj.TryGetPropertyValue(name, out var node) || node == null)
                    return fallback;

                if (node is JsonValue value)
                {
                    if (value.TryGetValue<int>(out var i))
                        return i;

                    if (value.TryGetValue<string>(out var s) && int.TryParse(s, out var parsed))
                        return parsed;
                }
            }
            catch
            {
            }

            return fallback;
        }

        private void Post(string type, object payload)
        {
            try
            {
                if (QuizWebView?.CoreWebView2 == null)
                    return;

                var msg = JsonSerializer.Serialize(new { type, payload });
                QuizWebView.CoreWebView2.PostWebMessageAsJson(msg);
            }
            catch
            {
            }
        }

        private sealed class QuizGenerateRequest
        {
            public string Topic { get; init; } = "General Knowledge";
            public string Difficulty { get; init; } = "Mixed";
            public int Count { get; init; } = 5;
            public int Rounds { get; init; } = 1;
            public bool MultipleChoice { get; init; } = true;
            public int TimeLimit { get; init; } = 30;
            public int Points { get; init; } = 100;
        }

        private sealed class QuizSpeakRequest
        {
            public string RequestId { get; init; } = string.Empty;
            public string Text { get; init; } = string.Empty;
            public string Reason { get; init; } = "quiz";
        }

        private void CoreWebView2_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            try
            {
                MissingUiOverlay.Visibility = e.IsSuccess ? Visibility.Collapsed : Visibility.Visible;
                System.Diagnostics.Debug.WriteLine(e.IsSuccess
                    ? "[QuizHost] Navigation success."
                    : $"[QuizHost] Navigation failed. Error={e.WebErrorStatus}");
            }
            catch
            {
            }
        }

        private static string? FindQuizDist()
        {
            try
            {
                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                var shipped = Path.Combine(baseDir, "Figma", "Design AI Quiz Night Section", "dist");
                if (File.Exists(Path.Combine(shipped, "index.html")))
                    return shipped;

                var dir = baseDir;
                for (var i = 0; i < 10 && !string.IsNullOrWhiteSpace(dir); i++)
                {
                    var candidate = Path.Combine(dir, "Figma", "Design AI Quiz Night Section", "dist");
                    if (File.Exists(Path.Combine(candidate, "index.html")))
                        return candidate;

                    var parent = Directory.GetParent(dir);
                    if (parent == null) break;
                    dir = parent.FullName;
                }
            }
            catch
            {
            }

            return null;
        }

        private static string GetQuizUserDataFolder()
        {
            try
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "AtlasAI",
                    "WebView2",
                    "Quiz");
            }
            catch
            {
                return Path.Combine(Path.GetTempPath(), "AtlasOS_WebView2", "Quiz");
            }
        }
    }

    internal static class QuizHostStringExtensions
    {
        public static string NullSafeTrim(this string? value)
        {
            return (value ?? string.Empty).Trim();
        }

        public static string NullSafeOr(this string? value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value) ? fallback : value;
        }
    }
}
