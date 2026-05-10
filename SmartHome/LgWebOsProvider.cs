using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Security;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using AtlasAI.Settings;

namespace AtlasAI.SmartHome
{
    internal sealed class LgWebOsProvider : ISmartHomeProvider
    {
        private static readonly HttpClient DiscoveryHttpClient = new()
        {
            Timeout = TimeSpan.FromSeconds(3),
        };

        private static readonly string[] RegisterPermissions =
        {
            "LAUNCH",
            "LAUNCH_WEBAPP",
            "APP_TO_APP",
            "CLOSE",
            "TEST_OPEN",
            "TEST_PROTECTED",
            "CONTROL_AUDIO",
            "CONTROL_DISPLAY",
            "CONTROL_INPUT_JOYSTICK",
            "CONTROL_INPUT_MEDIA_RECORDING",
            "CONTROL_INPUT_MEDIA_PLAYBACK",
            "CONTROL_INPUT_TV",
            "CONTROL_POWER",
            "READ_APP_STATUS",
            "READ_CURRENT_CHANNEL",
            "READ_INPUT_DEVICE_LIST",
            "READ_NETWORK_STATE",
            "READ_RUNNING_APPS",
            "READ_TV_CHANNEL_LIST",
            "WRITE_NOTIFICATION_TOAST",
            "READ_POWER_STATE",
            "READ_COUNTRY_INFO",
            "READ_SETTINGS",
            "CONTROL_TV_SCREEN",
            "CONTROL_TV_STANBY",
            "CONTROL_FAVORITE_GROUP",
            "CONTROL_USER_INFO",
            "CHECK_BLUETOOTH_DEVICE",
            "CONTROL_BLUETOOTH",
            "CONTROL_TIMER_INFO",
            "STB_INTERNAL_CONNECTION",
            "CONTROL_RECORDING",
            "READ_RECORDING_STATE",
            "WRITE_RECORDING_LIST",
            "READ_RECORDING_LIST",
            "READ_RECORDING_SCHEDULE",
            "WRITE_RECORDING_SCHEDULE",
            "READ_STORAGE_DEVICE_LIST",
            "READ_TV_PROGRAM_INFO",
            "CONTROL_BOX_CHANNEL",
            "READ_TV_ACR_AUTH_TOKEN",
            "READ_TV_CONTENT_STATE",
            "READ_TV_CURRENT_TIME",
            "ADD_LAUNCHER_CHANNEL",
            "SET_CHANNEL_SKIP",
            "RELEASE_CHANNEL_SKIP",
            "CONTROL_CHANNEL_BLOCK",
            "DELETE_SELECT_CHANNEL",
            "CONTROL_CHANNEL_GROUP",
            "SCAN_TV_CHANNELS",
            "CONTROL_TV_POWER",
            "CONTROL_WOL"
        };

        private static readonly string[] SignedPermissions =
        {
            "TEST_SECURE",
            "CONTROL_INPUT_TEXT",
            "CONTROL_MOUSE_AND_KEYBOARD",
            "READ_INSTALLED_APPS",
            "READ_LGE_SDX",
            "READ_NOTIFICATIONS",
            "SEARCH",
            "WRITE_SETTINGS",
            "WRITE_NOTIFICATION_ALERT",
            "CONTROL_POWER",
            "READ_CURRENT_CHANNEL",
            "READ_RUNNING_APPS",
            "READ_UPDATE_INFO",
            "UPDATE_FROM_REMOTE_APP",
            "READ_LGE_TV_INPUT_EVENTS",
            "READ_TV_CURRENT_TIME"
        };

        private const string RegisterSignature = "eyJhbGdvcml0aG0iOiJSU0EtU0hBMjU2Iiwia2V5SWQiOiJ0ZXN0LXNpZ25pbmctY2VydCIsInNpZ25hdHVyZVZlcnNpb24iOjF9.hrVRgjCwXVvE2OOSpDZ58hR+59aFNwYDyjQgKk3auukd7pcegmE2CzPCa0bJ0ZsRAcKkCTJrWo5iDzNhMBWRyaMOv5zWSrthlf7G128qvIlpMT0YNY+n/FaOHE73uLrS/g7swl3/qH/BGFG2Hu4RlL48eb3lLKqTt2xKHdCs6Cd4RMfJPYnzgvI4BNrFUKsjkcu+WD4OO2A27Pq1n50cMchmcaXadJhGrOqH5YmHdOCj5NSHzJYrsW0HPlpuAx/ECMeIZYDh6RMqaFM2DXzdKX9NmmyqzJ3o/0lkk/N97gfVRLW5hA29yeAwaCViZNCP8iC9aO0q9fQojoa7NQnAtw==";

        private readonly LgWebOsSettings _settings;

        public LgWebOsProvider(LgWebOsSettings settings)
        {
            _settings = settings ?? new LgWebOsSettings();
        }

        public string ProviderId => "lg_webos";

        public string DisplayName => "LG webOS TV";

        public SmartHomeProviderDescriptor GetDescriptor()
        {
            var hasHost = !string.IsNullOrWhiteSpace(_settings.Host);
            var hasClientKey = !string.IsNullOrWhiteSpace(_settings.ClientKey);
            var hasMacAddress = !string.IsNullOrWhiteSpace(_settings.MacAddress);
            var isConfigured = hasHost && hasClientKey;

            return new SmartHomeProviderDescriptor
            {
                ProviderId = ProviderId,
                DisplayName = DisplayName,
                Status = isConfigured ? "Connected" : "Not connected",
                IsConfigured = isConfigured,
                RequiredFields = new[] { "host", "client_key" },
                ConfiguredFields = GetConfiguredFields(hasHost, hasClientKey, hasMacAddress),
                Detail = isConfigured
                    ? (hasMacAddress
                        ? $"Targeting {NormalizeHost(_settings.Host)} with Wake-on-LAN ready."
                        : $"Targeting {NormalizeHost(_settings.Host)}. Pairing is stored; Wake-on-LAN will unlock once Atlas learns the TV MAC address.")
                    : "Enter the LG TV IP or hostname, then approve the pairing prompt on the TV so Atlas can store a webOS client key.",
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
                var snapshot = await GetTvSnapshotAsync(cancellationToken).ConfigureAwait(false);
                return new SmartHomeProviderState
                {
                    ProviderId = ProviderId,
                    DisplayName = DisplayName,
                    Descriptor = descriptor,
                    SavedSettings = GetSavedSettings(),
                    Devices = new[] { BuildDevice(snapshot, isOnline: true) },
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
                    Devices = new[] { BuildOfflineDevice() },
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

            if (string.IsNullOrWhiteSpace(_settings.Host) || string.IsNullOrWhiteSpace(_settings.ClientKey))
            {
                return new SmartHomeActionResult { Ok = false, Message = "LG TV is not paired yet." };
            }

            try
            {
                if (string.Equals(request.CapabilityInstance, "powerSwitch", StringComparison.OrdinalIgnoreCase) &&
                    TryReadBoolean(request.Value, out var powerValue))
                {
                    if (powerValue)
                    {
                        if (string.IsNullOrWhiteSpace(_settings.MacAddress))
                        {
                            return new SmartHomeActionResult
                            {
                                Ok = false,
                                Message = "Atlas can turn the TV off now, but powering it on needs a saved MAC address. Pair once while the TV is awake on the same network.",
                            };
                        }

                        SendWakeOnLan(_settings.MacAddress);
                        return new SmartHomeActionResult
                        {
                            Ok = true,
                            Message = "Wake signal sent to the LG TV.",
                        };
                    }

                    await using var client = new LgWebOsClient(_settings.Host, _settings.ClientKey);
                    await client.ConnectAndRegisterAsync(cancellationToken).ConfigureAwait(false);
                    await client.SendRequestAsync("ssap://system/turnOff", null, cancellationToken).ConfigureAwait(false);
                    return new SmartHomeActionResult
                    {
                        Ok = true,
                        Message = "LG TV turning off.",
                    };
                }

                await using var commandClient = new LgWebOsClient(_settings.Host, _settings.ClientKey);
                await commandClient.ConnectAndRegisterAsync(cancellationToken).ConfigureAwait(false);

                if (string.Equals(request.CapabilityInstance, "volume", StringComparison.OrdinalIgnoreCase) &&
                    TryReadInt(request.Value, out var volume))
                {
                    await commandClient.SendRequestAsync(
                        "ssap://audio/setVolume",
                        new Dictionary<string, object> { ["volume"] = Math.Clamp(volume, 0, 100) },
                        cancellationToken).ConfigureAwait(false);

                    return new SmartHomeActionResult
                    {
                        Ok = true,
                        Message = "LG TV volume updated.",
                    };
                }

                if (string.Equals(request.CapabilityInstance, "mute", StringComparison.OrdinalIgnoreCase) &&
                    TryReadBoolean(request.Value, out var muteValue))
                {
                    await commandClient.SendRequestAsync(
                        "ssap://audio/setMute",
                        new Dictionary<string, object> { ["mute"] = muteValue },
                        cancellationToken).ConfigureAwait(false);

                    return new SmartHomeActionResult
                    {
                        Ok = true,
                        Message = muteValue ? "LG TV muted." : "LG TV unmuted.",
                    };
                }

                if (string.Equals(request.CapabilityInstance, "inputSource", StringComparison.OrdinalIgnoreCase))
                {
                    var inputId = ReadStringValue(request.Value);
                    if (string.IsNullOrWhiteSpace(inputId))
                    {
                        return new SmartHomeActionResult { Ok = false, Message = "Input change requires a target input id." };
                    }

                    await commandClient.SendRequestAsync(
                        "ssap://tv/switchInput",
                        new Dictionary<string, object> { ["inputId"] = inputId },
                        cancellationToken).ConfigureAwait(false);

                    return new SmartHomeActionResult
                    {
                        Ok = true,
                        Message = "LG TV input updated.",
                    };
                }

                if (string.Equals(request.CapabilityInstance, "appLauncher", StringComparison.OrdinalIgnoreCase))
                {
                    var appId = ReadStringValue(request.Value);
                    if (string.IsNullOrWhiteSpace(appId))
                    {
                        return new SmartHomeActionResult { Ok = false, Message = "App launch requires an app id." };
                    }

                    await commandClient.SendRequestAsync(
                        "ssap://system.launcher/launch",
                        new Dictionary<string, object> { ["id"] = appId },
                        cancellationToken).ConfigureAwait(false);

                    return new SmartHomeActionResult
                    {
                        Ok = true,
                        Message = "LG TV app launch requested.",
                    };
                }

                return new SmartHomeActionResult { Ok = false, Message = "Unsupported LG TV action." };
            }
            catch (Exception ex)
            {
                return new SmartHomeActionResult
                {
                    Ok = false,
                    Message = ex.Message,
                };
            }
        }

        public async Task<LgWebOsLinkResult> LinkAsync(CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(_settings.Host))
            {
                return new LgWebOsLinkResult
                {
                    Ok = false,
                    Message = "Enter the LG TV IP or hostname first.",
                };
            }

            await using var client = new LgWebOsClient(_settings.Host, _settings.ClientKey);
            var clientKey = await client.ConnectAndRegisterAsync(cancellationToken).ConfigureAwait(false);
            var macAddress = await TryResolveMacAddressAsync(_settings.Host, cancellationToken).ConfigureAwait(false);

            return new LgWebOsLinkResult
            {
                Ok = !string.IsNullOrWhiteSpace(clientKey),
                ClientKey = clientKey,
                MacAddress = macAddress,
                Message = string.IsNullOrWhiteSpace(macAddress)
                    ? "LG TV paired. Atlas stored the webOS client key."
                    : "LG TV paired and Wake-on-LAN is ready.",
            };
        }

        public static async Task<IReadOnlyList<string>> DiscoverHostsAsync(CancellationToken cancellationToken)
        {
            const string request = "M-SEARCH * HTTP/1.1\r\nHOST: 239.255.255.250:1900\r\nMAN: \"ssdp:discover\"\r\nMX: 2\r\nST: urn:schemas-upnp-org:device:MediaRenderer:1\r\n\r\n";
            var responses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            using var client = new UdpClient(AddressFamily.InterNetwork)
            {
                EnableBroadcast = true,
            };

            client.Client.ReceiveTimeout = 1500;

            var requestBytes = Encoding.ASCII.GetBytes(request);
            for (var attempt = 0; attempt < 2; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await client.SendAsync(requestBytes, requestBytes.Length, new IPEndPoint(IPAddress.Parse("239.255.255.250"), 1900)).ConfigureAwait(false);
                var deadline = DateTime.UtcNow.AddSeconds(2);

                while (DateTime.UtcNow < deadline)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    try
                    {
                        var receiveTask = client.ReceiveAsync();
                        var completed = await Task.WhenAny(receiveTask, Task.Delay(350, cancellationToken)).ConfigureAwait(false);
                        if (completed != receiveTask)
                            continue;

                        var result = receiveTask.Result;
                        var responseText = Encoding.UTF8.GetString(result.Buffer);
                        var location = ExtractHeaderValue(responseText, "LOCATION");
                        if (!string.IsNullOrWhiteSpace(location))
                            responses.Add(location);
                    }
                    catch (SocketException)
                    {
                        break;
                    }
                }
            }

            var candidates = new List<string>();
            foreach (var location in responses)
            {
                var host = await TryResolveLgHostFromLocationAsync(location, cancellationToken).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(host) && !candidates.Contains(host, StringComparer.OrdinalIgnoreCase))
                    candidates.Add(host);
            }

            return candidates;
        }

        private async Task<LgTvSnapshot> GetTvSnapshotAsync(CancellationToken cancellationToken)
        {
            await using var client = new LgWebOsClient(_settings.Host, _settings.ClientKey);
            await client.ConnectAndRegisterAsync(cancellationToken).ConfigureAwait(false);

            var systemInfo = await client.SendRequestAsync("ssap://system/getSystemInfo", null, cancellationToken).ConfigureAwait(false);
            var powerState = await client.SendRequestAsync("ssap://com.webos.service.tvpower/power/getPowerState", null, cancellationToken).ConfigureAwait(false);
            var volumeInfo = await client.SendRequestAsync("ssap://audio/getVolume", null, cancellationToken).ConfigureAwait(false);
            var appInfo = await client.SendRequestAsync("ssap://com.webos.applicationManager/getForegroundAppInfo", null, cancellationToken).ConfigureAwait(false);
            var inputsInfo = await client.SendRequestAsync("ssap://tv/getExternalInputList", null, cancellationToken).ConfigureAwait(false);
            var launchPointsInfo = await client.SendRequestAsync("ssap://com.webos.applicationManager/listLaunchPoints", null, cancellationToken).ConfigureAwait(false);

            return new LgTvSnapshot
            {
                Host = NormalizeHost(_settings.Host),
                Name = FirstNonEmpty(
                    GetString(systemInfo, "modelName"),
                    GetString(systemInfo, "receiverType"),
                    "LG webOS TV"),
                DeviceType = FirstNonEmpty(GetString(systemInfo, "receiverType"), "TV"),
                Model = GetString(systemInfo, "modelName"),
                IsPoweredOn = !string.Equals(GetString(powerState, "state"), "Power Off", StringComparison.OrdinalIgnoreCase),
                Volume = GetInt(volumeInfo, "volume") ?? GetInt(volumeInfo, "volumeStatus") ?? 0,
                IsMuted = GetBoolean(volumeInfo, "muted") ?? false,
                CurrentAppId = GetString(appInfo, "appId"),
                CurrentAppName = GetString(appInfo, "appName"),
                Inputs = ParseInputOptions(inputsInfo),
                Apps = ParseLaunchPointOptions(launchPointsInfo),
            };
        }

        private SmartHomeProviderFormState GetSavedSettings()
        {
            return new SmartHomeProviderFormState
            {
                Enabled = _settings.Enabled,
                Host = _settings.Host,
                ClientKey = _settings.ClientKey,
            };
        }

        private SmartHomeDevice BuildOfflineDevice()
        {
            return new SmartHomeDevice
            {
                DeviceId = NormalizeHost(_settings.Host),
                Name = "LG webOS TV",
                Sku = "webos",
                DeviceType = "TV",
                IsOnline = false,
                Capabilities = new[]
                {
                    BuildPowerCapability(false)
                },
            };
        }

        private static SmartHomeDevice BuildDevice(LgTvSnapshot snapshot, bool isOnline)
        {
            var capabilities = new List<SmartHomeCapability>
            {
                BuildPowerCapability(snapshot.IsPoweredOn),
                BuildVolumeCapability(snapshot.Volume),
                BuildMuteCapability(snapshot.IsMuted),
            };

            if (snapshot.Inputs.Count > 0)
            {
                capabilities.Add(BuildOptionsCapability(
                    "devices.capabilities.mode",
                    "inputSource",
                    snapshot.Inputs,
                    snapshot.Inputs.FirstOrDefault(static option => option.IsActive)?.Value ?? string.Empty));
            }

            if (snapshot.Apps.Count > 0)
            {
                capabilities.Add(BuildOptionsCapability(
                    "devices.capabilities.mode",
                    "appLauncher",
                    snapshot.Apps,
                    snapshot.CurrentAppId));
            }

            return new SmartHomeDevice
            {
                DeviceId = snapshot.Host,
                Name = snapshot.Name,
                Sku = string.IsNullOrWhiteSpace(snapshot.Model) ? "webos" : snapshot.Model,
                DeviceType = snapshot.DeviceType,
                IsOnline = isOnline,
                Capabilities = capabilities,
            };
        }

        private static SmartHomeCapability BuildPowerCapability(bool isOn)
        {
            return new SmartHomeCapability
            {
                Type = "devices.capabilities.on_off",
                Instance = "powerSwitch",
                DataType = "boolean",
                Unit = string.Empty,
                HasState = true,
                StateValue = SerializeValue(isOn),
                Options = new[]
                {
                    new SmartHomeCapabilityOption { Name = "on", Value = SerializeValue(true) },
                    new SmartHomeCapabilityOption { Name = "off", Value = SerializeValue(false) },
                },
            };
        }

        private static SmartHomeCapability BuildVolumeCapability(int volume)
        {
            return new SmartHomeCapability
            {
                Type = "devices.capabilities.range",
                Instance = "volume",
                DataType = "integer",
                Unit = "%",
                Min = 0,
                Max = 100,
                HasState = true,
                StateValue = SerializeValue(Math.Clamp(volume, 0, 100)),
            };
        }

        private static SmartHomeCapability BuildMuteCapability(bool isMuted)
        {
            return new SmartHomeCapability
            {
                Type = "devices.capabilities.toggle",
                Instance = "mute",
                DataType = "boolean",
                Unit = string.Empty,
                HasState = true,
                StateValue = SerializeValue(isMuted),
                Options = new[]
                {
                    new SmartHomeCapabilityOption { Name = "mute", Value = SerializeValue(true) },
                    new SmartHomeCapabilityOption { Name = "unmute", Value = SerializeValue(false) },
                },
            };
        }

        private static SmartHomeCapability BuildOptionsCapability(string type, string instance, IReadOnlyList<LgNamedOption> options, string currentValue)
        {
            return new SmartHomeCapability
            {
                Type = type,
                Instance = instance,
                DataType = "string",
                Unit = string.Empty,
                HasState = !string.IsNullOrWhiteSpace(currentValue),
                StateValue = SerializeValue(currentValue),
                Options = options
                    .Where(static option => !string.IsNullOrWhiteSpace(option.Value))
                    .Take(12)
                    .Select(static option => new SmartHomeCapabilityOption
                    {
                        Name = option.Name,
                        Value = SerializeValue(option.Value),
                    })
                    .ToArray(),
            };
        }

        private static IReadOnlyList<string> GetConfiguredFields(bool hasHost, bool hasClientKey, bool hasMacAddress)
        {
            var configured = new List<string>(3);
            if (hasHost)
                configured.Add("host");
            if (hasClientKey)
                configured.Add("client_key");
            if (hasMacAddress)
                configured.Add("mac_address");
            return configured;
        }

        private static string NormalizeHost(string host)
        {
            var normalized = (host ?? string.Empty).Trim();
            normalized = normalized.Replace("wss://", string.Empty, StringComparison.OrdinalIgnoreCase);
            normalized = normalized.Replace("ws://", string.Empty, StringComparison.OrdinalIgnoreCase);
            normalized = normalized.Replace("https://", string.Empty, StringComparison.OrdinalIgnoreCase);
            normalized = normalized.Replace("http://", string.Empty, StringComparison.OrdinalIgnoreCase);
            return normalized.TrimEnd('/');
        }

        private static string ExtractHeaderValue(string response, string headerName)
        {
            var lines = response.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                var separatorIndex = line.IndexOf(':');
                if (separatorIndex <= 0)
                    continue;

                var name = line[..separatorIndex].Trim();
                if (!string.Equals(name, headerName, StringComparison.OrdinalIgnoreCase))
                    continue;

                return line[(separatorIndex + 1)..].Trim();
            }

            return string.Empty;
        }

        private static async Task<string> TryResolveLgHostFromLocationAsync(string location, CancellationToken cancellationToken)
        {
            try
            {
                if (!Uri.TryCreate(location, UriKind.Absolute, out var uri))
                    return string.Empty;

                var xml = await DiscoveryHttpClient.GetStringAsync(uri, cancellationToken).ConfigureAwait(false);
                if (xml.IndexOf("LG Electronics", StringComparison.OrdinalIgnoreCase) < 0 &&
                    xml.IndexOf("webOS", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    return string.Empty;
                }

                return NormalizeHost(uri.Host);
            }
            catch
            {
                return string.Empty;
            }
        }

        private static IReadOnlyList<LgNamedOption> ParseInputOptions(JsonElement payload)
        {
            if (!payload.TryGetProperty("devices", out var devicesElement) || devicesElement.ValueKind != JsonValueKind.Array)
                return Array.Empty<LgNamedOption>();

            var options = new List<LgNamedOption>();
            foreach (var deviceElement in devicesElement.EnumerateArray())
            {
                var inputId = FirstNonEmpty(GetString(deviceElement, "id"), GetString(deviceElement, "appId"));
                if (string.IsNullOrWhiteSpace(inputId))
                    continue;

                var name = FirstNonEmpty(GetString(deviceElement, "label"), GetString(deviceElement, "name"), inputId);
                options.Add(new LgNamedOption
                {
                    Name = name,
                    Value = inputId,
                    IsActive = GetBoolean(deviceElement, "connected") == true || GetBoolean(deviceElement, "active") == true,
                });
            }

            return options;
        }

        private static IReadOnlyList<LgNamedOption> ParseLaunchPointOptions(JsonElement payload)
        {
            if (!payload.TryGetProperty("launchPoints", out var launchPointsElement) || launchPointsElement.ValueKind != JsonValueKind.Array)
                return Array.Empty<LgNamedOption>();

            var options = new List<LgNamedOption>();
            foreach (var appElement in launchPointsElement.EnumerateArray())
            {
                var appId = GetString(appElement, "id");
                if (string.IsNullOrWhiteSpace(appId))
                    continue;

                if (GetBoolean(appElement, "systemApp") == true &&
                    string.Equals(appId, "com.webos.app.discovery", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                options.Add(new LgNamedOption
                {
                    Name = FirstNonEmpty(GetString(appElement, "title"), GetString(appElement, "label"), appId),
                    Value = appId,
                    IsActive = false,
                });
            }

            return options;
        }

        private static JsonElement SerializeValue<T>(T value)
        {
            return JsonSerializer.SerializeToElement(value);
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
                    return true;
                case JsonValueKind.Number when value.TryGetInt32(out var numericValue):
                    boolValue = numericValue != 0;
                    return true;
                case JsonValueKind.String:
                    return bool.TryParse(value.GetString(), out boolValue);
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

        private static string ReadStringValue(JsonElement value)
        {
            return value.ValueKind switch
            {
                JsonValueKind.String => value.GetString() ?? string.Empty,
                JsonValueKind.Number => value.ToString(),
                JsonValueKind.True => bool.TrueString,
                JsonValueKind.False => bool.FalseString,
                _ => string.Empty,
            };
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

            if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var value))
                return value;

            if (property.ValueKind == JsonValueKind.String && int.TryParse(property.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
                return value;

            return null;
        }

        private static bool? GetBoolean(JsonElement element, string propertyName)
        {
            if (!element.TryGetProperty(propertyName, out var property))
                return null;

            return property.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Number when property.TryGetInt32(out var numericValue) => numericValue != 0,
                JsonValueKind.String when bool.TryParse(property.GetString(), out var boolValue) => boolValue,
                _ => null,
            };
        }

        private static string FirstNonEmpty(params string[] values)
        {
            return values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
        }

        private static void SendWakeOnLan(string macAddress)
        {
            var bytes = ParseMacAddress(macAddress);
            var packet = new byte[102];

            for (var index = 0; index < 6; index++)
                packet[index] = 0xFF;

            for (var repeat = 0; repeat < 16; repeat++)
                Buffer.BlockCopy(bytes, 0, packet, 6 + (repeat * 6), 6);

            using var client = new UdpClient();
            client.EnableBroadcast = true;
            client.Send(packet, packet.Length, new IPEndPoint(IPAddress.Broadcast, 9));
        }

        private static byte[] ParseMacAddress(string macAddress)
        {
            var normalized = Regex.Replace(macAddress ?? string.Empty, "[^0-9A-Fa-f]", string.Empty);
            if (normalized.Length != 12)
                throw new InvalidOperationException("Stored LG TV MAC address is invalid.");

            var bytes = new byte[6];
            for (var index = 0; index < bytes.Length; index++)
                bytes[index] = Convert.ToByte(normalized.Substring(index * 2, 2), 16);

            return bytes;
        }

        private static async Task<string> TryResolveMacAddressAsync(string host, CancellationToken cancellationToken)
        {
            try
            {
                var normalizedHost = NormalizeHost(host);
                using var ping = new Ping();
                await ping.SendPingAsync(normalizedHost, 1500).WaitAsync(cancellationToken).ConfigureAwait(false);

                using var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "arp",
                        Arguments = $"-a {normalizedHost}",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                    }
                };

                process.Start();
                var output = await process.StandardOutput.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
                await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

                var match = Regex.Match(output, @"([0-9a-f]{2}(?:-[0-9a-f]{2}){5})", RegexOptions.IgnoreCase);
                return match.Success ? match.Groups[1].Value : string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private sealed class LgTvSnapshot
        {
            public string Host { get; init; } = string.Empty;
            public string Name { get; init; } = string.Empty;
            public string DeviceType { get; init; } = string.Empty;
            public string Model { get; init; } = string.Empty;
            public bool IsPoweredOn { get; init; }
            public int Volume { get; init; }
            public bool IsMuted { get; init; }
            public string CurrentAppId { get; init; } = string.Empty;
            public string CurrentAppName { get; init; } = string.Empty;
            public IReadOnlyList<LgNamedOption> Inputs { get; init; } = Array.Empty<LgNamedOption>();
            public IReadOnlyList<LgNamedOption> Apps { get; init; } = Array.Empty<LgNamedOption>();
        }

        private sealed class LgNamedOption
        {
            public string Name { get; init; } = string.Empty;
            public string Value { get; init; } = string.Empty;
            public bool IsActive { get; init; }
        }

        private sealed class LgWebOsClient : IAsyncDisposable
        {
            private readonly string _host;
            private readonly string _clientKey;
            private readonly ClientWebSocket _socket = new();
            private int _requestId;

            public LgWebOsClient(string host, string clientKey)
            {
                _host = NormalizeHost(host);
                _clientKey = clientKey ?? string.Empty;
                _socket.Options.RemoteCertificateValidationCallback = static (_, _, _, _) => true;
                _socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
            }

            public async Task<string> ConnectAndRegisterAsync(CancellationToken cancellationToken)
            {
                if (_socket.State == WebSocketState.None)
                {
                    await _socket.ConnectAsync(new Uri($"wss://{_host}:3001/"), cancellationToken).ConfigureAwait(false);
                }

                var payload = CreateRegisterEnvelope(_clientKey);
                await SendAsync(payload, cancellationToken).ConfigureAwait(false);

                while (true)
                {
                    var message = await ReceiveAsync(cancellationToken).ConfigureAwait(false);
                    var messageType = GetString(message, "type");

                    if (string.Equals(messageType, "registered", StringComparison.OrdinalIgnoreCase))
                    {
                        var payloadElement = message.TryGetProperty("payload", out var registeredPayload) ? registeredPayload : default;
                        var key = GetString(payloadElement, "client-key");
                        if (string.IsNullOrWhiteSpace(key))
                            throw new InvalidOperationException("LG TV pairing completed without returning a client key.");

                        return key;
                    }

                    if (message.TryGetProperty("error", out var errorElement))
                    {
                        throw new InvalidOperationException(FirstNonEmpty(GetString(errorElement, "message"), "LG TV pairing failed."));
                    }
                }
            }

            public async Task<JsonElement> SendRequestAsync(string uri, Dictionary<string, object>? payload, CancellationToken cancellationToken)
            {
                var messageId = Interlocked.Increment(ref _requestId).ToString(CultureInfo.InvariantCulture);
                var envelope = new Dictionary<string, object>
                {
                    ["id"] = messageId,
                    ["type"] = "request",
                    ["uri"] = uri,
                };

                if (payload is { Count: > 0 })
                    envelope["payload"] = payload;

                await SendAsync(envelope, cancellationToken).ConfigureAwait(false);

                while (true)
                {
                    var message = await ReceiveAsync(cancellationToken).ConfigureAwait(false);
                    if (!string.Equals(GetString(message, "id"), messageId, StringComparison.Ordinal))
                        continue;

                    var messageType = GetString(message, "type");
                    if (string.Equals(messageType, "error", StringComparison.OrdinalIgnoreCase))
                    {
                        var errorText = message.TryGetProperty("errorText", out var errorTextElement)
                            ? errorTextElement.ToString()
                            : "LG TV command failed.";
                        throw new InvalidOperationException(errorText);
                    }

                    return message.TryGetProperty("payload", out var responsePayload)
                        ? responsePayload.Clone()
                        : JsonSerializer.SerializeToElement(new { });
                }
            }

            public async ValueTask DisposeAsync()
            {
                if (_socket.State == WebSocketState.Open || _socket.State == WebSocketState.CloseReceived)
                {
                    try
                    {
                        await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None).ConfigureAwait(false);
                    }
                    catch
                    {
                    }
                }

                _socket.Dispose();
            }

            private async Task SendAsync(object payload, CancellationToken cancellationToken)
            {
                var json = JsonSerializer.Serialize(payload);
                var bytes = Encoding.UTF8.GetBytes(json);
                await _socket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken).ConfigureAwait(false);
            }

            private async Task<JsonElement> ReceiveAsync(CancellationToken cancellationToken)
            {
                using var stream = new MemoryStream();
                var buffer = new byte[4096];

                while (true)
                {
                    var result = await _socket.ReceiveAsync(buffer, cancellationToken).ConfigureAwait(false);
                    if (result.MessageType == WebSocketMessageType.Close)
                        throw new InvalidOperationException("LG TV closed the Smart Home connection.");

                    stream.Write(buffer, 0, result.Count);
                    if (result.EndOfMessage)
                        break;
                }

                stream.Position = 0;
                using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
                return document.RootElement.Clone();
            }

            private static Dictionary<string, object> CreateRegisterEnvelope(string clientKey)
            {
                var payload = new Dictionary<string, object>
                {
                    ["forcePairing"] = false,
                    ["manifest"] = new Dictionary<string, object>
                    {
                        ["appVersion"] = "1.1",
                        ["manifestVersion"] = 1,
                        ["permissions"] = RegisterPermissions,
                        ["signatures"] = new object[]
                        {
                            new Dictionary<string, object>
                            {
                                ["signature"] = RegisterSignature,
                                ["signatureVersion"] = 1,
                            }
                        },
                        ["signed"] = new Dictionary<string, object>
                        {
                            ["appId"] = "com.lge.test",
                            ["created"] = "20140509",
                            ["localizedAppNames"] = new Dictionary<string, string>
                            {
                                [""] = "LG Remote App",
                                ["ko-KR"] = "리모컨 앱",
                                ["zxx-XX"] = "ЛГ Rэмotэ AПП",
                            },
                            ["localizedVendorNames"] = new Dictionary<string, string>
                            {
                                [""] = "LG Electronics",
                            },
                            ["permissions"] = SignedPermissions,
                            ["serial"] = "2f930e2d2cfe083771f68e4fe7bb07",
                            ["vendorId"] = "com.lge",
                        }
                    },
                    ["pairingType"] = "PROMPT",
                };

                if (!string.IsNullOrWhiteSpace(clientKey))
                    payload["client-key"] = clientKey;

                return new Dictionary<string, object>
                {
                    ["id"] = "register_0",
                    ["type"] = "register",
                    ["payload"] = payload,
                };
            }
        }
    }

    internal sealed class LgWebOsLinkResult
    {
        public bool Ok { get; init; }
        public string ClientKey { get; init; } = string.Empty;
        public string MacAddress { get; init; } = string.Empty;
        public string Message { get; init; } = string.Empty;
    }
}