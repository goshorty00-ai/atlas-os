using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using AtlasAI.Core;
using AtlasAI.Settings;

namespace AtlasAI.SmartHome
{
    internal sealed class RingProvider : ISmartHomeProvider
    {
        private static readonly HttpClient HttpClient = new()
        {
            Timeout = TimeSpan.FromSeconds(60),
        };
        private static IReadOnlyList<SmartHomeDevice> LastKnownDevices = Array.Empty<SmartHomeDevice>();

        private readonly RingSettings _settings;

        public RingProvider(RingSettings settings)
        {
            _settings = settings ?? new RingSettings();
        }

        public string ProviderId => "ring";

        public string DisplayName => "Ring";

        public SmartHomeProviderDescriptor GetDescriptor()
        {
            return BuildDescriptor();
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
                using var client = new RingRestClient(_settings.RefreshToken);
                await client.InitializeAsync(cancellationToken).ConfigureAwait(false);
                await PersistRefreshTokenIfChangedAsync(client.RefreshToken).ConfigureAwait(false);

                var locations = await client.GetLocationsAsync(cancellationToken).ConfigureAwait(false);
                var ringDevices = await client.GetRingDevicesAsync(cancellationToken).ConfigureAwait(false);

                var devices = BuildDevices(locations, ringDevices);
                LastKnownDevices = devices;
                try
                {
                    var cameraCount = devices.Count(static device =>
                        device.DeviceType.Contains("camera", StringComparison.OrdinalIgnoreCase) ||
                        device.DeviceType.Contains("doorbell", StringComparison.OrdinalIgnoreCase) ||
                        device.Name.Contains("ring", StringComparison.OrdinalIgnoreCase));
                    AppLogger.LogInfo($"[SmartHome][Ring] Discovered {devices.Count} Ring devices ({cameraCount} cameras/doorbells).");
                }
                catch
                {
                }

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
                var refreshTokenRejected = IsRefreshTokenRejected(ex);
                try { AppLogger.LogWarning($"[SmartHome][Ring] Discovery failed. Using {LastKnownDevices.Count} cached devices. {ex.Message}"); } catch { }
                return new SmartHomeProviderState
                {
                    ProviderId = ProviderId,
                    DisplayName = DisplayName,
                    Descriptor = refreshTokenRejected ? BuildDescriptor(authExpired: true) : descriptor,
                    SavedSettings = GetSavedSettings(),
                    Devices = LastKnownDevices,
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

            if (string.IsNullOrWhiteSpace(_settings.RefreshToken))
            {
                return new SmartHomeActionResult { Ok = false, Message = "Ring account is not linked yet." };
            }

            try
            {
                using var client = new RingRestClient(_settings.RefreshToken);
                await client.InitializeAsync(cancellationToken).ConfigureAwait(false);
                await PersistRefreshTokenIfChangedAsync(client.RefreshToken).ConfigureAwait(false);

                if (string.Equals(request.CapabilityInstance, "cameraLight", StringComparison.OrdinalIgnoreCase) &&
                    TryReadBoolean(request.Value, out var lightValue))
                {
                    await client.SetCameraLightAsync(request.DeviceId, lightValue, cancellationToken).ConfigureAwait(false);
                    return new SmartHomeActionResult
                    {
                        Ok = true,
                        Message = lightValue ? "Ring light turned on." : "Ring light turned off.",
                    };
                }

                if (string.Equals(request.CapabilityInstance, "cameraSiren", StringComparison.OrdinalIgnoreCase) &&
                    TryReadBoolean(request.Value, out var sirenValue))
                {
                    await client.SetCameraSirenAsync(request.DeviceId, sirenValue, cancellationToken).ConfigureAwait(false);
                    return new SmartHomeActionResult
                    {
                        Ok = true,
                        Message = sirenValue ? "Ring siren activated." : "Ring siren stopped.",
                    };
                }

                if (string.Equals(request.CapabilityInstance, "chimeVolume", StringComparison.OrdinalIgnoreCase) &&
                    TryReadInt(request.Value, out var volume))
                {
                    await client.SetChimeVolumeAsync(request.DeviceId, Math.Clamp(volume, 0, 11), cancellationToken).ConfigureAwait(false);
                    return new SmartHomeActionResult
                    {
                        Ok = true,
                        Message = "Ring chime volume updated.",
                    };
                }

                return new SmartHomeActionResult
                {
                    Ok = false,
                    Message = "That Ring device action is not supported yet.",
                };
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

        public async Task<RingLiveSessionStartResult> StartLiveSessionAsync(RingLiveSessionStartRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(_settings.RefreshToken))
            {
                return new RingLiveSessionStartResult
                {
                    Ok = false,
                    Message = "Ring account is not linked yet.",
                };
            }

            if (string.IsNullOrWhiteSpace(request.DeviceId) || string.IsNullOrWhiteSpace(request.OfferSdp))
            {
                return new RingLiveSessionStartResult
                {
                    Ok = false,
                    Message = "Ring live session request is incomplete.",
                };
            }

            try
            {
                using var client = new RingRestClient(_settings.RefreshToken);
                await client.InitializeAsync(cancellationToken).ConfigureAwait(false);
                await PersistRefreshTokenIfChangedAsync(client.RefreshToken).ConfigureAwait(false);

                var result = await client.StartSimpleWebRtcSessionAsync(request.DeviceId, request.OfferSdp, cancellationToken).ConfigureAwait(false);
                return new RingLiveSessionStartResult
                {
                    Ok = true,
                    Message = "Ring live session started.",
                    SessionId = result.SessionId,
                    AnswerSdp = result.AnswerSdp,
                };
            }
            catch (Exception ex)
            {
                return new RingLiveSessionStartResult
                {
                    Ok = false,
                    Message = ex.Message,
                };
            }
        }

        public async Task<RingLiveSessionStopResult> StopLiveSessionAsync(string sessionId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(_settings.RefreshToken))
            {
                return new RingLiveSessionStopResult
                {
                    Ok = false,
                    Message = "Ring account is not linked yet.",
                };
            }

            if (string.IsNullOrWhiteSpace(sessionId))
            {
                return new RingLiveSessionStopResult
                {
                    Ok = true,
                    Message = "Ring live session already closed.",
                };
            }

            try
            {
                using var client = new RingRestClient(_settings.RefreshToken);
                await client.InitializeAsync(cancellationToken).ConfigureAwait(false);
                await PersistRefreshTokenIfChangedAsync(client.RefreshToken).ConfigureAwait(false);

                await client.StopSimpleWebRtcSessionAsync(sessionId, cancellationToken).ConfigureAwait(false);
                return new RingLiveSessionStopResult
                {
                    Ok = true,
                    Message = "Ring live session stopped.",
                };
            }
            catch (Exception ex)
            {
                return new RingLiveSessionStopResult
                {
                    Ok = false,
                    Message = ex.Message,
                };
            }
        }

        public async Task<RingLiveSessionSpeakerResult> ActivateLiveSessionSpeakerAsync(string sessionId, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                return new RingLiveSessionSpeakerResult
                {
                    Ok = false,
                    Message = "Ring live session id is missing.",
                };
            }

            try
            {
                using var client = new RingRestClient(_settings.RefreshToken);
                await client.InitializeAsync(cancellationToken).ConfigureAwait(false);
                await PersistRefreshTokenIfChangedAsync(client.RefreshToken).ConfigureAwait(false);
                await client.ActivateSimpleWebRtcCameraSpeakerAsync(sessionId, cancellationToken).ConfigureAwait(false);
                return new RingLiveSessionSpeakerResult
                {
                    Ok = true,
                    SessionId = sessionId,
                    Message = "Ring camera speaker is active.",
                };
            }
            catch (Exception ex)
            {
                AppLogger.LogWarning($"[SmartHome][RingLive] Speaker activation failed for session '{sessionId}': {ex.Message}");
                return new RingLiveSessionSpeakerResult
                {
                    Ok = false,
                    SessionId = sessionId,
                    Message = ex.Message,
                };
            }
        }

        public static async Task<RingAuthenticationResult> AuthenticateAsync(string email, string password, string twoFactorCode, string pendingHardwareId, CancellationToken cancellationToken)
        {
            using var client = new RingRestClient(email, password, pendingHardwareId);

            try
            {
                var auth = await client.AuthenticateAsync(twoFactorCode, cancellationToken).ConfigureAwait(false);
                return new RingAuthenticationResult
                {
                    Ok = true,
                    RefreshToken = auth.RefreshToken,
                    Message = "Ring account linked.",
                    PendingHardwareId = string.Empty,
                };
            }
            catch (RingTwoFactorRequiredException ex)
            {
                return new RingAuthenticationResult
                {
                    Ok = false,
                    RequiresTwoFactor = true,
                    Message = ex.Message,
                    PendingHardwareId = client.HardwareId,
                };
            }
        }

        private SmartHomeProviderFormState GetSavedSettings()
        {
            return new SmartHomeProviderFormState
            {
                Enabled = _settings.Enabled,
                RefreshToken = _settings.RefreshToken,
            };
        }

        private SmartHomeProviderDescriptor BuildDescriptor(bool authExpired = false)
        {
            var hasRefreshToken = !string.IsNullOrWhiteSpace(_settings.RefreshToken);

            if (authExpired)
            {
                return new SmartHomeProviderDescriptor
                {
                    ProviderId = ProviderId,
                    DisplayName = DisplayName,
                    Status = "Authentication required",
                    IsConfigured = false,
                    RequiredFields = new[] { "refresh_token" },
                    ConfiguredFields = hasRefreshToken ? new[] { "refresh_token" } : Array.Empty<string>(),
                    Detail = "The saved Ring token expired or was revoked. Sign in to Ring again to restore cameras, doorbells, and live view.",
                };
            }

            return new SmartHomeProviderDescriptor
            {
                ProviderId = ProviderId,
                DisplayName = DisplayName,
                Status = hasRefreshToken ? "Connected" : "Not connected",
                IsConfigured = hasRefreshToken,
                RequiredFields = new[] { "refresh_token" },
                ConfiguredFields = hasRefreshToken ? new[] { "refresh_token" } : Array.Empty<string>(),
                Detail = hasRefreshToken
                    ? "Ring account linked. Atlas will discover account devices and use the saved refresh token automatically."
                    : "Sign in to Ring or paste a refresh token, then Atlas will discover your Ring account devices.",
            };
        }

        private static bool IsRefreshTokenRejected(Exception exception)
        {
            return exception.Message.Contains("Ring rejected the saved refresh token", StringComparison.OrdinalIgnoreCase);
        }

        private static async Task PersistRefreshTokenIfChangedAsync(string refreshedToken)
        {
            if (string.IsNullOrWhiteSpace(refreshedToken))
                return;

            var settings = SettingsStore.Current;
            if (string.Equals(settings.SmartHome.Ring.RefreshToken, refreshedToken, StringComparison.Ordinal))
                return;

            settings.SmartHome.Ring.RefreshToken = refreshedToken;
            SettingsStore.Save(settings);
            await Task.CompletedTask.ConfigureAwait(false);
        }

        private static IReadOnlyList<SmartHomeDevice> BuildDevices(IReadOnlyDictionary<string, string> locations, RingDevicesResponse ringDevices)
        {
            var devices = new List<SmartHomeDevice>();

            foreach (var camera in ringDevices.GetAllCameras())
            {
                var capabilities = new List<SmartHomeCapability>();
                if (!string.IsNullOrWhiteSpace(camera.LedStatus))
                {
                    capabilities.Add(BuildSwitchCapability(
                        "cameraLight",
                        string.Equals(camera.LedStatus, "on", StringComparison.OrdinalIgnoreCase)));
                }

                if (camera.SirenSecondsRemaining.HasValue)
                {
                    capabilities.Add(BuildSwitchCapability("cameraSiren", camera.SirenSecondsRemaining.Value > 0));
                }

                var previewVideoUrl = camera.PreviewVideoUrl;
                var previewImageUrl = camera.PreviewImageUrl;
                bool? isOnline = camera.ConnectionStatus;
                if (isOnline == false && (!string.IsNullOrWhiteSpace(previewImageUrl) || !string.IsNullOrWhiteSpace(camera.ExternalUrl)))
                {
                    // Ring often reports stale connection status even when the device can still open via live view/provider fallback.
                    isOnline = null;
                }

                devices.Add(new SmartHomeDevice
                {
                    DeviceId = camera.Id,
                    Name = AppendLocation(camera.Description, locations.TryGetValue(camera.LocationId, out var locationName) ? locationName : string.Empty),
                    Sku = camera.Kind,
                    DeviceType = camera.Kind,
                    IsOnline = isOnline,
                    PreviewImageUrl = previewImageUrl,
                    PreviewVideoUrl = previewVideoUrl,
                    ExternalUrl = camera.ExternalUrl,
                    Capabilities = capabilities,
                });
            }

            foreach (var chime in ringDevices.Chimes)
            {
                var capabilities = new List<SmartHomeCapability>();
                if (chime.Volume.HasValue)
                {
                    capabilities.Add(new SmartHomeCapability
                    {
                        Type = "devices.capabilities.range",
                        Instance = "chimeVolume",
                        DataType = "integer",
                        Unit = string.Empty,
                        Min = 0,
                        Max = 11,
                        HasState = true,
                        StateValue = JsonSerializer.SerializeToElement(chime.Volume.Value),
                    });
                }

                devices.Add(new SmartHomeDevice
                {
                    DeviceId = chime.Id,
                    Name = AppendLocation(chime.Description, locations.TryGetValue(chime.LocationId, out var locationName) ? locationName : string.Empty),
                    Sku = chime.Kind,
                    DeviceType = "chime",
                    IsOnline = chime.ConnectionStatus,
                    Capabilities = capabilities,
                });
            }

            foreach (var station in ringDevices.BaseStations)
            {
                devices.Add(new SmartHomeDevice
                {
                    DeviceId = station.Id,
                    Name = AppendLocation(station.Description, locations.TryGetValue(station.LocationId, out var locationName) ? locationName : string.Empty),
                    Sku = station.Kind,
                    DeviceType = "alarm_base_station",
                    IsOnline = station.ConnectionStatus,
                    Capabilities = Array.Empty<SmartHomeCapability>(),
                });
            }

            foreach (var intercom in ringDevices.Intercoms)
            {
                devices.Add(new SmartHomeDevice
                {
                    DeviceId = intercom.Id,
                    Name = AppendLocation(intercom.Description, locations.TryGetValue(intercom.LocationId, out var locationName) ? locationName : string.Empty),
                    Sku = intercom.Kind,
                    DeviceType = intercom.Kind,
                    IsOnline = intercom.ConnectionStatus,
                    Capabilities = Array.Empty<SmartHomeCapability>(),
                });
            }

            return devices;
        }

        private static SmartHomeCapability BuildSwitchCapability(string instance, bool state)
        {
            return new SmartHomeCapability
            {
                Type = "devices.capabilities.on_off",
                Instance = instance,
                DataType = "boolean",
                Unit = string.Empty,
                HasState = true,
                StateValue = JsonSerializer.SerializeToElement(state),
                Options = new[]
                {
                    new SmartHomeCapabilityOption { Name = "on", Value = JsonSerializer.SerializeToElement(true) },
                    new SmartHomeCapabilityOption { Name = "off", Value = JsonSerializer.SerializeToElement(false) },
                },
            };
        }

        private static string AppendLocation(string name, string location)
        {
            if (string.IsNullOrWhiteSpace(location))
                return name;

            return $"{name} ({location})";
        }

        private static bool TryReadBoolean(JsonElement value, out bool result)
        {
            result = false;
            switch (value.ValueKind)
            {
                case JsonValueKind.True:
                    result = true;
                    return true;
                case JsonValueKind.False:
                    return true;
                case JsonValueKind.String:
                    return bool.TryParse(value.GetString(), out result);
                case JsonValueKind.Number when value.TryGetInt32(out var numericValue):
                    result = numericValue != 0;
                    return true;
                default:
                    return false;
            }
        }

        private static bool TryReadInt(JsonElement value, out int result)
        {
            result = 0;
            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out result))
                return true;

            if (value.ValueKind == JsonValueKind.String)
                return int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out result);

            return false;
        }

        private sealed class RingRestClient : IDisposable
        {
            private readonly RingAuthConfig _authConfig;
            private readonly string? _email;
            private readonly string? _password;
            private string? _accessToken;

            public RingRestClient(string refreshToken)
            {
                _authConfig = RingAuthConfig.Parse(refreshToken);
            }

            public RingRestClient(string email, string password, string? hardwareId)
            {
                _email = email;
                _password = password;
                _authConfig = new RingAuthConfig
                {
                    HardwareId = string.IsNullOrWhiteSpace(hardwareId) ? CreateHardwareId() : hardwareId,
                };
            }

            public string RefreshToken => _authConfig.Serialize();
            public string HardwareId => _authConfig.HardwareId;

            public async Task InitializeAsync(CancellationToken cancellationToken)
            {
                var auth = await AuthenticateAsync(string.Empty, cancellationToken).ConfigureAwait(false);
                _accessToken = auth.AccessToken;
                try
                {
                    await CreateSessionAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (IsNonCriticalSessionBootstrapFailure(ex))
                {
                    // Ring can reject session bootstrap with 406 while the account token is still valid.
                    // Device discovery and snapshot rendering should continue instead of blanking cameras.
                    try { AppLogger.LogWarning($"[SmartHome][Ring] Session bootstrap skipped: {ex.Message}"); } catch { }
                }
            }

            public async Task<RingAuthResponse> AuthenticateAsync(string twoFactorCode, CancellationToken cancellationToken)
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, "https://oauth.ring.com/oauth/token");
                request.Headers.TryAddWithoutValidation("2fa-support", "true");
                request.Headers.TryAddWithoutValidation("2fa-code", twoFactorCode ?? string.Empty);
                request.Headers.TryAddWithoutValidation("hardware_id", _authConfig.HardwareId);
                request.Headers.TryAddWithoutValidation("User-Agent", "android:com.ringapp");

                var payload = new Dictionary<string, object?>
                {
                    ["client_id"] = "ring_official_android",
                    ["scope"] = "client",
                };

                if (!string.IsNullOrWhiteSpace(_authConfig.RawRefreshToken) && string.IsNullOrWhiteSpace(twoFactorCode))
                {
                    payload["grant_type"] = "refresh_token";
                    payload["refresh_token"] = _authConfig.RawRefreshToken;
                }
                else if (!string.IsNullOrWhiteSpace(_email) && !string.IsNullOrWhiteSpace(_password))
                {
                    payload["grant_type"] = "password";
                    payload["username"] = _email;
                    payload["password"] = _password;
                }
                else
                {
                    throw new InvalidOperationException("Ring authentication data is incomplete.");
                }

                request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

                using var response = await HttpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
                var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

                if (response.StatusCode == HttpStatusCode.PreconditionFailed)
                {
                    using var document = JsonDocument.Parse(text);
                    throw new RingTwoFactorRequiredException(BuildTwoFactorPrompt(document.RootElement));
                }

                if (!response.IsSuccessStatusCode)
                {
                    throw new InvalidOperationException(BuildRingAuthError(text, usingRefreshToken: payload.ContainsKey("refresh_token")));
                }

                using var authDocument = JsonDocument.Parse(text);
                var accessToken = GetString(authDocument.RootElement, "access_token");
                var rawRefreshToken = GetString(authDocument.RootElement, "refresh_token");
                if (string.IsNullOrWhiteSpace(accessToken) || string.IsNullOrWhiteSpace(rawRefreshToken))
                    throw new InvalidOperationException("Ring did not return valid auth tokens.");

                _authConfig.RawRefreshToken = rawRefreshToken;
                _accessToken = accessToken;

                return new RingAuthResponse
                {
                    AccessToken = accessToken,
                    RefreshToken = _authConfig.Serialize(),
                };
            }

            public async Task<IReadOnlyDictionary<string, string>> GetLocationsAsync(CancellationToken cancellationToken)
            {
                using var document = await SendJsonAsync(HttpMethod.Get, "https://api.ring.com/devices/v1/locations", null, cancellationToken).ConfigureAwait(false);
                var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                if (!document.RootElement.TryGetProperty("user_locations", out var locationsElement) || locationsElement.ValueKind != JsonValueKind.Array)
                    return result;

                foreach (var locationElement in locationsElement.EnumerateArray())
                {
                    var locationId = FirstNonEmpty(GetString(locationElement, "location_id"), GetString(locationElement, "id"));
                    if (string.IsNullOrWhiteSpace(locationId))
                        continue;

                    var locationName = FirstNonEmpty(
                        GetString(locationElement, "name"),
                        GetString(locationElement, "description"),
                        GetNestedString(locationElement, "location_details", "name"),
                        GetNestedString(locationElement, "address", "address1"),
                        locationId);
                    result[locationId] = locationName;
                }

                return result;
            }

            public async Task<RingDevicesResponse> GetRingDevicesAsync(CancellationToken cancellationToken)
            {
                using var document = await SendJsonAsync(HttpMethod.Get, "https://api.ring.com/clients_api/ring_devices", null, cancellationToken).ConfigureAwait(false);
                return RingDevicesResponse.Parse(document.RootElement);
            }

            public async Task SetCameraLightAsync(string cameraId, bool on, CancellationToken cancellationToken)
            {
                await SendJsonAsync(HttpMethod.Put, $"https://api.ring.com/clients_api/doorbots/{cameraId}/floodlight_light_{(on ? "on" : "off")}", null, cancellationToken).ConfigureAwait(false);
            }

            public async Task SetCameraSirenAsync(string cameraId, bool on, CancellationToken cancellationToken)
            {
                await SendJsonAsync(HttpMethod.Put, $"https://api.ring.com/clients_api/doorbots/{cameraId}/siren_{(on ? "on" : "off")}", null, cancellationToken).ConfigureAwait(false);
            }

            public async Task SetChimeVolumeAsync(string chimeId, int volume, CancellationToken cancellationToken)
            {
                var body = new { chime = new { settings = new { volume } } };
                await SendJsonAsync(HttpMethod.Put, $"https://api.ring.com/clients_api/chimes/{chimeId}", body, cancellationToken).ConfigureAwait(false);
            }

            public async Task<RingSimpleLiveSession> StartSimpleWebRtcSessionAsync(string deviceId, string offerSdp, CancellationToken cancellationToken)
            {
                var normalizedOffer = NormalizeSimpleWebRtcOffer(offerSdp);
                var legacySanitizedOffer = SanitizeSimpleWebRtcOffer(offerSdp);
                var strictSanitizedOffer = BuildStrictSimpleWebRtcOffer(offerSdp);
                var offers = new[]
                    {
                        new { Name = "normalized", Sdp = normalizedOffer },
                        new { Name = "legacy-sanitized", Sdp = legacySanitizedOffer },
                        new { Name = "strict-sanitized", Sdp = strictSanitizedOffer },
                    }
                    .Where(static candidate => !string.IsNullOrWhiteSpace(candidate.Sdp))
                    .DistinctBy(candidate => candidate.Sdp)
                    .ToArray();

                Exception? lastError = null;

                foreach (var candidate in offers)
                {
                    if (string.IsNullOrWhiteSpace(candidate.Sdp))
                        continue;

                    var sessionId = Guid.NewGuid().ToString("D");
                    AppLogger.LogInfo($"[SmartHome][RingLive] Starting direct live session for '{deviceId}' using {candidate.Name} offer (length {candidate.Sdp.Length}).");

                    var body = new
                    {
                        session_id = sessionId,
                        device_id = deviceId,
                        sdp = candidate.Sdp,
                        protocol = "webrtc",
                    };

                    try
                    {
                        using var document = await SendJsonAsync(HttpMethod.Post, "https://api.ring.com/integrations/v1/liveview/start", body, cancellationToken).ConfigureAwait(false);
                        var answerSdp = GetString(document.RootElement, "sdp");
                        if (string.IsNullOrWhiteSpace(answerSdp))
                            throw new InvalidOperationException("Ring did not return a valid live-session answer.");

                        AppLogger.LogInfo($"[SmartHome][RingLive] Direct live session started for '{deviceId}' with session '{sessionId}' via {candidate.Name} offer (answer length {answerSdp.Length}).");

                        return new RingSimpleLiveSession
                        {
                            SessionId = sessionId,
                            AnswerSdp = answerSdp,
                        };
                    }
                    catch (Exception ex) when (ShouldRetrySimpleWebRtcOffer(ex, candidate.Name != offers[^1].Name))
                    {
                        lastError = ex;
                        AppLogger.LogWarning($"[SmartHome][RingLive] Ring rejected the {candidate.Name} offer for '{deviceId}'. Retrying with the next offer variant. {ex.Message}");
                    }
                    catch (Exception ex)
                    {
                        lastError = ex;
                        break;
                    }
                }

                throw lastError ?? new InvalidOperationException("Ring rejected the live-session request.");
            }

            public async Task StopSimpleWebRtcSessionAsync(string sessionId, CancellationToken cancellationToken)
            {
                var body = new
                {
                    session_id = sessionId,
                };

                await SendJsonAsync(HttpMethod.Post, "https://api.ring.com/integrations/v1/liveview/end", body, cancellationToken).ConfigureAwait(false);
            }

            public async Task ActivateSimpleWebRtcCameraSpeakerAsync(string sessionId, CancellationToken cancellationToken)
            {
                var body = new
                {
                    session_id = sessionId,
                    actions = new[] { "turn_off_stealth_mode" },
                };

                await SendJsonAsync(new HttpMethod("PATCH"), "https://api.ring.com/integrations/v1/liveview/options", body, cancellationToken).ConfigureAwait(false);
            }

            private async Task CreateSessionAsync(CancellationToken cancellationToken)
            {
                var body = new
                {
                    device = new
                    {
                        hardware_id = _authConfig.HardwareId,
                        metadata = new { api_version = 11, device_model = "ring-client-api" },
                        os = "android",
                    }
                };

                await SendJsonAsync(HttpMethod.Post, "https://api.ring.com/clients_api/session", body, cancellationToken).ConfigureAwait(false);
            }

            private async Task<JsonDocument> SendJsonAsync(HttpMethod method, string url, object? body, CancellationToken cancellationToken)
            {
                using var request = new HttpRequestMessage(method, url);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken ?? string.Empty);
                request.Headers.TryAddWithoutValidation("hardware_id", _authConfig.HardwareId);
                request.Headers.TryAddWithoutValidation("User-Agent", "android:com.ringapp");
                request.Headers.TryAddWithoutValidation("Accept", "application/json");

                if (body != null)
                    request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

                using var response = await HttpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
                var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    AppLogger.LogWarning($"[SmartHome][RingLive] Ring request failed: {url} -> {(int)response.StatusCode} {response.StatusCode}. {text}");
                    throw new InvalidOperationException(string.IsNullOrWhiteSpace(text)
                        ? $"Ring request failed with {(int)response.StatusCode}."
                        : text);
                }

                return string.IsNullOrWhiteSpace(text) ? JsonDocument.Parse("{}") : JsonDocument.Parse(text);
            }

            private static string NormalizeSimpleWebRtcOffer(string sdp)
            {
                if (string.IsNullOrWhiteSpace(sdp))
                    return string.Empty;

                var lines = sdp
                    .Replace("\r\n", "\n", StringComparison.Ordinal)
                    .Replace('\r', '\n')
                    .Split('\n', StringSplitOptions.RemoveEmptyEntries);

                return lines.Length == 0 ? string.Empty : string.Join("\r\n", lines) + "\r\n";
            }

            private static string SanitizeSimpleWebRtcOffer(string sdp)
            {
                var normalizedSdp = NormalizeSimpleWebRtcOffer(sdp);
                if (string.IsNullOrWhiteSpace(normalizedSdp))
                    return string.Empty;

                var lines = normalizedSdp
                    .Replace("\r\n", "\n", StringComparison.Ordinal)
                    .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                    .Where(static line =>
                        !line.StartsWith("a=candidate:", StringComparison.OrdinalIgnoreCase) &&
                        !line.StartsWith("a=end-of-candidates", StringComparison.OrdinalIgnoreCase) &&
                        !line.StartsWith("a=ice-options:", StringComparison.OrdinalIgnoreCase) &&
                        !line.StartsWith("a=extmap-allow-mixed", StringComparison.OrdinalIgnoreCase))
                    .ToArray();

                return string.Join("\r\n", lines) + "\r\n";
            }

            private static string BuildStrictSimpleWebRtcOffer(string sdp)
            {
                var normalizedSdp = NormalizeSimpleWebRtcOffer(sdp);
                if (string.IsNullOrWhiteSpace(normalizedSdp))
                    return string.Empty;

                var sections = normalizedSdp
                    .Replace("\r\n", "\n", StringComparison.Ordinal)
                    .Split("\nm=", StringSplitOptions.None);

                if (sections.Length == 0)
                    return string.Empty;

                var strictSections = new List<string>();
                var sessionLines = sections[0]
                    .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                    .Where(static line =>
                        !line.StartsWith("a=group:", StringComparison.OrdinalIgnoreCase) &&
                        !line.StartsWith("a=msid-semantic:", StringComparison.OrdinalIgnoreCase) &&
                        !line.StartsWith("a=extmap-allow-mixed", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                strictSections.Add(string.Join("\r\n", sessionLines));

                for (var i = 1; i < sections.Length; i++)
                {
                    var sectionText = "m=" + sections[i];
                    var sectionLines = sectionText.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                    if (sectionLines.Length == 0)
                        continue;

                    var mediaLine = sectionLines[0].Trim();
                    var isAudio = mediaLine.StartsWith("m=audio", StringComparison.OrdinalIgnoreCase);
                    var isVideo = mediaLine.StartsWith("m=video", StringComparison.OrdinalIgnoreCase);
                    if (!isAudio && !isVideo)
                        continue;

                    var preferredPayloads = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var line in sectionLines.Skip(1))
                    {
                        var trimmed = line.Trim();
                        if (isAudio && trimmed.StartsWith("a=rtpmap:", StringComparison.OrdinalIgnoreCase) && trimmed.Contains(" opus/", StringComparison.OrdinalIgnoreCase))
                        {
                            preferredPayloads.Add(GetPayloadType(trimmed));
                        }

                        if (isVideo && trimmed.StartsWith("a=rtpmap:", StringComparison.OrdinalIgnoreCase) && trimmed.Contains(" H264/", StringComparison.OrdinalIgnoreCase))
                        {
                            preferredPayloads.Add(GetPayloadType(trimmed));
                        }
                    }

                    if (isAudio && preferredPayloads.Count == 0)
                    {
                        foreach (var line in sectionLines.Skip(1))
                        {
                            var trimmed = line.Trim();
                            if (trimmed.StartsWith("a=rtpmap:", StringComparison.OrdinalIgnoreCase) &&
                                (trimmed.Contains(" PCMU/", StringComparison.OrdinalIgnoreCase) || trimmed.Contains(" PCMA/", StringComparison.OrdinalIgnoreCase)))
                            {
                                preferredPayloads.Add(GetPayloadType(trimmed));
                            }
                        }
                    }

                    foreach (var line in sectionLines.Skip(1))
                    {
                        var trimmed = line.Trim();
                        if (trimmed.StartsWith("a=fmtp:", StringComparison.OrdinalIgnoreCase))
                        {
                            var aptMatch = Regex.Match(trimmed, @"\bapt=(\d+)", RegexOptions.IgnoreCase);
                            if (aptMatch.Success && preferredPayloads.Contains(aptMatch.Groups[1].Value))
                            {
                                preferredPayloads.Add(GetPayloadType(trimmed));
                            }
                        }
                    }

                    var rebuiltSection = new List<string>
                    {
                        RebuildMediaLine(mediaLine, preferredPayloads),
                    };

                    foreach (var rawLine in sectionLines.Skip(1))
                    {
                        var trimmed = rawLine.Trim();
                        if (string.IsNullOrWhiteSpace(trimmed) ||
                            trimmed.StartsWith("a=candidate:", StringComparison.OrdinalIgnoreCase) ||
                            trimmed.StartsWith("a=end-of-candidates", StringComparison.OrdinalIgnoreCase) ||
                            trimmed.StartsWith("a=ice-options:", StringComparison.OrdinalIgnoreCase) ||
                            trimmed.StartsWith("a=extmap:", StringComparison.OrdinalIgnoreCase) ||
                            trimmed.StartsWith("a=extmap-allow-mixed", StringComparison.OrdinalIgnoreCase) ||
                            trimmed.StartsWith("a=msid:", StringComparison.OrdinalIgnoreCase) ||
                            trimmed.StartsWith("a=ssrc:", StringComparison.OrdinalIgnoreCase) ||
                            trimmed.StartsWith("a=ssrc-group:", StringComparison.OrdinalIgnoreCase) ||
                            trimmed.StartsWith("a=rtcp-rsize", StringComparison.OrdinalIgnoreCase) ||
                            trimmed.StartsWith("a=rtcp-mux-only", StringComparison.OrdinalIgnoreCase) ||
                            trimmed.StartsWith("a=bundle-only", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        if (trimmed.StartsWith("a=rtpmap:", StringComparison.OrdinalIgnoreCase) ||
                            trimmed.StartsWith("a=fmtp:", StringComparison.OrdinalIgnoreCase) ||
                            trimmed.StartsWith("a=rtcp-fb:", StringComparison.OrdinalIgnoreCase))
                        {
                            var payloadType = GetPayloadType(trimmed);
                            if (preferredPayloads.Count > 0 && !preferredPayloads.Contains(payloadType))
                                continue;
                        }

                        rebuiltSection.Add(trimmed);
                    }

                    strictSections.Add(string.Join("\r\n", rebuiltSection));
                }

                return string.Join("\r\n", strictSections.Where(static section => !string.IsNullOrWhiteSpace(section))) + "\r\n";
            }

            private static string RebuildMediaLine(string mediaLine, HashSet<string> preferredPayloads)
            {
                if (preferredPayloads.Count == 0)
                    return mediaLine;

                var parts = mediaLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length <= 3)
                    return mediaLine;

                var rebuilt = parts.Take(3).Concat(parts.Skip(3).Where(preferredPayloads.Contains)).ToArray();
                return rebuilt.Length > 3 ? string.Join(" ", rebuilt) : mediaLine;
            }

            private static string GetPayloadType(string attributeLine)
            {
                var separatorIndex = attributeLine.IndexOf(':');
                if (separatorIndex < 0 || separatorIndex >= attributeLine.Length - 1)
                    return string.Empty;

                var value = attributeLine[(separatorIndex + 1)..];
                var endIndex = value.IndexOfAny(new[] { ' ', '\t' });
                return (endIndex >= 0 ? value[..endIndex] : value).Trim();
            }

            private static bool ShouldRetrySimpleWebRtcOffer(Exception exception, bool hasMoreVariants)
            {
                if (!hasMoreVariants)
                    return false;

                var message = exception.Message ?? string.Empty;
                return message.Contains("failed decoding request", StringComparison.OrdinalIgnoreCase) ||
                       message.Contains("400", StringComparison.OrdinalIgnoreCase) ||
                       message.Contains("BadRequest", StringComparison.OrdinalIgnoreCase);
            }

            public void Dispose()
            {
            }

            private static string BuildTwoFactorPrompt(JsonElement responseData)
            {
                var phone = GetString(responseData, "phone");
                var state = GetString(responseData, "tsv_state");
                if (!string.IsNullOrWhiteSpace(state) && string.Equals(state, "totp", StringComparison.OrdinalIgnoreCase))
                    return "Enter the Ring code from your authenticator app.";

                if (!string.IsNullOrWhiteSpace(phone) && !string.IsNullOrWhiteSpace(state))
                    return $"Enter the Ring code sent to {phone} via {state}.";

                return "Enter the Ring verification code from SMS, email, or your authenticator app.";
            }

            private static string BuildRingAuthError(string text, bool usingRefreshToken)
            {
                try
                {
                    using var document = JsonDocument.Parse(text);
                    var error = GetString(document.RootElement, "error");
                    var description = GetString(document.RootElement, "error_description");

                    if (description.Equals("too many requests from dependency service", StringComparison.OrdinalIgnoreCase))
                        return "Ring rate-limited the login flow after too many codes. Wait 10 minutes and try again.";

                    if (!string.IsNullOrWhiteSpace(description))
                        return usingRefreshToken ? $"Ring rejected the saved refresh token. {description}" : $"Ring login failed. {description}";

                    if (!string.IsNullOrWhiteSpace(error))
                        return usingRefreshToken ? $"Ring rejected the saved refresh token ({error})." : $"Ring login failed ({error}).";
                }
                catch
                {
                }

                return usingRefreshToken ? "Ring rejected the saved refresh token." : "Ring login failed.";
            }

            private static bool IsNonCriticalSessionBootstrapFailure(Exception exception)
            {
                return exception.Message.Contains("406", StringComparison.OrdinalIgnoreCase) ||
                       exception.Message.Contains("Not Acceptable", StringComparison.OrdinalIgnoreCase);
            }

            private static string GetString(JsonElement element, string propertyName)
            {
                if (!element.TryGetProperty(propertyName, out var property))
                    return string.Empty;

                return property.ValueKind == JsonValueKind.String ? property.GetString() ?? string.Empty : property.ToString();
            }

            private static string GetNestedString(JsonElement element, string parentName, string childName)
            {
                if (!element.TryGetProperty(parentName, out var parent) || parent.ValueKind != JsonValueKind.Object)
                    return string.Empty;

                return GetString(parent, childName);
            }

            private static string CreateHardwareId()
            {
                Span<byte> bytes = stackalloc byte[16];
                RandomNumberGenerator.Fill(bytes);
                return new Guid(bytes).ToString("D");
            }
        }

        private sealed class RingAuthConfig
        {
            public string RawRefreshToken { get; set; } = string.Empty;
            public string HardwareId { get; set; } = Guid.NewGuid().ToString("D");

            public static RingAuthConfig Parse(string value)
            {
                if (string.IsNullOrWhiteSpace(value))
                    return new RingAuthConfig();

                try
                {
                    var json = Encoding.UTF8.GetString(Convert.FromBase64String(value));
                    using var document = JsonDocument.Parse(json);
                    var refreshToken = GetString(document.RootElement, "rt");
                    if (!string.IsNullOrWhiteSpace(refreshToken))
                    {
                        return new RingAuthConfig
                        {
                            RawRefreshToken = refreshToken,
                            HardwareId = FirstNonEmpty(GetString(document.RootElement, "hid"), Guid.NewGuid().ToString("D")),
                        };
                    }
                }
                catch
                {
                }

                return new RingAuthConfig
                {
                    RawRefreshToken = value,
                    HardwareId = Guid.NewGuid().ToString("D"),
                };
            }

            public string Serialize()
            {
                if (string.IsNullOrWhiteSpace(RawRefreshToken))
                    return string.Empty;

                var json = JsonSerializer.Serialize(new { rt = RawRefreshToken, hid = HardwareId });
                return Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
            }

            private static string GetString(JsonElement element, string propertyName)
            {
                if (!element.TryGetProperty(propertyName, out var property))
                    return string.Empty;

                return property.ValueKind == JsonValueKind.String ? property.GetString() ?? string.Empty : property.ToString();
            }
        }

        private sealed class RingDevicesResponse
        {
            public List<RingCameraDevice> Doorbots { get; } = new();
            public List<RingCameraDevice> AuthorizedDoorbots { get; } = new();
            public List<RingCameraDevice> StickupCams { get; } = new();
            public List<RingCameraDevice> AdditionalCameras { get; } = new();
            public List<RingChimeDevice> Chimes { get; } = new();
            public List<RingSimpleDevice> BaseStations { get; } = new();
            public List<RingSimpleDevice> Intercoms { get; } = new();

            public IEnumerable<RingCameraDevice> GetAllCameras()
            {
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var item in EnumerateCameraLists())
                {
                    if (seen.Add(GetCameraIdentityKey(item)))
                        yield return item;
                }
            }

            public static RingDevicesResponse Parse(JsonElement root)
            {
                var result = new RingDevicesResponse();
                AddRange(root, "doorbots", result.Doorbots, RingCameraDevice.Parse);
                AddRange(root, "authorized_doorbots", result.AuthorizedDoorbots, RingCameraDevice.Parse);
                AddRange(root, "stickup_cams", result.StickupCams, RingCameraDevice.Parse);
                AddRange(root, "chimes", result.Chimes, RingChimeDevice.Parse);
                AddRange(root, "base_stations", result.BaseStations, RingSimpleDevice.Parse);

                foreach (var property in root.EnumerateObject())
                {
                    if (property.Value.ValueKind != JsonValueKind.Array)
                        continue;

                    if (string.Equals(property.Name, "doorbots", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(property.Name, "authorized_doorbots", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(property.Name, "stickup_cams", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(property.Name, "chimes", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(property.Name, "base_stations", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(property.Name, "other", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var containsCameraLikeItems = false;
                    foreach (var item in property.Value.EnumerateArray())
                    {
                        if (!IsCameraLike(item, GetDeviceKind(item)))
                            continue;

                        containsCameraLikeItems = true;
                        break;
                    }

                    if (!LooksLikeCameraCollection(property.Name) && !containsCameraLikeItems)
                        continue;

                    foreach (var item in property.Value.EnumerateArray())
                    {
                        if (IsCameraLike(item, GetDeviceKind(item)))
                            result.AdditionalCameras.Add(RingCameraDevice.Parse(item));
                    }
                }

                if (root.TryGetProperty("other", out var othersElement) && othersElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in othersElement.EnumerateArray())
                    {
                        var kind = GetDeviceKind(item);
                        if (IsIntercomKind(kind))
                            result.Intercoms.Add(RingSimpleDevice.Parse(item));
                        else if (IsCameraLike(item, kind))
                            result.AdditionalCameras.Add(RingCameraDevice.Parse(item));
                    }
                }

                return result;
            }

            private IEnumerable<RingCameraDevice> EnumerateCameraLists()
            {
                foreach (var item in Doorbots) yield return item;
                foreach (var item in AuthorizedDoorbots) yield return item;
                foreach (var item in StickupCams) yield return item;
                foreach (var item in AdditionalCameras) yield return item;
            }

            private static string GetCameraIdentityKey(RingCameraDevice device)
            {
                return FirstNonEmpty(device.Id, $"{device.Kind}|{device.LocationId}|{device.Description}").Trim();
            }

            private static string GetDeviceKind(JsonElement element)
            {
                return FirstNonEmpty(GetString(element, "kind"), GetString(element, "deviceType"));
            }

            private static bool LooksLikeCameraCollection(string propertyName)
            {
                if (string.IsNullOrWhiteSpace(propertyName))
                    return false;

                return propertyName.IndexOf("doorbot", StringComparison.OrdinalIgnoreCase) >= 0 ||
                       propertyName.IndexOf("doorbell", StringComparison.OrdinalIgnoreCase) >= 0 ||
                       propertyName.IndexOf("stickup", StringComparison.OrdinalIgnoreCase) >= 0 ||
                       propertyName.IndexOf("camera", StringComparison.OrdinalIgnoreCase) >= 0 ||
                       propertyName.IndexOf("cam", StringComparison.OrdinalIgnoreCase) >= 0 ||
                       propertyName.IndexOf("floodlight", StringComparison.OrdinalIgnoreCase) >= 0 ||
                       propertyName.IndexOf("spotlight", StringComparison.OrdinalIgnoreCase) >= 0 ||
                       propertyName.IndexOf("peephole", StringComparison.OrdinalIgnoreCase) >= 0 ||
                       propertyName.IndexOf("onvif", StringComparison.OrdinalIgnoreCase) >= 0;
            }

            private static bool IsIntercomKind(string kind)
            {
                return kind.StartsWith("intercom_", StringComparison.OrdinalIgnoreCase);
            }

            private static bool IsCameraLike(JsonElement element, string kind)
            {
                if (IsIntercomKind(kind))
                    return false;

                if (LooksLikeCameraCollection(kind))
                    return true;

                if (HasNonEmptyProperty(element, "snapshot_image_url", "snapshot_url", "image_url", "live_view_url", "video_url", "stream_url", "live_stream_url"))
                    return true;

                var description = FirstNonEmpty(GetString(element, "description"), GetString(element, "name"));
                return description.IndexOf("camera", StringComparison.OrdinalIgnoreCase) >= 0 ||
                       description.IndexOf("doorbell", StringComparison.OrdinalIgnoreCase) >= 0 ||
                       description.IndexOf("door bot", StringComparison.OrdinalIgnoreCase) >= 0;
            }

            private static bool HasNonEmptyProperty(JsonElement element, params string[] propertyNames)
            {
                foreach (var propertyName in propertyNames)
                {
                    if (!string.IsNullOrWhiteSpace(GetString(element, propertyName)))
                        return true;
                }

                return false;
            }

            private static void AddRange<T>(JsonElement root, string propertyName, List<T> target, Func<JsonElement, T> parser)
            {
                if (!root.TryGetProperty(propertyName, out var arrayElement) || arrayElement.ValueKind != JsonValueKind.Array)
                    return;

                foreach (var item in arrayElement.EnumerateArray())
                    target.Add(parser(item));
            }

            private static string GetString(JsonElement element, string propertyName)
            {
                if (!element.TryGetProperty(propertyName, out var property))
                    return string.Empty;

                return property.ValueKind == JsonValueKind.String ? property.GetString() ?? string.Empty : property.ToString();
            }
        }

        private sealed class RingCameraDevice
        {
            public string Id { get; init; } = string.Empty;
            public string Description { get; init; } = string.Empty;
            public string Kind { get; init; } = string.Empty;
            public string LocationId { get; init; } = string.Empty;
            public bool? ConnectionStatus { get; init; }
            public string LedStatus { get; init; } = string.Empty;
            public int? SirenSecondsRemaining { get; init; }
            public string PreviewImageUrl { get; init; } = string.Empty;
            public string PreviewVideoUrl { get; init; } = string.Empty;
            public string ExternalUrl { get; init; } = string.Empty;

            public static RingCameraDevice Parse(JsonElement element)
            {
                var alerts = element.TryGetProperty("alerts", out var alertsElement) ? alertsElement : default;
                var siren = element.TryGetProperty("siren_status", out var sirenElement) ? sirenElement : default;
                var discoveredUrls = new List<string>();
                CollectUrls(element, discoveredUrls);

                return new RingCameraDevice
                {
                    Id = FirstNonEmpty(GetString(element, "id"), GetString(element, "device_id")),
                    Description = FirstNonEmpty(GetString(element, "description"), GetString(element, "name"), "Ring Camera"),
                    Kind = FirstNonEmpty(GetString(element, "kind"), "ring_camera"),
                    LocationId = GetString(element, "location_id"),
                    ConnectionStatus = GetBoolean(alerts, "connection"),
                    LedStatus = GetString(element, "led_status"),
                    SirenSecondsRemaining = GetInt(siren, "seconds_remaining"),
                    PreviewImageUrl = FirstNonEmpty(
                        GetString(element, "snapshot_image_url"),
                        GetString(element, "snapshot_url"),
                        GetString(element, "image_url"),
                        FindFirstUrl(discoveredUrls, "snapshot", ".jpg", ".jpeg", ".png", "image")),
                    PreviewVideoUrl = FirstNonEmpty(
                        GetString(element, "live_view_url"),
                        GetString(element, "video_url"),
                        GetString(element, "stream_url"),
                        GetString(element, "live_stream_url"),
                        FindFirstUrl(discoveredUrls, "liveview", ".m3u8", ".mp4", "live", "stream", "recording")),
                    ExternalUrl = FirstNonEmpty(
                        GetString(element, "live_view_url"),
                        GetString(element, "url"),
                        GetString(element, "html_url"),
                        FindFirstUrl(discoveredUrls, "liveview", "account.ring.com", "/account/dashboard", "dashboard"),
                        "https://account.ring.com/account/dashboard"),
                };
            }

            private static void CollectUrls(JsonElement element, List<string> target)
            {
                switch (element.ValueKind)
                {
                    case JsonValueKind.Object:
                        foreach (var property in element.EnumerateObject())
                            CollectUrls(property.Value, target);
                        break;
                    case JsonValueKind.Array:
                        foreach (var entry in element.EnumerateArray())
                            CollectUrls(entry, target);
                        break;
                    case JsonValueKind.String:
                        var value = element.GetString() ?? string.Empty;
                        if (Regex.IsMatch(value, @"^https?://", RegexOptions.IgnoreCase))
                        {
                            var exists = false;
                            foreach (var existing in target)
                            {
                                if (string.Equals(existing, value, StringComparison.OrdinalIgnoreCase))
                                {
                                    exists = true;
                                    break;
                                }
                            }

                            if (!exists)
                                target.Add(value);
                        }
                        break;
                }
            }

            private static string FindFirstUrl(IEnumerable<string> urls, params string[] needles)
            {
                foreach (var url in urls)
                {
                    if (needles.Length == 0)
                        return url;

                    foreach (var needle in needles)
                    {
                        if (!string.IsNullOrWhiteSpace(needle) && url.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0)
                            return url;
                    }
                }

                return string.Empty;
            }
        }

        private sealed class RingChimeDevice
        {
            public string Id { get; init; } = string.Empty;
            public string Description { get; init; } = string.Empty;
            public string Kind { get; init; } = string.Empty;
            public string LocationId { get; init; } = string.Empty;
            public bool? ConnectionStatus { get; init; }
            public int? Volume { get; init; }

            public static RingChimeDevice Parse(JsonElement element)
            {
                var settings = element.TryGetProperty("settings", out var settingsElement) ? settingsElement : default;
                var alerts = element.TryGetProperty("alerts", out var alertsElement) ? alertsElement : default;

                return new RingChimeDevice
                {
                    Id = FirstNonEmpty(GetString(element, "id"), GetString(element, "device_id")),
                    Description = FirstNonEmpty(GetString(element, "description"), GetString(element, "name"), "Ring Chime"),
                    Kind = FirstNonEmpty(GetString(element, "kind"), "ring_chime"),
                    LocationId = GetString(element, "location_id"),
                    ConnectionStatus = GetBoolean(alerts, "connection"),
                    Volume = GetInt(settings, "volume"),
                };
            }
        }

        private sealed class RingSimpleDevice
        {
            public string Id { get; init; } = string.Empty;
            public string Description { get; init; } = string.Empty;
            public string Kind { get; init; } = string.Empty;
            public string LocationId { get; init; } = string.Empty;
            public bool? ConnectionStatus { get; init; }

            public static RingSimpleDevice Parse(JsonElement element)
            {
                var alerts = element.TryGetProperty("alerts", out var alertsElement) ? alertsElement : default;

                return new RingSimpleDevice
                {
                    Id = FirstNonEmpty(GetString(element, "id"), GetString(element, "device_id"), GetString(element, "zid")),
                    Description = FirstNonEmpty(GetString(element, "description"), GetString(element, "name"), "Ring Device"),
                    Kind = FirstNonEmpty(GetString(element, "kind"), GetString(element, "deviceType"), "ring_device"),
                    LocationId = GetString(element, "location_id"),
                    ConnectionStatus = GetBoolean(alerts, "connection"),
                };
            }
        }

        private sealed class RingAuthResponse
        {
            public string AccessToken { get; init; } = string.Empty;
            public string RefreshToken { get; init; } = string.Empty;
        }

        private sealed class RingSimpleLiveSession
        {
            public string SessionId { get; init; } = string.Empty;
            public string AnswerSdp { get; init; } = string.Empty;
        }

        private sealed class RingTwoFactorRequiredException : Exception
        {
            public RingTwoFactorRequiredException(string message) : base(message)
            {
            }
        }

        private static bool? GetBoolean(JsonElement element, string propertyName)
        {
            var value = GetString(element, propertyName);
            return value switch
            {
                "online" => true,
                "offline" => false,
                _ => null,
            };
        }

        private static int? GetInt(JsonElement element, string propertyName)
        {
            if (element.ValueKind != JsonValueKind.Object)
                return null;

            if (!element.TryGetProperty(propertyName, out var property))
                return null;

            if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var intValue))
                return intValue;

            if (property.ValueKind == JsonValueKind.String && int.TryParse(property.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out intValue))
                return intValue;

            return null;
        }

        private static string GetString(JsonElement element, string propertyName)
        {
            if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(propertyName, out var property))
                return string.Empty;

            return property.ValueKind == JsonValueKind.String ? property.GetString() ?? string.Empty : property.ToString();
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

    internal sealed class RingAuthenticationResult
    {
        public bool Ok { get; init; }
        public bool RequiresTwoFactor { get; init; }
        public string RefreshToken { get; init; } = string.Empty;
        public string PendingHardwareId { get; init; } = string.Empty;
        public string Message { get; init; } = string.Empty;
    }
}