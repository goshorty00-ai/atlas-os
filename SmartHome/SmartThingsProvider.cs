using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AtlasAI.Settings;

namespace AtlasAI.SmartHome
{
    internal sealed class SmartThingsProvider : ISmartHomeProvider
    {
        private static readonly HttpClient HttpClient = new()
        {
            BaseAddress = new Uri("https://api.smartthings.com/v1/"),
            Timeout = TimeSpan.FromSeconds(20),
        };

        private readonly SmartThingsSettings _settings;

        public SmartThingsProvider(SmartThingsSettings settings)
        {
            _settings = settings ?? new SmartThingsSettings();
        }

        public string ProviderId => "smartthings";

        public string DisplayName => "SmartThings";

        public SmartHomeProviderDescriptor GetDescriptor()
        {
            var hasToken = !string.IsNullOrWhiteSpace(_settings.AccessToken);
            var fields = new List<string>();
            if (hasToken)
                fields.Add("access_token");
            if (!string.IsNullOrWhiteSpace(_settings.LocationId))
                fields.Add("location_id");

            return new SmartHomeProviderDescriptor
            {
                ProviderId = ProviderId,
                DisplayName = DisplayName,
                Status = hasToken ? "Token saved" : "Not connected",
                IsConfigured = hasToken,
                RequiredFields = new[] { "access_token", "location_id" },
                ConfiguredFields = fields,
                Detail = hasToken
                    ? "Atlas can read your SmartThings devices and control common switch, dimmer, and lock capabilities."
                    : "Paste a SmartThings personal access token. Location id is optional if you want to scope devices.",
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
                var mappedDevices = new List<SmartHomeDevice>();

                foreach (var device in devices)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var status = await GetDeviceStatusAsync(device.DeviceId, cancellationToken).ConfigureAwait(false);
                    if (!string.IsNullOrWhiteSpace(_settings.LocationId) &&
                        !string.Equals(device.LocationId, _settings.LocationId, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    mappedDevices.Add(BuildDevice(device, status));
                }

                return new SmartHomeProviderState
                {
                    ProviderId = ProviderId,
                    DisplayName = DisplayName,
                    Descriptor = new SmartHomeProviderDescriptor
                    {
                        ProviderId = ProviderId,
                        DisplayName = DisplayName,
                        Status = mappedDevices.Count > 0 ? $"Live · {mappedDevices.Count} devices" : "Token saved",
                        IsConfigured = true,
                        RequiredFields = descriptor.RequiredFields,
                        ConfiguredFields = descriptor.ConfiguredFields,
                        Detail = mappedDevices.Count > 0
                            ? "SmartThings responded successfully and Atlas loaded supported devices."
                            : "The token is valid, but no SmartThings devices matched the current scope.",
                    },
                    SavedSettings = GetSavedSettings(),
                    Devices = mappedDevices,
                };
            }
            catch (Exception ex)
            {
                return new SmartHomeProviderState
                {
                    ProviderId = ProviderId,
                    DisplayName = DisplayName,
                    Descriptor = new SmartHomeProviderDescriptor
                    {
                        ProviderId = ProviderId,
                        DisplayName = DisplayName,
                        Status = "Connection error",
                        IsConfigured = true,
                        RequiredFields = descriptor.RequiredFields,
                        ConfiguredFields = descriptor.ConfiguredFields,
                        Detail = "Atlas could not load SmartThings devices. Verify the token and optional location id.",
                    },
                    SavedSettings = GetSavedSettings(),
                    Devices = Array.Empty<SmartHomeDevice>(),
                    Error = ex.Message,
                };
            }
        }

        public async Task<SmartHomeActionResult> ExecuteActionAsync(SmartHomeActionRequest request, CancellationToken cancellationToken)
        {
            if (!string.Equals(request.ProviderId, ProviderId, StringComparison.OrdinalIgnoreCase))
                return new SmartHomeActionResult { Ok = false, Message = "Provider mismatch." };

            if (string.IsNullOrWhiteSpace(_settings.AccessToken))
                return new SmartHomeActionResult { Ok = false, Message = "SmartThings token is not configured." };

            string capability;
            string command;
            object[]? arguments = null;

            if (string.Equals(request.CapabilityInstance, "powerSwitch", StringComparison.OrdinalIgnoreCase))
            {
                capability = "switch";
                command = GetBoolean(request.Value) ? "on" : "off";
            }
            else if (string.Equals(request.CapabilityInstance, "brightness", StringComparison.OrdinalIgnoreCase))
            {
                capability = "switchLevel";
                command = "setLevel";
                arguments = new object[] { Math.Clamp(GetInt(request.Value), 0, 100) };
            }
            else if (string.Equals(request.CapabilityInstance, "doorLock", StringComparison.OrdinalIgnoreCase))
            {
                capability = "lock";
                command = GetBoolean(request.Value) ? "lock" : "unlock";
            }
            else
            {
                return new SmartHomeActionResult { Ok = false, Message = "That SmartThings action is not supported yet." };
            }

            var body = JsonSerializer.Serialize(new
            {
                commands = new[]
                {
                    new
                    {
                        component = "main",
                        capability,
                        command,
                        arguments = arguments ?? Array.Empty<object>(),
                    }
                }
            });

            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"devices/{Uri.EscapeDataString(request.DeviceId)}/commands");
            ApplyHeaders(httpRequest);
            httpRequest.Content = new StringContent(body, Encoding.UTF8, "application/json");

            using var response = await HttpClient.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);
            var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return new SmartHomeActionResult
                {
                    Ok = false,
                    Message = string.IsNullOrWhiteSpace(text) ? $"SmartThings control failed: {(int)response.StatusCode}." : text,
                };
            }

            return new SmartHomeActionResult { Ok = true, Message = "SmartThings device updated." };
        }

        private SmartHomeProviderFormState GetSavedSettings()
        {
            return new SmartHomeProviderFormState
            {
                Enabled = _settings.Enabled,
                AccessToken = _settings.AccessToken,
                LocationId = _settings.LocationId,
            };
        }

        private async Task<List<SmartThingsDevice>> GetDevicesAsync(CancellationToken cancellationToken)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "devices");
            ApplyHeaders(request);
            using var response = await HttpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (!document.RootElement.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
                return new List<SmartThingsDevice>();

            var results = new List<SmartThingsDevice>();
            foreach (var item in items.EnumerateArray())
            {
                var deviceId = GetString(item, "deviceId");
                if (string.IsNullOrWhiteSpace(deviceId))
                    continue;

                results.Add(new SmartThingsDevice
                {
                    DeviceId = deviceId,
                    Label = FirstNonEmpty(GetString(item, "label"), GetString(item, "name"), deviceId),
                    DeviceTypeName = FirstNonEmpty(GetString(item, "deviceTypeName"), GetString(item, "presentationId"), "SmartThings Device"),
                    LocationId = GetString(item, "locationId"),
                });
            }

            return results;
        }

        private async Task<JsonElement?> GetDeviceStatusAsync(string deviceId, CancellationToken cancellationToken)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"devices/{Uri.EscapeDataString(deviceId)}/status");
            ApplyHeaders(request);
            using var response = await HttpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            return document.RootElement.Clone();
        }

        private SmartHomeDevice BuildDevice(SmartThingsDevice device, JsonElement? status)
        {
            var capabilities = new List<SmartHomeCapability>();
            var main = GetNested(status, "components", "main");
            var isOnline = true;

            if (TryGetCapabilityValue(main, "switch", "switch", out var switchState))
            {
                capabilities.Add(BuildBooleanCapability("switch", "powerSwitch", string.Equals(switchState, "on", StringComparison.OrdinalIgnoreCase)));
            }

            if (TryGetCapabilityInt(main, "switchLevel", "level", out var level))
            {
                capabilities.Add(BuildNumericCapability("switchLevel", "brightness", level, 0, 100, "%"));
            }

            if (TryGetCapabilityValue(main, "lock", "lock", out var lockState))
            {
                capabilities.Add(BuildBooleanCapability("lock", "doorLock", string.Equals(lockState, "locked", StringComparison.OrdinalIgnoreCase)));
            }

            if (TryGetCapabilityInt(main, "audioVolume", "volume", out var volume))
            {
                capabilities.Add(BuildNumericCapability("audioVolume", "volume", volume, 0, 100, "%"));
            }

            if (TryGetCapabilityValue(main, "healthCheck", "DeviceWatch-DeviceStatus", out var healthValue))
            {
                isOnline = !string.Equals(healthValue, "offline", StringComparison.OrdinalIgnoreCase);
            }

            return new SmartHomeDevice
            {
                DeviceId = device.DeviceId,
                Name = device.Label,
                Sku = device.DeviceTypeName,
                DeviceType = device.DeviceTypeName,
                IsOnline = isOnline,
                ExternalUrl = "https://my.smartthings.com/advanced/devices",
                Capabilities = capabilities,
            };
        }

        private void ApplyHeaders(HttpRequestMessage request)
        {
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _settings.AccessToken.Trim());
            request.Headers.TryAddWithoutValidation("Accept", "application/json");
        }

        private static SmartHomeCapability BuildBooleanCapability(string type, string instance, bool value)
        {
            return new SmartHomeCapability
            {
                Type = type,
                Instance = instance,
                DataType = "boolean",
                Unit = string.Empty,
                HasState = true,
                StateValue = JsonSerializer.SerializeToElement(value),
            };
        }

        private static SmartHomeCapability BuildNumericCapability(string type, string instance, int value, int min, int max, string unit)
        {
            return new SmartHomeCapability
            {
                Type = type,
                Instance = instance,
                DataType = "integer",
                Unit = unit,
                Min = min,
                Max = max,
                HasState = true,
                StateValue = JsonSerializer.SerializeToElement(value),
            };
        }

        private static JsonElement? GetNested(JsonElement? root, string propertyName, string nestedName)
        {
            if (root is null || root.Value.ValueKind != JsonValueKind.Object)
                return null;
            if (!root.Value.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.Object)
                return null;
            if (!property.TryGetProperty(nestedName, out var nested) || nested.ValueKind != JsonValueKind.Object)
                return null;
            return nested;
        }

        private static bool TryGetCapabilityValue(JsonElement? component, string capabilityName, string attributeName, out string value)
        {
            value = string.Empty;
            if (component is null || component.Value.ValueKind != JsonValueKind.Object)
                return false;
            if (!component.Value.TryGetProperty(capabilityName, out var capability) || capability.ValueKind != JsonValueKind.Object)
                return false;
            if (!capability.TryGetProperty(attributeName, out var attribute) || attribute.ValueKind != JsonValueKind.Object)
                return false;
            if (!attribute.TryGetProperty("value", out var rawValue))
                return false;

            value = rawValue.ValueKind == JsonValueKind.String ? rawValue.GetString() ?? string.Empty : rawValue.GetRawText();
            return !string.IsNullOrWhiteSpace(value);
        }

        private static bool TryGetCapabilityInt(JsonElement? component, string capabilityName, string attributeName, out int value)
        {
            value = 0;
            if (!TryGetCapabilityValue(component, capabilityName, attributeName, out var raw))
                return false;

            return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }

        private static string GetString(JsonElement element, string propertyName)
        {
            return element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
                ? property.GetString() ?? string.Empty
                : string.Empty;
        }

        private static bool GetBoolean(JsonElement value)
        {
            return value.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.String => bool.TryParse(value.GetString(), out var parsed) && parsed,
                JsonValueKind.Number => value.TryGetInt32(out var numeric) && numeric != 0,
                _ => false,
            };
        }

        private static int GetInt(JsonElement value)
        {
            return value.ValueKind switch
            {
                JsonValueKind.Number when value.TryGetInt32(out var numeric) => numeric,
                JsonValueKind.String when int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) => parsed,
                _ => 0,
            };
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

        private sealed class SmartThingsDevice
        {
            public string DeviceId { get; init; } = string.Empty;
            public string Label { get; init; } = string.Empty;
            public string DeviceTypeName { get; init; } = string.Empty;
            public string LocationId { get; init; } = string.Empty;
        }
    }
}