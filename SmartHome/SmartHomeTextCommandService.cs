using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using AtlasAI.Settings;

namespace AtlasAI.SmartHome
{
    internal sealed class SmartHomeTextCommandService
    {
        private static readonly string[] AllLightsOnPhrases =
        {
            "let there be light",
            "all lights on",
            "turn all lights on",
            "switch all lights on",
            "lights on",
        };

        private static readonly string[] AllLightsOffPhrases =
        {
            "goodnight",
            "good night",
            "all lights off",
            "turn all lights off",
            "switch all lights off",
            "lights off",
        };

        private static readonly string[] SmartHomeKeywords =
        {
            "light", "lamp", "strip", "bulb", "tv", "television", "speaker", "camera", "doorbell",
            "switch", "power", "brightness", "dim", "volume", "mute", "unmute", "hue", "saturation",
            "temperature", "warm", "cool", "input", "hdmi", "netflix", "youtube", "ring", "govee", "hue",
            "goodnight", "good night"
        };

        private static readonly string[] CameraOpenKeywords =
        {
            "show",
            "open",
            "view",
            "watch",
            "display",
            "pull up",
            "bring up",
            "check",
            "see",
        };

        private readonly SmartHomeRuntimeService _runtimeService = new();

        public async Task<SmartHomeTextCommandResult> ExecuteAsync(string commandText, CancellationToken cancellationToken, bool bypassVoiceCommandToggle = false)
        {
            var raw = (commandText ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(raw))
            {
                return SmartHomeTextCommandResult.NotMatched();
            }

            var settings = SettingsStore.Current;
            if (!bypassVoiceCommandToggle && !settings.SmartHome.Agent.VoiceCommandsEnabled)
            {
                return SmartHomeTextCommandResult.NotMatched();
            }

            var customMatch = TryMatchCustomCommand(settings.SmartHome.CustomCommands, raw);
            if (customMatch != null)
            {
                return await ExecuteCustomCommandAsync(customMatch, cancellationToken).ConfigureAwait(false);
            }

            var sceneMatch = TryMatchCustomScene(settings.SmartHome.CustomScenes, raw);
            if (sceneMatch != null)
            {
                var result = await _runtimeService.ExecuteSceneAsync(sceneMatch.Id, cancellationToken).ConfigureAwait(false);
                return new SmartHomeTextCommandResult
                {
                    Matched = true,
                    Ok = result.Ok,
                    Message = result.Ok ? $"Scene '{sceneMatch.Name}' executed." : result.Message,
                };
            }

            var snapshot = await _runtimeService.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
            var sceneResult = await TryExecuteLightSceneAsync(snapshot, raw, cancellationToken).ConfigureAwait(false);
            if (sceneResult != null)
            {
                return sceneResult;
            }

            var cameraResult = TryResolveCameraCommand(snapshot, raw, settings.SmartHome.Agent);
            if (cameraResult != null)
            {
                return cameraResult;
            }

            var resolution = ResolveBuiltInCommand(snapshot, settings.SmartHome.Agent, raw);
            if (resolution == null)
            {
                return SmartHomeTextCommandResult.NotMatched();
            }

            var actionResult = await _runtimeService.ExecuteActionAsync(resolution.Request, cancellationToken).ConfigureAwait(false);
            return new SmartHomeTextCommandResult
            {
                Matched = true,
                Ok = actionResult.Ok,
                Message = actionResult.Ok ? resolution.SuccessMessage : actionResult.Message,
            };
        }

        private async Task<SmartHomeTextCommandResult?> TryExecuteLightSceneAsync(SmartHomeSnapshot snapshot, string input, CancellationToken cancellationToken)
        {
            var normalized = Normalize(input);
            var shouldTurnOn = AllLightsOnPhrases.Any(phrase => normalized.Contains(Normalize(phrase), StringComparison.Ordinal));
            var shouldTurnOff = !shouldTurnOn && AllLightsOffPhrases.Any(phrase => normalized.Contains(Normalize(phrase), StringComparison.Ordinal));
            if (!shouldTurnOn && !shouldTurnOff)
            {
                return null;
            }

            var targetValue = shouldTurnOn;
            var lightRequests = snapshot.Providers
                .SelectMany(provider => provider.Devices.Select(device => new FlatDevice(provider, device)))
                .Where(IsLightDevice)
                .Select(device => new
                {
                    Device = device,
                    Capability = device.FindCapability("powerSwitch"),
                })
                .Where(entry => entry.Capability != null)
                .GroupBy(entry => $"{entry.Device.Provider.ProviderId}:{entry.Device.Device.DeviceId}", StringComparer.OrdinalIgnoreCase)
                .Select(entry => entry.First())
                .ToArray();

            if (lightRequests.Length == 0)
            {
                return new SmartHomeTextCommandResult
                {
                    Matched = true,
                    Ok = false,
                    Message = "Atlas could not find any controllable lights for that command.",
                };
            }

            var results = new List<SmartHomeActionResult>(lightRequests.Length);
            foreach (var entry in lightRequests)
            {
                cancellationToken.ThrowIfCancellationRequested();
                results.Add(await _runtimeService.ExecuteActionAsync(new SmartHomeActionRequest
                {
                    ProviderId = entry.Device.Provider.ProviderId,
                    DeviceId = entry.Device.Device.DeviceId,
                    Sku = entry.Device.Device.Sku,
                    CapabilityType = entry.Capability!.Type,
                    CapabilityInstance = entry.Capability.Instance,
                    Value = JsonSerializer.SerializeToElement(targetValue),
                }, cancellationToken).ConfigureAwait(false));
            }

            var successCount = results.Count(result => result.Ok);
            if (successCount == 0)
            {
                return new SmartHomeTextCommandResult
                {
                    Matched = true,
                    Ok = false,
                    Message = results.FirstOrDefault(result => !string.IsNullOrWhiteSpace(result.Message))?.Message ?? "Atlas could not update any lights.",
                };
            }

            var actionText = targetValue ? "on" : "off";
            var message = successCount == lightRequests.Length
                ? $"Turned {successCount} light{(successCount == 1 ? string.Empty : "s")} {actionText}."
                : $"Turned {successCount} of {lightRequests.Length} lights {actionText}.";

            return new SmartHomeTextCommandResult
            {
                Matched = true,
                Ok = successCount == lightRequests.Length,
                Message = message,
            };
        }

        internal static bool LooksLikeSmartHomeCommand(string commandText)
        {
            var normalized = Normalize(commandText);
            if (string.IsNullOrWhiteSpace(normalized))
                return false;

            return SmartHomeKeywords.Any(keyword => normalized.Contains(keyword, StringComparison.Ordinal)) ||
                   normalized.Contains("turn on", StringComparison.Ordinal) ||
                   normalized.Contains("turn off", StringComparison.Ordinal) ||
                   normalized.Contains("switch on", StringComparison.Ordinal) ||
                   normalized.Contains("switch off", StringComparison.Ordinal) ||
                   normalized.Contains("set ", StringComparison.Ordinal);
        }

        internal static SmartHomeCustomCommandSetting? TryMatchCustomCommand(IEnumerable<SmartHomeCustomCommandSetting> commands, string input)
        {
            var normalizedInput = Normalize(input);
            return commands
                .Where(static command => command.Enabled && !string.IsNullOrWhiteSpace(command.Phrase))
                .Select(command => new
                {
                    Command = command,
                    Phrase = Normalize(command.Phrase),
                })
                .Where(item => normalizedInput.Equals(item.Phrase, StringComparison.Ordinal))
                .OrderByDescending(item => item.Phrase.Length)
                .Select(item => item.Command)
                .FirstOrDefault();
        }

        internal static SmartHomeGreetingSetting? TryMatchCustomGreeting(IEnumerable<SmartHomeGreetingSetting> greetings, string input)
        {
            var normalizedInput = Normalize(input);
            return greetings
                .Where(static greeting => greeting.Enabled && !string.IsNullOrWhiteSpace(greeting.Phrase))
                .Select(greeting => new
                {
                    Greeting = greeting,
                    Phrase = Normalize(greeting.Phrase),
                })
                .Where(item => normalizedInput.Equals(item.Phrase, StringComparison.Ordinal))
                .OrderByDescending(item => item.Phrase.Length)
                .Select(item => item.Greeting)
                .FirstOrDefault();
        }

        internal static SmartHomeSceneSetting? TryMatchCustomScene(IEnumerable<SmartHomeSceneSetting> scenes, string input)
        {
            var normalizedInput = Normalize(input);
            return scenes
                .Where(static scene => scene.Enabled && (!string.IsNullOrWhiteSpace(scene.Name) || !string.IsNullOrWhiteSpace(scene.Phrase)))
                .Select(scene => new
                {
                    Scene = scene,
                    Name = Normalize(scene.Name),
                    Phrase = Normalize(scene.Phrase),
                })
                .Where(item =>
                    (!string.IsNullOrWhiteSpace(item.Phrase) &&
                     normalizedInput.Equals(item.Phrase, StringComparison.Ordinal)) ||
                    (!string.IsNullOrWhiteSpace(item.Name) &&
                     normalizedInput.Equals(item.Name, StringComparison.Ordinal)))
                .OrderByDescending(item => Math.Max(item.Name.Length, item.Phrase.Length))
                .Select(item => item.Scene)
                .FirstOrDefault();
        }

        internal static SmartHomeBuiltInCommandResolution? ResolveBuiltInCommand(SmartHomeSnapshot snapshot, SmartHomeAgentSettings agentSettings, string input)
        {
            var devices = snapshot.Providers
                .SelectMany(provider => provider.Devices.Select(device => new FlatDevice(provider, device)))
                .ToArray();

            if (devices.Length == 0)
                return null;

            var normalized = Normalize(input);
            var parsedNumber = TryExtractNumber(normalized);
            var matchedDevice = FindBestDevice(devices, normalized);
            var volumeDevice = FindBestDeviceForCapability(devices, normalized, matchedDevice, CapabilityPreference.Volume);
            var brightnessDevice = FindBestDeviceForCapability(devices, normalized, matchedDevice, CapabilityPreference.Brightness);
            var temperatureDevice = FindBestDeviceForCapability(devices, normalized, matchedDevice, CapabilityPreference.ColorTemperature);
            var hueDevice = FindBestDeviceForCapability(devices, normalized, matchedDevice, CapabilityPreference.ColorHue);
            var saturationDevice = FindBestDeviceForCapability(devices, normalized, matchedDevice, CapabilityPreference.ColorSaturation);
            var powerDevice = FindBestDeviceForCapability(devices, normalized, matchedDevice, CapabilityPreference.Power);
            var muteDevice = FindBestDeviceForCapability(devices, normalized, matchedDevice, CapabilityPreference.Mute);

            var actionDevice = matchedDevice ?? volumeDevice ?? brightnessDevice ?? temperatureDevice ?? hueDevice ?? saturationDevice ?? powerDevice ?? muteDevice;
            if (actionDevice == null)
                return null;

            var powerCapability = powerDevice?.FindCapability("powerSwitch");
            if (powerCapability != null)
            {
                if ((normalized.Contains("turn on", StringComparison.Ordinal) || normalized.Contains("switch on", StringComparison.Ordinal) || Regex.IsMatch(normalized, @"\bon\b")) && !normalized.Contains("turn off", StringComparison.Ordinal))
                {
                    return CreateResolution(powerDevice!, powerCapability, true, $"Turning {powerDevice!.Device.Name} on.");
                }

                if (normalized.Contains("turn off", StringComparison.Ordinal) || normalized.Contains("switch off", StringComparison.Ordinal) || Regex.IsMatch(normalized, @"\boff\b"))
                {
                    return CreateResolution(powerDevice!, powerCapability, false, $"Turning {powerDevice!.Device.Name} off.");
                }
            }

            var volumeCapability = volumeDevice?.FindCapability("volume");
            if (volumeCapability != null && volumeDevice != null && (normalized.Contains("volume", StringComparison.Ordinal) || normalized.Contains("louder", StringComparison.Ordinal) || normalized.Contains("quieter", StringComparison.Ordinal)))
            {
                var currentVolume = ReadInt(volumeCapability.StateValue) ?? 0;
                var step = Math.Clamp(agentSettings.DefaultVolumeStep, 1, 25);

                if (parsedNumber.HasValue)
                {
                    var target = Math.Clamp(parsedNumber.Value, volumeCapability.Min ?? 0, volumeCapability.Max ?? 100);
                    return CreateResolution(volumeDevice, volumeCapability, target, $"Setting {volumeDevice.Device.Name} volume to {target}%.");
                }

                if (normalized.Contains("up", StringComparison.Ordinal) || normalized.Contains("louder", StringComparison.Ordinal) || normalized.Contains("increase", StringComparison.Ordinal))
                {
                    var target = Math.Clamp(currentVolume + step, volumeCapability.Min ?? 0, volumeCapability.Max ?? 100);
                    return CreateResolution(volumeDevice, volumeCapability, target, $"Increasing {volumeDevice.Device.Name} volume to {target}%.");
                }

                if (normalized.Contains("down", StringComparison.Ordinal) || normalized.Contains("quieter", StringComparison.Ordinal) || normalized.Contains("decrease", StringComparison.Ordinal) || normalized.Contains("lower", StringComparison.Ordinal))
                {
                    var target = Math.Clamp(currentVolume - step, volumeCapability.Min ?? 0, volumeCapability.Max ?? 100);
                    return CreateResolution(volumeDevice, volumeCapability, target, $"Lowering {volumeDevice.Device.Name} volume to {target}%.");
                }
            }

            var muteCapability = muteDevice?.FindCapability("mute");
            if (muteCapability != null && muteDevice != null)
            {
                if (normalized.Contains("unmute", StringComparison.Ordinal))
                    return CreateResolution(muteDevice, muteCapability, false, $"Unmuting {muteDevice.Device.Name}.");
                if (normalized.Contains("mute", StringComparison.Ordinal))
                    return CreateResolution(muteDevice, muteCapability, true, $"Muting {muteDevice.Device.Name}.");
            }

            if (brightnessDevice != null && TryResolveRangeCommand(brightnessDevice, normalized, parsedNumber, "brightness", new[] { "brightness", "dim", "brighter" }, out var brightnessResolution))
                return brightnessResolution;

            if (temperatureDevice != null && TryResolveRangeCommand(temperatureDevice, normalized, parsedNumber, "colorTemperature", new[] { "temperature", "warm", "cool" }, out var temperatureResolution))
                return temperatureResolution;

            if (hueDevice != null && TryResolveRangeCommand(hueDevice, normalized, parsedNumber, "colorHue", new[] { "hue", "color" }, out var hueResolution))
                return hueResolution;

            if (saturationDevice != null && TryResolveRangeCommand(saturationDevice, normalized, parsedNumber, "colorSaturation", new[] { "saturation" }, out var saturationResolution))
                return saturationResolution;

            var optionResolution = TryResolveOptionCommand(actionDevice, normalized);
            if (optionResolution != null)
                return optionResolution;

            return null;
        }

        private async Task<SmartHomeTextCommandResult> ExecuteCustomCommandAsync(SmartHomeCustomCommandSetting command, CancellationToken cancellationToken)
        {
            if (string.Equals(command.TargetKind, "atlas-intent", StringComparison.OrdinalIgnoreCase))
            {
                return await ExecuteAtlasIntentCommandAsync(command, cancellationToken).ConfigureAwait(false);
            }

            if (string.Equals(command.TargetKind, "scene", StringComparison.OrdinalIgnoreCase))
            {
                var sceneId = !string.IsNullOrWhiteSpace(command.DeviceId)
                    ? command.DeviceId
                    : command.TargetScope.StartsWith("scene:", StringComparison.OrdinalIgnoreCase)
                        ? command.TargetScope["scene:".Length..]
                        : string.Empty;

                if (string.IsNullOrWhiteSpace(sceneId))
                {
                    return new SmartHomeTextCommandResult
                    {
                        Matched = true,
                        Ok = false,
                        Message = "Atlas could not find the scene bound to that command.",
                    };
                }

                var result = await _runtimeService.ExecuteSceneAsync(sceneId, cancellationToken).ConfigureAwait(false);
                return new SmartHomeTextCommandResult
                {
                    Matched = true,
                    Ok = result.Ok,
                    Message = result.Ok
                        ? (string.IsNullOrWhiteSpace(command.ResponseText) ? $"Executed scene command '{command.Phrase}'." : command.ResponseText)
                        : result.Message,
                };
            }

            if (!UsesGroupTarget(command))
            {
                var request = BuildRequest(command);
                var result = await _runtimeService.ExecuteActionAsync(request, cancellationToken).ConfigureAwait(false);
                return new SmartHomeTextCommandResult
                {
                    Matched = true,
                    Ok = result.Ok,
                    Message = result.Ok
                        ? (string.IsNullOrWhiteSpace(command.ResponseText) ? $"Executed custom command '{command.Phrase}'." : command.ResponseText)
                        : result.Message,
                };
            }

            var snapshot = await _runtimeService.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
            var requests = BuildRequests(command, snapshot).ToArray();
            if (requests.Length == 0)
            {
                return new SmartHomeTextCommandResult
                {
                    Matched = true,
                    Ok = false,
                    Message = string.IsNullOrWhiteSpace(command.TargetLabel)
                        ? "Atlas could not find any matching devices for that grouped command."
                        : $"Atlas could not find any matching devices in {command.TargetLabel}.",
                };
            }

            var results = new List<SmartHomeActionResult>(requests.Length);
            foreach (var request in requests)
            {
                cancellationToken.ThrowIfCancellationRequested();
                results.Add(await _runtimeService.ExecuteActionAsync(request, cancellationToken).ConfigureAwait(false));
            }

            var successCount = results.Count(static result => result.Ok);
            if (successCount == 0)
            {
                return new SmartHomeTextCommandResult
                {
                    Matched = true,
                    Ok = false,
                    Message = results.FirstOrDefault(result => !string.IsNullOrWhiteSpace(result.Message))?.Message
                        ?? "Atlas could not execute that grouped command.",
                };
            }

            var defaultMessage = successCount == requests.Length
                ? $"Executed custom command '{command.Phrase}' for {successCount} device{(successCount == 1 ? string.Empty : "s")}."
                : $"Executed custom command '{command.Phrase}' for {successCount} of {requests.Length} devices.";

            return new SmartHomeTextCommandResult
            {
                Matched = true,
                Ok = successCount == requests.Length,
                Message = string.IsNullOrWhiteSpace(command.ResponseText) ? defaultMessage : command.ResponseText,
            };
        }

        private async Task<SmartHomeTextCommandResult> ExecuteAtlasIntentCommandAsync(SmartHomeCustomCommandSetting command, CancellationToken cancellationToken)
        {
            if (!string.Equals(command.TargetScope, "door-answer", StringComparison.OrdinalIgnoreCase))
            {
                return new SmartHomeTextCommandResult
                {
                    Matched = true,
                    Ok = false,
                    Message = "Atlas could not resolve that built-in Smart Home command intent.",
                };
            }

            var settings = SettingsStore.Current;
            var snapshot = await _runtimeService.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
            var resolved = TryResolveCameraCommand(snapshot, "answer the door", settings.SmartHome.Agent);
            if (resolved == null)
            {
                return new SmartHomeTextCommandResult
                {
                    Matched = true,
                    Ok = false,
                    Message = "Atlas could not find a doorbell camera to answer right now.",
                };
            }

            if (!resolved.Ok || string.IsNullOrWhiteSpace(command.ResponseText))
                return resolved;

            return new SmartHomeTextCommandResult
            {
                Matched = resolved.Matched,
                Ok = resolved.Ok,
                Message = command.ResponseText,
                RequestedPage = resolved.RequestedPage,
                OpenManagedRingLiveView = resolved.OpenManagedRingLiveView,
                CameraProviderId = resolved.CameraProviderId,
                CameraDeviceId = resolved.CameraDeviceId,
                CameraName = resolved.CameraName,
                CameraExternalUrl = resolved.CameraExternalUrl,
            };
        }

        private static SmartHomeActionRequest BuildRequest(SmartHomeCustomCommandSetting command)
        {
            var value = ParseCommandValue(command.ValueJson);
            return new SmartHomeActionRequest
            {
                ProviderId = command.ProviderId,
                DeviceId = command.DeviceId,
                Sku = command.Sku,
                CapabilityType = command.CapabilityType,
                CapabilityInstance = command.CapabilityInstance,
                Value = value,
            };
        }

        private static IReadOnlyList<SmartHomeActionRequest> BuildRequests(SmartHomeCustomCommandSetting command, SmartHomeSnapshot snapshot)
        {
            if (!UsesGroupTarget(command))
                return new[] { BuildRequest(command) };

            var devices = snapshot.Providers
                .SelectMany(provider => provider.Devices.Select(device => new FlatDevice(provider, device)))
                .ToArray();

            IEnumerable<FlatDevice> targetDevices = Array.Empty<FlatDevice>();
            if (string.Equals(command.TargetScope, "all-lights", StringComparison.OrdinalIgnoreCase))
            {
                targetDevices = devices.Where(IsLightDevice);
            }
            else if (command.TargetScope.StartsWith("provider-lights:", StringComparison.OrdinalIgnoreCase))
            {
                var providerId = command.TargetScope["provider-lights:".Length..];
                targetDevices = devices.Where(device =>
                    string.Equals(device.Provider.ProviderId, providerId, StringComparison.OrdinalIgnoreCase) &&
                    IsLightDevice(device));
            }

            var value = ParseCommandValue(command.ValueJson);
            return targetDevices
                .Select(device => new { Device = device, Capability = device.Device.Capabilities.FirstOrDefault(capability =>
                    string.Equals(capability.Type, command.CapabilityType, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(capability.Instance, command.CapabilityInstance, StringComparison.OrdinalIgnoreCase)) })
                .Where(entry => entry.Capability != null)
                .Select(entry => new SmartHomeActionRequest
                {
                    ProviderId = entry.Device.Provider.ProviderId,
                    DeviceId = entry.Device.Device.DeviceId,
                    Sku = entry.Device.Device.Sku,
                    CapabilityType = entry.Capability!.Type,
                    CapabilityInstance = entry.Capability.Instance,
                    Value = value,
                })
                .ToArray();
        }

        private static JsonElement ParseCommandValue(string? json)
        {
            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "null" : json);
            return document.RootElement.Clone();
        }

        private static bool UsesGroupTarget(SmartHomeCustomCommandSetting command)
        {
            return string.Equals(command.TargetKind, "group", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(command.TargetScope);
        }

        private static SmartHomeBuiltInCommandResolution? TryResolveOptionCommand(FlatDevice device, string normalized)
        {
            var optionMatch = device.Device.Capabilities
                .SelectMany(capability => capability.Options.Select(option => new { Capability = capability, Option = option, Name = Normalize(option.Name) }))
                .Where(item => !string.IsNullOrWhiteSpace(item.Name) && normalized.Contains(item.Name, StringComparison.Ordinal))
                .OrderByDescending(item => item.Name.Length)
                .FirstOrDefault();

            if (optionMatch == null)
                return null;

            return CreateResolution(device, optionMatch.Capability, optionMatch.Option.Value, $"Applying {optionMatch.Option.Name} on {device.Device.Name}.");
        }

        private static bool TryResolveRangeCommand(FlatDevice device, string normalized, int? parsedNumber, string instanceNeedle, IReadOnlyList<string> keywords, out SmartHomeBuiltInCommandResolution? resolution)
        {
            resolution = null;
            if (!keywords.Any(keyword => normalized.Contains(keyword, StringComparison.Ordinal)))
                return false;

            var capability = device.Device.Capabilities.FirstOrDefault(item => item.Instance.Contains(instanceNeedle, StringComparison.OrdinalIgnoreCase));
            if (capability == null)
                return false;

            var min = capability.Min ?? 0;
            var max = capability.Max ?? 100;
            var currentValue = ReadInt(capability.StateValue) ?? min;
            var target = parsedNumber.HasValue
                ? Math.Clamp(parsedNumber.Value, min, max)
                : ResolveRelativeRangeTarget(normalized, currentValue, min, max, instanceNeedle);

            resolution = CreateResolution(device, capability, target, $"Setting {device.Device.Name} {capability.Instance} to {target}{capability.Unit}.");
            return true;
        }

        private static int ResolveRelativeRangeTarget(string normalized, int currentValue, int min, int max, string instanceNeedle)
        {
            var range = Math.Max(1, max - min);
            var defaultStep = Math.Max(1, range / (instanceNeedle.Contains("brightness", StringComparison.OrdinalIgnoreCase) ? 5 : 8));

            if (normalized.Contains("up", StringComparison.Ordinal) || normalized.Contains("increase", StringComparison.Ordinal) || normalized.Contains("higher", StringComparison.Ordinal) || normalized.Contains("brighter", StringComparison.Ordinal) || normalized.Contains("warmer", StringComparison.Ordinal))
                return Math.Clamp(currentValue + defaultStep, min, max);

            if (normalized.Contains("down", StringComparison.Ordinal) || normalized.Contains("decrease", StringComparison.Ordinal) || normalized.Contains("lower", StringComparison.Ordinal) || normalized.Contains("dimmer", StringComparison.Ordinal) || normalized.Contains("cooler", StringComparison.Ordinal))
                return Math.Clamp(currentValue - defaultStep, min, max);

            return Math.Clamp(currentValue, min, max);
        }

        private static FlatDevice? FindBestDeviceForCapability(IEnumerable<FlatDevice> devices, string normalizedInput, FlatDevice? preferredDevice, CapabilityPreference preference)
        {
            if (preferredDevice != null && DeviceSupports(preferredDevice, preference))
                return preferredDevice;

            var capableDevices = devices.Where(device => DeviceSupports(device, preference)).ToArray();
            if (capableDevices.Length == 0)
                return null;

            if (capableDevices.Length == 1)
                return capableDevices[0];

            return capableDevices
                .Select(device => new { Device = device, Score = ScoreDevice(device, normalizedInput) + ScoreCapabilityAffinity(device, normalizedInput, preference) })
                .OrderByDescending(entry => entry.Score)
                .Select(entry => entry.Device)
                .FirstOrDefault();
        }

        private static bool DeviceSupports(FlatDevice device, CapabilityPreference preference)
        {
            return preference switch
            {
                CapabilityPreference.Power => device.FindCapability("powerSwitch") != null,
                CapabilityPreference.Volume => device.FindCapability("volume") != null,
                CapabilityPreference.Mute => device.FindCapability("mute") != null,
                CapabilityPreference.Brightness => device.Device.Capabilities.Any(item => item.Instance.Contains("brightness", StringComparison.OrdinalIgnoreCase)),
                CapabilityPreference.ColorTemperature => device.Device.Capabilities.Any(item => item.Instance.Contains("colorTemperature", StringComparison.OrdinalIgnoreCase)),
                CapabilityPreference.ColorHue => device.Device.Capabilities.Any(item => item.Instance.Contains("colorHue", StringComparison.OrdinalIgnoreCase)),
                CapabilityPreference.ColorSaturation => device.Device.Capabilities.Any(item => item.Instance.Contains("colorSaturation", StringComparison.OrdinalIgnoreCase)),
                _ => false,
            };
        }

        private static int ScoreCapabilityAffinity(FlatDevice device, string normalizedInput, CapabilityPreference preference)
        {
            var score = 0;
            var deviceType = Normalize(device.Device.DeviceType);
            var providerName = Normalize(device.Provider.DisplayName);

            switch (preference)
            {
                case CapabilityPreference.Volume:
                case CapabilityPreference.Mute:
                    if (deviceType.Contains("tv", StringComparison.Ordinal) || deviceType.Contains("speaker", StringComparison.Ordinal) || providerName.Contains("webos", StringComparison.Ordinal))
                        score += 30;
                    break;
                case CapabilityPreference.Brightness:
                case CapabilityPreference.ColorTemperature:
                case CapabilityPreference.ColorHue:
                case CapabilityPreference.ColorSaturation:
                    if (IsLightDevice(device))
                        score += 30;
                    else if (IsMediaDevice(device))
                        score -= 20;
                    break;
            }

            return score;
        }

        private static SmartHomeBuiltInCommandResolution CreateResolution(FlatDevice device, SmartHomeCapability capability, object value, string successMessage)
        {
            return new SmartHomeBuiltInCommandResolution
            {
                Request = new SmartHomeActionRequest
                {
                    ProviderId = device.Provider.ProviderId,
                    DeviceId = device.Device.DeviceId,
                    Sku = device.Device.Sku,
                    CapabilityType = capability.Type,
                    CapabilityInstance = capability.Instance,
                    Value = JsonSerializer.SerializeToElement(value),
                },
                SuccessMessage = successMessage,
            };
        }

        internal static SmartHomeTextCommandResult? TryResolveCameraCommand(SmartHomeSnapshot snapshot, string input, SmartHomeAgentSettings? agentSettings = null)
        {
            var normalized = Normalize(input);
            if (string.IsNullOrWhiteSpace(normalized))
                return null;

            var requestsCameraView = CameraOpenKeywords.Any(keyword => normalized.Contains(Normalize(keyword), StringComparison.Ordinal));
            var requestsDoorbellAnswer =
                (normalized.Contains("answer", StringComparison.Ordinal) || normalized.Contains("pick up", StringComparison.Ordinal)) &&
                (normalized.Contains("doorbell", StringComparison.Ordinal) ||
                 normalized.Contains("front door", StringComparison.Ordinal) ||
                 normalized.Contains("answer the door", StringComparison.Ordinal));
            var mentionsCamera = normalized.Contains("camera", StringComparison.Ordinal) ||
                                 normalized.Contains("doorbell", StringComparison.Ordinal) ||
                                 normalized.Contains("front door", StringComparison.Ordinal) ||
                                 requestsDoorbellAnswer ||
                                 normalized.Contains("ring", StringComparison.Ordinal);

            if (!requestsCameraView && !mentionsCamera)
                return null;

            if (requestsDoorbellAnswer && agentSettings is { AnswerDoorEnabled: false })
            {
                return new SmartHomeTextCommandResult
                {
                    Matched = true,
                    Ok = false,
                    Message = "Answer-the-door is turned off in Smart Home settings.",
                };
            }

            var cameras = snapshot.Providers
                .SelectMany(provider => provider.Devices.Select(device => new FlatDevice(provider, device)))
                .Where(IsCameraDevice)
                .ToArray();

            if (cameras.Length == 0)
            {
                return new SmartHomeTextCommandResult
                {
                    Matched = true,
                    Ok = false,
                    Message = "Atlas could not find any live cameras for that request.",
                };
            }

            var targetCandidates = requestsDoorbellAnswer
                ? cameras.Where(IsDoorbellDevice).ToArray()
                : cameras;

            if (targetCandidates.Length == 0 && requestsDoorbellAnswer)
            {
                return new SmartHomeTextCommandResult
                {
                    Matched = true,
                    Ok = false,
                    Message = "Atlas could not find a doorbell camera to answer right now.",
                };
            }

            var targetCamera = FindBestDevice(targetCandidates, normalized)
                ?? (targetCandidates.Length == 1 ? targetCandidates[0] : null);

            var autoSelected = false;
            if (targetCamera == null && requestsCameraView)
            {
                var fallbackPool = targetCandidates
                    .OrderByDescending(device => device.Device.IsOnline)
                    .ThenByDescending(device => !string.IsNullOrWhiteSpace(device.Device.PreviewVideoUrl))
                    .ThenByDescending(device => string.Equals(device.Provider.ProviderId, "ring", StringComparison.OrdinalIgnoreCase))
                    .ToArray();

                if (fallbackPool.Length > 0)
                {
                    targetCamera = fallbackPool[0];
                    autoSelected = true;
                }
            }

            if (targetCamera == null)
            {
                return new SmartHomeTextCommandResult
                {
                    Matched = true,
                    Ok = false,
                    Message = "Atlas could not work out which camera you wanted to open. Try saying the camera name, for example 'open front door camera'.",
                };
            }

            var responseMessage = requestsDoorbellAnswer
                ? $"Opening {targetCamera.Device.Name} so you can answer the doorbell."
                : autoSelected
                    ? $"Opening {targetCamera.Device.Name}."
                    : $"Opening {targetCamera.Device.Name}.";

            return new SmartHomeTextCommandResult
            {
                Matched = true,
                Ok = true,
                Message = responseMessage,
                RequestedPage = "smarthome",
                OpenManagedRingLiveView = string.Equals(targetCamera.Provider.ProviderId, "ring", StringComparison.OrdinalIgnoreCase),
                CameraProviderId = targetCamera.Provider.ProviderId,
                CameraDeviceId = targetCamera.Device.DeviceId,
                CameraName = targetCamera.Device.Name,
                CameraExternalUrl = targetCamera.Device.ExternalUrl,
            };
        }

        private static FlatDevice? FindBestDevice(IEnumerable<FlatDevice> devices, string input)
        {
            var normalizedInput = Normalize(input);
            return devices
                .Select(device => new { Device = device, Score = ScoreDevice(device, normalizedInput) })
                .Where(item => item.Score > 0)
                .OrderByDescending(item => item.Score)
                .Select(item => item.Device)
                .FirstOrDefault();
        }

        private static bool IsLightDevice(FlatDevice device)
        {
            if (IsMediaDevice(device))
                return false;

            var providerId = Normalize(device.Provider.ProviderId);
            var providerName = Normalize(device.Provider.DisplayName);
            var deviceName = Normalize(device.Device.Name);
            var deviceType = Normalize(device.Device.DeviceType);

            var hasLightCapability = device.Device.Capabilities.Any(item =>
                item.Instance.Contains("brightness", StringComparison.OrdinalIgnoreCase) ||
                item.Instance.Contains("colorTemperature", StringComparison.OrdinalIgnoreCase) ||
                item.Instance.Contains("colorHue", StringComparison.OrdinalIgnoreCase) ||
                item.Instance.Contains("colorSaturation", StringComparison.OrdinalIgnoreCase) ||
                item.Instance.Contains("lightScene", StringComparison.OrdinalIgnoreCase) ||
                item.Instance.Contains("diyScene", StringComparison.OrdinalIgnoreCase) ||
                item.Instance.Contains("musicMode", StringComparison.OrdinalIgnoreCase));

            if (!hasLightCapability &&
                !deviceName.Contains("light", StringComparison.Ordinal) &&
                !deviceName.Contains("lamp", StringComparison.Ordinal) &&
                !deviceName.Contains("bulb", StringComparison.Ordinal) &&
                !deviceName.Contains("strip", StringComparison.Ordinal) &&
                !deviceName.Contains("neon", StringComparison.Ordinal) &&
                !deviceType.Contains("light", StringComparison.Ordinal) &&
                !deviceType.Contains("lamp", StringComparison.Ordinal) &&
                !deviceType.Contains("bulb", StringComparison.Ordinal) &&
                !deviceType.Contains("strip", StringComparison.Ordinal))
            {
                return false;
            }

            if (providerId is "philips hue" or "philips_hue" or "govee")
                return true;

            if (providerName.Contains("hue", StringComparison.Ordinal) || providerName.Contains("govee", StringComparison.Ordinal))
                return true;

            return deviceName.Contains("light", StringComparison.Ordinal) ||
                   deviceName.Contains("lamp", StringComparison.Ordinal) ||
                   deviceName.Contains("bulb", StringComparison.Ordinal) ||
                   deviceName.Contains("strip", StringComparison.Ordinal) ||
                   deviceName.Contains("neon", StringComparison.Ordinal) ||
                   deviceType.Contains("light", StringComparison.Ordinal) ||
                   deviceType.Contains("lamp", StringComparison.Ordinal) ||
                   deviceType.Contains("bulb", StringComparison.Ordinal) ||
                   deviceType.Contains("strip", StringComparison.Ordinal);
        }

        private static bool IsMediaDevice(FlatDevice device)
        {
            var providerId = Normalize(device.Provider.ProviderId);
            var providerName = Normalize(device.Provider.DisplayName);
            var deviceName = Normalize(device.Device.Name);
            var deviceType = Normalize(device.Device.DeviceType);

            return providerId.Contains("webos", StringComparison.Ordinal) ||
                   providerName.Contains("webos", StringComparison.Ordinal) ||
                   deviceName.Contains("tv", StringComparison.Ordinal) ||
                   deviceName.Contains("speaker", StringComparison.Ordinal) ||
                   deviceName.Contains("media", StringComparison.Ordinal) ||
                   deviceType.Contains("tv", StringComparison.Ordinal) ||
                   deviceType.Contains("speaker", StringComparison.Ordinal) ||
                   deviceType.Contains("media", StringComparison.Ordinal);
        }

        private static bool IsCameraDevice(FlatDevice device)
        {
            var providerId = Normalize(device.Provider.ProviderId);
            var providerName = Normalize(device.Provider.DisplayName);
            var deviceName = Normalize(device.Device.Name);
            var deviceType = Normalize(device.Device.DeviceType);

            return providerId.Contains("ring", StringComparison.Ordinal) ||
                   providerName.Contains("ring", StringComparison.Ordinal) ||
                   deviceName.Contains("camera", StringComparison.Ordinal) ||
                   deviceName.Contains("doorbell", StringComparison.Ordinal) ||
                   deviceName.Contains("front door", StringComparison.Ordinal) ||
                   deviceType.Contains("camera", StringComparison.Ordinal) ||
                   deviceType.Contains("doorbell", StringComparison.Ordinal) ||
                   !string.IsNullOrWhiteSpace(device.Device.PreviewVideoUrl) ||
                   !string.IsNullOrWhiteSpace(device.Device.PreviewImageUrl) ||
                   !string.IsNullOrWhiteSpace(device.Device.ExternalUrl);
        }

        private static bool IsDoorbellDevice(FlatDevice device)
        {
            var deviceName = Normalize(device.Device.Name);
            var deviceType = Normalize(device.Device.DeviceType);
            var sku = Normalize(device.Device.Sku);
            return deviceName.Contains("doorbell", StringComparison.Ordinal) ||
                   deviceName.Contains("front door", StringComparison.Ordinal) ||
                   deviceType.Contains("doorbell", StringComparison.Ordinal) ||
                   sku.Contains("doorbell", StringComparison.Ordinal);
        }

        private static int ScoreDevice(FlatDevice device, string normalizedInput)
        {
            var deviceName = Normalize(device.Device.Name);
            var deviceType = Normalize(device.Device.DeviceType);
            var providerName = Normalize(device.Provider.DisplayName);

            var score = 0;
            if (normalizedInput.Contains(deviceName, StringComparison.Ordinal))
                score += 100 + deviceName.Length;
            if (!string.IsNullOrWhiteSpace(deviceType) && normalizedInput.Contains(deviceType, StringComparison.Ordinal))
                score += 24;
            if (!string.IsNullOrWhiteSpace(providerName) && normalizedInput.Contains(providerName, StringComparison.Ordinal))
                score += 12;

            foreach (var token in deviceName.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (token.Length > 2 && normalizedInput.Contains(token, StringComparison.Ordinal))
                    score += 10;
            }

            if (deviceType.Contains("tv", StringComparison.OrdinalIgnoreCase) && (normalizedInput.Contains("tv", StringComparison.Ordinal) || normalizedInput.Contains("television", StringComparison.Ordinal)))
                score += 20;

            return score;
        }

        private static int? TryExtractNumber(string normalized)
        {
            var match = Regex.Match(normalized, "-?\\d+");
            if (!match.Success)
                return null;

            return int.TryParse(match.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;
        }

        private static int? ReadInt(JsonElement value)
        {
            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var numericValue))
                return numericValue;

            if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out numericValue))
                return numericValue;

            return null;
        }

        private static string Normalize(string value)
        {
            return Regex.Replace((value ?? string.Empty).ToLowerInvariant(), "[^a-z0-9]+", " ").Trim();
        }

        private sealed record FlatDevice(SmartHomeProviderState Provider, SmartHomeDevice Device)
        {
            public SmartHomeCapability? FindCapability(string instance)
            {
                return Device.Capabilities.FirstOrDefault(item => string.Equals(item.Instance, instance, StringComparison.OrdinalIgnoreCase));
            }
        }

        private enum CapabilityPreference
        {
            Power,
            Volume,
            Mute,
            Brightness,
            ColorTemperature,
            ColorHue,
            ColorSaturation,
        }
    }

    internal sealed class SmartHomeTextCommandResult
    {
        public bool Matched { get; init; }
        public bool Ok { get; init; }
        public string Message { get; init; } = string.Empty;
        public string RequestedPage { get; init; } = string.Empty;
        public bool OpenManagedRingLiveView { get; init; }
        public string CameraProviderId { get; init; } = string.Empty;
        public string CameraDeviceId { get; init; } = string.Empty;
        public string CameraName { get; init; } = string.Empty;
        public string CameraExternalUrl { get; init; } = string.Empty;

        public static SmartHomeTextCommandResult NotMatched() => new() { Matched = false, Ok = false };
    }

    internal sealed class SmartHomeBuiltInCommandResolution
    {
        public required SmartHomeActionRequest Request { get; init; }
        public required string SuccessMessage { get; init; }
    }
}