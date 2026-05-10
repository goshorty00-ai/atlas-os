using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AtlasAI.Agent;
using AtlasAI.Core;
using AtlasAI.ITManagement;
using AtlasAI.Ledger;
using AtlasAI.Services;
using AtlasAI.Settings;
using QRCoderPngByteQRCode = QRCoder.PngByteQRCode;
using QRCoderQRCodeGenerator = QRCoder.QRCodeGenerator;

namespace AtlasAI.SmartHome
{
    internal sealed class SmartHomeRuntimeService
    {
        internal const string AnswerDoorCommandId = "atlas-answer-door";
        private const string AnswerDoorPhrase = "answer the door";
        private static readonly TimeSpan ProviderSnapshotTimeout = TimeSpan.FromSeconds(8);
        private static readonly TimeSpan SnapshotCacheDuration = TimeSpan.FromSeconds(3);
        private static readonly RingManagedLiveViewService RingManagedLiveViewService = new();
        private static readonly NetworkDiscovery NetworkDiscovery = new();
        private static DateTime? LastNetworkScanUtc;

        private SmartHomeSnapshot? _cachedSnapshot;
        private DateTime _cachedSnapshotUtc = DateTime.MinValue;
        private readonly SemaphoreSlim _snapshotLock = new(1, 1);

        public async Task<SmartHomeActionResult> ExecuteActionAsync(SmartHomeActionRequest request, CancellationToken cancellationToken)
        {
            var settings = SettingsStore.Current;
            var provider = CreateProviders(settings)
                .FirstOrDefault(candidate => string.Equals(candidate.ProviderId, request.ProviderId, StringComparison.OrdinalIgnoreCase));

            if (provider == null)
            {
                return new SmartHomeActionResult
                {
                    Ok = false,
                    Message = $"Unknown Smart Home provider '{request.ProviderId}'.",
                };
            }

            return await provider.ExecuteActionAsync(request, cancellationToken).ConfigureAwait(false);
        }

        public async Task<SmartHomeActionResult> ExecuteSceneAsync(string sceneId, CancellationToken cancellationToken)
        {
            var settings = SettingsStore.Current;
            var scene = settings.SmartHome.CustomScenes.FirstOrDefault(candidate => string.Equals(candidate.Id, sceneId, StringComparison.OrdinalIgnoreCase));
            if (scene == null)
            {
                return new SmartHomeActionResult
                {
                    Ok = false,
                    Message = "Scene was not found.",
                };
            }

            AppLogger.LogInfo($"[SmartHome][Scene] Executing scene '{scene.Name}' with {scene.Actions.Count} action(s).");

            return await ExecuteSceneActionsAsync(
                CreateProviders(settings).ToArray(),
                scene.Name,
                scene.Actions.Select(static action => new SmartHomeSceneActionState
                {
                    ProviderId = action.ProviderId,
                    DeviceId = action.DeviceId,
                    DeviceName = action.DeviceName,
                    Sku = action.Sku,
                    CapabilityType = action.CapabilityType,
                    CapabilityInstance = action.CapabilityInstance,
                    Value = ParseSceneValue(action.ValueJson),
                    HexColor = action.HexColor,
                }).ToArray(),
                cancellationToken).ConfigureAwait(false);
        }

        public async Task<SmartHomeActionResult> ExecuteScenePreviewAsync(IReadOnlyList<SmartHomeSceneActionState> actions, string? sceneName, CancellationToken cancellationToken)
        {
            var settings = SettingsStore.Current;
            var providers = CreateProviders(settings).ToArray();
            var effectiveSceneName = string.IsNullOrWhiteSpace(sceneName) ? "Preview Scene" : sceneName.Trim();

            AppLogger.LogInfo($"[SmartHome][Scene] Previewing scene '{effectiveSceneName}' with {actions.Count} action(s).");

            return await ExecuteSceneActionsAsync(providers, effectiveSceneName, actions, cancellationToken).ConfigureAwait(false);
        }

        private static async Task<SmartHomeActionResult> ExecuteSceneActionsAsync(
            ISmartHomeProvider[] providers,
            string sceneName,
            IReadOnlyList<SmartHomeSceneActionState> actions,
            CancellationToken cancellationToken)
        {
            var successCount = 0;
            var failureMessages = new List<string>();

            foreach (var action in actions)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var provider = providers.FirstOrDefault(candidate => string.Equals(candidate.ProviderId, action.ProviderId, StringComparison.OrdinalIgnoreCase));
                if (provider == null)
                {
                    failureMessages.Add($"{action.DeviceName}: unknown provider '{action.ProviderId}'.");
                    AppLogger.LogWarning($"[SmartHome][Scene] Unknown provider '{action.ProviderId}' in scene '{sceneName}'.");
                    continue;
                }

                var result = await provider.ExecuteActionAsync(new SmartHomeActionRequest
                {
                    ProviderId = action.ProviderId,
                    DeviceId = action.DeviceId,
                    Sku = action.Sku,
                    CapabilityType = action.CapabilityType,
                    CapabilityInstance = action.CapabilityInstance,
                    Value = action.Value,
                }, cancellationToken).ConfigureAwait(false);

                if (!result.Ok)
                {
                    var failureMessage = string.IsNullOrWhiteSpace(action.DeviceName)
                        ? result.Message
                        : $"{action.DeviceName}: {result.Message}";
                    failureMessages.Add(failureMessage);
                    AppLogger.LogWarning($"[SmartHome][Scene] Action failed in '{sceneName}' for '{action.DeviceName}' ({action.CapabilityInstance}): {result.Message}");
                    continue;
                }

                successCount++;
            }

            var totalActions = actions.Count;
            var allSucceeded = totalActions > 0 && successCount == totalActions;
            var partiallySucceeded = successCount > 0 && successCount < totalActions;

            var message = allSucceeded
                ? $"Scene '{sceneName}' executed across {successCount} device action(s)."
                : partiallySucceeded
                    ? $"Scene '{sceneName}' applied {successCount} of {totalActions} action(s). {string.Join(" ", failureMessages.Take(2))}"
                    : failureMessages.FirstOrDefault() ?? $"Scene '{sceneName}' did not execute any actions.";

            if (allSucceeded)
                AppLogger.LogInfo($"[SmartHome][Scene] Scene '{sceneName}' completed successfully.");
            else if (partiallySucceeded)
                AppLogger.LogWarning($"[SmartHome][Scene] Scene '{sceneName}' partially completed: {message}");
            else
                AppLogger.LogWarning($"[SmartHome][Scene] Scene '{sceneName}' failed: {message}");

            return new SmartHomeActionResult
            {
                Ok = allSucceeded || partiallySucceeded,
                Message = message,
            };
        }

        public async Task<RingLiveSessionStartResult> StartRingLiveSessionAsync(RingLiveSessionStartRequest request, CancellationToken cancellationToken)
        {
            var settings = SettingsStore.Current;
            var provider = CreateProviders(settings)
                .OfType<RingProvider>()
                .FirstOrDefault();

            if (provider == null)
            {
                return new RingLiveSessionStartResult
                {
                    Ok = false,
                    Message = "Ring provider is unavailable.",
                };
            }

            return await provider.StartLiveSessionAsync(request, cancellationToken).ConfigureAwait(false);
        }

        public async Task<RingLiveSessionStopResult> StopRingLiveSessionAsync(string sessionId, CancellationToken cancellationToken)
        {
            var settings = SettingsStore.Current;
            var provider = CreateProviders(settings)
                .OfType<RingProvider>()
                .FirstOrDefault();

            if (provider == null)
            {
                return new RingLiveSessionStopResult
                {
                    Ok = false,
                    Message = "Ring provider is unavailable.",
                };
            }

            return await provider.StopLiveSessionAsync(sessionId, cancellationToken).ConfigureAwait(false);
        }

        public async Task<RingLiveSessionSpeakerResult> ActivateRingLiveSessionSpeakerAsync(string sessionId, CancellationToken cancellationToken)
        {
            var settings = SettingsStore.Current;
            var provider = CreateProviders(settings)
                .OfType<RingProvider>()
                .FirstOrDefault();

            if (provider == null)
            {
                return new RingLiveSessionSpeakerResult
                {
                    Ok = false,
                    SessionId = sessionId,
                    Message = "Ring provider is unavailable.",
                };
            }

            return await provider.ActivateLiveSessionSpeakerAsync(sessionId, cancellationToken).ConfigureAwait(false);
        }

        public Task<RingManagedLiveViewStartResult> StartRingManagedLiveViewAsync(string deviceId, CancellationToken cancellationToken)
        {
            var settings = SettingsStore.Current;
            return RingManagedLiveViewService.StartAsync(settings.SmartHome.Ring, deviceId, cancellationToken);
        }

        public Task<RingManagedLiveViewStatusResult> GetRingManagedLiveViewStatusAsync(string deviceId, CancellationToken cancellationToken)
        {
            return RingManagedLiveViewService.GetStatusAsync(deviceId, cancellationToken);
        }

        public Task<RingManagedLiveViewStopResult> StopRingManagedLiveViewAsync(string deviceId, CancellationToken cancellationToken)
        {
            return RingManagedLiveViewService.StopAsync(deviceId, cancellationToken);
        }

        public async Task<SmartHomeOperationResult> DiscoverNetworkAsync(CancellationToken cancellationToken)
        {
            var devices = await NetworkDiscovery.ScanNetworkAsync(cancellationToken).ConfigureAwait(false);
            LastNetworkScanUtc = DateTime.UtcNow;

            var message = devices.Count == 0
                ? "No devices were found on the current network."
                : $"Network scan found {devices.Count} device{(devices.Count == 1 ? string.Empty : "s")}.";

            return new SmartHomeOperationResult
            {
                Ok = devices.Count > 0,
                Message = message,
            };
        }

        public SmartHomeOperationResult CreateAutomation(string trigger, IReadOnlyList<string> actions, string? schedule)
        {
            var sanitizedTrigger = (trigger ?? string.Empty).Trim();
            var sanitizedActions = (actions ?? Array.Empty<string>())
                .Where(static action => !string.IsNullOrWhiteSpace(action))
                .Select(static action => action.Trim())
                .ToList();

            if (string.IsNullOrWhiteSpace(sanitizedTrigger))
            {
                return new SmartHomeOperationResult
                {
                    Ok = false,
                    Message = "Automation trigger is required.",
                };
            }

            if (sanitizedActions.Count == 0)
            {
                return new SmartHomeOperationResult
                {
                    Ok = false,
                    Message = "Add at least one automation action.",
                };
            }

            var result = SmartAutomation.Instance.CreateAutomationEntry(sanitizedTrigger, sanitizedActions, string.IsNullOrWhiteSpace(schedule) ? null : schedule.Trim());
            return new SmartHomeOperationResult
            {
                Ok = result.Ok,
                Message = result.Message,
            };
        }

        public SmartHomeOperationResult ToggleAutomation(string automationId)
        {
            var result = SmartAutomation.Instance.ToggleAutomationById(automationId);
            return new SmartHomeOperationResult
            {
                Ok = result.Ok,
                Message = result.Message,
            };
        }

        public SmartHomeOperationResult DeleteAutomation(string automationId)
        {
            var result = SmartAutomation.Instance.DeleteAutomationById(automationId);
            return new SmartHomeOperationResult
            {
                Ok = result.Ok,
                Message = result.Message,
            };
        }

        public async Task<SmartHomeOperationResult> ExecuteAutomationAsync(string automationId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var result = await SmartAutomation.Instance.ExecuteAutomationByIdAsync(automationId).ConfigureAwait(false);
            return new SmartHomeOperationResult
            {
                Ok = result.Ok,
                Message = result.Message,
            };
        }

        public async Task<SmartHomeSnapshot> GetSnapshotAsync(CancellationToken cancellationToken)
        {
            // Return cached snapshot if still fresh to avoid hammering providers
            var cached = _cachedSnapshot;
            if (cached != null && (DateTime.UtcNow - _cachedSnapshotUtc) < SnapshotCacheDuration)
                return cached;

            await _snapshotLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                // Double-check after acquiring lock
                cached = _cachedSnapshot;
                if (cached != null && (DateTime.UtcNow - _cachedSnapshotUtc) < SnapshotCacheDuration)
                    return cached;

                var snapshot = await BuildSnapshotCoreAsync(cancellationToken).ConfigureAwait(false);
                LogCameraClassificationDiagnostics(snapshot.Providers);
                _cachedSnapshot = snapshot;
                _cachedSnapshotUtc = DateTime.UtcNow;
                return snapshot;
            }
            finally
            {
                _snapshotLock.Release();
            }
        }

        /// <summary>Invalidate the snapshot cache so the next GetSnapshotAsync rebuilds from providers.</summary>
        internal void InvalidateSnapshotCache() => _cachedSnapshot = null;

        private async Task<SmartHomeSnapshot> BuildSnapshotCoreAsync(CancellationToken cancellationToken)
        {
            var settings = SettingsStore.Current;
            if (EnsureBuiltInAnswerDoorCommand(settings))
                SettingsStore.Save(settings);

            var providers = CreateProviders(settings);
            var states = await Task.WhenAll(
                providers.Select(provider => GetProviderStateSafelyAsync(provider, cancellationToken))).ConfigureAwait(false);

            var allDevices = states.SelectMany(static provider => provider.Devices).ToList();
            var alerts = BuildAlerts(states, allDevices);

            return new SmartHomeSnapshot
            {
                GeneratedAtUtc = DateTime.UtcNow,
                Providers = states,
                TotalDevices = allDevices.Count,
                OnlineDevices = allDevices.Count(static device => device.IsOnline == true),
                ConfiguredProviders = states.Count(static provider => provider.Descriptor.IsConfigured),
                AgentSettings = new SmartHomeAgentSettingsState
                {
                    VoiceCommandsEnabled = settings.SmartHome.Agent.VoiceCommandsEnabled,
                    AnswerDoorEnabled = settings.SmartHome.Agent.AnswerDoorEnabled,
                    ShowDeviceShortcutsInSidebar = settings.SmartHome.Agent.ShowDeviceShortcutsInSidebar,
                    DefaultVolumeStep = settings.SmartHome.Agent.DefaultVolumeStep,
                },
                CustomGreetings = settings.SmartHome.CustomGreetings.Select(static greeting => new SmartHomeSavedGreeting
                {
                    Id = greeting.Id,
                    Enabled = greeting.Enabled,
                    Phrase = greeting.Phrase,
                    ResponseText = greeting.ResponseText,
                }).ToArray(),
                CustomCommands = settings.SmartHome.CustomCommands.Select(static command => new SmartHomeSavedCommand
                {
                    Id = command.Id,
                    Enabled = command.Enabled,
                    Phrase = command.Phrase,
                    TargetKind = string.IsNullOrWhiteSpace(command.TargetKind) ? "device" : command.TargetKind,
                    TargetScope = command.TargetScope,
                    TargetLabel = command.TargetLabel,
                    ProviderId = command.ProviderId,
                    DeviceId = command.DeviceId,
                    Sku = command.Sku,
                    CapabilityType = command.CapabilityType,
                    CapabilityInstance = command.CapabilityInstance,
                    Value = ParseCommandValue(command.ValueJson),
                    ResponseText = command.ResponseText,
                    DoorbellResponseText = command.DoorbellResponseText,
                }).ToArray(),
                CustomScenes = settings.SmartHome.CustomScenes.Select(static scene => new SmartHomeSavedScene
                {
                    Id = scene.Id,
                    Enabled = scene.Enabled,
                    Name = scene.Name,
                    Phrase = scene.Phrase,
                    PreviewColors = scene.PreviewColors.Where(static color => !string.IsNullOrWhiteSpace(color)).ToArray(),
                    Actions = scene.Actions.Select(static action => new SmartHomeSceneActionState
                    {
                        ProviderId = action.ProviderId,
                        DeviceId = action.DeviceId,
                        DeviceName = action.DeviceName,
                        Sku = action.Sku,
                        CapabilityType = action.CapabilityType,
                        CapabilityInstance = action.CapabilityInstance,
                        Value = ParseCommandValue(action.ValueJson),
                        HexColor = action.HexColor,
                    }).ToArray(),
                }).ToArray(),
                Alerts = alerts,
                Automations = SmartAutomation.Instance.GetAutomations().Select(static automation => new SmartHomeAutomationState
                {
                    Id = automation.Id,
                    Trigger = automation.Trigger,
                    Actions = automation.Actions.ToArray(),
                    Schedule = automation.Schedule ?? string.Empty,
                    CreatedAtUtc = automation.CreatedAt.ToUniversalTime(),
                    LastTriggeredUtc = automation.LastTriggered?.ToUniversalTime(),
                    TriggerCount = automation.TriggerCount,
                    IsEnabled = automation.IsEnabled,
                }).ToArray(),
                Security = BuildSecurityState(allDevices, alerts),
                CompanionPairing = BuildCompanionPairingState(),
                NetworkDiscovery = BuildNetworkDiscoveryState(),
            };
        }

        private static bool EnsureBuiltInAnswerDoorCommand(AtlasSettings settings)
        {
            var commands = settings.SmartHome.CustomCommands;
            var existing = commands.FirstOrDefault(command => string.Equals(command.Id, AnswerDoorCommandId, StringComparison.OrdinalIgnoreCase));
            if (existing == null)
            {
                commands.Add(new SmartHomeCustomCommandSetting
                {
                    Id = AnswerDoorCommandId,
                    Enabled = settings.SmartHome.Agent.AnswerDoorEnabled,
                    Phrase = AnswerDoorPhrase,
                    TargetKind = "atlas-intent",
                    TargetScope = "door-answer",
                    TargetLabel = "Front door answer",
                    ProviderId = "atlas",
                    DeviceId = "smart-home",
                    Sku = "atlas-intent",
                    CapabilityType = "atlas.intent",
                    CapabilityInstance = "door-answer",
                    ValueJson = "true",
                    ResponseText = "Opening the doorbell feed now.",
                    DoorbellResponseText = "Hello, Atlas here. One moment please.",
                });
                return true;
            }

            var changed = false;
            if (existing.TargetKind != "atlas-intent")
            {
                existing.TargetKind = "atlas-intent";
                changed = true;
            }

            if (existing.TargetScope != "door-answer")
            {
                existing.TargetScope = "door-answer";
                changed = true;
            }

            if (existing.CapabilityType != "atlas.intent")
            {
                existing.CapabilityType = "atlas.intent";
                changed = true;
            }

            if (existing.CapabilityInstance != "door-answer")
            {
                existing.CapabilityInstance = "door-answer";
                changed = true;
            }

            if (existing.Enabled != settings.SmartHome.Agent.AnswerDoorEnabled)
            {
                existing.Enabled = settings.SmartHome.Agent.AnswerDoorEnabled;
                changed = true;
            }

            if (string.IsNullOrWhiteSpace(existing.ResponseText))
            {
                existing.ResponseText = "Opening the doorbell feed now.";
                changed = true;
            }

            if (string.IsNullOrWhiteSpace(existing.DoorbellResponseText))
            {
                existing.DoorbellResponseText = "Hello, Atlas here. One moment please.";
                changed = true;
            }

            return changed;
        }

        private static async Task<SmartHomeProviderState> GetProviderStateSafelyAsync(
            ISmartHomeProvider provider,
            CancellationToken cancellationToken)
        {
            try
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(ProviderSnapshotTimeout);
                return await provider.GetStateAsync(timeoutCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                AppLogger.LogWarning($"[SmartHome] Provider '{provider.ProviderId}' snapshot timed out after {ProviderSnapshotTimeout.TotalSeconds:0}s.");
                return BuildProviderFailureState(provider, $"Provider timed out after {ProviderSnapshotTimeout.TotalSeconds:0} seconds.");
            }
            catch (Exception ex)
            {
                AppLogger.LogWarning($"[SmartHome] Provider '{provider.ProviderId}' snapshot failed: {ex.Message}");
                return BuildProviderFailureState(provider, ex.Message);
            }
        }

        private static SmartHomeProviderState BuildProviderFailureState(ISmartHomeProvider provider, string error)
        {
            var descriptor = provider.GetDescriptor();
            return new SmartHomeProviderState
            {
                ProviderId = provider.ProviderId,
                DisplayName = provider.DisplayName,
                Descriptor = descriptor,
                Error = string.IsNullOrWhiteSpace(error) ? "Provider request failed." : error,
                Devices = Array.Empty<SmartHomeDevice>(),
                SavedSettings = new SmartHomeProviderFormState(),
            };
        }

        private static SmartHomeNetworkDiscoveryState BuildNetworkDiscoveryState()
        {
            var networkInfo = NetworkDiscovery.GetLocalNetworkInfo();
            var devices = NetworkDiscovery.DiscoveredDevices
                .Select(static device => new SmartHomeNetworkDeviceState
                {
                    IpAddress = device.IPAddress,
                    Hostname = device.Hostname,
                    MacAddress = device.MacAddress,
                    DeviceType = device.DeviceType.ToString(),
                    IsOnline = device.IsOnline,
                    ResponseTime = device.ResponseTime,
                    LastSeenUtc = device.LastSeen.ToUniversalTime(),
                    OpenPorts = device.OpenPorts.ToArray(),
                    PortServices = device.GetPortServices(),
                    Vendor = device.Vendor,
                })
                .ToArray();

            return new SmartHomeNetworkDiscoveryState
            {
                IsScanning = NetworkDiscovery.IsScanning,
                LastScanUtc = LastNetworkScanUtc,
                Summary = NetworkDiscovery.GetDiscoverySummary(),
                LocalIp = networkInfo.LocalIP,
                SubnetMask = networkInfo.SubnetMask,
                Gateway = networkInfo.Gateway,
                DnsServer = networkInfo.DnsServer,
                AdapterName = networkInfo.AdapterName,
                Devices = devices,
            };
        }

        private static SmartHomeCompanionPairingState BuildCompanionPairingState()
        {
            var pairing = CompanionTransportService.Instance.GetPairingInfo();
            return new SmartHomeCompanionPairingState
            {
                IsAvailable = pairing.IsAvailable,
                AvailabilityMessage = pairing.AvailabilityMessage ?? string.Empty,
                BaseUrl = pairing.BaseUrl ?? string.Empty,
                Protocol = pairing.Protocol ?? string.Empty,
                Host = pairing.Host ?? string.Empty,
                Port = pairing.Port,
                DisplayName = pairing.DisplayName,
                ApiVersion = pairing.ApiVersion,
                PayloadFormat = pairing.PayloadFormat,
                Payload = pairing.Payload ?? string.Empty,
                QrCodeDataUrl = BuildQrCodeDataUrl(pairing.Payload),
            };
        }

        private static JsonElement ParseSceneValue(string? json)
        {
            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "null" : json);
            return document.RootElement.Clone();
        }

        private static string BuildQrCodeDataUrl(string? payload)
        {
            if (string.IsNullOrWhiteSpace(payload))
                return string.Empty;

            using var generator = new QRCoderQRCodeGenerator();
            foreach (var correctionLevel in new[]
                     {
                         QRCoderQRCodeGenerator.ECCLevel.Q,
                         QRCoderQRCodeGenerator.ECCLevel.M,
                         QRCoderQRCodeGenerator.ECCLevel.L,
                     })
            {
                try
                {
                    using var data = generator.CreateQrCode(payload, correctionLevel);
                    var pngQrCode = new QRCoderPngByteQRCode(data);
                    var pngBytes = pngQrCode.GetGraphic(
                        20,
                        new byte[] { 15, 23, 42, 255 },
                        new byte[] { 248, 250, 252, 255 },
                        true);

                    return $"data:image/png;base64,{Convert.ToBase64String(pngBytes)}";
                }
                catch (Exception ex)
                {
                    AppLogger.LogWarning($"[SmartHome] QR generation failed at correction level {correctionLevel}: {ex.Message}");
                }
            }

            return string.Empty;
        }

        private static SmartHomeSecurityState BuildSecurityState(IReadOnlyList<SmartHomeDevice> allDevices, IReadOnlyList<SmartHomeAlertState> alerts)
        {
            var securityAgent = SecurityAgent.Instance;
            var activeCameraCount = allDevices.Count(device => LooksLikeCamera(device));
            var sirenActive = allDevices.Any(device => device.Capabilities.Any(capability =>
                capability.Instance.Contains("siren", StringComparison.OrdinalIgnoreCase) &&
                capability.HasState &&
                capability.StateValue.ValueKind == JsonValueKind.True));

            return new SmartHomeSecurityState
            {
                Mode = securityAgent.CurrentMode.ToString(),
                ThreatLevel = securityAgent.ThreatLevel,
                IsScanning = securityAgent.IsScanning,
                ScanProgress = securityAgent.ScanProgress,
                RecentSecurityEventCount = alerts.Count(alert => string.Equals(alert.Category, "security", StringComparison.OrdinalIgnoreCase)),
                CriticalAlertCount = alerts.Count(alert => string.Equals(alert.Severity, "critical", StringComparison.OrdinalIgnoreCase) || string.Equals(alert.Severity, "high", StringComparison.OrdinalIgnoreCase)),
                ActiveCameraCount = activeCameraCount,
                SirenActive = sirenActive,
            };
        }

        private static SmartHomeAlertState[] BuildAlerts(IReadOnlyList<SmartHomeProviderState> states, IReadOnlyList<SmartHomeDevice> allDevices)
        {
            var alerts = new List<SmartHomeAlertState>();

            foreach (var ledgerEvent in LedgerManager.Instance.GetRecent(72).Take(24))
            {
                alerts.Add(new SmartHomeAlertState
                {
                    Id = ledgerEvent.Id,
                    TimestampUtc = ledgerEvent.Timestamp.ToUniversalTime(),
                    Category = NormalizeLedgerCategory(ledgerEvent.Category),
                    Severity = NormalizeLedgerSeverity(ledgerEvent.Severity),
                    Title = ledgerEvent.Title,
                    Detail = ledgerEvent.WhyItMatters,
                    Source = "Atlas Ledger",
                    IsResolved = ledgerEvent.IsResolved,
                });
            }

            foreach (var provider in states.Where(static provider => !string.IsNullOrWhiteSpace(provider.Error)))
            {
                alerts.Add(new SmartHomeAlertState
                {
                    Id = $"provider-error:{provider.ProviderId}",
                    TimestampUtc = DateTime.UtcNow,
                    Category = "integration",
                    Severity = "high",
                    Title = $"{provider.DisplayName} requires attention",
                    Detail = provider.Error,
                    Source = provider.DisplayName,
                    ProviderId = provider.ProviderId,
                    IsResolved = false,
                });
            }

            foreach (var device in allDevices.Where(static device => device.IsOnline == false))
            {
                alerts.Add(new SmartHomeAlertState
                {
                    Id = $"device-offline:{device.DeviceId}",
                    TimestampUtc = DateTime.UtcNow,
                    Category = LooksLikeCamera(device) ? "camera" : "device",
                    Severity = "medium",
                    Title = $"{device.Name} is offline",
                    Detail = "The latest provider update reported this device as unavailable or cached.",
                    Source = "Device telemetry",
                    DeviceId = device.DeviceId,
                    IsResolved = false,
                });
            }

            return alerts
                .OrderByDescending(static alert => alert.TimestampUtc)
                .ThenBy(static alert => alert.Title, StringComparer.OrdinalIgnoreCase)
                .Take(40)
                .ToArray();
        }

        private static string NormalizeLedgerCategory(LedgerCategory category)
        {
            return category switch
            {
                LedgerCategory.Security => "security",
                LedgerCategory.Network => "network",
                LedgerCategory.Startup => "startup",
                LedgerCategory.ScheduledTask => "automation",
                LedgerCategory.Mode => "mode",
                _ => "system",
            };
        }

        private static string NormalizeLedgerSeverity(LedgerSeverity severity)
        {
            return severity switch
            {
                LedgerSeverity.Critical => "critical",
                LedgerSeverity.High => "high",
                LedgerSeverity.Medium => "medium",
                LedgerSeverity.Low => "low",
                _ => "info",
            };
        }

        private static bool LooksLikeCamera(SmartHomeDevice device)
        {
            if (LooksLikeNonCameraLightDevice(device))
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(device.PreviewVideoUrl) ||
                !string.IsNullOrWhiteSpace(device.PreviewImageUrl))
            {
                return true;
            }

            var normalizedName = NormalizeCameraToken(device.Name);
            var normalizedId = NormalizeCameraToken(device.DeviceId);
            var normalizedType = NormalizeCameraToken(device.DeviceType);
            var normalizedSku = NormalizeCameraToken(device.Sku);
            var normalizedExternalUrl = NormalizeCameraToken(device.ExternalUrl);

            var looksLikeCamera = normalizedType.Contains("camera", StringComparison.Ordinal) ||
                normalizedType.Contains("doorbell", StringComparison.Ordinal) ||
                normalizedSku.Contains("camera", StringComparison.Ordinal) ||
                normalizedSku.Contains("doorbell", StringComparison.Ordinal) ||
                normalizedName.Contains("camera", StringComparison.Ordinal) ||
                normalizedName.Contains("doorbell", StringComparison.Ordinal) ||
                normalizedName.Contains("doorcam", StringComparison.Ordinal) ||
                normalizedName.Contains("videodoor", StringComparison.Ordinal) ||
                normalizedId.Contains("camera", StringComparison.Ordinal) ||
                normalizedId.Contains("doorbell", StringComparison.Ordinal);

            if (looksLikeCamera)
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(device.ExternalUrl) &&
                (normalizedExternalUrl.Contains("camera", StringComparison.Ordinal) ||
                 normalizedExternalUrl.Contains("doorbell", StringComparison.Ordinal) ||
                 normalizedExternalUrl.Contains("video", StringComparison.Ordinal) ||
                 normalizedExternalUrl.Contains("stream", StringComparison.Ordinal)))
            {
                return true;
            }

            return device.Capabilities.Any(IsCameraLikeCapability);
        }

        private static bool LooksLikeNonCameraLightDevice(SmartHomeDevice device)
        {
            var normalizedType = NormalizeCameraToken(device.DeviceType);
            var normalizedSku = NormalizeCameraToken(device.Sku);

            var looksLikeLightingHardware = normalizedType.Contains("light", StringComparison.Ordinal) ||
                normalizedType.Contains("bulb", StringComparison.Ordinal) ||
                normalizedType.Contains("lamp", StringComparison.Ordinal) ||
                normalizedType.Contains("strip", StringComparison.Ordinal) ||
                normalizedType.Contains("panel", StringComparison.Ordinal) ||
                normalizedType.Contains("backlight", StringComparison.Ordinal) ||
                normalizedSku.Contains("light", StringComparison.Ordinal) ||
                normalizedSku.Contains("bulb", StringComparison.Ordinal) ||
                normalizedSku.Contains("lamp", StringComparison.Ordinal) ||
                normalizedSku.Contains("strip", StringComparison.Ordinal) ||
                normalizedSku.Contains("panel", StringComparison.Ordinal) ||
                normalizedSku.Contains("backlight", StringComparison.Ordinal);

            if (!looksLikeLightingHardware)
            {
                return false;
            }

            return string.IsNullOrWhiteSpace(device.PreviewVideoUrl) &&
                string.IsNullOrWhiteSpace(device.PreviewImageUrl) &&
                !device.Capabilities.Any(IsCameraLikeCapability);
        }

        private static bool IsCameraLikeCapability(SmartHomeCapability capability)
        {
            var normalizedInstance = NormalizeCameraToken(capability.Instance);
            var normalizedCapabilityType = NormalizeCameraToken(capability.Type);
            var isDynamicSceneSnapshot = normalizedInstance.Contains("snapshot", StringComparison.Ordinal) &&
                (normalizedCapabilityType.Contains("dynamicscene", StringComparison.Ordinal) ||
                 normalizedCapabilityType.Contains("scene", StringComparison.Ordinal));

            if (isDynamicSceneSnapshot)
            {
                return false;
            }

            return normalizedInstance.Contains("camera", StringComparison.Ordinal) ||
                normalizedInstance.Contains("doorbell", StringComparison.Ordinal) ||
                normalizedInstance.Contains("snapshot", StringComparison.Ordinal) ||
                normalizedInstance.Contains("stream", StringComparison.Ordinal) ||
                normalizedCapabilityType.Contains("camera", StringComparison.Ordinal) ||
                normalizedCapabilityType.Contains("doorbell", StringComparison.Ordinal) ||
                normalizedCapabilityType.Contains("snapshot", StringComparison.Ordinal) ||
                normalizedCapabilityType.Contains("stream", StringComparison.Ordinal);
        }

        private static void LogCameraClassificationDiagnostics(IReadOnlyList<SmartHomeProviderState> states)
        {
            try
            {
                foreach (var provider in states)
                {
                    foreach (var device in provider.Devices)
                    {
                        var isCamera = LooksLikeCamera(device);
                        if (!isCamera && !string.Equals(provider.ProviderId, "govee", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        var capabilityTokens = string.Join(", ",
                            device.Capabilities
                                .Select(capability => $"{capability.Type}:{capability.Instance}")
                                .Where(token => !string.IsNullOrWhiteSpace(token))
                                .Take(12));

                        AppLogger.LogInfo(
                            $"[SmartHome][CameraDiag] provider={provider.ProviderId} device='{device.Name}' type='{device.DeviceType}' sku='{device.Sku}' classified={isCamera} previewVideo={!string.IsNullOrWhiteSpace(device.PreviewVideoUrl)} previewImage={!string.IsNullOrWhiteSpace(device.PreviewImageUrl)} external={!string.IsNullOrWhiteSpace(device.ExternalUrl)} capabilities=[{capabilityTokens}]");
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogWarning($"[SmartHome][CameraDiag] Failed to write diagnostics: {ex.Message}");
            }
        }

        private static string NormalizeCameraToken(string? value)
        {
            return (value ?? string.Empty)
                .Trim()
                .Replace("-", string.Empty, StringComparison.Ordinal)
                .Replace("_", string.Empty, StringComparison.Ordinal)
                .Replace(" ", string.Empty, StringComparison.Ordinal)
                .ToLowerInvariant();
        }

        private static JsonElement ParseCommandValue(string valueJson)
        {
            try
            {
                using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(valueJson) ? "null" : valueJson);
                return document.RootElement.Clone();
            }
            catch
            {
                return JsonSerializer.SerializeToElement<string?>(null);
            }
        }

        private static ISmartHomeProvider[] CreateProviders(AtlasSettings settings)
        {
            return new ISmartHomeProvider[]
            {
                new PhilipsHueProvider(settings.SmartHome.PhilipsHue),
                new GoveeProvider(settings.SmartHome.Govee),
                new RingProvider(settings.SmartHome.Ring),
                new LgWebOsProvider(settings.SmartHome.LgWebOs),
                new SmartThingsProvider(settings.SmartHome.SmartThings),
                new HomeAssistantProvider(settings.SmartHome.HomeAssistant),
                new TapoKasaProvider(settings.SmartHome.TapoKasa),
                new OnvifRtspProvider(settings.SmartHome.OnvifRtsp),
            };
        }
    }
}