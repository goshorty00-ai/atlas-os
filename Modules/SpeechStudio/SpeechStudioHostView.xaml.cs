using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using AtlasAI.Settings;
using Microsoft.Web.WebView2.Core;

namespace AtlasAI.Modules.SpeechStudio
{
    public partial class SpeechStudioHostView : UserControl
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };

        public SpeechStudioHostView()
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
            catch
            {
            }
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            try
            {
                if (SpeechStudioWebView?.CoreWebView2 != null)
                {
                    SpeechStudioWebView.CoreWebView2.NavigationCompleted -= CoreWebView2_NavigationCompleted;
                    SpeechStudioWebView.CoreWebView2.WebMessageReceived -= CoreWebView2_WebMessageReceived;
                }
            }
            catch
            {
            }
        }

        private async Task EnsureInitializedAsync()
        {
            if (SpeechStudioWebView?.CoreWebView2 != null)
                return;

            await SpeechStudioWebView.EnsureCoreWebView2Async();

            var settings = SpeechStudioWebView.CoreWebView2.Settings;
            settings.AreDefaultContextMenusEnabled = true;
            settings.AreDevToolsEnabled = true;
            settings.AreBrowserAcceleratorKeysEnabled = true;

            var dist = FindFigmaDist();
            if (string.IsNullOrWhiteSpace(dist))
            {
                try { MissingUiOverlay.Visibility = Visibility.Visible; } catch { }
                return;
            }

            try { MissingUiOverlay.Visibility = Visibility.Collapsed; } catch { }

            SpeechStudioWebView.CoreWebView2.NavigationCompleted -= CoreWebView2_NavigationCompleted;
            SpeechStudioWebView.CoreWebView2.NavigationCompleted += CoreWebView2_NavigationCompleted;
            SpeechStudioWebView.CoreWebView2.WebMessageReceived -= CoreWebView2_WebMessageReceived;
            SpeechStudioWebView.CoreWebView2.WebMessageReceived += CoreWebView2_WebMessageReceived;
            SpeechStudioWebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                "speech-studio-ui",
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
            SpeechStudioWebView.CoreWebView2.Navigate($"https://speech-studio-ui/index.html?mode=speech&v={version}");
        }

        private void CoreWebView2_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            try
            {
                MissingUiOverlay.Visibility = e.IsSuccess ? Visibility.Collapsed : Visibility.Visible;
                if (e.IsSuccess)
                    _ = PostStateAsync(CancellationToken.None);
            }
            catch
            {
            }
        }

        private async void CoreWebView2_WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                var json = e.WebMessageAsJson;
                System.Diagnostics.Debug.WriteLine($"[SpeechStudio] WebMessage: {json}");
                if (string.IsNullOrWhiteSpace(json))
                    return;

                using var document = JsonDocument.Parse(json);
                var root = document.RootElement;
                if (!root.TryGetProperty("type", out var typeElement))
                    return;

                var type = typeElement.GetString() ?? string.Empty;
                var payload = root.TryGetProperty("payload", out var payloadElement) ? payloadElement : default;

                switch (type)
                {
                    case "speech.getState":
                        await PostStateAsync(CancellationToken.None);
                        break;
                    case "speech.addEntry":
                        await AddEntryAsync(payload, CancellationToken.None);
                        break;
                    case "speech.removeEntry":
                        await RemoveEntryAsync(payload, CancellationToken.None);
                        break;
                    case "speech.addRule":
                        await AddRuleAsync(payload, CancellationToken.None);
                        break;
                    case "speech.removeRule":
                        await RemoveRuleAsync(payload, CancellationToken.None);
                        break;
                    case "speech.generatePreset":
                        await GeneratePresetAsync(payload, CancellationToken.None);
                        break;
                }
            }
            catch (Exception ex)
            {
                await PostAsync("speech.error", new { message = ex.Message }, CancellationToken.None);
            }
        }

        private async Task PostStateAsync(CancellationToken cancellationToken)
        {
            var settings = SettingsStore.Current;
            var payload = new
            {
                startupGreetings = settings.CustomStartupGreetings.Where(static item => !string.IsNullOrWhiteSpace(item)).Select(static item => item.Trim()).ToArray(),
                chatGreetings = settings.CustomChatGreetings.Where(static item => !string.IsNullOrWhiteSpace(item)).Select(static item => item.Trim()).ToArray(),
                quickResponses = settings.CustomQuickResponses.Where(static item => !string.IsNullOrWhiteSpace(item)).Select(static item => item.Trim()).ToArray(),
                customRules = settings.CustomSpeechRules
                    .Where(static item => item.Enabled && !string.IsNullOrWhiteSpace(item.Phrase) && !string.IsNullOrWhiteSpace(item.ResponseText))
                    .Select(static item => new { id = item.Id, phrase = item.Phrase.Trim(), responseText = item.ResponseText.Trim() })
                    .ToArray(),
                chaosIntensity = settings.UnfilteredChaosIntensity,
                allowProfanity = settings.UnfilteredAllowProfanity,
                startupPreview = settings.CustomStartupGreetings.FirstOrDefault(static item => !string.IsNullOrWhiteSpace(item)) ?? "Atlas is up. Let's get on with it.",
                chatPreview = settings.CustomChatGreetings.FirstOrDefault(static item => !string.IsNullOrWhiteSpace(item)) ?? "What do you need?",
                responsePreview = settings.CustomQuickResponses.FirstOrDefault(static item => !string.IsNullOrWhiteSpace(item)) ?? "Handled.",
            };

            await PostAsync("speech.state", payload, cancellationToken);
        }

        private async Task AddEntryAsync(JsonElement payload, CancellationToken cancellationToken)
        {
            var bucket = NormalizeBucket(payload.TryGetProperty("bucket", out var bucketElement) ? bucketElement.GetString() ?? string.Empty : string.Empty);
            var text = (payload.TryGetProperty("text", out var textElement) ? textElement.GetString() ?? string.Empty : string.Empty).Trim();
            System.Diagnostics.Debug.WriteLine($"[SpeechStudio] AddEntry bucket='{bucket}' text='{text}'");
            if (string.IsNullOrWhiteSpace(bucket) || string.IsNullOrWhiteSpace(text))
            {
                await PostAsync("speech.error", new { message = "Missing bucket or text." }, cancellationToken);
                return;
            }

            var settings = SettingsStore.Current;
            var target = GetBucket(settings, bucket);
            if (!target.Any(existing => string.Equals(existing?.Trim(), text, StringComparison.OrdinalIgnoreCase)))
            {
                target.Add(text);
                SettingsStore.Save(settings);
                System.Diagnostics.Debug.WriteLine($"[SpeechStudio] Added '{text}' to {bucket} (now {target.Count})");
            }

            await PostAsync("speech.result", new { message = $"Saved to {bucket}." }, cancellationToken);
            await PostStateAsync(cancellationToken);
        }

        private async Task RemoveEntryAsync(JsonElement payload, CancellationToken cancellationToken)
        {
            var bucket = NormalizeBucket(payload.TryGetProperty("bucket", out var bucketElement) ? bucketElement.GetString() ?? string.Empty : string.Empty);
            var text = (payload.TryGetProperty("text", out var textElement) ? textElement.GetString() ?? string.Empty : string.Empty).Trim();
            System.Diagnostics.Debug.WriteLine($"[SpeechStudio] RemoveEntry bucket='{bucket}' text='{text}'");
            if (string.IsNullOrWhiteSpace(bucket) || string.IsNullOrWhiteSpace(text))
            {
                await PostAsync("speech.error", new { message = "Missing bucket or text for removal." }, cancellationToken);
                return;
            }

            var settings = SettingsStore.Current;
            var target = GetBucket(settings, bucket);
            var removed = target.RemoveAll(existing => string.Equals(existing?.Trim(), text, StringComparison.OrdinalIgnoreCase));
            SettingsStore.Save(settings);
            System.Diagnostics.Debug.WriteLine($"[SpeechStudio] Removed {removed} items from {bucket}");

            await PostAsync("speech.result", new { message = $"Removed from {bucket}." }, cancellationToken);
            await PostStateAsync(cancellationToken);
        }

        private async Task AddRuleAsync(JsonElement payload, CancellationToken cancellationToken)
        {
            var phrase = (payload.TryGetProperty("phrase", out var phraseElement) ? phraseElement.GetString() ?? string.Empty : string.Empty).Trim();
            var responseText = (payload.TryGetProperty("responseText", out var responseElement) ? responseElement.GetString() ?? string.Empty : string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(phrase) || string.IsNullOrWhiteSpace(responseText))
                return;

            var settings = SettingsStore.Current;
            var existing = settings.CustomSpeechRules.FirstOrDefault(rule => string.Equals(rule.Phrase.Trim(), phrase, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                existing.ResponseText = responseText;
                existing.Enabled = true;
            }
            else
            {
                settings.CustomSpeechRules.Add(new SpeechPhraseResponseRule
                {
                    Phrase = phrase,
                    ResponseText = responseText,
                    Enabled = true,
                });
            }

            SettingsStore.Save(settings);
            await PostAsync("speech.result", new { message = $"Saved speech rule '{phrase}'." }, cancellationToken);
            await PostStateAsync(cancellationToken);
        }

        private async Task RemoveRuleAsync(JsonElement payload, CancellationToken cancellationToken)
        {
            var id = (payload.TryGetProperty("id", out var idElement) ? idElement.GetString() ?? string.Empty : string.Empty).Trim();
            var phrase = (payload.TryGetProperty("phrase", out var phraseElement) ? phraseElement.GetString() ?? string.Empty : string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(id) && string.IsNullOrWhiteSpace(phrase))
                return;

            var settings = SettingsStore.Current;
            settings.CustomSpeechRules.RemoveAll(rule =>
                (!string.IsNullOrWhiteSpace(id) && string.Equals(rule.Id, id, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrWhiteSpace(phrase) && string.Equals(rule.Phrase.Trim(), phrase, StringComparison.OrdinalIgnoreCase)));
            SettingsStore.Save(settings);

            await PostAsync("speech.result", new { message = "Removed speech rule." }, cancellationToken);
            await PostStateAsync(cancellationToken);
        }

        private async Task GeneratePresetAsync(JsonElement payload, CancellationToken cancellationToken)
        {
            var preset = (payload.TryGetProperty("preset", out var presetElement) ? presetElement.GetString() ?? string.Empty : string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(preset))
                return;

            var settings = SettingsStore.Current;
            var pack = GetPresetPack(preset);
            MergeLines(settings.CustomStartupGreetings, pack.StartupGreetings);
            MergeLines(settings.CustomChatGreetings, pack.ChatGreetings);
            MergeLines(settings.CustomQuickResponses, pack.QuickResponses);

            foreach (var rule in pack.Rules)
            {
                var existing = settings.CustomSpeechRules.FirstOrDefault(item => string.Equals(item.Phrase.Trim(), rule.Phrase, StringComparison.OrdinalIgnoreCase));
                if (existing != null)
                {
                    existing.ResponseText = rule.ResponseText;
                    existing.Enabled = true;
                    continue;
                }

                settings.CustomSpeechRules.Add(new SpeechPhraseResponseRule
                {
                    Phrase = rule.Phrase,
                    ResponseText = rule.ResponseText,
                    Enabled = true,
                });
            }

            SettingsStore.Save(settings);
            await PostAsync("speech.result", new { message = $"Generated {preset} speech pack." }, cancellationToken);
            await PostStateAsync(cancellationToken);
        }

        private static string NormalizeBucket(string bucket)
        {
            return (bucket ?? string.Empty).Trim() switch
            {
                "startupGreetings" => "startupGreetings",
                "chatGreetings" => "chatGreetings",
                "quickResponses" => "quickResponses",
                _ => string.Empty,
            };
        }

        private static System.Collections.Generic.List<string> GetBucket(AtlasSettings settings, string bucket)
        {
            return bucket switch
            {
                "startupGreetings" => settings.CustomStartupGreetings,
                "chatGreetings" => settings.CustomChatGreetings,
                _ => settings.CustomQuickResponses,
            };
        }

        private static void MergeLines(List<string> target, IEnumerable<string> additions)
        {
            foreach (var addition in additions.Select(static item => item.Trim()).Where(static item => !string.IsNullOrWhiteSpace(item)))
            {
                if (!target.Any(existing => string.Equals(existing?.Trim(), addition, StringComparison.OrdinalIgnoreCase)))
                    target.Add(addition);
            }
        }

        private static SpeechPresetPack GetPresetPack(string preset)
        {
            return preset.Equals("pro", StringComparison.OrdinalIgnoreCase)
                ? new SpeechPresetPack(
                    new[]
                    {
                        "System ready. What needs attention first?",
                        "Atlas online. Workflow is clear and standing by.",
                        "Everything is running. Tell me what you need.",
                        "Back at it. Give me the first task.",
                        "Systems nominal. Ready when you are.",
                    },
                    new[]
                    {
                        "Ready when you are.",
                        "Understood. Give me the next step.",
                        "Standing by. What's the ask?",
                        "Listening. Go ahead.",
                        "At your service. What do you need handled?",
                    },
                    new[]
                    {
                        "Handled.",
                        "On it.",
                        "Completed cleanly.",
                        "Done and dusted.",
                        "Taken care of.",
                        "Finished. What's next?",
                    },
                    new[]
                    {
                        new SpeechPresetRule("status report", "Systems are steady. Tell me what you want checked next."),
                        new SpeechPresetRule("focus mode", "Focus mode locked in. Keep it tight and give me the target."),
                        new SpeechPresetRule("good morning", "Good morning. Everything ran smoothly overnight. What's the plan?"),
                        new SpeechPresetRule("goodnight", "Rest well. I'll keep things running while you're out."),
                    })
                : new SpeechPresetPack(
                    new[]
                    {
                        "Right, I'm up. What shitstorm are we dealing with today?",
                        "Atlas is alive. Try not to break anything before I've had a chance to wake up.",
                        "Morning. The house didn't burn down. Let's keep that streak going.",
                        "Oi oi. What do you want?",
                        "I'm here. Unfortunately for both of us. What's the job?",
                        "Alright, alright, I'm awake. Stop poking me and tell me what you need.",
                    },
                    new[]
                    {
                        "Go on then, spit it out.",
                        "I'm listening. Make it worth my time.",
                        "What now? I was in the middle of absolutely nothing.",
                        "Yeah? What do you want? I haven't got all day.",
                        "Talk to me. And make it interesting for once.",
                        "Right, hit me. What's gone tits up this time?",
                    },
                    new[]
                    {
                        "Sorted.",
                        "Done. You're welcome.",
                        "Handled. Next.",
                        "Consider it done, genius.",
                        "Boom. Easy.",
                        "Yeah, already did it. Keep up.",
                        "Done. Try not to look so surprised.",
                    },
                    new[]
                    {
                        new SpeechPresetRule("full send", "Right then, full bloody send. Point me at the mess and stand back."),
                        new SpeechPresetRule("what's the damage", "Nothing fatal. Yet. Give me the job before that changes."),
                        new SpeechPresetRule("good morning", "Morning. Don't talk to me until I've warmed up properly. What's the play?"),
                        new SpeechPresetRule("goodnight", "Night night. Try not to need me at 3am again, yeah?"),
                        new SpeechPresetRule("thank you", "Don't go getting soft on me now. What's next?"),
                        new SpeechPresetRule("who are you", "I'm Atlas. I run this gaff. You just live here."),
                    });
        }

        private sealed record SpeechPresetRule(string Phrase, string ResponseText);

        private sealed record SpeechPresetPack(
            IReadOnlyList<string> StartupGreetings,
            IReadOnlyList<string> ChatGreetings,
            IReadOnlyList<string> QuickResponses,
            IReadOnlyList<SpeechPresetRule> Rules);

        private async Task PostAsync(string type, object payload, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (SpeechStudioWebView?.CoreWebView2 == null)
                return;

            var message = JsonSerializer.Serialize(new { type, payload }, JsonOptions);
            await Dispatcher.InvokeAsync(() => SpeechStudioWebView.CoreWebView2.PostWebMessageAsJson(message));
        }

        private static string? FindFigmaDist()
        {
            var candidates = new[]
            {
                Path.Combine("D:\\Atlas.OS", "Figma", "AI_Smart_Home", "dist"),
                Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "Figma", "AI_Smart_Home", "dist"),
                Path.Combine(AppContext.BaseDirectory, "Figma", "AI_Smart_Home", "dist"),
                Path.Combine(AppContext.BaseDirectory, "AI_Smart_Home", "dist"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Figma", "AI_Smart_Home", "dist"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "AI_Smart_Home", "dist"),
                Path.Combine(Directory.GetCurrentDirectory(), "Figma", "AI_Smart_Home", "dist"),
                Path.Combine(Directory.GetCurrentDirectory(), "AI_Smart_Home", "dist")
            };

            foreach (var candidate in candidates)
            {
                try
                {
                    if (File.Exists(Path.Combine(candidate, "index.html")))
                        return candidate;
                }
                catch
                {
                }
            }

            return null;
        }
    }
}