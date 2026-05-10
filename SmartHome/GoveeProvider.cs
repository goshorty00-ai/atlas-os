using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AtlasAI.Settings;

namespace AtlasAI.SmartHome
{
    internal sealed class GoveeProvider : ISmartHomeProvider
    {
        private static readonly Uri ApiBaseUri = new("https://openapi.api.govee.com/");
        private static readonly HttpClient HttpClient = new()
        {
            BaseAddress = ApiBaseUri,
            Timeout = TimeSpan.FromSeconds(20),
        };

        private readonly GoveeSettings _settings;

        public GoveeProvider(GoveeSettings settings)
        {
            _settings = settings ?? new GoveeSettings();
        }

        public string ProviderId => "govee";

        public string DisplayName => "Govee";

        public SmartHomeProviderDescriptor GetDescriptor()
        {
            var hasApiKey = !string.IsNullOrWhiteSpace(_settings.ApiKey);

            return new SmartHomeProviderDescriptor
            {
                ProviderId = ProviderId,
                DisplayName = DisplayName,
                Status = hasApiKey ? "API key saved" : "Not connected",
                IsConfigured = hasApiKey,
                RequiredFields = new[] { "api_key" },
                ConfiguredFields = hasApiKey ? new[] { "api_key" } : Array.Empty<string>(),
                Detail = hasApiKey
                    ? "Atlas will verify the key by fetching your live Govee devices, state, and scene-capable capabilities."
                    : "Paste a Govee developer API key to discover devices and fetch their live state.",
            };
        }

        public async Task<SmartHomeProviderState> GetStateAsync(CancellationToken cancellationToken)
        {
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
                var devices = await GetDevicesAsync(cancellationToken).ConfigureAwait(false);
                var result = new List<SmartHomeDevice>(devices.Count);

                foreach (var device in devices)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var stateMap = await GetDeviceStateMapAsync(device.Sku, device.DeviceId, cancellationToken).ConfigureAwait(false);
                    result.Add(BuildDevice(device, stateMap));
                }

                descriptor = new SmartHomeProviderDescriptor
                {
                    ProviderId = descriptor.ProviderId,
                    DisplayName = descriptor.DisplayName,
                    IsConfigured = descriptor.IsConfigured,
                    RequiredFields = descriptor.RequiredFields,
                    ConfiguredFields = descriptor.ConfiguredFields,
                    Status = result.Count > 0 ? $"Live · {result.Count} devices" : "API key saved",
                    Detail = result.Count > 0
                        ? "Govee responded successfully and Atlas loaded the live device inventory."
                        : "The API key is saved, but Govee did not return any devices for this account.",
                };

                return new SmartHomeProviderState
                {
                    ProviderId = ProviderId,
                    DisplayName = DisplayName,
                    Descriptor = descriptor,
                    SavedSettings = GetSavedSettings(),
                    Devices = result,
                };
            }
            catch (Exception ex)
            {
                descriptor = new SmartHomeProviderDescriptor
                {
                    ProviderId = descriptor.ProviderId,
                    DisplayName = descriptor.DisplayName,
                    IsConfigured = descriptor.IsConfigured,
                    RequiredFields = descriptor.RequiredFields,
                    ConfiguredFields = descriptor.ConfiguredFields,
                    Status = "Connection error",
                    Detail = "Atlas could not verify the Govee API key or device inventory. Check the key and try refreshing again.",
                };

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
            if (!string.Equals(request.ProviderId, ProviderId, StringComparison.OrdinalIgnoreCase))
            {
                return new SmartHomeActionResult { Ok = false, Message = "Provider mismatch." };
            }

            if (string.IsNullOrWhiteSpace(_settings.ApiKey))
            {
                return new SmartHomeActionResult { Ok = false, Message = "Govee API key is not configured." };
            }

            GoveeCapabilityDefinition? capabilityDefinition = null;
            try
            {
                var devices = await GetDevicesAsync(cancellationToken).ConfigureAwait(false);
                capabilityDefinition = devices
                    .FirstOrDefault(device =>
                        string.Equals(device.Sku, request.Sku, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(device.DeviceId, request.DeviceId, StringComparison.OrdinalIgnoreCase))?
                    .Capabilities
                    .FirstOrDefault(capability =>
                        string.Equals(capability.Type, request.CapabilityType, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(capability.Instance, request.CapabilityInstance, StringComparison.OrdinalIgnoreCase));
            }
            catch
            {
            }

            var payloadValue = ConvertActionValue(request.Value, request.CapabilityType, request.CapabilityInstance, capabilityDefinition);
            var requestBody = JsonSerializer.Serialize(new
            {
                requestId = Guid.NewGuid().ToString("D"),
                payload = new
                {
                    sku = request.Sku,
                    device = request.DeviceId,
                    capability = new
                    {
                        type = request.CapabilityType,
                        instance = request.CapabilityInstance,
                        value = payloadValue,
                    }
                }
            });

            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "router/api/v1/device/control");
            ApplyHeaders(httpRequest);
            httpRequest.Content = new StringContent(requestBody, Encoding.UTF8, "application/json");

            using var response = await HttpClient.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);
            var responseText = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return new SmartHomeActionResult
                {
                    Ok = false,
                    Message = string.IsNullOrWhiteSpace(responseText)
                        ? $"Govee control failed: {(int)response.StatusCode}."
                        : responseText,
                };
            }

            if (!string.IsNullOrWhiteSpace(responseText))
            {
                try
                {
                    using var document = JsonDocument.Parse(responseText);
                    if (document.RootElement.TryGetProperty("code", out var codeElement) && codeElement.TryGetInt32(out var codeValue) && codeValue != 200)
                    {
                        var apiMessage = GetString(document.RootElement, "message");
                        return new SmartHomeActionResult
                        {
                            Ok = false,
                            Message = string.IsNullOrWhiteSpace(apiMessage) ? $"Govee control failed with code {codeValue}." : apiMessage,
                        };
                    }
                }
                catch
                {
                }
            }

            return new SmartHomeActionResult
            {
                Ok = true,
                Message = "Device updated.",
            };
        }

        private SmartHomeProviderFormState GetSavedSettings()
        {
            return new SmartHomeProviderFormState
            {
                Enabled = _settings.Enabled,
                ApiKey = _settings.ApiKey,
            };
        }

        private async Task<List<GoveeDiscoveredDevice>> GetDevicesAsync(CancellationToken cancellationToken)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "router/api/v1/user/devices");
            ApplyHeaders(request);

            using var response = await HttpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

            if (!document.RootElement.TryGetProperty("data", out var dataElement) || dataElement.ValueKind != JsonValueKind.Array)
                return new List<GoveeDiscoveredDevice>();

            var devices = new List<GoveeDiscoveredDevice>();

            foreach (var deviceElement in dataElement.EnumerateArray())
            {
                var sku = GetString(deviceElement, "sku");
                var deviceId = GetString(deviceElement, "device");
                if (string.IsNullOrWhiteSpace(sku) || string.IsNullOrWhiteSpace(deviceId))
                    continue;

                var capabilities = new List<GoveeCapabilityDefinition>();
                if (deviceElement.TryGetProperty("capabilities", out var capabilitiesElement) && capabilitiesElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var capabilityElement in capabilitiesElement.EnumerateArray())
                    {
                        capabilities.Add(ParseCapabilityDefinition(capabilityElement));
                    }
                }

                devices.Add(new GoveeDiscoveredDevice
                {
                    Sku = sku,
                    DeviceId = deviceId,
                    Name = GetString(deviceElement, "deviceName") is { Length: > 0 } name ? name : sku,
                    DeviceType = GetString(deviceElement, "type"),
                    Capabilities = capabilities,
                });
            }

            return devices;
        }

        private async Task<Dictionary<string, JsonElement>> GetDeviceStateMapAsync(string sku, string deviceId, CancellationToken cancellationToken)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "router/api/v1/device/state");
            ApplyHeaders(request);

            var requestBody = JsonSerializer.Serialize(new
            {
                requestId = Guid.NewGuid().ToString("D"),
                payload = new
                {
                    sku,
                    device = deviceId,
                }
            });

            request.Content = new StringContent(requestBody, Encoding.UTF8, "application/json");

            using var response = await HttpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

            var map = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
            if (!document.RootElement.TryGetProperty("payload", out var payloadElement) ||
                !payloadElement.TryGetProperty("capabilities", out var capabilitiesElement) ||
                capabilitiesElement.ValueKind != JsonValueKind.Array)
            {
                return map;
            }

            foreach (var capabilityElement in capabilitiesElement.EnumerateArray())
            {
                var type = GetString(capabilityElement, "type");
                var instance = GetString(capabilityElement, "instance");
                if (string.IsNullOrWhiteSpace(type) || string.IsNullOrWhiteSpace(instance))
                    continue;

                if (capabilityElement.TryGetProperty("state", out var stateElement) &&
                    stateElement.TryGetProperty("value", out var valueElement))
                {
                    map[$"{type}:{instance}"] = valueElement.Clone();
                }
            }

            return map;
        }

        private SmartHomeDevice BuildDevice(GoveeDiscoveredDevice device, Dictionary<string, JsonElement> stateMap)
        {
            var capabilities = new List<SmartHomeCapability>(device.Capabilities.Count);
            bool? isOnline = TryGetOnlineState(stateMap);

            foreach (var capability in device.Capabilities)
            {
                var key = $"{capability.Type}:{capability.Instance}";
                var hasState = stateMap.TryGetValue(key, out var stateValue);
                var normalizedState = hasState ? NormalizeStateValue(capability, stateValue) : default;

                capabilities.Add(new SmartHomeCapability
                {
                    Type = capability.Type,
                    Instance = capability.Instance,
                    DataType = InferDataType(capability, hasState ? normalizedState : default),
                    Unit = capability.Unit,
                    Min = capability.Min,
                    Max = capability.Max,
                    HasState = hasState,
                    StateValue = hasState ? normalizedState : default,
                    Options = capability.Options,
                });
            }

            return new SmartHomeDevice
            {
                DeviceId = device.DeviceId,
                Name = device.Name,
                Sku = device.Sku,
                DeviceType = device.DeviceType,
                IsOnline = isOnline,
                Capabilities = capabilities,
            };
        }

        private static bool? TryGetOnlineState(Dictionary<string, JsonElement> stateMap)
        {
            if (!stateMap.TryGetValue("devices.capabilities.online:online", out var onlineValue))
                return null;

            return onlineValue.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Number when onlineValue.TryGetInt32(out var intValue) => intValue != 0,
                JsonValueKind.String when bool.TryParse(onlineValue.GetString(), out var boolValue) => boolValue,
                JsonValueKind.String when int.TryParse(onlineValue.GetString(), out var numericValue) => numericValue != 0,
                _ => null,
            };
        }

        private static GoveeCapabilityDefinition ParseCapabilityDefinition(JsonElement capabilityElement)
        {
            var options = new List<SmartHomeCapabilityOption>();
            var dataType = string.Empty;
            var unit = string.Empty;
            int? min = null;
            int? max = null;

            if (capabilityElement.TryGetProperty("parameters", out var parametersElement))
            {
                dataType = GetString(parametersElement, "dataType");
                unit = GetString(parametersElement, "unit");

                if (parametersElement.TryGetProperty("range", out var rangeElement))
                {
                    min = GetInt(rangeElement, "min");
                    max = GetInt(rangeElement, "max");
                }

                min ??= GetInt(parametersElement, "min");
                max ??= GetInt(parametersElement, "max");

                if (parametersElement.TryGetProperty("options", out var optionsElement) && optionsElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var optionElement in optionsElement.EnumerateArray())
                    {
                        if (!optionElement.TryGetProperty("value", out var optionValue))
                            continue;

                        options.Add(new SmartHomeCapabilityOption
                        {
                            Name = GetString(optionElement, "name"),
                            Value = optionValue.Clone(),
                        });
                    }
                }
            }

            return new GoveeCapabilityDefinition
            {
                Type = GetString(capabilityElement, "type"),
                Instance = GetString(capabilityElement, "instance"),
                DataType = InferDefinitionDataType(dataType, min, max, options),
                Unit = unit,
                Min = min,
                Max = max,
                Options = options,
            };
        }

        private static string InferDefinitionDataType(string dataType, int? min, int? max, IReadOnlyList<SmartHomeCapabilityOption> options)
        {
            if (!string.IsNullOrWhiteSpace(dataType))
                return NormalizeCapabilityDataType(dataType);

            if (min.HasValue || max.HasValue)
                return "integer";

            if (options.Count > 0)
            {
                var firstValue = options[0].Value;
                return firstValue.ValueKind switch
                {
                    JsonValueKind.True or JsonValueKind.False => "boolean",
                    JsonValueKind.Number => "integer",
                    _ => "string",
                };
            }

            return string.Empty;
        }

        private static string InferDataType(GoveeCapabilityDefinition capability, JsonElement stateValue)
        {
            var configured = InferDefinitionDataType(capability.DataType, capability.Min, capability.Max, capability.Options);
            if (!string.IsNullOrWhiteSpace(configured))
                return configured;

            return stateValue.ValueKind switch
            {
                JsonValueKind.True or JsonValueKind.False => "boolean",
                JsonValueKind.Number => "integer",
                JsonValueKind.String => "string",
                JsonValueKind.Object => "object",
                JsonValueKind.Array => "array",
                _ => string.Empty,
            };
        }

        private static string NormalizeCapabilityDataType(string dataType)
        {
            var normalized = (dataType ?? string.Empty).Trim().ToLowerInvariant();
            return normalized switch
            {
                "enum" => "integer",
                "integer" => "integer",
                "float" => "number",
                "bool" or "boolean" => "boolean",
                "struct" => "object",
                "array" => "array",
                _ => normalized,
            };
        }

        private static JsonElement NormalizeStateValue(GoveeCapabilityDefinition capability, JsonElement stateValue)
        {
            if (IsBooleanEnumCapability(capability) && TryReadBoolean(stateValue, out var boolValue))
            {
                return JsonSerializer.SerializeToElement(boolValue);
            }

            if (string.Equals(capability.Type, "devices.capabilities.color_setting", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(capability.Instance, "colorRgb", StringComparison.OrdinalIgnoreCase) &&
                TryReadRgb(stateValue, out var red, out var green, out var blue))
            {
                return JsonSerializer.SerializeToElement(new { r = red, g = green, b = blue });
            }

            return stateValue.Clone();
        }

        private static bool IsBooleanEnumCapability(GoveeCapabilityDefinition capability)
        {
            if (!(string.Equals(capability.Type, "devices.capabilities.on_off", StringComparison.OrdinalIgnoreCase) ||
                  string.Equals(capability.Type, "devices.capabilities.toggle", StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }

            if (capability.Options.Count == 0)
            {
                return true;
            }

            return capability.Options.All(option =>
            {
                var name = (option.Name ?? string.Empty).Trim().ToLowerInvariant();
                return name is "on" or "off";
            });
        }

        private void ApplyHeaders(HttpRequestMessage request)
        {
            request.Headers.TryAddWithoutValidation("Govee-API-Key", _settings.ApiKey.Trim());
            request.Headers.TryAddWithoutValidation("Accept", "application/json");
        }

        private static string GetString(JsonElement element, string propertyName)
        {
            if (!element.TryGetProperty(propertyName, out var property))
                return string.Empty;

            return property.ValueKind == JsonValueKind.String ? property.GetString() ?? string.Empty : property.ToString();
        }

        private static int? GetInt(JsonElement element, string propertyName)
        {
            if (!element.TryGetProperty(propertyName, out var property))
                return null;

            if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var intValue))
                return intValue;

            if (property.ValueKind == JsonValueKind.String && int.TryParse(property.GetString(), out var parsed))
                return parsed;

            return null;
        }

        private static object? ConvertActionValue(
            JsonElement value,
            string capabilityType,
            string capabilityInstance,
            GoveeCapabilityDefinition? capability)
        {
            if (IsBooleanEnumCapability(capability ?? new GoveeCapabilityDefinition
                {
                    Type = capabilityType,
                    Instance = capabilityInstance,
                }) && TryReadBoolean(value, out var boolValue))
            {
                return boolValue ? 1 : 0;
            }

            if (string.Equals(capabilityType, "devices.capabilities.color_setting", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(capabilityInstance, "colorRgb", StringComparison.OrdinalIgnoreCase) &&
                TryReadRgb(value, out var red, out var green, out var blue))
            {
                return (red << 16) | (green << 8) | blue;
            }

            return value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Number when value.TryGetInt32(out var intValue) => intValue,
                JsonValueKind.Number when value.TryGetDouble(out var doubleValue) => doubleValue,
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Null => null,
                _ => JsonSerializer.Deserialize<object>(value.GetRawText()),
            };
        }

        private static bool TryReadBoolean(JsonElement value, out bool result)
        {
            result = value.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Number when value.TryGetInt32(out var number) => number != 0,
                JsonValueKind.String when bool.TryParse(value.GetString(), out var boolValue) => boolValue,
                JsonValueKind.String when int.TryParse(value.GetString(), out var parsedNumber) => parsedNumber != 0,
                _ => false,
            };

            return value.ValueKind is JsonValueKind.True or JsonValueKind.False or JsonValueKind.Number or JsonValueKind.String;
        }

        private static bool TryReadRgb(JsonElement value, out int red, out int green, out int blue)
        {
            red = 0;
            green = 0;
            blue = 0;

            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var packed))
            {
                red = (packed >> 16) & 0xFF;
                green = (packed >> 8) & 0xFF;
                blue = packed & 0xFF;
                return true;
            }

            if (value.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            if (!TryGetRgbChannel(value, "r", out red) ||
                !TryGetRgbChannel(value, "g", out green) ||
                !TryGetRgbChannel(value, "b", out blue))
            {
                red = 0;
                green = 0;
                blue = 0;
                return false;
            }

            red = Math.Clamp(red, 0, 255);
            green = Math.Clamp(green, 0, 255);
            blue = Math.Clamp(blue, 0, 255);
            return true;
        }

        private static bool TryGetRgbChannel(JsonElement value, string propertyName, out int channel)
        {
            channel = 0;
            if (!value.TryGetProperty(propertyName, out var property))
            {
                return false;
            }

            if (property.ValueKind == JsonValueKind.Number)
            {
                return property.TryGetInt32(out channel);
            }

            return property.ValueKind == JsonValueKind.String && int.TryParse(property.GetString(), out channel);
        }

        private sealed class GoveeDiscoveredDevice
        {
            public string Sku { get; init; } = string.Empty;
            public string DeviceId { get; init; } = string.Empty;
            public string Name { get; init; } = string.Empty;
            public string DeviceType { get; init; } = string.Empty;
            public List<GoveeCapabilityDefinition> Capabilities { get; init; } = new();
        }

        private sealed class GoveeCapabilityDefinition
        {
            public string Type { get; init; } = string.Empty;
            public string Instance { get; init; } = string.Empty;
            public string DataType { get; init; } = string.Empty;
            public string Unit { get; init; } = string.Empty;
            public int? Min { get; init; }
            public int? Max { get; init; }
            public IReadOnlyList<SmartHomeCapabilityOption> Options { get; init; } = Array.Empty<SmartHomeCapabilityOption>();
        }
    }
}