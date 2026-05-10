using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AtlasAI.Settings;

namespace AtlasAI.SmartHome
{
    internal sealed class OnvifRtspProvider : ISmartHomeProvider
    {
        private readonly OnvifRtspSettings _settings;

        public OnvifRtspProvider(OnvifRtspSettings settings)
        {
            _settings = settings ?? new OnvifRtspSettings();
        }

        public string ProviderId => "onvif_rtsp";

        public string DisplayName => "ONVIF / RTSP Cameras";

        public SmartHomeProviderDescriptor GetDescriptor()
        {
            var hasHost = !string.IsNullOrWhiteSpace(_settings.Host);
            var hasRtsp = !string.IsNullOrWhiteSpace(_settings.RtspUrl);
            var configuredFields = new List<string>();
            if (hasHost)
                configuredFields.Add("host");
            if (!string.IsNullOrWhiteSpace(_settings.Username))
                configuredFields.Add("username");
            if (!string.IsNullOrWhiteSpace(_settings.Password))
                configuredFields.Add("password");
            if (hasRtsp)
                configuredFields.Add("rtsp_url");

            return new SmartHomeProviderDescriptor
            {
                ProviderId = ProviderId,
                DisplayName = DisplayName,
                Status = hasHost || hasRtsp ? "Endpoint saved" : "Not connected",
                IsConfigured = hasHost || hasRtsp,
                RequiredFields = new[] { "host", "username", "password", "rtsp_url" },
                ConfiguredFields = configuredFields,
                Detail = hasHost || hasRtsp
                    ? "Atlas can surface manually configured camera endpoints and use the saved host as an embedded camera target."
                    : "Enter a camera host and optionally a direct RTSP URL. This is the manual camera path for ONVIF/RTSP devices.",
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
                var isReachable = await ProbeHostAsync(cancellationToken).ConfigureAwait(false);
                var device = BuildCameraDevice(isReachable);

                return new SmartHomeProviderState
                {
                    ProviderId = ProviderId,
                    DisplayName = DisplayName,
                    Descriptor = new SmartHomeProviderDescriptor
                    {
                        ProviderId = ProviderId,
                        DisplayName = DisplayName,
                        Status = isReachable ? "Live · 1 camera" : "Saved endpoint",
                        IsConfigured = true,
                        RequiredFields = descriptor.RequiredFields,
                        ConfiguredFields = descriptor.ConfiguredFields,
                        Detail = isReachable
                            ? "Atlas reached the saved ONVIF/RTSP host and exposed it as a camera endpoint."
                            : "The endpoint is saved, but Atlas could not verify it right now.",
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
                        Detail = "Atlas could not verify the saved ONVIF/RTSP endpoint.",
                    },
                    SavedSettings = GetSavedSettings(),
                    Devices = Array.Empty<SmartHomeDevice>(),
                    Error = ex.Message,
                };
            }
        }

        public Task<SmartHomeActionResult> ExecuteActionAsync(SmartHomeActionRequest request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new SmartHomeActionResult
            {
                Ok = false,
                Message = "ONVIF / RTSP is currently a camera-ingest path only. Direct PTZ and profile actions are not wired yet.",
            });
        }

        private SmartHomeProviderFormState GetSavedSettings()
        {
            return new SmartHomeProviderFormState
            {
                Enabled = _settings.Enabled,
                Host = _settings.Host,
                Username = _settings.Username,
                Password = _settings.Password,
                RtspUrl = _settings.RtspUrl,
            };
        }

        private async Task<bool> ProbeHostAsync(CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(_settings.Host))
                return !string.IsNullOrWhiteSpace(_settings.RtspUrl);

            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(4) };
            foreach (var candidate in new[]
            {
                BuildHttpUrl(_settings.Host),
                $"{BuildHttpUrl(_settings.Host).TrimEnd('/')}/onvif/device_service",
            })
            {
                try
                {
                    using var response = await client.GetAsync(candidate, cancellationToken).ConfigureAwait(false);
                    if ((int)response.StatusCode < 500)
                        return true;
                }
                catch
                {
                }
            }

            return false;
        }

        private SmartHomeDevice BuildCameraDevice(bool isReachable)
        {
            var name = string.IsNullOrWhiteSpace(_settings.Host) ? "Manual RTSP Camera" : $"Camera {_settings.Host}";
            var externalUrl = BuildHttpUrl(_settings.Host);
            return new SmartHomeDevice
            {
                DeviceId = string.IsNullOrWhiteSpace(_settings.RtspUrl) ? (_settings.Host ?? string.Empty) : _settings.RtspUrl,
                Name = name,
                Sku = "onvif_rtsp",
                DeviceType = "camera",
                IsOnline = isReachable,
                PreviewVideoUrl = string.Empty,
                ExternalUrl = string.IsNullOrWhiteSpace(externalUrl) ? BuildHttpUrlFromRtsp(_settings.RtspUrl) : externalUrl,
                Capabilities = Array.Empty<SmartHomeCapability>(),
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

        private static string BuildHttpUrlFromRtsp(string rtspUrl)
        {
            if (string.IsNullOrWhiteSpace(rtspUrl))
                return string.Empty;

            try
            {
                if (!Uri.TryCreate(rtspUrl, UriKind.Absolute, out var uri) || string.IsNullOrWhiteSpace(uri.Host))
                    return string.Empty;
                return $"http://{uri.Host}";
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}