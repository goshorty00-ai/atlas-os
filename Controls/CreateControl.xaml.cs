using Microsoft.Web.WebView2.Core;
using AtlasAI.AI;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace AtlasAI.Controls
{
    public partial class CreateControl : UserControl
    {
        private const string AppFolderName = "Premium Social Media Creator App (1)";
        private const string VirtualHostName = "atlas-social-creator";
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
        private bool _initialized;
        private readonly Dictionary<string, PendingAtlasRequest> _pendingAtlasRequests = new(StringComparer.OrdinalIgnoreCase);

        private sealed class PendingAtlasRequest
        {
            public PendingAtlasRequest(CancellationTokenSource cancellationTokenSource)
            {
                CancellationTokenSource = cancellationTokenSource;
            }

            public CancellationTokenSource CancellationTokenSource { get; }

            public bool CancelRequestedByUser { get; set; }
        }

        public CreateControl()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            IsVisibleChanged += OnVisibleChanged;
            Unloaded += OnUnloaded;
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (IsVisible)
                await TryInitAsync();
        }

        private async void OnVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if ((bool)e.NewValue)
                await TryInitAsync();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            try
            {
                if (CreateWebView?.CoreWebView2 != null)
                {
                    CreateWebView.CoreWebView2.NavigationCompleted -= OnNavigationCompleted;
                    CreateWebView.CoreWebView2.WebMessageReceived -= OnWebMessageReceived;
                }
            }
            catch
            {
            }

            foreach (var pending in _pendingAtlasRequests.Values)
            {
                try { pending.CancellationTokenSource.Cancel(); } catch { }
                try { pending.CancellationTokenSource.Dispose(); } catch { }
            }
            _pendingAtlasRequests.Clear();
        }

        private async Task TryInitAsync()
        {
            if (_initialized)
                return;

            _initialized = true;
            await InitializeAsync();
        }

        private async Task InitializeAsync()
        {
            try
            {
                if (!Dispatcher.CheckAccess())
                {
                    await Dispatcher.InvokeAsync(async () => await InitializeAsync());
                    return;
                }

                ShowLoadingOverlay(true);

                if (CreateWebView.CoreWebView2 == null)
                {
                    var userDataFolder = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "AtlasAI",
                        "WebView2",
                        "Create");

                    Directory.CreateDirectory(userDataFolder);
                    var env = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
                    await CreateWebView.EnsureCoreWebView2Async(env);
                }

                if (CreateWebView.CoreWebView2 == null)
                    return;

                try { CreateWebView.DefaultBackgroundColor = System.Drawing.Color.FromArgb(255, 11, 18, 32); } catch { }

                var settings = CreateWebView.CoreWebView2.Settings;
                settings.IsScriptEnabled = true;
                settings.IsWebMessageEnabled = true;
                settings.AreDefaultContextMenusEnabled = true;
                settings.AreDevToolsEnabled = true;
                settings.AreBrowserAcceleratorKeysEnabled = true;
                settings.IsStatusBarEnabled = false;

                var dist = FindCreatorDist();
                if (dist == null)
                {
                    CreateWebView.CoreWebView2.NavigateToString(BuildMissingDistHtml());
                    ShowLoadingOverlay(false);
                    return;
                }

                CreateWebView.CoreWebView2.NavigationCompleted -= OnNavigationCompleted;
                CreateWebView.CoreWebView2.NavigationCompleted += OnNavigationCompleted;
                CreateWebView.CoreWebView2.WebMessageReceived -= OnWebMessageReceived;
                CreateWebView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;
                CreateWebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    VirtualHostName,
                    dist,
                    CoreWebView2HostResourceAccessKind.Allow);

                var version = File.GetLastWriteTimeUtc(Path.Combine(dist, "index.html")).Ticks;
                CreateWebView.CoreWebView2.Navigate($"https://{VirtualHostName}/index.html#/?v={version}");
            }
            catch (Exception ex)
            {
                try
                {
                    CreateWebView.CoreWebView2?.NavigateToString(
                        $"<html><body style='margin:0;background:#0b1220;color:#ffb4b4;font-family:Segoe UI,Arial,sans-serif;padding:32px'><h2>Create module failed to load</h2><pre style='white-space:pre-wrap'>{System.Net.WebUtility.HtmlEncode(ex.ToString())}</pre></body></html>");
                }
                catch
                {
                }

                ShowLoadingOverlay(false);
            }
        }

        private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            ShowLoadingOverlay(false);
            PostMessage(new
            {
                type = "atlas.bridgeReady",
                payload = new
                {
                    ready = true,
                    timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                }
            });
        }

        private async void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                string raw;
                try { raw = e.TryGetWebMessageAsString(); }
                catch { raw = e.WebMessageAsJson; }
                if (string.IsNullOrWhiteSpace(raw))
                    return;

                using var doc = JsonDocument.Parse(raw);
                var root = doc.RootElement;
                if (!root.TryGetProperty("type", out var typeProp))
                    return;

                if (!root.TryGetProperty("payload", out var payload))
                    return;

                var type = typeProp.GetString() ?? string.Empty;
                switch (type)
                {
                    case "atlas.executeBrief":
                        await HandleAtlasExecuteBriefAsync(payload);
                        break;
                    case "atlas.cancelBrief":
                        HandleAtlasCancelBrief(payload);
                        break;
                }
            }
            catch (Exception ex)
            {
                PostMessage(new
                {
                    type = "atlas.requestFailed",
                    payload = new
                    {
                        requestId = string.Empty,
                        briefId = string.Empty,
                        providerId = string.Empty,
                        modelId = string.Empty,
                        errorMessage = ex.Message,
                        completedAt = DateTimeOffset.UtcNow.ToString("O"),
                    }
                });
            }
        }

        private async Task HandleAtlasExecuteBriefAsync(JsonElement payload)
        {
            var requestId = GetString(payload, "requestId");
            var briefId = GetString(payload, "briefId");
            var providerId = GetString(payload, "providerId");
            var modelId = GetString(payload, "modelId");
            var taskType = GetString(payload, "taskType");
            var objective = GetString(payload, "objective");
            var brief = GetString(payload, "brief");
            var requestPacket = GetString(payload, "requestPacket");
            var platformId = GetString(payload, "platformId");
            var targetSurface = GetString(payload, "targetSurface");
            var contentType = GetString(payload, "contentType");
            var draftTitle = GetString(payload, "draftTitle");
            var sceneName = GetString(payload, "sceneName");
            var selectedLayerText = GetString(payload, "selectedLayerText");

            if (string.IsNullOrWhiteSpace(requestId) || string.IsNullOrWhiteSpace(briefId))
                return;

            if (!TryMapProvider(providerId, out var providerType, out var providerError))
            {
                PostFailure(requestId, briefId, providerId, modelId, providerError, "provider-map");
                return;
            }

            var startedAt = DateTimeOffset.UtcNow.ToString("O");
            PostMessage(new
            {
                type = "atlas.requestStarted",
                payload = new
                {
                    requestId,
                    briefId,
                    providerId,
                    modelId,
                    startedAt,
                }
            });

            CancellationTokenSource? cts = null;
            PendingAtlasRequest? pendingRequest = null;
            try
            {
                cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));
                pendingRequest = new PendingAtlasRequest(cts);
                lock (_pendingAtlasRequests)
                {
                    if (_pendingAtlasRequests.TryGetValue(requestId, out var existing))
                    {
                        try { existing.CancellationTokenSource.Cancel(); } catch { }
                        try { existing.CancellationTokenSource.Dispose(); } catch { }
                    }

                    _pendingAtlasRequests[requestId] = pendingRequest;
                }

                var response = await ExecuteAtlasRequestAsync(
                    providerType,
                    modelId,
                    taskType,
                    objective,
                    brief,
                    requestPacket,
                    platformId,
                    targetSurface,
                    contentType,
                    draftTitle,
                    sceneName,
                    selectedLayerText,
                    cts.Token).ConfigureAwait(false);

                if (response.Success)
                {
                    PostMessage(new
                    {
                        type = "atlas.requestSucceeded",
                        payload = new
                        {
                            requestId,
                            briefId,
                            providerId = ToFrontendProviderId(response.Provider),
                            modelId = string.IsNullOrWhiteSpace(response.Model) ? modelId : response.Model,
                            responseText = response.Content,
                            routeSummary = response.RouteSummary,
                            tokensUsed = response.TokensUsed,
                            completedAt = DateTimeOffset.UtcNow.ToString("O"),
                        }
                    });
                }
                else
                {
                    PostFailure(requestId, briefId, providerId, modelId, response.Error, response.RouteSummary);
                }
            }
            catch (OperationCanceledException)
            {
                if (pendingRequest?.CancelRequestedByUser == true)
                {
                    PostMessage(new
                    {
                        type = "atlas.requestCancelled",
                        payload = new
                        {
                            requestId,
                            briefId,
                            providerId,
                            modelId,
                            routeSummary = "cancelled-by-user",
                            completedAt = DateTimeOffset.UtcNow.ToString("O"),
                        }
                    });
                }
                else
                {
                    PostFailure(requestId, briefId, providerId, modelId, "ATLAS request timed out before the provider returned a result.", "timeout");
                }
            }
            catch (Exception ex)
            {
                PostFailure(requestId, briefId, providerId, modelId, ex.Message, "exception");
            }
            finally
            {
                lock (_pendingAtlasRequests)
                {
                    if (_pendingAtlasRequests.TryGetValue(requestId, out var existing) && ReferenceEquals(existing, pendingRequest))
                    {
                        _pendingAtlasRequests.Remove(requestId);
                    }
                }

                try { cts?.Dispose(); } catch { }
            }
        }

        private void HandleAtlasCancelBrief(JsonElement payload)
        {
            var requestId = GetString(payload, "requestId");
            if (string.IsNullOrWhiteSpace(requestId))
                return;

            lock (_pendingAtlasRequests)
            {
                if (_pendingAtlasRequests.TryGetValue(requestId, out var pending))
                {
                    pending.CancelRequestedByUser = true;
                    try { pending.CancellationTokenSource.Cancel(); } catch { }
                }
            }
        }

        private static async Task<AIResponse> ExecuteAtlasRequestAsync(
            AIProviderType providerType,
            string modelId,
            string taskType,
            string objective,
            string brief,
            string requestPacket,
            string platformId,
            string targetSurface,
            string contentType,
            string draftTitle,
            string sceneName,
            string selectedLayerText,
            CancellationToken cancellationToken)
        {
            var systemPrompt = BuildAtlasSystemPrompt(taskType, platformId, targetSurface, contentType);
            var userPrompt = string.Join("\n\n", new[]
            {
                $"Objective: {objective}",
                string.IsNullOrWhiteSpace(draftTitle) ? null : $"Draft: {draftTitle}",
                string.IsNullOrWhiteSpace(sceneName) ? null : $"Scene: {sceneName}",
                string.IsNullOrWhiteSpace(selectedLayerText) ? null : $"Selected Layer Text: {selectedLayerText}",
                "ATLAS Packet:",
                requestPacket,
                string.IsNullOrWhiteSpace(brief) ? null : $"Creator Brief:\n{brief}",
            }.Where(value => !string.IsNullOrWhiteSpace(value)));

            var messages = new List<object>
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt },
            };

            var response = await AIManager.SendMessageAsync(new AIManager.AIRoutingRequest
            {
                Module = "SocialCreator",
                Messages = messages,
                MaxTokens = 1600,
                BucketHint = AIManager.AITaskBucket.Generation,
                PreferredProviderOverride = providerType,
                PreferredModelOverride = modelId ?? string.Empty,
                RuntimeContext = new AIManager.AIRuntimeContext
                {
                    ActiveModule = "SocialCreator",
                    ActivePage = "CreateStudio",
                    ModuleState = $"task={taskType};platform={platformId};surface={targetSurface};contentType={contentType}",
                    AdditionalInstructions = "Return production-ready creative output only. Do not describe what you would do. Follow the requested output structure exactly.",
                }
            }, cancellationToken).ConfigureAwait(false);

            return response;
        }

        private static string BuildAtlasSystemPrompt(string taskType, string platformId, string targetSurface, string contentType)
        {
            var structureInstruction = taskType switch
            {
                "caption" => "Return 3 to 5 numbered caption options. Each option should include a concise caption body and a CTA line in the format 'CTA: ...'.",
                "hook" => "Return 5 to 8 numbered hook options. Keep each hook compact, high-contrast, and immediately usable.",
                "script" => "Return 2 to 4 script options. Each option should use labeled sections such as Hook:, Beat 1:, Beat 2:, CTA:.",
                "carousel-structure" => "Return 2 to 4 carousel options. Each option should use labeled sections such as Cover:, Slide 1:, Slide 2:, CTA:.",
                "visual-concept" => "Return 2 to 4 concept options. Each option should use labeled sections such as Concept:, Palette:, Camera:, Scene 1:, CTA:.",
                "campaign-pack" => "Return 2 to 4 numbered variant options. Each option should include Hook:, Caption:, CTA:, and any scene or angle notes.",
                "platform-adaptation" => "Return platform rewrites with headings for each platform, for example Instagram:, TikTok:, YouTube:, LinkedIn:.",
                "voiceover" => "Return 2 to 4 voiceover script options. Each option should use labeled sections such as Hook:, Body:, CTA:.",
                _ => "Return numbered creative options with clear labels and ready-to-use copy.",
            };

            return string.Join(" ", new[]
            {
                "You are ATLAS, the real AI orchestration layer for Atlas Social Creator.",
                $"The active task is '{taskType}' for platform '{platformId}', target surface '{targetSurface}', and content type '{contentType}'.",
                structureInstruction,
                "Do not include markdown fences, analysis, or extra explanation outside the requested options.",
                "Keep the response directly usable inside an editor workflow.",
            });
        }

        private static bool TryMapProvider(string providerId, out AIProviderType providerType, out string errorMessage)
        {
            switch ((providerId ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "gpt":
                    providerType = AIProviderType.OpenAI;
                    errorMessage = string.Empty;
                    return true;
                case "claude":
                    providerType = AIProviderType.Claude;
                    errorMessage = string.Empty;
                    return true;
                case "gemini":
                    providerType = AIProviderType.Gemini;
                    errorMessage = string.Empty;
                    return true;
                case "elevenlabs":
                    providerType = AIProviderType.OpenAI;
                    errorMessage = "Direct ElevenLabs text execution is not available in the ATLAS creator bridge. Generate the script with GPT, Claude, or Gemini, then send it to Voice Studio for synthesis.";
                    return false;
                default:
                    providerType = AIProviderType.OpenAI;
                    errorMessage = $"Unsupported ATLAS provider '{providerId}'.";
                    return false;
            }
        }

        private static string ToFrontendProviderId(AIProviderType providerType)
        {
            return providerType switch
            {
                AIProviderType.OpenAI => "gpt",
                AIProviderType.Claude => "claude",
                AIProviderType.Gemini => "gemini",
                _ => providerType.ToString().ToLowerInvariant(),
            };
        }

        private static string GetString(JsonElement element, string propertyName)
        {
            return element.TryGetProperty(propertyName, out var property) ? property.GetString() ?? string.Empty : string.Empty;
        }

        private void PostFailure(string requestId, string briefId, string providerId, string modelId, string errorMessage, string routeSummary)
        {
            PostMessage(new
            {
                type = "atlas.requestFailed",
                payload = new
                {
                    requestId,
                    briefId,
                    providerId,
                    modelId,
                    errorMessage,
                    routeSummary,
                    completedAt = DateTimeOffset.UtcNow.ToString("O"),
                }
            });
        }

        private void PostMessage(object payload)
        {
            try
            {
                var json = JsonSerializer.Serialize(payload, JsonOptions);
                Dispatcher.BeginInvoke(() =>
                {
                    try { CreateWebView.CoreWebView2?.PostWebMessageAsJson(json); } catch { }
                });
            }
            catch
            {
            }
        }

        private void ShowLoadingOverlay(bool visible)
        {
            try
            {
                LoadingOverlay.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            }
            catch
            {
            }
        }

        private static string? FindCreatorDist()
        {
            foreach (var root in EnumerateCandidateRoots())
            {
                try
                {
                    var dist = Path.Combine(root, "Figma", AppFolderName, "dist");
                    if (File.Exists(Path.Combine(dist, "index.html")))
                        return dist;
                }
                catch
                {
                }
            }

            return null;
        }

        private static string[] EnumerateCandidateRoots()
        {
            var seen = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var roots = new System.Collections.Generic.List<string>();

            void Add(string? path)
            {
                if (string.IsNullOrWhiteSpace(path))
                    return;

                try
                {
                    var full = Path.GetFullPath(path);
                    if (seen.Add(full))
                        roots.Add(full);
                }
                catch
                {
                }
            }

            var baseDir = AppDomain.CurrentDomain.BaseDirectory ?? string.Empty;
            var cwd = Directory.GetCurrentDirectory();

            Add(baseDir);
            Add(cwd);

            try
            {
                var current = new DirectoryInfo(string.IsNullOrWhiteSpace(baseDir) ? cwd : baseDir);
                for (var i = 0; i < 6 && current != null; i++, current = current.Parent)
                    Add(current.FullName);
            }
            catch
            {
            }

            return roots.ToArray();
        }

        private static string BuildMissingDistHtml()
        {
            var candidates = string.Join("", EnumerateCandidateRoots()
                .Select(root => $"<li>{System.Net.WebUtility.HtmlEncode(Path.Combine(root, "Figma", AppFolderName, "dist"))}</li>"));

            return $@"<html>
<body style='margin:0;background:#0b1220;color:#d8e6f5;font-family:Segoe UI,Arial,sans-serif;padding:32px'>
  <div style='max-width:920px;margin:0 auto'>
    <div style='font-size:12px;letter-spacing:0.18em;color:#8aa0b8'>AI CREATE</div>
    <h1 style='margin:8px 0 12px;font-weight:600'>Premium Social Media Creator bundle not found</h1>
    <p style='color:#9fb3c8;line-height:1.6'>The host control is installed, but WebView2 could not find a built front-end bundle for <strong>{System.Net.WebUtility.HtmlEncode(AppFolderName)}</strong>.</p>
    <p style='color:#9fb3c8;line-height:1.6'>Build the app in <code>D:\Atlas.OS\Figma\{System.Net.WebUtility.HtmlEncode(AppFolderName)}</code> so that <code>dist\index.html</code> exists, then reopen AI CREATE.</p>
    <h3 style='margin-top:24px'>Checked paths</h3>
    <ul style='line-height:1.8'>{candidates}</ul>
  </div>
</body>
</html>";
        }
    }
}