using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AtlasAI.Settings;

namespace AtlasAI.SmartHome
{
    internal sealed class TapoKasaProvider : ISmartHomeProvider
    {
        private readonly TapoKasaSettings _settings;

        public TapoKasaProvider(TapoKasaSettings settings)
        {
            _settings = settings ?? new TapoKasaSettings();
        }

        public string ProviderId => "tapo_kasa";

        public string DisplayName => "TP-Link Kasa / Tapo";

        public SmartHomeProviderDescriptor GetDescriptor()
        {
            var hasHost = !string.IsNullOrWhiteSpace(_settings.Host);
            return new SmartHomeProviderDescriptor
            {
                ProviderId = ProviderId,
                DisplayName = DisplayName,
                Status = hasHost ? "Host saved" : "Not connected",
                IsConfigured = hasHost,
                RequiredFields = new[] { "host" },
                ConfiguredFields = BuildConfiguredFields(hasHost),
                Detail = hasHost
                    ? "Atlas probes the saved local device host using the legacy local TP-Link protocol. Compatible Kasa devices work best; newer Tapo models may be limited."
                    : "Enter a local Kasa or Tapo device IP or hostname. Atlas currently supports compatible local TP-Link devices first.",
            };
        }

        private string[] BuildConfiguredFields(bool hasHost)
        {
            var fields = new List<string>();
            if (hasHost)
                fields.Add("host");
            if (!string.IsNullOrWhiteSpace(_settings.Username))
                fields.Add("username");
            if (!string.IsNullOrWhiteSpace(_settings.Password))
                fields.Add("password");
            return fields.ToArray();
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
                var sysInfo = await QuerySysInfoAsync(cancellationToken).ConfigureAwait(false);
                var device = BuildDevice(sysInfo);

                return new SmartHomeProviderState
                {
                    ProviderId = ProviderId,
                    DisplayName = DisplayName,
                    Descriptor = new SmartHomeProviderDescriptor
                    {
                        ProviderId = ProviderId,
                        DisplayName = DisplayName,
                        Status = "Live · 1 device",
                        IsConfigured = true,
                        RequiredFields = descriptor.RequiredFields,
                        ConfiguredFields = descriptor.ConfiguredFields,
                        Detail = "Atlas connected to the saved TP-Link device using the local Kasa-compatible protocol.",
                    },
                    SavedSettings = GetSavedSettings(),
                    Devices = new[] { device },
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
                        Detail = "Atlas could not talk to the saved TP-Link device. Newer Tapo hardware may require a different auth path than the legacy local protocol.",
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

            if (string.Equals(request.CapabilityInstance, "powerSwitch", StringComparison.OrdinalIgnoreCase))
            {
                await SendCommandAsync(
                    JsonSerializer.Serialize(new { system = new { set_relay_state = new { state = GetBoolean(request.Value) ? 1 : 0 } } }),
                    cancellationToken).ConfigureAwait(false);

                return new SmartHomeActionResult { Ok = true, Message = "TP-Link device power updated." };
            }

            if (string.Equals(request.CapabilityInstance, "brightness", StringComparison.OrdinalIgnoreCase))
            {
                var brightnessPayload = new Dictionary<string, object?>
                {
                    ["smartlife"] = new Dictionary<string, object?>
                    {
                        ["iot.smartbulb.lightingservice"] = new Dictionary<string, object?>
                        {
                            ["transition_light_state"] = new Dictionary<string, object?>
                            {
                                ["on_off"] = 1,
                                ["brightness"] = Math.Clamp(GetInt(request.Value), 0, 100),
                            },
                        },
                    },
                };

                await SendCommandAsync(
                    JsonSerializer.Serialize(brightnessPayload),
                    cancellationToken).ConfigureAwait(false);

                return new SmartHomeActionResult { Ok = true, Message = "TP-Link brightness updated." };
            }

            return new SmartHomeActionResult { Ok = false, Message = "That TP-Link action is not supported yet." };
        }

        private SmartHomeProviderFormState GetSavedSettings()
        {
            return new SmartHomeProviderFormState
            {
                Enabled = _settings.Enabled,
                Host = _settings.Host,
                Username = _settings.Username,
                Password = _settings.Password,
            };
        }

        private async Task<JsonElement> QuerySysInfoAsync(CancellationToken cancellationToken)
        {
            var response = await SendCommandAsync("{\"system\":{\"get_sysinfo\":{}}}", cancellationToken).ConfigureAwait(false);
            using var document = JsonDocument.Parse(response);
            if (!document.RootElement.TryGetProperty("system", out var system) || !system.TryGetProperty("get_sysinfo", out var sysInfo))
                throw new InvalidOperationException("TP-Link device did not return sysinfo.");
            return sysInfo.Clone();
        }

        private SmartHomeDevice BuildDevice(JsonElement sysInfo)
        {
            var alias = GetString(sysInfo, "alias");
            var model = FirstNonEmpty(GetString(sysInfo, "model"), GetString(sysInfo, "mic_type"), "TP-Link Device");
            var deviceId = FirstNonEmpty(GetString(sysInfo, "deviceId"), GetString(sysInfo, "mac"), _settings.Host);
            var relayState = TryGetInt(sysInfo, "relay_state", out var relay) && relay == 1;
            var brightness = TryGetInt(sysInfo, "brightness", out var level) ? level : -1;

            var capabilities = new List<SmartHomeCapability>
            {
                new()
                {
                    Type = "switch",
                    Instance = "powerSwitch",
                    DataType = "boolean",
                    Unit = string.Empty,
                    HasState = true,
                    StateValue = JsonSerializer.SerializeToElement(relayState),
                }
            };

            if (brightness >= 0)
            {
                capabilities.Add(new SmartHomeCapability
                {
                    Type = "switchLevel",
                    Instance = "brightness",
                    DataType = "integer",
                    Unit = "%",
                    Min = 0,
                    Max = 100,
                    HasState = true,
                    StateValue = JsonSerializer.SerializeToElement(brightness),
                });
            }

            return new SmartHomeDevice
            {
                DeviceId = deviceId,
                Name = string.IsNullOrWhiteSpace(alias) ? _settings.Host : alias,
                Sku = model,
                DeviceType = model,
                IsOnline = true,
                ExternalUrl = BuildHttpUrl(_settings.Host),
                Capabilities = capabilities,
            };
        }

        private static string BuildHttpUrl(string host)
        {
            var trimmed = (host ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
                return string.Empty;
            if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                return trimmed;
            return $"http://{trimmed}";
        }

        private async Task<string> SendCommandAsync(string payload, CancellationToken cancellationToken)
        {
            using var client = new TcpClient();
            await client.ConnectAsync(_settings.Host.Trim(), 9999, cancellationToken).ConfigureAwait(false);
            await using var stream = client.GetStream();

            var requestBytes = Encrypt(payload);
            var lengthBytes = BitConverter.GetBytes(System.Net.IPAddress.HostToNetworkOrder(requestBytes.Length));
            await stream.WriteAsync(lengthBytes, cancellationToken).ConfigureAwait(false);
            await stream.WriteAsync(requestBytes, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);

            var header = new byte[4];
            await ReadExactAsync(stream, header, cancellationToken).ConfigureAwait(false);
            var responseLength = System.Net.IPAddress.NetworkToHostOrder(BitConverter.ToInt32(header, 0));
            if (responseLength <= 0 || responseLength > 1_000_000)
                throw new InvalidDataException("TP-Link device returned an invalid payload length.");

            var responseBuffer = new byte[responseLength];
            await ReadExactAsync(stream, responseBuffer, cancellationToken).ConfigureAwait(false);
            return Decrypt(responseBuffer);
        }

        private static async Task ReadExactAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
        {
            var offset = 0;
            while (offset < buffer.Length)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(offset, buffer.Length - offset), cancellationToken).ConfigureAwait(false);
                if (read <= 0)
                    throw new EndOfStreamException("Unexpected end of TP-Link device response.");
                offset += read;
            }
        }

        private static byte[] Encrypt(string plainText)
        {
            var key = 171;
            var bytes = Encoding.UTF8.GetBytes(plainText);
            var result = new byte[bytes.Length];
            for (var index = 0; index < bytes.Length; index++)
            {
                result[index] = (byte)(bytes[index] ^ key);
                key = result[index];
            }

            return result;
        }

        private static string Decrypt(byte[] cipherText)
        {
            var key = 171;
            var result = new byte[cipherText.Length];
            for (var index = 0; index < cipherText.Length; index++)
            {
                result[index] = (byte)(cipherText[index] ^ key);
                key = cipherText[index];
            }

            return Encoding.UTF8.GetString(result);
        }

        private static string GetString(JsonElement element, string propertyName)
        {
            return element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
                ? property.GetString() ?? string.Empty
                : string.Empty;
        }

        private static bool TryGetInt(JsonElement element, string propertyName, out int value)
        {
            value = 0;
            if (!element.TryGetProperty(propertyName, out var property))
                return false;

            if (property.ValueKind == JsonValueKind.Number)
                return property.TryGetInt32(out value);
            if (property.ValueKind == JsonValueKind.String)
                return int.TryParse(property.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
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

        private static string FirstNonEmpty(params string[] values)
        {
            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                    return value;
            }

            return string.Empty;
        }
    }
}