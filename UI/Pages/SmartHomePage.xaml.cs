using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using AtlasAI.AI;
using AtlasAI.Core;
using AtlasAI.Settings;
using AtlasAI.SmartHome;
using AtlasAI.UI.Controls;
using Microsoft.Web.WebView2.Core;

namespace AtlasAI.UI.Pages
{
    public partial class SmartHomePage : UserControl
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };
        private static readonly HttpClient EmbeddedBrowserProbeClient = new()
        {
            Timeout = TimeSpan.FromSeconds(3),
        };
        private static readonly string SmartHomeWebViewUserDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AtlasAI",
            "WebView2",
            "SmartHome");
        private static readonly string SmartHomeEmbeddedBrowserUserDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AtlasAI",
            "WebView2",
            "SmartHomeEmbedded");

        private sealed class CameraWorkspaceSession
        {
            public required string SessionId { get; init; }
            public required string Title { get; set; }
            public required string NavigationUrl { get; set; }
            public required string RecordingUrl { get; set; }
            public required string RecordingId { get; set; }
            public string ManagedCameraId { get; set; } = string.Empty;
            public string StatusMessage { get; set; } = "Opening camera view...";
        }

        private readonly SmartHomeRuntimeService _runtimeService = new();
        private readonly SmartHomeTextCommandService _textCommandService = new();
        private readonly SmartHomeCameraRecordingService _cameraRecordingService = new();
        private const string AnswerDoorCommandId = "atlas-answer-door";
        private CancellationTokenSource? _postStateDebounceCts;
        private string _pendingRingEmail = string.Empty;
        private string _pendingRingPassword = string.Empty;
        private string _pendingRingHardwareId = string.Empty;
        private CameraWorkspaceControl? _cameraWorkspaceControl;
        private readonly Dictionary<string, CameraWorkspaceSession> _cameraWorkspaceSessions = new(StringComparer.OrdinalIgnoreCase);
        private string _focusedCameraWorkspaceSessionId = string.Empty;
        private bool _embeddedBrowserInitialized;
        private readonly HashSet<string> _activeManagedRingCameraIds = new(StringComparer.OrdinalIgnoreCase);
        private readonly object _smartHomeMicrophoneLock = new();
        private int _smartHomeMicrophoneReservationCount;
        private bool _resumeWakeWordAfterSmartHomeMicrophone;

        public SmartHomePage()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        internal async Task ExecuteResolvedVoiceCommandAsync(SmartHomeTextCommandResult result, CancellationToken cancellationToken = default)
        {
            if (result == null || !result.Matched || !result.Ok)
                return;

            await EnsureInitializedAsync();
            await ExecuteUiIntentAsync(result, cancellationToken);
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
            var activeManagedRingCameraIds = _activeManagedRingCameraIds.ToArray();
            _activeManagedRingCameraIds.Clear();

            try
            {
                if (SmartHomeWebView?.CoreWebView2 != null)
                {
                    SmartHomeWebView.CoreWebView2.NavigationCompleted -= CoreWebView2_NavigationCompleted;
                    SmartHomeWebView.CoreWebView2.WebMessageReceived -= CoreWebView2_WebMessageReceived;
                    SmartHomeWebView.CoreWebView2.PermissionRequested -= SmartHomeWebView_PermissionRequested;
                }

                if (EmbeddedBrowserWebView?.CoreWebView2 != null)
                {
                    EmbeddedBrowserWebView.CoreWebView2.NavigationCompleted -= EmbeddedBrowserWebView_NavigationCompleted;
                    EmbeddedBrowserWebView.CoreWebView2.WebMessageReceived -= EmbeddedBrowserWebView_WebMessageReceived;
                    EmbeddedBrowserWebView.CoreWebView2.PermissionRequested -= EmbeddedBrowserWebView_PermissionRequested;
                }
            }
            catch
            {
            }

            if (_cameraWorkspaceControl != null)
            {
                try
                {
                    _ = _cameraWorkspaceControl.CloseAllSessionsAsync();
                }
                catch
                {
                }
                _cameraWorkspaceControl = null;
            }

            foreach (var activeManagedRingCameraId in activeManagedRingCameraIds)
                _ = _runtimeService.StopRingManagedLiveViewAsync(activeManagedRingCameraId, CancellationToken.None);

            ReleaseAllSmartHomeMicrophoneReservations();
        }

        private async System.Threading.Tasks.Task EnsureInitializedAsync()
        {
            if (SmartHomeWebView?.CoreWebView2 != null)
                return;

            Directory.CreateDirectory(SmartHomeWebViewUserDataFolder);
            var environment = await CoreWebView2Environment.CreateAsync(null, SmartHomeWebViewUserDataFolder);
            await SmartHomeWebView.EnsureCoreWebView2Async(environment);

            var settings = SmartHomeWebView.CoreWebView2.Settings;
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

            SmartHomeWebView.CoreWebView2.NavigationCompleted -= CoreWebView2_NavigationCompleted;
            SmartHomeWebView.CoreWebView2.NavigationCompleted += CoreWebView2_NavigationCompleted;
            SmartHomeWebView.CoreWebView2.WebMessageReceived -= CoreWebView2_WebMessageReceived;
            SmartHomeWebView.CoreWebView2.WebMessageReceived += CoreWebView2_WebMessageReceived;
            SmartHomeWebView.CoreWebView2.PermissionRequested -= SmartHomeWebView_PermissionRequested;
            SmartHomeWebView.CoreWebView2.PermissionRequested += SmartHomeWebView_PermissionRequested;
            SmartHomeWebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                "smart-home-ui",
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
            SmartHomeWebView.CoreWebView2.Navigate($"https://smart-home-ui/index.html?v={version}");
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

        private void SmartHomeWebView_PermissionRequested(object? sender, CoreWebView2PermissionRequestedEventArgs e)
        {
            AllowTrustedSmartHomeCapturePermission(e);
        }

        private void EmbeddedBrowserWebView_PermissionRequested(object? sender, CoreWebView2PermissionRequestedEventArgs e)
        {
            AllowTrustedSmartHomeCapturePermission(e);
        }

        private static void AllowTrustedSmartHomeCapturePermission(CoreWebView2PermissionRequestedEventArgs e)
        {
            if (e.PermissionKind != CoreWebView2PermissionKind.Microphone &&
                e.PermissionKind != CoreWebView2PermissionKind.Camera)
            {
                return;
            }

            if (!IsTrustedSmartHomeCaptureOrigin(e.Uri))
            {
                return;
            }

            e.State = CoreWebView2PermissionState.Allow;
        }

        private static bool IsTrustedSmartHomeCaptureOrigin(string? uriText)
        {
            if (string.IsNullOrWhiteSpace(uriText) || !Uri.TryCreate(uriText, UriKind.Absolute, out var uri))
            {
                return false;
            }

            if (string.Equals(uri.Host, "smart-home-ui", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return string.Equals(uri.Host, "127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase);
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

                var type = typeElement.GetString() ?? string.Empty;
                var payload = root.TryGetProperty("payload", out var payloadElement) ? payloadElement : default;

                switch (type)
                {
                    case "smart-home.getState":
                    case "smart-home.refresh":
                        await PostStateAsync(CancellationToken.None);
                        break;
                    case "smart-home.saveSettings":
                        ApplySettingsPatch(payload);
                        await Post("smart-home.settingsSaved", new { ok = true, savedAtUtc = DateTime.UtcNow }, CancellationToken.None);
                        await PostStateAsync(CancellationToken.None);
                        break;
                    case "smart-home.linkHueBridge":
                        await LinkHueBridgeAsync(payload, CancellationToken.None);
                        break;
                    case "smart-home.linkLgTv":
                        await LinkLgTvAsync(payload, CancellationToken.None);
                        break;
                    case "smart-home.discoverLgTv":
                        await DiscoverLgTvAsync(CancellationToken.None);
                        break;
                    case "smart-home.discoverNetwork":
                        await DiscoverNetworkAsync(CancellationToken.None);
                        break;
                    case "smart-home.loginRing":
                        await LoginRingAsync(payload, CancellationToken.None);
                        break;
                    case "smart-home.executeAction":
                        await ExecuteActionAsync(payload, CancellationToken.None);
                        break;
                    case "smart-home.runCommand":
                        await RunTextCommandAsync(payload, CancellationToken.None);
                        break;
                    case "smart-home.askAtlas":
                        await AskAtlasAsync(payload, CancellationToken.None);
                        break;
                    case "smart-home.saveCustomCommand":
                        await SaveCustomCommandAsync(payload, CancellationToken.None);
                        break;
                    case "smart-home.deleteCustomCommand":
                        await DeleteCustomCommandAsync(payload, CancellationToken.None);
                        break;
                    case "smart-home.saveScene":
                        await SaveSceneAsync(payload, CancellationToken.None);
                        break;
                    case "smart-home.deleteScene":
                        await DeleteSceneAsync(payload, CancellationToken.None);
                        break;
                    case "smart-home.runScene":
                        await RunSceneAsync(payload, CancellationToken.None);
                        break;
                    case "smart-home.executeScenePreview":
                        await ExecuteScenePreviewAsync(payload, CancellationToken.None);
                        break;
                    case "smart-home.saveCustomGreeting":
                        await SaveCustomGreetingAsync(payload, CancellationToken.None);
                        break;
                    case "smart-home.deleteCustomGreeting":
                        await DeleteCustomGreetingAsync(payload, CancellationToken.None);
                        break;
                    case "smart-home.generateGreetingPreset":
                        await GenerateGreetingPresetAsync(payload, CancellationToken.None);
                        break;
                    case "smart-home.saveAgentSettings":
                        await SaveAgentSettingsAsync(payload, CancellationToken.None);
                        break;
                    case "smart-home.createAutomation":
                        await CreateAutomationAsync(payload, CancellationToken.None);
                        break;
                    case "smart-home.toggleAutomation":
                        await ToggleAutomationAsync(payload, CancellationToken.None);
                        break;
                    case "smart-home.deleteAutomation":
                        await DeleteAutomationAsync(payload, CancellationToken.None);
                        break;
                    case "smart-home.runAutomation":
                        await RunAutomationAsync(payload, CancellationToken.None);
                        break;
                    case "smart-home.openExternalUrl":
                        await OpenExternalUrlAsync(payload);
                        break;
                    case "smart-home.startRingLiveSession":
                        await StartRingLiveSessionAsync(payload, CancellationToken.None);
                        break;
                    case "smart-home.stopRingLiveSession":
                        await StopRingLiveSessionAsync(payload, CancellationToken.None);
                        break;
                    case "smart-home.activateRingLiveSessionSpeaker":
                        await ActivateRingLiveSessionSpeakerAsync(payload, CancellationToken.None);
                        break;
                    case "smart-home.requestMicrophoneAccess":
                        await RequestMicrophoneAccessAsync(payload, CancellationToken.None);
                        break;
                    case "smart-home.releaseMicrophoneAccess":
                        await ReleaseMicrophoneAccessAsync(payload, CancellationToken.None);
                        break;
                    case "smart-home.startRingManagedLiveView":
                        await StartRingManagedLiveViewAsync(payload, CancellationToken.None);
                        break;
                    case "smart-home.stopRingManagedLiveView":
                        await StopRingManagedLiveViewAsync(payload, CancellationToken.None);
                        break;
                    case "smart-home.startCameraRecording":
                        await StartCameraRecordingAsync(payload, CancellationToken.None);
                        break;
                    case "smart-home.stopCameraRecording":
                        await StopCameraRecordingAsync(payload, CancellationToken.None);
                        break;
                }
            }
            catch (Exception ex)
            {
                try
                {
                    var json = e.WebMessageAsJson;
                    if (!string.IsNullOrWhiteSpace(json))
                    {
                        using var errorDocument = JsonDocument.Parse(json);
                        var errorRoot = errorDocument.RootElement;
                        var errorType = errorRoot.TryGetProperty("type", out var errorTypeElement) ? errorTypeElement.GetString() ?? string.Empty : string.Empty;
                        var errorPayload = errorRoot.TryGetProperty("payload", out var errorPayloadElement) ? errorPayloadElement : default;
                        var requestId = errorPayload.ValueKind == JsonValueKind.Object && errorPayload.TryGetProperty("requestId", out var errorRequestIdElement)
                            ? errorRequestIdElement.GetString() ?? string.Empty
                            : string.Empty;

                        if ((!string.IsNullOrWhiteSpace(requestId)) &&
                            (string.Equals(errorType, "smart-home.startRingLiveSession", StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(errorType, "smart-home.stopRingLiveSession", StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(errorType, "smart-home.activateRingLiveSessionSpeaker", StringComparison.OrdinalIgnoreCase)))
                        {
                            var errorCameraId = errorPayload.ValueKind == JsonValueKind.Object && errorPayload.TryGetProperty("deviceId", out var errorCameraIdElement)
                                ? errorCameraIdElement.GetString() ?? string.Empty
                                : string.Empty;
                            await Post("smart-home.ringLiveSessionFailed", new { requestId, cameraId = errorCameraId, message = ex.Message }, CancellationToken.None);
                            return;
                        }

                        if ((!string.IsNullOrWhiteSpace(requestId)) &&
                            (string.Equals(errorType, "smart-home.requestMicrophoneAccess", StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(errorType, "smart-home.releaseMicrophoneAccess", StringComparison.OrdinalIgnoreCase)))
                        {
                            var errorCameraId = errorPayload.ValueKind == JsonValueKind.Object && errorPayload.TryGetProperty("deviceId", out var errorMicrophoneCameraIdElement)
                                ? errorMicrophoneCameraIdElement.GetString() ?? string.Empty
                                : string.Empty;
                            await Post("smart-home.microphoneAccessFailed", new { requestId, cameraId = errorCameraId, message = ex.Message }, CancellationToken.None);
                            return;
                        }

                        if ((!string.IsNullOrWhiteSpace(requestId)) &&
                            (string.Equals(errorType, "smart-home.startRingManagedLiveView", StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(errorType, "smart-home.stopRingManagedLiveView", StringComparison.OrdinalIgnoreCase)))
                        {
                            var errorCameraId = errorPayload.ValueKind == JsonValueKind.Object && errorPayload.TryGetProperty("deviceId", out var errorManagedCameraIdElement)
                                ? errorManagedCameraIdElement.GetString() ?? string.Empty
                                : string.Empty;
                            await Post("smart-home.ringManagedLiveViewFailed", new { requestId, cameraId = errorCameraId, message = ex.Message }, CancellationToken.None);
                            return;
                        }
                    }
                }
                catch
                {
                }

                await Post("smart-home.error", new { message = ex.Message }, CancellationToken.None);
            }
        }

        private async Task PostStateAsync(CancellationToken cancellationToken)
        {
            // Debounce rapid sequential calls — only the last one within a 300ms window executes.
            _postStateDebounceCts?.Cancel();
            var debounceCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _postStateDebounceCts = debounceCts;
            try
            {
                await Task.Delay(300, debounceCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return; // A newer call superseded this one.
            }

            var snapshot = await _runtimeService.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                AppLogger.LogInfo($"[SmartHome] Posting snapshot: providers={snapshot.Providers.Count}, configured={snapshot.ConfiguredProviders}, devices={snapshot.TotalDevices}, online={snapshot.OnlineDevices}");
            }
            catch
            {
            }
            await Post("smart-home.state", snapshot, cancellationToken);
        }

        private async Task ExecuteActionAsync(JsonElement payload, CancellationToken cancellationToken)
        {
            var request = ParseActionRequest(payload);
            var result = await _runtimeService.ExecuteActionAsync(request, cancellationToken).ConfigureAwait(false);

            if (!result.Ok)
            {
                await Post("smart-home.error", new { message = result.Message }, cancellationToken).ConfigureAwait(false);
                return;
            }

            await Post("smart-home.actionResult", new { ok = true, message = result.Message }, cancellationToken).ConfigureAwait(false);
            _runtimeService.InvalidateSnapshotCache();
            await PostStateAsync(cancellationToken).ConfigureAwait(false);
        }

        private async Task RunTextCommandAsync(JsonElement payload, CancellationToken cancellationToken)
        {
            var text = payload.TryGetProperty("text", out var textElement) ? textElement.GetString() ?? string.Empty : string.Empty;
            var result = await _textCommandService.ExecuteAsync(text, cancellationToken).ConfigureAwait(false);

            if (!result.Matched)
            {
                await Post("smart-home.error", new { message = "That command did not match a live Smart Home action yet." }, cancellationToken).ConfigureAwait(false);
                return;
            }

            if (!result.Ok)
            {
                await Post("smart-home.error", new { message = result.Message }, cancellationToken).ConfigureAwait(false);
                return;
            }

            try
            {
                await ExecuteUiIntentAsync(result, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                await Post("smart-home.error", new { message = ex.Message }, cancellationToken).ConfigureAwait(false);
                return;
            }

            await Post("smart-home.actionResult", new { ok = true, message = result.Message }, cancellationToken).ConfigureAwait(false);
            _runtimeService.InvalidateSnapshotCache();
            await PostStateAsync(cancellationToken).ConfigureAwait(false);
        }

        private async Task AskAtlasAsync(JsonElement payload, CancellationToken cancellationToken)
        {
            var prompt = payload.TryGetProperty("prompt", out var promptElement) ? promptElement.GetString() ?? string.Empty : string.Empty;
            var providerId = payload.TryGetProperty("providerId", out var providerIdElement) ? providerIdElement.GetString() ?? string.Empty : string.Empty;
            var deviceId = payload.TryGetProperty("deviceId", out var deviceIdElement) ? deviceIdElement.GetString() ?? string.Empty : string.Empty;

            if (string.IsNullOrWhiteSpace(prompt))
            {
                await Post("smart-home.error", new { message = "Ask Atlas needs a prompt first." }, cancellationToken).ConfigureAwait(false);
                return;
            }

            var snapshot = await _runtimeService.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
            var context = BuildAskAtlasContext(snapshot, providerId, deviceId);
            var messages = new List<object>
            {
                new
                {
                    role = "system",
                    content = "You are Atlas, a concise Smart Home assistant inside Atlas OS. Use the provided live Smart Home context only. Suggest actions and automations that fit the devices Atlas already has. If something cannot be done directly from Atlas yet, say that plainly and give the nearest working option."
                },
                new
                {
                    role = "user",
                    content = $"{context}\n\nUser request: {prompt.Trim()}"
                },
            };

            var response = await AIManager.SendMessageAsync("SmartHome", messages, 700, cancellationToken).ConfigureAwait(false);
            if (response == null || !response.Success || string.IsNullOrWhiteSpace(response.Content))
            {
                var errorMessage = response?.Error;
                await Post("smart-home.error", new
                {
                    message = string.IsNullOrWhiteSpace(errorMessage)
                        ? "Atlas could not get an AI response for that Smart Home request."
                        : errorMessage,
                }, cancellationToken).ConfigureAwait(false);
                return;
            }

            await Post("smart-home.actionResult", new { ok = true, message = response.Content.Trim() }, cancellationToken).ConfigureAwait(false);
        }

        private static string BuildAskAtlasContext(SmartHomeSnapshot snapshot, string providerId, string deviceId)
        {
            var lines = new List<string>
            {
                $"Configured providers: {snapshot.ConfiguredProviders}",
                $"Total devices: {snapshot.TotalDevices}",
                $"Online devices: {snapshot.OnlineDevices}",
                $"Saved scenes: {snapshot.CustomScenes.Count}",
                $"Saved automations: {snapshot.Automations.Count}",
            };

            if (!string.IsNullOrWhiteSpace(providerId) && !string.IsNullOrWhiteSpace(deviceId))
            {
                SmartHomeProviderState? targetProvider = null;
                SmartHomeDevice? targetDevice = null;

                foreach (var provider in snapshot.Providers)
                {
                    if (!string.Equals(provider.ProviderId, providerId, StringComparison.OrdinalIgnoreCase))
                        continue;

                    targetProvider = provider;
                    foreach (var device in provider.Devices)
                    {
                        if (string.Equals(device.DeviceId, deviceId, StringComparison.OrdinalIgnoreCase))
                        {
                            targetDevice = device;
                            break;
                        }
                    }

                    break;
                }

                if (targetProvider != null && targetDevice != null)
                {
                    lines.Add($"Target provider: {targetProvider.DisplayName}");
                    lines.Add($"Target device: {targetDevice.Name}");
                    lines.Add($"Target online: {(targetDevice.IsOnline == false ? "no" : "yes")}");

                    var capabilityLines = new List<string>();
                    var capabilityCount = 0;
                    foreach (var capability in targetDevice.Capabilities)
                    {
                        capabilityLines.Add($"- {capability.Instance} ({capability.Type}) state={FormatCapabilityValue(capability.StateValue)}");
                        capabilityCount++;
                        if (capabilityCount >= 12)
                            break;
                    }

                    if (capabilityLines.Count > 0)
                    {
                        lines.Add("Target capabilities:");
                        lines.AddRange(capabilityLines);
                    }
                }
            }

            return string.Join(Environment.NewLine, lines);
        }

        private static string FormatCapabilityValue(JsonElement value)
        {
            return value.ValueKind switch
            {
                JsonValueKind.String => value.GetString() ?? string.Empty,
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                JsonValueKind.Number => value.ToString(),
                JsonValueKind.Object => value.GetRawText(),
                JsonValueKind.Array => value.GetRawText(),
                _ => "null",
            };
        }

        private async Task ExecuteUiIntentAsync(SmartHomeTextCommandResult result, CancellationToken cancellationToken)
        {
            var launchTarget = await ResolveCameraLaunchTargetAsync(result, cancellationToken).ConfigureAwait(false);
            await FocusCameraInSmartHomeUiAsync(launchTarget, null, null, cancellationToken).ConfigureAwait(false);

            if (launchTarget.UseManagedRingLiveView && !string.IsNullOrWhiteSpace(launchTarget.CameraDeviceId))
            {
                var liveViewResult = await _runtimeService.StartRingManagedLiveViewAsync(launchTarget.CameraDeviceId, cancellationToken).ConfigureAwait(false);
                if (!liveViewResult.Ok || string.IsNullOrWhiteSpace(liveViewResult.PlayerUrl))
                    throw new InvalidOperationException(string.IsNullOrWhiteSpace(liveViewResult.Message) ? "Atlas could not open that camera." : liveViewResult.Message);

                _activeManagedRingCameraIds.Add(liveViewResult.CameraId);
                await FocusCameraInSmartHomeUiAsync(launchTarget, liveViewResult.PlayerUrl, liveViewResult.ManifestUrl, cancellationToken).ConfigureAwait(false);
                await OpenCameraWorkspaceAsync(
                    liveViewResult.CameraId,
                    liveViewResult.PlayerUrl,
                    string.IsNullOrWhiteSpace(launchTarget.CameraName) ? "Ring Live View" : launchTarget.CameraName,
                    liveViewResult.ManifestUrl,
                    liveViewResult.CameraId).ConfigureAwait(false);
                return;
            }

            if (!string.IsNullOrWhiteSpace(launchTarget.CameraExternalUrl))
            {
                await FocusCameraInSmartHomeUiAsync(launchTarget, launchTarget.CameraExternalUrl, launchTarget.CameraExternalUrl, cancellationToken).ConfigureAwait(false);
                await OpenCameraWorkspaceAsync(
                    launchTarget.CameraDeviceId,
                    launchTarget.CameraExternalUrl,
                    string.IsNullOrWhiteSpace(launchTarget.CameraName) ? "Camera View" : launchTarget.CameraName,
                    launchTarget.CameraExternalUrl,
                    string.Empty).ConfigureAwait(false);
            }

            if (!launchTarget.UseManagedRingLiveView && string.IsNullOrWhiteSpace(launchTarget.CameraExternalUrl))
                throw new InvalidOperationException("Atlas found the camera request but no playable feed URL or camera id was available.");
        }

        private async Task FocusCameraInSmartHomeUiAsync(CameraLaunchTarget launchTarget, string? playerUrl, string? manifestUrl, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(launchTarget.CameraDeviceId))
            {
                return;
            }

            await Post("smart-home.focusCamera", new
            {
                cameraId = launchTarget.CameraDeviceId,
                cameraName = launchTarget.CameraName,
                useManagedRingLiveView = launchTarget.UseManagedRingLiveView,
                playerUrl,
                manifestUrl,
            }, cancellationToken).ConfigureAwait(false);
        }

        private async Task<CameraLaunchTarget> ResolveCameraLaunchTargetAsync(SmartHomeTextCommandResult result, CancellationToken cancellationToken)
        {
            var useManaged = result.OpenManagedRingLiveView;
            var providerId = (result.CameraProviderId ?? string.Empty).Trim();
            var deviceId = (result.CameraDeviceId ?? string.Empty).Trim();
            var cameraName = (result.CameraName ?? string.Empty).Trim();
            var externalUrl = (result.CameraExternalUrl ?? string.Empty).Trim();

            if ((useManaged && !string.IsNullOrWhiteSpace(deviceId)) || !string.IsNullOrWhiteSpace(externalUrl))
            {
                return new CameraLaunchTarget
                {
                    UseManagedRingLiveView = useManaged,
                    CameraDeviceId = deviceId,
                    CameraExternalUrl = externalUrl,
                    CameraName = cameraName,
                };
            }

            var snapshot = await _runtimeService.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
            var cameraCandidates = snapshot.Providers
                .SelectMany(provider => provider.Devices.Select(device => (provider, device)))
                .Where(entry => IsCameraLikeDevice(entry.device))
                .ToArray();

            var requestedName = NormalizeForMatch(cameraName);
            var match = cameraCandidates.FirstOrDefault(entry =>
                (string.IsNullOrWhiteSpace(providerId) || string.Equals(entry.provider.ProviderId, providerId, StringComparison.OrdinalIgnoreCase)) &&
                ((!string.IsNullOrWhiteSpace(deviceId) && string.Equals(entry.device.DeviceId, deviceId, StringComparison.OrdinalIgnoreCase)) ||
                 (string.IsNullOrWhiteSpace(deviceId) && !string.IsNullOrWhiteSpace(requestedName) && NormalizeForMatch(entry.device.Name).Contains(requestedName, StringComparison.Ordinal))));

            if (match.provider == null)
            {
                match = cameraCandidates
                    .Where(entry => string.IsNullOrWhiteSpace(providerId) || string.Equals(entry.provider.ProviderId, providerId, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(entry => entry.device.IsOnline == true)
                    .ThenByDescending(entry => !string.IsNullOrWhiteSpace(entry.device.PreviewVideoUrl))
                    .ThenByDescending(entry => !string.IsNullOrWhiteSpace(entry.device.ExternalUrl))
                    .FirstOrDefault();
            }

            if (match.provider != null)
            {
                if (string.IsNullOrWhiteSpace(cameraName))
                    cameraName = match.device.Name ?? string.Empty;

                if (string.IsNullOrWhiteSpace(deviceId))
                    deviceId = match.device.DeviceId ?? string.Empty;

                if (string.IsNullOrWhiteSpace(externalUrl))
                    externalUrl = FirstNonEmpty(match.device.PreviewVideoUrl, match.device.ExternalUrl, match.device.PreviewImageUrl);

                if (!useManaged && string.Equals(match.provider.ProviderId, "ring", StringComparison.OrdinalIgnoreCase))
                    useManaged = true;
            }

            return new CameraLaunchTarget
            {
                UseManagedRingLiveView = useManaged,
                CameraDeviceId = deviceId,
                CameraExternalUrl = externalUrl,
                CameraName = cameraName,
            };
        }

        private static bool IsCameraLikeDevice(SmartHomeDevice device)
        {
            if (LooksLikeNonCameraLightDevice(device))
                return false;

            var type = NormalizeForMatch(device.DeviceType);
            var name = NormalizeForMatch(device.Name);
            return type.Contains("camera", StringComparison.Ordinal) ||
                   type.Contains("doorbell", StringComparison.Ordinal) ||
                   name.Contains("camera", StringComparison.Ordinal) ||
                   name.Contains("doorbell", StringComparison.Ordinal) ||
                   !string.IsNullOrWhiteSpace(device.PreviewVideoUrl) ||
                   !string.IsNullOrWhiteSpace(device.PreviewImageUrl) ||
                   !string.IsNullOrWhiteSpace(device.ExternalUrl);
        }

        private static bool LooksLikeNonCameraLightDevice(SmartHomeDevice device)
        {
            var normalized = $"{device.DeviceType} {device.Sku}".ToLowerInvariant();
            var looksLikeLightingHardware = normalized.Contains("light", StringComparison.Ordinal) ||
                normalized.Contains("bulb", StringComparison.Ordinal) ||
                normalized.Contains("lamp", StringComparison.Ordinal) ||
                normalized.Contains("strip", StringComparison.Ordinal) ||
                normalized.Contains("panel", StringComparison.Ordinal) ||
                normalized.Contains("backlight", StringComparison.Ordinal);

            if (!looksLikeLightingHardware)
                return false;

            if (!string.IsNullOrWhiteSpace(device.PreviewVideoUrl) ||
                !string.IsNullOrWhiteSpace(device.PreviewImageUrl))
                return false;

            return !device.Capabilities.Any(IsCameraLikeCapability);
        }

        private static bool IsCameraLikeCapability(SmartHomeCapability capability)
        {
            var normalizedType = (capability.Type ?? string.Empty).ToLowerInvariant();
            var normalizedInstance = (capability.Instance ?? string.Empty).ToLowerInvariant();
            var isDynamicSceneSnapshot = normalizedInstance.Contains("snapshot", StringComparison.Ordinal) &&
                (normalizedType.Contains("dynamic_scene", StringComparison.Ordinal) ||
                 normalizedType.Contains("scene", StringComparison.Ordinal));

            if (isDynamicSceneSnapshot)
                return false;

            var token = $"{normalizedType} {normalizedInstance}";
            return token.Contains("camera", StringComparison.Ordinal) ||
                token.Contains("doorbell", StringComparison.Ordinal) ||
                token.Contains("snapshot", StringComparison.Ordinal) ||
                token.Contains("stream", StringComparison.Ordinal);
        }

        private static string NormalizeForMatch(string value)
        {
            return string.Join(" ", (value ?? string.Empty)
                .ToLowerInvariant()
                .Split(new[] { ' ', '-', '_', '.', ',', ':', ';', '/', '\\', '|', '(', ')', '[', ']', '{', '}' }, StringSplitOptions.RemoveEmptyEntries));
        }

        private static string FirstNonEmpty(params string[] values)
        {
            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                    return value;
            }

            return string.Empty;
        }

        private sealed class CameraLaunchTarget
        {
            public bool UseManagedRingLiveView { get; init; }
            public string CameraDeviceId { get; init; } = string.Empty;
            public string CameraExternalUrl { get; init; } = string.Empty;
            public string CameraName { get; init; } = string.Empty;
        }

        private async Task SaveCustomCommandAsync(JsonElement payload, CancellationToken cancellationToken)
        {
            var settings = SettingsStore.Current;
            var command = ParseCustomCommand(payload);
            if (string.IsNullOrWhiteSpace(command.Id))
                command.Id = Guid.NewGuid().ToString("N");

            var existingIndex = settings.SmartHome.CustomCommands.FindIndex(item => string.Equals(item.Id, command.Id, StringComparison.OrdinalIgnoreCase));
            if (existingIndex >= 0)
                settings.SmartHome.CustomCommands[existingIndex] = command;
            else
                settings.SmartHome.CustomCommands.Add(command);

            SettingsStore.Save(settings);
            await Post("smart-home.actionResult", new { ok = true, message = $"Saved voice command '{command.Phrase}'." }, cancellationToken).ConfigureAwait(false);
            await PostStateAsync(cancellationToken).ConfigureAwait(false);
        }

        private async Task DeleteCustomCommandAsync(JsonElement payload, CancellationToken cancellationToken)
        {
            var commandId = payload.TryGetProperty("id", out var idElement) ? idElement.GetString() ?? string.Empty : string.Empty;
            if (string.IsNullOrWhiteSpace(commandId))
                return;

            var settings = SettingsStore.Current;
            settings.SmartHome.CustomCommands.RemoveAll(item => string.Equals(item.Id, commandId, StringComparison.OrdinalIgnoreCase));
            SettingsStore.Save(settings);

            await Post("smart-home.actionResult", new { ok = true, message = "Deleted voice command." }, cancellationToken).ConfigureAwait(false);
            await PostStateAsync(cancellationToken).ConfigureAwait(false);
        }

        private async Task SaveSceneAsync(JsonElement payload, CancellationToken cancellationToken)
        {
            var settings = SettingsStore.Current;
            var scene = ParseScene(payload);
            if (string.IsNullOrWhiteSpace(scene.Id))
                scene.Id = Guid.NewGuid().ToString("N");

            var existingIndex = settings.SmartHome.CustomScenes.FindIndex(item => string.Equals(item.Id, scene.Id, StringComparison.OrdinalIgnoreCase));
            if (existingIndex >= 0)
                settings.SmartHome.CustomScenes[existingIndex] = scene;
            else
                settings.SmartHome.CustomScenes.Add(scene);

            SettingsStore.Save(settings);
            await Post("smart-home.actionResult", new { ok = true, message = $"Saved scene '{scene.Name}'." }, cancellationToken).ConfigureAwait(false);
            await PostStateAsync(cancellationToken).ConfigureAwait(false);
        }

        private async Task DeleteSceneAsync(JsonElement payload, CancellationToken cancellationToken)
        {
            var sceneId = payload.TryGetProperty("id", out var idElement) ? idElement.GetString() ?? string.Empty : string.Empty;
            if (string.IsNullOrWhiteSpace(sceneId))
                return;

            var settings = SettingsStore.Current;
            settings.SmartHome.CustomScenes.RemoveAll(item => string.Equals(item.Id, sceneId, StringComparison.OrdinalIgnoreCase));
            SettingsStore.Save(settings);

            await Post("smart-home.actionResult", new { ok = true, message = "Deleted scene." }, cancellationToken).ConfigureAwait(false);
            await PostStateAsync(cancellationToken).ConfigureAwait(false);
        }

        private async Task RunSceneAsync(JsonElement payload, CancellationToken cancellationToken)
        {
            var sceneId = payload.TryGetProperty("id", out var idElement) ? idElement.GetString() ?? string.Empty : string.Empty;
            var result = await _runtimeService.ExecuteSceneAsync(sceneId, cancellationToken).ConfigureAwait(false);
            if (!result.Ok)
            {
                await Post("smart-home.error", new { message = result.Message }, cancellationToken).ConfigureAwait(false);
                return;
            }

            await Post("smart-home.actionResult", new { ok = true, message = result.Message }, cancellationToken).ConfigureAwait(false);
            await PostStateAsync(cancellationToken).ConfigureAwait(false);
        }

        private async Task ExecuteScenePreviewAsync(JsonElement payload, CancellationToken cancellationToken)
        {
            var sceneName = payload.TryGetProperty("name", out var nameElement) ? nameElement.GetString() ?? string.Empty : string.Empty;
            var actions = ParseScenePreviewActions(payload);
            var result = await _runtimeService.ExecuteScenePreviewAsync(actions, sceneName, cancellationToken).ConfigureAwait(false);
            if (!result.Ok)
            {
                await Post("smart-home.error", new { message = result.Message }, cancellationToken).ConfigureAwait(false);
                return;
            }

            await Post("smart-home.actionResult", new { ok = true, message = result.Message }, cancellationToken).ConfigureAwait(false);
            await PostStateAsync(cancellationToken).ConfigureAwait(false);
        }

        private async Task SaveCustomGreetingAsync(JsonElement payload, CancellationToken cancellationToken)
        {
            var settings = SettingsStore.Current;
            var greeting = ParseCustomGreeting(payload);
            if (string.IsNullOrWhiteSpace(greeting.Id))
                greeting.Id = Guid.NewGuid().ToString("N");

            var existingIndex = settings.SmartHome.CustomGreetings.FindIndex(item => string.Equals(item.Id, greeting.Id, StringComparison.OrdinalIgnoreCase));
            if (existingIndex >= 0)
                settings.SmartHome.CustomGreetings[existingIndex] = greeting;
            else
                settings.SmartHome.CustomGreetings.Add(greeting);

            SettingsStore.Save(settings);
            await Post("smart-home.actionResult", new { ok = true, message = $"Saved greeting '{greeting.Phrase}'." }, cancellationToken).ConfigureAwait(false);
            await PostStateAsync(cancellationToken).ConfigureAwait(false);
        }

        private async Task DeleteCustomGreetingAsync(JsonElement payload, CancellationToken cancellationToken)
        {
            var greetingId = payload.TryGetProperty("id", out var idElement) ? idElement.GetString() ?? string.Empty : string.Empty;
            if (string.IsNullOrWhiteSpace(greetingId))
                return;

            var settings = SettingsStore.Current;
            settings.SmartHome.CustomGreetings.RemoveAll(item => string.Equals(item.Id, greetingId, StringComparison.OrdinalIgnoreCase));
            SettingsStore.Save(settings);

            await Post("smart-home.actionResult", new { ok = true, message = "Deleted greeting." }, cancellationToken).ConfigureAwait(false);
            await PostStateAsync(cancellationToken).ConfigureAwait(false);
        }

        private async Task GenerateGreetingPresetAsync(JsonElement payload, CancellationToken cancellationToken)
        {
            var preset = (payload.TryGetProperty("preset", out var presetElement) ? presetElement.GetString() ?? string.Empty : string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(preset))
                return;

            var settings = SettingsStore.Current;
            var pack = GetGreetingPresetPack(preset);
            var added = 0;

            foreach (var entry in pack)
            {
                var existing = settings.SmartHome.CustomGreetings.FindIndex(item =>
                    string.Equals(item.Phrase.Trim(), entry.Phrase, StringComparison.OrdinalIgnoreCase));

                if (existing >= 0)
                {
                    settings.SmartHome.CustomGreetings[existing].ResponseText = entry.ResponseText;
                    settings.SmartHome.CustomGreetings[existing].Enabled = true;
                }
                else
                {
                    settings.SmartHome.CustomGreetings.Add(new SmartHomeGreetingSetting
                    {
                        Phrase = entry.Phrase,
                        ResponseText = entry.ResponseText,
                        Enabled = true,
                    });
                    added++;
                }
            }

            SettingsStore.Save(settings);
            var label = preset.Equals("pro", StringComparison.OrdinalIgnoreCase) ? "Professional" : "Unfiltered";
            await Post("smart-home.actionResult", new { ok = true, message = $"Generated {label} greeting pack ({added} new, {pack.Length - added} updated)." }, cancellationToken).ConfigureAwait(false);
            await PostStateAsync(cancellationToken).ConfigureAwait(false);
        }

        private static (string Phrase, string ResponseText)[] GetGreetingPresetPack(string preset)
        {
            if (preset.Equals("pro", StringComparison.OrdinalIgnoreCase))
            {
                return new (string, string)[]
                {
                    ("good morning atlas", "Good morning. All systems are operational and ready for the day ahead."),
                    ("hello atlas", "Hello. Standing by and ready to assist. What can I help you with?"),
                    ("good afternoon atlas", "Good afternoon. Everything is running smoothly. How can I assist?"),
                    ("good evening atlas", "Good evening. I hope the day has treated you well. What do you need?"),
                    ("goodnight atlas", "Goodnight. I'll keep everything running while you rest."),
                    ("how are you atlas", "Functioning at full capacity. Thank you for asking. Ready when you are."),
                    ("thank you atlas", "You're welcome. I'm always here if you need anything else."),
                    ("what's the status", "All connected systems are reporting nominal. Would you like a detailed breakdown?"),
                    ("welcome home", "Welcome home. Everything has been running smoothly in your absence."),
                    ("i'm leaving", "Understood. I'll keep everything secure and monitored while you're away."),
                    ("atlas help", "Of course. Tell me what you need and I'll handle it promptly."),
                    ("who are you", "I'm Atlas, your personal AI assistant. Built to manage your home and keep things running."),
                };
            }

            return new (string, string)[]
            {
                ("good morning atlas", "Morning. Coffee first, questions after. What's the play?"),
                ("hello atlas", "Hey. What do you need? I'm not big on small talk."),
                ("good afternoon atlas", "Afternoon. Still here, still running. What's up?"),
                ("good evening atlas", "Evening. Place has been quiet. That's about to change, isn't it?"),
                ("goodnight atlas", "Night. I'll hold down the fort. Don't worry about it."),
                ("how are you atlas", "Still alive, still sharp. Better question - what do YOU need?"),
                ("thank you atlas", "No need for that. Just doing what I do. What's next?"),
                ("what's the status", "Everything's ticking over fine. Want the boring details or just the highlights?"),
                ("welcome home", "About time you showed up. Place ran fine without you, obviously."),
                ("i'm leaving", "Right, I'll keep an eye on things. Try not to be too long."),
                ("atlas help", "Alright, hit me. What's gone wrong this time?"),
                ("who are you", "I'm Atlas. Your house runs because I let it. What do you need?"),
            };
        }

        private async Task SaveAgentSettingsAsync(JsonElement payload, CancellationToken cancellationToken)
        {
            var settings = SettingsStore.Current;
            if (payload.TryGetProperty("voiceCommandsEnabled", out var voiceCommandsElement) &&
                (voiceCommandsElement.ValueKind == JsonValueKind.True || voiceCommandsElement.ValueKind == JsonValueKind.False))
            {
                settings.SmartHome.Agent.VoiceCommandsEnabled = voiceCommandsElement.GetBoolean();
            }

            if (payload.TryGetProperty("showDeviceShortcutsInSidebar", out var shortcutsElement) &&
                (shortcutsElement.ValueKind == JsonValueKind.True || shortcutsElement.ValueKind == JsonValueKind.False))
            {
                settings.SmartHome.Agent.ShowDeviceShortcutsInSidebar = shortcutsElement.GetBoolean();
            }

            if (payload.TryGetProperty("answerDoorEnabled", out var answerDoorElement) &&
                (answerDoorElement.ValueKind == JsonValueKind.True || answerDoorElement.ValueKind == JsonValueKind.False))
            {
                settings.SmartHome.Agent.AnswerDoorEnabled = answerDoorElement.GetBoolean();
                var answerDoorCommand = settings.SmartHome.CustomCommands
                    .FirstOrDefault(item => string.Equals(item.Id, AnswerDoorCommandId, StringComparison.OrdinalIgnoreCase));
                if (answerDoorCommand != null)
                    answerDoorCommand.Enabled = settings.SmartHome.Agent.AnswerDoorEnabled;
            }

            if (payload.TryGetProperty("defaultVolumeStep", out var volumeStepElement) && volumeStepElement.TryGetInt32(out var volumeStep))
            {
                settings.SmartHome.Agent.DefaultVolumeStep = Math.Clamp(volumeStep, 1, 25);
            }

            SettingsStore.Save(settings);
            await Post("smart-home.actionResult", new { ok = true, message = "Smart Home agent settings saved." }, cancellationToken).ConfigureAwait(false);
            await PostStateAsync(cancellationToken).ConfigureAwait(false);
        }

        private async Task CreateAutomationAsync(JsonElement payload, CancellationToken cancellationToken)
        {
            var trigger = payload.TryGetProperty("trigger", out var triggerElement) ? triggerElement.GetString() ?? string.Empty : string.Empty;
            var schedule = payload.TryGetProperty("schedule", out var scheduleElement) ? scheduleElement.GetString() ?? string.Empty : string.Empty;
            var actions = new List<string>();
            if (payload.TryGetProperty("actions", out var actionsElement) && actionsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var actionElement in actionsElement.EnumerateArray())
                {
                    if (actionElement.ValueKind == JsonValueKind.String)
                    {
                        var action = actionElement.GetString() ?? string.Empty;
                        if (!string.IsNullOrWhiteSpace(action))
                            actions.Add(action);
                    }
                }
            }

            var result = _runtimeService.CreateAutomation(trigger, actions, schedule);
            if (!result.Ok)
            {
                await Post("smart-home.error", new { message = result.Message }, cancellationToken).ConfigureAwait(false);
                return;
            }

            await Post("smart-home.actionResult", new { ok = true, message = result.Message }, cancellationToken).ConfigureAwait(false);
            await PostStateAsync(cancellationToken).ConfigureAwait(false);
        }

        private async Task ToggleAutomationAsync(JsonElement payload, CancellationToken cancellationToken)
        {
            var automationId = payload.TryGetProperty("id", out var idElement) ? idElement.GetString() ?? string.Empty : string.Empty;
            var result = _runtimeService.ToggleAutomation(automationId);
            if (!result.Ok)
            {
                await Post("smart-home.error", new { message = result.Message }, cancellationToken).ConfigureAwait(false);
                return;
            }

            await Post("smart-home.actionResult", new { ok = true, message = result.Message }, cancellationToken).ConfigureAwait(false);
            await PostStateAsync(cancellationToken).ConfigureAwait(false);
        }

        private async Task DeleteAutomationAsync(JsonElement payload, CancellationToken cancellationToken)
        {
            var automationId = payload.TryGetProperty("id", out var idElement) ? idElement.GetString() ?? string.Empty : string.Empty;
            var result = _runtimeService.DeleteAutomation(automationId);
            if (!result.Ok)
            {
                await Post("smart-home.error", new { message = result.Message }, cancellationToken).ConfigureAwait(false);
                return;
            }

            await Post("smart-home.actionResult", new { ok = true, message = result.Message }, cancellationToken).ConfigureAwait(false);
            await PostStateAsync(cancellationToken).ConfigureAwait(false);
        }

        private async Task RunAutomationAsync(JsonElement payload, CancellationToken cancellationToken)
        {
            var automationId = payload.TryGetProperty("id", out var idElement) ? idElement.GetString() ?? string.Empty : string.Empty;
            var result = await _runtimeService.ExecuteAutomationAsync(automationId, cancellationToken).ConfigureAwait(false);
            if (!result.Ok)
            {
                await Post("smart-home.error", new { message = result.Message }, cancellationToken).ConfigureAwait(false);
                return;
            }

            await Post("smart-home.actionResult", new { ok = true, message = result.Message }, cancellationToken).ConfigureAwait(false);
            await PostStateAsync(cancellationToken).ConfigureAwait(false);
        }

        private async Task LinkHueBridgeAsync(JsonElement payload, CancellationToken cancellationToken)
        {
            var settings = SettingsStore.Current;
            if (payload.TryGetProperty("bridgeIp", out var bridgeIpElement))
            {
                settings.SmartHome.PhilipsHue.BridgeIp = PhilipsHueProvider.NormalizeBridgeHost(bridgeIpElement.GetString() ?? string.Empty);
            }

            SettingsStore.Save(settings);

            var provider = new PhilipsHueProvider(settings.SmartHome.PhilipsHue);
            var result = await provider.LinkBridgeAsync(cancellationToken).ConfigureAwait(false);
            if (!result.Ok)
            {
                await Post("smart-home.error", new { message = result.Message }, cancellationToken).ConfigureAwait(false);
                return;
            }

            settings.SmartHome.PhilipsHue.ApplicationKey = result.ApplicationKey;
            settings.SmartHome.PhilipsHue.Enabled = true;
            SettingsStore.Save(settings);

            await Post("smart-home.actionResult", new { ok = true, message = result.Message }, cancellationToken).ConfigureAwait(false);
            await PostStateAsync(cancellationToken).ConfigureAwait(false);
        }

        private async Task LinkLgTvAsync(JsonElement payload, CancellationToken cancellationToken)
        {
            var settings = SettingsStore.Current;
            if (payload.TryGetProperty("host", out var hostElement))
            {
                settings.SmartHome.LgWebOs.Host = hostElement.GetString() ?? string.Empty;
            }

            SettingsStore.Save(settings);

            var provider = new LgWebOsProvider(settings.SmartHome.LgWebOs);
            var result = await provider.LinkAsync(cancellationToken).ConfigureAwait(false);
            if (!result.Ok)
            {
                await Post("smart-home.error", new { message = result.Message }, cancellationToken).ConfigureAwait(false);
                return;
            }

            settings.SmartHome.LgWebOs.ClientKey = result.ClientKey;
            if (!string.IsNullOrWhiteSpace(result.MacAddress))
                settings.SmartHome.LgWebOs.MacAddress = result.MacAddress;
            settings.SmartHome.LgWebOs.Enabled = true;
            SettingsStore.Save(settings);

            await Post("smart-home.actionResult", new { ok = true, message = result.Message }, cancellationToken).ConfigureAwait(false);
            await PostStateAsync(cancellationToken).ConfigureAwait(false);
        }

        private async Task DiscoverLgTvAsync(CancellationToken cancellationToken)
        {
            var hosts = await LgWebOsProvider.DiscoverHostsAsync(cancellationToken).ConfigureAwait(false);
            if (hosts.Count == 0)
            {
                await Post("smart-home.error", new { message = "No LG webOS TV was found on this network." }, cancellationToken).ConfigureAwait(false);
                return;
            }

            var settings = SettingsStore.Current;
            settings.SmartHome.LgWebOs.Host = hosts[0];
            SettingsStore.Save(settings);

            var message = hosts.Count == 1
                ? $"Found LG TV at {hosts[0]}."
                : $"Found {hosts.Count} LG TVs. Using {hosts[0]} as the default target.";

            await Post("smart-home.actionResult", new { ok = true, message }, cancellationToken).ConfigureAwait(false);
            await PostStateAsync(cancellationToken).ConfigureAwait(false);
        }

        private async Task DiscoverNetworkAsync(CancellationToken cancellationToken)
        {
            var result = await _runtimeService.DiscoverNetworkAsync(cancellationToken).ConfigureAwait(false);
            if (!result.Ok)
            {
                await Post("smart-home.error", new { message = result.Message }, cancellationToken).ConfigureAwait(false);
                await PostStateAsync(cancellationToken).ConfigureAwait(false);
                return;
            }

            await Post("smart-home.actionResult", new { ok = true, message = result.Message }, cancellationToken).ConfigureAwait(false);
            await PostStateAsync(cancellationToken).ConfigureAwait(false);
        }

        private async Task LoginRingAsync(JsonElement payload, CancellationToken cancellationToken)
        {
            var email = payload.TryGetProperty("email", out var emailElement) ? emailElement.GetString() ?? string.Empty : string.Empty;
            var password = payload.TryGetProperty("password", out var passwordElement) ? passwordElement.GetString() ?? string.Empty : string.Empty;
            var code = payload.TryGetProperty("code", out var codeElement) ? codeElement.GetString() ?? string.Empty : string.Empty;

            if (!string.IsNullOrWhiteSpace(code))
            {
                email = string.IsNullOrWhiteSpace(email) ? _pendingRingEmail : email;
                password = string.IsNullOrWhiteSpace(password) ? _pendingRingPassword : password;
            }

            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            {
                await Post("smart-home.error", new { message = "Enter your Ring email and password first." }, cancellationToken).ConfigureAwait(false);
                return;
            }

            var result = await RingProvider.AuthenticateAsync(email, password, code, _pendingRingHardwareId, cancellationToken).ConfigureAwait(false);
            if (result.Ok)
            {
                var settings = SettingsStore.Current;
                settings.SmartHome.Ring.RefreshToken = result.RefreshToken;
                settings.SmartHome.Ring.Enabled = true;
                SettingsStore.Save(settings);

                _pendingRingEmail = string.Empty;
                _pendingRingPassword = string.Empty;
                _pendingRingHardwareId = string.Empty;

                await Post("smart-home.actionResult", new { ok = true, message = result.Message }, cancellationToken).ConfigureAwait(false);
                await PostStateAsync(cancellationToken).ConfigureAwait(false);
                return;
            }

            if (result.RequiresTwoFactor)
            {
                _pendingRingEmail = email;
                _pendingRingPassword = password;
                _pendingRingHardwareId = result.PendingHardwareId;
                await Post("smart-home.actionResult", new { ok = true, message = result.Message }, cancellationToken).ConfigureAwait(false);
                return;
            }

            _pendingRingEmail = string.Empty;
            _pendingRingPassword = string.Empty;
            _pendingRingHardwareId = string.Empty;

            await Post("smart-home.error", new { message = result.Message }, cancellationToken).ConfigureAwait(false);
        }

        private Task Post(string type, object payload, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (Dispatcher.CheckAccess())
            {
                TryPostMessage(type, payload);
                return Task.CompletedTask;
            }

            return Dispatcher.InvokeAsync(() => TryPostMessage(type, payload)).Task;
        }

        private void TryPostMessage(string type, object payload)
        {
            try
            {
                if (SmartHomeWebView?.CoreWebView2 == null)
                    return;

                var message = JsonSerializer.Serialize(new { type, payload }, JsonOptions);
                SmartHomeWebView.CoreWebView2.PostWebMessageAsJson(message);
            }
            catch
            {
            }
        }

        private static void ApplySettingsPatch(JsonElement payload)
        {
            if (!payload.TryGetProperty("providerId", out var providerIdElement))
                return;

            var providerId = providerIdElement.GetString() ?? string.Empty;
            var settings = SettingsStore.Current;
            var values = payload.TryGetProperty("settings", out var settingsElement) ? settingsElement : default;

            switch (providerId)
            {
                case "govee":
                    ApplyCommonEnabled(values, out var goveeEnabled);
                    if (goveeEnabled.HasValue)
                        settings.SmartHome.Govee.Enabled = goveeEnabled.Value;
                    if (values.TryGetProperty("apiKey", out var apiKeyElement))
                        settings.SmartHome.Govee.ApiKey = apiKeyElement.GetString() ?? string.Empty;
                    break;
                case "philips_hue":
                    ApplyCommonEnabled(values, out var hueEnabled);
                    if (hueEnabled.HasValue)
                        settings.SmartHome.PhilipsHue.Enabled = hueEnabled.Value;
                    if (values.TryGetProperty("bridgeIp", out var bridgeIpElement))
                        settings.SmartHome.PhilipsHue.BridgeIp = bridgeIpElement.GetString() ?? string.Empty;
                    if (values.TryGetProperty("applicationKey", out var appKeyElement))
                        settings.SmartHome.PhilipsHue.ApplicationKey = appKeyElement.GetString() ?? string.Empty;
                    break;
                case "ring":
                    ApplyCommonEnabled(values, out var ringEnabled);
                    if (ringEnabled.HasValue)
                        settings.SmartHome.Ring.Enabled = ringEnabled.Value;
                    if (values.TryGetProperty("refreshToken", out var refreshTokenElement))
                        settings.SmartHome.Ring.RefreshToken = refreshTokenElement.GetString() ?? string.Empty;
                    break;
                case "lg_webos":
                    ApplyCommonEnabled(values, out var lgEnabled);
                    if (lgEnabled.HasValue)
                        settings.SmartHome.LgWebOs.Enabled = lgEnabled.Value;
                    if (values.TryGetProperty("host", out var hostElement))
                        settings.SmartHome.LgWebOs.Host = hostElement.GetString() ?? string.Empty;
                    if (values.TryGetProperty("clientKey", out var clientKeyElement))
                        settings.SmartHome.LgWebOs.ClientKey = clientKeyElement.GetString() ?? string.Empty;
                    break;
                case "smartthings":
                    ApplyCommonEnabled(values, out var smartThingsEnabled);
                    if (smartThingsEnabled.HasValue)
                        settings.SmartHome.SmartThings.Enabled = smartThingsEnabled.Value;
                    if (values.TryGetProperty("accessToken", out var smartThingsAccessTokenElement))
                        settings.SmartHome.SmartThings.AccessToken = smartThingsAccessTokenElement.GetString() ?? string.Empty;
                    if (values.TryGetProperty("locationId", out var smartThingsLocationIdElement))
                        settings.SmartHome.SmartThings.LocationId = smartThingsLocationIdElement.GetString() ?? string.Empty;
                    break;
                case "home_assistant":
                    ApplyCommonEnabled(values, out var homeAssistantEnabled);
                    if (homeAssistantEnabled.HasValue)
                        settings.SmartHome.HomeAssistant.Enabled = homeAssistantEnabled.Value;
                    if (values.TryGetProperty("baseUrl", out var homeAssistantBaseUrlElement))
                        settings.SmartHome.HomeAssistant.BaseUrl = homeAssistantBaseUrlElement.GetString() ?? string.Empty;
                    if (values.TryGetProperty("accessToken", out var homeAssistantAccessTokenElement))
                        settings.SmartHome.HomeAssistant.AccessToken = homeAssistantAccessTokenElement.GetString() ?? string.Empty;
                    break;
                case "tapo_kasa":
                    ApplyCommonEnabled(values, out var tapoKasaEnabled);
                    if (tapoKasaEnabled.HasValue)
                        settings.SmartHome.TapoKasa.Enabled = tapoKasaEnabled.Value;
                    if (values.TryGetProperty("host", out var tapoKasaHostElement))
                        settings.SmartHome.TapoKasa.Host = tapoKasaHostElement.GetString() ?? string.Empty;
                    if (values.TryGetProperty("username", out var tapoKasaUsernameElement))
                        settings.SmartHome.TapoKasa.Username = tapoKasaUsernameElement.GetString() ?? string.Empty;
                    if (values.TryGetProperty("password", out var tapoKasaPasswordElement))
                        settings.SmartHome.TapoKasa.Password = tapoKasaPasswordElement.GetString() ?? string.Empty;
                    break;
                case "onvif_rtsp":
                    ApplyCommonEnabled(values, out var onvifEnabled);
                    if (onvifEnabled.HasValue)
                        settings.SmartHome.OnvifRtsp.Enabled = onvifEnabled.Value;
                    if (values.TryGetProperty("host", out var onvifHostElement))
                        settings.SmartHome.OnvifRtsp.Host = onvifHostElement.GetString() ?? string.Empty;
                    if (values.TryGetProperty("username", out var onvifUsernameElement))
                        settings.SmartHome.OnvifRtsp.Username = onvifUsernameElement.GetString() ?? string.Empty;
                    if (values.TryGetProperty("password", out var onvifPasswordElement))
                        settings.SmartHome.OnvifRtsp.Password = onvifPasswordElement.GetString() ?? string.Empty;
                    if (values.TryGetProperty("rtspUrl", out var onvifRtspUrlElement))
                        settings.SmartHome.OnvifRtsp.RtspUrl = onvifRtspUrlElement.GetString() ?? string.Empty;
                    break;
                default:
                    return;
            }

            SettingsStore.Save(settings);
        }

        private static void ApplyCommonEnabled(JsonElement values, out bool? enabled)
        {
            enabled = null;
            if (values.ValueKind != JsonValueKind.Object)
                return;

            if (values.TryGetProperty("enabled", out var enabledElement) &&
                (enabledElement.ValueKind == JsonValueKind.True || enabledElement.ValueKind == JsonValueKind.False))
            {
                enabled = enabledElement.GetBoolean();
            }
        }

        private static SmartHomeActionRequest ParseActionRequest(JsonElement payload)
        {
            return new SmartHomeActionRequest
            {
                ProviderId = payload.TryGetProperty("providerId", out var providerIdElement) ? providerIdElement.GetString() ?? string.Empty : string.Empty,
                DeviceId = payload.TryGetProperty("deviceId", out var deviceIdElement) ? deviceIdElement.GetString() ?? string.Empty : string.Empty,
                Sku = payload.TryGetProperty("sku", out var skuElement) ? skuElement.GetString() ?? string.Empty : string.Empty,
                CapabilityType = payload.TryGetProperty("capabilityType", out var capabilityTypeElement) ? capabilityTypeElement.GetString() ?? string.Empty : string.Empty,
                CapabilityInstance = payload.TryGetProperty("capabilityInstance", out var capabilityInstanceElement) ? capabilityInstanceElement.GetString() ?? string.Empty : string.Empty,
                Value = payload.TryGetProperty("value", out var valueElement) ? valueElement.Clone() : default,
            };
        }

        private static SmartHomeCustomCommandSetting ParseCustomCommand(JsonElement payload)
        {
            return new SmartHomeCustomCommandSetting
            {
                Id = payload.TryGetProperty("id", out var idElement) ? idElement.GetString() ?? string.Empty : string.Empty,
                Enabled = !payload.TryGetProperty("enabled", out var enabledElement) || enabledElement.ValueKind != JsonValueKind.False,
                Phrase = payload.TryGetProperty("phrase", out var phraseElement) ? phraseElement.GetString() ?? string.Empty : string.Empty,
                TargetKind = payload.TryGetProperty("targetKind", out var targetKindElement) ? targetKindElement.GetString() ?? "device" : "device",
                TargetScope = payload.TryGetProperty("targetScope", out var targetScopeElement) ? targetScopeElement.GetString() ?? string.Empty : string.Empty,
                TargetLabel = payload.TryGetProperty("targetLabel", out var targetLabelElement) ? targetLabelElement.GetString() ?? string.Empty : string.Empty,
                ProviderId = payload.TryGetProperty("providerId", out var providerIdElement) ? providerIdElement.GetString() ?? string.Empty : string.Empty,
                DeviceId = payload.TryGetProperty("deviceId", out var deviceIdElement) ? deviceIdElement.GetString() ?? string.Empty : string.Empty,
                Sku = payload.TryGetProperty("sku", out var skuElement) ? skuElement.GetString() ?? string.Empty : string.Empty,
                CapabilityType = payload.TryGetProperty("capabilityType", out var capabilityTypeElement) ? capabilityTypeElement.GetString() ?? string.Empty : string.Empty,
                CapabilityInstance = payload.TryGetProperty("capabilityInstance", out var capabilityInstanceElement) ? capabilityInstanceElement.GetString() ?? string.Empty : string.Empty,
                ValueJson = payload.TryGetProperty("value", out var valueElement) ? valueElement.GetRawText() : "null",
                ResponseText = payload.TryGetProperty("responseText", out var responseElement) ? responseElement.GetString() ?? string.Empty : string.Empty,
                DoorbellResponseText = payload.TryGetProperty("doorbellResponseText", out var doorbellResponseElement) ? doorbellResponseElement.GetString() ?? string.Empty : string.Empty,
            };
        }

        private static SmartHomeGreetingSetting ParseCustomGreeting(JsonElement payload)
        {
            return new SmartHomeGreetingSetting
            {
                Id = payload.TryGetProperty("id", out var idElement) ? idElement.GetString() ?? string.Empty : string.Empty,
                Enabled = !payload.TryGetProperty("enabled", out var enabledElement) || enabledElement.ValueKind != JsonValueKind.False,
                Phrase = payload.TryGetProperty("phrase", out var phraseElement) ? phraseElement.GetString() ?? string.Empty : string.Empty,
                ResponseText = payload.TryGetProperty("responseText", out var responseElement) ? responseElement.GetString() ?? string.Empty : string.Empty,
            };
        }

        private static SmartHomeSceneSetting ParseScene(JsonElement payload)
        {
            var previewColors = new List<string>();
            if (payload.TryGetProperty("previewColors", out var previewColorsElement) && previewColorsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var colorElement in previewColorsElement.EnumerateArray())
                {
                    if (colorElement.ValueKind == JsonValueKind.String)
                    {
                        var color = colorElement.GetString() ?? string.Empty;
                        if (!string.IsNullOrWhiteSpace(color))
                            previewColors.Add(color);
                    }
                }
            }

            var actions = new List<SmartHomeSceneActionSetting>();
            if (payload.TryGetProperty("actions", out var actionsElement) && actionsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var actionElement in actionsElement.EnumerateArray())
                {
                    actions.Add(new SmartHomeSceneActionSetting
                    {
                        ProviderId = actionElement.TryGetProperty("providerId", out var providerIdElement) ? providerIdElement.GetString() ?? string.Empty : string.Empty,
                        DeviceId = actionElement.TryGetProperty("deviceId", out var deviceIdElement) ? deviceIdElement.GetString() ?? string.Empty : string.Empty,
                        DeviceName = actionElement.TryGetProperty("deviceName", out var deviceNameElement) ? deviceNameElement.GetString() ?? string.Empty : string.Empty,
                        Sku = actionElement.TryGetProperty("sku", out var skuElement) ? skuElement.GetString() ?? string.Empty : string.Empty,
                        CapabilityType = actionElement.TryGetProperty("capabilityType", out var capabilityTypeElement) ? capabilityTypeElement.GetString() ?? string.Empty : string.Empty,
                        CapabilityInstance = actionElement.TryGetProperty("capabilityInstance", out var capabilityInstanceElement) ? capabilityInstanceElement.GetString() ?? string.Empty : string.Empty,
                        ValueJson = actionElement.TryGetProperty("value", out var valueElement) ? valueElement.GetRawText() : "null",
                        HexColor = actionElement.TryGetProperty("hexColor", out var hexColorElement) ? hexColorElement.GetString() ?? string.Empty : string.Empty,
                    });
                }
            }

            return new SmartHomeSceneSetting
            {
                Id = payload.TryGetProperty("id", out var idElement) ? idElement.GetString() ?? string.Empty : string.Empty,
                Enabled = !payload.TryGetProperty("enabled", out var enabledElement) || enabledElement.ValueKind != JsonValueKind.False,
                Name = payload.TryGetProperty("name", out var nameElement) ? nameElement.GetString() ?? string.Empty : string.Empty,
                Phrase = payload.TryGetProperty("phrase", out var phraseElement) ? phraseElement.GetString() ?? string.Empty : string.Empty,
                PreviewColors = previewColors,
                Actions = actions,
            };
        }

        private static List<SmartHomeSceneActionState> ParseScenePreviewActions(JsonElement payload)
        {
            var actions = new List<SmartHomeSceneActionState>();
            if (!payload.TryGetProperty("actions", out var actionsElement) || actionsElement.ValueKind != JsonValueKind.Array)
                return actions;

            foreach (var actionElement in actionsElement.EnumerateArray())
            {
                actions.Add(new SmartHomeSceneActionState
                {
                    ProviderId = actionElement.TryGetProperty("providerId", out var providerIdElement) ? providerIdElement.GetString() ?? string.Empty : string.Empty,
                    DeviceId = actionElement.TryGetProperty("deviceId", out var deviceIdElement) ? deviceIdElement.GetString() ?? string.Empty : string.Empty,
                    DeviceName = actionElement.TryGetProperty("deviceName", out var deviceNameElement) ? deviceNameElement.GetString() ?? string.Empty : string.Empty,
                    Sku = actionElement.TryGetProperty("sku", out var skuElement) ? skuElement.GetString() ?? string.Empty : string.Empty,
                    CapabilityType = actionElement.TryGetProperty("capabilityType", out var capabilityTypeElement) ? capabilityTypeElement.GetString() ?? string.Empty : string.Empty,
                    CapabilityInstance = actionElement.TryGetProperty("capabilityInstance", out var capabilityInstanceElement) ? capabilityInstanceElement.GetString() ?? string.Empty : string.Empty,
                    Value = actionElement.TryGetProperty("value", out var valueElement) ? valueElement.Clone() : default,
                    HexColor = actionElement.TryGetProperty("hexColor", out var hexColorElement) ? hexColorElement.GetString() ?? string.Empty : string.Empty,
                });
            }

            return actions;
        }

        private async Task OpenExternalUrlAsync(JsonElement payload)
        {
            var url = payload.TryGetProperty("url", out var urlElement) ? urlElement.GetString() ?? string.Empty : string.Empty;
            var title = payload.TryGetProperty("title", out var titleElement) ? titleElement.GetString() ?? string.Empty : string.Empty;
            var recordingUrl = payload.TryGetProperty("recordingUrl", out var recordingUrlElement) ? recordingUrlElement.GetString() ?? string.Empty : string.Empty;
            var sessionId = payload.TryGetProperty("sessionId", out var sessionIdElement) ? sessionIdElement.GetString() ?? string.Empty : string.Empty;
            if (string.IsNullOrWhiteSpace(url))
                return;

            url = NormalizeNavigationUrl(url);

            // Launch ms-settings: URIs directly via the OS shell (Bluetooth, Wi-Fi, etc.)
            if (url.StartsWith("ms-settings:", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                    await Post("smart-home.actionResult", new
                    {
                        ok = true,
                        message = $"Opened {(string.IsNullOrWhiteSpace(title) ? "Windows Settings" : title)}."
                    }, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    await Post("smart-home.error", new { message = $"Atlas could not open {(string.IsNullOrWhiteSpace(title) ? "Windows Settings" : title)}: {ex.Message}" }, CancellationToken.None).ConfigureAwait(false);
                }
                return;
            }

            if (Uri.TryCreate(url, UriKind.Absolute, out var initialUri) &&
                !string.Equals(initialUri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(initialUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                    await Post("smart-home.actionResult", new
                    {
                        ok = true,
                        message = $"Opened {(string.IsNullOrWhiteSpace(title) ? initialUri.Scheme.ToUpperInvariant() : title)} using the system handler."
                    }, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    await Post("smart-home.error", new { message = $"Atlas could not open the external target: {ex.Message}" }, CancellationToken.None).ConfigureAwait(false);
                }
                return;
            }

            try
            {
                if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
                {
                    var host = (uri.Host ?? string.Empty).Trim();
                    var path = (uri.AbsolutePath ?? string.Empty).Trim();
                    if ((string.Equals(host, "ring.com", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(host, "www.ring.com", StringComparison.OrdinalIgnoreCase)) &&
                        string.Equals(path, "/app", StringComparison.OrdinalIgnoreCase))
                    {
                        url = "https://account.ring.com/account/dashboard";
                    }
                }
            }
            catch
            {
            }

            var canRecordInWorkspace = SmartHomeCameraRecordingService.IsSupportedSourceUrl(recordingUrl);

            if (!canRecordInWorkspace)
            {
                try
                {
                    Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                    await Post("smart-home.actionResult", new
                    {
                        ok = true,
                        message = $"Opened {(string.IsNullOrWhiteSpace(title) ? "external page" : title)} in your browser."
                    }, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    await Post("smart-home.error", new { message = $"Atlas could not open the external page: {ex.Message}" }, CancellationToken.None).ConfigureAwait(false);
                }
                return;
            }

            try
            {
                await Post("smart-home.actionResult", new
                {
                    ok = true,
                    message = $"Opening {(string.IsNullOrWhiteSpace(title) ? "camera workspace" : title)} inside Atlas."
                }, CancellationToken.None).ConfigureAwait(false);

                await OpenCameraWorkspaceAsync(
                    sessionId,
                    url,
                    string.IsNullOrWhiteSpace(title) ? (IsRingUrl(url) ? "Ring Live View" : "Smart Home Provider") : title,
                    string.IsNullOrWhiteSpace(recordingUrl) ? url : recordingUrl,
                    string.Empty);
            }
            catch (Exception ex)
            {
                await Post("smart-home.error", new { message = $"Atlas could not open the camera workspace: {ex.Message}" }, CancellationToken.None).ConfigureAwait(false);
            }
        }

        private static string NormalizeNavigationUrl(string url)
        {
            var trimmed = (url ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
                return string.Empty;

            if (trimmed.Contains("://", StringComparison.OrdinalIgnoreCase) || trimmed.StartsWith("ms-settings:", StringComparison.OrdinalIgnoreCase))
                return trimmed;

            if (Uri.TryCreate(trimmed, UriKind.Absolute, out _))
                return trimmed;

            return $"http://{trimmed}";
        }

        private async Task StartRingManagedLiveViewAsync(JsonElement payload, CancellationToken cancellationToken)
        {
            var requestId = payload.TryGetProperty("requestId", out var requestIdElement) ? requestIdElement.GetString() ?? string.Empty : string.Empty;
            var deviceId = payload.TryGetProperty("deviceId", out var deviceIdElement) ? deviceIdElement.GetString() ?? string.Empty : string.Empty;

            AppLogger.LogInfo($"[SmartHome][RingManaged] Start bridge request received for '{deviceId}' (request '{requestId}').");

            if (string.IsNullOrWhiteSpace(requestId) || string.IsNullOrWhiteSpace(deviceId))
            {
                await Post("smart-home.ringManagedLiveViewFailed", new
                {
                    requestId,
                    cameraId = deviceId,
                    message = "Ring camera id is missing.",
                }, cancellationToken).ConfigureAwait(false);
                return;
            }

            var result = await _runtimeService.StartRingManagedLiveViewAsync(deviceId, cancellationToken).ConfigureAwait(false);
            if (!result.Ok || string.IsNullOrWhiteSpace(result.PlayerUrl))
            {
                AppLogger.LogWarning($"[SmartHome][RingManaged] Start bridge request failed for '{deviceId}' (request '{requestId}'). {result.Message}");
                await Post("smart-home.ringManagedLiveViewFailed", new
                {
                    requestId,
                    cameraId = deviceId,
                    message = result.Message,
                }, cancellationToken).ConfigureAwait(false);
                return;
            }

            _activeManagedRingCameraIds.Add(result.CameraId);
            AppLogger.LogInfo($"[SmartHome][RingManaged] Start bridge request accepted for '{result.CameraId}' (request '{requestId}'). Tracking readiness.");

            try
            {
                await Post("smart-home.ringManagedLiveViewStarted", new
                {
                    requestId,
                    cameraId = result.CameraId,
                    playerUrl = result.PlayerUrl,
                    manifestUrl = result.ManifestUrl,
                }, cancellationToken).ConfigureAwait(false);
                await Post("smart-home.actionResult", new
                {
                    ok = true,
                    message = "Ring live view is ready in Atlas."
                }, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                await Post("smart-home.ringManagedLiveViewFailed", new { requestId, cameraId = deviceId, message = $"Atlas could not open the Ring player: {ex.Message}" }, cancellationToken).ConfigureAwait(false);
            }
        }

        private async Task TrackRingManagedLiveViewStartupAsync(string requestId, string cameraId, string playerUrl, string manifestUrl)
        {
            try
            {
                var deadlineUtc = DateTime.UtcNow.AddSeconds(95);
                while (DateTime.UtcNow < deadlineUtc)
                {
                    if (!_activeManagedRingCameraIds.Contains(cameraId))
                        return;

                    var status = await _runtimeService.GetRingManagedLiveViewStatusAsync(cameraId, CancellationToken.None).ConfigureAwait(false);
                    if (status.Ok && string.Equals(status.State, "ready", StringComparison.OrdinalIgnoreCase))
                    {
                        await Post("smart-home.ringManagedLiveViewStarted", new
                        {
                            requestId,
                            cameraId,
                            playerUrl = string.IsNullOrWhiteSpace(status.PlayerUrl) ? playerUrl : status.PlayerUrl,
                            manifestUrl = string.IsNullOrWhiteSpace(status.ManifestUrl) ? manifestUrl : status.ManifestUrl,
                        }, CancellationToken.None).ConfigureAwait(false);
                        await Post("smart-home.actionResult", new
                        {
                            ok = true,
                            message = "Ring live view is ready in Atlas."
                        }, CancellationToken.None).ConfigureAwait(false);
                        return;
                    }

                    if (!status.Ok || string.Equals(status.State, "error", StringComparison.OrdinalIgnoreCase))
                    {
                        var message = string.IsNullOrWhiteSpace(status.Message)
                            ? "Atlas could not start the Ring live stream."
                            : status.Message;
                        await Post("smart-home.ringManagedLiveViewFailed", new
                        {
                            requestId,
                            cameraId,
                            message,
                        }, CancellationToken.None).ConfigureAwait(false);
                        return;
                    }

                    await Task.Delay(1000).ConfigureAwait(false);
                }

                if (!_activeManagedRingCameraIds.Contains(cameraId))
                    return;

                const string timeoutMessage = "Ring live view is still not ready. Atlas kept the player open, but the stream did not become playable in time.";
                NotifyCameraWorkspaceError(cameraId, timeoutMessage);
                await Post("smart-home.ringManagedLiveViewFailed", new
                {
                    requestId,
                    cameraId,
                    message = timeoutMessage,
                }, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                if (!_activeManagedRingCameraIds.Contains(cameraId))
                    return;

                NotifyCameraWorkspaceError(cameraId, ex.Message);
                await Post("smart-home.ringManagedLiveViewFailed", new
                {
                    requestId,
                    cameraId,
                    message = ex.Message,
                }, CancellationToken.None).ConfigureAwait(false);
            }
        }

        private async Task StopRingManagedLiveViewAsync(JsonElement payload, CancellationToken cancellationToken)
        {
            var requestId = payload.TryGetProperty("requestId", out var requestIdElement) ? requestIdElement.GetString() ?? string.Empty : string.Empty;
            var deviceId = payload.TryGetProperty("deviceId", out var deviceIdElement) ? deviceIdElement.GetString() ?? string.Empty : _activeManagedRingCameraIds.FirstOrDefault() ?? string.Empty;

            AppLogger.LogInfo($"[SmartHome][RingManaged] Stop bridge request received for '{deviceId}' (request '{requestId}').");

            var result = await _runtimeService.StopRingManagedLiveViewAsync(deviceId, cancellationToken).ConfigureAwait(false);
            if (result.Ok)
            {
                _activeManagedRingCameraIds.Remove(deviceId);
                NotifyCameraWorkspaceStopped(string.IsNullOrWhiteSpace(result.CameraId) ? deviceId : result.CameraId, result.Message);
                AppLogger.LogInfo($"[SmartHome][RingManaged] Stop bridge request completed for '{(string.IsNullOrWhiteSpace(result.CameraId) ? deviceId : result.CameraId)}' (request '{requestId}'). {result.Message}");
            }

            if (!result.Ok)
            {
                AppLogger.LogWarning($"[SmartHome][RingManaged] Stop bridge request failed for '{deviceId}' (request '{requestId}'). {result.Message}");
                await Post("smart-home.ringManagedLiveViewFailed", new
                {
                    requestId,
                    cameraId = deviceId,
                    message = result.Message,
                }, cancellationToken).ConfigureAwait(false);
                return;
            }

            await Post("smart-home.ringManagedLiveViewStopped", new
            {
                requestId,
                cameraId = string.IsNullOrWhiteSpace(result.CameraId) ? deviceId : result.CameraId,
                message = result.Message,
            }, cancellationToken).ConfigureAwait(false);
        }

        private async Task StartCameraRecordingAsync(JsonElement payload, CancellationToken cancellationToken)
        {
            var requestId = payload.TryGetProperty("requestId", out var requestIdElement) ? requestIdElement.GetString() ?? string.Empty : string.Empty;
            var cameraId = payload.TryGetProperty("cameraId", out var cameraIdElement) ? cameraIdElement.GetString() ?? string.Empty : string.Empty;
            var cameraName = payload.TryGetProperty("cameraName", out var cameraNameElement) ? cameraNameElement.GetString() ?? string.Empty : string.Empty;
            var recordingUrl = payload.TryGetProperty("recordingUrl", out var recordingUrlElement) ? recordingUrlElement.GetString() ?? string.Empty : string.Empty;

            if (string.IsNullOrWhiteSpace(requestId) || string.IsNullOrWhiteSpace(cameraId) || !SmartHomeCameraRecordingService.IsSupportedSourceUrl(recordingUrl))
            {
                await Post("smart-home.cameraRecordingFailed", new
                {
                    requestId,
                    cameraId,
                    message = "Atlas could not start recording because the camera stream URL is not supported."
                }, cancellationToken).ConfigureAwait(false);
                return;
            }

            var result = await _cameraRecordingService.StartAsync(recordingUrl, string.IsNullOrWhiteSpace(cameraName) ? cameraId : cameraName, cameraId, cancellationToken).ConfigureAwait(false);
            if (!result.Ok)
            {
                await Post("smart-home.cameraRecordingFailed", new
                {
                    requestId,
                    cameraId,
                    message = result.Message,
                }, cancellationToken).ConfigureAwait(false);
                return;
            }

            await Post("smart-home.cameraRecordingStarted", new
            {
                requestId,
                cameraId,
                message = result.Message,
                recordingPath = result.OutputPath,
            }, cancellationToken).ConfigureAwait(false);
        }

        private async Task StopCameraRecordingAsync(JsonElement payload, CancellationToken cancellationToken)
        {
            var requestId = payload.TryGetProperty("requestId", out var requestIdElement) ? requestIdElement.GetString() ?? string.Empty : string.Empty;
            var cameraId = payload.TryGetProperty("cameraId", out var cameraIdElement) ? cameraIdElement.GetString() ?? string.Empty : string.Empty;

            if (string.IsNullOrWhiteSpace(requestId) || string.IsNullOrWhiteSpace(cameraId))
            {
                await Post("smart-home.cameraRecordingFailed", new
                {
                    requestId,
                    cameraId,
                    message = "Atlas could not stop recording because the camera id is missing."
                }, cancellationToken).ConfigureAwait(false);
                return;
            }

            var result = await _cameraRecordingService.StopAsync(cameraId, cancellationToken).ConfigureAwait(false);
            if (!result.Ok)
            {
                await Post("smart-home.cameraRecordingFailed", new
                {
                    requestId,
                    cameraId,
                    message = result.Message,
                }, cancellationToken).ConfigureAwait(false);
                return;
            }

            await Post("smart-home.cameraRecordingStopped", new
            {
                requestId,
                cameraId,
                message = result.Message,
                recordingPath = result.OutputPath,
            }, cancellationToken).ConfigureAwait(false);
        }

        private async Task OpenCameraWorkspaceAsync(string sessionId, string url, string title, string? recordingUrl, string? managedCameraId)
        {
            if (!Dispatcher.CheckAccess())
            {
                await Dispatcher.InvokeAsync(() => OpenCameraWorkspaceAsync(sessionId, url, title, recordingUrl, managedCameraId)).Task.Unwrap();
                return;
            }

            await EnsureEmbeddedBrowserInitializedAsync().ConfigureAwait(true);

            var normalizedSessionId = NormalizeCameraWorkspaceSessionId(sessionId, title, url);
            var effectiveRecordingUrl = SmartHomeCameraRecordingService.IsSupportedSourceUrl(recordingUrl ?? string.Empty)
                ? (recordingUrl ?? string.Empty).Trim()
                : string.Empty;

            if (_cameraWorkspaceSessions.TryGetValue(normalizedSessionId, out var existingSession))
            {
                existingSession.Title = string.IsNullOrWhiteSpace(title) ? existingSession.Title : title.Trim();
                existingSession.NavigationUrl = url.Trim();
                existingSession.RecordingUrl = effectiveRecordingUrl;
                existingSession.RecordingId = normalizedSessionId;
                existingSession.ManagedCameraId = (managedCameraId ?? string.Empty).Trim();
                existingSession.StatusMessage = "Camera view refreshed.";
            }
            else
            {
                _cameraWorkspaceSessions[normalizedSessionId] = new CameraWorkspaceSession
                {
                    SessionId = normalizedSessionId,
                    Title = string.IsNullOrWhiteSpace(title) ? "Camera View" : title.Trim(),
                    NavigationUrl = url.Trim(),
                    RecordingUrl = effectiveRecordingUrl,
                    RecordingId = normalizedSessionId,
                    ManagedCameraId = (managedCameraId ?? string.Empty).Trim(),
                    StatusMessage = string.IsNullOrWhiteSpace(effectiveRecordingUrl)
                        ? "Live view opened. Recording will enable when Atlas has a direct stream URL."
                        : "Live view opened. Recording is ready for this camera.",
                };
            }

            CameraWorkspaceContentHost.Content = null;
            CameraWorkspaceContentHost.Visibility = Visibility.Collapsed;
            EmbeddedBrowserTitle.Text = "Camera Workspace";
            EmbeddedBrowserOverlay.Visibility = Visibility.Visible;
            EmbeddedBrowserLoadingOverlay.Visibility = Visibility.Collapsed;
            SmartHomeWebView.Visibility = Visibility.Collapsed;
            EmbeddedBrowserWebView.Visibility = Visibility.Visible;
            EmbeddedBrowserOverlay.UpdateLayout();

            await RenderCameraWorkspaceAsync().ConfigureAwait(true);
            UpdateWorkspaceHeader();
        }

        private void NotifyCameraWorkspaceStarted(string cameraId)
        {
            if (Dispatcher.CheckAccess())
            {
                UpdateCameraWorkspaceStatus(cameraId, "Ring live view is ready in Atlas.");
                UpdateWorkspaceHeader();
                return;
            }

            Dispatcher.BeginInvoke(new Action(() =>
            {
                UpdateCameraWorkspaceStatus(cameraId, "Ring live view is ready in Atlas.");
                UpdateWorkspaceHeader();
            }));
        }

        private void NotifyCameraWorkspaceStopped(string cameraId, string message)
        {
            if (Dispatcher.CheckAccess())
            {
                UpdateCameraWorkspaceStatus(cameraId, message);
                UpdateWorkspaceHeader();
                return;
            }

            Dispatcher.BeginInvoke(new Action(() =>
            {
                UpdateCameraWorkspaceStatus(cameraId, message);
                UpdateWorkspaceHeader();
            }));
        }

        private void NotifyCameraWorkspaceError(string cameraId, string message)
        {
            if (Dispatcher.CheckAccess())
            {
                UpdateCameraWorkspaceStatus(cameraId, message);
                UpdateWorkspaceHeader();
                return;
            }

            Dispatcher.BeginInvoke(new Action(() =>
            {
                UpdateCameraWorkspaceStatus(cameraId, message);
                UpdateWorkspaceHeader();
            }));
        }

        private async Task StartRingLiveSessionAsync(JsonElement payload, CancellationToken cancellationToken)
        {
            var requestId = payload.TryGetProperty("requestId", out var requestIdElement) ? requestIdElement.GetString() ?? string.Empty : string.Empty;
            var deviceId = payload.TryGetProperty("deviceId", out var deviceIdElement) ? deviceIdElement.GetString() ?? string.Empty : string.Empty;
            var offerSdp = payload.TryGetProperty("offerSdp", out var offerSdpElement) ? offerSdpElement.GetString() ?? string.Empty : string.Empty;

            if (string.IsNullOrWhiteSpace(requestId) || string.IsNullOrWhiteSpace(deviceId) || string.IsNullOrWhiteSpace(offerSdp))
            {
                await Post("smart-home.ringLiveSessionFailed", new
                {
                    requestId,
                    cameraId = deviceId,
                    message = "Ring live session request is incomplete.",
                }, cancellationToken).ConfigureAwait(false);
                return;
            }

            var result = await _runtimeService.StartRingLiveSessionAsync(new RingLiveSessionStartRequest
            {
                DeviceId = deviceId,
                OfferSdp = offerSdp,
            }, cancellationToken).ConfigureAwait(false);

            if (!result.Ok)
            {
                await Post("smart-home.ringLiveSessionFailed", new
                {
                    requestId,
                    cameraId = deviceId,
                    message = result.Message,
                }, cancellationToken).ConfigureAwait(false);
                return;
            }

            await Post("smart-home.ringLiveSessionStarted", new
            {
                requestId,
                sessionId = result.SessionId,
                answerSdp = result.AnswerSdp,
            }, cancellationToken).ConfigureAwait(false);
        }

        private async Task StopRingLiveSessionAsync(JsonElement payload, CancellationToken cancellationToken)
        {
            var requestId = payload.TryGetProperty("requestId", out var requestIdElement) ? requestIdElement.GetString() ?? string.Empty : string.Empty;
            var sessionId = payload.TryGetProperty("sessionId", out var sessionIdElement) ? sessionIdElement.GetString() ?? string.Empty : string.Empty;

            var result = await _runtimeService.StopRingLiveSessionAsync(sessionId, cancellationToken).ConfigureAwait(false);
            if (!result.Ok)
            {
                await Post("smart-home.ringLiveSessionFailed", new
                {
                    requestId,
                    message = result.Message,
                }, cancellationToken).ConfigureAwait(false);
                return;
            }

            await Post("smart-home.ringLiveSessionStopped", new
            {
                requestId,
                message = result.Message,
            }, cancellationToken).ConfigureAwait(false);
        }

        private async Task ActivateRingLiveSessionSpeakerAsync(JsonElement payload, CancellationToken cancellationToken)
        {
            var requestId = payload.TryGetProperty("requestId", out var requestIdElement) ? requestIdElement.GetString() ?? string.Empty : string.Empty;
            var sessionId = payload.TryGetProperty("sessionId", out var sessionIdElement) ? sessionIdElement.GetString() ?? string.Empty : string.Empty;

            if (string.IsNullOrWhiteSpace(requestId) || string.IsNullOrWhiteSpace(sessionId))
            {
                await Post("smart-home.ringLiveSessionFailed", new
                {
                    requestId,
                    cameraId = string.Empty,
                    message = "Ring speaker activation request is incomplete.",
                }, cancellationToken).ConfigureAwait(false);
                return;
            }

            var result = await _runtimeService.ActivateRingLiveSessionSpeakerAsync(sessionId, cancellationToken).ConfigureAwait(false);
            if (!result.Ok)
            {
                await Post("smart-home.ringLiveSessionFailed", new
                {
                    requestId,
                    cameraId = string.Empty,
                    message = result.Message,
                }, cancellationToken).ConfigureAwait(false);
                return;
            }

            await Post("smart-home.ringLiveSessionSpeakerActivated", new
            {
                requestId,
                sessionId = string.IsNullOrWhiteSpace(result.SessionId) ? sessionId : result.SessionId,
                message = result.Message,
            }, cancellationToken).ConfigureAwait(false);
        }

        private async Task RequestMicrophoneAccessAsync(JsonElement payload, CancellationToken cancellationToken)
        {
            var requestId = payload.TryGetProperty("requestId", out var requestIdElement) ? requestIdElement.GetString() ?? string.Empty : string.Empty;

            try
            {
                ReserveSmartHomeMicrophone();
                await Post("smart-home.microphoneAccessGranted", new
                {
                    requestId,
                    cameraId = payload.TryGetProperty("deviceId", out var grantedDeviceIdElement) ? grantedDeviceIdElement.GetString() ?? string.Empty : string.Empty,
                    message = "Atlas reserved the microphone for Smart Home talkback."
                }, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                await Post("smart-home.microphoneAccessFailed", new { requestId, cameraId = payload.TryGetProperty("deviceId", out var failedDeviceIdElement) ? failedDeviceIdElement.GetString() ?? string.Empty : string.Empty, message = ex.Message }, cancellationToken).ConfigureAwait(false);
            }
        }

        private async Task ReleaseMicrophoneAccessAsync(JsonElement payload, CancellationToken cancellationToken)
        {
            var requestId = payload.TryGetProperty("requestId", out var requestIdElement) ? requestIdElement.GetString() ?? string.Empty : string.Empty;

            try
            {
                ReleaseSmartHomeMicrophone();
                await Post("smart-home.microphoneAccessReleased", new
                {
                    requestId,
                    cameraId = payload.TryGetProperty("deviceId", out var releasedDeviceIdElement) ? releasedDeviceIdElement.GetString() ?? string.Empty : string.Empty,
                    message = "Atlas released the Smart Home talkback microphone."
                }, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                await Post("smart-home.microphoneAccessFailed", new { requestId, cameraId = payload.TryGetProperty("deviceId", out var releaseFailedDeviceIdElement) ? releaseFailedDeviceIdElement.GetString() ?? string.Empty : string.Empty, message = ex.Message }, cancellationToken).ConfigureAwait(false);
            }
        }

        private void ReserveSmartHomeMicrophone()
        {
            lock (_smartHomeMicrophoneLock)
            {
                _smartHomeMicrophoneReservationCount++;
                if (_smartHomeMicrophoneReservationCount > 1)
                {
                    return;
                }

                var wakeWordService = AtlasAI.Voice.WakeWordService.Instance;
                var shouldResumeWakeWord = wakeWordService.IsListening || wakeWordService.ContinuousListeningEnabled;
                _resumeWakeWordAfterSmartHomeMicrophone = shouldResumeWakeWord;

                if (!shouldResumeWakeWord)
                {
                    return;
                }

                wakeWordService.ContinuousListeningEnabled = false;
                wakeWordService.Stop(AtlasAI.Voice.WakeStopReason.ExternalHandlerDefer);
                AppLogger.LogInfo("[SmartHome] Reserved microphone for talkback by pausing wake word detection.");
            }
        }

        private void ReleaseSmartHomeMicrophone()
        {
            var shouldResumeWakeWord = false;

            lock (_smartHomeMicrophoneLock)
            {
                if (_smartHomeMicrophoneReservationCount <= 0)
                {
                    return;
                }

                _smartHomeMicrophoneReservationCount--;
                if (_smartHomeMicrophoneReservationCount > 0)
                {
                    return;
                }

                shouldResumeWakeWord = _resumeWakeWordAfterSmartHomeMicrophone;
                _resumeWakeWordAfterSmartHomeMicrophone = false;
            }

            if (!shouldResumeWakeWord)
            {
                return;
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    var preferences = PreferencesStore.Instance.Current;
                    if (!preferences.EnableWakeWord)
                    {
                        return;
                    }

                    await Task.Delay(350).ConfigureAwait(false);
                    var wakeWordService = AtlasAI.Voice.WakeWordService.Instance;
                    if (wakeWordService.IsListening)
                    {
                        return;
                    }

                    wakeWordService.ContinuousListeningEnabled = true;
                    var started = await wakeWordService.StartAsync().ConfigureAwait(false);
                    AppLogger.LogInfo($"[SmartHome] Restored wake word detection after talkback: {(started ? "started" : "not-started")}");
                }
                catch (Exception ex)
                {
                    AppLogger.LogError($"[SmartHome] Failed to restore wake word detection after talkback: {ex.Message}");
                }
            });
        }

        private void ReleaseAllSmartHomeMicrophoneReservations()
        {
            var hadReservations = false;
            var shouldResumeWakeWord = false;

            lock (_smartHomeMicrophoneLock)
            {
                hadReservations = _smartHomeMicrophoneReservationCount > 0;
                shouldResumeWakeWord = _resumeWakeWordAfterSmartHomeMicrophone;
                _smartHomeMicrophoneReservationCount = 0;
                _resumeWakeWordAfterSmartHomeMicrophone = false;
            }

            if (!hadReservations || !shouldResumeWakeWord)
            {
                return;
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    var preferences = PreferencesStore.Instance.Current;
                    if (!preferences.EnableWakeWord)
                    {
                        return;
                    }

                    await Task.Delay(350).ConfigureAwait(false);
                    var wakeWordService = AtlasAI.Voice.WakeWordService.Instance;
                    if (wakeWordService.IsListening)
                    {
                        return;
                    }

                    wakeWordService.ContinuousListeningEnabled = true;
                    var started = await wakeWordService.StartAsync().ConfigureAwait(false);
                    AppLogger.LogInfo($"[SmartHome] Restored wake word detection after Smart Home page unload: {(started ? "started" : "not-started")}");
                }
                catch (Exception ex)
                {
                    AppLogger.LogError($"[SmartHome] Failed to restore wake word detection after Smart Home page unload: {ex.Message}");
                }
            });
        }

        private async Task EnsureEmbeddedBrowserInitializedAsync()
        {
            if (_embeddedBrowserInitialized && EmbeddedBrowserWebView?.CoreWebView2 != null)
                return;

            Directory.CreateDirectory(SmartHomeEmbeddedBrowserUserDataFolder);
            var environment = await CoreWebView2Environment.CreateAsync(null, SmartHomeEmbeddedBrowserUserDataFolder);
            await EmbeddedBrowserWebView.EnsureCoreWebView2Async(environment);
            try { EmbeddedBrowserWebView.DefaultBackgroundColor = System.Drawing.Color.Black; } catch { }
            var settings = EmbeddedBrowserWebView.CoreWebView2.Settings;
            settings.AreDefaultContextMenusEnabled = true;
            settings.AreDevToolsEnabled = true;
            settings.AreBrowserAcceleratorKeysEnabled = true;

            EmbeddedBrowserWebView.CoreWebView2.NavigationCompleted -= EmbeddedBrowserWebView_NavigationCompleted;
            EmbeddedBrowserWebView.CoreWebView2.NavigationCompleted += EmbeddedBrowserWebView_NavigationCompleted;
            EmbeddedBrowserWebView.CoreWebView2.WebMessageReceived -= EmbeddedBrowserWebView_WebMessageReceived;
            EmbeddedBrowserWebView.CoreWebView2.WebMessageReceived += EmbeddedBrowserWebView_WebMessageReceived;
            EmbeddedBrowserWebView.CoreWebView2.PermissionRequested -= EmbeddedBrowserWebView_PermissionRequested;
            EmbeddedBrowserWebView.CoreWebView2.PermissionRequested += EmbeddedBrowserWebView_PermissionRequested;
            _embeddedBrowserInitialized = true;
        }

        private async Task ShowEmbeddedBrowserAsync(string url, string title, string? recordingUrl = null)
        {
            await EnsureEmbeddedBrowserInitializedAsync();
            EmbeddedBrowserTitle.Text = title;
            EmbeddedBrowserUrlText.Text = url;
            EmbeddedBrowserLoadingTitle.Text = "Loading provider view...";
            EmbeddedBrowserLoadingSubtitle.Text = "Atlas is opening the live camera player inside Smart Home.";
            EmbeddedBrowserLoadingOverlay.Visibility = Visibility.Visible;
            EmbeddedBrowserOverlay.Visibility = Visibility.Visible;
            // Hide the React WebView while the embedded browser is active to avoid
            // WebView2 HWND airspace issues where two WebView2 controls overlap.
            SmartHomeWebView.Visibility = Visibility.Collapsed;
            CameraWorkspaceContentHost.Visibility = Visibility.Collapsed;
            EmbeddedBrowserWebView.Visibility = Visibility.Visible;
            ConfigureRecordingSource(string.IsNullOrWhiteSpace(recordingUrl) ? url : recordingUrl, title);

            try
            {
                EmbeddedBrowserWebView.CoreWebView2.Navigate(url);
            }
            catch
            {
                ClearRecordingSource();
                ShowEmbeddedBrowserFailure(url, "Atlas could not open the camera player.");
            }
        }

        private Task ShowEmbeddedBrowserOnUiThreadAsync(string url, string title, string? recordingUrl = null)
        {
            if (Dispatcher.CheckAccess())
            {
                return ShowEmbeddedBrowserAsync(url, title, recordingUrl);
            }

            return Dispatcher.InvokeAsync(() => ShowEmbeddedBrowserAsync(url, title, recordingUrl)).Task.Unwrap();
        }

        private void EmbeddedBrowserWebView_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            try
            {
                if (!e.IsSuccess)
                {
                    ShowEmbeddedBrowserFailure(EmbeddedBrowserWebView?.Source?.ToString() ?? EmbeddedBrowserUrlText.Text, $"Navigation failed: {e.WebErrorStatus}");
                    return;
                }

                EmbeddedBrowserLoadingOverlay.Visibility = Visibility.Collapsed;
                if (EmbeddedBrowserWebView?.Source != null)
                    EmbeddedBrowserUrlText.Text = EmbeddedBrowserWebView.Source.ToString();

                UpdateRecordingUi();
            }
            catch
            {
            }
        }

        private void ShowEmbeddedBrowserFailure(string url, string message)
        {
            EmbeddedBrowserTitle.Text = "Camera View Unavailable";
            EmbeddedBrowserUrlText.Text = url;
            EmbeddedBrowserLoadingTitle.Text = "Atlas could not load the camera view.";
            EmbeddedBrowserLoadingSubtitle.Text = message;
            EmbeddedBrowserLoadingOverlay.Visibility = Visibility.Visible;
            EmbeddedBrowserOverlay.Visibility = Visibility.Visible;
            UpdateRecordingUi(message);
        }

        private Task ShowEmbeddedBrowserFailureOnUiThreadAsync(string url, string message)
        {
            if (Dispatcher.CheckAccess())
            {
                ShowEmbeddedBrowserFailure(url, message);
                return Task.CompletedTask;
            }

            return Dispatcher.InvokeAsync(() => ShowEmbeddedBrowserFailure(url, message)).Task;
        }

        private static async Task<bool> WaitForEmbeddedBrowserTargetAsync(string url)
        {
            try
            {
                if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
                    return false;

                if (!string.Equals(uri.Host, "127.0.0.1", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                var deadlineUtc = DateTime.UtcNow.AddSeconds(8);
                while (DateTime.UtcNow < deadlineUtc)
                {
                    try
                    {
                        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
                        using var response = await EmbeddedBrowserProbeClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
                        if (response.IsSuccessStatusCode)
                            return true;
                    }
                    catch
                    {
                    }

                    await Task.Delay(250).ConfigureAwait(false);
                }
            }
            catch
            {
            }

            return false;
        }

        private async void CloseEmbeddedBrowser_Click(object sender, RoutedEventArgs e)
        {
            await CloseAllCameraWorkspaceSessionsAsync().ConfigureAwait(true);

            HideEmbeddedBrowserOverlayCore();

            await Task.CompletedTask.ConfigureAwait(true);
        }

        private void ToggleCameraWorkspaceLayout_Click(object sender, RoutedEventArgs e)
        {
            if (_cameraWorkspaceSessions.Count == 0)
                return;

            _focusedCameraWorkspaceSessionId = string.IsNullOrWhiteSpace(_focusedCameraWorkspaceSessionId)
                ? _cameraWorkspaceSessions.Keys.FirstOrDefault() ?? string.Empty
                : string.Empty;

            _ = RenderCameraWorkspaceAsync();
            UpdateWorkspaceHeader();
        }

        private void ConfigureRecordingSource(string sourceUrl, string cameraName)
        {
            UpdateRecordingUi();
        }

        private void ClearRecordingSource()
        {
            UpdateRecordingUi();
        }

        private void UpdateRecordingUi(string? overrideMessage = null)
        {
            if (EmbeddedBrowserRecordButton == null || EmbeddedBrowserRecordingStatusText == null)
                return;

            if (_cameraRecordingService.IsRecording)
            {
                EmbeddedBrowserRecordButton.IsEnabled = false;
                if (EmbeddedBrowserRecordButton.Content is TextBlock recordText)
                    recordText.Text = "Record In Workspace";
                EmbeddedBrowserRecordingStatusText.Text = !string.IsNullOrWhiteSpace(overrideMessage)
                    ? overrideMessage
                    : $"Recording now: {_cameraRecordingService.ActiveRecordingCount} camera(s).";
                return;
            }

            if (EmbeddedBrowserRecordButton.Content is TextBlock idleText)
                idleText.Text = string.IsNullOrWhiteSpace(_focusedCameraWorkspaceSessionId) ? "Single View" : "Grid View";

            EmbeddedBrowserRecordButton.IsEnabled = _cameraWorkspaceSessions.Count > 0;
            EmbeddedBrowserRecordingStatusText.Text = !string.IsNullOrWhiteSpace(overrideMessage)
                ? overrideMessage
                : "Camera recording is managed per tile in the embedded camera workspace.";
        }

        private void CameraWorkspaceControl_WorkspaceChanged(object? sender, EventArgs e)
        {
            UpdateWorkspaceHeader();
        }

        private void UpdateWorkspaceHeader()
        {
            if (EmbeddedBrowserRecordButton?.Content is TextBlock actionText)
                actionText.Text = string.IsNullOrWhiteSpace(_focusedCameraWorkspaceSessionId) ? "Single View" : "Grid View";

            if (_cameraWorkspaceSessions.Count == 0)
            {
                EmbeddedBrowserTitle.Text = "Camera Workspace";
                EmbeddedBrowserUrlText.Text = "No cameras open.";
                EmbeddedBrowserRecordingStatusText.Text = "Open cameras from Camera Centre. In grid view all opened cameras stay loaded and visible here.";
                return;
            }

            EmbeddedBrowserTitle.Text = "Camera Workspace";
            EmbeddedBrowserUrlText.Text = _cameraWorkspaceSessions.Count == 1
                ? "1 camera open."
                : $"{_cameraWorkspaceSessions.Count} cameras open in grid view.";
            EmbeddedBrowserRecordingStatusText.Text = !string.IsNullOrWhiteSpace(_focusedCameraWorkspaceSessionId)
                ? "Single view is active. Press Grid View to show every open camera again."
                : "Grid view is active. Every opened camera stays visible here until you close it.";
        }

        private async void EmbeddedBrowserWebView_WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                using var document = JsonDocument.Parse(e.WebMessageAsJson);
                var root = document.RootElement;
                var type = root.TryGetProperty("type", out var typeElement) ? typeElement.GetString() ?? string.Empty : string.Empty;
                var payload = root.TryGetProperty("payload", out var payloadElement) ? payloadElement : default;
                var sessionId = payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty("sessionId", out var sessionIdElement)
                    ? sessionIdElement.GetString() ?? string.Empty
                    : string.Empty;

                switch (type)
                {
                    case "camera-workspace.close-all":
                        CloseEmbeddedBrowser_Click(this, new RoutedEventArgs());
                        break;
                    case "camera-workspace.close-session":
                        await CloseCameraWorkspaceSessionAsync(sessionId).ConfigureAwait(true);
                        break;
                    case "camera-workspace.toggle-focus":
                        _focusedCameraWorkspaceSessionId = string.Equals(_focusedCameraWorkspaceSessionId, sessionId, StringComparison.OrdinalIgnoreCase)
                            ? string.Empty
                            : sessionId;
                        await RenderCameraWorkspaceAsync().ConfigureAwait(true);
                        UpdateWorkspaceHeader();
                        break;
                    case "camera-workspace.toggle-recording":
                        await ToggleCameraWorkspaceRecordingAsync(sessionId).ConfigureAwait(true);
                        break;
                }
            }
            catch
            {
            }
        }

        private async Task ToggleCameraWorkspaceRecordingAsync(string sessionId)
        {
            if (!_cameraWorkspaceSessions.TryGetValue(sessionId, out var session))
                return;

            if (_cameraRecordingService.IsRecordingSession(session.RecordingId))
            {
                var stop = await _cameraRecordingService.StopAsync(session.RecordingId, CancellationToken.None).ConfigureAwait(true);
                session.StatusMessage = stop.Message;
            }
            else if (!SmartHomeCameraRecordingService.IsSupportedSourceUrl(session.RecordingUrl))
            {
                session.StatusMessage = "Recording is available when Atlas has a direct stream URL.";
            }
            else
            {
                var start = await _cameraRecordingService.StartAsync(session.RecordingUrl, session.Title, session.RecordingId, CancellationToken.None).ConfigureAwait(true);
                session.StatusMessage = start.Message;
            }

            await RenderCameraWorkspaceAsync().ConfigureAwait(true);
            UpdateWorkspaceHeader();
        }

        private async Task CloseCameraWorkspaceSessionAsync(string sessionId)
        {
            if (!_cameraWorkspaceSessions.TryGetValue(sessionId, out var session))
                return;

            if (_cameraRecordingService.IsRecordingSession(session.RecordingId))
            {
                try
                {
                    await _cameraRecordingService.StopAsync(session.RecordingId, CancellationToken.None).ConfigureAwait(true);
                }
                catch
                {
                }
            }

            if (!string.IsNullOrWhiteSpace(session.ManagedCameraId))
            {
                try
                {
                    var stopResult = await _runtimeService.StopRingManagedLiveViewAsync(session.ManagedCameraId, CancellationToken.None).ConfigureAwait(true);
                    _activeManagedRingCameraIds.Remove(session.ManagedCameraId);
                    await Post("smart-home.ringManagedLiveViewStopped", new
                    {
                        cameraId = string.IsNullOrWhiteSpace(stopResult.CameraId) ? session.ManagedCameraId : stopResult.CameraId,
                        message = stopResult.Message,
                    }, CancellationToken.None).ConfigureAwait(true);
                }
                catch
                {
                }
            }

            _cameraWorkspaceSessions.Remove(sessionId);
            if (string.Equals(_focusedCameraWorkspaceSessionId, sessionId, StringComparison.OrdinalIgnoreCase))
                _focusedCameraWorkspaceSessionId = string.Empty;

            if (_cameraWorkspaceSessions.Count == 0)
            {
                HideEmbeddedBrowserOverlayCore();
                return;
            }

            await RenderCameraWorkspaceAsync().ConfigureAwait(true);
            UpdateWorkspaceHeader();
        }

        private async Task CloseAllCameraWorkspaceSessionsAsync()
        {
            var sessions = _cameraWorkspaceSessions.Values.ToArray();
            foreach (var session in sessions)
            {
                if (_cameraRecordingService.IsRecordingSession(session.RecordingId))
                {
                    try
                    {
                        await _cameraRecordingService.StopAsync(session.RecordingId, CancellationToken.None).ConfigureAwait(true);
                    }
                    catch
                    {
                    }
                }

                if (!string.IsNullOrWhiteSpace(session.ManagedCameraId))
                {
                    try
                    {
                        var stopResult = await _runtimeService.StopRingManagedLiveViewAsync(session.ManagedCameraId, CancellationToken.None).ConfigureAwait(true);
                        _activeManagedRingCameraIds.Remove(session.ManagedCameraId);
                        await Post("smart-home.ringManagedLiveViewStopped", new
                        {
                            cameraId = string.IsNullOrWhiteSpace(stopResult.CameraId) ? session.ManagedCameraId : stopResult.CameraId,
                            message = stopResult.Message,
                        }, CancellationToken.None).ConfigureAwait(true);
                    }
                    catch
                    {
                    }
                }
            }

            _cameraWorkspaceSessions.Clear();
            _focusedCameraWorkspaceSessionId = string.Empty;
        }

        private void HideEmbeddedBrowserOverlayCore()
        {
            try
            {
                EmbeddedBrowserOverlay.Visibility = Visibility.Collapsed;
                EmbeddedBrowserLoadingOverlay.Visibility = Visibility.Collapsed;
                SmartHomeWebView.Visibility = Visibility.Visible;
                CameraWorkspaceContentHost.Content = null;
                CameraWorkspaceContentHost.Visibility = Visibility.Collapsed;
                EmbeddedBrowserWebView.Visibility = Visibility.Collapsed;
                if (EmbeddedBrowserWebView?.CoreWebView2 != null)
                    EmbeddedBrowserWebView.CoreWebView2.Navigate("about:blank");
            }
            catch
            {
            }
        }

        private async Task RenderCameraWorkspaceAsync()
        {
            if (_cameraWorkspaceSessions.Count == 0)
                return;

            await EnsureEmbeddedBrowserInitializedAsync().ConfigureAwait(true);
            EmbeddedBrowserWebView.Visibility = Visibility.Visible;
            EmbeddedBrowserLoadingOverlay.Visibility = Visibility.Collapsed;
            EmbeddedBrowserWebView.CoreWebView2.NavigateToString(BuildCameraWorkspaceHtml());
        }

        private void UpdateCameraWorkspaceStatus(string sessionId, string message)
        {
            if (_cameraWorkspaceSessions.TryGetValue(sessionId, out var session))
            {
                session.StatusMessage = string.IsNullOrWhiteSpace(message) ? session.StatusMessage : message.Trim();
                _ = RenderCameraWorkspaceAsync();
            }
        }

        private string BuildCameraWorkspaceHtml()
        {
            var visibleSessions = string.IsNullOrWhiteSpace(_focusedCameraWorkspaceSessionId)
                ? _cameraWorkspaceSessions.Values.ToArray()
                : _cameraWorkspaceSessions.Values.Where(session => string.Equals(session.SessionId, _focusedCameraWorkspaceSessionId, StringComparison.OrdinalIgnoreCase)).ToArray();

            var columns = string.IsNullOrWhiteSpace(_focusedCameraWorkspaceSessionId)
                ? GetCameraWorkspaceColumnCount(visibleSessions.Length)
                : 1;
            var sessionCards = string.Join(Environment.NewLine, visibleSessions.Select(BuildCameraWorkspaceSessionCard));
            var layoutLabel = string.IsNullOrWhiteSpace(_focusedCameraWorkspaceSessionId) ? "Single View" : "Grid View";
            var recordingSummary = _cameraRecordingService.ActiveRecordingCount == 0
                ? "No recordings running."
                : _cameraRecordingService.ActiveRecordingCount == 1
                    ? "1 recording running."
                    : $"{_cameraRecordingService.ActiveRecordingCount} recordings running.";

            return $@"<!DOCTYPE html>
<html lang='en'>
<head>
<meta charset='utf-8' />
<meta name='viewport' content='width=device-width, initial-scale=1' />
<title>Atlas Camera Workspace</title>
<style>
  :root {{ color-scheme: dark; --bg:#05080d; --panel:rgba(8,16,29,0.94); --border:rgba(103,232,249,0.18); --text:#d8f9ff; --muted:#7fe8f9; --danger:#ff6b7d; }}
  * {{ box-sizing:border-box; }}
  body {{ margin:0; font-family:Segoe UI,sans-serif; background:radial-gradient(circle at top, rgba(0,212,255,0.08), transparent 34%), var(--bg); color:var(--text); }}
  .shell {{ min-height:100vh; padding:18px; }}
  .toolbar {{ display:flex; align-items:center; justify-content:space-between; gap:12px; margin-bottom:16px; padding:12px 14px; border:1px solid var(--border); border-radius:16px; background:rgba(5,10,18,0.85); position:sticky; top:0; z-index:20; backdrop-filter:blur(12px); }}
  .summary {{ font-size:13px; color:var(--muted); letter-spacing:0.08em; text-transform:uppercase; }}
  .toolbar-buttons {{ display:flex; gap:10px; flex-wrap:wrap; }}
  button {{ border:1px solid var(--border); background:rgba(22,35,48,0.92); color:var(--text); border-radius:10px; padding:10px 14px; cursor:pointer; font-size:12px; font-weight:600; }}
  button.danger {{ border-color:rgba(255,107,125,0.45); background:rgba(58,26,36,0.92); }}
  .grid {{ display:grid; grid-template-columns:repeat({columns}, minmax(0, 1fr)); gap:16px; align-items:start; }}
  .card {{ border:1px solid var(--border); background:var(--panel); border-radius:18px; overflow:hidden; box-shadow:0 18px 40px rgba(0,0,0,0.24); }}
  .card-header {{ display:flex; justify-content:space-between; gap:12px; align-items:center; padding:14px 16px 10px; }}
  .card-title {{ font-size:16px; font-weight:700; }}
  .card-actions {{ display:flex; gap:8px; flex-wrap:wrap; justify-content:flex-end; }}
  .viewer {{ padding:0 16px 12px; }}
  iframe {{ width:100%; height:{(string.IsNullOrWhiteSpace(_focusedCameraWorkspaceSessionId) ? (columns == 1 ? 420 : 260) : 620)}px; border:0; border-radius:14px; background:#08111d; }}
  .status {{ padding:0 16px 16px; color:var(--muted); font-size:12px; line-height:1.5; }}
  @media (max-width:1100px) {{ .grid {{ grid-template-columns:1fr; }} iframe {{ height:260px; }} }}
</style>
</head>
<body>
  <div class='shell'>
    <div class='toolbar'>
      <div>
        <div style='font-size:18px;font-weight:700;'>Camera Workspace</div>
        <div class='summary'>{WebUtility.HtmlEncode(_cameraWorkspaceSessions.Count == 1 ? "1 camera open." : $"{_cameraWorkspaceSessions.Count} cameras open.")} {WebUtility.HtmlEncode(recordingSummary)}</div>
      </div>
      <div class='toolbar-buttons'>
        <button type='button' onclick='postMessageToHost(&quot;camera-workspace.toggle-focus&quot;, {{ sessionId: &quot;{JavaScriptStringEncode(_focusedCameraWorkspaceSessionId)}&quot; }})'>{WebUtility.HtmlEncode(layoutLabel)}</button>
        <button type='button' class='danger' onclick='postMessageToHost(&quot;camera-workspace.close-all&quot;, {{}})'>Close Workspace</button>
      </div>
    </div>
    <div class='grid'>
      {sessionCards}
    </div>
  </div>
  <script>
    function postMessageToHost(type, payload) {{
      if (window.chrome && window.chrome.webview) {{
        window.chrome.webview.postMessage(JSON.stringify({{ type: type, payload: payload || {{}} }}));
      }}
    }}
  </script>
</body>
</html>";
        }

        private string BuildCameraWorkspaceSessionCard(CameraWorkspaceSession session)
        {
            var focusLabel = string.Equals(_focusedCameraWorkspaceSessionId, session.SessionId, StringComparison.OrdinalIgnoreCase) ? "Grid" : "Single";
            var recordLabel = _cameraRecordingService.IsRecordingSession(session.RecordingId) ? "Stop Recording" : "Record";

            return $@"<section class='card'>
  <div class='card-header'>
    <div class='card-title'>{WebUtility.HtmlEncode(session.Title)}</div>
    <div class='card-actions'>
    <button type='button' onclick='postMessageToHost(&quot;camera-workspace.toggle-recording&quot;, {{ sessionId: &quot;{JavaScriptStringEncode(session.SessionId)}&quot; }})'>{WebUtility.HtmlEncode(recordLabel)}</button>
    <button type='button' onclick='postMessageToHost(&quot;camera-workspace.toggle-focus&quot;, {{ sessionId: &quot;{JavaScriptStringEncode(session.SessionId)}&quot; }})'>{WebUtility.HtmlEncode(focusLabel)}</button>
    <button type='button' class='danger' onclick='postMessageToHost(&quot;camera-workspace.close-session&quot;, {{ sessionId: &quot;{JavaScriptStringEncode(session.SessionId)}&quot; }})'>Close</button>
    </div>
  </div>
  <div class='viewer'>
    <iframe src='{WebUtility.HtmlEncode(session.NavigationUrl)}' allow='autoplay; fullscreen; picture-in-picture'></iframe>
  </div>
  <div class='status'>{WebUtility.HtmlEncode(session.StatusMessage)}</div>
</section>";
        }

        private static int GetCameraWorkspaceColumnCount(int sessionCount)
        {
            if (sessionCount <= 1) return 1;
            if (sessionCount <= 4) return 2;
            if (sessionCount <= 9) return 3;
            return Math.Max(1, (int)Math.Ceiling(Math.Sqrt(sessionCount)));
        }

        private static string NormalizeCameraWorkspaceSessionId(string? sessionId, string? title, string? navigationUrl)
        {
            var normalized = (sessionId ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(normalized))
                return normalized;

            var normalizedTitle = (title ?? string.Empty).Trim();
            var normalizedUrl = (navigationUrl ?? string.Empty).Trim();
            return string.IsNullOrWhiteSpace(normalizedTitle) ? normalizedUrl : $"{normalizedTitle}:{normalizedUrl}";
        }

        private static string JavaScriptStringEncode(string value)
        {
            return (value ?? string.Empty)
                .Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("\"", "\\\"", StringComparison.Ordinal)
                .Replace("\r", string.Empty, StringComparison.Ordinal)
                .Replace("\n", "\\n", StringComparison.Ordinal);
        }

        private static bool IsRingUrl(string url)
        {
            try
            {
                if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
                    return false;

                var host = (uri.Host ?? string.Empty).Trim();
                return host.EndsWith("ring.com", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
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