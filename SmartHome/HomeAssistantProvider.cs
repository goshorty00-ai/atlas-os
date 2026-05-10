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
    internal sealed class HomeAssistantProvider : ISmartHomeProvider
    {
        private readonly HomeAssistantSettings _settings;

        public HomeAssistantProvider(HomeAssistantSettings settings)
        {
            _settings = settings ?? new HomeAssistantSettings();
        }

        public string ProviderId => "home_assistant";

        public string DisplayName => "Home Assistant";

        public SmartHomeProviderDescriptor GetDescriptor()
        {
            var hasBaseUrl = !string.IsNullOrWhiteSpace(_settings.BaseUrl);
            var hasToken = !string.IsNullOrWhiteSpace(_settings.AccessToken);
            var fields = new List<string>();
            if (hasBaseUrl)
                fields.Add("base_url");
            if (hasToken)
                fields.Add("access_token");

            return new SmartHomeProviderDescriptor
            {
                ProviderId = ProviderId,
                DisplayName = DisplayName,
                Status = hasBaseUrl && hasToken ? "Credentials saved" : "Not connected",
                IsConfigured = hasBaseUrl && hasToken,
                RequiredFields = new[] { "base_url", "access_token" },
                ConfiguredFields = fields,
                Detail = hasBaseUrl && hasToken
                    ? "Atlas can load Home Assistant entities and control common domains like lights, switches, locks, and media players."
                    : "Enter your Home Assistant base URL and a long-lived access token.",
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
                using var client = CreateClient();
                using var response = await client.GetAsync("api/states", cancellationToken).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();

                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

                var devices = new List<SmartHomeDevice>();
                if (document.RootElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var entity in document.RootElement.EnumerateArray())
                    {
                        var mapped = BuildDevice(entity);
                        if (mapped != null)
                            devices.Add(mapped);
                    }
                }

                return new SmartHomeProviderState
                {
                    ProviderId = ProviderId,
                    DisplayName = DisplayName,
                    Descriptor = new SmartHomeProviderDescriptor
                    {
                        ProviderId = ProviderId,
                        DisplayName = DisplayName,
                        Status = devices.Count > 0 ? $"Live · {devices.Count} entities" : "Credentials saved",
                        IsConfigured = true,
                        RequiredFields = descriptor.RequiredFields,
                        ConfiguredFields = descriptor.ConfiguredFields,
                        Detail = devices.Count > 0
                            ? "Home Assistant responded successfully and Atlas mapped supported entities."
                            : "Home Assistant is reachable, but Atlas did not find supported entities yet.",
                    },
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
                    Descriptor = new SmartHomeProviderDescriptor
                    {
                        ProviderId = ProviderId,
                        DisplayName = DisplayName,
                        Status = "Connection error",
                        IsConfigured = true,
                        RequiredFields = descriptor.RequiredFields,
                        ConfiguredFields = descriptor.ConfiguredFields,
                        Detail = "Atlas could not reach Home Assistant. Check the base URL and token.",
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

            using var client = CreateClient();

            string domain;
            string service;
            object body;

            if (string.Equals(request.CapabilityInstance, "powerSwitch", StringComparison.OrdinalIgnoreCase))
            {
                domain = GetDomain(request.DeviceId);
                service = GetBoolean(request.Value) ? "turn_on" : "turn_off";
                body = new { entity_id = request.DeviceId };
            }
            else if (string.Equals(request.CapabilityInstance, "brightness", StringComparison.OrdinalIgnoreCase))
            {
                domain = "light";
                service = "turn_on";
                body = new { entity_id = request.DeviceId, brightness_pct = Math.Clamp(GetInt(request.Value), 0, 100) };
            }
            else if (string.Equals(request.CapabilityInstance, "volume", StringComparison.OrdinalIgnoreCase))
            {
                domain = "media_player";
                service = "volume_set";
                body = new { entity_id = request.DeviceId, volume_level = Math.Clamp(GetInt(request.Value), 0, 100) / 100.0 };
            }
            else if (string.Equals(request.CapabilityInstance, "doorLock", StringComparison.OrdinalIgnoreCase))
            {
                domain = "lock";
                service = GetBoolean(request.Value) ? "lock" : "unlock";
                body = new { entity_id = request.DeviceId };
            }
            else
            {
                return new SmartHomeActionResult { Ok = false, Message = "That Home Assistant action is not supported yet." };
            }

            using var response = await client.PostAsync(
                $"api/services/{domain}/{service}",
                new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
                cancellationToken).ConfigureAwait(false);

            var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return new SmartHomeActionResult
                {
                    Ok = false,
                    Message = string.IsNullOrWhiteSpace(text) ? $"Home Assistant control failed: {(int)response.StatusCode}." : text,
                };
            }

            return new SmartHomeActionResult { Ok = true, Message = "Home Assistant entity updated." };
        }

        private SmartHomeProviderFormState GetSavedSettings()
        {
            return new SmartHomeProviderFormState
            {
                Enabled = _settings.Enabled,
                BaseUrl = _settings.BaseUrl,
                AccessToken = _settings.AccessToken,
            };
        }

        private HttpClient CreateClient()
        {
            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            client.BaseAddress = new Uri(NormalizeBaseUrl(_settings.BaseUrl));
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _settings.AccessToken.Trim());
            return client;
        }

        private SmartHomeDevice? BuildDevice(JsonElement entity)
        {
            var entityId = GetString(entity, "entity_id");
            if (string.IsNullOrWhiteSpace(entityId) || !entityId.Contains('.'))
                return null;

            var domain = GetDomain(entityId);
            if (!SupportedDomains.Contains(domain, StringComparer.OrdinalIgnoreCase))
                return null;

            var state = GetString(entity, "state");
            var attributes = entity.TryGetProperty("attributes", out var attrs) && attrs.ValueKind == JsonValueKind.Object ? attrs : default;
            var friendlyName = GetString(attributes, "friendly_name");
            var capabilities = new List<SmartHomeCapability>();

            if (domain is "light" or "switch" or "input_boolean")
            {
                capabilities.Add(BuildBooleanCapability(domain, "powerSwitch", state is "on" or "open"));
            }

            if (domain == "light" && TryGetInt(attributes, "brightness", out var brightness0to255))
            {
                var brightnessPct = (int)Math.Round((brightness0to255 / 255.0) * 100.0, MidpointRounding.AwayFromZero);
                capabilities.Add(BuildNumericCapability("light", "brightness", brightnessPct, 0, 100, "%"));
            }

            if (domain == "media_player" && TryGetDouble(attributes, "volume_level", out var volumeLevel))
            {
                capabilities.Add(BuildNumericCapability("media_player", "volume", (int)Math.Round(volumeLevel * 100.0), 0, 100, "%"));
                capabilities.Add(BuildBooleanCapability("media_player", "powerSwitch", !string.Equals(state, "off", StringComparison.OrdinalIgnoreCase)));
            }

            if (domain == "lock")
            {
                capabilities.Add(BuildBooleanCapability("lock", "doorLock", string.Equals(state, "locked", StringComparison.OrdinalIgnoreCase)));
            }

            var externalUrl = NormalizeBaseUrl(_settings.BaseUrl);
            var previewImageUrl = domain == "camera" ? BuildAbsoluteUrl(GetString(attributes, "entity_picture")) : string.Empty;

            return new SmartHomeDevice
            {
                DeviceId = entityId,
                Name = string.IsNullOrWhiteSpace(friendlyName) ? entityId : friendlyName,
                Sku = domain,
                DeviceType = domain,
                IsOnline = !string.Equals(state, "unavailable", StringComparison.OrdinalIgnoreCase) && !string.Equals(state, "unknown", StringComparison.OrdinalIgnoreCase),
                PreviewImageUrl = previewImageUrl,
                ExternalUrl = externalUrl,
                Capabilities = capabilities,
            };
        }

        private string BuildAbsoluteUrl(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return string.Empty;
            if (Uri.TryCreate(input, UriKind.Absolute, out var absolute))
                return absolute.ToString();

            return new Uri(new Uri(NormalizeBaseUrl(_settings.BaseUrl)), input.TrimStart('/')).ToString();
        }

        private static string NormalizeBaseUrl(string value)
        {
            var trimmed = (value ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
                return "http://homeassistant.local:8123/";
            if (!trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                trimmed = $"http://{trimmed}";
            }

            return trimmed.EndsWith("/", StringComparison.Ordinal) ? trimmed : $"{trimmed}/";
        }

        private static string GetDomain(string entityId)
        {
            var index = entityId.IndexOf('.');
            return index > 0 ? entityId[..index] : entityId;
        }

        private static readonly string[] SupportedDomains =
        {
            "light", "switch", "input_boolean", "lock", "media_player", "camera", "cover", "climate", "sensor", "binary_sensor"
        };

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

        private static string GetString(JsonElement element, string propertyName)
        {
            return element.ValueKind == JsonValueKind.Object && element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
                ? property.GetString() ?? string.Empty
                : string.Empty;
        }

        private static bool TryGetInt(JsonElement element, string propertyName, out int value)
        {
            value = 0;
            if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(propertyName, out var property))
                return false;

            if (property.ValueKind == JsonValueKind.Number)
                return property.TryGetInt32(out value);
            if (property.ValueKind == JsonValueKind.String)
                return int.TryParse(property.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
            return false;
        }

        private static bool TryGetDouble(JsonElement element, string propertyName, out double value)
        {
            value = 0;
            if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(propertyName, out var property))
                return false;

            if (property.ValueKind == JsonValueKind.Number)
                return property.TryGetDouble(out value);
            if (property.ValueKind == JsonValueKind.String)
                return double.TryParse(property.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
            return false;
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
    }
}