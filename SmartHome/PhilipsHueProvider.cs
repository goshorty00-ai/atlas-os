using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using AtlasAI.Settings;
using System.Threading;
using System.Threading.Tasks;

namespace AtlasAI.SmartHome
{
    internal sealed class PhilipsHueProvider : ISmartHomeProvider
    {
        private const int HueApiBrightnessMax = 254;
        private const int HueApiSaturationMax = 254;
        private const int HueApiHueMax = 65535;
        private const int HueColorTemperatureMin = 153;
        private const int HueColorTemperatureMax = 500;

        private static readonly HttpClient HttpClient = new()
        {
            Timeout = TimeSpan.FromSeconds(15),
        };

        private readonly PhilipsHueSettings _settings;

        public PhilipsHueProvider(PhilipsHueSettings settings)
        {
            _settings = settings ?? new PhilipsHueSettings();
        }

        public string ProviderId => "philips_hue";

        public string DisplayName => "Philips Hue";

        public SmartHomeProviderDescriptor GetDescriptor()
        {
            var hasBridgeIp = !string.IsNullOrWhiteSpace(_settings.BridgeIp);
            var hasApplicationKey = !string.IsNullOrWhiteSpace(_settings.ApplicationKey);
            var isConfigured = hasBridgeIp && hasApplicationKey;

            return new SmartHomeProviderDescriptor
            {
                ProviderId = ProviderId,
                DisplayName = DisplayName,
                Status = isConfigured ? "Connected" : "Not connected",
                IsConfigured = isConfigured,
                RequiredFields = new[] { "bridge_ip", "application_key" },
                ConfiguredFields = GetConfiguredFields(hasBridgeIp, hasApplicationKey),
                Detail = isConfigured
                    ? $"Hue bridge target: {NormalizeBridgeHost(_settings.BridgeIp)}"
                    : "Press the button on the Hue Bridge, create a local application key, then store both fields here.",
            };
        }

        public async Task<SmartHomeProviderState> GetStateAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var descriptor = GetDescriptor();
            if (!descriptor.IsConfigured)
            {
                return new SmartHomeProviderState
                {
                    ProviderId = ProviderId,
                    DisplayName = DisplayName,
                    Descriptor = descriptor,
                    SavedSettings = GetSavedSettings(),
                    Devices = Array.Empty<SmartHomeDevice>(),
                };
            }

            try
            {
                var devices = await GetLightsAsync(cancellationToken).ConfigureAwait(false);
                return new SmartHomeProviderState
                {
                    ProviderId = ProviderId,
                    DisplayName = DisplayName,
                    Descriptor = descriptor,
                    SavedSettings = GetSavedSettings(),
                    Devices = devices,
                };
            }
            catch (Exception ex)
            {
                return new SmartHomeProviderState
                {
                    ProviderId = ProviderId,
                    DisplayName = DisplayName,
                    Descriptor = descriptor,
                    SavedSettings = GetSavedSettings(),
                    Devices = Array.Empty<SmartHomeDevice>(),
                    Error = ex.Message,
                };
            }
        }

        public async Task<SmartHomeActionResult> ExecuteActionAsync(SmartHomeActionRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!string.Equals(request.ProviderId, ProviderId, StringComparison.OrdinalIgnoreCase))
            {
                return new SmartHomeActionResult { Ok = false, Message = "Provider mismatch." };
            }

            if (string.IsNullOrWhiteSpace(_settings.BridgeIp) || string.IsNullOrWhiteSpace(_settings.ApplicationKey))
            {
                return new SmartHomeActionResult { Ok = false, Message = "Hue bridge is not linked yet." };
            }

            var statePatch = BuildStatePatch(request);
            if (statePatch.Count == 0)
            {
                return new SmartHomeActionResult { Ok = false, Message = "Unsupported Hue action." };
            }

            var host = NormalizeBridgeHost(_settings.BridgeIp);
            var uri = new Uri($"http://{host}/api/{_settings.ApplicationKey}/lights/{request.DeviceId}/state");
            var body = JsonSerializer.Serialize(statePatch);

            using var httpRequest = new HttpRequestMessage(HttpMethod.Put, uri)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };

            using var response = await HttpClient.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            return new SmartHomeActionResult
            {
                Ok = true,
                Message = "Hue light updated.",
            };
        }

        private async Task<IReadOnlyList<SmartHomeDevice>> GetLightsAsync(CancellationToken cancellationToken)
        {
            var host = NormalizeBridgeHost(_settings.BridgeIp);
            var uri = new Uri($"http://{host}/api/{_settings.ApplicationKey}/lights");

            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            using var response = await HttpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return Array.Empty<SmartHomeDevice>();

            var result = new List<SmartHomeDevice>();
            foreach (var property in document.RootElement.EnumerateObject())
            {
                var lightId = property.Name;
                var light = property.Value;

                var state = light.TryGetProperty("state", out var stateElement) ? stateElement : default;
                var isOn = GetBoolean(state, "on");
                var brightness = GetInt(state, "bri");
                var hue = GetInt(state, "hue");
                var saturation = GetInt(state, "sat");
                var colorTemperature = GetInt(state, "ct");
                var effect = GetString(state, "effect");
                var reachable = GetBoolean(state, "reachable");

                var capabilities = new List<SmartHomeCapability>
                {
                    BuildOnOffCapability(isOn),
                };

                if (brightness.HasValue)
                {
                    capabilities.Add(BuildBrightnessCapability(brightness.Value));
                }

                if (hue.HasValue)
                {
                    capabilities.Add(BuildHueCapability(hue.Value));
                }

                if (saturation.HasValue)
                {
                    capabilities.Add(BuildSaturationCapability(saturation.Value));
                }

                if (colorTemperature.HasValue)
                {
                    capabilities.Add(BuildColorTemperatureCapability(colorTemperature.Value));
                }

                if (!string.IsNullOrWhiteSpace(effect))
                {
                    capabilities.Add(BuildEffectCapability(effect));
                }

                result.Add(new SmartHomeDevice
                {
                    DeviceId = lightId,
                    Name = GetString(light, "name") is { Length: > 0 } name ? name : $"Hue Light {lightId}",
                    Sku = GetString(light, "modelid"),
                    DeviceType = GetString(light, "type"),
                    IsOnline = reachable,
                    Capabilities = capabilities,
                });
            }

            return result;
        }

        private static SmartHomeCapability BuildOnOffCapability(bool? isOn)
        {
            return new SmartHomeCapability
            {
                Type = "devices.capabilities.on_off",
                Instance = "powerSwitch",
                DataType = "boolean",
                Unit = string.Empty,
                HasState = isOn.HasValue,
                StateValue = SerializeValue(isOn ?? false),
                Options = new[]
                {
                    new SmartHomeCapabilityOption { Name = "on", Value = SerializeValue(true) },
                    new SmartHomeCapabilityOption { Name = "off", Value = SerializeValue(false) },
                },
            };
        }

        private static SmartHomeCapability BuildBrightnessCapability(int brightness)
        {
            return new SmartHomeCapability
            {
                Type = "devices.capabilities.range",
                Instance = "brightness",
                DataType = "integer",
                Unit = "%",
                Min = 0,
                Max = 100,
                HasState = true,
                StateValue = SerializeValue(ConvertBrightnessToPercent(brightness)),
            };
        }

        private static SmartHomeCapability BuildHueCapability(int hue)
        {
            return new SmartHomeCapability
            {
                Type = "devices.capabilities.range",
                Instance = "colorHue",
                DataType = "integer",
                Unit = "deg",
                Min = 0,
                Max = 360,
                HasState = true,
                StateValue = SerializeValue(ConvertHueToDegrees(hue)),
            };
        }

        private static SmartHomeCapability BuildSaturationCapability(int saturation)
        {
            return new SmartHomeCapability
            {
                Type = "devices.capabilities.range",
                Instance = "colorSaturation",
                DataType = "integer",
                Unit = "%",
                Min = 0,
                Max = 100,
                HasState = true,
                StateValue = SerializeValue(ConvertSaturationToPercent(saturation)),
            };
        }

        private static SmartHomeCapability BuildColorTemperatureCapability(int colorTemperature)
        {
            return new SmartHomeCapability
            {
                Type = "devices.capabilities.range",
                Instance = "colorTemperature",
                DataType = "integer",
                Unit = "mired",
                Min = HueColorTemperatureMin,
                Max = HueColorTemperatureMax,
                HasState = true,
                StateValue = SerializeValue(Math.Clamp(colorTemperature, HueColorTemperatureMin, HueColorTemperatureMax)),
            };
        }

        private static SmartHomeCapability BuildEffectCapability(string effect)
        {
            return new SmartHomeCapability
            {
                Type = "devices.capabilities.mode",
                Instance = "effectMode",
                DataType = "string",
                Unit = string.Empty,
                HasState = true,
                StateValue = SerializeValue(effect),
                Options = new[]
                {
                    new SmartHomeCapabilityOption { Name = "none", Value = SerializeValue("none") },
                    new SmartHomeCapabilityOption { Name = "colorloop", Value = SerializeValue("colorloop") },
                },
            };
        }

        private static Dictionary<string, object> BuildStatePatch(SmartHomeActionRequest request)
        {
            var statePatch = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            var instance = request.CapabilityInstance ?? string.Empty;

            if (string.Equals(instance, "powerSwitch", StringComparison.OrdinalIgnoreCase))
            {
                if (TryReadBoolean(request.Value, out var boolValue))
                {
                    statePatch["on"] = boolValue;
                }
            }
            else if (string.Equals(instance, "brightness", StringComparison.OrdinalIgnoreCase))
            {
                if (TryReadInt(request.Value, out var intValue))
                {
                    statePatch["bri"] = ConvertPercentToBrightness(intValue);
                    statePatch["on"] = true;
                }
            }
            else if (string.Equals(instance, "colorHue", StringComparison.OrdinalIgnoreCase))
            {
                if (TryReadInt(request.Value, out var intValue))
                {
                    statePatch["hue"] = ConvertDegreesToHue(intValue);
                    statePatch["on"] = true;
                }
            }
            else if (string.Equals(instance, "colorSaturation", StringComparison.OrdinalIgnoreCase))
            {
                if (TryReadInt(request.Value, out var intValue))
                {
                    statePatch["sat"] = ConvertPercentToSaturation(intValue);
                    statePatch["on"] = true;
                }
            }
            else if (string.Equals(instance, "colorTemperature", StringComparison.OrdinalIgnoreCase))
            {
                if (TryReadInt(request.Value, out var intValue))
                {
                    statePatch["ct"] = Math.Clamp(intValue, HueColorTemperatureMin, HueColorTemperatureMax);
                    statePatch["on"] = true;
                }
            }
            else if (string.Equals(instance, "effectMode", StringComparison.OrdinalIgnoreCase))
            {
                var effectValue = request.Value.ValueKind == JsonValueKind.String ? request.Value.GetString() : request.Value.ToString();
                if (!string.IsNullOrWhiteSpace(effectValue))
                {
                    statePatch["effect"] = effectValue;
                    statePatch["on"] = true;
                }
            }

            return statePatch;
        }

        private static int ConvertBrightnessToPercent(int brightness)
        {
            return (int)Math.Round(Math.Clamp(brightness, 0, HueApiBrightnessMax) * 100d / HueApiBrightnessMax, MidpointRounding.AwayFromZero);
        }

        private static int ConvertPercentToBrightness(int percent)
        {
            return (int)Math.Round(Math.Clamp(percent, 0, 100) * HueApiBrightnessMax / 100d, MidpointRounding.AwayFromZero);
        }

        private static int ConvertHueToDegrees(int hue)
        {
            return (int)Math.Round(Math.Clamp(hue, 0, HueApiHueMax) * 360d / HueApiHueMax, MidpointRounding.AwayFromZero);
        }

        private static int ConvertDegreesToHue(int degrees)
        {
            return (int)Math.Round(Math.Clamp(degrees, 0, 360) * HueApiHueMax / 360d, MidpointRounding.AwayFromZero);
        }

        private static int ConvertSaturationToPercent(int saturation)
        {
            return (int)Math.Round(Math.Clamp(saturation, 0, HueApiSaturationMax) * 100d / HueApiSaturationMax, MidpointRounding.AwayFromZero);
        }

        private static int ConvertPercentToSaturation(int percent)
        {
            return (int)Math.Round(Math.Clamp(percent, 0, 100) * HueApiSaturationMax / 100d, MidpointRounding.AwayFromZero);
        }

        private static bool TryReadBoolean(JsonElement value, out bool boolValue)
        {
            boolValue = false;
            switch (value.ValueKind)
            {
                case JsonValueKind.True:
                    boolValue = true;
                    return true;
                case JsonValueKind.False:
                    boolValue = false;
                    return true;
                case JsonValueKind.String:
                    return bool.TryParse(value.GetString(), out boolValue);
                case JsonValueKind.Number when value.TryGetInt32(out var numericValue):
                    boolValue = numericValue != 0;
                    return true;
                default:
                    return false;
            }
        }

        private static bool TryReadInt(JsonElement value, out int intValue)
        {
            intValue = 0;
            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out intValue))
                return true;

            if (value.ValueKind == JsonValueKind.String)
                return int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out intValue);

            return false;
        }

        public async Task<PhilipsHueLinkResult> LinkBridgeAsync(CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(_settings.BridgeIp))
            {
                return new PhilipsHueLinkResult
                {
                    Ok = false,
                    Message = "Enter the Hue bridge IP first.",
                };
            }

            var host = NormalizeBridgeHost(_settings.BridgeIp);
            var uri = new Uri($"http://{host}/api");
            var deadlineUtc = DateTime.UtcNow.AddSeconds(30);
            PhilipsHueLinkResult? lastFailure = null;

            while (DateTime.UtcNow < deadlineUtc)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var result = await TryLinkBridgeOnceAsync(uri, cancellationToken).ConfigureAwait(false);
                if (result.Ok)
                    return result;

                lastFailure = result;
                if (!result.ShouldRetry)
                    return result;

                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
            }

            return lastFailure ?? new PhilipsHueLinkResult
            {
                Ok = false,
                Message = "Timed out waiting for the Hue bridge button press.",
            };
        }

        private static async Task<PhilipsHueLinkResult> TryLinkBridgeOnceAsync(Uri uri, CancellationToken cancellationToken)
        {
            var body = JsonSerializer.Serialize(new
            {
                devicetype = "atlasai#smart_home",
                generateclientkey = true,
            });

            using var request = new HttpRequestMessage(HttpMethod.Post, uri)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };

            using var response = await HttpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var responseText = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            using var document = JsonDocument.Parse(responseText);

            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return new PhilipsHueLinkResult
                {
                    Ok = false,
                    Message = "Unexpected Hue bridge response.",
                };
            }

            foreach (var item in document.RootElement.EnumerateArray())
            {
                if (item.TryGetProperty("success", out var successElement))
                {
                    var applicationKey = successElement.TryGetProperty("username", out var usernameElement)
                        ? usernameElement.GetString() ?? string.Empty
                        : string.Empty;

                    if (!string.IsNullOrWhiteSpace(applicationKey))
                    {
                        return new PhilipsHueLinkResult
                        {
                            Ok = true,
                            ApplicationKey = applicationKey,
                            Message = "Hue bridge linked.",
                        };
                    }
                }

                if (item.TryGetProperty("error", out var errorElement))
                {
                    var description = errorElement.TryGetProperty("description", out var descriptionElement)
                        ? descriptionElement.GetString() ?? string.Empty
                        : string.Empty;
                    var shouldRetry = description.Contains("link button not pressed", StringComparison.OrdinalIgnoreCase);

                    return new PhilipsHueLinkResult
                    {
                        Ok = false,
                        ShouldRetry = shouldRetry,
                        Message = shouldRetry
                            ? "Waiting for Hue bridge button press..."
                            : (string.IsNullOrWhiteSpace(description)
                                ? "Press the button on the Hue Bridge, then try Link Bridge again."
                                : description),
                    };
                }
            }

            return new PhilipsHueLinkResult
            {
                Ok = false,
                Message = "Hue bridge did not return an application key.",
            };
        }

        private SmartHomeProviderFormState GetSavedSettings()
        {
            return new SmartHomeProviderFormState
            {
                Enabled = _settings.Enabled,
                BridgeIp = _settings.BridgeIp,
                ApplicationKey = _settings.ApplicationKey,
            };
        }

        public Uri? GetBridgeBaseUri()
        {
            if (string.IsNullOrWhiteSpace(_settings.BridgeIp))
                return null;

            var normalized = NormalizeBridgeHost(_settings.BridgeIp);
            return Uri.TryCreate($"https://{normalized}", UriKind.Absolute, out var uri) ? uri : null;
        }

        internal static string NormalizeBridgeHost(string bridgeIp)
        {
            var value = (bridgeIp ?? string.Empty).Trim();

            if (TryExtractBridgeIp(value, out var extractedIp))
                value = extractedIp;

            value = value.Replace("https://", string.Empty, StringComparison.OrdinalIgnoreCase);
            value = value.Replace("http://", string.Empty, StringComparison.OrdinalIgnoreCase);
            return value.TrimEnd('/');
        }

        private static bool TryExtractBridgeIp(string input, out string bridgeIp)
        {
            bridgeIp = string.Empty;
            if (string.IsNullOrWhiteSpace(input))
                return false;

            try
            {
                using var document = JsonDocument.Parse(input);
                var root = document.RootElement;

                if (root.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in root.EnumerateArray())
                    {
                        if (TryGetInternalIp(item, out bridgeIp))
                            return true;
                    }
                }

                if (root.ValueKind == JsonValueKind.Object)
                {
                    return TryGetInternalIp(root, out bridgeIp);
                }
            }
            catch
            {
            }

            return false;
        }

        private static bool TryGetInternalIp(JsonElement element, out string bridgeIp)
        {
            bridgeIp = string.Empty;
            if (!element.TryGetProperty("internalipaddress", out var ipElement))
                return false;

            bridgeIp = ipElement.GetString() ?? string.Empty;
            return !string.IsNullOrWhiteSpace(bridgeIp);
        }

        private static string GetString(JsonElement element, string propertyName)
        {
            if (!element.TryGetProperty(propertyName, out var property))
                return string.Empty;

            return property.ValueKind == JsonValueKind.String ? property.GetString() ?? string.Empty : property.ToString();
        }

        private static bool? GetBoolean(JsonElement element, string propertyName)
        {
            if (!element.TryGetProperty(propertyName, out var property))
                return null;

            return property.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.String when bool.TryParse(property.GetString(), out var boolValue) => boolValue,
                _ => null,
            };
        }

        private static int? GetInt(JsonElement element, string propertyName)
        {
            if (!element.TryGetProperty(propertyName, out var property))
                return null;

            if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var intValue))
                return intValue;

            if (property.ValueKind == JsonValueKind.String && int.TryParse(property.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                return parsed;

            return null;
        }

        private static JsonElement SerializeValue<T>(T value)
        {
            using var document = JsonDocument.Parse(JsonSerializer.Serialize(value));
            return document.RootElement.Clone();
        }

        private static string[] GetConfiguredFields(bool hasBridgeIp, bool hasApplicationKey)
        {
            var fields = new System.Collections.Generic.List<string>();
            if (hasBridgeIp)
                fields.Add("bridge_ip");
            if (hasApplicationKey)
                fields.Add("application_key");
            return fields.ToArray();
        }

        internal sealed class PhilipsHueLinkResult
        {
            public bool Ok { get; init; }
            public bool ShouldRetry { get; init; }
            public string ApplicationKey { get; init; } = string.Empty;
            public string Message { get; init; } = string.Empty;
        }
    }
}