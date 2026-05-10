using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Runtime.ExceptionServices;
using System.ServiceProcess;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NAudio.Wave;
using AtlasAI.AI;
using AtlasAI.Controls;
using AtlasAI.Core;
using AtlasAI.Coding;
using AtlasAI.MediaScanner;
using AtlasAI.Monetization;
using AtlasAI.Personality;
using AtlasAI.SecuritySuite.Models;
using AtlasAI.SecuritySuite;
using AtlasAI.SmartHome;
using AtlasAI.Settings;
using AtlasAI.Tools;
using AtlasAI.Voice;
using AtlasAI.Views.ViewModels;
using AtlasAI.Workflows;
using AtlasAI.Settings;
using AtlasAI.DJ;

namespace AtlasAI.Services
{
    public sealed class CompanionTransportService : IDisposable
    {
        private enum CompanionLifecycleState
        {
            Stopped,
            Starting,
            Running,
            Restarting,
            Error,
        }

        public sealed record CompanionPrefixBindingResult(
            string Prefix,
            bool IsLanPrefix,
            bool Succeeded,
            string Detail,
            string? RemediationCommand);

        public sealed record CompanionTransportStatus(
            bool IsRunning,
            int Port,
            bool IsLanAccessible,
            string BindingMode,
            string[] DetectedLanAddresses,
            string[] ActivePrefixes,
            string[] BoundLanAddresses,
            CompanionPrefixBindingResult[] PrefixBindingResults,
            string? RecommendedBaseUrl,
            string? LastStartupError,
            string? UrlAclFixCommand,
            string LifecycleState,
            int RestartAttemptCount);

        public sealed record CompanionPairingInfo(
            bool IsAvailable,
            string AvailabilityMessage,
            string? BaseUrl,
            string? Protocol,
            string? Host,
            int Port,
            string DisplayName,
            string ApiVersion,
            string PayloadFormat,
            string? Payload,
            string? AuthToken,
            string AuthHeaderName,
            string AuthQueryParameter);

        public sealed record CompanionTransportProbeResult(
            bool Success,
            string[] AttemptedUrls,
            string? SuccessfulUrl,
            HttpStatusCode? StatusCode,
            string Message);

        private sealed record CompanionSftpProvisioningState(bool IsInstalled, bool IsRunning, string StatusText);

        private static readonly Lazy<CompanionTransportService> _instance =
            new(() => new CompanionTransportService());

        public static CompanionTransportService Instance => _instance.Value;

        private readonly object _stateLock = new();
        private readonly object _replyLock = new();
        private readonly object _subscriptionLock = new();
        private readonly AsyncLocal<string?> _activeReplyRequestId = new();
        private readonly string _replyCacheDir;
        private readonly string _deviceSessionMapPath;
        private readonly string _authTokenPath;
        private HttpListener? _listener;
        private CancellationTokenSource? _cts;
        private Task? _listenLoop;
        private CancellationTokenSource? _restartCts;
        private Task? _restartLoop;
        private readonly Dictionary<string, PendingReplySession> _pendingReplies = new(StringComparer.Ordinal);
        private readonly Queue<string> _pendingReplyOrder = new();
        private readonly Dictionary<string, PendingConversationReply> _pendingConversationReplies = new(StringComparer.Ordinal);
        private readonly Dictionary<string, ReplyMediaItem> _replyMedia = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _deviceSessions = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<Guid, ConversationSubscription> _conversationSubscriptions = new();
        private readonly Dictionary<Guid, LiveSyncSubscription> _liveSyncSubscriptions = new();
        private readonly Dictionary<string, ConversationProgressState> _conversationProgress = new(StringComparer.Ordinal);
        private readonly SmartHomeRuntimeService _smartHomeRuntimeService;
        private readonly CompanionRemoteDesktopService _remoteDesktopService;
        private readonly HardwareMetricsService _hardwareMetricsService;
        private readonly CodeAssistantService _companionCodeAssistant = new();
        private readonly object _networkSampleLock = new();
        private const int RemoteDesktopStreamIntervalMs = 140;
        private const string RemoteDesktopBinaryTransport = "binary-jpeg";
        private static readonly TimeSpan LiveSyncBroadcastInterval = TimeSpan.FromMilliseconds(900);
        private readonly SmartHomeCameraRecordingService _cameraRecordingService = new();
        private readonly string _securityActivityPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "AtlasAI",
            "SecuritySuite",
            "activity.json");
        private string _activeSecurityRecordingCameraId = string.Empty;
        private string _activeSecurityRecordingCameraName = string.Empty;
        private string _activeSecurityRecordingSourceUrl = string.Empty;
        private ChatWindow? _remoteChatBackend;
        private string[] _activePrefixes = Array.Empty<string>();
        private string[] _boundLanAddresses = Array.Empty<string>();
        private bool _loopbackOnlyMode;
        private string? _lastStartupError;
        private string? _urlAclFixCommand;
        private CompanionPrefixBindingResult[] _prefixBindingResults = Array.Empty<CompanionPrefixBindingResult>();
        private CompanionLifecycleState _lifecycleState = CompanionLifecycleState.Stopped;
        private int _restartAttemptCount;
        private CancellationTokenSource? _liveSyncLoopCts;
        private Task? _liveSyncLoop;
        private string? _lastMediaLiveSyncSignature;
        private string? _lastDownloaderLiveSyncSignature;
        private string? _lastDjLiveSyncSignature;
        private long _lastNetworkBytesSent;
        private long _lastNetworkBytesReceived;
        private DateTime _lastNetworkSampleUtc = DateTime.MinValue;
        private string _companionAuthToken;

        private const string CompanionAuthHeaderName = "X-Atlas-Companion-Token";
        private const string CompanionAuthQueryParameter = "atlas_companion_token";
        private const int MaxAutoRestartAttempts = 3;
        private static readonly TimeSpan AutoRestartDelay = TimeSpan.FromSeconds(5);

        public event EventHandler? StatusChanged;

        private CompanionTransportService()
            : this(new SmartHomeRuntimeService())
        {
        }

        internal CompanionTransportService(SmartHomeRuntimeService smartHomeRuntimeService)
        {
            _smartHomeRuntimeService = smartHomeRuntimeService ?? throw new ArgumentNullException(nameof(smartHomeRuntimeService));
            _remoteDesktopService = new CompanionRemoteDesktopService();
            _hardwareMetricsService = new HardwareMetricsService();
            _replyCacheDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "AtlasAI",
                "companion_replies");
            _deviceSessionMapPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "AtlasAI",
                "companion_device_sessions.json");
            _authTokenPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "AtlasAI",
                "companion_auth_token.txt");
            Directory.CreateDirectory(_replyCacheDir);
            Directory.CreateDirectory(Path.GetDirectoryName(_authTokenPath)!);
            _companionAuthToken = LoadOrCreateCompanionAuthToken();
            try { _companionCodeAssistant.SetWorkspace(Directory.GetCurrentDirectory()); } catch { }
            LoadDeviceSessionBindings();
        }

        public int Port => 3000;

        public string AudioStreamPath => "/ws/audio/live";

        public string ConversationStreamPath => "/ws/conversation/live";

        public string SecurityAlertsPath => "/ws/security/alerts";

        public string LiveSyncPath => "/api/live-sync";

        public string RemoteDesktopStreamPath => CompanionRemoteDesktopService.LiveStreamPath;

        public CompanionTransportStatus GetStatus()
        {
            lock (_stateLock)
            {
                var detectedLanAddresses = SafeGetLocalIpv4Addresses();
                var recommendedBaseUrl = BuildRecommendedBaseUrl(detectedLanAddresses);
                var isRunning = _listener != null && _lifecycleState == CompanionLifecycleState.Running;
                var lifecycleState = _lifecycleState.ToString().ToLowerInvariant();
                var bindingMode = isRunning
                    ? (_loopbackOnlyMode ? "localhost only" : "LAN + localhost")
                    : _lifecycleState switch
                    {
                        CompanionLifecycleState.Starting => "starting",
                        CompanionLifecycleState.Restarting => "restarting",
                        CompanionLifecycleState.Error => "error",
                        _ => "stopped",
                    };

                return new CompanionTransportStatus(
                    isRunning,
                    Port,
                    isRunning && _boundLanAddresses.Length > 0,
                    bindingMode,
                    detectedLanAddresses,
                    _activePrefixes.ToArray(),
                    _boundLanAddresses.ToArray(),
                    _prefixBindingResults.ToArray(),
                    recommendedBaseUrl,
                    _lastStartupError,
                    _urlAclFixCommand,
                    lifecycleState,
                    _restartAttemptCount);
            }
        }

        public CompanionPairingInfo GetPairingInfo()
        {
            return BuildPairingInfo(GetStatus());
        }

        public string? GetProvisioningPayload(bool preferRemote = false)
        {
            var status = GetStatus();
            var authToken = EnsureCompanionAuthToken();
            return BuildProvisioningPayload(status, BuildDesktopDisplayName(), authToken, preferRemote);
        }

        public async Task<CompanionTransportProbeResult> ProbeSystemStatusAsync(CancellationToken cancellationToken = default)
        {
            var status = GetStatus();
            var attemptedUrls = BuildProbeUrls(status);
            var failures = new List<string>();

            using var client = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(3)
            };

            foreach (var url in attemptedUrls)
            {
                try
                {
                    using var response = await client.GetAsync(url, cancellationToken).ConfigureAwait(false);
                    if (response.IsSuccessStatusCode)
                    {
                        var mode = url.Contains("localhost", StringComparison.OrdinalIgnoreCase) ||
                                   url.Contains("127.0.0.1", StringComparison.OrdinalIgnoreCase)
                            ? "localhost"
                            : "LAN";

                        return new CompanionTransportProbeResult(
                            true,
                            attemptedUrls,
                            url,
                            response.StatusCode,
                            $"Endpoint responded successfully over {mode}.");
                    }

                    failures.Add($"{url} -> {(int)response.StatusCode} {response.StatusCode}");
                }
                catch (Exception ex)
                {
                    failures.Add($"{url} -> {ex.Message}");
                }
            }

            var message = failures.Count > 0
                ? string.Join(" | ", failures)
                : "No endpoint candidates were available to test.";

            if (!string.IsNullOrWhiteSpace(status.UrlAclFixCommand) && !status.IsLanAccessible)
            {
                message += $" Run as administrator: {status.UrlAclFixCommand}";
            }

            return new CompanionTransportProbeResult(false, attemptedUrls, null, null, message);
        }

        public void Start()
        {
            StartInternal("manual start", scheduleRestartOnFailure: true, isAutoRestart: false, rethrowOnFailure: true);
        }

        public void Stop()
        {
            var shouldRaiseStatusChanged = false;

            lock (_stateLock)
            {
                try
                {
                    _cts?.Cancel();
                }
                catch
                {
                }

                try
                {
                    _listener?.Stop();
                    _listener?.Close();
                }
                catch
                {
                }

                CancelRestartLoop_NoLock(resetAttemptCount: true);
                ClearRuntimeState_NoLock();
                _lastStartupError = null;
                _urlAclFixCommand = null;
                _prefixBindingResults = Array.Empty<CompanionPrefixBindingResult>();
                _lifecycleState = CompanionLifecycleState.Stopped;
                shouldRaiseStatusChanged = true;
            }

            if (shouldRaiseStatusChanged)
            {
                OnStatusChanged();
            }
        }

        private IEnumerable<string> BuildPrefixes()
        {
            var prefixes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                $"http://localhost:{Port}/",
                $"http://127.0.0.1:{Port}/",
            };

            try
            {
                foreach (var address in GetLocalIpv4Addresses())
                {
                    prefixes.Add($"http://{address}:{Port}/");
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogWarning($"[Companion] Failed to enumerate LAN addresses: {ex.Message}");
            }

            return prefixes;
        }

        private IEnumerable<string> BuildLanPrefixes()
        {
            foreach (var address in SafeGetLocalIpv4Addresses())
            {
                yield return $"http://{address}:{Port}/";
            }
        }

        private IEnumerable<string> BuildLoopbackPrefixes()
        {
            yield return $"http://localhost:{Port}/";
            yield return $"http://127.0.0.1:{Port}/";
        }

        private string BuildWildcardPrefix()
        {
            return $"http://+:{Port}/";
        }

        private HttpListener? StartListenerWithFallback(
            out string[] activePrefixes,
            out string[] boundLanAddresses,
            out bool loopbackOnlyMode,
            out string? startupWarning,
            out CompanionPrefixBindingResult[] prefixBindingResults,
            out string? urlAclFixCommand)
        {
            var fixCommand = BuildUrlAclFixCommand();
            var wildcardPrefix = BuildWildcardPrefix();

            var wildcardResult = TryProbePrefixRegistration(wildcardPrefix, false, fixCommand);
            if (wildcardResult.Succeeded)
            {
                activePrefixes = new[] { wildcardPrefix };
                boundLanAddresses = SafeGetLocalIpv4Addresses();
                loopbackOnlyMode = boundLanAddresses.Length == 0;
                prefixBindingResults = new[]
                {
                    wildcardResult with
                    {
                        Detail = boundLanAddresses.Length > 0
                            ? "Wildcard listener binding available for localhost and LAN addresses."
                            : "Wildcard listener binding available for localhost."
                    }
                };
                urlAclFixCommand = null;
                startupWarning = boundLanAddresses.Length > 0
                    ? null
                    : "Running in localhost-only mode.";
                return CreateStartedListener(activePrefixes);
            }

            var allCandidates = BuildLoopbackPrefixes()
                .Concat(BuildLanPrefixes())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var diagnostics = new List<CompanionPrefixBindingResult>(allCandidates.Length + 1)
            {
                wildcardResult
            };
            var successfulPrefixes = new List<string>(allCandidates.Length);
            var successfulLanAddresses = new List<string>();
            var hasUrlAclFailure = !string.IsNullOrWhiteSpace(wildcardResult.RemediationCommand);

            foreach (var prefix in allCandidates)
            {
                var isLanPrefix = IsLanPrefix(prefix);
                var result = TryProbePrefixRegistration(prefix, isLanPrefix, fixCommand);
                diagnostics.Add(result);

                if (!result.Succeeded)
                {
                    hasUrlAclFailure |= isLanPrefix && !string.IsNullOrWhiteSpace(result.RemediationCommand);
                    continue;
                }

                successfulPrefixes.Add(prefix);
                if (isLanPrefix)
                {
                    var address = TryGetHostFromPrefix(prefix);
                    if (!string.IsNullOrWhiteSpace(address))
                    {
                        successfulLanAddresses.Add(address);
                    }
                }
            }

            if (successfulPrefixes.Count == 0)
            {
                activePrefixes = Array.Empty<string>();
                boundLanAddresses = Array.Empty<string>();
                loopbackOnlyMode = false;
                prefixBindingResults = diagnostics.ToArray();
                urlAclFixCommand = hasUrlAclFailure ? fixCommand : null;
                startupWarning = diagnostics.Count > 0
                    ? string.Join(Environment.NewLine, diagnostics.Select(d => $"{d.Prefix} -> {d.Detail}"))
                    : "No listener prefixes were available.";
                return null;
            }

            activePrefixes = successfulPrefixes.ToArray();
            boundLanAddresses = successfulLanAddresses
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(address => address, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            loopbackOnlyMode = boundLanAddresses.Length == 0;
            prefixBindingResults = diagnostics.ToArray();
            urlAclFixCommand = hasUrlAclFailure ? fixCommand : null;
            startupWarning = BuildStartupWarning(boundLanAddresses.Length, diagnostics, fixCommand);
            return CreateStartedListener(activePrefixes);
        }

        private static HttpListener CreateStartedListener(IEnumerable<string> prefixes)
        {
            var listener = new HttpListener();
            foreach (var prefix in prefixes)
            {
                listener.Prefixes.Add(prefix);
            }

            listener.Start();
            return listener;
        }

        private static IEnumerable<string> GetLocalIpv4Addresses()
        {
            foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (networkInterface.OperationalStatus != OperationalStatus.Up)
                {
                    continue;
                }

                if (networkInterface.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                {
                    continue;
                }

                foreach (var unicastAddress in networkInterface.GetIPProperties().UnicastAddresses)
                {
                    if (unicastAddress.Address.AddressFamily == AddressFamily.InterNetwork)
                    {
                        yield return unicastAddress.Address.ToString();
                    }
                }
            }
        }

        private static string[] SafeGetLocalIpv4Addresses()
        {
            try
            {
                return GetLocalIpv4Addresses()
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(address => address, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
            catch
            {
                return Array.Empty<string>();
            }
        }

        private string? BuildRecommendedBaseUrl(string[] detectedLanAddresses)
        {
            if (_listener == null)
            {
                return null;
            }

            if (_boundLanAddresses.Length > 0)
            {
                var preferredLanAddress = _boundLanAddresses.FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(preferredLanAddress))
                {
                    return $"http://{preferredLanAddress}:{Port}";
                }
            }

            if (!_loopbackOnlyMode)
            {
                var preferredLanAddress = detectedLanAddresses.FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(preferredLanAddress))
                {
                    return $"http://{preferredLanAddress}:{Port}";
                }
            }

            return $"http://localhost:{Port}";
        }

        private CompanionPairingInfo BuildPairingInfo(CompanionTransportStatus status)
        {
            const string apiVersion = "v1";
            const string payloadFormat = "atlas-companion-provisioning-json-v3";

            var displayName = BuildDesktopDisplayName();
            var pairableBaseUrl = TryGetPairableBaseUrl(status);
            var authToken = EnsureCompanionAuthToken();

            if (string.IsNullOrWhiteSpace(pairableBaseUrl))
            {
                return new CompanionPairingInfo(
                    false,
                    BuildPairingUnavailableMessage(status),
                    null,
                    null,
                    null,
                    status.Port,
                    displayName,
                    apiVersion,
                    payloadFormat,
                    null,
                    authToken,
                    CompanionAuthHeaderName,
                    CompanionAuthQueryParameter);
            }

            var uri = new Uri(pairableBaseUrl, UriKind.Absolute);
            var payload = BuildProvisioningPayload(status, displayName, authToken, preferRemote: false);

            return new CompanionPairingInfo(
                true,
                $"QR pairing is available for {pairableBaseUrl}.",
                pairableBaseUrl,
                uri.Scheme,
                uri.Host,
                status.Port,
                displayName,
                apiVersion,
                payloadFormat,
                payload,
                authToken,
                CompanionAuthHeaderName,
                CompanionAuthQueryParameter);
        }

        private string? BuildProvisioningPayload(
            CompanionTransportStatus status,
            string displayName,
            string authToken,
            bool preferRemote)
        {
            var localBaseUrl = TryGetPairableBaseUrl(status);
            var companionSettings = SettingsStore.Current.CompanionFileTransfer ?? new CompanionFileTransferSettings();
            var remoteHost = (companionSettings.RemoteHost ?? string.Empty).Trim();
            var remotePort = companionSettings.RemotePort > 0 ? companionSettings.RemotePort : Port;
            var remoteUseTls = companionSettings.RemoteUseTls;
            var remoteConfigured = !string.IsNullOrWhiteSpace(remoteHost);
            if (!preferRemote && string.IsNullOrWhiteSpace(localBaseUrl))
            {
                return null;
            }

            if (preferRemote && !remoteConfigured)
            {
                return null;
            }

            var remoteBaseUrl = remoteConfigured ? TryBuildBaseUrl(remoteHost, remotePort, remoteUseTls) : null;
            if (preferRemote && string.IsNullOrWhiteSpace(remoteBaseUrl))
            {
                return null;
            }

            Uri? localUri = null;
            if (!string.IsNullOrWhiteSpace(localBaseUrl) && Uri.TryCreate(localBaseUrl, UriKind.Absolute, out var parsedLocalUri))
            {
                localUri = parsedLocalUri;
            }

            var sftpState = GetSftpProvisioningState();
            var usesDedicatedAccount = companionSettings.UseDedicatedAccount;
            var dedicatedUsername = string.IsNullOrWhiteSpace(companionSettings.DedicatedUsername)
                ? "atlas_sftp"
                : companionSettings.DedicatedUsername.Trim();
            var dedicatedFolderPath = string.IsNullOrWhiteSpace(companionSettings.DedicatedFolderPath)
                ? GetDefaultDedicatedSftpFolderPath()
                : companionSettings.DedicatedFolderPath.Trim();
            var sftpUsername = usesDedicatedAccount ? dedicatedUsername : Environment.UserName;
            var sftpInitialPath = usesDedicatedAccount
                ? dedicatedFolderPath
                : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var preferredFileTransferHost = preferRemote && remoteConfigured
                ? remoteHost
                : localUri?.Host ?? string.Empty;
            var sftpHost = ResolveProvisioningFileTransferHost(status, preferredFileTransferHost);
            var sftpHostReady = !string.IsNullOrWhiteSpace(sftpHost) &&
                !string.Equals(sftpHost, "localhost", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(sftpHost, "127.0.0.1", StringComparison.OrdinalIgnoreCase);
            var sftpProvisioned = sftpHostReady;
            var vncHost = (companionSettings.VncHost ?? string.Empty).Trim();
            var vncConfigured = !string.IsNullOrWhiteSpace(vncHost);
            var vncPort = companionSettings.VncPort > 0 ? companionSettings.VncPort : 5900;
            var vncPassword = string.IsNullOrWhiteSpace(companionSettings.VncPassword)
                ? null
                : companionSettings.VncPassword.Trim();
            var remoteDesktopState = _remoteDesktopService.GetState(
                CompanionRemoteDesktopService.PreviewFramePath,
                RemoteDesktopStreamPath);
            var preferredConnection = preferRemote && remoteConfigured ? "remote" : "local";

            var payload = new Dictionary<string, object?>
            {
                ["type"] = "atlas-companion-provisioning",
                ["payloadFormat"] = "atlas-companion-provisioning-json-v3",
                ["label"] = displayName,
                ["preferredConnection"] = preferredConnection,
                ["provisioning"] = new Dictionary<string, object?>
                {
                    ["preferredConnection"] = preferredConnection,
                    ["hostPlatform"] = "windows",
                },
                ["capabilities"] = new Dictionary<string, object?>
                {
                    ["atlasApi"] = BuildCompactProvisioningFeature(supported: true),
                    ["liveSync"] = BuildCompactProvisioningFeature(supported: true, route: LiveSyncPath),
                    ["remoteControl"] = BuildCompactProvisioningFeature(
                        supported: remoteDesktopState.IsAvailable,
                        route: RemoteDesktopStreamPath),
                    ["vnc"] = BuildCompactProvisioningFeature(
                        supported: vncConfigured,
                        credentialStatus: vncConfigured
                            ? (string.IsNullOrWhiteSpace(vncPassword) ? "manual" : "provided")
                            : null),
                    ["sftp"] = BuildCompactProvisioningFeature(
                        supported: sftpState.IsInstalled,
                        credentialStatus: sftpProvisioned ? "manual" : null),
                },
            };

            if (localUri != null && !string.IsNullOrWhiteSpace(localBaseUrl))
            {
                payload["atlas"] = new Dictionary<string, object?>
                {
                    ["host"] = localUri.Host,
                    ["port"] = localUri.Port,
                    ["useTls"] = string.Equals(localUri.Scheme, "https", StringComparison.OrdinalIgnoreCase),
                    ["baseUrl"] = localBaseUrl,
                    ["authToken"] = authToken,
                };
            }

            if (remoteConfigured && !string.IsNullOrWhiteSpace(remoteBaseUrl))
            {
                payload["remote"] = new Dictionary<string, object?>
                {
                    ["host"] = remoteHost,
                    ["port"] = remotePort,
                    ["useTls"] = remoteUseTls,
                    ["baseUrl"] = remoteBaseUrl,
                    ["authToken"] = authToken,
                };
            }

            if (vncConfigured)
            {
                payload["vnc"] = new Dictionary<string, object?>
                {
                    ["host"] = vncHost,
                    ["port"] = vncPort,
                    ["password"] = vncPassword,
                };
            }

            if (sftpProvisioned)
            {
                var sftpProfile = new Dictionary<string, object?>
                {
                    ["protocol"] = "sftp",
                    ["label"] = usesDedicatedAccount ? "Atlas Dedicated SFTP" : "Atlas PC",
                    ["host"] = sftpHost,
                    ["port"] = 22,
                    ["username"] = sftpUsername,
                    ["authType"] = "password",
                    ["initialRemotePath"] = sftpInitialPath,
                };

                payload["sftp"] = sftpProfile;
            }

            return JsonSerializer.Serialize(payload, new JsonSerializerOptions
            {
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
                WriteIndented = false,
            });
        }

        private static Dictionary<string, object?> BuildCompactProvisioningFeature(
            bool supported,
            string? route = null,
            string? credentialStatus = null)
        {
            return new Dictionary<string, object?>
            {
                ["supported"] = supported,
                ["route"] = route,
                ["credentialStatus"] = credentialStatus,
            };
        }

        private static string? TryBuildBaseUrl(string host, int port, bool useTls)
        {
            var normalizedHost = (host ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(normalizedHost))
            {
                return null;
            }

            return $"{(useTls ? "https" : "http")}://{normalizedHost}:{port}";
        }

        private static string ResolveProvisioningFileTransferHost(CompanionTransportStatus status, string localHost)
        {
            if (IsPhoneReachableHost(localHost))
            {
                return localHost;
            }

            if (!string.IsNullOrWhiteSpace(status.RecommendedBaseUrl) &&
                Uri.TryCreate(status.RecommendedBaseUrl, UriKind.Absolute, out var recommendedUri) &&
                IsPhoneReachableHost(recommendedUri.Host))
            {
                return recommendedUri.Host;
            }

            return status.DetectedLanAddresses.FirstOrDefault(IsPhoneReachableHost) ?? string.Empty;
        }

        private static CompanionSftpProvisioningState GetSftpProvisioningState()
        {
            try
            {
                using var controller = new ServiceController("sshd");
                var status = controller.Status;
                var statusText = status switch
                {
                    ServiceControllerStatus.Running => "Running",
                    ServiceControllerStatus.StartPending => "Starting",
                    ServiceControllerStatus.StopPending => "Stopping",
                    ServiceControllerStatus.Stopped => "Stopped",
                    ServiceControllerStatus.Paused => "Paused",
                    ServiceControllerStatus.PausePending => "Pausing",
                    ServiceControllerStatus.ContinuePending => "Resuming",
                    _ => status.ToString(),
                };

                return new CompanionSftpProvisioningState(true, status == ServiceControllerStatus.Running, statusText);
            }
            catch (InvalidOperationException)
            {
                return new CompanionSftpProvisioningState(false, false, "Not installed");
            }
            catch (Exception ex)
            {
                return new CompanionSftpProvisioningState(false, false, ex.Message);
            }
        }

        private static string GetDefaultDedicatedSftpFolderPath()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Atlas Companion Transfer");
        }

        private string EnsureCompanionAuthToken()
        {
            lock (_stateLock)
            {
                if (string.IsNullOrWhiteSpace(_companionAuthToken))
                {
                    _companionAuthToken = LoadOrCreateCompanionAuthToken();
                }

                return _companionAuthToken;
            }
        }

        private string LoadOrCreateCompanionAuthToken()
        {
            try
            {
                if (File.Exists(_authTokenPath))
                {
                    var protectedToken = File.ReadAllText(_authTokenPath).Trim();
                    var token = SecretProtector.UnprotectIfNeeded(protectedToken).Trim();
                    if (!string.IsNullOrWhiteSpace(token))
                    {
                        return token;
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogWarning($"[Companion] Failed to load auth token: {ex.Message}");
            }

            var generated = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

            try
            {
                File.WriteAllText(_authTokenPath, SecretProtector.Protect(generated));
            }
            catch (Exception ex)
            {
                AppLogger.LogWarning($"[Companion] Failed to persist auth token: {ex.Message}");
            }

            return generated;
        }

        private static bool RequestRequiresAuthentication(string path)
        {
            return !string.Equals(path, "/api/system/status", StringComparison.OrdinalIgnoreCase);
        }

        private bool IsAuthorizedRequest(HttpListenerRequest request)
        {
            var expectedToken = EnsureCompanionAuthToken();
            if (string.IsNullOrWhiteSpace(expectedToken))
            {
                return false;
            }

            var presentedToken = request.Headers[CompanionAuthHeaderName];
            if (string.IsNullOrWhiteSpace(presentedToken))
            {
                presentedToken = request.QueryString[CompanionAuthQueryParameter];
            }

            return !string.IsNullOrWhiteSpace(presentedToken) &&
                   string.Equals(presentedToken.Trim(), expectedToken, StringComparison.Ordinal);
        }

        private string BuildAuthorizedRelativePath(string relativePath)
        {
            var delimiter = relativePath.Contains('?', StringComparison.Ordinal) ? "&" : "?";
            return $"{relativePath}{delimiter}{CompanionAuthQueryParameter}={WebUtility.UrlEncode(EnsureCompanionAuthToken())}";
        }

        private static async Task WriteUnauthorizedAsync(HttpListenerContext context)
        {
            context.Response.StatusCode = 401;
            context.Response.Headers["WWW-Authenticate"] = CompanionAuthHeaderName;
            await WriteJsonAsync(context, new { error = "Companion pairing token is required for this endpoint." }).ConfigureAwait(false);
        }

        private string? TryGetPairableBaseUrl(CompanionTransportStatus status)
        {
            if (!status.IsRunning)
            {
                return null;
            }

            foreach (var address in status.BoundLanAddresses)
            {
                if (IsPhoneReachableHost(address))
                {
                    return $"http://{address}:{status.Port}";
                }
            }

            if (Uri.TryCreate(status.RecommendedBaseUrl, UriKind.Absolute, out var recommendedUri) &&
                IsPhoneReachableHost(recommendedUri.Host))
            {
                return status.RecommendedBaseUrl;
            }

            foreach (var prefix in status.ActivePrefixes)
            {
                var host = TryGetHostFromPrefix(prefix);
                if (IsPhoneReachableHost(host))
                {
                    return $"http://{host}:{status.Port}";
                }
            }

            return null;
        }

        private static bool IsPhoneReachableHost(string? host)
        {
            return !string.IsNullOrWhiteSpace(host) &&
                   !string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) &&
                   !string.Equals(host, "127.0.0.1", StringComparison.OrdinalIgnoreCase) &&
                   !string.Equals(host, "+", StringComparison.OrdinalIgnoreCase) &&
                   !string.Equals(host, "*", StringComparison.OrdinalIgnoreCase);
        }

        private static string BuildDesktopDisplayName()
        {
            var machineName = Environment.MachineName?.Trim();
            return string.IsNullOrWhiteSpace(machineName)
                ? "Atlas Desktop"
                : $"Atlas on {machineName}";
        }

        private static string BuildPairingUnavailableMessage(CompanionTransportStatus status)
        {
            if (string.Equals(status.LifecycleState, "starting", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(status.LifecycleState, "restarting", StringComparison.OrdinalIgnoreCase))
            {
                return "QR pairing is unavailable while the companion service is starting. Wait for Atlas to finish binding the companion endpoint, then try again.";
            }

            if (string.Equals(status.LifecycleState, "error", StringComparison.OrdinalIgnoreCase))
            {
                return !string.IsNullOrWhiteSpace(status.LastStartupError)
                    ? $"QR pairing is unavailable because the companion service failed to start: {status.LastStartupError}"
                    : "QR pairing is unavailable because the companion service failed to start.";
            }

            if (!status.IsRunning)
            {
                return "QR pairing is unavailable because the companion service is stopped.";
            }

            if (status.IsLanAccessible)
            {
                return "QR pairing is unavailable because Atlas could not resolve a phone-usable LAN URL from the current binding state.";
            }

            return "QR pairing is unavailable for iPhone because Atlas is only reachable on localhost. Restore LAN reachability first, then generate the QR code.";
        }

        private string[] BuildProbeUrls(CompanionTransportStatus status)
        {
            var urls = new List<string>();

            if (!string.IsNullOrWhiteSpace(status.RecommendedBaseUrl))
            {
                urls.Add($"{status.RecommendedBaseUrl}/api/system/status");
            }

            foreach (var address in status.BoundLanAddresses)
            {
                urls.Add($"http://{address}:{status.Port}/api/system/status");
            }

            foreach (var prefix in status.ActivePrefixes)
            {
                var host = TryGetHostFromPrefix(prefix);
                if (string.IsNullOrWhiteSpace(host))
                {
                    continue;
                }

                urls.Add($"http://{host}:{status.Port}/api/system/status");
            }

            urls.Add($"http://localhost:{status.Port}/api/system/status");
            urls.Add($"http://127.0.0.1:{status.Port}/api/system/status");

            return urls
                .Where(url => !string.IsNullOrWhiteSpace(url))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private CompanionPrefixBindingResult TryProbePrefixRegistration(string prefix, bool isLanPrefix, string fixCommand)
        {
            try
            {
                using var listener = new HttpListener();
                listener.Prefixes.Add(prefix);
                listener.Start();
                listener.Stop();

                return new CompanionPrefixBindingResult(
                    prefix,
                    isLanPrefix,
                    true,
                    isLanPrefix ? "LAN binding available." : "Loopback binding available.",
                    null);
            }
            catch (HttpListenerException ex) when (ex.ErrorCode == 5)
            {
                var detail = isLanPrefix
                    ? "URL ACL denied this LAN prefix. Run the fix command in an elevated terminal, then restart Atlas."
                    : "URL ACL denied this loopback prefix.";

                AppLogger.LogWarning($"[Companion] Prefix probe denied for {prefix}: {ex.Message}");

                return new CompanionPrefixBindingResult(
                    prefix,
                    isLanPrefix,
                    false,
                    detail,
                    isLanPrefix ? fixCommand : null);
            }
            catch (Exception ex)
            {
                AppLogger.LogWarning($"[Companion] Prefix probe failed for {prefix}: {ex.Message}");
                return new CompanionPrefixBindingResult(
                    prefix,
                    isLanPrefix,
                    false,
                    ex.Message,
                    null);
            }
        }

        private static bool IsLanPrefix(string prefix)
        {
            var host = TryGetHostFromPrefix(prefix);
            return !string.IsNullOrWhiteSpace(host) &&
                   !string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) &&
                   !string.Equals(host, "127.0.0.1", StringComparison.OrdinalIgnoreCase);
        }

        private static string? TryGetHostFromPrefix(string prefix)
        {
            return Uri.TryCreate(prefix, UriKind.Absolute, out var uri)
                ? uri.Host
                : null;
        }

        private static string? BuildStartupWarning(int boundLanAddressCount, IEnumerable<CompanionPrefixBindingResult> diagnostics, string fixCommand)
        {
            var failedLanPrefixes = diagnostics.Where(d => d.IsLanPrefix && !d.Succeeded).ToArray();
            if (failedLanPrefixes.Length == 0)
            {
                return boundLanAddressCount > 0
                    ? null
                    : "Running in localhost-only mode.";
            }

            if (boundLanAddressCount > 0)
            {
                return $"Atlas bound at least one LAN address, but some LAN prefixes still failed. If the iPhone cannot reach Atlas on your preferred IP, run as administrator: {fixCommand}";
            }

            return $"LAN prefix registration was denied, so Atlas fell back to localhost-only mode. Run as administrator: {fixCommand}";
        }

        private string BuildUrlAclFixCommand()
        {
            var identity = WindowsIdentity.GetCurrent()?.Name;
            if (string.IsNullOrWhiteSpace(identity))
            {
                identity = $"{Environment.UserDomainName}\\{Environment.UserName}";
            }

            return $"netsh http add urlacl url=http://+:{Port}/ user=\"{identity}\"";
        }

        private void OnStatusChanged()
        {
            try
            {
                StatusChanged?.Invoke(this, EventArgs.Empty);
            }
            catch
            {
            }
        }

        private async Task ListenLoopAsync(HttpListener listener, CancellationToken cancellationToken)
        {
            Exception? terminalException = null;

            while (!cancellationToken.IsCancellationRequested)
            {
                HttpListenerContext? context = null;

                try
                {
                    context = await listener.GetContextAsync().ConfigureAwait(false);
                }
                catch (ObjectDisposedException ex)
                {
                    terminalException = ex;
                    break;
                }
                catch (HttpListenerException ex)
                {
                    terminalException = ex;
                    break;
                }
                catch (Exception ex)
                {
                    AppLogger.LogWarning($"[Companion] Listener error: {ex.Message}");
                    await Task.Delay(200, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                _ = Task.Run(() => HandleContextAsync(context, cancellationToken), cancellationToken);
            }

            HandleListenerLoopExit(listener, cancellationToken, terminalException);
        }

        private async Task HandleContextAsync(HttpListenerContext context, CancellationToken cancellationToken)
        {
            var path = context.Request.Url?.AbsolutePath ?? "/";

            try
            {
                if (string.Equals(path, "/api/system/status", StringComparison.OrdinalIgnoreCase))
                {
                    await WriteJsonAsync(context, BuildSystemStatusResponse()).ConfigureAwait(false);
                    return;
                }

                if (RequestRequiresAuthentication(path) && !IsAuthorizedRequest(context.Request))
                {
                    await WriteUnauthorizedAsync(context).ConfigureAwait(false);
                    return;
                }

                if (string.Equals(path, "/api/assistant/state", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(context.Request.HttpMethod, "GET", StringComparison.OrdinalIgnoreCase))
                {
                    await HandleAssistantStateAsync(context).ConfigureAwait(false);
                    return;
                }

                if (string.Equals(path, "/api/assistant/state", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(context.Request.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase))
                {
                    await HandleAssistantUpdateAsync(context).ConfigureAwait(false);
                    return;
                }

                if (string.Equals(path, "/api/ai/chat", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(context.Request.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase))
                {
                    await HandleAiChatAsync(context).ConfigureAwait(false);
                    return;
                }

                if (string.Equals(path, "/api/ai/voice", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(context.Request.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase))
                {
                    await HandleAiVoiceAsync(context).ConfigureAwait(false);
                    return;
                }

                if (string.Equals(path, "/api/ai/conversation", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(context.Request.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase))
                {
                    await HandleConversationRequestAsync(context).ConfigureAwait(false);
                    return;
                }

                if (string.Equals(path, "/api/ai/conversation/reset", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(context.Request.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase))
                {
                    await HandleConversationResetAsync(context).ConfigureAwait(false);
                    return;
                }

                if (string.Equals(path, "/api/ai/conversation/state", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(context.Request.HttpMethod, "GET", StringComparison.OrdinalIgnoreCase))
                {
                    await HandleConversationStateAsync(context).ConfigureAwait(false);
                    return;
                }

                if (string.Equals(path, "/api/billing/plans", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(context.Request.HttpMethod, "GET", StringComparison.OrdinalIgnoreCase))
                {
                    await HandleBillingPlansAsync(context).ConfigureAwait(false);
                    return;
                }

                if (string.Equals(path, "/api/billing/state", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(context.Request.HttpMethod, "GET", StringComparison.OrdinalIgnoreCase))
                {
                    await HandleBillingStateAsync(context).ConfigureAwait(false);
                    return;
                }

                if (string.Equals(path, "/api/billing/ledger", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(context.Request.HttpMethod, "GET", StringComparison.OrdinalIgnoreCase))
                {
                    await HandleBillingLedgerAsync(context).ConfigureAwait(false);
                    return;
                }

                if (string.Equals(path, "/api/billing/mode", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(context.Request.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase))
                {
                    await HandleBillingModeAsync(context).ConfigureAwait(false);
                    return;
                }

                if (string.Equals(path, "/api/billing/plan", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(context.Request.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase))
                {
                    await HandleBillingPlanAsync(context).ConfigureAwait(false);
                    return;
                }

                if (string.Equals(path, "/api/billing/top-up", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(context.Request.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase))
                {
                    await HandleBillingTopUpAsync(context).ConfigureAwait(false);
                    return;
                }

                if (string.Equals(path, "/api/billing/quote", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(context.Request.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase))
                {
                    await HandleBillingQuoteAsync(context).ConfigureAwait(false);
                    return;
                }

                if (string.Equals(path, "/api/smart-home/state", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(context.Request.HttpMethod, "GET", StringComparison.OrdinalIgnoreCase))
                {
                    await HandleSmartHomeStateAsync(context, cancellationToken).ConfigureAwait(false);
                    return;
                }

                if (string.Equals(path, "/api/smart-home/devices", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(context.Request.HttpMethod, "GET", StringComparison.OrdinalIgnoreCase))
                {
                    await HandleSmartHomeDevicesAsync(context, cancellationToken).ConfigureAwait(false);
                    return;
                }

                if (TryMatchSmartHomeDeviceToggle(path, out var routeDeviceId) &&
                    string.Equals(context.Request.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase))
                {
                    await HandleSmartHomeDeviceToggleAliasAsync(context, routeDeviceId, cancellationToken).ConfigureAwait(false);
                    return;
                }

                if (string.Equals(path, "/api/smart-home/action", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(context.Request.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase))
                {
                    await HandleSmartHomeActionAsync(context, cancellationToken).ConfigureAwait(false);
                    return;
                }

                if (string.Equals(path, "/api/smart-home/toggle", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(context.Request.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase))
                {
                    await HandleSmartHomeToggleAsync(context, cancellationToken).ConfigureAwait(false);
                    return;
                }

                if (string.Equals(path, "/api/security/alerts", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(context.Request.HttpMethod, "GET", StringComparison.OrdinalIgnoreCase))
                {
                    await HandleSecurityAlertsAsync(context).ConfigureAwait(false);
                    return;
                }

                if (string.Equals(path, "/api/security/cameras", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(context.Request.HttpMethod, "GET", StringComparison.OrdinalIgnoreCase))
                {
                    await HandleSecurityCamerasAsync(context, cancellationToken).ConfigureAwait(false);
                    return;
                }

                if (TryMatchSecurityCameraReconnect(path, out var routeCameraId) &&
                    string.Equals(context.Request.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase))
                {
                    await HandleSecurityCameraReconnectAsync(context, routeCameraId, cancellationToken).ConfigureAwait(false);
                    return;
                }

                if (TryMatchSecurityCameraRecordingStart(path, out routeCameraId) &&
                    string.Equals(context.Request.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase))
                {
                    await HandleSecurityCameraRecordingStartAsync(context, routeCameraId, cancellationToken).ConfigureAwait(false);
                    return;
                }

                if (TryMatchSecurityCameraRecordingStop(path, out routeCameraId) &&
                    string.Equals(context.Request.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase))
                {
                    await HandleSecurityCameraRecordingStopAsync(context, routeCameraId, cancellationToken).ConfigureAwait(false);
                    return;
                }

                if (string.Equals(path, "/api/smart-home/scenes", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(context.Request.HttpMethod, "GET", StringComparison.OrdinalIgnoreCase))
                {
                    await HandleSmartHomeScenesAsync(context).ConfigureAwait(false);
                    return;
                }

                if (TryMatchSmartHomeSceneActivate(path, out var routeSceneId) &&
                    string.Equals(context.Request.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase))
                {
                    await HandleSmartHomeSceneActivateAsync(context, routeSceneId).ConfigureAwait(false);
                    return;
                }

                if (string.Equals(path, "/api/automation/routines", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(context.Request.HttpMethod, "GET", StringComparison.OrdinalIgnoreCase))
                {
                    await HandleAutomationRoutinesAsync(context).ConfigureAwait(false);
                    return;
                }

                if (TryMatchAutomationRoutineExecute(path, out var routeRoutineId) &&
                    string.Equals(context.Request.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase))
                {
                    await HandleAutomationRoutineExecuteAsync(context, routeRoutineId).ConfigureAwait(false);
                    return;
                }

                if (string.Equals(path, "/api/media/state", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(context.Request.HttpMethod, "GET", StringComparison.OrdinalIgnoreCase))
                {
                    await HandleMediaStateAsync(context).ConfigureAwait(false);
                    return;
                }

                if (string.Equals(path, "/api/media/artwork", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(context.Request.HttpMethod, "GET", StringComparison.OrdinalIgnoreCase))
                {
                    await HandleMediaArtworkAsync(context).ConfigureAwait(false);
                    return;
                }

                if (string.Equals(path, "/api/media/file", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(context.Request.HttpMethod, "GET", StringComparison.OrdinalIgnoreCase))
                {
                    await HandleMediaFileAsync(context).ConfigureAwait(false);
                    return;
                }

                if (string.Equals(path, "/api/media/control", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(context.Request.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase))
                {
                    await HandleMediaControlAsync(context).ConfigureAwait(false);
                    return;
                }

                if (string.Equals(path, "/api/media/volume", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(context.Request.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase))
                {
                    await HandleMediaVolumeAsync(context).ConfigureAwait(false);
                    return;
                }

                if (string.Equals(path, "/api/media/queue", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(context.Request.HttpMethod, "GET", StringComparison.OrdinalIgnoreCase))
                {
                    await HandleMediaQueueAsync(context).ConfigureAwait(false);
                    return;
                }

                if (string.Equals(path, "/api/media/queue", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(context.Request.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase))
                {
                    await HandleMediaQueueControlAsync(context).ConfigureAwait(false);
                    return;
                }

                if (string.Equals(path, "/api/media/history", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(context.Request.HttpMethod, "GET", StringComparison.OrdinalIgnoreCase))
                {
                    await HandleMediaHistoryAsync(context).ConfigureAwait(false);
                    return;
                }

                if (string.Equals(path, "/api/media/home", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(context.Request.HttpMethod, "GET", StringComparison.OrdinalIgnoreCase))
                {
                    await HandleMediaHomeAsync(context).ConfigureAwait(false);
                    return;
                }

                if (string.Equals(path, "/api/media/library", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(context.Request.HttpMethod, "GET", StringComparison.OrdinalIgnoreCase))
                {
                    await HandleMediaLibraryAsync(context).ConfigureAwait(false);
                    return;
                }

                if (string.Equals(path, "/api/media/library/control", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(context.Request.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase))
                {
                    await HandleMediaLibraryControlAsync(context).ConfigureAwait(false);
                    return;
                }

                if (string.Equals(path, "/api/downloader/state", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(context.Request.HttpMethod, "GET", StringComparison.OrdinalIgnoreCase))
                {
                    await HandleDownloaderStateAsync(context).ConfigureAwait(false);
                    return;
                }

                if (string.Equals(path, "/api/downloader/control", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(context.Request.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase))
                {
                    await HandleDownloaderControlAsync(context).ConfigureAwait(false);
                    return;
                }

                if (string.Equals(path, "/api/dj/state", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(context.Request.HttpMethod, "GET", StringComparison.OrdinalIgnoreCase))
                {
                    await HandleDjStateAsync(context).ConfigureAwait(false);
                    return;
                }

                if (string.Equals(path, "/api/dj/control", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(context.Request.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase))
                {
                    await HandleDjControlAsync(context).ConfigureAwait(false);
                    return;
                }

                if (string.Equals(path, "/api/dj/activate", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(context.Request.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase))
                {
                    await HandleDjActivateAsync(context).ConfigureAwait(false);
                    return;
                }

                if (string.Equals(path, "/api/code/state", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(context.Request.HttpMethod, "GET", StringComparison.OrdinalIgnoreCase))
                {
                    await HandleCodeStateAsync(context).ConfigureAwait(false);
                    return;
                }

                if (string.Equals(path, "/api/code/workspaces", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(context.Request.HttpMethod, "GET", StringComparison.OrdinalIgnoreCase))
                {
                    await HandleCodeWorkspacesAsync(context).ConfigureAwait(false);
                    return;
                }

                if (string.Equals(path, "/api/code/workspace", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(context.Request.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase))
                {
                    await HandleCodeWorkspaceAsync(context).ConfigureAwait(false);
                    return;
                }

                if (string.Equals(path, "/api/code/search", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(context.Request.HttpMethod, "GET", StringComparison.OrdinalIgnoreCase))
                {
                    await HandleCodeSearchAsync(context).ConfigureAwait(false);
                    return;
                }

                if (string.Equals(path, "/api/code/file", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(context.Request.HttpMethod, "GET", StringComparison.OrdinalIgnoreCase))
                {
                    await HandleCodeFileAsync(context).ConfigureAwait(false);
                    return;
                }

                if (string.Equals(path, "/api/code/file", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(context.Request.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase))
                {
                    await HandleCodeWriteAsync(context).ConfigureAwait(false);
                    return;
                }

                if (string.Equals(path, "/api/code/patch", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(context.Request.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase))
                {
                    await HandleCodePatchAsync(context).ConfigureAwait(false);
                    return;
                }

                if (string.Equals(path, "/api/code/run", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(context.Request.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase))
                {
                    await HandleCodeRunAsync(context).ConfigureAwait(false);
                    return;
                }

                if (string.Equals(path, "/api/remote/state", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(context.Request.HttpMethod, "GET", StringComparison.OrdinalIgnoreCase))
                {
                    await HandleRemoteStateAsync(context).ConfigureAwait(false);
                    return;
                }

                if (string.Equals(path, "/api/remote/action", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(context.Request.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase))
                {
                    await HandleRemoteActionAsync(context).ConfigureAwait(false);
                    return;
                }

                if (string.Equals(path, "/api/remote/desktop/frame", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(context.Request.HttpMethod, "GET", StringComparison.OrdinalIgnoreCase))
                {
                    await HandleRemoteDesktopFrameAsync(context).ConfigureAwait(false);
                    return;
                }

                if (string.Equals(path, RemoteDesktopStreamPath, StringComparison.OrdinalIgnoreCase) &&
                    context.Request.IsWebSocketRequest)
                {
                    await HandleRemoteDesktopStreamAsync(context, cancellationToken).ConfigureAwait(false);
                    return;
                }

                if (path.StartsWith("/media/replies/", StringComparison.OrdinalIgnoreCase))
                {
                    await WriteReplyMediaAsync(context, path).ConfigureAwait(false);
                    return;
                }

                if (string.Equals(path, AudioStreamPath, StringComparison.OrdinalIgnoreCase) &&
                    context.Request.IsWebSocketRequest)
                {
                    await HandleAudioSocketAsync(context, cancellationToken).ConfigureAwait(false);
                    return;
                }

                if (string.Equals(path, ConversationStreamPath, StringComparison.OrdinalIgnoreCase) &&
                    context.Request.IsWebSocketRequest)
                {
                    await HandleConversationSocketAsync(context, cancellationToken).ConfigureAwait(false);
                    return;
                }

                if (string.Equals(path, LiveSyncPath, StringComparison.OrdinalIgnoreCase) &&
                    context.Request.IsWebSocketRequest)
                {
                    await HandleLiveSyncSocketAsync(context, cancellationToken).ConfigureAwait(false);
                    return;
                }

                context.Response.StatusCode = 404;
                context.Response.Close();
            }
            catch (Exception ex)
            {
                AppLogger.LogWarning($"[Companion] Request handling error: {ex.Message}");
                try
                {
                    context.Response.StatusCode = 500;
                    context.Response.Close();
                }
                catch
                {
                }
            }
        }

        private async Task HandleAudioSocketAsync(HttpListenerContext context, CancellationToken cancellationToken)
        {
            WebSocket? socket = null;
            var audioBuffer = new MemoryStream();
            var started = false;
            string? activeRequestId = null;

            try
            {
                var webSocketContext = await context.AcceptWebSocketAsync(null).ConfigureAwait(false);
                socket = webSocketContext.WebSocket;

                await SendSocketMessageAsync(socket, new { type = "ready", sampleRate = 16000, channels = 1 }, cancellationToken)
                    .ConfigureAwait(false);

                var receiveBuffer = new byte[8192];

                while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
                {
                    using var messageBuffer = new MemoryStream();
                    WebSocketReceiveResult result;

                    do
                    {
                        result = await socket.ReceiveAsync(
                            new ArraySegment<byte>(receiveBuffer),
                            cancellationToken).ConfigureAwait(false);

                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            await socket.CloseAsync(
                                WebSocketCloseStatus.NormalClosure,
                                "closing",
                                cancellationToken).ConfigureAwait(false);
                            return;
                        }

                        if (result.Count > 0)
                        {
                            messageBuffer.Write(receiveBuffer, 0, result.Count);
                        }
                    }
                    while (!result.EndOfMessage);

                    var payload = messageBuffer.ToArray();

                    if (result.MessageType == WebSocketMessageType.Binary)
                    {
                        if (started && payload.Length > 0)
                        {
                            audioBuffer.Write(payload, 0, payload.Length);
                        }
                        continue;
                    }

                    if (result.MessageType != WebSocketMessageType.Text)
                    {
                        continue;
                    }

                    var json = Encoding.UTF8.GetString(payload);
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;
                    var type = root.TryGetProperty("type", out var typeProp)
                        ? typeProp.GetString() ?? string.Empty
                        : string.Empty;

                    if (string.Equals(type, "start", StringComparison.OrdinalIgnoreCase))
                    {
                        activeRequestId = root.TryGetProperty("requestId", out var requestIdProp)
                            ? requestIdProp.GetString()
                            : null;

                        if (string.IsNullOrWhiteSpace(activeRequestId))
                        {
                            activeRequestId = Guid.NewGuid().ToString("N");
                        }

                        audioBuffer.SetLength(0);
                        started = true;
                        await SendSocketMessageAsync(socket, new
                        {
                            type = "status",
                            message = "streaming",
                            requestId = activeRequestId,
                        }, cancellationToken)
                            .ConfigureAwait(false);
                        continue;
                    }

                    if (string.Equals(type, "stop", StringComparison.OrdinalIgnoreCase))
                    {
                        await ProcessAudioAsync(socket, audioBuffer.ToArray(), activeRequestId, cancellationToken)
                            .ConfigureAwait(false);
                        audioBuffer.SetLength(0);
                        started = false;
                        continue;
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogWarning($"[Companion] Audio socket error: {ex.Message}");
                if (socket != null && socket.State == WebSocketState.Open)
                {
                    await SendSocketMessageAsync(socket, new { type = "error", message = ex.Message }, CancellationToken.None)
                        .ConfigureAwait(false);
                }
            }
            finally
            {
                UnregisterPendingReply(activeRequestId, socket);
                audioBuffer.Dispose();
                if (socket != null)
                {
                    try
                    {
                        if (socket.State == WebSocketState.Open || socket.State == WebSocketState.CloseReceived)
                        {
                            await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None)
                                .ConfigureAwait(false);
                        }
                    }
                    catch
                    {
                    }

                    socket.Dispose();
                }
            }
        }

        private async Task HandleConversationSocketAsync(HttpListenerContext context, CancellationToken cancellationToken)
        {
            WebSocket? socket = null;
            ConversationSubscription? subscription = null;

            try
            {
                var webSocketContext = await context.AcceptWebSocketAsync(null).ConfigureAwait(false);
                socket = webSocketContext.WebSocket;

                subscription = new ConversationSubscription(
                    Guid.NewGuid(),
                    socket,
                    context.Request.QueryString["device"],
                    context.Request.QueryString["conversationId"],
                    DateTime.UtcNow);

                RegisterConversationSubscription(subscription);

                await SendSocketMessageAsync(socket, new
                {
                    type = "ready",
                    stream = "conversation",
                }, cancellationToken).ConfigureAwait(false);

                await SendConversationSnapshotAsync(subscription, cancellationToken).ConfigureAwait(false);

                var receiveBuffer = new byte[4096];

                while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
                {
                    using var messageBuffer = new MemoryStream();
                    WebSocketReceiveResult result;

                    do
                    {
                        result = await socket.ReceiveAsync(
                            new ArraySegment<byte>(receiveBuffer),
                            cancellationToken).ConfigureAwait(false);

                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            await socket.CloseAsync(
                                WebSocketCloseStatus.NormalClosure,
                                "closing",
                                cancellationToken).ConfigureAwait(false);
                            return;
                        }

                        if (result.Count > 0)
                        {
                            messageBuffer.Write(receiveBuffer, 0, result.Count);
                        }
                    }
                    while (!result.EndOfMessage);

                    if (result.MessageType != WebSocketMessageType.Text)
                    {
                        continue;
                    }

                    var payload = Encoding.UTF8.GetString(messageBuffer.ToArray());
                    if (string.IsNullOrWhiteSpace(payload))
                    {
                        continue;
                    }

                    try
                    {
                        using var doc = JsonDocument.Parse(payload);
                        var root = doc.RootElement;
                        var type = root.TryGetProperty("type", out var typeProp)
                            ? typeProp.GetString() ?? string.Empty
                            : string.Empty;

                        if (string.Equals(type, "refresh", StringComparison.OrdinalIgnoreCase))
                        {
                            await SendConversationSnapshotAsync(subscription, cancellationToken).ConfigureAwait(false);
                        }
                    }
                    catch (JsonException)
                    {
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogWarning($"[Companion] Conversation socket error: {ex.Message}");
            }
            finally
            {
                if (subscription != null)
                {
                    UnregisterConversationSubscription(subscription.Id);
                }

                if (socket != null)
                {
                    try
                    {
                        if (socket.State == WebSocketState.Open || socket.State == WebSocketState.CloseReceived)
                        {
                            await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None)
                                .ConfigureAwait(false);
                        }
                    }
                    catch
                    {
                    }

                    socket.Dispose();
                }
            }
        }

        private async Task HandleLiveSyncSocketAsync(HttpListenerContext context, CancellationToken cancellationToken)
        {
            WebSocket? socket = null;
            LiveSyncSubscription? subscription = null;

            try
            {
                var webSocketContext = await context.AcceptWebSocketAsync(null).ConfigureAwait(false);
                socket = webSocketContext.WebSocket;

                subscription = new LiveSyncSubscription(
                    Guid.NewGuid(),
                    socket,
                    context.Request.QueryString["device"],
                    DateTime.UtcNow);

                RegisterLiveSyncSubscription(subscription);

                await SendSocketMessageAsync(socket, new
                {
                    type = "connection_status",
                    channel = "live_sync",
                    timestamp = DateTime.UtcNow,
                    payload = new
                    {
                        status = "ready",
                        stream = "live_sync",
                    },
                }, cancellationToken).ConfigureAwait(false);

                await SendLiveSyncMediaSnapshotAsync(subscription, cancellationToken, force: true).ConfigureAwait(false);
                await SendLiveSyncDownloaderSnapshotAsync(subscription, cancellationToken, force: true).ConfigureAwait(false);
                await SendLiveSyncDjSnapshotAsync(subscription, cancellationToken, force: true).ConfigureAwait(false);
                EnsureLiveSyncLoopRunning();

                var receiveBuffer = new byte[4096];

                while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
                {
                    using var messageBuffer = new MemoryStream();
                    WebSocketReceiveResult result;

                    do
                    {
                        result = await socket.ReceiveAsync(
                            new ArraySegment<byte>(receiveBuffer),
                            cancellationToken).ConfigureAwait(false);

                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            await socket.CloseAsync(
                                WebSocketCloseStatus.NormalClosure,
                                "closing",
                                cancellationToken).ConfigureAwait(false);
                            return;
                        }

                        if (result.Count > 0)
                        {
                            messageBuffer.Write(receiveBuffer, 0, result.Count);
                        }
                    }
                    while (!result.EndOfMessage);

                    if (result.MessageType != WebSocketMessageType.Text)
                    {
                        continue;
                    }

                    var payload = Encoding.UTF8.GetString(messageBuffer.ToArray());
                    if (string.IsNullOrWhiteSpace(payload))
                    {
                        continue;
                    }

                    try
                    {
                        using var doc = JsonDocument.Parse(payload);
                        var root = doc.RootElement;
                        var type = root.TryGetProperty("type", out var typeProp)
                            ? typeProp.GetString() ?? string.Empty
                            : string.Empty;

                        if (string.Equals(type, "refresh", StringComparison.OrdinalIgnoreCase))
                        {
                            await SendLiveSyncMediaSnapshotAsync(subscription, cancellationToken, force: true).ConfigureAwait(false);
                            await SendLiveSyncDownloaderSnapshotAsync(subscription, cancellationToken, force: true).ConfigureAwait(false);
                            await SendLiveSyncDjSnapshotAsync(subscription, cancellationToken, force: true).ConfigureAwait(false);
                        }
                    }
                    catch (JsonException)
                    {
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogWarning($"[Companion] Live sync socket error: {ex.Message}");
            }
            finally
            {
                if (subscription != null)
                {
                    UnregisterLiveSyncSubscription(subscription.Id);
                }

                if (socket != null)
                {
                    try
                    {
                        if (socket.State == WebSocketState.Open || socket.State == WebSocketState.CloseReceived)
                        {
                            await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None)
                                .ConfigureAwait(false);
                        }
                    }
                    catch
                    {
                    }

                    socket.Dispose();
                }
            }
        }

        private async Task ProcessAudioAsync(
            WebSocket socket,
            byte[] pcmBytes,
            string? requestId,
            CancellationToken cancellationToken)
        {
            if (pcmBytes.Length < 3200)
            {
                await SendSocketMessageAsync(socket, new { type = "error", message = "Audio stream too short." }, cancellationToken)
                    .ConfigureAwait(false);
                return;
            }

            await SendSocketMessageAsync(socket, new { type = "status", message = "transcribing" }, cancellationToken)
                .ConfigureAwait(false);

            var wavBytes = WrapPcmAsWave(pcmBytes, new WaveFormat(16000, 16, 1));
            using var recognizer = new WhisperSpeechRecognition();

            if (!recognizer.IsConfigured)
            {
                await SendSocketMessageAsync(socket, new { type = "error", message = "Desktop speech recognition is not configured." }, cancellationToken)
                    .ConfigureAwait(false);
                return;
            }

            var transcript = await recognizer.TranscribeWavBytesAsync(wavBytes).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(transcript))
            {
                await SendSocketMessageAsync(socket, new { type = "error", message = "Atlas could not transcribe the audio." }, cancellationToken)
                    .ConfigureAwait(false);
                return;
            }

            transcript = transcript.Trim();
            await SendSocketMessageAsync(socket, new
            {
                type = "transcript",
                text = transcript,
                requestId,
            }, cancellationToken)
                .ConfigureAwait(false);

            RegisterPendingReply(requestId, socket);
            await SubmitTranscriptToAtlasAsync(transcript).ConfigureAwait(false);

            await SendSocketMessageAsync(socket, new
            {
                type = "submitted",
                text = transcript,
                requestId,
            }, cancellationToken)
                .ConfigureAwait(false);
        }

        public string? GetActiveReplyRequestId() => _activeReplyRequestId.Value;

        public void NotifyConversationStateChanged(string? conversationId)
        {
            _ = Task.Run(() => PublishConversationStateChangedAsync(conversationId));
        }

        public void UpdateConversationProgress(string? conversationId, bool isThinking, string? status)
        {
            if (string.IsNullOrWhiteSpace(conversationId))
            {
                return;
            }

            lock (_subscriptionLock)
            {
                if (isThinking)
                {
                    _conversationProgress[conversationId] = new ConversationProgressState(
                        true,
                        string.IsNullOrWhiteSpace(status) ? "Atlas is thinking..." : status.Trim(),
                        DateTime.UtcNow);
                }
                else
                {
                    _conversationProgress.Remove(conversationId);
                }
            }

            NotifyConversationStateChanged(conversationId);
        }

        public async Task PublishReplyAudioAsync(AssistantUtterance utterance, string audioFilePath, string? requestId = null)
        {
            if (!ShouldPublishReplyAudio(utterance) || string.IsNullOrWhiteSpace(audioFilePath))
            {
                return;
            }

            PendingReplySession? session;
            PendingConversationReply? conversationReply;
            lock (_replyLock)
            {
                CleanupExpiredPendingReplies_NoLock();
                CleanupExpiredConversationReplies_NoLock();
                CleanupOldReplyMedia_NoLock();
                session = TryTakePendingReplySession_NoLock(requestId) ?? DequeueNextPendingReply_NoLock();
                conversationReply = session == null
                    ? TryTakePendingConversationReply_NoLock(requestId)
                    : null;
            }

            if (session == null && conversationReply == null)
            {
                return;
            }

            var media = CopyReplyMedia(audioFilePath);
            if (media == null)
            {
                return;
            }

            if (conversationReply != null)
            {
                conversationReply.Completion.TrySetResult(media);
                return;
            }

            if (session == null || session.Socket.State != WebSocketState.Open)
            {
                return;
            }

            try
            {
                await SendSocketMessageAsync(session.Socket, new
                {
                    type = "reply_audio",
                    requestId = session.RequestId,
                    text = utterance.Text,
                    audioPath = BuildAuthorizedRelativePath($"/media/replies/{media.FileName}"),
                    contentType = media.ContentType,
                }, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AppLogger.LogWarning($"[Companion] Failed to push reply audio: {ex.Message}");
            }
        }

        private static bool ShouldPublishReplyAudio(AssistantUtterance utterance)
        {
            return utterance.Source is UtteranceSource.Conversation
                or UtteranceSource.LLM
                or UtteranceSource.Template
                or UtteranceSource.Macro
                or UtteranceSource.Action
                or UtteranceSource.Web;
        }

        private static byte[] WrapPcmAsWave(byte[] pcmBytes, WaveFormat waveFormat)
        {
            using var output = new MemoryStream();
            using (var writer = new WaveFileWriter(output, waveFormat))
            {
                writer.Write(pcmBytes, 0, pcmBytes.Length);
                writer.Flush();
            }

            return output.ToArray();
        }

        private async Task SubmitTranscriptToAtlasAsync(string transcript)
        {
            await Application.Current.Dispatcher.InvokeAsync(async () =>
            {
                try
                {
                    if (VoiceSystemOrchestrator.Instance.SubmitMessageHandler != null)
                    {
                        VoiceSystemOrchestrator.Instance.SubmitMessageHandler(transcript);
                        return;
                    }

                    var chatWindow = await GetOrCreateRemoteChatBackendAsync().ConfigureAwait(true);

                    chatWindow.SubmitRemoteVoiceMessage(transcript);
                }
                catch (Exception ex)
                {
                    AppLogger.LogWarning($"[Companion] Failed to submit transcript to Atlas: {ex.Message}");
                }
            }).Task.Unwrap().ConfigureAwait(false);
        }

        private async Task HandleConversationRequestAsync(HttpListenerContext context)
        {
            RemoteConversationRequest? request;

            try
            {
                request = await ReadJsonAsync<RemoteConversationRequest>(context.Request).ConfigureAwait(false);
            }
            catch (JsonException ex)
            {
                context.Response.StatusCode = 400;
                await WriteJsonAsync(context, new { error = $"Invalid JSON payload: {ex.Message}" }).ConfigureAwait(false);
                return;
            }

            var message = request?.Message?.Trim();
            if (string.IsNullOrWhiteSpace(message))
            {
                context.Response.StatusCode = 400;
                await WriteJsonAsync(context, new { error = "A non-empty 'message' field is required." }).ConfigureAwait(false);
                return;
            }

            try
            {
                var requestedConversationId = request?.ConversationId;
                var deviceName = request?.Device;
                var boundConversationId = string.IsNullOrWhiteSpace(deviceName)
                    ? null
                    : GetDeviceConversationId(deviceName);
                var effectiveConversationId = !string.IsNullOrWhiteSpace(requestedConversationId)
                    ? requestedConversationId
                    : boundConversationId;
                var shouldStartNewConversation = request?.StartNewConversation == true ||
                    (string.IsNullOrWhiteSpace(effectiveConversationId) && !string.IsNullOrWhiteSpace(deviceName));
                var requestId = request?.IncludeReplyAudio == true
                    ? Guid.NewGuid().ToString("N")
                    : null;
                var pendingAudioTask = requestId == null
                    ? null
                    : RegisterPendingConversationReply(requestId);

                var (reply, conversationId) = await SubmitRemoteTextToAtlasAsync(
                        message,
                        effectiveConversationId,
                        deviceName,
                        requestId,
                        shouldStartNewConversation)
                    .ConfigureAwait(false);

                BindDeviceConversation(deviceName, conversationId);

                ReplyMediaItem? audioMedia = null;
                if (pendingAudioTask != null)
                {
                    audioMedia = await AwaitConversationReplyAudioAsync(requestId!, pendingAudioTask).ConfigureAwait(false);
                }

                await WriteJsonAsync(context, new
                {
                    reply,
                    conversationId,
                    audioPath = audioMedia == null ? null : BuildAuthorizedRelativePath($"/media/replies/{audioMedia.FileName}"),
                    contentType = audioMedia?.ContentType,
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AppLogger.LogWarning($"[Companion] Remote conversation failed: {ex.Message}");
                context.Response.StatusCode = 500;
                await WriteJsonAsync(context, new { error = ex.Message }).ConfigureAwait(false);
            }
        }

        private object BuildSystemStatusResponse()
        {
            var status = GetStatus();
            var settings = SettingsStore.Current;
            var companionFileTransfer = settings.CompanionFileTransfer ?? new CompanionFileTransferSettings();
            var defaultUserProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var preferredSftpUsername = companionFileTransfer.UseDedicatedAccount && !string.IsNullOrWhiteSpace(companionFileTransfer.DedicatedUsername)
                ? companionFileTransfer.DedicatedUsername.Trim()
                : Environment.UserName;
            var preferredSftpInitialPath = companionFileTransfer.UseDedicatedAccount && !string.IsNullOrWhiteSpace(companionFileTransfer.DedicatedFolderPath)
                ? companionFileTransfer.DedicatedFolderPath.Trim()
                : defaultUserProfile;
            var systemDrive = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
            var diskUsage = 0d;

            try
            {
                var drive = new DriveInfo(systemDrive);
                if (drive.IsReady && drive.TotalSize > 0)
                {
                    diskUsage = Math.Round((1d - ((double)drive.AvailableFreeSpace / drive.TotalSize)) * 100d, 1);
                }
            }
            catch
            {
            }

            var hardwareSnapshot = _hardwareMetricsService.GetSnapshot();
            var networkRates = ReadNetworkThroughput();

            var runningServices = new List<string>
            {
                "companion-api",
                "smart-home",
                "conversation",
                "voice"
            };

            if (MediaCentreViewModel.Instance != null)
            {
                runningServices.Add("media");
            }

            return new
            {
                app = "Atlas.OS",
                status = "online",
                port = Port,
                lifecycleState = status.LifecycleState,
                recommendedBaseUrl = status.RecommendedBaseUrl,
                lastStartupError = status.LastStartupError,
                voiceStreaming = true,
                replyAudio = true,
                conversationStreaming = true,
                audioStreamPath = AudioStreamPath,
                conversationStreamPath = ConversationStreamPath,
                lanAddresses = GetLocalIpv4Addresses().ToArray(),
                cpuUsage = Math.Round(hardwareSnapshot.Cpu, 1),
                memoryUsage = Math.Round(hardwareSnapshot.Ram, 1),
                diskUsage = Math.Round(hardwareSnapshot.Disk > 0 ? hardwareSnapshot.Disk : diskUsage, 1),
                networkUpload = Math.Round(networkRates.UploadBytesPerSecond, 1),
                networkDownload = Math.Round(networkRates.DownloadBytesPerSecond, 1),
                activeConnections = IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpConnections().Length,
                uptime = TimeSpan.FromMilliseconds(Environment.TickCount64).ToString(@"dd\.hh\:mm\:ss"),
                runningServices = runningServices.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                remoteDesktopStreamPath = RemoteDesktopStreamPath,
                currentUser = Environment.UserName,
                userHomeDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                preferredSftpUsername,
                preferredSftpInitialPath,
                usesDedicatedSftpAccount = companionFileTransfer.UseDedicatedAccount,
                recommendedSftpPort = 22,
            };
        }

        private (double UploadBytesPerSecond, double DownloadBytesPerSecond) ReadNetworkThroughput()
        {
            try
            {
                var interfaces = NetworkInterface.GetAllNetworkInterfaces()
                    .Where(networkInterface =>
                        networkInterface.OperationalStatus == OperationalStatus.Up &&
                        networkInterface.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                        networkInterface.NetworkInterfaceType != NetworkInterfaceType.Tunnel)
                    .ToList();

                long totalSent = 0;
                long totalReceived = 0;
                foreach (var networkInterface in interfaces)
                {
                    try
                    {
                        var stats = networkInterface.GetIPv4Statistics();
                        totalSent += stats.BytesSent;
                        totalReceived += stats.BytesReceived;
                    }
                    catch
                    {
                    }
                }

                lock (_networkSampleLock)
                {
                    var now = DateTime.UtcNow;
                    if (_lastNetworkSampleUtc == DateTime.MinValue)
                    {
                        _lastNetworkSampleUtc = now;
                        _lastNetworkBytesSent = totalSent;
                        _lastNetworkBytesReceived = totalReceived;
                        return (0d, 0d);
                    }

                    var elapsedSeconds = Math.Max((now - _lastNetworkSampleUtc).TotalSeconds, 0.1d);
                    var uploadRate = Math.Max(0d, (totalSent - _lastNetworkBytesSent) / elapsedSeconds);
                    var downloadRate = Math.Max(0d, (totalReceived - _lastNetworkBytesReceived) / elapsedSeconds);

                    _lastNetworkSampleUtc = now;
                    _lastNetworkBytesSent = totalSent;
                    _lastNetworkBytesReceived = totalReceived;

                    return (uploadRate, downloadRate);
                }
            }
            catch
            {
                return (0d, 0d);
            }
        }

        private async Task HandleAssistantStateAsync(HttpListenerContext context)
        {
            try
            {
                await WriteJsonAsync(context, await BuildAssistantStateResponseAsync().ConfigureAwait(false)).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AppLogger.LogWarning($"[Companion] Assistant state request failed: {ex.Message}");
                context.Response.StatusCode = 500;
                await WriteJsonAsync(context, new { error = ex.Message }).ConfigureAwait(false);
            }
        }

        private async Task HandleAssistantUpdateAsync(HttpListenerContext context)
        {
            AssistantStateUpdateRequest? request;

            try
            {
                request = await ReadJsonAsync<AssistantStateUpdateRequest>(context.Request).ConfigureAwait(false);
            }
            catch (JsonException ex)
            {
                context.Response.StatusCode = 400;
                await WriteJsonAsync(context, new { error = $"Invalid JSON payload: {ex.Message}" }).ConfigureAwait(false);
                return;
            }

            if (request == null ||
                string.IsNullOrWhiteSpace(request.AiProvider) &&
                string.IsNullOrWhiteSpace(request.PersonalityId) &&
                string.IsNullOrWhiteSpace(request.VoiceProvider) &&
                string.IsNullOrWhiteSpace(request.VoiceId))
            {
                context.Response.StatusCode = 400;
                await WriteJsonAsync(context, new { error = "At least one assistant setting field is required." }).ConfigureAwait(false);
                return;
            }

            try
            {
                if (!string.IsNullOrWhiteSpace(request.AiProvider))
                {
                    if (!Enum.TryParse<AIProviderType>(request.AiProvider.Trim(), true, out var aiProviderType))
                    {
                        context.Response.StatusCode = 400;
                        await WriteJsonAsync(context, new { error = $"Unsupported AI provider '{request.AiProvider}'." }).ConfigureAwait(false);
                        return;
                    }

                    await AIManager.SetActiveProviderAsync(aiProviderType).ConfigureAwait(false);
                }

                if (!string.IsNullOrWhiteSpace(request.PersonalityId))
                {
                    var personalityId = request.PersonalityId.Trim();
                    if (PersonalityRegistry.GetById(personalityId) == null)
                    {
                        context.Response.StatusCode = 404;
                        await WriteJsonAsync(context, new { error = $"Assistant personality '{request.PersonalityId}' was not found." }).ConfigureAwait(false);
                        return;
                    }

                    SettingsStore.Update(settings =>
                    {
                        settings.PersonalitySelected = personalityId;
                    });
                }

                if (!string.IsNullOrWhiteSpace(request.VoiceProvider) || !string.IsNullOrWhiteSpace(request.VoiceId))
                {
                    var voiceUpdate = await ApplyAssistantVoiceUpdateAsync(request.VoiceProvider, request.VoiceId).ConfigureAwait(false);
                    if (!voiceUpdate.Success)
                    {
                        context.Response.StatusCode = 400;
                        await WriteJsonAsync(context, new { error = voiceUpdate.ErrorMessage ?? "Voice update failed." }).ConfigureAwait(false);
                        return;
                    }
                }

                await WriteJsonAsync(context, await BuildAssistantStateResponseAsync().ConfigureAwait(false)).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AppLogger.LogWarning($"[Companion] Assistant update request failed: {ex.Message}");
                context.Response.StatusCode = 500;
                await WriteJsonAsync(context, new { error = ex.Message }).ConfigureAwait(false);
            }
        }

        private async Task<object> BuildAssistantStateResponseAsync()
        {
            var settings = SettingsStore.Current;
            var voiceState = await GetAssistantVoiceStateAsync().ConfigureAwait(false);
            var selectedPersonality = FirstNonEmpty(settings.PersonalitySelected, PersonalityRegistry.GetDefault().Id) ?? PersonalityRegistry.GetDefault().Id;
            var currentAiProvider = AIManager.GetActiveProvider();

            var aiProviders = Enum.GetValues(typeof(AIProviderType))
                .Cast<AIProviderType>()
                .Select(providerType =>
                {
                    var provider = AIManager.GetProvider(providerType);
                    return new
                    {
                        id = providerType.ToString(),
                        displayName = provider?.DisplayName ?? providerType.ToString(),
                        isConfigured = provider?.IsConfigured ?? false,
                        isActive = providerType == currentAiProvider,
                    };
                })
                .ToArray();

            var personalities = PersonalityRegistry.GetAvailable()
                .Select(definition => new
                {
                    id = definition.Id,
                    displayName = definition.DisplayName,
                    description = definition.Description,
                    icon = definition.Icon,
                    domain = definition.Domain,
                    isActive = string.Equals(definition.Id, selectedPersonality, StringComparison.OrdinalIgnoreCase),
                })
                .ToArray();

            var voiceProviders = Enum.GetValues(typeof(VoiceProviderType))
                .Cast<VoiceProviderType>()
                .Select(providerType => new
                {
                    id = providerType.ToString(),
                    displayName = GetVoiceProviderDisplayName(providerType),
                    isConfigured = IsVoiceProviderConfigured(providerType),
                    isActive = providerType == voiceState.ActiveProvider,
                })
                .ToArray();

            return new
            {
                aiProvider = currentAiProvider.ToString(),
                personalityId = selectedPersonality,
                voiceProvider = voiceState.ActiveProvider.ToString(),
                voiceId = voiceState.SelectedVoiceId,
                globalVoiceId = voiceState.GlobalVoiceId,
                aiProviders,
                personalities,
                voiceProviders,
                voices = voiceState.Voices.Select(voice => new
                {
                    id = voice.Id,
                    displayName = voice.DisplayName,
                    category = voice.Category,
                    provider = voice.Provider,
                    description = voice.Description,
                    isCloud = voice.IsCloud,
                    isSelected = string.Equals(voice.Id, voiceState.SelectedVoiceId, StringComparison.Ordinal),
                }).ToArray(),
            };
        }

        private async Task<AssistantVoiceStateSnapshot> BuildFallbackAssistantVoiceStateAsync(
            VoiceProviderType persistedProvider,
            string globalVoiceId,
            string? selectedVoiceId)
        {
            var effectiveVoiceId = string.IsNullOrWhiteSpace(selectedVoiceId) ? globalVoiceId : selectedVoiceId;
            var fallbackVoices = persistedProvider == VoiceProviderType.ElevenLabs
                ? (await VoiceCatalogService.Instance.GetVoicesAsync().ConfigureAwait(false))
                    .Select(voice => new AssistantVoiceOption(
                        voice.VoiceId,
                        voice.DisplayName,
                        string.Empty,
                        voice.Provider,
                        voice.Description,
                        true))
                    .ToArray()
                : Array.Empty<AssistantVoiceOption>();

            return new AssistantVoiceStateSnapshot(
                persistedProvider,
                effectiveVoiceId,
                globalVoiceId,
                fallbackVoices);
        }

        private async Task<AssistantVoiceStateSnapshot> GetAssistantVoiceStateAsync()
        {
            var settings = SettingsStore.Current;
            var globalVoiceId = VoicePreferences.Current.GlobalVoiceId;
            var persistedProvider = Enum.TryParse<VoiceProviderType>(settings.VoiceRuntime.Provider, true, out var parsedProvider)
                ? parsedProvider
                : VoiceProviderType.ElevenLabs;

            if (Application.Current == null)
            {
                return await BuildFallbackAssistantVoiceStateAsync(
                    persistedProvider,
                    globalVoiceId,
                    settings.VoiceRuntime.VoiceId).ConfigureAwait(false);
            }

            return await Application.Current.Dispatcher.InvokeAsync(async () =>
            {
                var voiceManager = GetActiveVoiceManager();
                if (voiceManager == null)
                {
                    return await BuildFallbackAssistantVoiceStateAsync(
                        persistedProvider,
                        globalVoiceId,
                        settings.VoiceRuntime.VoiceId).ConfigureAwait(true);
                }

                await voiceManager.WaitForInitializationAsync().ConfigureAwait(true);
                var voices = await voiceManager.GetVoicesAsync().ConfigureAwait(true);

                var options = voices
                    .Select(voice => new AssistantVoiceOption(
                        voice.Id,
                        voice.DisplayName ?? voice.Id,
                        voice.Category ?? string.Empty,
                        voice.Provider.ToString(),
                        string.Empty,
                        voice.IsCloud))
                    .ToArray();

                return new AssistantVoiceStateSnapshot(
                    voiceManager.ActiveProviderType,
                    voiceManager.SelectedVoice?.Id ?? settings.VoiceRuntime.VoiceId,
                    VoicePreferences.Current.GlobalVoiceId,
                    options);
            }).Task.Unwrap().ConfigureAwait(false);
        }

        private async Task<AssistantVoiceUpdateResult> ApplyAssistantVoiceUpdateAsync(string? rawProvider, string? rawVoiceId)
        {
            if (Application.Current == null)
            {
                return new AssistantVoiceUpdateResult(false, "Atlas UI is not available.");
            }

            return await Application.Current.Dispatcher.InvokeAsync(async () =>
            {
                var voiceManager = GetActiveVoiceManager();
                if (voiceManager == null)
                {
                    return new AssistantVoiceUpdateResult(false, "Atlas voice system is not available.");
                }

                await voiceManager.WaitForInitializationAsync().ConfigureAwait(true);

                if (!string.IsNullOrWhiteSpace(rawProvider))
                {
                    if (!Enum.TryParse<VoiceProviderType>(rawProvider.Trim(), true, out var providerType))
                    {
                        return new AssistantVoiceUpdateResult(false, $"Unsupported voice provider '{rawProvider}'.");
                    }

                    var providerReady = await voiceManager.SetProviderAsync(providerType).ConfigureAwait(true);
                    if (!providerReady)
                    {
                        return new AssistantVoiceUpdateResult(false, $"Voice provider '{providerType}' is not available on the desktop.");
                    }
                }

                var targetVoiceId = (rawVoiceId ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(targetVoiceId))
                {
                    targetVoiceId = voiceManager.SelectedVoice?.Id ?? SettingsStore.Current.VoiceRuntime.VoiceId;
                }

                if (string.IsNullOrWhiteSpace(targetVoiceId))
                {
                    var availableVoices = await voiceManager.GetVoicesAsync().ConfigureAwait(true);
                    targetVoiceId = availableVoices.FirstOrDefault()?.Id ?? string.Empty;
                }

                if (string.IsNullOrWhiteSpace(targetVoiceId))
                {
                    return new AssistantVoiceUpdateResult(false, "No voices are available for the selected voice provider.");
                }

                var voiceSelected = await voiceManager.SelectVoiceAsync(targetVoiceId).ConfigureAwait(true);
                if (!voiceSelected)
                {
                    return new AssistantVoiceUpdateResult(false, $"Voice '{targetVoiceId}' is not available on the selected provider.");
                }

                VoicePreferences.Current.SetGlobalVoice(targetVoiceId);
                return new AssistantVoiceUpdateResult(true, null);
            }).Task.Unwrap().ConfigureAwait(false);
        }

        private static CommandCenterWindow? GetPrimaryCommandCenterWindow()
        {
            return Application.Current?.Windows
                .OfType<CommandCenterWindow>()
                .OrderByDescending(window => window.IsActive)
                .FirstOrDefault();
        }

        private static VoiceManager? GetActiveVoiceManager()
        {
            var commandCenter = GetPrimaryCommandCenterWindow();
            if (commandCenter != null)
                return commandCenter.VoiceManager;

            return Application.Current?.Windows
                .OfType<ChatWindow>()
                .FirstOrDefault(window => window.IsLoaded && window.IsVisible)
                ?.VoiceManager;
        }

        private static bool IsVoiceProviderConfigured(VoiceProviderType providerType)
        {
            return providerType switch
            {
                VoiceProviderType.WindowsSAPI => true,
                VoiceProviderType.EdgeTTS => true,
                VoiceProviderType.ElevenLabs => SettingsStore.TryGetVoiceProviderKey("elevenlabs", out _),
                VoiceProviderType.OpenAI => SettingsStore.TryGetVoiceProviderKey("openai", out _),
                _ => false,
            };
        }

        private static string GetVoiceProviderDisplayName(VoiceProviderType providerType)
        {
            return providerType switch
            {
                VoiceProviderType.WindowsSAPI => "Windows SAPI",
                VoiceProviderType.EdgeTTS => "Edge TTS",
                VoiceProviderType.OpenAI => "OpenAI TTS",
                VoiceProviderType.ElevenLabs => "ElevenLabs",
                _ => providerType.ToString(),
            };
        }

        private async Task HandleAiChatAsync(HttpListenerContext context)
        {
            AiChatRequest? request;

            try
            {
                request = await ReadJsonAsync<AiChatRequest>(context.Request).ConfigureAwait(false);
            }
            catch (JsonException ex)
            {
                context.Response.StatusCode = 400;
                await WriteJsonAsync(context, new { error = $"Invalid JSON payload: {ex.Message}" }).ConfigureAwait(false);
                return;
            }

            var message = request?.Message?.Trim();
            if (string.IsNullOrWhiteSpace(message))
            {
                context.Response.StatusCode = 400;
                await WriteJsonAsync(context, new { error = "A non-empty 'message' field is required." }).ConfigureAwait(false);
                return;
            }

            try
            {
                var deviceName = string.IsNullOrWhiteSpace(request?.Device)
                    ? "iPhone Companion"
                    : request!.Device!.Trim();
                var boundConversationId = GetDeviceConversationId(deviceName);
                var effectiveConversationId = !string.IsNullOrWhiteSpace(request?.ConversationId)
                    ? request!.ConversationId
                    : boundConversationId;
                var shouldStartNewConversation = string.IsNullOrWhiteSpace(effectiveConversationId);

                if (request?.WaitForReply == false)
                {
                    if (shouldStartNewConversation)
                    {
                        effectiveConversationId = await StartRemoteConversationAsync(deviceName).ConfigureAwait(false);
                        shouldStartNewConversation = false;
                    }

                    var queuedConversationId = effectiveConversationId;
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await SubmitRemoteTextToAtlasAsync(
                                message,
                                queuedConversationId,
                                deviceName,
                                requestId: null,
                                startNewConversation: false).ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            AppLogger.LogWarning($"[Companion] Async AI chat dispatch failed: {ex.Message}");
                        }
                    });

                    await WriteJsonAsync(context, new
                    {
                        accepted = true,
                        conversationId = queuedConversationId,
                    }).ConfigureAwait(false);
                    return;
                }

                var requestId = Guid.NewGuid().ToString("N");
                var pendingAudioTask = RegisterPendingConversationReply(requestId);

                var (reply, conversationId) = await SubmitRemoteTextToAtlasAsync(
                        message,
                        effectiveConversationId,
                        deviceName,
                        requestId,
                        startNewConversation: shouldStartNewConversation)
                    .ConfigureAwait(false);

                var audioMedia = await AwaitConversationReplyAudioAsync(requestId, pendingAudioTask).ConfigureAwait(false);

                await WriteJsonAsync(context, new
                {
                    response = reply,
                    conversationId,
                    audioPath = audioMedia == null ? null : BuildAuthorizedRelativePath($"/media/replies/{audioMedia.FileName}"),
                    contentType = audioMedia?.ContentType,
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AppLogger.LogWarning($"[Companion] AI chat alias failed: {ex.Message}");
                context.Response.StatusCode = 500;
                await WriteJsonAsync(context, new { error = ex.Message }).ConfigureAwait(false);
            }
        }

        private async Task HandleAiVoiceAsync(HttpListenerContext context)
        {
            AiVoiceRequest? request;

            try
            {
                request = await ReadJsonAsync<AiVoiceRequest>(context.Request).ConfigureAwait(false);
            }
            catch (JsonException ex)
            {
                context.Response.StatusCode = 400;
                await WriteJsonAsync(context, new { error = $"Invalid JSON payload: {ex.Message}" }).ConfigureAwait(false);
                return;
            }

            if (string.IsNullOrWhiteSpace(request?.Audio))
            {
                context.Response.StatusCode = 400;
                await WriteJsonAsync(context, new { error = "A non-empty 'audio' field is required." }).ConfigureAwait(false);
                return;
            }

            try
            {
                var audioBytes = DecodeBase64Audio(request.Audio);
                using var recognizer = new WhisperSpeechRecognition();
                if (!recognizer.IsConfigured)
                {
                    context.Response.StatusCode = 503;
                    await WriteJsonAsync(context, new { error = "Desktop speech recognition is not configured." }).ConfigureAwait(false);
                    return;
                }

                var transcript = await recognizer.TranscribeWavBytesAsync(audioBytes).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(transcript))
                {
                    context.Response.StatusCode = 422;
                    await WriteJsonAsync(context, new { error = "Atlas could not transcribe the provided audio." }).ConfigureAwait(false);
                    return;
                }

                transcript = transcript.Trim();
                var requestId = Guid.NewGuid().ToString("N");
                var pendingAudioTask = RegisterPendingConversationReply(requestId);

                var (reply, conversationId) = await SubmitRemoteTextToAtlasAsync(
                        transcript,
                        request.ConversationId,
                        request.Device ?? "iPhone Companion",
                        requestId,
                        startNewConversation: string.IsNullOrWhiteSpace(request.ConversationId))
                    .ConfigureAwait(false);

                var audioMedia = await AwaitConversationReplyAudioAsync(requestId, pendingAudioTask).ConfigureAwait(false);

                await WriteJsonAsync(context, new
                {
                    response = reply,
                    transcript,
                    conversationId,
                    audioPath = audioMedia == null ? null : BuildAuthorizedRelativePath($"/media/replies/{audioMedia.FileName}"),
                    contentType = audioMedia?.ContentType,
                }).ConfigureAwait(false);
            }
            catch (FormatException ex)
            {
                context.Response.StatusCode = 400;
                await WriteJsonAsync(context, new { error = ex.Message }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AppLogger.LogWarning($"[Companion] AI voice alias failed: {ex.Message}");
                context.Response.StatusCode = 500;
                await WriteJsonAsync(context, new { error = ex.Message }).ConfigureAwait(false);
            }
        }

        private async Task HandleConversationResetAsync(HttpListenerContext context)
        {
            RemoteConversationResetRequest? request;

            try
            {
                request = await ReadJsonAsync<RemoteConversationResetRequest>(context.Request).ConfigureAwait(false);
            }
            catch (JsonException ex)
            {
                context.Response.StatusCode = 400;
                await WriteJsonAsync(context, new { error = $"Invalid JSON payload: {ex.Message}" }).ConfigureAwait(false);
                return;
            }

            try
            {
                var conversationId = await StartRemoteConversationAsync(request?.Device).ConfigureAwait(false);
                await WriteJsonAsync(context, new { conversationId }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AppLogger.LogWarning($"[Companion] Remote conversation reset failed: {ex.Message}");
                context.Response.StatusCode = 500;
                await WriteJsonAsync(context, new { error = ex.Message }).ConfigureAwait(false);
            }
        }

        private async Task HandleConversationStateAsync(HttpListenerContext context)
        {
            try
            {
                var requestedConversationId = context.Request.QueryString["conversationId"];
                var deviceName = context.Request.QueryString["device"];
                var boundConversationId = string.IsNullOrWhiteSpace(deviceName)
                    ? null
                    : GetDeviceConversationId(deviceName);
                var effectiveConversationId = !string.IsNullOrWhiteSpace(requestedConversationId)
                    ? requestedConversationId
                    : boundConversationId;

                var state = await GetConversationStateAsync(
                        effectiveConversationId,
                        deviceName,
                        createIfMissing: !string.IsNullOrWhiteSpace(deviceName))
                    .ConfigureAwait(false);

                BindDeviceConversation(deviceName, state.ConversationId);

                await WriteJsonAsync(context, new
                {
                    conversationId = state.ConversationId,
                    title = state.Title,
                    isAssistantThinking = GetConversationProgressState(state.ConversationId)?.IsThinking ?? false,
                    assistantStatus = GetConversationProgressState(state.ConversationId)?.Status,
                    messages = state.Messages.Select(message => new
                    {
                        id = message.Id,
                        role = message.Role,
                        text = message.Text,
                        timestamp = message.Timestamp,
                        isVoiceInput = message.IsVoiceInput,
                    }).ToArray(),
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AppLogger.LogWarning($"[Companion] Remote conversation state failed: {ex.Message}");
                context.Response.StatusCode = 500;
                await WriteJsonAsync(context, new { error = ex.Message }).ConfigureAwait(false);
            }
        }

        private async Task HandleBillingPlansAsync(HttpListenerContext context)
        {
            try
            {
                var plans = AtlasEconomyService.Instance.GetPlanCatalog()
                    .Select(plan => new
                    {
                        id = plan.Id,
                        displayName = plan.DisplayName,
                        monthlyPriceUsd = plan.MonthlyPriceUsd,
                        monthlyCredits = plan.MonthlyCredits,
                        defaultMode = plan.DefaultMode,
                        maxMode = plan.MaxMode,
                        entitlements = plan.Entitlements,
                    })
                    .ToArray();

                await WriteJsonAsync(context, new
                {
                    generatedAtUtc = DateTime.UtcNow,
                    plans,
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AppLogger.LogWarning($"[Companion] Billing plans request failed: {ex.Message}");
                context.Response.StatusCode = 500;
                await WriteJsonAsync(context, new { error = ex.Message }).ConfigureAwait(false);
            }
        }

        private async Task HandleBillingStateAsync(HttpListenerContext context)
        {
            try
            {
                await WriteJsonAsync(context, AtlasEconomyService.Instance.GetSnapshot()).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AppLogger.LogWarning($"[Companion] Billing state request failed: {ex.Message}");
                context.Response.StatusCode = 500;
                await WriteJsonAsync(context, new { error = ex.Message }).ConfigureAwait(false);
            }
        }

        private async Task HandleBillingLedgerAsync(HttpListenerContext context)
        {
            try
            {
                var limit = 50;
                if (int.TryParse(context.Request.QueryString["limit"], out var parsedLimit))
                    limit = Math.Clamp(parsedLimit, 1, 200);

                await WriteJsonAsync(context, new
                {
                    generatedAtUtc = DateTime.UtcNow,
                    entries = AtlasEconomyService.Instance.GetLedgerEntries(limit),
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AppLogger.LogWarning($"[Companion] Billing ledger request failed: {ex.Message}");
                context.Response.StatusCode = 500;
                await WriteJsonAsync(context, new { error = ex.Message }).ConfigureAwait(false);
            }
        }

        private async Task HandleBillingModeAsync(HttpListenerContext context)
        {
            CompanionBillingModeRequest? request;

            try
            {
                request = await ReadJsonAsync<CompanionBillingModeRequest>(context.Request).ConfigureAwait(false);
            }
            catch (JsonException ex)
            {
                context.Response.StatusCode = 400;
                await WriteJsonAsync(context, new { error = $"Invalid JSON payload: {ex.Message}" }).ConfigureAwait(false);
                return;
            }

            if (string.IsNullOrWhiteSpace(request?.Mode))
            {
                context.Response.StatusCode = 400;
                await WriteJsonAsync(context, new { error = "A non-empty 'mode' field is required." }).ConfigureAwait(false);
                return;
            }

            try
            {
                await WriteJsonAsync(context, AtlasEconomyService.Instance.SetMode(request.Mode)).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AppLogger.LogWarning($"[Companion] Billing mode request failed: {ex.Message}");
                context.Response.StatusCode = 500;
                await WriteJsonAsync(context, new { error = ex.Message }).ConfigureAwait(false);
            }
        }

        private async Task HandleBillingPlanAsync(HttpListenerContext context)
        {
            CompanionBillingPlanChangeRequest? request;

            try
            {
                request = await ReadJsonAsync<CompanionBillingPlanChangeRequest>(context.Request).ConfigureAwait(false);
            }
            catch (JsonException ex)
            {
                context.Response.StatusCode = 400;
                await WriteJsonAsync(context, new { error = $"Invalid JSON payload: {ex.Message}" }).ConfigureAwait(false);
                return;
            }

            if (string.IsNullOrWhiteSpace(request?.PlanId))
            {
                context.Response.StatusCode = 400;
                await WriteJsonAsync(context, new { error = "A non-empty 'planId' field is required." }).ConfigureAwait(false);
                return;
            }

            try
            {
                await WriteJsonAsync(context, AtlasEconomyService.Instance.ChangePlan(
                    request.PlanId,
                    request.ResetCycle,
                    request.Source ?? "companion-api")).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AppLogger.LogWarning($"[Companion] Billing plan request failed: {ex.Message}");
                context.Response.StatusCode = 500;
                await WriteJsonAsync(context, new { error = ex.Message }).ConfigureAwait(false);
            }
        }

        private async Task HandleBillingTopUpAsync(HttpListenerContext context)
        {
            CompanionBillingTopUpRequest? request;

            try
            {
                request = await ReadJsonAsync<CompanionBillingTopUpRequest>(context.Request).ConfigureAwait(false);
            }
            catch (JsonException ex)
            {
                context.Response.StatusCode = 400;
                await WriteJsonAsync(context, new { error = $"Invalid JSON payload: {ex.Message}" }).ConfigureAwait(false);
                return;
            }

            if ((request?.Credits ?? 0) <= 0)
            {
                context.Response.StatusCode = 400;
                await WriteJsonAsync(context, new { error = "A positive 'credits' field is required." }).ConfigureAwait(false);
                return;
            }

            try
            {
                await WriteJsonAsync(context, AtlasEconomyService.Instance.PurchaseTopUp(
                    request!.Credits,
                    request.Source ?? "companion-api",
                    request.Note ?? string.Empty)).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AppLogger.LogWarning($"[Companion] Billing top-up request failed: {ex.Message}");
                context.Response.StatusCode = 500;
                await WriteJsonAsync(context, new { error = ex.Message }).ConfigureAwait(false);
            }
        }

        private async Task HandleBillingQuoteAsync(HttpListenerContext context)
        {
            CompanionBillingQuoteRequest? request;

            try
            {
                request = await ReadJsonAsync<CompanionBillingQuoteRequest>(context.Request).ConfigureAwait(false);
            }
            catch (JsonException ex)
            {
                context.Response.StatusCode = 400;
                await WriteJsonAsync(context, new { error = $"Invalid JSON payload: {ex.Message}" }).ConfigureAwait(false);
                return;
            }

            if (request == null)
            {
                context.Response.StatusCode = 400;
                await WriteJsonAsync(context, new { error = "A billing quote request body is required." }).ConfigureAwait(false);
                return;
            }

            try
            {
                var quote = AtlasEconomyService.Instance.QuoteUsage(
                    request.Module ?? "conversation",
                    request.Kind ?? "chat",
                    request.Units <= 0 ? 500 : request.Units,
                    TryParseProviderType(request.Provider),
                    request.Model ?? string.Empty,
                    request.ActorId);

                await WriteJsonAsync(context, quote).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AppLogger.LogWarning($"[Companion] Billing quote request failed: {ex.Message}");
                context.Response.StatusCode = 500;
                await WriteJsonAsync(context, new { error = ex.Message }).ConfigureAwait(false);
            }
        }

        private async Task HandleSmartHomeStateAsync(HttpListenerContext context, CancellationToken cancellationToken)
        {
            try
            {
                var snapshot = await _smartHomeRuntimeService.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
                await WriteJsonAsync(context, BuildCompanionSmartHomeStateResponse(snapshot)).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AppLogger.LogWarning($"[Companion] Smart Home state request failed: {ex.Message}");
                context.Response.StatusCode = 500;
                await WriteJsonAsync(context, new { error = ex.Message }).ConfigureAwait(false);
            }
        }

        private async Task HandleSmartHomeDevicesAsync(HttpListenerContext context, CancellationToken cancellationToken)
        {
            try
            {
                var snapshot = await _smartHomeRuntimeService.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
                await WriteJsonAsync(context, new
                {
                    generatedAtUtc = snapshot.GeneratedAtUtc,
                    totalDevices = snapshot.TotalDevices,
                    onlineDevices = snapshot.OnlineDevices,
                    devices = FlattenCompanionDevices(snapshot).ToArray(),
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AppLogger.LogWarning($"[Companion] Smart Home devices request failed: {ex.Message}");
                context.Response.StatusCode = 500;
                await WriteJsonAsync(context, new { error = ex.Message }).ConfigureAwait(false);
            }
        }

        private async Task HandleSmartHomeDeviceToggleAliasAsync(
            HttpListenerContext context,
            string routeDeviceId,
            CancellationToken cancellationToken)
        {
            SmartHomeToggleAliasRequest? request;

            try
            {
                request = await ReadJsonAsync<SmartHomeToggleAliasRequest>(context.Request).ConfigureAwait(false);
            }
            catch (JsonException ex)
            {
                context.Response.StatusCode = 400;
                await WriteJsonAsync(context, new { error = $"Invalid JSON payload: {ex.Message}" }).ConfigureAwait(false);
                return;
            }

            await HandleSmartHomeActionCoreAsync(
                context,
                new CompanionSmartHomeActionRequest
                {
                    DeviceId = routeDeviceId,
                    Id = routeDeviceId,
                    IsOn = request?.IsOn,
                },
                forceToggle: true,
                cancellationToken).ConfigureAwait(false);
        }

        private async Task HandleSecurityAlertsAsync(HttpListenerContext context)
        {
            try
            {
                var alerts = LoadSecurityAlerts()
                    .OrderByDescending(alert => alert.Timestamp)
                    .Take(50)
                    .Select(alert => new
                    {
                        id = alert.Id,
                        timestamp = alert.Timestamp,
                        severity = alert.Severity,
                        message = alert.Message,
                    })
                    .ToArray();

                await WriteJsonAsync(context, new { alerts }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AppLogger.LogWarning($"[Companion] Security alerts request failed: {ex.Message}");
                context.Response.StatusCode = 500;
                await WriteJsonAsync(context, new { error = ex.Message }).ConfigureAwait(false);
            }
        }

        private async Task HandleSecurityCamerasAsync(HttpListenerContext context, CancellationToken cancellationToken)
        {
            try
            {
                var snapshot = await _smartHomeRuntimeService.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
                var cameras = snapshot.Providers
                    .SelectMany(provider => provider.Devices.Select(device => new { provider, device }))
                    .Where(candidate => IsSecurityCameraDevice(candidate.device))
                    .OrderBy(candidate => candidate.device.Name)
                    .ThenBy(candidate => candidate.device.DeviceId)
                    .Select(candidate => BuildCompanionSecurityCamera(candidate.provider, candidate.device))
                    .ToArray();

                await WriteJsonAsync(context, new
                {
                    generatedAtUtc = snapshot.GeneratedAtUtc,
                    cameras,
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AppLogger.LogWarning($"[Companion] Security cameras request failed: {ex.Message}");
                context.Response.StatusCode = 500;
                await WriteJsonAsync(context, new { error = ex.Message }).ConfigureAwait(false);
            }
        }

        private async Task HandleSecurityCameraReconnectAsync(
            HttpListenerContext context,
            string cameraId,
            CancellationToken cancellationToken)
        {
            try
            {
                var snapshot = await _smartHomeRuntimeService.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
                var match = snapshot.Providers
                    .SelectMany(provider => provider.Devices.Select(device => new { provider, device }))
                    .FirstOrDefault(candidate =>
                        string.Equals(candidate.device.DeviceId, cameraId, StringComparison.OrdinalIgnoreCase) &&
                        IsSecurityCameraDevice(candidate.device));

                if (match == null)
                {
                    context.Response.StatusCode = 404;
                    await WriteJsonAsync(context, new { error = $"Security camera '{cameraId}' was not found." }).ConfigureAwait(false);
                    return;
                }

                await WriteJsonAsync(context, new
                {
                    ok = true,
                    message = $"Camera '{match.device.Name}' is reachable from the current Atlas smart-home snapshot.",
                    camera = BuildCompanionSecurityCamera(match.provider, match.device),
                    snapshotGeneratedAtUtc = snapshot.GeneratedAtUtc,
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AppLogger.LogWarning($"[Companion] Security camera reconnect request failed: {ex.Message}");
                context.Response.StatusCode = 500;
                await WriteJsonAsync(context, new { error = ex.Message }).ConfigureAwait(false);
            }
        }

        private async Task HandleSecurityCameraRecordingStartAsync(
            HttpListenerContext context,
            string cameraId,
            CancellationToken cancellationToken)
        {
            try
            {
                var snapshot = await _smartHomeRuntimeService.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
                var match = snapshot.Providers
                    .SelectMany(provider => provider.Devices.Select(device => new { provider, device }))
                    .FirstOrDefault(candidate =>
                        string.Equals(candidate.device.DeviceId, cameraId, StringComparison.OrdinalIgnoreCase) &&
                        IsSecurityCameraDevice(candidate.device));

                if (match == null)
                {
                    context.Response.StatusCode = 404;
                    await WriteJsonAsync(context, new { error = $"Security camera '{cameraId}' was not found." }).ConfigureAwait(false);
                    return;
                }

                var recordingUrl = GetSecurityCameraRecordingUrl(match.device);
                if (string.IsNullOrWhiteSpace(recordingUrl))
                {
                    context.Response.StatusCode = 400;
                    await WriteJsonAsync(context, new
                    {
                        error = "Atlas can only record feeds that expose a direct stream URL.",
                        camera = BuildCompanionSecurityCamera(match.provider, match.device),
                    }).ConfigureAwait(false);
                    return;
                }

                if (_cameraRecordingService.IsRecordingSession(match.device.DeviceId))
                {
                    await WriteJsonAsync(context, new
                    {
                        ok = false,
                        error = $"Atlas is already recording {match.device.Name}.",
                        activeCameraId = match.device.DeviceId,
                        activeCameraName = match.device.Name,
                        recordingPath = _cameraRecordingService.GetRecordingPath(match.device.DeviceId),
                        camera = BuildCompanionSecurityCamera(match.provider, match.device),
                    }).ConfigureAwait(false);
                    return;
                }

                var startResult = await _cameraRecordingService.StartAsync(
                    recordingUrl,
                    match.device.Name,
                    match.device.DeviceId,
                    cancellationToken).ConfigureAwait(false);

                if (startResult.Ok)
                {
                    _activeSecurityRecordingCameraId = match.device.DeviceId;
                    _activeSecurityRecordingCameraName = match.device.Name ?? string.Empty;
                    _activeSecurityRecordingSourceUrl = recordingUrl;
                }

                context.Response.StatusCode = startResult.Ok ? 200 : 400;
                await WriteJsonAsync(context, new
                {
                    ok = startResult.Ok,
                    message = startResult.Message,
                    outputPath = startResult.OutputPath,
                    camera = BuildCompanionSecurityCamera(match.provider, match.device),
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AppLogger.LogWarning($"[Companion] Security camera recording start failed: {ex.Message}");
                context.Response.StatusCode = 500;
                await WriteJsonAsync(context, new { error = ex.Message }).ConfigureAwait(false);
            }
        }

        private async Task HandleSecurityCameraRecordingStopAsync(
            HttpListenerContext context,
            string cameraId,
            CancellationToken cancellationToken)
        {
            try
            {
                var snapshot = await _smartHomeRuntimeService.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
                var match = snapshot.Providers
                    .SelectMany(provider => provider.Devices.Select(device => new { provider, device }))
                    .FirstOrDefault(candidate =>
                        string.Equals(candidate.device.DeviceId, cameraId, StringComparison.OrdinalIgnoreCase) &&
                        IsSecurityCameraDevice(candidate.device));

                if (match == null)
                {
                    context.Response.StatusCode = 404;
                    await WriteJsonAsync(context, new { error = $"Security camera '{cameraId}' was not found." }).ConfigureAwait(false);
                    return;
                }

                var stopResult = await _cameraRecordingService.StopAsync(match.device.DeviceId, cancellationToken).ConfigureAwait(false);
                if (string.Equals(_activeSecurityRecordingCameraId, match.device.DeviceId, StringComparison.OrdinalIgnoreCase))
                {
                    _activeSecurityRecordingCameraId = string.Empty;
                    _activeSecurityRecordingCameraName = string.Empty;
                    _activeSecurityRecordingSourceUrl = string.Empty;
                }

                await WriteJsonAsync(context, new
                {
                    ok = stopResult.Ok,
                    message = stopResult.Message,
                    outputPath = stopResult.OutputPath,
                    camera = BuildCompanionSecurityCamera(match.provider, match.device),
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AppLogger.LogWarning($"[Companion] Security camera recording stop failed: {ex.Message}");
                context.Response.StatusCode = 500;
                await WriteJsonAsync(context, new { error = ex.Message }).ConfigureAwait(false);
            }
        }

        private async Task HandleSmartHomeScenesAsync(HttpListenerContext context)
        {
            try
            {
                var engine = WorkflowEngine.Instance;
                var activeDefinitionId = engine.ActiveWorkflow?.DefinitionId;
                var scenes = engine.Definitions
                    .Where(LooksLikeLightingScene)
                    .OrderBy(definition => definition.Title)
                    .ThenBy(definition => definition.Id)
                    .Select(definition => BuildCompanionLightingScene(definition, activeDefinitionId, engine.IsWorkflowActive))
                    .ToArray();

                await WriteJsonAsync(context, new { scenes }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AppLogger.LogWarning($"[Companion] Smart Home scenes request failed: {ex.Message}");
                context.Response.StatusCode = 500;
                await WriteJsonAsync(context, new { error = ex.Message }).ConfigureAwait(false);
            }
        }

        private async Task HandleSmartHomeSceneActivateAsync(HttpListenerContext context, string sceneId)
        {
            try
            {
                var engine = WorkflowEngine.Instance;
                var workflow = engine.StartWorkflow(sceneId);
                if (workflow == null)
                {
                    context.Response.StatusCode = 404;
                    await WriteJsonAsync(context, new { error = $"Smart Home scene '{sceneId}' was not found." }).ConfigureAwait(false);
                    return;
                }

                while (engine.IsWorkflowActive)
                {
                    var step = await engine.RunNextStepAsync().ConfigureAwait(false);
                    if (step == null)
                    {
                        break;
                    }
                }

                var finalWorkflow = engine.ActiveWorkflow ?? workflow;
                await WriteJsonAsync(context, new
                {
                    ok = true,
                    sceneId,
                    status = finalWorkflow.Status.ToString(),
                    finalInsight = finalWorkflow.FinalInsight,
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AppLogger.LogWarning($"[Companion] Smart Home scene activation failed: {ex.Message}");
                context.Response.StatusCode = 500;
                await WriteJsonAsync(context, new { error = ex.Message }).ConfigureAwait(false);
            }
        }

        private async Task HandleAutomationRoutinesAsync(HttpListenerContext context)
        {
            try
            {
                var engine = WorkflowEngine.Instance;
                var activeDefinitionId = engine.ActiveWorkflow?.DefinitionId;
                var routines = engine.Definitions
                    .Select(definition => new
                    {
                        id = definition.Id,
                        name = definition.Title,
                        description = definition.Description,
                        iconData = MapWorkflowIconData(definition),
                        isActive = string.Equals(activeDefinitionId, definition.Id, StringComparison.OrdinalIgnoreCase) && engine.IsWorkflowActive,
                    })
                    .ToArray();

                await WriteJsonAsync(context, new { routines }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AppLogger.LogWarning($"[Companion] Automation routines request failed: {ex.Message}");
                context.Response.StatusCode = 500;
                await WriteJsonAsync(context, new { error = ex.Message }).ConfigureAwait(false);
            }
        }

        private async Task HandleAutomationRoutineExecuteAsync(HttpListenerContext context, string routineId)
        {
            try
            {
                var engine = WorkflowEngine.Instance;
                var workflow = engine.StartWorkflow(routineId);
                if (workflow == null)
                {
                    context.Response.StatusCode = 404;
                    await WriteJsonAsync(context, new { error = $"Automation routine '{routineId}' was not found." }).ConfigureAwait(false);
                    return;
                }

                while (engine.IsWorkflowActive)
                {
                    var step = await engine.RunNextStepAsync().ConfigureAwait(false);
                    if (step == null)
                    {
                        break;
                    }
                }

                var finalWorkflow = engine.ActiveWorkflow ?? workflow;
                await WriteJsonAsync(context, new
                {
                    ok = true,
                    routineId,
                    status = finalWorkflow.Status.ToString(),
                    finalInsight = finalWorkflow.FinalInsight,
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AppLogger.LogWarning($"[Companion] Automation execute request failed: {ex.Message}");
                context.Response.StatusCode = 500;
                await WriteJsonAsync(context, new { error = ex.Message }).ConfigureAwait(false);
            }
        }

        private async Task HandleMediaStateAsync(HttpListenerContext context)
        {
            try
            {
                await WriteJsonAsync(context, BuildMediaStateResponse()).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AppLogger.LogWarning($"[Companion] Media state request failed: {ex.Message}");
                context.Response.StatusCode = 500;
                await WriteJsonAsync(context, new { error = ex.Message }).ConfigureAwait(false);
            }
        }

        private async Task HandleMediaArtworkAsync(HttpListenerContext context)
        {
            try
            {
                var sourcePath = context.Request.QueryString["sourcePath"];
                var artworkPath = GetMediaArtworkPath(sourcePath);
                if (string.IsNullOrWhiteSpace(artworkPath) || !File.Exists(artworkPath))
                {
                    context.Response.StatusCode = 404;
                    context.Response.Close();
                    return;
                }

                context.Response.StatusCode = 200;
                context.Response.ContentType = GetImageContentType(artworkPath);
                context.Response.ContentLength64 = new FileInfo(artworkPath).Length;

                await using var fileStream = File.OpenRead(artworkPath);
                await fileStream.CopyToAsync(context.Response.OutputStream).ConfigureAwait(false);
                context.Response.Close();
            }
            catch (Exception ex)
            {
                AppLogger.LogWarning($"[Companion] Media artwork request failed: {ex.Message}");
                context.Response.StatusCode = 500;
                await WriteJsonAsync(context, new { error = ex.Message }).ConfigureAwait(false);
            }
        }

        private async Task HandleMediaFileAsync(HttpListenerContext context)
        {
            try
            {
                var sourcePath = (context.Request.QueryString["sourcePath"] ?? context.Request.QueryString["path"] ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(sourcePath))
                {
                    context.Response.StatusCode = 400;
                    await WriteJsonAsync(context, new { error = "A non-empty 'sourcePath' query parameter is required." }).ConfigureAwait(false);
                    return;
                }

                var resolvedPath = ResolveMediaFileSystemPath(sourcePath);
                if (string.IsNullOrWhiteSpace(resolvedPath))
                {
                    context.Response.StatusCode = 404;
                    await WriteJsonAsync(context, new { error = "Atlas could not resolve the requested media source." }).ConfigureAwait(false);
                    return;
                }

                var download = TryParseBooleanQuery(context.Request.QueryString["download"]);

                if (Directory.Exists(resolvedPath))
                {
                    await WriteMediaDirectoryZipAsync(context, resolvedPath).ConfigureAwait(false);
                    return;
                }

                if (!File.Exists(resolvedPath))
                {
                    context.Response.StatusCode = 404;
                    await WriteJsonAsync(context, new { error = "Atlas could not find the requested media file." }).ConfigureAwait(false);
                    return;
                }

                await WriteMediaFileAsync(context, resolvedPath, download).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AppLogger.LogWarning($"[Companion] Media file request failed: {ex.Message}");
                context.Response.StatusCode = 500;
                await WriteJsonAsync(context, new { error = ex.Message }).ConfigureAwait(false);
            }
        }

        private static string? ResolveMediaFileSystemPath(string? sourcePath)
        {
            var trimmed = (sourcePath ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                return null;
            }

            if (File.Exists(trimmed) || Directory.Exists(trimmed))
            {
                return trimmed;
            }

            var item = FindLibraryItem(trimmed, null, null);
            if (!string.IsNullOrWhiteSpace(item?.FilePath) &&
                (File.Exists(item.FilePath) || Directory.Exists(item.FilePath)))
            {
                return item.FilePath;
            }

            var album = FindMusicAlbum(trimmed, null);
            if (!string.IsNullOrWhiteSpace(album?.SourceFolderPath) && Directory.Exists(album.SourceFolderPath))
            {
                return album.SourceFolderPath;
            }

            return null;
        }

        private static bool TryParseBooleanQuery(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            return value.Equals("1", StringComparison.OrdinalIgnoreCase) ||
                   value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                   value.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
                   value.Equals("download", StringComparison.OrdinalIgnoreCase);
        }

        private static async Task WriteMediaFileAsync(HttpListenerContext context, string filePath, bool download)
        {
            var info = new FileInfo(filePath);
            var totalLength = info.Length;
            var rangeHeader = context.Request.Headers["Range"];
            var hasRange = TryParseRangeHeader(rangeHeader, totalLength, out var rangeStart, out var rangeEnd);

            if (!string.IsNullOrWhiteSpace(rangeHeader) && !hasRange)
            {
                context.Response.StatusCode = (int)HttpStatusCode.RequestedRangeNotSatisfiable;
                context.Response.AddHeader("Content-Range", $"bytes */{totalLength}");
                context.Response.Close();
                return;
            }

            var contentLength = hasRange ? rangeEnd - rangeStart + 1 : totalLength;

            context.Response.StatusCode = hasRange
                ? (int)HttpStatusCode.PartialContent
                : (int)HttpStatusCode.OK;
            context.Response.ContentType = GetMediaContentType(filePath);
            context.Response.ContentLength64 = contentLength;
            context.Response.AddHeader("Accept-Ranges", "bytes");
            context.Response.AddHeader(
                "Content-Disposition",
                $"{(download ? "attachment" : "inline")}; filename*=UTF-8''{Uri.EscapeDataString(CreateDownloadFileName(filePath))}");

            if (hasRange)
            {
                context.Response.AddHeader("Content-Range", $"bytes {rangeStart}-{rangeEnd}/{totalLength}");
            }

            await using var fileStream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (hasRange)
            {
                fileStream.Seek(rangeStart, SeekOrigin.Begin);
            }

            await CopyBytesAsync(fileStream, context.Response.OutputStream, contentLength).ConfigureAwait(false);
            context.Response.Close();
        }

        private static async Task WriteMediaDirectoryZipAsync(HttpListenerContext context, string directoryPath)
        {
            var files = Directory
                .EnumerateFiles(directoryPath, "*", SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (files.Count == 0)
            {
                context.Response.StatusCode = 404;
                await WriteJsonAsync(context, new { error = "Atlas could not find any files in the requested media folder." }).ConfigureAwait(false);
                return;
            }

            context.Response.StatusCode = (int)HttpStatusCode.OK;
            context.Response.ContentType = "application/zip";
            context.Response.AddHeader(
                "Content-Disposition",
                $"attachment; filename*=UTF-8''{Uri.EscapeDataString(CreateDownloadFileName(directoryPath) + ".zip")}");

            using (var archive = new ZipArchive(context.Response.OutputStream, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (var filePath in files)
                {
                    var relativePath = Path.GetRelativePath(directoryPath, filePath);
                    var entry = archive.CreateEntry(relativePath, CompressionLevel.Fastest);
                    await using var input = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    await using var entryStream = entry.Open();
                    await input.CopyToAsync(entryStream).ConfigureAwait(false);
                }
            }

            context.Response.Close();
        }

        private static bool TryParseRangeHeader(string? headerValue, long totalLength, out long start, out long end)
        {
            start = 0;
            end = totalLength - 1;

            if (string.IsNullOrWhiteSpace(headerValue) ||
                !headerValue.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var range = headerValue.Substring("bytes=".Length).Split(',')[0].Trim();
            var parts = range.Split('-', 2);
            if (parts.Length != 2)
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(parts[0]))
            {
                if (!long.TryParse(parts[1], out var suffixLength) || suffixLength <= 0)
                {
                    return false;
                }

                if (suffixLength >= totalLength)
                {
                    start = 0;
                }
                else
                {
                    start = totalLength - suffixLength;
                }

                end = totalLength - 1;
                return true;
            }

            if (!long.TryParse(parts[0], out start) || start < 0 || start >= totalLength)
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(parts[1]))
            {
                end = totalLength - 1;
                return true;
            }

            if (!long.TryParse(parts[1], out end) || end < start)
            {
                return false;
            }

            if (end >= totalLength)
            {
                end = totalLength - 1;
            }

            return true;
        }

        private static async Task CopyBytesAsync(Stream input, Stream output, long bytesToCopy)
        {
            var buffer = new byte[1024 * 64];
            var remaining = bytesToCopy;

            while (remaining > 0)
            {
                var read = await input.ReadAsync(buffer, 0, (int)Math.Min(buffer.Length, remaining)).ConfigureAwait(false);
                if (read <= 0)
                {
                    break;
                }

                await output.WriteAsync(buffer, 0, read).ConfigureAwait(false);
                remaining -= read;
            }
        }

        private static string CreateDownloadFileName(string path)
        {
            var candidate = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (string.IsNullOrWhiteSpace(candidate))
            {
                candidate = "atlas-media";
            }

            foreach (var invalid in Path.GetInvalidFileNameChars())
            {
                candidate = candidate.Replace(invalid, '_');
            }

            return candidate;
        }

        private static string GetMediaContentType(string path)
        {
            var extension = Path.GetExtension(path)?.ToLowerInvariant();
            return extension switch
            {
                ".mp3" => "audio/mpeg",
                ".m4a" => "audio/mp4",
                ".aac" => "audio/aac",
                ".flac" => "audio/flac",
                ".wav" => "audio/wav",
                ".ogg" => "audio/ogg",
                ".mp4" => "video/mp4",
                ".m4v" => "video/x-m4v",
                ".mkv" => "video/x-matroska",
                ".mov" => "video/quicktime",
                ".avi" => "video/x-msvideo",
                ".wmv" => "video/x-ms-wmv",
                ".webm" => "video/webm",
                ".jpg" or ".jpeg" => "image/jpeg",
                ".png" => "image/png",
                ".gif" => "image/gif",
                ".webp" => "image/webp",
                _ => "application/octet-stream",
            };
        }

        private async Task HandleMediaControlAsync(HttpListenerContext context)
        {
            MediaControlRequest? request;

            try
            {
                request = await ReadJsonAsync<MediaControlRequest>(context.Request).ConfigureAwait(false);
            }
            catch (JsonException ex)
            {
                context.Response.StatusCode = 400;
                await WriteJsonAsync(context, new { error = $"Invalid JSON payload: {ex.Message}" }).ConfigureAwait(false);
                return;
            }

            var action = request?.Action?.Trim();
            if (string.IsNullOrWhiteSpace(action))
            {
                context.Response.StatusCode = 400;
                await WriteJsonAsync(context, new { error = "A non-empty 'action' field is required." }).ConfigureAwait(false);
                return;
            }

            try
            {
                var normalizedAction = NormalizeToken(action);
                switch (normalizedAction)
                {
                    case "next":
                    case "nexttrack":
                        MediaPlaybackService.GetOrCreate().PlayNext();
                        break;

                    case "previous":
                    case "prev":
                    case "previoustrack":
                        MediaPlaybackService.GetOrCreate().PlayPrevious();
                        break;

                    case "toggle":
                    case "playpause":
                    case "toggleplayback":
                    case "toggleplaypause":
                        await InvokeMediaPlayerAsync(
                            control => control.TogglePlayPause(),
                            control => control.TogglePlayPause(),
                            fallback: () =>
                            {
                                if (MediaCentreViewModel.Instance != null)
                                {
                                    MediaCentreViewModel.Instance.IsPlaying = !MediaCentreViewModel.Instance.IsPlaying;
                                }
                            }).ConfigureAwait(false);
                        break;

                    case "play":
                        await InvokeMediaPlayerAsync(
                            control => control.Play(),
                            control => control.TogglePlayPause(),
                            fallback: () =>
                            {
                                if (MediaCentreViewModel.Instance != null)
                                {
                                    MediaCentreViewModel.Instance.IsPlaying = true;
                                }
                            }).ConfigureAwait(false);
                        break;

                    case "pause":
                        await InvokeMediaPlayerAsync(
                            control => control.Pause(),
                            control => control.TogglePlayPause(),
                            fallback: () =>
                            {
                                if (MediaCentreViewModel.Instance != null)
                                {
                                    MediaCentreViewModel.Instance.IsPlaying = false;
                                }
                            }).ConfigureAwait(false);
                        break;

                    case "stop":
                        await InvokeMediaPlayerAsync(
                            control => control.Stop(),
                            control => control.StopPlayback(),
                            fallback: () =>
                            {
                                if (MediaCentreViewModel.Instance != null)
                                {
                                    MediaCentreViewModel.Instance.IsPlaying = false;
                                    MediaCentreViewModel.Instance.ProgressSeconds = 0;
                                }
                            }).ConfigureAwait(false);
                        break;

                    case "seek":
                    case "seekto":
                        var totalSeconds = MediaCentreViewModel.Instance?.TotalSeconds ?? 0d;
                        double targetSeconds;
                        if (request?.Seconds is double explicitSeconds)
                        {
                            targetSeconds = explicitSeconds;
                        }
                        else if (request?.Progress is double progress)
                        {
                            targetSeconds = totalSeconds > 0
                                ? Math.Clamp(progress, 0d, 1d) * totalSeconds
                                : 0d;
                        }
                        else
                        {
                            context.Response.StatusCode = 400;
                            await WriteJsonAsync(context, new { error = "Media seek requires either 'seconds' or 'progress'." }).ConfigureAwait(false);
                            return;
                        }

                        targetSeconds = Math.Max(0d, targetSeconds);
                        await InvokeMediaPlayerAsync(
                            control => control.SeekToSeconds(targetSeconds),
                            control => control.SeekToSeconds(targetSeconds),
                            fallback: () =>
                            {
                                if (MediaCentreViewModel.Instance != null)
                                {
                                    var bounded = MediaCentreViewModel.Instance.TotalSeconds > 0
                                        ? Math.Min(targetSeconds, MediaCentreViewModel.Instance.TotalSeconds)
                                        : targetSeconds;
                                    MediaCentreViewModel.Instance.ProgressSeconds = bounded;
                                }
                            }).ConfigureAwait(false);
                        break;

                    case "setplaybackspeed":
                    case "playbackspeed":
                    case "speed":
                    case "rate":
                        if (!request?.Speed.HasValue ?? true)
                        {
                            context.Response.StatusCode = 400;
                            await WriteJsonAsync(context, new { error = "Playback speed control requires a 'speed' field." }).ConfigureAwait(false);
                            return;
                        }

                        var requestedSpeed = Math.Clamp(request!.Speed.Value, 0.5d, 2.0d);
                        await InvokeMediaPlayerAsync(
                            control => control.SetPlaybackSpeed(requestedSpeed),
                            control => control.SetPlaybackSpeed(requestedSpeed)).ConfigureAwait(false);
                        break;

                    case "setsubtitleenabled":
                    case "subtitletoggle":
                    case "subtitles":
                        if (!request?.Enabled.HasValue ?? true)
                        {
                            context.Response.StatusCode = 400;
                            await WriteJsonAsync(context, new { error = "Subtitle toggle control requires an 'enabled' field." }).ConfigureAwait(false);
                            return;
                        }

                        await InvokeMediaPlayerAsync(
                            control => control.SetSubtitleEnabled(request!.Enabled!.Value),
                            control => control.SetSubtitleEnabled(request!.Enabled!.Value)).ConfigureAwait(false);
                        break;

                    case "setsubtitletrack":
                    case "subtitletrack":
                        if (!request?.TrackId.HasValue ?? true)
                        {
                            context.Response.StatusCode = 400;
                            await WriteJsonAsync(context, new { error = "Subtitle track control requires a 'trackId' field." }).ConfigureAwait(false);
                            return;
                        }

                        await InvokeMediaPlayerAsync(
                            control => control.SetSubtitleTrack(request!.TrackId!.Value),
                            control => control.SetSubtitleEnabled(true)).ConfigureAwait(false);
                        break;

                    case "setaudiotrack":
                    case "audiotrack":
                        if (!request?.TrackId.HasValue ?? true)
                        {
                            context.Response.StatusCode = 400;
                            await WriteJsonAsync(context, new { error = "Audio track control requires a 'trackId' field." }).ConfigureAwait(false);
                            return;
                        }

                        await InvokeMediaPlayerAsync(
                            control => control.SetAudioTrack(request!.TrackId!.Value),
                            control => { }).ConfigureAwait(false);
                        break;

                    case "setmuted":
                    case "mute":
                    case "unmute":
                        var shouldMute = normalizedAction switch
                        {
                            "mute" => true,
                            "unmute" => false,
                            _ => request?.Enabled,
                        };
                        if (!shouldMute.HasValue)
                        {
                            context.Response.StatusCode = 400;
                            await WriteJsonAsync(context, new { error = "Mute control requires an 'enabled' field when using 'setMuted'." }).ConfigureAwait(false);
                            return;
                        }

                        await InvokeMediaPlayerAsync(
                            control => control.SetMuted(shouldMute.Value),
                            control => control.SetMuted(shouldMute.Value)).ConfigureAwait(false);
                        break;

                    case "togglefullscreen":
                    case "fullscreen":
                        await InvokeMediaPlayerAsync(
                            control => control.ToggleFullscreenMode(),
                            control => control.ToggleFullscreenMode()).ConfigureAwait(false);
                        break;

                    case "toggleshuffle":
                    case "shuffle":
                        if (MediaCentreViewModel.Instance != null)
                        {
                            MediaCentreViewModel.Instance.ShuffleEnabled = !MediaCentreViewModel.Instance.ShuffleEnabled;
                        }
                        break;

                    case "togglerepeat":
                    case "repeat":
                        if (MediaCentreViewModel.Instance != null)
                        {
                            MediaCentreViewModel.Instance.RepeatEnabled = !MediaCentreViewModel.Instance.RepeatEnabled;
                        }
                        break;

                    default:
                        context.Response.StatusCode = 400;
                        await WriteJsonAsync(context, new { error = $"Unsupported media action '{action}'." }).ConfigureAwait(false);
                        return;
                }

                await WriteJsonAsync(context, new
                {
                    ok = true,
                    state = BuildMediaStateResponse(),
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AppLogger.LogWarning($"[Companion] Media control request failed: {ex.Message}");
                context.Response.StatusCode = 500;
                await WriteJsonAsync(context, new { error = ex.Message }).ConfigureAwait(false);
            }
        }

        private async Task HandleMediaVolumeAsync(HttpListenerContext context)
        {
            MediaVolumeRequest? request;

            try
            {
                request = await ReadJsonAsync<MediaVolumeRequest>(context.Request).ConfigureAwait(false);
            }
            catch (JsonException ex)
            {
                context.Response.StatusCode = 400;
                await WriteJsonAsync(context, new { error = $"Invalid JSON payload: {ex.Message}" }).ConfigureAwait(false);
                return;
            }

            if (request == null)
            {
                context.Response.StatusCode = 400;
                await WriteJsonAsync(context, new { error = "A media volume payload is required." }).ConfigureAwait(false);
                return;
            }

            try
            {
                var volume = Math.Clamp((int)Math.Round(request.Volume), 0, 100);
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    if (MediaCentreViewModel.Instance != null)
                    {
                        MediaCentreViewModel.Instance.Volume = volume;
                    }

                    foreach (var player in EnumerateVisualChildren<SimpleMediaPlayerControl>(Application.Current))
                    {
                        player.SetVolume(volume);
                    }

                    foreach (var player in EnumerateVisualChildren<MediaPlayerControl>(Application.Current))
                    {
                        player.SetVolume(volume);
                    }
                }).Task.ConfigureAwait(false);

                await WriteJsonAsync(context, new
                {
                    ok = true,
                    volume,
                    state = BuildMediaStateResponse(),
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AppLogger.LogWarning($"[Companion] Media volume request failed: {ex.Message}");
                context.Response.StatusCode = 500;
                await WriteJsonAsync(context, new { error = ex.Message }).ConfigureAwait(false);
            }
        }

        private async Task HandleMediaQueueAsync(HttpListenerContext context)
        {
            try
            {
                await WriteJsonAsync(context, BuildMediaQueueResponse()).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AppLogger.LogWarning($"[Companion] Media queue request failed: {ex.Message}");
                context.Response.StatusCode = 500;
                await WriteJsonAsync(context, new { error = ex.Message }).ConfigureAwait(false);
            }
        }

        private async Task HandleMediaQueueControlAsync(HttpListenerContext context)
        {
            MediaQueueControlRequest? request;

            try
            {
                request = await ReadJsonAsync<MediaQueueControlRequest>(context.Request).ConfigureAwait(false);
            }
            catch (JsonException ex)
            {
                context.Response.StatusCode = 400;
                await WriteJsonAsync(context, new { error = $"Invalid JSON payload: {ex.Message}" }).ConfigureAwait(false);
                return;
            }

            var action = request?.Action?.Trim();
            if (string.IsNullOrWhiteSpace(action))
            {
                context.Response.StatusCode = 400;
                await WriteJsonAsync(context, new { error = "A non-empty 'action' field is required." }).ConfigureAwait(false);
                return;
            }

            try
            {
                var playback = MediaPlaybackService.GetOrCreate();
                switch (NormalizeToken(action))
                {
                    case "playindex":
                    case "play":
                        if (!request?.Index.HasValue ?? true)
                        {
                            context.Response.StatusCode = 400;
                            await WriteJsonAsync(context, new { error = "Queue play requests require an 'index' field." }).ConfigureAwait(false);
                            return;
                        }

                        playback.PlayFromQueue(request!.Index!.Value);
                        break;

                    case "remove":
                    case "delete":
                    case "removeindex":
                        if (!request?.Index.HasValue ?? true)
                        {
                            context.Response.StatusCode = 400;
                            await WriteJsonAsync(context, new { error = "Queue remove requests require an 'index' field." }).ConfigureAwait(false);
                            return;
                        }

                        playback.RemoveFromQueue(request!.Index!.Value);
                        break;

                    case "move":
                    case "reorder":
                        if (!request?.Index.HasValue ?? true || !request.TargetIndex.HasValue)
                        {
                            context.Response.StatusCode = 400;
                            await WriteJsonAsync(context, new { error = "Queue move requests require both 'index' and 'targetIndex' fields." }).ConfigureAwait(false);
                            return;
                        }

                        playback.MoveQueueItem(request!.Index!.Value, request.TargetIndex.Value);
                        break;

                    case "clear":
                    case "clearqueue":
                        playback.ClearQueue();
                        break;

                    default:
                        context.Response.StatusCode = 400;
                        await WriteJsonAsync(context, new { error = $"Unsupported media queue action '{action}'." }).ConfigureAwait(false);
                        return;
                }

                await WriteJsonAsync(context, new
                {
                    ok = true,
                    state = BuildMediaStateResponse(),
                    queue = BuildMediaQueueResponse(),
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AppLogger.LogWarning($"[Companion] Media queue control request failed: {ex.Message}");
                context.Response.StatusCode = 500;
                await WriteJsonAsync(context, new { error = ex.Message }).ConfigureAwait(false);
            }
        }

        private async Task HandleMediaHistoryAsync(HttpListenerContext context)
        {
            try
            {
                await WriteJsonAsync(context, BuildMediaHistoryResponse()).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AppLogger.LogWarning($"[Companion] Media history request failed: {ex.Message}");
                context.Response.StatusCode = 500;
                await WriteJsonAsync(context, new { error = ex.Message }).ConfigureAwait(false);
            }
        }

        private async Task HandleMediaHomeAsync(HttpListenerContext context)
        {
            try
            {
                await WriteJsonAsync(context, BuildMediaHomeResponse()).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AppLogger.LogWarning($"[Companion] Media home request failed: {ex.Message}");
                context.Response.StatusCode = 500;
                await WriteJsonAsync(context, new { error = ex.Message }).ConfigureAwait(false);
            }
        }

        private async Task HandleMediaLibraryAsync(HttpListenerContext context)
        {
            try
            {
                var query = (context.Request.QueryString["q"] ?? string.Empty).Trim();
                var type = NormalizeMediaBrowseType(context.Request.QueryString["type"]);
                var max = 5000;
                if (int.TryParse(context.Request.QueryString["max"], out var parsedMax))
                {
                    max = Math.Clamp(parsedMax, 1, 5000);
                }

                await WriteJsonAsync(context, BuildMediaLibraryResponse(query, type, max)).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AppLogger.LogWarning($"[Companion] Media library request failed: {ex.Message}");
                context.Response.StatusCode = 500;
                await WriteJsonAsync(context, new { error = ex.Message }).ConfigureAwait(false);
            }
        }

        private async Task HandleMediaLibraryControlAsync(HttpListenerContext context)
        {
            MediaLibraryControlRequest? request;

            try
            {
                request = await ReadJsonAsync<MediaLibraryControlRequest>(context.Request).ConfigureAwait(false);
            }
            catch (JsonException ex)
            {
                context.Response.StatusCode = 400;
                await WriteJsonAsync(context, new { error = $"Invalid JSON payload: {ex.Message}" }).ConfigureAwait(false);
                return;
            }

            var action = request?.Action?.Trim();
            if (string.IsNullOrWhiteSpace(action))
            {
                context.Response.StatusCode = 400;
                await WriteJsonAsync(context, new { error = "A non-empty 'action' field is required." }).ConfigureAwait(false);
                return;
            }

            try
            {
                var normalizedAction = NormalizeToken(action);
                var normalizedType = NormalizeMediaBrowseType(request?.MediaType);
                var album = normalizedType == "music"
                    ? FindMusicAlbum(request?.SourcePath, request?.DisplayName)
                    : null;
                var item = normalizedType == "apps" || album != null
                    ? null
                    : FindLibraryItem(request?.SourcePath, request?.DisplayName, request?.MediaType);

                if (normalizedType != "apps" &&
                    normalizedAction is not "markwatched" and not "watched" and not "markunwatched" and not "unwatched" &&
                    item == null &&
                    album == null)
                {
                    context.Response.StatusCode = 404;
                    await WriteJsonAsync(context, new { error = "The requested media item could not be found in the Atlas library." }).ConfigureAwait(false);
                    return;
                }

                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    var vm = MediaCentreViewModel.Instance;
                    if (vm == null)
                    {
                        throw new InvalidOperationException("Atlas media centre is not available.");
                    }

                    if (album != null)
                    {
                        switch (normalizedAction)
                        {
                            case "play":
                            case "open":
                            case "resume":
                                PlayMusicAlbum(album);
                                break;

                            case "queue":
                            case "addtoqueue":
                                QueueMusicAlbum(album, playNext: false);
                                break;

                            case "queuenext":
                            case "playnext":
                                QueueMusicAlbum(album, playNext: true);
                                break;

                            default:
                                throw new InvalidOperationException($"Unsupported album action '{action}'.");
                        }
                    }
                    else
                    {
                        switch (normalizedAction)
                        {
                            case "play":
                            case "open":
                                if (normalizedType == "apps")
                                {
                                    LaunchMediaApp(request?.SourcePath, request?.DisplayName);
                                    break;
                                }

                                vm.PlayItemCommand.Execute(item);
                                break;

                            case "resume":
                                if (normalizedType == "apps")
                                {
                                    LaunchMediaApp(request?.SourcePath, request?.DisplayName);
                                    break;
                                }

                                vm.PlayItemCommand.Execute(item);
                                var resumeSeconds = ResolveResumeSeconds(item, request);
                                if (resumeSeconds > 0)
                                {
                                    foreach (var player in EnumerateVisualChildren<MediaPlayerControl>(Application.Current))
                                    {
                                        player.SeekToSeconds(resumeSeconds);
                                    }

                                    foreach (var player in EnumerateVisualChildren<SimpleMediaPlayerControl>(Application.Current))
                                    {
                                        player.SeekToSeconds(resumeSeconds);
                                    }

                                    if (MediaCentreViewModel.Instance != null)
                                    {
                                        MediaCentreViewModel.Instance.ProgressSeconds = resumeSeconds;
                                    }
                                }
                                break;

                            case "launch":
                            case "openapp":
                            case "openurl":
                                if (normalizedType != "apps")
                                {
                                    throw new InvalidOperationException("Launch actions are only supported for Atlas media apps.");
                                }

                                LaunchMediaApp(request?.SourcePath, request?.DisplayName);
                                break;

                            case "queue":
                            case "addtoqueue":
                                if (normalizedType == "apps")
                                {
                                    throw new InvalidOperationException("Apps cannot be queued in the Atlas playback queue.");
                                }

                                vm.AddToQueueCommand.Execute(item);
                                break;

                            case "queuenext":
                            case "playnext":
                                if (normalizedType == "apps")
                                {
                                    throw new InvalidOperationException("Apps cannot be queued in the Atlas playback queue.");
                                }

                                vm.QueueNextCommand.Execute(item);
                                break;

                            case "markwatched":
                            case "watched":
                                MarkMediaItemWatched(item, request);
                                break;

                            case "markunwatched":
                            case "unwatched":
                                MarkMediaItemUnwatched(item, request);
                                break;

                            default:
                                throw new InvalidOperationException($"Unsupported media library action '{action}'.");
                        }
                    }
                }).Task.ConfigureAwait(false);

                await WriteJsonAsync(context, new
                {
                    ok = true,
                    state = BuildMediaStateResponse(),
                    queue = BuildMediaQueueResponse(),
                    history = BuildMediaHistoryResponse(),
                    home = BuildMediaHomeResponse(),
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AppLogger.LogWarning($"[Companion] Media library control request failed: {ex.Message}");
                context.Response.StatusCode = 500;
                await WriteJsonAsync(context, new { error = ex.Message }).ConfigureAwait(false);
            }
        }

        private async Task HandleDownloaderStateAsync(HttpListenerContext context)
        {
            try
            {
                await AtlasAI.Modules.Downloader.DownloadManager.Instance.InitializeAsync().ConfigureAwait(false);
                await WriteJsonAsync(context, BuildDownloaderStateResponse()).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AppLogger.LogWarning($"[Companion] Downloader state request failed: {ex.Message}");
                context.Response.StatusCode = 500;
                await WriteJsonAsync(context, new { error = ex.Message }).ConfigureAwait(false);
            }
        }

        private async Task HandleDownloaderControlAsync(HttpListenerContext context)
        {
            DownloaderControlRequest? request;

            try
            {
                request = await ReadJsonAsync<DownloaderControlRequest>(context.Request).ConfigureAwait(false);
            }
            catch (JsonException ex)
            {
                context.Response.StatusCode = 400;
                await WriteJsonAsync(context, new { error = $"Invalid JSON payload: {ex.Message}" }).ConfigureAwait(false);
                return;
            }

            var action = request?.Action?.Trim();
            if (string.IsNullOrWhiteSpace(action))
            {
                context.Response.StatusCode = 400;
                await WriteJsonAsync(context, new { error = "A non-empty 'action' field is required." }).ConfigureAwait(false);
                return;
            }

            try
            {
                var manager = AtlasAI.Modules.Downloader.DownloadManager.Instance;
                await manager.InitializeAsync().ConfigureAwait(false);
                var normalizedAction = NormalizeToken(action);

                switch (normalizedAction)
                {
                    case "add":
                    case "queue":
                        if (!string.IsNullOrWhiteSpace(request?.Input))
                        {
                            await DownloadService.Instance.AddDownloadAsync(request.Input!).ConfigureAwait(false);
                        }
                        else if (request?.Urls != null && request.Urls.Count > 0)
                        {
                            await DownloadService.Instance.AddDownloadsAsync(request.Urls).ConfigureAwait(false);
                        }
                        else
                        {
                            context.Response.StatusCode = 400;
                            await WriteJsonAsync(context, new { error = "Downloader add requires either 'input' or 'urls'." }).ConfigureAwait(false);
                            return;
                        }
                        break;

                    case "pause":
                    case "resume":
                    case "retry":
                    case "cancel":
                    case "remove":
                        if (string.IsNullOrWhiteSpace(request?.JobId))
                        {
                            context.Response.StatusCode = 400;
                            await WriteJsonAsync(context, new { error = $"Downloader {action} requires a 'jobId'." }).ConfigureAwait(false);
                            return;
                        }

                        var jobPayload = JsonSerializer.SerializeToElement(new { id = request!.JobId });
                        switch (normalizedAction)
                        {
                            case "pause":
                                manager.Pause(jobPayload);
                                break;
                            case "resume":
                                manager.Resume(jobPayload);
                                break;
                            case "retry":
                                manager.Retry(jobPayload);
                                break;
                            case "cancel":
                                manager.Cancel(jobPayload);
                                break;
                            case "remove":
                                manager.Remove(jobPayload);
                                break;
                        }
                        break;

                    case "pauseall":
                        manager.PauseAll();
                        break;
                    case "resumeall":
                        manager.ResumeAll();
                        break;
                    case "retryall":
                        manager.RetryAll();
                        break;
                    case "stopall":
                    case "cancelall":
                        manager.StopAll();
                        break;
                    case "clearfinished":
                        manager.ClearFinished();
                        break;
                    default:
                        context.Response.StatusCode = 400;
                        await WriteJsonAsync(context, new { error = $"Unsupported downloader action '{action}'." }).ConfigureAwait(false);
                        return;
                }

                await WriteJsonAsync(context, new
                {
                    ok = true,
                    state = BuildDownloaderStateResponse(),
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AppLogger.LogWarning($"[Companion] Downloader control request failed: {ex.Message}");
                context.Response.StatusCode = 500;
                await WriteJsonAsync(context, new { error = ex.Message }).ConfigureAwait(false);
            }
        }

        private async Task HandleDjStateAsync(HttpListenerContext context)
        {
            try
            {
                var djView = GetActiveDjConsoleView();
                if (djView == null)
                {
                    await WriteJsonAsync(context, new
                    {
                        routeAvailable = false,
                        message = "DJ booth is not active on the Atlas host.",
                    }).ConfigureAwait(false);
                    return;
                }

                var state = await Application.Current.Dispatcher.InvokeAsync(() => djView.GetCompanionState()).Task.ConfigureAwait(false);
                await WriteJsonAsync(context, new
                {
                    routeAvailable = true,
                    state,
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AppLogger.LogWarning($"[Companion] DJ state request failed: {ex.Message}");
                context.Response.StatusCode = 500;
                await WriteJsonAsync(context, new { error = ex.Message }).ConfigureAwait(false);
            }
        }

        private async Task HandleDjControlAsync(HttpListenerContext context)
        {
            DjControlRequest? request;

            try
            {
                request = await ReadJsonAsync<DjControlRequest>(context.Request).ConfigureAwait(false);
            }
            catch (JsonException ex)
            {
                context.Response.StatusCode = 400;
                await WriteJsonAsync(context, new { error = $"Invalid JSON payload: {ex.Message}" }).ConfigureAwait(false);
                return;
            }

            var action = request?.Action?.Trim();
            if (string.IsNullOrWhiteSpace(action))
            {
                context.Response.StatusCode = 400;
                await WriteJsonAsync(context, new { error = "A non-empty 'action' field is required." }).ConfigureAwait(false);
                return;
            }

            try
            {
                var djView = GetActiveDjConsoleView();
                if (djView == null)
                {
                    context.Response.StatusCode = 409;
                    await WriteJsonAsync(context, new { error = "DJ booth is not active on the Atlas host." }).ConfigureAwait(false);
                    return;
                }

                var state = await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    djView.ExecuteCompanionCommand(
                        action!,
                        request?.Deck,
                        request?.Path,
                        request?.Value,
                        request?.Band,
                        request?.CueIndex,
                        request?.TransitionBeats);
                    return djView.GetCompanionState();
                }).Task.ConfigureAwait(false);

                await WriteJsonAsync(context, new
                {
                    ok = true,
                    state,
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AppLogger.LogWarning($"[Companion] DJ control request failed: {ex.Message}");
                context.Response.StatusCode = 500;
                await WriteJsonAsync(context, new { error = ex.Message }).ConfigureAwait(false);
            }
        }

        private async Task HandleDjActivateAsync(HttpListenerContext context)
        {
            try
            {
                var response = await ActivateDjBoothAsync().ConfigureAwait(false);
                await WriteJsonAsync(context, response).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AppLogger.LogWarning($"[Companion] DJ activation request failed: {ex.Message}");
                context.Response.StatusCode = 500;
                await WriteJsonAsync(context, new { error = ex.Message }).ConfigureAwait(false);
            }
        }

        private async Task HandleCodeStateAsync(HttpListenerContext context)
        {
            try
            {
                await WriteJsonAsync(context, BuildCodeStateResponse()).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AppLogger.LogWarning($"[Companion] Code state request failed: {ex.Message}");
                context.Response.StatusCode = 500;
                await WriteJsonAsync(context, new { error = ex.Message }).ConfigureAwait(false);
            }
        }

        private async Task HandleCodeWorkspacesAsync(HttpListenerContext context)
        {
            try
            {
                await WriteJsonAsync(context, new
                {
                    currentWorkspace = _companionCodeAssistant.WorkspacePath ?? string.Empty,
                    workspaces = BuildCodeWorkspaceCandidates(),
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AppLogger.LogWarning($"[Companion] Code workspaces request failed: {ex.Message}");
                context.Response.StatusCode = 500;
                await WriteJsonAsync(context, new { error = ex.Message }).ConfigureAwait(false);
            }
        }

        private async Task HandleCodeWorkspaceAsync(HttpListenerContext context)
        {
            CodeWorkspaceRequest? request;

            try
            {
                request = await ReadJsonAsync<CodeWorkspaceRequest>(context.Request).ConfigureAwait(false);
            }
            catch (JsonException ex)
            {
                context.Response.StatusCode = 400;
                await WriteJsonAsync(context, new { error = $"Invalid JSON payload: {ex.Message}" }).ConfigureAwait(false);
                return;
            }

            var path = (request?.Path ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            {
                context.Response.StatusCode = 400;
                await WriteJsonAsync(context, new { error = "A valid existing workspace 'path' is required." }).ConfigureAwait(false);
                return;
            }

            try
            {
                _companionCodeAssistant.SetWorkspace(path);
                await WriteJsonAsync(context, new
                {
                    ok = _companionCodeAssistant.HasWorkspace,
                    state = BuildCodeStateResponse(),
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AppLogger.LogWarning($"[Companion] Code workspace update failed: {ex.Message}");
                context.Response.StatusCode = 500;
                await WriteJsonAsync(context, new { error = ex.Message }).ConfigureAwait(false);
            }
        }

        private async Task HandleCodeSearchAsync(HttpListenerContext context)
        {
            try
            {
                var query = (context.Request.QueryString["q"] ?? string.Empty).Trim();
                var mode = (context.Request.QueryString["mode"] ?? "content").Trim();
                var max = int.TryParse(context.Request.QueryString["max"], out var parsedMax)
                    ? Math.Clamp(parsedMax, 1, 100)
                    : 40;

                if (string.IsNullOrWhiteSpace(query))
                {
                    context.Response.StatusCode = 400;
                    await WriteJsonAsync(context, new { error = "A non-empty 'q' query parameter is required." }).ConfigureAwait(false);
                    return;
                }

                await WriteJsonAsync(context, BuildCodeSearchResponse(query, mode, max)).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AppLogger.LogWarning($"[Companion] Code search request failed: {ex.Message}");
                context.Response.StatusCode = 500;
                await WriteJsonAsync(context, new { error = ex.Message }).ConfigureAwait(false);
            }
        }

        private async Task HandleCodeFileAsync(HttpListenerContext context)
        {
            try
            {
                var path = (context.Request.QueryString["path"] ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(path))
                {
                    context.Response.StatusCode = 400;
                    await WriteJsonAsync(context, new { error = "A non-empty 'path' query parameter is required." }).ConfigureAwait(false);
                    return;
                }

                var fullPath = ResolveCodeFilePath(path);
                if (fullPath == null || !File.Exists(fullPath))
                {
                    context.Response.StatusCode = 404;
                    await WriteJsonAsync(context, new { error = "The requested code file could not be found in the active workspace." }).ConfigureAwait(false);
                    return;
                }

                var content = await File.ReadAllTextAsync(fullPath).ConfigureAwait(false);
                await WriteJsonAsync(context, new
                {
                    path = Path.GetRelativePath(_companionCodeAssistant.WorkspacePath ?? Path.GetDirectoryName(fullPath) ?? fullPath, fullPath),
                    fullPath,
                    content,
                    fileInfo = _companionCodeAssistant.GetFileInfo(fullPath),
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AppLogger.LogWarning($"[Companion] Code file request failed: {ex.Message}");
                context.Response.StatusCode = 500;
                await WriteJsonAsync(context, new { error = ex.Message }).ConfigureAwait(false);
            }
        }

        private async Task HandleCodeWriteAsync(HttpListenerContext context)
        {
            CodeWriteRequest? request;

            try
            {
                request = await ReadJsonAsync<CodeWriteRequest>(context.Request).ConfigureAwait(false);
            }
            catch (JsonException ex)
            {
                context.Response.StatusCode = 400;
                await WriteJsonAsync(context, new { error = $"Invalid JSON payload: {ex.Message}" }).ConfigureAwait(false);
                return;
            }

            var path = (request?.Path ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(path))
            {
                context.Response.StatusCode = 400;
                await WriteJsonAsync(context, new { error = "A non-empty 'path' field is required." }).ConfigureAwait(false);
                return;
            }

            var content = request?.Content ?? string.Empty;
            var fullPath = ResolveCodeFilePath(path);
            if (fullPath == null)
            {
                context.Response.StatusCode = 400;
                await WriteJsonAsync(context, new { error = "The requested code file path must stay inside the active workspace." }).ConfigureAwait(false);
                return;
            }

            try
            {
                var relativePath = GetRelativeCodePath(fullPath);
                var writeResult = await _companionCodeAssistant.WriteFileAsync(relativePath, content).ConfigureAwait(false);
                if (!writeResult.StartsWith("✅", StringComparison.Ordinal))
                {
                    context.Response.StatusCode = writeResult.StartsWith("⚠", StringComparison.Ordinal) ? 409 : 400;
                    await WriteJsonAsync(context, new { error = writeResult }).ConfigureAwait(false);
                    return;
                }

                await WriteJsonAsync(context, BuildCodeFileResponse(fullPath, writeResult)).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AppLogger.LogWarning($"[Companion] Code write request failed: {ex.Message}");
                context.Response.StatusCode = 500;
                await WriteJsonAsync(context, new { error = ex.Message }).ConfigureAwait(false);
            }
        }

        private async Task HandleCodePatchAsync(HttpListenerContext context)
        {
            CodePatchRequest? request;

            try
            {
                request = await ReadJsonAsync<CodePatchRequest>(context.Request).ConfigureAwait(false);
            }
            catch (JsonException ex)
            {
                context.Response.StatusCode = 400;
                await WriteJsonAsync(context, new { error = $"Invalid JSON payload: {ex.Message}" }).ConfigureAwait(false);
                return;
            }

            var path = (request?.Path ?? string.Empty).Trim();
            var oldText = request?.OldText ?? string.Empty;
            if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(oldText))
            {
                context.Response.StatusCode = 400;
                await WriteJsonAsync(context, new { error = "Non-empty 'path' and 'oldText' fields are required." }).ConfigureAwait(false);
                return;
            }

            var fullPath = ResolveCodeFilePath(path);
            if (fullPath == null || !File.Exists(fullPath))
            {
                context.Response.StatusCode = 404;
                await WriteJsonAsync(context, new { error = "The requested code file could not be found in the active workspace." }).ConfigureAwait(false);
                return;
            }

            try
            {
                var relativePath = GetRelativeCodePath(fullPath);
                var patchResult = await _companionCodeAssistant.ReplaceInFileAsync(relativePath, oldText, request?.NewText ?? string.Empty).ConfigureAwait(false);
                if (!patchResult.StartsWith("✅", StringComparison.Ordinal))
                {
                    context.Response.StatusCode = patchResult.StartsWith("⚠", StringComparison.Ordinal) ? 409 : 400;
                    await WriteJsonAsync(context, new { error = patchResult }).ConfigureAwait(false);
                    return;
                }

                await WriteJsonAsync(context, BuildCodeFileResponse(fullPath, patchResult)).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AppLogger.LogWarning($"[Companion] Code patch request failed: {ex.Message}");
                context.Response.StatusCode = 500;
                await WriteJsonAsync(context, new { error = ex.Message }).ConfigureAwait(false);
            }
        }

        private async Task HandleCodeRunAsync(HttpListenerContext context)
        {
            CodeRunRequest? request;

            try
            {
                request = await ReadJsonAsync<CodeRunRequest>(context.Request).ConfigureAwait(false);
            }
            catch (JsonException ex)
            {
                context.Response.StatusCode = 400;
                await WriteJsonAsync(context, new { error = $"Invalid JSON payload: {ex.Message}" }).ConfigureAwait(false);
                return;
            }

            var action = (request?.Action ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(action))
            {
                context.Response.StatusCode = 400;
                await WriteJsonAsync(context, new { error = "A non-empty 'action' field is required." }).ConfigureAwait(false);
                return;
            }

            try
            {
                var command = ResolveCodeCommand(action, request?.Command);
                if (string.IsNullOrWhiteSpace(command))
                {
                    context.Response.StatusCode = 400;
                    await WriteJsonAsync(context, new { error = $"Atlas could not resolve a command for code action '{action}'." }).ConfigureAwait(false);
                    return;
                }

                var output = await _companionCodeAssistant.RunCommandAsync(command, request?.TimeoutSeconds ?? 60).ConfigureAwait(false);
                await WriteJsonAsync(context, new
                {
                    ok = true,
                    action,
                    command,
                    output,
                    state = BuildCodeStateResponse(),
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AppLogger.LogWarning($"[Companion] Code run request failed: {ex.Message}");
                context.Response.StatusCode = 500;
                await WriteJsonAsync(context, new { error = ex.Message }).ConfigureAwait(false);
            }
        }

        private static object BuildDownloaderStateResponse()
        {
            var manager = AtlasAI.Modules.Downloader.DownloadManager.Instance;
            return new
            {
                routeAvailable = true,
                state = manager.GetUiState(),
                lastUpdatedUtc = DateTime.UtcNow,
            };
        }

        private static DjConsoleView? GetActiveDjConsoleView()
        {
            if (DjConsoleView.ActiveInstance != null)
            {
                return DjConsoleView.ActiveInstance;
            }

            try
            {
                return EnumerateVisualChildren<DjConsoleView>(Application.Current).FirstOrDefault();
            }
            catch
            {
                return null;
            }
        }

        private static async Task<object> ActivateDjBoothAsync()
        {
            if (Application.Current == null)
            {
                return new
                {
                    ok = false,
                    routeAvailable = false,
                    message = "Atlas UI is not available to activate the DJ booth.",
                };
            }

            return await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                var commandCenter = Application.Current.Windows
                    .OfType<CommandCenterWindow>()
                    .FirstOrDefault(window => window.IsLoaded);

                if (commandCenter == null)
                {
                    commandCenter = new CommandCenterWindow();
                    commandCenter.Show();
                }

                if (commandCenter.WindowState == WindowState.Minimized)
                {
                    commandCenter.WindowState = WindowState.Normal;
                }

                commandCenter.NavigateToTab("AI DJ BOOTH", "DJ");
                commandCenter.Activate();

                var djView = GetActiveDjConsoleView();
                if (djView == null)
                {
                    return (object)new
                    {
                        ok = true,
                        routeAvailable = false,
                        message = "DJ booth activation was requested. Atlas is still bringing the booth online.",
                    };
                }

                return (object)new
                {
                    ok = true,
                    routeAvailable = true,
                    message = "DJ booth is active on the Atlas host.",
                    state = djView.GetCompanionState(),
                };
            }).Task.ConfigureAwait(false);
        }

        private object BuildCodeStateResponse()
        {
            var workspacePath = _companionCodeAssistant.WorkspacePath ?? string.Empty;
            return new
            {
                routeAvailable = true,
                hasWorkspace = _companionCodeAssistant.HasWorkspace,
                workspacePath,
                workspaceName = string.IsNullOrWhiteSpace(workspacePath) ? string.Empty : Path.GetFileName(workspacePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
                projectStructure = _companionCodeAssistant.GetProjectStructure(2),
                suggestedActions = new[]
                {
                    new { id = "build", label = "Build workspace" },
                    new { id = "test", label = "Run tests" },
                    new { id = "command", label = "Run custom command" },
                },
                lastUpdatedUtc = DateTime.UtcNow,
            };
        }

        private object BuildCodeFileResponse(string fullPath, string? message = null)
        {
            var workspacePath = _companionCodeAssistant.WorkspacePath ?? Path.GetDirectoryName(fullPath) ?? fullPath;
            return new
            {
                path = Path.GetRelativePath(workspacePath, fullPath),
                fullPath,
                content = File.Exists(fullPath) ? File.ReadAllText(fullPath) : string.Empty,
                fileInfo = _companionCodeAssistant.GetFileInfo(fullPath),
                message = message ?? string.Empty,
            };
        }

        private IEnumerable<object> BuildCodeWorkspaceCandidates()
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var candidates = new List<string>();

            void AddCandidate(string? path)
            {
                var trimmed = (path ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(trimmed) || !Directory.Exists(trimmed) || !seen.Add(trimmed))
                {
                    return;
                }

                candidates.Add(trimmed);
            }

            AddCandidate(_companionCodeAssistant.WorkspacePath);
            AddCandidate(Directory.GetCurrentDirectory());

            var currentDirectory = Directory.GetCurrentDirectory();
            try
            {
                foreach (var directory in Directory.EnumerateDirectories(currentDirectory))
                {
                    if (File.Exists(Path.Combine(directory, ".git")) ||
                        Directory.Exists(Path.Combine(directory, ".git")) ||
                        Directory.EnumerateFiles(directory, "*.sln*", SearchOption.TopDirectoryOnly).Any() ||
                        File.Exists(Path.Combine(directory, "package.json")) ||
                        Directory.EnumerateFiles(directory, "*.csproj", SearchOption.TopDirectoryOnly).Any())
                    {
                        AddCandidate(directory);
                    }
                }
            }
            catch
            {
            }

            return candidates.Select(path => new
            {
                path,
                label = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
                isCurrent = string.Equals(path, _companionCodeAssistant.WorkspacePath, StringComparison.OrdinalIgnoreCase),
            }).ToList();
        }

        private object BuildCodeSearchResponse(string query, string mode, int maxResults)
        {
            var workspace = _companionCodeAssistant.WorkspacePath;
            if (string.IsNullOrWhiteSpace(workspace) || !Directory.Exists(workspace))
            {
                return new
                {
                    routeAvailable = true,
                    hasWorkspace = false,
                    query,
                    mode,
                    count = 0,
                    items = Array.Empty<object>(),
                };
            }

            var normalizedMode = NormalizeToken(mode);
            var items = new List<object>();

            foreach (var file in EnumerateCodeWorkspaceFiles(workspace))
            {
                if (items.Count >= maxResults)
                {
                    break;
                }

                var relativePath = Path.GetRelativePath(workspace, file);
                if (normalizedMode == "file" || normalizedMode == "files")
                {
                    if (Path.GetFileName(file).Contains(query, StringComparison.OrdinalIgnoreCase) ||
                        relativePath.Contains(query, StringComparison.OrdinalIgnoreCase))
                    {
                        items.Add(new
                        {
                            type = "file",
                            path = relativePath,
                            preview = relativePath,
                        });
                    }
                    continue;
                }

                string[] lines;
                try
                {
                    lines = File.ReadAllLines(file);
                }
                catch
                {
                    continue;
                }

                for (var index = 0; index < lines.Length && items.Count < maxResults; index++)
                {
                    var line = lines[index];
                    if (!line.Contains(query, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    items.Add(new
                    {
                        type = "match",
                        path = relativePath,
                        line = index + 1,
                        preview = line.Trim(),
                    });
                }
            }

            return new
            {
                routeAvailable = true,
                hasWorkspace = true,
                query,
                mode = normalizedMode,
                count = items.Count,
                items,
            };
        }

        private IEnumerable<string> EnumerateCodeWorkspaceFiles(string workspacePath)
        {
            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(workspacePath, "*.*", SearchOption.AllDirectories);
            }
            catch
            {
                return Enumerable.Empty<string>();
            }

            return files.Where(file =>
            {
                var relative = Path.GetRelativePath(workspacePath, file);
                var segments = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                return !segments.Any(segment =>
                    string.Equals(segment, "node_modules", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(segment, "bin", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(segment, "obj", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(segment, ".git", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(segment, ".vs", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(segment, "dist", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(segment, "build", StringComparison.OrdinalIgnoreCase));
            });
        }

        private string? ResolveCodeFilePath(string path)
        {
            var workspace = _companionCodeAssistant.WorkspacePath;
            if (string.IsNullOrWhiteSpace(workspace) || !Directory.Exists(workspace))
            {
                return null;
            }

            var candidate = Path.IsPathRooted(path)
                ? path
                : Path.GetFullPath(Path.Combine(workspace, path));
            var normalizedWorkspace = Path.GetFullPath(workspace);
            var normalizedCandidate = Path.GetFullPath(candidate);
            return normalizedCandidate.StartsWith(normalizedWorkspace, StringComparison.OrdinalIgnoreCase)
                ? normalizedCandidate
                : null;
        }

        private string GetRelativeCodePath(string fullPath)
        {
            var workspace = _companionCodeAssistant.WorkspacePath;
            return string.IsNullOrWhiteSpace(workspace)
                ? fullPath
                : Path.GetRelativePath(workspace, fullPath);
        }

        private string? ResolveCodeCommand(string action, string? explicitCommand)
        {
            var normalizedAction = NormalizeToken(action);
            if (normalizedAction is "command" or "run")
            {
                return (explicitCommand ?? string.Empty).Trim();
            }

            var workspace = _companionCodeAssistant.WorkspacePath;
            if (string.IsNullOrWhiteSpace(workspace) || !Directory.Exists(workspace))
            {
                return null;
            }

            var hasSln = Directory.EnumerateFiles(workspace, "*.sln*", SearchOption.TopDirectoryOnly).Any();
            var hasCsproj = Directory.EnumerateFiles(workspace, "*.csproj", SearchOption.AllDirectories).Any();
            var hasPackageJson = File.Exists(Path.Combine(workspace, "package.json"));

            return normalizedAction switch
            {
                "build" when hasSln || hasCsproj => "dotnet build",
                "test" when hasSln || hasCsproj => "dotnet test",
                "build" when hasPackageJson => "npm run build",
                "test" when hasPackageJson => "npm test",
                _ => (explicitCommand ?? string.Empty).Trim(),
            };
        }

        private async Task HandleRemoteStateAsync(HttpListenerContext context)
        {
            try
            {
                var desktop = _remoteDesktopService.GetState(
                    CompanionRemoteDesktopService.PreviewFramePath,
                    RemoteDesktopStreamPath);
                await WriteJsonAsync(context, new
                {
                    desktop = new
                    {
                        isAvailable = desktop.IsAvailable,
                        sessionName = desktop.SessionName,
                        activeApp = desktop.ActiveApp,
                        windowTitle = desktop.WindowTitle,
                        previewUrl = desktop.PreviewPath,
                        frameUrl = desktop.PreviewPath,
                        streamUrl = desktop.LiveStreamPath,
                        liveStreamUrl = desktop.LiveStreamPath,
                        width = desktop.Width,
                        height = desktop.Height,
                        supportsPointer = desktop.SupportsPointer,
                        supportsKeyboard = desktop.SupportsKeyboard,
                        supportsClipboard = desktop.SupportsClipboard,
                        supportsSystemShortcuts = desktop.SupportsSystemShortcuts,
                    },
                    activeApp = desktop.ActiveApp,
                    windowTitle = desktop.WindowTitle,
                    supportsCameraDock = true,
                    generatedAtUtc = DateTime.UtcNow,
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AppLogger.LogWarning($"[Companion] Remote state request failed: {ex.Message}");
                context.Response.StatusCode = 500;
                await WriteJsonAsync(context, new { error = ex.Message }).ConfigureAwait(false);
            }
        }

        private async Task HandleRemoteActionAsync(HttpListenerContext context)
        {
            RemoteDesktopActionRequest? request;

            try
            {
                request = await ReadJsonAsync<RemoteDesktopActionRequest>(context.Request).ConfigureAwait(false);
            }
            catch (JsonException ex)
            {
                context.Response.StatusCode = 400;
                await WriteJsonAsync(context, new { error = $"Invalid JSON payload: {ex.Message}" }).ConfigureAwait(false);
                return;
            }

            try
            {
                var result = await ExecuteCompanionRemoteActionAsync(request ?? new RemoteDesktopActionRequest())
                    .ConfigureAwait(false);
                var desktop = _remoteDesktopService.GetState(
                    CompanionRemoteDesktopService.PreviewFramePath,
                    RemoteDesktopStreamPath);
                await WriteJsonAsync(context, new
                {
                    ok = result.Ok,
                    message = result.Message,
                    desktop = new
                    {
                        isAvailable = desktop.IsAvailable,
                        activeApp = desktop.ActiveApp,
                        windowTitle = desktop.WindowTitle,
                        width = desktop.Width,
                        height = desktop.Height,
                    }
                }).ConfigureAwait(false);
            }
            catch (InvalidOperationException ex)
            {
                context.Response.StatusCode = 400;
                await WriteJsonAsync(context, new { error = ex.Message }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AppLogger.LogWarning($"[Companion] Remote action request failed: {ex.Message}");
                context.Response.StatusCode = 500;
                await WriteJsonAsync(context, new { error = ex.Message }).ConfigureAwait(false);
            }
        }

        private async Task<CompanionRemoteActionResult> ExecuteCompanionRemoteActionAsync(RemoteDesktopActionRequest request)
        {
            var action = NormalizeToken(request.Action);
            return action switch
            {
                "desktopcontrol" or "opendesktop" or "openatlasui" =>
                    await ActivateAtlasWindowAsync().ConfigureAwait(false),
                "launchmediacentre" =>
                    await ActivateAtlasWindowAsync("AI MEDIA CENTRE", "Media").ConfigureAwait(false),
                "opencamera" or "opencameras" =>
                    await ActivateSecuritySurfaceAsync().ConfigureAwait(false),
                "sleep" => QueueSleepAction(),
                "wake" => new CompanionRemoteActionResult(true, "Atlas is already online."),
                "restartservices" => QueueAtlasRestartAction(),
                _ => _remoteDesktopService.ExecuteAction(request),
            };
        }

        private static CompanionRemoteActionResult QueueSleepAction()
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(600).ConfigureAwait(false);
                    await SystemTool.SleepAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    AppLogger.LogWarning($"[Companion] Sleep request failed: {ex.Message}");
                }
            });

            return new CompanionRemoteActionResult(true, "Atlas PC is going to sleep.");
        }

        private static CompanionRemoteActionResult QueueAtlasRestartAction()
        {
            var executablePath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(executablePath))
            {
                executablePath = Process.GetCurrentProcess().MainModule?.FileName;
            }

            if (string.IsNullOrWhiteSpace(executablePath))
            {
                throw new InvalidOperationException("Atlas could not resolve its executable path for restart.");
            }

            var resolvedExecutablePath = executablePath;
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(900).ConfigureAwait(false);
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = resolvedExecutablePath,
                        WorkingDirectory = Path.GetDirectoryName(resolvedExecutablePath) ?? AppContext.BaseDirectory,
                        UseShellExecute = true,
                    });

                    Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        try
                        {
                            Application.Current?.Shutdown();
                        }
                        catch
                        {
                        }
                    }));
                }
                catch (Exception ex)
                {
                    AppLogger.LogWarning($"[Companion] Atlas restart request failed: {ex.Message}");
                }
            });

            return new CompanionRemoteActionResult(true, "Atlas is restarting.");
        }

        private static async Task<CompanionRemoteActionResult> ActivateAtlasWindowAsync(
            string? tab = null,
            string? sidebarKey = null)
        {
            if (Application.Current == null)
            {
                throw new InvalidOperationException("Atlas UI is not available.");
            }

            return await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                var commandCenter = GetPrimaryCommandCenterWindow();
                if (commandCenter == null)
                {
                    throw new InvalidOperationException("Atlas command centre is not available.");
                }

                if (commandCenter.WindowState == WindowState.Minimized)
                {
                    commandCenter.WindowState = WindowState.Normal;
                }

                if (!commandCenter.IsVisible)
                {
                    commandCenter.Show();
                }

                if (!string.IsNullOrWhiteSpace(tab))
                {
                    commandCenter.NavigateToTab(tab, sidebarKey ?? tab);
                }

                commandCenter.Activate();
                return new CompanionRemoteActionResult(
                    true,
                    string.IsNullOrWhiteSpace(tab)
                        ? "Atlas UI opened."
                        : $"Atlas {sidebarKey ?? tab} opened.");
            }).Task.ConfigureAwait(false);
        }

        private static async Task<CompanionRemoteActionResult> ActivateSecuritySurfaceAsync()
        {
            if (Application.Current == null)
            {
                throw new InvalidOperationException("Atlas UI is not available.");
            }

            return await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                var commandCenter = GetPrimaryCommandCenterWindow();
                if (commandCenter != null)
                {
                    if (commandCenter.WindowState == WindowState.Minimized)
                    {
                        commandCenter.WindowState = WindowState.Normal;
                    }

                    if (!commandCenter.IsVisible)
                    {
                        commandCenter.Show();
                    }

                    commandCenter.NavigateToTab("AI SECURITY", "Security");
                    commandCenter.Activate();
                    return new CompanionRemoteActionResult(true, "Atlas Security opened.");
                }

                var securityWindow = Application.Current.Windows
                    .OfType<SecuritySuiteWindow>()
                    .OrderByDescending(window => window.IsActive)
                    .FirstOrDefault();

                if (securityWindow == null)
                {
                    securityWindow = new SecuritySuiteWindow();
                    securityWindow.Show();
                }
                else if (!securityWindow.IsVisible)
                {
                    securityWindow.Show();
                }

                if (securityWindow.WindowState == WindowState.Minimized)
                {
                    securityWindow.WindowState = WindowState.Normal;
                }

                securityWindow.Activate();
                return new CompanionRemoteActionResult(true, "Atlas Security opened.");
            }).Task.ConfigureAwait(false);
        }

        private async Task HandleRemoteDesktopFrameAsync(HttpListenerContext context)
        {
            try
            {
                var profile = ResolveRemoteDesktopProfile(context.Request.QueryString["profile"]);
                var frame = _remoteDesktopService.CaptureFrame(
                    maxWidth: profile.MaxWidth,
                    maxHeight: profile.MaxHeight,
                    quality: profile.Quality);
                context.Response.StatusCode = 200;
                context.Response.ContentType = frame.ContentType;
                context.Response.ContentLength64 = frame.Bytes.Length;
                context.Response.AddHeader("X-Atlas-Remote-Profile", profile.Name);
                context.Response.AddHeader("Cache-Control", "no-store, no-cache, must-revalidate");
                context.Response.AddHeader("Pragma", "no-cache");
                await context.Response.OutputStream.WriteAsync(frame.Bytes, 0, frame.Bytes.Length).ConfigureAwait(false);
                context.Response.Close();
            }
            catch (Exception ex)
            {
                AppLogger.LogWarning($"[Companion] Remote frame request failed: {ex.Message}");
                try
                {
                    context.Response.StatusCode = 500;
                    await WriteJsonAsync(context, new { error = ex.Message }).ConfigureAwait(false);
                }
                catch
                {
                }
            }
        }

        private async Task HandleRemoteDesktopStreamAsync(HttpListenerContext context, CancellationToken cancellationToken)
        {
            WebSocket? socket = null;

            try
            {
                var requestedTransport = context.Request.QueryString["transport"];
                var profile = ResolveRemoteDesktopProfile(context.Request.QueryString["profile"]);
                var useBinaryFrames = string.Equals(
                    requestedTransport,
                    RemoteDesktopBinaryTransport,
                    StringComparison.OrdinalIgnoreCase);
                var webSocketContext = await context.AcceptWebSocketAsync(null).ConfigureAwait(false);
                socket = webSocketContext.WebSocket;

                var desktop = _remoteDesktopService.GetState(
                    CompanionRemoteDesktopService.PreviewFramePath,
                    RemoteDesktopStreamPath);

                await SendSocketMessageAsync(socket, new
                {
                    type = "ready",
                    stream = "remote-desktop",
                    transport = useBinaryFrames ? RemoteDesktopBinaryTransport : "json-base64",
                    frameIntervalMs = profile.FrameIntervalMs,
                    profile = profile.Name,
                    desktop = new
                    {
                        isAvailable = desktop.IsAvailable,
                        sessionName = desktop.SessionName,
                        activeApp = desktop.ActiveApp,
                        windowTitle = desktop.WindowTitle,
                        width = desktop.Width,
                        height = desktop.Height,
                        supportsPointer = desktop.SupportsPointer,
                        supportsKeyboard = desktop.SupportsKeyboard,
                        supportsClipboard = desktop.SupportsClipboard,
                        supportsSystemShortcuts = desktop.SupportsSystemShortcuts,
                    },
                }, cancellationToken).ConfigureAwait(false);

                var stateHeartbeat = 0;
                var lastActiveApp = desktop.ActiveApp;
                var lastWindowTitle = desktop.WindowTitle;

                while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
                {
                    desktop = _remoteDesktopService.GetState(
                        CompanionRemoteDesktopService.PreviewFramePath,
                        RemoteDesktopStreamPath);
                    var frame = _remoteDesktopService.CaptureFrame(
                        maxWidth: profile.MaxWidth,
                        maxHeight: profile.MaxHeight,
                        quality: profile.Quality);

                    var shouldSendState = stateHeartbeat == 0 ||
                        !string.Equals(lastActiveApp, desktop.ActiveApp, StringComparison.Ordinal) ||
                        !string.Equals(lastWindowTitle, desktop.WindowTitle, StringComparison.Ordinal);

                    if (shouldSendState)
                    {
                        await SendSocketMessageAsync(socket, new
                        {
                            type = "state",
                            transport = useBinaryFrames ? RemoteDesktopBinaryTransport : "json-base64",
                            profile = profile.Name,
                            desktop = new
                            {
                                isAvailable = desktop.IsAvailable,
                                sessionName = desktop.SessionName,
                                activeApp = desktop.ActiveApp,
                                windowTitle = desktop.WindowTitle,
                                width = desktop.Width,
                                height = desktop.Height,
                                supportsPointer = desktop.SupportsPointer,
                                supportsKeyboard = desktop.SupportsKeyboard,
                                supportsClipboard = desktop.SupportsClipboard,
                                supportsSystemShortcuts = desktop.SupportsSystemShortcuts,
                            },
                            frameWidth = frame.Width,
                            frameHeight = frame.Height,
                            capturedAtUtc = frame.CapturedAtUtc,
                        }, cancellationToken).ConfigureAwait(false);

                        lastActiveApp = desktop.ActiveApp;
                        lastWindowTitle = desktop.WindowTitle;
                    }

                    if (useBinaryFrames)
                    {
                        await SendSocketBinaryAsync(socket, frame.Bytes, cancellationToken).ConfigureAwait(false);
                    }
                    else
                    {
                        await SendSocketMessageAsync(socket, new
                        {
                            type = "frame",
                            profile = profile.Name,
                            desktop = new
                            {
                                isAvailable = desktop.IsAvailable,
                                sessionName = desktop.SessionName,
                                activeApp = desktop.ActiveApp,
                                windowTitle = desktop.WindowTitle,
                                width = desktop.Width,
                                height = desktop.Height,
                                supportsPointer = desktop.SupportsPointer,
                                supportsKeyboard = desktop.SupportsKeyboard,
                                supportsClipboard = desktop.SupportsClipboard,
                                supportsSystemShortcuts = desktop.SupportsSystemShortcuts,
                            },
                            imageBase64 = Convert.ToBase64String(frame.Bytes),
                            contentType = frame.ContentType,
                            frameWidth = frame.Width,
                            frameHeight = frame.Height,
                            capturedAtUtc = frame.CapturedAtUtc,
                        }, cancellationToken).ConfigureAwait(false);
                    }

                    stateHeartbeat = (stateHeartbeat + 1) % 8;
                    await Task.Delay(profile.FrameIntervalMs, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                AppLogger.LogWarning($"[Companion] Remote desktop stream error: {ex.Message}");
                if (socket != null && socket.State == WebSocketState.Open)
                {
                    await SendSocketMessageAsync(socket, new
                    {
                        type = "error",
                        message = ex.Message,
                    }, CancellationToken.None).ConfigureAwait(false);
                }
            }
            finally
            {
                if (socket != null)
                {
                    try
                    {
                        if (socket.State == WebSocketState.Open || socket.State == WebSocketState.CloseReceived)
                        {
                            await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None)
                                .ConfigureAwait(false);
                        }
                    }
                    catch
                    {
                    }

                    socket.Dispose();
                }
            }
        }

        private static RemoteDesktopStreamProfile ResolveRemoteDesktopProfile(string? requestedProfile)
        {
            var normalized = (requestedProfile ?? string.Empty).Trim().ToLowerInvariant();
            return normalized switch
            {
                "performance" or "smooth" => new RemoteDesktopStreamProfile("performance", 800, 450, 34L, 110),
                "detail" or "sharp" or "quality" => new RemoteDesktopStreamProfile("detail", 1280, 720, 58L, 180),
                _ => new RemoteDesktopStreamProfile("balanced", 960, 540, 45L, RemoteDesktopStreamIntervalMs),
            };
        }

        private sealed record RemoteDesktopStreamProfile(
            string Name,
            int MaxWidth,
            int MaxHeight,
            long Quality,
            int FrameIntervalMs);

        private async Task HandleSmartHomeActionAsync(HttpListenerContext context, CancellationToken cancellationToken)
        {
            CompanionSmartHomeActionRequest? request;

            try
            {
                request = await ReadJsonAsync<CompanionSmartHomeActionRequest>(context.Request).ConfigureAwait(false);
            }
            catch (JsonException ex)
            {
                context.Response.StatusCode = 400;
                await WriteJsonAsync(context, new { error = $"Invalid JSON payload: {ex.Message}" }).ConfigureAwait(false);
                return;
            }

            await HandleSmartHomeActionCoreAsync(context, request, forceToggle: false, cancellationToken).ConfigureAwait(false);
        }

        private async Task HandleSmartHomeToggleAsync(HttpListenerContext context, CancellationToken cancellationToken)
        {
            CompanionSmartHomeActionRequest? request;

            try
            {
                request = await ReadJsonAsync<CompanionSmartHomeActionRequest>(context.Request).ConfigureAwait(false);
            }
            catch (JsonException ex)
            {
                context.Response.StatusCode = 400;
                await WriteJsonAsync(context, new { error = $"Invalid JSON payload: {ex.Message}" }).ConfigureAwait(false);
                return;
            }

            await HandleSmartHomeActionCoreAsync(context, request, forceToggle: true, cancellationToken).ConfigureAwait(false);
        }

        private async Task HandleSmartHomeActionCoreAsync(
            HttpListenerContext context,
            CompanionSmartHomeActionRequest? request,
            bool forceToggle,
            CancellationToken cancellationToken)
        {
            try
            {
                var snapshot = await _smartHomeRuntimeService.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
                if (!TryResolveCompanionActionRequest(
                        request,
                        snapshot,
                        forceToggle,
                        out var actionRequest,
                        out var requestDevice,
                        out var errorMessage,
                        out var statusCode))
                {
                    context.Response.StatusCode = statusCode;
                    await WriteJsonAsync(context, new { error = errorMessage }).ConfigureAwait(false);
                    return;
                }

                var result = await _smartHomeRuntimeService.ExecuteActionAsync(actionRequest, cancellationToken).ConfigureAwait(false);
                var refreshedSnapshot = await _smartHomeRuntimeService.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
                var refreshedDevice = FindSnapshotDevice(refreshedSnapshot, actionRequest.ProviderId, actionRequest.DeviceId);

                await WriteJsonAsync(context, new
                {
                    ok = result.Ok,
                    outcome = result.Outcome,
                    message = result.Message,
                    providerId = result.ProviderId,
                    deviceId = result.DeviceId,
                    capabilityType = result.CapabilityType,
                    capabilityInstance = result.CapabilityInstance,
                    device = refreshedDevice == null
                        ? requestDevice == null
                            ? null
                            : BuildCompanionSmartHomeDevice(requestDevice.Value.Provider, requestDevice.Value.Device, refreshedSnapshot.GeneratedAtUtc)
                        : BuildCompanionSmartHomeDevice(refreshedDevice.Value.Provider, refreshedDevice.Value.Device, refreshedSnapshot.GeneratedAtUtc),
                    snapshotGeneratedAtUtc = refreshedSnapshot.GeneratedAtUtc,
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AppLogger.LogWarning($"[Companion] Smart Home action request failed: {ex.Message}");
                context.Response.StatusCode = 500;
                await WriteJsonAsync(context, new { error = ex.Message }).ConfigureAwait(false);
            }
        }

        private bool TryResolveCompanionActionRequest(
            CompanionSmartHomeActionRequest? request,
            SmartHomeSnapshot snapshot,
            bool forceToggle,
            out SmartHomeActionRequest actionRequest,
            out (SmartHomeProviderState Provider, SmartHomeDevice Device)? requestDevice,
            out string errorMessage,
            out int statusCode)
        {
            actionRequest = null!;
            requestDevice = null;
            errorMessage = string.Empty;
            statusCode = 400;

            if (request == null)
            {
                errorMessage = "A Smart Home action payload is required.";
                return false;
            }

            var providerId = FirstNonEmpty(request.ProviderId, request.Provider)?.Trim() ?? string.Empty;
            var deviceId = FirstNonEmpty(request.DeviceId, request.Id)?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(deviceId))
            {
                errorMessage = "A non-empty 'deviceId' or legacy 'id' field is required.";
                return false;
            }

            var snapshotDevice = FindSnapshotDevice(snapshot, providerId, deviceId);
            if (snapshotDevice == null)
            {
                statusCode = 404;
                errorMessage = string.IsNullOrWhiteSpace(providerId)
                    ? $"Smart Home device '{deviceId}' was not found. Include 'providerId' if multiple providers expose similar IDs."
                    : $"Smart Home device '{deviceId}' was not found for provider '{providerId}'.";
                return false;
            }

            requestDevice = snapshotDevice;
            providerId = snapshotDevice.Value.Provider.ProviderId;
            var sku = FirstNonEmpty(request.Sku, snapshotDevice.Value.Device.Sku) ?? string.Empty;

            if (forceToggle)
            {
                var toggleCapability = FindCapability(
                    snapshotDevice.Value.Device,
                    typeAliases: new[] { "devices.capabilities.on_off", "on_off", "onoff" },
                    instanceAliases: new[] { "powerswitch", "power", "switch", "mainpower" });
                if (toggleCapability == null)
                {
                    errorMessage = "This device does not expose a toggleable power capability in the current Atlas snapshot.";
                    return false;
                }

                var requestedPowerState = request.IsOn ?? TogglePowerValue(toggleCapability.StateValue);
                if (requestedPowerState == null)
                {
                    errorMessage = "Atlas could not infer a target power state for this device. Send 'isOn' explicitly.";
                    return false;
                }

                actionRequest = new SmartHomeActionRequest
                {
                    ProviderId = providerId,
                    DeviceId = snapshotDevice.Value.Device.DeviceId,
                    Sku = sku,
                    CapabilityType = toggleCapability.Type,
                    CapabilityInstance = toggleCapability.Instance,
                    Value = JsonSerializer.SerializeToElement(requestedPowerState.Value),
                };

                return true;
            }

            var capabilityType = request.CapabilityType?.Trim() ?? string.Empty;
            var capabilityInstance = request.CapabilityInstance?.Trim() ?? string.Empty;
            var hasDirectCapabilityRequest = !string.IsNullOrWhiteSpace(capabilityType) && !string.IsNullOrWhiteSpace(capabilityInstance);

            if (hasDirectCapabilityRequest)
            {
                var directValue = ResolveDirectActionValue(request);
                if (directValue.ValueKind == JsonValueKind.Undefined)
                {
                    errorMessage = "A Smart Home action 'value' is required when sending a direct capability request.";
                    return false;
                }

                actionRequest = new SmartHomeActionRequest
                {
                    ProviderId = providerId,
                    DeviceId = snapshotDevice.Value.Device.DeviceId,
                    Sku = sku,
                    CapabilityType = capabilityType,
                    CapabilityInstance = capabilityInstance,
                    Value = directValue,
                };

                return true;
            }

            var normalizedAction = NormalizeToken(request.Action);
            if (string.IsNullOrWhiteSpace(normalizedAction))
            {
                errorMessage = "Send either 'capabilityType'/'capabilityInstance' or a supported legacy 'action'.";
                return false;
            }

            if (!TryResolveLegacyAction(
                    snapshotDevice.Value.Device,
                    normalizedAction,
                    request,
                    out var capability,
                    out var value,
                    out errorMessage))
            {
                return false;
            }

            actionRequest = new SmartHomeActionRequest
            {
                ProviderId = providerId,
                DeviceId = snapshotDevice.Value.Device.DeviceId,
                Sku = sku,
                CapabilityType = capability.Type,
                CapabilityInstance = capability.Instance,
                Value = value,
            };

            return true;
        }

        private static bool TryResolveLegacyAction(
            SmartHomeDevice device,
            string normalizedAction,
            CompanionSmartHomeActionRequest request,
            out SmartHomeCapability capability,
            out JsonElement value,
            out string errorMessage)
        {
            capability = null!;
            value = default;
            errorMessage = string.Empty;

            switch (normalizedAction)
            {
                case "toggle":
                case "togglepower":
                case "poweron":
                case "poweroff":
                    capability = FindCapability(
                        device,
                        typeAliases: new[] { "devices.capabilities.on_off", "on_off", "onoff" },
                        instanceAliases: new[] { "powerswitch", "power", "switch", "mainpower" }) ?? null!;
                    if (capability == null)
                    {
                        errorMessage = "This device does not expose a power capability in the current Atlas snapshot.";
                        return false;
                    }

                    var requestedPowerState = request.IsOn;
                    if (requestedPowerState == null)
                    {
                        requestedPowerState = normalizedAction switch
                        {
                            "poweron" => true,
                            "poweroff" => false,
                            _ => TogglePowerValue(capability.StateValue),
                        };
                    }

                    if (requestedPowerState == null)
                    {
                        errorMessage = "Atlas could not infer a target power state for this device. Send 'isOn' explicitly.";
                        return false;
                    }

                    value = JsonSerializer.SerializeToElement(requestedPowerState.Value);
                    return true;

                case "setbrightness":
                case "brightness":
                case "setlevel":
                case "setdimmer":
                    capability = FindCapability(
                        device,
                        typeAliases: new[] { "devices.capabilities.range", "range" },
                        instanceAliases: new[] { "brightness", "dimmer", "lightlevel" }) ?? null!;
                    if (capability == null)
                    {
                        errorMessage = "This device does not expose a brightness capability in the current Atlas snapshot.";
                        return false;
                    }

                    var brightness = request.Brightness ?? request.Level ?? ReadNumericValue(request.Value);
                    if (brightness == null)
                    {
                        errorMessage = "A numeric brightness value is required.";
                        return false;
                    }

                    value = JsonSerializer.SerializeToElement(brightness.Value);
                    return true;

                case "setvolume":
                case "volume":
                case "setaudiolevel":
                    capability = FindCapability(
                        device,
                        typeAliases: new[] { "devices.capabilities.range", "range" },
                        instanceAliases: new[] { "volume", "audiovolume", "soundlevel" }) ?? null!;
                    if (capability == null)
                    {
                        errorMessage = "This device does not expose a volume capability in the current Atlas snapshot.";
                        return false;
                    }

                    var volume = request.Level ?? ReadNumericValue(request.Value);
                    if (volume == null)
                    {
                        errorMessage = "A numeric volume value is required.";
                        return false;
                    }

                    value = JsonSerializer.SerializeToElement(volume.Value);
                    return true;

                case "mute":
                case "unmute":
                case "setmute":
                    capability = FindCapability(
                        device,
                        typeAliases: new[] { "devices.capabilities.toggle", "toggle", "devices.capabilities.on_off", "on_off", "onoff" },
                        instanceAliases: new[] { "mute", "muted", "audiomute" }) ?? null!;
                    if (capability == null)
                    {
                        errorMessage = "This device does not expose a mute capability in the current Atlas snapshot.";
                        return false;
                    }

                    var mute = request.IsOn;
                    if (mute == null)
                    {
                        mute = normalizedAction switch
                        {
                            "mute" => true,
                            "unmute" => false,
                            _ => TogglePowerValue(capability.StateValue),
                        };
                    }

                    if (mute == null)
                    {
                        errorMessage = "Atlas could not infer a target mute state for this device.";
                        return false;
                    }

                    value = JsonSerializer.SerializeToElement(mute.Value);
                    return true;

                case "setinputsource":
                case "inputsource":
                case "switchinput":
                    capability = FindCapability(
                        device,
                        typeAliases: new[] { "devices.capabilities.mode", "mode" },
                        instanceAliases: new[] { "inputsource", "input", "source" }) ?? null!;
                    if (capability == null)
                    {
                        errorMessage = "This device does not expose an input-source capability in the current Atlas snapshot.";
                        return false;
                    }

                    var inputSource = ReadStringValue(request.Value);
                    if (string.IsNullOrWhiteSpace(inputSource))
                    {
                        errorMessage = "An input source value is required.";
                        return false;
                    }

                    value = JsonSerializer.SerializeToElement(inputSource);
                    return true;

                case "settargettemperature":
                case "settemperature":
                case "settarget":
                case "thermostatsettarget":
                    capability = FindCapability(
                        device,
                        typeAliases: new[] { "devices.capabilities.range", "range" },
                        instanceAliases: new[] { "targettemperature", "targettemp", "setpoint", "thermostatsetpoint", "thermostattarget" }) ?? null!;
                    if (capability == null)
                    {
                        errorMessage = "This device does not expose a thermostat target capability in the current Atlas snapshot.";
                        return false;
                    }

                    var targetTemperature = request.TargetTemperature ?? ReadNumericValue(request.Value);
                    if (targetTemperature == null)
                    {
                        errorMessage = "A numeric thermostat target value is required.";
                        return false;
                    }

                    value = JsonSerializer.SerializeToElement(targetTemperature.Value);
                    return true;

                case "lock":
                case "unlock":
                    capability = FindCapability(
                        device,
                        typeAliases: new[] { "devices.capabilities.toggle", "toggle", "lock" },
                        instanceAliases: new[] { "lock", "lockstate", "doorlock", "locked" }) ?? null!;
                    if (capability == null)
                    {
                        errorMessage = "This device does not expose a lock capability in the current Atlas snapshot.";
                        return false;
                    }

                    var locked = request.Locked ?? (normalizedAction == "lock");
                    value = JsonSerializer.SerializeToElement(locked);
                    return true;

                case "trigger":
                case "triggerscene":
                case "runscene":
                case "runroutine":
                case "activate":
                case "execute":
                case "startroutine":
                    capability = FindCapability(
                        device,
                        typeAliases: new[] { "devices.capabilities.mode", "mode", "scene", "routine" },
                        instanceAliases: new[] { "scene", "routine", "trigger", "activate", "execute" }) ?? null!;
                    if (capability == null)
                    {
                        errorMessage = "This device does not expose a scene or routine capability in the current Atlas snapshot.";
                        return false;
                    }

                    value = request.Value.ValueKind == JsonValueKind.Undefined
                        ? JsonSerializer.SerializeToElement(true)
                        : request.Value;
                    return true;
            }

            errorMessage = $"Legacy action '{request.Action}' is not supported by the companion Smart Home API. Send the explicit capability request instead.";
            return false;
        }

        private static object BuildCompanionSmartHomeStateResponse(SmartHomeSnapshot snapshot)
        {
            return new
            {
                generatedAtUtc = snapshot.GeneratedAtUtc,
                totalDevices = snapshot.TotalDevices,
                onlineDevices = snapshot.OnlineDevices,
                configuredProviders = snapshot.ConfiguredProviders,
                providers = snapshot.Providers.Select(provider => new
                {
                    providerId = provider.ProviderId,
                    displayName = provider.DisplayName,
                    runtimeStatus = provider.RuntimeStatus,
                    isAvailable = provider.IsAvailable,
                    statusDetail = provider.StatusDetail,
                    error = provider.Error,
                    isConfigured = provider.Descriptor.IsConfigured,
                    deviceCount = provider.Devices.Count,
                }).ToArray(),
                devices = FlattenCompanionDevices(snapshot).ToArray(),
            };
        }

        private static IEnumerable<object> FlattenCompanionDevices(SmartHomeSnapshot snapshot)
        {
            foreach (var provider in snapshot.Providers)
            {
                foreach (var device in provider.Devices)
                {
                    yield return BuildCompanionSmartHomeDevice(provider, device, snapshot.GeneratedAtUtc);
                }
            }
        }

        private static object BuildCompanionSmartHomeDevice(
            SmartHomeProviderState provider,
            SmartHomeDevice device,
            DateTime generatedAtUtc)
        {
            var capabilityTokens = device.Capabilities
                .SelectMany(capability => new[] { capability.Instance, capability.Type })
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var supportedActions = BuildSupportedActions(device).ToArray();
            var category = ClassifyCompanionCategory(device);
            var powerState = ResolveCompanionPowerState(device);
            var brightness = ResolveCapabilityNumber(device, new[] { "brightness", "dimmer", "lightlevel" });
            var thermostatTarget = ResolveCapabilityNumber(device, new[] { "targettemperature", "targettemp", "setpoint", "thermostatsetpoint", "thermostattarget" });
            var thermostatCurrent = ResolveCapabilityNumber(device, new[] { "currenttemperature", "temperature", "ambienttemperature", "ambienttemp" });
            var humidity = ResolveCapabilityNumber(device, new[] { "humidity" });
            var media = BuildCompanionMedia(device);

            return new
            {
                id = device.DeviceId,
                provider = provider.ProviderId,
                providerId = provider.ProviderId,
                name = device.Name,
                type = category,
                category,
                deviceType = category,
                isOn = string.Equals(powerState, "on", StringComparison.OrdinalIgnoreCase),
                availability = device.Availability,
                controlMode = ResolveCompanionControlMode(device),
                powerState,
                brightness,
                thermostat = thermostatTarget == null && thermostatCurrent == null && humidity == null
                    ? null
                    : new
                    {
                        currentTemperature = thermostatCurrent,
                        targetTemperature = thermostatTarget,
                        humidity,
                        unit = ResolveCapabilityUnit(device, new[] { "targettemperature", "targettemp", "setpoint", "thermostatsetpoint", "thermostattarget", "currenttemperature", "temperature", "ambienttemperature", "ambienttemp" }),
                    },
                lastSeen = generatedAtUtc,
                capabilities = capabilityTokens,
                supportedActions,
                media,
                isOnline = device.IsOnline,
                isControllable = supportedActions.Length > 0,
                readOnly = supportedActions.Length == 0,
            };
        }

        private static object? BuildCompanionMedia(SmartHomeDevice device)
        {
            if (string.IsNullOrWhiteSpace(device.PreviewVideoUrl) &&
                string.IsNullOrWhiteSpace(device.PreviewImageUrl))
            {
                return null;
            }

            return new
            {
                streamUrl = string.IsNullOrWhiteSpace(device.PreviewVideoUrl) ? null : device.PreviewVideoUrl,
                snapshotUrl = string.IsNullOrWhiteSpace(device.PreviewImageUrl) ? null : device.PreviewImageUrl,
                thumbnailUrl = string.IsNullOrWhiteSpace(device.PreviewImageUrl) ? null : device.PreviewImageUrl,
                hasLiveStream = !string.IsNullOrWhiteSpace(device.PreviewVideoUrl),
                hasTwoWayAudio = false,
                isDoorbellRinging = false,
            };
        }

        private static IEnumerable<string> BuildSupportedActions(SmartHomeDevice device)
        {
            var actions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (FindCapability(device, new[] { "devices.capabilities.on_off", "on_off", "onoff" }, new[] { "powerswitch", "power", "switch", "mainpower" }) != null)
            {
                actions.Add("togglePower");
                actions.Add("powerOn");
                actions.Add("powerOff");
            }

            if (FindCapability(device, new[] { "devices.capabilities.range", "range" }, new[] { "brightness", "dimmer", "lightlevel" }) != null)
            {
                actions.Add("setBrightness");
            }

            if (FindCapability(device, new[] { "devices.capabilities.range", "range" }, new[] { "volume", "audiovolume", "soundlevel" }) != null)
            {
                actions.Add("setVolume");
            }

            if (FindCapability(device, new[] { "devices.capabilities.toggle", "toggle", "devices.capabilities.on_off", "on_off", "onoff" }, new[] { "mute", "muted", "audiomute" }) != null)
            {
                actions.Add("mute");
                actions.Add("unmute");
            }

            if (FindCapability(device, new[] { "devices.capabilities.mode", "mode" }, new[] { "inputsource", "input", "source" }) != null)
            {
                actions.Add("setInputSource");
            }

            if (FindCapability(device, new[] { "devices.capabilities.range", "range" }, new[] { "targettemperature", "targettemp", "setpoint", "thermostatsetpoint", "thermostattarget" }) != null)
            {
                actions.Add("setTargetTemperature");
            }

            if (FindCapability(device, new[] { "devices.capabilities.toggle", "toggle", "lock" }, new[] { "lock", "lockstate", "doorlock", "locked" }) != null)
            {
                actions.Add("lock");
                actions.Add("unlock");
            }

            if (FindCapability(device, new[] { "devices.capabilities.mode", "mode", "scene", "routine" }, new[] { "scene", "routine", "trigger", "activate", "execute" }) != null)
            {
                actions.Add("trigger");
            }

            if (!string.IsNullOrWhiteSpace(device.PreviewVideoUrl) || !string.IsNullOrWhiteSpace(device.PreviewImageUrl))
            {
                actions.Add("openCameraView");
            }

            return actions;
        }

        private static string ResolveCompanionControlMode(SmartHomeDevice device)
        {
            if (string.Equals(device.Availability, "unavailable", StringComparison.OrdinalIgnoreCase))
            {
                return "unavailable";
            }

            if (string.Equals(device.SupportStatus, "unsupported", StringComparison.OrdinalIgnoreCase))
            {
                return "unsupported";
            }

            return device.Capabilities.Count > 0 ? "controllable" : "readOnly";
        }

        private static string ResolveCompanionPowerState(SmartHomeDevice device)
        {
            var lockCapability = FindCapability(
                device,
                typeAliases: new[] { "devices.capabilities.toggle", "toggle", "lock" },
                instanceAliases: new[] { "lock", "lockstate", "doorlock", "locked" });
            if (lockCapability != null)
            {
                var locked = ReadBooleanValue(lockCapability.StateValue);
                if (locked != null)
                {
                    return locked.Value ? "off" : "on";
                }
            }

            var powerCapability = FindCapability(
                device,
                typeAliases: new[] { "devices.capabilities.on_off", "on_off", "onoff" },
                instanceAliases: new[] { "powerswitch", "power", "switch", "mainpower" });
            if (powerCapability != null)
            {
                var poweredOn = ReadBooleanValue(powerCapability.StateValue);
                if (poweredOn != null)
                {
                    return poweredOn.Value ? "on" : "off";
                }
            }

            return string.Equals(device.Availability, "unavailable", StringComparison.OrdinalIgnoreCase)
                ? "unavailable"
                : string.Equals(device.SupportStatus, "unsupported", StringComparison.OrdinalIgnoreCase)
                    ? "unsupported"
                    : "unknown";
        }

        private static string ClassifyCompanionCategory(SmartHomeDevice device)
        {
            var normalizedType = NormalizeToken(device.DeviceType);
            var normalizedSku = NormalizeToken(device.Sku);

            if (!string.IsNullOrWhiteSpace(device.PreviewVideoUrl) || !string.IsNullOrWhiteSpace(device.PreviewImageUrl))
            {
                return normalizedType.Contains("doorbell", StringComparison.Ordinal) || normalizedSku.Contains("doorbell", StringComparison.Ordinal)
                    ? "doorbell"
                    : "camera";
            }

            if (FindCapability(device, new[] { "devices.capabilities.toggle", "toggle", "lock" }, new[] { "lock", "lockstate", "doorlock", "locked" }) != null)
            {
                return "lock";
            }

            if (FindCapability(device, new[] { "devices.capabilities.range", "range" }, new[] { "targettemperature", "targettemp", "setpoint", "thermostatsetpoint", "thermostattarget" }) != null)
            {
                return "thermostat";
            }

            if (FindCapability(device, new[] { "devices.capabilities.mode", "mode", "scene", "routine" }, new[] { "scene", "routine", "trigger", "activate", "execute" }) != null)
            {
                return "scene";
            }

            if (normalizedType.Contains("light", StringComparison.Ordinal) || normalizedSku.Contains("light", StringComparison.Ordinal))
            {
                return "light";
            }

            if (normalizedType.Contains("tv", StringComparison.Ordinal) || normalizedSku.Contains("tv", StringComparison.Ordinal))
            {
                return "tv";
            }

            return string.IsNullOrWhiteSpace(normalizedType) ? "device" : normalizedType;
        }

        private static (SmartHomeProviderState Provider, SmartHomeDevice Device)? FindSnapshotDevice(
            SmartHomeSnapshot snapshot,
            string? providerId,
            string deviceId)
        {
            if (!string.IsNullOrWhiteSpace(providerId))
            {
                foreach (var provider in snapshot.Providers)
                {
                    if (!string.Equals(provider.ProviderId, providerId, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var device = provider.Devices.FirstOrDefault(candidate => string.Equals(candidate.DeviceId, deviceId, StringComparison.OrdinalIgnoreCase));
                    if (device != null)
                    {
                        return (provider, device);
                    }

                    return null;
                }

                return null;
            }

            (SmartHomeProviderState Provider, SmartHomeDevice Device)? match = null;
            foreach (var provider in snapshot.Providers)
            {
                var device = provider.Devices.FirstOrDefault(candidate => string.Equals(candidate.DeviceId, deviceId, StringComparison.OrdinalIgnoreCase));
                if (device == null)
                {
                    continue;
                }

                if (match != null)
                {
                    return null;
                }

                match = (provider, device);
            }

            return match;
        }

        private static SmartHomeCapability? FindCapability(
            SmartHomeDevice device,
            IEnumerable<string> typeAliases,
            IEnumerable<string> instanceAliases)
        {
            var normalizedTypes = new HashSet<string>(typeAliases.Select(NormalizeToken), StringComparer.OrdinalIgnoreCase);
            var normalizedInstances = new HashSet<string>(instanceAliases.Select(NormalizeToken), StringComparer.OrdinalIgnoreCase);

            return device.Capabilities.FirstOrDefault(capability =>
            {
                var capabilityInstance = NormalizeToken(capability.Instance);
                var capabilityType = NormalizeToken(capability.Type);

                if (normalizedInstances.Count > 0)
                {
                    return normalizedInstances.Contains(capabilityInstance);
                }

                if (normalizedTypes.Count > 0)
                {
                    return normalizedTypes.Contains(capabilityType);
                }

                return false;
            });
        }

        private static string NormalizeToken(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var builder = new StringBuilder(value.Length);
            foreach (var character in value)
            {
                if (char.IsLetterOrDigit(character))
                {
                    builder.Append(char.ToLowerInvariant(character));
                }
            }

            return builder.ToString();
        }

        private static bool? TogglePowerValue(JsonElement stateValue)
        {
            var current = ReadBooleanValue(stateValue);
            return current == null ? null : !current.Value;
        }

        private static JsonElement ResolveDirectActionValue(CompanionSmartHomeActionRequest request)
        {
            if (request.Value.ValueKind != JsonValueKind.Undefined)
            {
                return request.Value;
            }

            if (request.IsOn != null)
            {
                return JsonSerializer.SerializeToElement(request.IsOn.Value);
            }

            if (request.Locked != null)
            {
                return JsonSerializer.SerializeToElement(request.Locked.Value);
            }

            if (request.Trigger != null)
            {
                return JsonSerializer.SerializeToElement(request.Trigger.Value);
            }

            if (request.Brightness != null)
            {
                return JsonSerializer.SerializeToElement(request.Brightness.Value);
            }

            if (request.Level != null)
            {
                return JsonSerializer.SerializeToElement(request.Level.Value);
            }

            if (request.TargetTemperature != null)
            {
                return JsonSerializer.SerializeToElement(request.TargetTemperature.Value);
            }

            return default;
        }

        private static bool? ReadBooleanValue(JsonElement value)
        {
            return value.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.String when bool.TryParse(value.GetString(), out var parsed) => parsed,
                JsonValueKind.Number when value.TryGetDouble(out var numberValue) => Math.Abs(numberValue) > double.Epsilon,
                _ => null,
            };
        }

        private static double? ReadNumericValue(JsonElement value)
        {
            return value.ValueKind switch
            {
                JsonValueKind.Number when value.TryGetDouble(out var numberValue) => numberValue,
                JsonValueKind.String when double.TryParse(value.GetString(), out var parsed) => parsed,
                _ => null,
            };
        }

        private static string? ReadStringValue(JsonElement value)
        {
            return value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => value.ToString(),
                _ => null,
            };
        }

        private static double? ResolveCapabilityNumber(SmartHomeDevice device, IEnumerable<string> instanceAliases)
        {
            var capability = FindCapability(device, Array.Empty<string>(), instanceAliases);
            return capability == null ? null : ReadNumericValue(capability.StateValue);
        }

        private static string? ResolveCapabilityUnit(SmartHomeDevice device, IEnumerable<string> instanceAliases)
        {
            var capability = FindCapability(device, Array.Empty<string>(), instanceAliases);
            return capability?.Unit;
        }

        private static string? FirstNonEmpty(params string?[] values)
        {
            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }

            return null;
        }

        private static bool TryMatchSmartHomeDeviceToggle(string path, out string deviceId)
        {
            const string prefix = "/api/smart-home/devices/";
            const string suffix = "/toggle";

            deviceId = string.Empty;
            if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
                !path.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) ||
                path.Length <= prefix.Length + suffix.Length)
            {
                return false;
            }

            deviceId = Uri.UnescapeDataString(path.Substring(prefix.Length, path.Length - prefix.Length - suffix.Length)).Trim();
            return !string.IsNullOrWhiteSpace(deviceId);
        }

        private static bool TryMatchSecurityCameraReconnect(string path, out string cameraId)
        {
            const string prefix = "/api/security/cameras/";
            const string suffix = "/reconnect";

            cameraId = string.Empty;
            if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
                !path.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) ||
                path.Length <= prefix.Length + suffix.Length)
            {
                return false;
            }

            cameraId = Uri.UnescapeDataString(path.Substring(prefix.Length, path.Length - prefix.Length - suffix.Length)).Trim();
            return !string.IsNullOrWhiteSpace(cameraId);
        }

        private static bool TryMatchSecurityCameraRecordingStart(string path, out string cameraId)
        {
            const string prefix = "/api/security/cameras/";
            const string suffix = "/recording/start";

            cameraId = string.Empty;
            if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
                !path.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) ||
                path.Length <= prefix.Length + suffix.Length)
            {
                return false;
            }

            cameraId = Uri.UnescapeDataString(path.Substring(prefix.Length, path.Length - prefix.Length - suffix.Length)).Trim();
            return !string.IsNullOrWhiteSpace(cameraId);
        }

        private static bool TryMatchSecurityCameraRecordingStop(string path, out string cameraId)
        {
            const string prefix = "/api/security/cameras/";
            const string suffix = "/recording/stop";

            cameraId = string.Empty;
            if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
                !path.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) ||
                path.Length <= prefix.Length + suffix.Length)
            {
                return false;
            }

            cameraId = Uri.UnescapeDataString(path.Substring(prefix.Length, path.Length - prefix.Length - suffix.Length)).Trim();
            return !string.IsNullOrWhiteSpace(cameraId);
        }

        private static bool TryMatchSmartHomeSceneActivate(string path, out string sceneId)
        {
            const string prefix = "/api/smart-home/scenes/";
            const string suffix = "/activate";

            sceneId = string.Empty;
            if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
                !path.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) ||
                path.Length <= prefix.Length + suffix.Length)
            {
                return false;
            }

            sceneId = Uri.UnescapeDataString(path.Substring(prefix.Length, path.Length - prefix.Length - suffix.Length)).Trim();
            return !string.IsNullOrWhiteSpace(sceneId);
        }

        private static bool TryMatchAutomationRoutineExecute(string path, out string routineId)
        {
            const string prefix = "/api/automation/routines/";
            const string suffix = "/execute";

            routineId = string.Empty;
            if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
                !path.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) ||
                path.Length <= prefix.Length + suffix.Length)
            {
                return false;
            }

            routineId = Uri.UnescapeDataString(path.Substring(prefix.Length, path.Length - prefix.Length - suffix.Length)).Trim();
            return !string.IsNullOrWhiteSpace(routineId);
        }

        private IEnumerable<CompanionSecurityAlert> LoadSecurityAlerts()
        {
            if (!File.Exists(_securityActivityPath))
            {
                return Array.Empty<CompanionSecurityAlert>();
            }

            var json = File.ReadAllText(_securityActivityPath);
            var activity = JsonSerializer.Deserialize<List<RecentActivity>>(json) ?? new List<RecentActivity>();
            return activity.Select(entry => new CompanionSecurityAlert(
                entry.Id,
                entry.Timestamp,
                ClassifySecuritySeverity(entry),
                BuildSecurityMessage(entry)));
        }

        private static string BuildSecurityMessage(RecentActivity entry)
        {
            if (string.IsNullOrWhiteSpace(entry.Description))
            {
                return entry.Title;
            }

            return string.IsNullOrWhiteSpace(entry.Title)
                ? entry.Description
                : $"{entry.Title}: {entry.Description}";
        }

        private static string ClassifySecuritySeverity(RecentActivity entry)
        {
            var haystack = $"{entry.Title} {entry.Description} {entry.ActionTaken}";
            var normalized = haystack.ToLowerInvariant();

            if (normalized.Contains("critical") || normalized.Contains("severe"))
            {
                return "critical";
            }

            if (normalized.Contains("threat") || normalized.Contains("malware") || normalized.Contains("quarantine") || normalized.Contains("error") || normalized.Contains("failed"))
            {
                return "high";
            }

            if (normalized.Contains("warning") || normalized.Contains("scan") || normalized.Contains("update") || normalized.Contains("watcher"))
            {
                return "medium";
            }

            return "low";
        }

        private static bool IsSecurityCameraDevice(SmartHomeDevice device)
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

            var normalizedName = NormalizeToken(device.Name);
            var normalizedId = NormalizeToken(device.DeviceId);
            var normalizedType = NormalizeToken(device.DeviceType);
            var normalizedSku = NormalizeToken(device.Sku);
            var normalizedExternalUrl = NormalizeToken(device.ExternalUrl);

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
            var normalizedType = NormalizeToken(device.DeviceType);
            var normalizedSku = NormalizeToken(device.Sku);

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
            var normalizedInstance = NormalizeToken(capability.Instance);
            var normalizedCapabilityType = NormalizeToken(capability.Type);
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

        private object BuildCompanionSecurityCamera(SmartHomeProviderState provider, SmartHomeDevice device)
        {
            var thumbnailUrl = FirstNonEmpty(device.PreviewImageUrl, device.PreviewVideoUrl) ?? string.Empty;
            var streamUrl = FirstNonEmpty(device.PreviewVideoUrl, device.PreviewImageUrl) ?? string.Empty;
            var snapshotUrl = FirstNonEmpty(device.PreviewImageUrl, device.PreviewVideoUrl) ?? string.Empty;
            var externalUrl = device.ExternalUrl ?? string.Empty;
            var normalizedName = NormalizeToken(device.Name);
            var normalizedType = NormalizeToken(device.DeviceType);
            var normalizedSku = NormalizeToken(device.Sku);
            var recordingUrl = GetSecurityCameraRecordingUrl(device);
            var recordingSupported = !string.IsNullOrWhiteSpace(recordingUrl);
            var isRecordingThisCamera = _cameraRecordingService.IsRecordingSession(device.DeviceId);
            var canRecord = recordingSupported;
            var recordingPath = isRecordingThisCamera ? _cameraRecordingService.GetRecordingPath(device.DeviceId) : string.Empty;
            var recordingStatus = isRecordingThisCamera
                ? (string.IsNullOrWhiteSpace(recordingPath)
                    ? "Camera recording in progress."
                    : $"Recording now: {recordingPath}")
                : canRecord
                    ? "Ready to record from Atlas Companion."
                    : "Atlas can only record feeds that expose a direct stream URL.";

            return new
            {
                id = device.DeviceId,
                name = string.IsNullOrWhiteSpace(device.Name) ? device.DeviceId : device.Name,
                provider = provider.ProviderId,
                providerId = provider.ProviderId,
                streamUrl,
                snapshotUrl,
                thumbnailUrl,
                externalUrl,
                isOnline = device.IsOnline,
                hasMotion = normalizedType.Contains("motion", StringComparison.Ordinal) || normalizedSku.Contains("motion", StringComparison.Ordinal),
                supportsAudio = normalizedType.Contains("doorbell", StringComparison.Ordinal) || normalizedSku.Contains("doorbell", StringComparison.Ordinal) || normalizedName.Contains("doorbell", StringComparison.Ordinal),
                supportsPtz = normalizedType.Contains("ptz", StringComparison.Ordinal) || normalizedSku.Contains("ptz", StringComparison.Ordinal),
                recordingUrl,
                recordingSupported,
                canRecord,
                isRecording = isRecordingThisCamera,
                recordingPath,
                recordingStatus,
            };
        }

        private static string GetSecurityCameraRecordingUrl(SmartHomeDevice device)
        {
            var candidates = new[]
            {
                device.PreviewVideoUrl,
                device.DeviceId,
                device.PreviewImageUrl,
                device.ExternalUrl,
            };

            foreach (var candidate in candidates)
            {
                var normalized = (candidate ?? string.Empty).Trim();
                if (SmartHomeCameraRecordingService.IsSupportedSourceUrl(normalized))
                {
                    return normalized;
                }
            }

            return string.Empty;
        }

        private static bool LooksLikeLightingScene(WorkflowChainDefinition definition)
        {
            var value = $"{definition.Id} {definition.Title} {definition.Description} {definition.Category}".ToLowerInvariant();
            return value.Contains("scene") ||
                value.Contains("mode") ||
                value.Contains("movie") ||
                value.Contains("sleep") ||
                value.Contains("wake") ||
                value.Contains("party") ||
                value.Contains("chill") ||
                value.Contains("neon") ||
                value.Contains("storm") ||
                value.Contains("ambience") ||
                value.Contains("lighting") ||
                value.Contains("lights") ||
                value.Contains("all off");
        }

        private static object BuildCompanionLightingScene(
            WorkflowChainDefinition definition,
            string? activeDefinitionId,
            bool isWorkflowActive)
        {
            var roomTargets = InferSceneRoomTargets(definition).ToArray();
            return new
            {
                id = definition.Id,
                name = string.IsNullOrWhiteSpace(definition.Title) ? definition.Id : definition.Title,
                icon = MapWorkflowIconData(definition),
                roomTargets,
                brightness = InferSceneBrightness(definition),
                colorHex = InferSceneColor(definition),
                effect = InferSceneEffect(definition),
                isActive = isWorkflowActive && string.Equals(activeDefinitionId, definition.Id, StringComparison.OrdinalIgnoreCase),
                description = string.IsNullOrWhiteSpace(definition.Description) ? null : definition.Description,
            };
        }

        private static IEnumerable<string> InferSceneRoomTargets(WorkflowChainDefinition definition)
        {
            var haystack = $"{definition.Title} {definition.Description}".ToLowerInvariant();
            if (haystack.Contains("bedroom"))
            {
                yield return "Bedroom";
            }

            if (haystack.Contains("living"))
            {
                yield return "Living Room";
            }

            if (haystack.Contains("kitchen"))
            {
                yield return "Kitchen";
            }

            if (haystack.Contains("office") || haystack.Contains("desk"))
            {
                yield return "Office";
            }
        }

        private static int? InferSceneBrightness(WorkflowChainDefinition definition)
        {
            var haystack = $"{definition.Title} {definition.Description}".ToLowerInvariant();
            if (haystack.Contains("sleep") || haystack.Contains("all off"))
            {
                return 0;
            }

            if (haystack.Contains("movie"))
            {
                return 18;
            }

            if (haystack.Contains("night"))
            {
                return 24;
            }

            if (haystack.Contains("focus") || haystack.Contains("work"))
            {
                return 82;
            }

            if (haystack.Contains("party") || haystack.Contains("bright"))
            {
                return 100;
            }

            return null;
        }

        private static string? InferSceneColor(WorkflowChainDefinition definition)
        {
            var haystack = $"{definition.Title} {definition.Description}".ToLowerInvariant();
            if (haystack.Contains("sunset") || haystack.Contains("warm"))
            {
                return "#FF9E57";
            }

            if (haystack.Contains("cool") || haystack.Contains("focus"))
            {
                return "#8FD8FF";
            }

            if (haystack.Contains("movie") || haystack.Contains("night"))
            {
                return "#5F7CFF";
            }

            if (haystack.Contains("party") || haystack.Contains("neon"))
            {
                return "#FF5BD6";
            }

            return null;
        }

        private static string? InferSceneEffect(WorkflowChainDefinition definition)
        {
            var haystack = $"{definition.Title} {definition.Description}".ToLowerInvariant();
            if (haystack.Contains("party") || haystack.Contains("pulse"))
            {
                return "pulse";
            }

            if (haystack.Contains("storm"))
            {
                return "storm";
            }

            if (haystack.Contains("focus"))
            {
                return "focus";
            }

            return null;
        }

        private static string MapWorkflowIconData(WorkflowChainDefinition definition)
        {
            var haystack = $"{definition.Id} {definition.Title} {definition.Description} {definition.Category}".ToLowerInvariant();
            if (haystack.Contains("security"))
            {
                return "security";
            }

            if (haystack.Contains("internet") || haystack.Contains("network") || haystack.Contains("wifi"))
            {
                return "wifi";
            }

            if (haystack.Contains("disk") || haystack.Contains("storage"))
            {
                return "storage";
            }

            if (haystack.Contains("game") || haystack.Contains("gaming"))
            {
                return "sports_esports";
            }

            if (haystack.Contains("health") || haystack.Contains("review") || haystack.Contains("diagnostic"))
            {
                return "health_and_safety";
            }

            return "build";
        }

        private static object BuildMediaStateResponse()
        {
            return BuildMediaSnapshotOnUiThread(BuildMediaStateResponseCore);
        }

        private static object BuildMediaStateResponseCore()
        {
            var mediaViewModel = MediaCentreViewModel.Instance;
            var currentMedia = MediaPlaybackService.Instance?.CurrentMedia;
            var title = string.IsNullOrWhiteSpace(mediaViewModel?.NowPlayingTitle)
                ? currentMedia?.Title ?? currentMedia?.DisplayName ?? "Unknown Track"
                : mediaViewModel.NowPlayingTitle;
            var artist = string.IsNullOrWhiteSpace(mediaViewModel?.NowPlayingArtist)
                ? currentMedia?.Artist ?? string.Empty
                : mediaViewModel.NowPlayingArtist;
            var album = string.IsNullOrWhiteSpace(mediaViewModel?.NowPlayingAlbum)
                ? currentMedia?.Album ?? string.Empty
                : mediaViewModel.NowPlayingAlbum;
            var duration = mediaViewModel?.TotalSeconds ?? currentMedia?.Duration?.TotalSeconds ?? 0d;
            var position = mediaViewModel?.ProgressSeconds ?? 0d;
            var mediaType = ResolveMediaTypeId(mediaViewModel, currentMedia);
            var artworkUrl = ResolveMediaArtworkUrl(currentMedia);
            var backdropUrl = ResolveMediaBackdropUrl(currentMedia);
            var advancedControls = BuildAdvancedMediaControls();
            var isMuted = ResolveMediaMuteState();
            var isFullscreen = ResolveMediaFullscreenState();
            var remainingSeconds = duration <= 0d ? 0d : Math.Max(0d, duration - position);
            var playbackState = ResolvePlaybackState(currentMedia, mediaViewModel?.IsPlaying == true, position, duration);

            return new
            {
                title,
                artist,
                album,
                albumArtUrl = artworkUrl,
                artworkUrl,
                backdropUrl,
                mediaType,
                sectionName = currentMedia?.SectionName ?? string.Empty,
                sourcePath = currentMedia?.FilePath ?? string.Empty,
                fileName = string.IsNullOrWhiteSpace(currentMedia?.FilePath) ? string.Empty : Path.GetFileName(currentMedia!.FilePath),
                displayName = currentMedia?.DisplayName ?? title,
                isPlaying = mediaViewModel?.IsPlaying ?? false,
                playbackState,
                isMuted,
                isFullscreen,
                volume = (double)(mediaViewModel?.Volume ?? 50),
                position,
                duration,
                remainingSeconds,
                progress = duration <= 0 ? 0d : Math.Clamp(position / duration, 0d, 1d),
                queueCount = MediaPlaybackService.Instance?.Queue.Count ?? 0,
                controls = new
                {
                    canSeek = duration > 0.5d,
                    canStop = true,
                    canSkip = true,
                    canAdjustVolume = true,
                    canMute = true,
                    canToggleFullscreen = true,
                },
                advanced = advancedControls,
                lastUpdatedUtc = DateTime.UtcNow,
            };
        }

        private static string ResolvePlaybackState(AtlasAI.MediaScanner.MediaItem? currentMedia, bool isPlaying, double position, double duration)
        {
            if (isPlaying)
            {
                return "playing";
            }

            var hasMedia = currentMedia != null || duration > 0.5d || position > 0.5d;
            return hasMedia ? "paused" : "stopped";
        }

        private sealed record AdvancedMediaControlsPayload(
            double playbackSpeed,
            IReadOnlyList<double> availablePlaybackSpeeds,
            bool canAdjustPlaybackSpeed,
            bool subtitlesEnabled,
            bool canControlSubtitles,
            string subtitleMode,
            int? currentSubtitleTrackId,
            IReadOnlyList<object> availableSubtitleTracks,
            bool canSelectAudioTrack,
            int? currentAudioTrackId,
            IReadOnlyList<object> availableAudioTracks,
            bool canShuffle,
            bool shuffleEnabled,
            bool canRepeat,
            string repeatMode);

        private static AdvancedMediaControlsPayload BuildAdvancedMediaControls()
        {
            var playbackSpeeds = new[] { 0.5d, 0.75d, 1.0d, 1.25d, 1.5d, 2.0d };

            try
            {
                if (Application.Current == null)
                {
                    return new AdvancedMediaControlsPayload(
                        playbackSpeed: 1.0d,
                        availablePlaybackSpeeds: playbackSpeeds,
                        canAdjustPlaybackSpeed: false,
                        subtitlesEnabled: false,
                        canControlSubtitles: false,
                        subtitleMode: "none",
                        currentSubtitleTrackId: null,
                        availableSubtitleTracks: Array.Empty<object>(),
                        canSelectAudioTrack: false,
                        currentAudioTrackId: null,
                        availableAudioTracks: Array.Empty<object>(),
                        canShuffle: false,
                        shuffleEnabled: false,
                        canRepeat: false,
                        repeatMode: "off");
                }

                var mediaViewModel = MediaCentreViewModel.Instance;
                var mediaPlayer = EnumerateVisualChildren<MediaPlayerControl>(Application.Current).FirstOrDefault();
                if (mediaPlayer != null)
                {
                    var subtitleTracks = mediaPlayer.GetSubtitleTrackOptions()
                        .Select(track => new { id = track.Id, name = track.Name })
                        .ToList();
                    var audioTracks = mediaPlayer.GetAudioTrackOptions()
                        .Select(track => new { id = track.Id, name = track.Name })
                        .ToList();

                    return new AdvancedMediaControlsPayload(
                        playbackSpeed: mediaPlayer.GetPlaybackSpeed(),
                        availablePlaybackSpeeds: playbackSpeeds,
                        canAdjustPlaybackSpeed: true,
                        subtitlesEnabled: mediaPlayer.AreSubtitlesEnabled(),
                        canControlSubtitles: subtitleTracks.Count > 0,
                        subtitleMode: subtitleTracks.Count > 0 ? "tracks" : "none",
                        currentSubtitleTrackId: mediaPlayer.GetCurrentSubtitleTrackId(),
                        availableSubtitleTracks: subtitleTracks.Cast<object>().ToList(),
                        canSelectAudioTrack: audioTracks.Count > 0,
                        currentAudioTrackId: mediaPlayer.GetCurrentAudioTrackId(),
                        availableAudioTracks: audioTracks.Cast<object>().ToList(),
                        canShuffle: true,
                        shuffleEnabled: mediaViewModel?.ShuffleEnabled ?? false,
                        canRepeat: true,
                        repeatMode: (mediaViewModel?.RepeatEnabled ?? false) ? "all" : "off");
                }

                var simplePlayer = EnumerateVisualChildren<SimpleMediaPlayerControl>(Application.Current).FirstOrDefault();
                if (simplePlayer != null)
                {
                    return new AdvancedMediaControlsPayload(
                        playbackSpeed: simplePlayer.GetPlaybackSpeed(),
                        availablePlaybackSpeeds: playbackSpeeds,
                        canAdjustPlaybackSpeed: true,
                        subtitlesEnabled: simplePlayer.AreSubtitlesEnabled(),
                        canControlSubtitles: simplePlayer.HasLoadedSubtitles(),
                        subtitleMode: simplePlayer.HasLoadedSubtitles() ? "toggle" : "none",
                        currentSubtitleTrackId: null,
                        availableSubtitleTracks: Array.Empty<object>(),
                        canSelectAudioTrack: false,
                        currentAudioTrackId: null,
                        availableAudioTracks: Array.Empty<object>(),
                        canShuffle: true,
                        shuffleEnabled: mediaViewModel?.ShuffleEnabled ?? false,
                        canRepeat: true,
                        repeatMode: (mediaViewModel?.RepeatEnabled ?? false) ? "all" : "off");
                }
            }
            catch
            {
            }

            return new AdvancedMediaControlsPayload(
                playbackSpeed: 1.0d,
                availablePlaybackSpeeds: playbackSpeeds,
                canAdjustPlaybackSpeed: false,
                subtitlesEnabled: false,
                canControlSubtitles: false,
                subtitleMode: "none",
                currentSubtitleTrackId: null,
                availableSubtitleTracks: Array.Empty<object>(),
                canSelectAudioTrack: false,
                currentAudioTrackId: null,
                availableAudioTracks: Array.Empty<object>(),
                canShuffle: false,
                shuffleEnabled: false,
                canRepeat: false,
                repeatMode: "off");
        }

        private static string ResolveMediaTypeId(MediaCentreViewModel? mediaViewModel, AtlasAI.MediaScanner.MediaItem? currentMedia)
        {
            if (!string.IsNullOrWhiteSpace(mediaViewModel?.NowPlayingTypeId))
            {
                return mediaViewModel.NowPlayingTypeId;
            }

            if (!string.IsNullOrWhiteSpace(currentMedia?.Type))
            {
                return currentMedia.Type!;
            }

            return currentMedia?.MediaType switch
            {
                MediaType.Audio => "music",
                MediaType.Video => "video",
                MediaType.Image => "image",
                _ => "media",
            };
        }

        private static bool ResolveMediaMuteState()
        {
            try
            {
                if (Application.Current == null)
                {
                    return false;
                }

                var mediaPlayer = EnumerateVisualChildren<MediaPlayerControl>(Application.Current).FirstOrDefault();
                if (mediaPlayer != null)
                {
                    return mediaPlayer.IsMuted;
                }

                var simplePlayer = EnumerateVisualChildren<SimpleMediaPlayerControl>(Application.Current).FirstOrDefault();
                if (simplePlayer != null)
                {
                    return simplePlayer.IsMuted;
                }
            }
            catch
            {
            }

            return false;
        }

        private static bool ResolveMediaFullscreenState()
        {
            try
            {
                if (Application.Current == null)
                {
                    return false;
                }

                var mediaPlayer = EnumerateVisualChildren<MediaPlayerControl>(Application.Current).FirstOrDefault();
                if (mediaPlayer != null)
                {
                    return mediaPlayer.IsFullscreen;
                }

                var simplePlayer = EnumerateVisualChildren<SimpleMediaPlayerControl>(Application.Current).FirstOrDefault();
                if (simplePlayer != null)
                {
                    return simplePlayer.IsFullscreenActive;
                }
            }
            catch
            {
            }

            return false;
        }

        private static object BuildMediaQueueResponse()
        {
            return BuildMediaSnapshotOnUiThread(BuildMediaQueueResponseCore);
        }

        private static object BuildMediaQueueResponseCore()
        {
            var playback = MediaPlaybackService.GetOrCreate();
            var current = playback.CurrentMedia;
            var queue = playback.Queue;
            var currentIndex = ResolveCurrentQueueIndex(queue, current);

            return new
            {
                currentIndex,
                count = queue.Count,
                items = queue.Select((item, index) => SerializeQueueItem(item, index, index == currentIndex)).ToList(),
                lastUpdatedUtc = DateTime.UtcNow,
            };
        }

        private static object BuildMediaHistoryResponse()
        {
            return BuildMediaSnapshotOnUiThread(BuildMediaHistoryResponseCore);
        }

        private static object BuildMediaHistoryResponseCore()
        {
            var recentEntries = AtlasAI.Core.WatchHistoryStore.GetRecent(24);
            var resolvableEntries = recentEntries
                .Where(item => FindLibraryItem(item.filePath, item.title, item.type) != null)
                .ToList();

            var recent = resolvableEntries
                .Select(SerializeHistoryItem)
                .ToList();

            var continueWatching = resolvableEntries
                .Where(item => item.durationSeconds > 60d)
                .Where(item => item.positionSeconds > 30d)
                .Where(item => item.durationSeconds <= 0d || (item.positionSeconds / item.durationSeconds) > 0.03d)
                .Where(item => item.durationSeconds <= 0d || (item.positionSeconds / item.durationSeconds) < 0.97d)
                .Select(SerializeHistoryItem)
                .Take(12)
                .ToList();

            return new
            {
                continueWatching,
                recentlyPlayed = recent.Take(12).ToList(),
                lastUpdatedUtc = DateTime.UtcNow,
            };
        }

        private static object BuildMediaHomeResponse()
        {
            return BuildMediaSnapshotOnUiThread(BuildMediaHomeResponseCore);
        }

        private static object BuildMediaHomeResponseCore()
        {
            var continueWatching = AtlasAI.Core.WatchHistoryStore.GetRecent(24)
                .Where(item => FindLibraryItem(item.filePath, item.title, item.type) != null)
                .Where(item => item.durationSeconds > 60d)
                .Where(item => item.positionSeconds > 30d)
                .Where(item => item.durationSeconds <= 0d || (item.positionSeconds / item.durationSeconds) > 0.03d)
                .Where(item => item.durationSeconds <= 0d || (item.positionSeconds / item.durationSeconds) < 0.97d)
                .Select(SerializeHistoryItem)
                .Take(12)
                .Cast<object>()
                .ToList();

            var latestMovies = GetLibraryItemsByRecency("movies", 18)
                .Select(SerializeLibraryItem)
                .Cast<object>()
                .ToList();

            var latestSeries = GetLibraryItemsByRecency("tv", 18)
                .Select(SerializeLibraryItem)
                .Cast<object>()
                .ToList();

            var popular = GetPopularItems(18)
                .Select(SerializeLibraryItem)
                .Cast<object>()
                .ToList();

            var recentlyAdded = GetRecentlyAddedItems(18)
                .Select(SerializeLibraryItem)
                .Cast<object>()
                .ToList();

            var musicAlbums = SnapshotMusicAlbums(null, 18)
                .Select(SerializeMusicAlbumItem)
                .Cast<object>()
                .ToList();

            var musicTracks = SnapshotMusicTracks(null, 18)
                .Select(SerializeLibraryItem)
                .Cast<object>()
                .ToList();

            var apps = GetAppsItems(null, 18)
                .Select(SerializeAppItem)
                .Cast<object>()
                .ToList();

            return new
            {
                sections = new[]
                {
                    new { id = "continue_watching", title = "Continue Watching", items = continueWatching },
                    new { id = "latest_movies", title = "Latest Movies", items = latestMovies },
                    new { id = "latest_series", title = "Latest Series", items = latestSeries },
                    new { id = "popular", title = "Popular", items = popular },
                    new { id = "recently_added", title = "Recently Added", items = recentlyAdded },
                    new { id = "music_albums", title = "Music Albums", items = musicAlbums },
                    new { id = "music_tracks", title = "Music Tracks", items = musicTracks },
                    new { id = "apps", title = "Apps", items = apps },
                },
                lastUpdatedUtc = DateTime.UtcNow,
            };
        }

        private static object BuildLiveSyncMediaPayload()
        {
            return new
            {
                state = BuildMediaStateResponse(),
                queue = BuildMediaQueueResponse(),
            };
        }

        private static object BuildLiveSyncDownloaderPayload()
        {
            return BuildDownloaderStateResponse();
        }

        private static string BuildLiveSyncMediaSignature()
        {
            var payload = BuildLiveSyncMediaPayload();
            var root = JsonNode.Parse(JsonSerializer.Serialize(payload)) as JsonObject;
            if (root == null)
            {
                return string.Empty;
            }

            RemoveRecursiveProperty(root, "lastUpdatedUtc");
            return root.ToJsonString();
        }

        private static string BuildLiveSyncDownloaderSignature()
        {
            var payload = BuildLiveSyncDownloaderPayload();
            var root = JsonNode.Parse(JsonSerializer.Serialize(payload)) as JsonObject;
            if (root == null)
            {
                return string.Empty;
            }

            RemoveRecursiveProperty(root, "lastUpdatedUtc");
            return root.ToJsonString();
        }

        private static async Task<object> BuildLiveSyncDjPayloadAsync()
        {
            if (Application.Current == null)
            {
                return new
                {
                    routeAvailable = false,
                    message = "Atlas UI is not available.",
                };
            }

            return await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                var djView = GetActiveDjConsoleView();
                if (djView == null)
                {
                    return (object)new
                    {
                        routeAvailable = false,
                        message = "DJ booth is not active on the Atlas host.",
                    };
                }

                return (object)new
                {
                    routeAvailable = true,
                    state = djView.GetCompanionState(),
                };
            }).Task.ConfigureAwait(false);
        }

        private static async Task<string> BuildLiveSyncDjSignatureAsync()
        {
            var payload = await BuildLiveSyncDjPayloadAsync().ConfigureAwait(false);
            var root = JsonNode.Parse(JsonSerializer.Serialize(payload)) as JsonObject;
            if (root == null)
            {
                return string.Empty;
            }

            RemoveRecursiveProperty(root, "lastUpdatedUtc");
            return root.ToJsonString();
        }

        private static void RemoveRecursiveProperty(JsonNode? node, string propertyName)
        {
            switch (node)
            {
                case JsonObject obj:
                    obj.Remove(propertyName);
                    foreach (var child in obj.ToList())
                    {
                        RemoveRecursiveProperty(child.Value, propertyName);
                    }
                    break;
                case JsonArray array:
                    foreach (var child in array)
                    {
                        RemoveRecursiveProperty(child, propertyName);
                    }
                    break;
            }
        }

        private static object BuildMediaLibraryResponse(string query, string? typeId, int maxResults)
        {
            return BuildMediaSnapshotOnUiThread(() => BuildMediaLibraryResponseCore(query, typeId, maxResults));
        }

        private static object BuildMediaLibraryResponseCore(string query, string? typeId, int maxResults)
        {
            if (string.Equals(typeId, "apps", StringComparison.OrdinalIgnoreCase))
            {
                var appItems = GetAppsItems(query, maxResults)
                    .Select(SerializeAppItem)
                    .ToList();

                return new
                {
                    query,
                    type = "apps",
                    count = appItems.Count,
                    items = appItems,
                    lastUpdatedUtc = DateTime.UtcNow,
                };
            }

            if (string.Equals(typeId, "music", StringComparison.OrdinalIgnoreCase))
            {
                var musicItems = SnapshotMusicTracks(query, maxResults)
                    .Select(SerializeLibraryItem)
                    .ToList();

                return new
                {
                    query,
                    type = "music",
                    count = musicItems.Count,
                    items = musicItems,
                    lastUpdatedUtc = DateTime.UtcNow,
                };
            }

            var vm = MediaCentreViewModel.Instance;
            List<Views.ViewModels.MediaItem> items;
            if (string.IsNullOrWhiteSpace(query))
            {
                items = SnapshotLibraryItems(typeId)
                    .Take(Math.Max(1, maxResults))
                    .ToList();
            }
            else
            {
                items = SearchLibraryItems(query, typeId, maxResults);
            }

            return new
            {
                query,
                type = typeId ?? "all",
                count = items.Count,
                items = items.Select(SerializeLibraryItem).ToList(),
                lastUpdatedUtc = DateTime.UtcNow,
            };
        }

        private static T BuildMediaSnapshotOnUiThread<T>(Func<T> builder)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.CheckAccess())
            {
                return builder();
            }

            return dispatcher.Invoke(builder);
        }

        private static int ResolveCurrentQueueIndex(IReadOnlyList<AtlasAI.MediaScanner.MediaItem> queue, AtlasAI.MediaScanner.MediaItem? current)
        {
            if (current == null || queue.Count == 0)
            {
                return -1;
            }

            for (var index = 0; index < queue.Count; index++)
            {
                var item = queue[index];
                if (!string.IsNullOrWhiteSpace(item.FilePath) &&
                    string.Equals(item.FilePath, current.FilePath, StringComparison.OrdinalIgnoreCase))
                {
                    return index;
                }

                if (string.Equals(item.DisplayName, current.DisplayName, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(item.Title, current.Title, StringComparison.OrdinalIgnoreCase))
                {
                    return index;
                }
            }

            return -1;
        }

        private static object SerializeQueueItem(AtlasAI.MediaScanner.MediaItem item, int index, bool isCurrent)
        {
            var title = string.IsNullOrWhiteSpace(item.Title)
                ? item.DisplayName ?? "Unknown item"
                : item.Title;
            var artworkUrl = ResolveMediaArtworkUrl(item);
            var backdropUrl = ResolveMediaBackdropUrl(item);

            return new
            {
                index,
                title,
                artist = item.Artist ?? string.Empty,
                album = item.Album ?? string.Empty,
                albumArtUrl = artworkUrl,
                artworkUrl,
                backdropUrl,
                mediaType = ResolveMediaTypeId(null, item),
                sectionName = item.SectionName ?? string.Empty,
                sourcePath = item.FilePath ?? string.Empty,
                fileName = string.IsNullOrWhiteSpace(item.FilePath) ? string.Empty : Path.GetFileName(item.FilePath),
                displayName = item.DisplayName ?? title,
                duration = item.Duration?.TotalSeconds ?? 0d,
                isCurrent,
            };
        }

        private static object SerializeLibraryItem(Views.ViewModels.MediaItem item)
        {
            var title = string.IsNullOrWhiteSpace(item.Title)
                ? "Unknown item"
                : item.Title;
            var artworkUrl = ResolveLibraryArtworkUrl(item);
            var backdropUrl = ResolveLibraryBackdropUrl(item);
            var progress = item.ProgressPercent > 0 ? Math.Clamp(item.ProgressPercent, 0d, 1d) : 0d;
            var year = item.Year > 0
                ? item.Year
                : item.ReleaseDate?.Year;

            return new
            {
                title,
                artist = item.Artist ?? string.Empty,
                album = item.Album ?? string.Empty,
                albumArtUrl = artworkUrl,
                artworkUrl,
                backdropUrl,
                mediaType = item.Type ?? string.Empty,
                sectionName = item.Type ?? string.Empty,
                sourcePath = item.FilePath ?? string.Empty,
                fileName = string.IsNullOrWhiteSpace(item.FilePath) ? string.Empty : Path.GetFileName(item.FilePath),
                displayName = title,
                duration = item.Duration.TotalSeconds,
                isPlaying = item.IsPlaying,
                year,
                rating = item.Rating > 0 ? item.Rating : (double?)null,
                progress,
                isWatched = item.IsWatched,
                overview = item.Overview ?? string.Empty,
                runtimeMinutes = item.RuntimeMinutes > 0 ? item.RuntimeMinutes : (int?)null,
                releaseDate = item.ReleaseDate,
                sourceBadge = ResolveSourceBadge(item.Type, item.StreamSource),
                sourceApp = item.StreamSource ?? string.Empty,
            };
        }

        private static object SerializeMusicAlbumItem(Views.ViewModels.AlbumEntry album)
        {
            var firstTrack = album.Tracks.FirstOrDefault();
            var title = string.IsNullOrWhiteSpace(album.AlbumTitle)
                ? "Unknown album"
                : album.AlbumTitle;
            var artworkUrl = ResolveAlbumArtworkUrl(album) ?? ResolveLibraryArtworkUrl(firstTrack);
            var backdropUrl = ResolveLibraryBackdropUrl(firstTrack) ?? artworkUrl;
            var totalDuration = album.Tracks.Sum(track => track.Duration.TotalSeconds);
            var year = album.Tracks.Select(track => track.Year).FirstOrDefault(value => value > 0);
            var releaseDate = album.Tracks
                .Where(track => track.ReleaseDate.HasValue)
                .Select(track => track.ReleaseDate)
                .FirstOrDefault();

            return new
            {
                title,
                artist = album.Artist ?? string.Empty,
                album = title,
                albumArtUrl = artworkUrl,
                artworkUrl,
                backdropUrl,
                mediaType = "music",
                sectionName = "music",
                sourcePath = firstTrack?.FilePath ?? album.SourceFolderPath ?? string.Empty,
                fileName = string.IsNullOrWhiteSpace(firstTrack?.FilePath)
                    ? string.Empty
                    : Path.GetFileName(firstTrack!.FilePath),
                displayName = title,
                duration = totalDuration,
                isPlaying = album.Tracks.Any(track => track.IsPlaying),
                year = year > 0 ? year : releaseDate?.Year,
                rating = (double?)null,
                progress = 0d,
                isWatched = false,
                overview = album.TrackCount > 0 ? $"{album.TrackCount} tracks" : string.Empty,
                runtimeMinutes = totalDuration > 0 ? (int?)Math.Max(1, Math.Round(totalDuration / 60d)) : null,
                releaseDate,
                sourceBadge = "Album",
                sourceApp = "Atlas Music",
            };
        }

        private static object SerializeHistoryItem((string filePath, string title, string type, string? coverUrl, string? backdropUrl, DateTime lastWatchedUtc, double positionSeconds, double durationSeconds) item)
        {
            var libraryItem = FindLibraryItem(item.filePath, item.title, item.type);
            var resolvedSourcePath = string.IsNullOrWhiteSpace(libraryItem?.FilePath)
                ? item.filePath ?? string.Empty
                : libraryItem!.FilePath;
            var title = string.IsNullOrWhiteSpace(item.title)
                ? libraryItem?.Title ?? "Unknown item"
                : item.title;
            var artworkUrl = ResolveStoredHistoryImageUrl(item.coverUrl, item.filePath) ?? ResolveLibraryArtworkUrl(libraryItem);
            var backdropUrl = ResolveStoredHistoryImageUrl(item.backdropUrl, item.filePath) ?? ResolveLibraryBackdropUrl(libraryItem) ?? artworkUrl;
            var duration = Math.Max(0d, item.durationSeconds);
            var position = Math.Max(0d, Math.Min(item.positionSeconds, duration > 0 ? duration : item.positionSeconds));

            return new
            {
                title,
                displayName = string.IsNullOrWhiteSpace(libraryItem?.Title) ? title : libraryItem!.Title,
                artist = libraryItem?.Artist ?? string.Empty,
                album = libraryItem?.Album ?? string.Empty,
                albumArtUrl = artworkUrl,
                artworkUrl,
                backdropUrl,
                mediaType = string.IsNullOrWhiteSpace(item.type) ? libraryItem?.Type ?? string.Empty : item.type,
                sectionName = libraryItem?.Type ?? string.Empty,
                sourcePath = resolvedSourcePath,
                fileName = string.IsNullOrWhiteSpace(resolvedSourcePath) ? string.Empty : Path.GetFileName(resolvedSourcePath),
                position,
                duration,
                progress = duration <= 0 ? 0d : Math.Clamp(position / duration, 0d, 1d),
                lastWatchedUtc = item.lastWatchedUtc,
                year = libraryItem?.Year > 0 ? libraryItem.Year : libraryItem?.ReleaseDate?.Year,
                rating = libraryItem != null && libraryItem.Rating > 0 ? libraryItem.Rating : (double?)null,
                isWatched = libraryItem?.IsWatched == true,
                overview = libraryItem?.Overview ?? string.Empty,
                runtimeMinutes = libraryItem != null && libraryItem.RuntimeMinutes > 0 ? libraryItem.RuntimeMinutes : (int?)null,
                releaseDate = libraryItem?.ReleaseDate,
                sourceBadge = ResolveSourceBadge(libraryItem?.Type ?? item.type, libraryItem?.StreamSource),
                sourceApp = libraryItem?.StreamSource ?? string.Empty,
            };
        }

        private static string? NormalizeMediaBrowseType(string? type)
        {
            if (string.IsNullOrWhiteSpace(type))
            {
                return null;
            }

            var normalized = NormalizeToken(type);
            return normalized switch
            {
                "all" => null,
                "movie" => "movies",
                "movies" => "movies",
                "music" => "music",
                "song" => "music",
                "songs" => "music",
                "track" => "music",
                "tracks" => "music",
                "album" => "music",
                "albums" => "music",
                "app" => "apps",
                "apps" => "apps",
                "tv" => "tv",
                "series" => "tv",
                "show" => "tv",
                "shows" => "tv",
                _ => normalized,
            };
        }

        private static IEnumerable<Views.ViewModels.MediaItem> SnapshotLibraryItems(string? typeId = null)
        {
            var vm = MediaCentreViewModel.Instance;
            if (vm == null)
            {
                return Enumerable.Empty<Views.ViewModels.MediaItem>();
            }

            return SnapshotBrowseItems(vm, typeId);
        }

        private static List<Views.ViewModels.MediaItem> SearchLibraryItems(string query, string? typeId, int maxResults)
        {
            var trimmed = (query ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                return SnapshotLibraryItems(typeId)
                    .Take(Math.Max(1, maxResults))
                    .ToList();
            }

            var tokens = trimmed
                .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(token => token.Trim())
                .Where(token => !string.IsNullOrWhiteSpace(token))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            return SnapshotLibraryItems(typeId)
                .Select(item =>
                {
                    var haystack = string.Join(
                        " ",
                        new[]
                        {
                            item.Title ?? string.Empty,
                            item.Metadata ?? string.Empty,
                            item.Artist ?? string.Empty,
                            item.Album ?? string.Empty,
                            item.FilePath ?? string.Empty,
                            item.MetaId ?? string.Empty,
                            item.StreamSource ?? string.Empty,
                        });

                    var matchedTokens = tokens.Count(token =>
                        haystack.Contains(token, StringComparison.OrdinalIgnoreCase));
                    var containsFullQuery = haystack.Contains(trimmed, StringComparison.OrdinalIgnoreCase);

                    return new
                    {
                        Item = item,
                        MatchedTokens = matchedTokens,
                        ContainsFullQuery = containsFullQuery,
                    };
                })
                .Where(match => match.ContainsFullQuery || match.MatchedTokens > 0)
                .OrderByDescending(match => match.ContainsFullQuery)
                .ThenByDescending(match => match.MatchedTokens)
                .ThenBy(match => match.Item.Title ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .Select(match => match.Item)
                .Take(Math.Max(1, maxResults))
                .ToList();
        }

        private static IEnumerable<Views.ViewModels.MediaItem> SnapshotBrowseItems(MediaCentreViewModel vm, string? typeId = null)
        {
            var normalizedType = NormalizeMediaBrowseType(typeId);
            var items = new List<Views.ViewModels.MediaItem>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void AddItems(IEnumerable<Views.ViewModels.MediaItem> source)
            {
                foreach (var item in source)
                {
                    if (item == null)
                    {
                        continue;
                    }

                    if (!string.IsNullOrWhiteSpace(normalizedType) && !MediaItemMatchesBrowseType(item, normalizedType))
                    {
                        continue;
                    }

                    var key = ResolveBrowseIdentity(item);
                    if (!seen.Add(key))
                    {
                        continue;
                    }

                    items.Add(item);
                }
            }

            AddItems(vm.AllItems.ToList());

            if (string.IsNullOrWhiteSpace(normalizedType) || string.Equals(normalizedType, "movies", StringComparison.OrdinalIgnoreCase))
            {
                AddItems(vm.ServerMovieCatalogItems.ToList());
            }

            if (string.IsNullOrWhiteSpace(normalizedType) || string.Equals(normalizedType, "tv", StringComparison.OrdinalIgnoreCase))
            {
                AddItems(vm.ServerTvCatalogItems.ToList());
            }

            if (string.IsNullOrWhiteSpace(normalizedType) || string.Equals(normalizedType, "music", StringComparison.OrdinalIgnoreCase))
            {
                AddItems(vm.ServerMusicCatalogItems.ToList());
            }

            return items;
        }

        private static string ResolveBrowseIdentity(Views.ViewModels.MediaItem item)
        {
            var normalizedType = NormalizeMediaBrowseType(item.Type) ?? NormalizeToken(item.Type);
            var metaId = (item.MetaId ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(metaId))
            {
                return $"{normalizedType}|meta|{metaId}";
            }

            var filePath = (item.FilePath ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(filePath))
            {
                return $"{normalizedType}|path|{filePath}";
            }

            return $"{normalizedType}|title|{(item.Title ?? string.Empty).Trim()}";
        }

        private static IEnumerable<Views.ViewModels.AlbumEntry> SnapshotMusicAlbums(string? query, int maxResults)
        {
            var vm = MediaCentreViewModel.Instance;
            if (vm == null)
            {
                return Enumerable.Empty<Views.ViewModels.AlbumEntry>();
            }

            IEnumerable<Views.ViewModels.AlbumEntry> albums = vm.MusicAlbums.ToList();
            if (!string.IsNullOrWhiteSpace(query))
            {
                var trimmed = query.Trim();
                albums = albums.Where(album =>
                    album.AlbumTitle.Contains(trimmed, StringComparison.OrdinalIgnoreCase) ||
                    album.Artist.Contains(trimmed, StringComparison.OrdinalIgnoreCase) ||
                    album.Tracks.Any(track =>
                        (!string.IsNullOrWhiteSpace(track.Title) && track.Title.Contains(trimmed, StringComparison.OrdinalIgnoreCase)) ||
                        (!string.IsNullOrWhiteSpace(track.FilePath) && track.FilePath.Contains(trimmed, StringComparison.OrdinalIgnoreCase))));
            }

            return albums
                .OrderBy(album => album.Artist, StringComparer.OrdinalIgnoreCase)
                .ThenBy(album => album.AlbumTitle, StringComparer.OrdinalIgnoreCase)
                .Take(Math.Max(1, maxResults))
                .ToList();
        }

        private static IEnumerable<Views.ViewModels.MediaItem> SnapshotMusicTracks(string? query, int maxResults)
        {
            IEnumerable<Views.ViewModels.MediaItem> items = SnapshotLibraryItems("music");
            if (!string.IsNullOrWhiteSpace(query))
            {
                var trimmed = query.Trim();
                items = items.Where(item =>
                    (!string.IsNullOrWhiteSpace(item.Title) && item.Title.Contains(trimmed, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrWhiteSpace(item.Artist) && item.Artist.Contains(trimmed, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrWhiteSpace(item.Album) && item.Album.Contains(trimmed, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrWhiteSpace(item.Metadata) && item.Metadata.Contains(trimmed, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrWhiteSpace(item.FilePath) && item.FilePath.Contains(trimmed, StringComparison.OrdinalIgnoreCase)));
            }

            return items
                .OrderBy(item => item.Artist ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.Album ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.DiscNumber)
                .ThenBy(item => item.TrackNumber)
                .ThenBy(item => item.Title ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .Take(Math.Max(1, maxResults))
                .ToList();
        }

        private static IEnumerable<Views.ViewModels.MediaItem> GetLibraryItemsByRecency(string typeId, int maxResults)
        {
            return SnapshotLibraryItems(typeId)
                .OrderByDescending(ResolveMediaReleaseSortUtc)
                .ThenByDescending(item => item.Year)
                .ThenBy(item => item.Title ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .Take(maxResults)
                .ToList();
        }

        private static IEnumerable<Views.ViewModels.MediaItem> GetRecentlyAddedItems(int maxResults)
        {
            return SnapshotLibraryItems()
                .OrderByDescending(ResolveRecentlyAddedSortUtc)
                .ThenBy(item => item.Title ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .Take(maxResults)
                .ToList();
        }

        private static IEnumerable<Views.ViewModels.MediaItem> GetPopularItems(int maxResults)
        {
            var vm = MediaCentreViewModel.Instance;
            if (vm == null)
            {
                return Enumerable.Empty<Views.ViewModels.MediaItem>();
            }

            return vm.HomePopularMovies
                .Concat(vm.HomePopularSeries)
                .DistinctBy(ResolveMediaIdentity, StringComparer.OrdinalIgnoreCase)
                .Take(maxResults)
                .ToList();
        }

        private static IEnumerable<MediaAppEntry> GetAppsItems(string? query, int maxResults)
        {
            var vm = MediaCentreViewModel.Instance;
            if (vm == null)
            {
                return Enumerable.Empty<MediaAppEntry>();
            }

            IEnumerable<MediaAppEntry> items = vm.AppsBookmarks
                .Select(bookmark => new MediaAppEntry(
                    bookmark.Name,
                    bookmark.Url,
                    bookmark.IconUrl,
                    "Bookmark"))
                .Concat(vm.AppsServices.Select(service => new MediaAppEntry(
                    service.Name,
                    service.Url,
                    service.IconUrl,
                    "Service")));

            if (!string.IsNullOrWhiteSpace(query))
            {
                var trimmed = query.Trim();
                items = items.Where(item =>
                    item.Title.Contains(trimmed, StringComparison.OrdinalIgnoreCase) ||
                    item.SourcePath.Contains(trimmed, StringComparison.OrdinalIgnoreCase) ||
                    item.SectionName.Contains(trimmed, StringComparison.OrdinalIgnoreCase));
            }

            return items
                .DistinctBy(item => item.SourcePath, StringComparer.OrdinalIgnoreCase)
                .OrderBy(item => item.Title, StringComparer.OrdinalIgnoreCase)
                .Take(maxResults)
                .ToList();
        }

        private static object SerializeAppItem(MediaAppEntry item)
        {
            return new
            {
                title = item.Title,
                artist = string.Empty,
                album = string.Empty,
                albumArtUrl = item.ArtworkUrl,
                artworkUrl = item.ArtworkUrl,
                backdropUrl = item.ArtworkUrl,
                mediaType = "apps",
                sectionName = item.SectionName,
                sourcePath = item.SourcePath,
                fileName = item.SourcePath,
                displayName = item.Title,
                duration = 0d,
                isPlaying = false,
                progress = 0d,
                isWatched = false,
                sourceBadge = item.SectionName,
                sourceApp = item.SectionName,
            };
        }

        private static DateTime ResolveMediaReleaseSortUtc(Views.ViewModels.MediaItem item)
        {
            if (item.ReleaseDate.HasValue)
            {
                return item.ReleaseDate.Value.ToUniversalTime();
            }

            if (item.Year > 0)
            {
                return new DateTime(item.Year, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            }

            return ResolveMediaReleaseSortUtcFallback(item);
        }

        private static DateTime ResolveRecentlyAddedSortUtc(Views.ViewModels.MediaItem item)
        {
            try
            {
                var path = (item.FilePath ?? string.Empty).Trim();
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                {
                    return File.GetLastWriteTimeUtc(path);
                }
            }
            catch
            {
            }

            return ResolveMediaReleaseSortUtcFallback(item);
        }

        private static DateTime ResolveMediaReleaseSortUtcFallback(Views.ViewModels.MediaItem item)
        {
            if (item.ReleaseDate.HasValue)
            {
                return item.ReleaseDate.Value.ToUniversalTime();
            }

            if (item.Year > 0)
            {
                return new DateTime(item.Year, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            }

            return DateTime.MinValue;
        }

        private static string ResolveMediaIdentity(Views.ViewModels.MediaItem item)
        {
            if (!string.IsNullOrWhiteSpace(item.FilePath))
            {
                return item.FilePath;
            }

            if (!string.IsNullOrWhiteSpace(item.MetaId))
            {
                return item.MetaId;
            }

            return item.Title ?? string.Empty;
        }

        private static string ResolveSourceBadge(string? mediaType, string? streamSource)
        {
            var source = (streamSource ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(source))
            {
                return source;
            }

            return NormalizeMediaBrowseType(mediaType) switch
            {
                "movies" => "Movies",
                "tv" => "Series",
                "music" => "Music",
                "apps" => "App",
                _ => "Atlas",
            };
        }

        private static void MarkMediaItemWatched(Views.ViewModels.MediaItem? item, MediaLibraryControlRequest? request)
        {
            var sourcePath = (item?.FilePath ?? request?.SourcePath ?? string.Empty).Trim();
            var title = (item?.Title ?? request?.DisplayName ?? "Unknown item").Trim();
            var mediaType = (item?.Type ?? NormalizeMediaBrowseType(request?.MediaType) ?? "media").Trim();
            var artworkUrl = ResolveLibraryArtworkUrl(item);
            var backdropUrl = ResolveLibraryBackdropUrl(item);

            if (string.IsNullOrWhiteSpace(sourcePath))
            {
                throw new InvalidOperationException("A sourcePath is required to mark an item watched.");
            }

            AtlasAI.Core.WatchHistoryStore.AddOrUpdate(sourcePath, title, mediaType, artworkUrl, backdropUrl);
            var durationSeconds = item != null && item.Duration.TotalSeconds > 0 ? item.Duration.TotalSeconds : 1d;
            AtlasAI.Core.WatchHistoryStore.UpdateProgress(sourcePath, durationSeconds, durationSeconds);

            if (item != null)
            {
                item.IsWatched = true;
                item.ProgressPercent = 1d;
            }
        }

        private static void MarkMediaItemUnwatched(Views.ViewModels.MediaItem? item, MediaLibraryControlRequest? request)
        {
            var sourcePath = (item?.FilePath ?? request?.SourcePath ?? string.Empty).Trim();
            var title = (item?.Title ?? request?.DisplayName ?? string.Empty).Trim();
            var removed = 0;

            if (!string.IsNullOrWhiteSpace(sourcePath))
            {
                removed = AtlasAI.Core.WatchHistoryStore.RemoveMatching(sourcePath);
            }

            if (removed == 0 && !string.IsNullOrWhiteSpace(title))
            {
                removed = AtlasAI.Core.WatchHistoryStore.RemoveByTitle(title);
            }

            if (item != null)
            {
                item.IsWatched = false;
                item.ProgressPercent = 0d;
            }
        }

        private static void LaunchMediaApp(string? sourcePath, string? displayName)
        {
            var url = (sourcePath ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(url))
            {
                throw new InvalidOperationException("Atlas app launch requires a valid URL.");
            }

            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
                 !string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException($"Atlas cannot launch the requested app '{displayName ?? url}' because its URL is invalid.");
            }

            Process.Start(new ProcessStartInfo(uri.ToString()) { UseShellExecute = true });
        }

        private static Views.ViewModels.AlbumEntry? FindMusicAlbum(string? sourcePath, string? displayName)
        {
            var vm = MediaCentreViewModel.Instance;
            if (vm == null)
            {
                return null;
            }

            var trimmedSourcePath = (sourcePath ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(trimmedSourcePath))
            {
                var exact = vm.MusicAlbums.FirstOrDefault(album =>
                    string.Equals(album.SourceFolderPath, trimmedSourcePath, StringComparison.OrdinalIgnoreCase) ||
                    album.Tracks.Any(track => string.Equals(track.FilePath, trimmedSourcePath, StringComparison.OrdinalIgnoreCase)));
                if (exact != null)
                {
                    return exact;
                }
            }

            var trimmedDisplayName = (displayName ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(trimmedDisplayName))
            {
                return vm.MusicAlbums.FirstOrDefault(album =>
                    string.Equals(album.AlbumTitle, trimmedDisplayName, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals($"{album.Artist} - {album.AlbumTitle}", trimmedDisplayName, StringComparison.OrdinalIgnoreCase));
            }

            return null;
        }

        private static List<AtlasAI.MediaScanner.MediaItem> ResolveAlbumPlaybackItems(Views.ViewModels.AlbumEntry album)
        {
            return album.Tracks
                .Select(track => track.PlaybackItem)
                .Where(track => track != null)
                .Cast<AtlasAI.MediaScanner.MediaItem>()
                .OrderBy(track => track.TrackNumber ?? 999)
                .ThenBy(track => track.DisplayName)
                .ToList();
        }

        private static void PlayMusicAlbum(Views.ViewModels.AlbumEntry album)
        {
            var albumTracks = ResolveAlbumPlaybackItems(album);
            if (albumTracks.Count == 0)
            {
                throw new InvalidOperationException($"Atlas could not resolve playback tracks for album '{album.AlbumTitle}'.");
            }

            MediaPlaybackService.GetOrCreate().PlayAlbum(albumTracks, albumTracks[0]);
        }

        private static void QueueMusicAlbum(Views.ViewModels.AlbumEntry album, bool playNext)
        {
            var albumTracks = ResolveAlbumPlaybackItems(album);
            if (albumTracks.Count == 0)
            {
                throw new InvalidOperationException($"Atlas could not resolve playback tracks for album '{album.AlbumTitle}'.");
            }

            var playback = MediaPlaybackService.GetOrCreate();
            if (playNext)
            {
                foreach (var track in albumTracks.AsEnumerable().Reverse())
                {
                    playback.QueueNext(track);
                }

                return;
            }

            playback.AddToQueue(albumTracks);
        }

        private static double ResolveResumeSeconds(Views.ViewModels.MediaItem? item, MediaLibraryControlRequest? request)
        {
            if (request?.Seconds is double explicitSeconds && explicitSeconds > 0)
            {
                return explicitSeconds;
            }

            if (request?.Progress is double explicitProgress && explicitProgress > 0)
            {
                var duration = item?.Duration.TotalSeconds ?? 0d;
                if (duration > 0)
                {
                    return Math.Clamp(explicitProgress, 0d, 1d) * duration;
                }
            }

            var sourcePath = (item?.FilePath ?? request?.SourcePath ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(sourcePath))
            {
                return 0d;
            }

            var historyItem = AtlasAI.Core.WatchHistoryStore.GetRecent(40)
                .Where(entry => string.Equals(entry.filePath, sourcePath, StringComparison.OrdinalIgnoreCase))
                .Select(entry => new
                {
                    PositionSeconds = entry.positionSeconds,
                    DurationSeconds = entry.durationSeconds,
                })
                .FirstOrDefault();
            if (historyItem == null || historyItem.PositionSeconds <= 0)
            {
                return 0d;
            }

            return historyItem.PositionSeconds;
        }

        private sealed record MediaAppEntry(string Title, string SourcePath, string ArtworkUrl, string SectionName);

        private static Views.ViewModels.MediaItem? FindLibraryItem(string? sourcePath, string? displayName, string? mediaType)
        {
            var vm = MediaCentreViewModel.Instance;
            if (vm == null)
            {
                return null;
            }

            var typeFilter = NormalizeMediaBrowseType(mediaType);
            IEnumerable<Views.ViewModels.MediaItem> items = vm.AllItems;
            if (!string.IsNullOrWhiteSpace(typeFilter))
            {
                items = items.Where(item => MediaItemMatchesBrowseType(item, typeFilter));
            }

            if (!string.IsNullOrWhiteSpace(sourcePath))
            {
                var exact = items.FirstOrDefault(item => string.Equals(item.FilePath, sourcePath, StringComparison.OrdinalIgnoreCase));
                if (exact != null)
                {
                    return exact;
                }
            }

            if (!string.IsNullOrWhiteSpace(displayName))
            {
                return items.FirstOrDefault(item =>
                    string.Equals(item.Title, displayName, StringComparison.OrdinalIgnoreCase));
            }

            return null;
        }

        private static bool MediaItemMatchesBrowseType(Views.ViewModels.MediaItem? item, string? browseType)
        {
            if (item == null)
            {
                return false;
            }

            return MediaTypeMatchesBrowseType(item.Type, browseType);
        }

        private static bool MediaTypeMatchesBrowseType(string? itemType, string? browseType)
        {
            var normalizedFilter = NormalizeMediaBrowseType(browseType);
            if (string.IsNullOrWhiteSpace(normalizedFilter))
            {
                return true;
            }

            var normalizedItemType = NormalizeMediaBrowseType(itemType) ?? NormalizeToken(itemType);
            return string.Equals(normalizedItemType, normalizedFilter, StringComparison.OrdinalIgnoreCase);
        }

        private static string? ResolveMediaArtworkUrl(AtlasAI.MediaScanner.MediaItem? currentMedia)
        {
            var cover = currentMedia?.CoverUrl;
            if (string.IsNullOrWhiteSpace(cover))
            {
                return string.IsNullOrWhiteSpace(currentMedia?.FilePath)
                    ? null
                    : $"/api/media/artwork?sourcePath={Uri.EscapeDataString(currentMedia.FilePath)}";
            }

            if (Uri.TryCreate(cover, UriKind.Absolute, out var uri))
            {
                if (string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
                {
                    return cover;
                }

                if (string.Equals(uri.Scheme, Uri.UriSchemeFile, StringComparison.OrdinalIgnoreCase) &&
                    File.Exists(uri.LocalPath))
                {
                    return "/api/media/artwork";
                }
            }

            return File.Exists(cover) ? "/api/media/artwork" : null;
        }

        private static string? ResolveMediaBackdropUrl(AtlasAI.MediaScanner.MediaItem? currentMedia)
        {
            var backdrop = currentMedia?.BackdropUrl;
            if (string.IsNullOrWhiteSpace(backdrop))
            {
                return ResolveMediaArtworkUrl(currentMedia);
            }

            if (Uri.TryCreate(backdrop, UriKind.Absolute, out var uri))
            {
                if (string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
                {
                    return backdrop;
                }

                if (string.Equals(uri.Scheme, Uri.UriSchemeFile, StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(currentMedia?.FilePath))
                {
                    return $"/api/media/artwork?sourcePath={Uri.EscapeDataString(currentMedia.FilePath)}";
                }
            }

            return !string.IsNullOrWhiteSpace(currentMedia?.FilePath)
                ? $"/api/media/artwork?sourcePath={Uri.EscapeDataString(currentMedia.FilePath)}"
                : ResolveMediaArtworkUrl(currentMedia);
        }

        private static string? ResolveLibraryArtworkUrl(Views.ViewModels.MediaItem? item)
        {
            var cover = item?.CoverUrl;
            if (string.IsNullOrWhiteSpace(cover))
            {
                return string.IsNullOrWhiteSpace(item?.FilePath)
                    ? null
                    : $"/api/media/artwork?sourcePath={Uri.EscapeDataString(item.FilePath)}";
            }

            if (Uri.TryCreate(cover, UriKind.Absolute, out var uri))
            {
                if (string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
                {
                    return cover;
                }
            }

            return string.IsNullOrWhiteSpace(item?.FilePath)
                ? null
                : $"/api/media/artwork?sourcePath={Uri.EscapeDataString(item.FilePath)}";
        }

        private static string? ResolveAlbumArtworkUrl(Views.ViewModels.AlbumEntry? album)
        {
            return ResolveLibraryArtworkUrl(album?.Tracks.FirstOrDefault());
        }

        private static string? ResolveLibraryBackdropUrl(Views.ViewModels.MediaItem? item)
        {
            var backdrop = item?.BackdropUrl;
            if (string.IsNullOrWhiteSpace(backdrop))
            {
                return ResolveLibraryArtworkUrl(item);
            }

            if (Uri.TryCreate(backdrop, UriKind.Absolute, out var uri))
            {
                if (string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
                {
                    return backdrop;
                }
            }

            return string.IsNullOrWhiteSpace(item?.FilePath)
                ? ResolveLibraryArtworkUrl(item)
                : $"/api/media/artwork?sourcePath={Uri.EscapeDataString(item.FilePath)}";
        }

        private static string? ResolveStoredHistoryImageUrl(string? storedUrl, string? sourcePath)
        {
            if (!string.IsNullOrWhiteSpace(storedUrl) &&
                Uri.TryCreate(storedUrl, UriKind.Absolute, out var uri) &&
                (string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
            {
                return storedUrl;
            }

            return string.IsNullOrWhiteSpace(sourcePath)
                ? null
                : $"/api/media/artwork?sourcePath={Uri.EscapeDataString(sourcePath)}";
        }

        private static string? GetMediaArtworkPath(string? sourcePath = null)
        {
            if (!string.IsNullOrWhiteSpace(sourcePath))
            {
                var libraryItem = FindLibraryItem(sourcePath, null, null);
                var itemCover = libraryItem?.CoverUrl;
                if (!string.IsNullOrWhiteSpace(itemCover))
                {
                    if (File.Exists(itemCover))
                    {
                        return itemCover;
                    }

                    if (Uri.TryCreate(itemCover, UriKind.Absolute, out var coverUri) &&
                        string.Equals(coverUri.Scheme, Uri.UriSchemeFile, StringComparison.OrdinalIgnoreCase) &&
                        File.Exists(coverUri.LocalPath))
                    {
                        return coverUri.LocalPath;
                    }
                }

                var inferredArtworkPath = TryResolveArtworkPathFromSourcePath(sourcePath);
                if (!string.IsNullOrWhiteSpace(inferredArtworkPath))
                {
                    return inferredArtworkPath;
                }
            }

            var cover = MediaPlaybackService.Instance?.CurrentMedia?.CoverUrl;
            if (string.IsNullOrWhiteSpace(cover))
            {
                return null;
            }

            if (File.Exists(cover))
            {
                return cover;
            }

            if (Uri.TryCreate(cover, UriKind.Absolute, out var uri) &&
                string.Equals(uri.Scheme, Uri.UriSchemeFile, StringComparison.OrdinalIgnoreCase) &&
                File.Exists(uri.LocalPath))
            {
                return uri.LocalPath;
            }

            return null;
        }

        private static string? TryResolveArtworkPathFromSourcePath(string sourcePath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(sourcePath))
                {
                    return null;
                }

                var normalizedSourcePath = sourcePath.Trim();
                var directFileExists = File.Exists(normalizedSourcePath);

                if (directFileExists)
                {
                    if (AtlasAI.MediaMetadata.MediaArtworkCache.TryGetCustomMoviePosterCachePath(normalizedSourcePath, out var customMovie) &&
                        !string.IsNullOrWhiteSpace(customMovie) &&
                        File.Exists(customMovie))
                    {
                        return customMovie;
                    }

                    if (AtlasAI.MediaMetadata.MediaArtworkCache.TryGetMoviePosterCachePath(normalizedSourcePath, out var cachedMovie) &&
                        !string.IsNullOrWhiteSpace(cachedMovie) &&
                        File.Exists(cachedMovie))
                    {
                        return cachedMovie;
                    }
                }

                var folder = directFileExists
                    ? Path.GetDirectoryName(normalizedSourcePath)
                    : Directory.Exists(normalizedSourcePath)
                        ? normalizedSourcePath
                        : null;

                if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
                {
                    return null;
                }

                var albumFolder = folder;
                try
                {
                    var folderName = Path.GetFileName(folder) ?? string.Empty;
                    if (System.Text.RegularExpressions.Regex.IsMatch(folderName, "^(cd|disc|disk)\\s*0*\\d+$", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                    {
                        var parent = Path.GetDirectoryName(folder);
                        if (!string.IsNullOrWhiteSpace(parent) && Directory.Exists(parent))
                        {
                            albumFolder = parent;
                        }
                    }
                }
                catch
                {
                    albumFolder = folder;
                }

                if (AtlasAI.MediaMetadata.MediaArtworkCache.TryGetCustomMusicFolderCoverCachePath(albumFolder, out var customMusic) &&
                    !string.IsNullOrWhiteSpace(customMusic) &&
                    File.Exists(customMusic))
                {
                    return customMusic;
                }

                if (AtlasAI.MediaMetadata.MediaArtworkCache.TryGetMusicFolderCoverCachePath(albumFolder, out var cachedMusic) &&
                    !string.IsNullOrWhiteSpace(cachedMusic) &&
                    File.Exists(cachedMusic))
                {
                    return cachedMusic;
                }

                var baseName = directFileExists
                    ? (Path.GetFileNameWithoutExtension(normalizedSourcePath) ?? string.Empty)
                    : (Path.GetFileName(folder) ?? string.Empty);

                if (!string.IsNullOrWhiteSpace(baseName))
                {
                    var namedCandidates = new[]
                    {
                        $"{baseName}.png",
                        $"{baseName}.jpg",
                        $"{baseName}.jpeg",
                    };

                    foreach (var name in namedCandidates)
                    {
                        var candidate = Path.Combine(folder, name);
                        if (File.Exists(candidate))
                        {
                            return candidate;
                        }
                    }
                }

                var coverNames = new[]
                {
                    "folder.png", "folder.jpg", "folder.jpeg",
                    "cover.png", "cover.jpg", "cover.jpeg",
                    "album.png", "album.jpg", "album.jpeg",
                    "front.png", "front.jpg", "front.jpeg",
                    "poster.png", "poster.jpg", "poster.jpeg",
                    "AlbumArtSmall.jpg",
                };

                foreach (var name in coverNames)
                {
                    var candidate = Path.Combine(folder, name);
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }

                var parentFolder = Path.GetDirectoryName(folder);
                if (!string.IsNullOrWhiteSpace(parentFolder) && Directory.Exists(parentFolder) &&
                    !string.Equals(parentFolder, folder, StringComparison.OrdinalIgnoreCase))
                {
                    if (!string.IsNullOrWhiteSpace(baseName))
                    {
                        foreach (var name in new[] { $"{baseName}.png", $"{baseName}.jpg", $"{baseName}.jpeg" })
                        {
                            var candidate = Path.Combine(parentFolder, name);
                            if (File.Exists(candidate))
                            {
                                return candidate;
                            }
                        }
                    }

                    foreach (var name in coverNames)
                    {
                        var candidate = Path.Combine(parentFolder, name);
                        if (File.Exists(candidate))
                        {
                            return candidate;
                        }
                    }
                }
            }
            catch
            {
            }

            return null;
        }

        private static string GetImageContentType(string artworkPath)
        {
            return Path.GetExtension(artworkPath).ToLowerInvariant() switch
            {
                ".png" => "image/png",
                ".webp" => "image/webp",
                ".gif" => "image/gif",
                ".bmp" => "image/bmp",
                _ => "image/jpeg",
            };
        }

        private async Task InvokeMediaPlayerAsync(
            Action<MediaPlayerControl> mediaPlayerAction,
            Action<SimpleMediaPlayerControl> simplePlayerAction,
            Action? fallback = null)
        {
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                var handled = false;

                foreach (var player in EnumerateVisualChildren<MediaPlayerControl>(Application.Current))
                {
                    mediaPlayerAction(player);
                    handled = true;
                }

                foreach (var player in EnumerateVisualChildren<SimpleMediaPlayerControl>(Application.Current))
                {
                    simplePlayerAction(player);
                    handled = true;
                }

                if (!handled)
                {
                    fallback?.Invoke();
                }
            }).Task.ConfigureAwait(false);
        }

        private static IEnumerable<T> EnumerateVisualChildren<T>(Application application) where T : DependencyObject
        {
            if (application == null)
            {
                yield break;
            }

            foreach (Window window in application.Windows)
            {
                foreach (var match in EnumerateVisualChildren<T>(window))
                {
                    yield return match;
                }
            }
        }

        private static IEnumerable<T> EnumerateVisualChildren<T>(DependencyObject root) where T : DependencyObject
        {
            if (root == null)
            {
                yield break;
            }

            if (root is T match)
            {
                yield return match;
            }

            if (root is ContentControl contentControl && contentControl.Content is DependencyObject content)
            {
                foreach (var nested in EnumerateVisualChildren<T>(content))
                {
                    yield return nested;
                }
            }

            var childCount = 0;
            try
            {
                childCount = VisualTreeHelper.GetChildrenCount(root);
            }
            catch
            {
            }

            for (var index = 0; index < childCount; index++)
            {
                var child = VisualTreeHelper.GetChild(root, index);
                foreach (var nested in EnumerateVisualChildren<T>(child))
                {
                    yield return nested;
                }
            }
        }

        private static byte[] DecodeBase64Audio(string audio)
        {
            var payload = audio.Trim();
            var commaIndex = payload.IndexOf(',');
            if (payload.StartsWith("data:", StringComparison.OrdinalIgnoreCase) && commaIndex >= 0)
            {
                payload = payload[(commaIndex + 1)..];
            }

            try
            {
                return Convert.FromBase64String(payload);
            }
            catch (FormatException)
            {
                throw new FormatException("The 'audio' field must be a valid base64-encoded WAV payload.");
            }
        }

        private static async Task<T?> ReadJsonAsync<T>(HttpListenerRequest request)
        {
            using var reader = new StreamReader(request.InputStream, request.ContentEncoding ?? Encoding.UTF8);
            var json = await reader.ReadToEndAsync().ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(json))
            {
                return default;
            }

            return JsonSerializer.Deserialize<T>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            });
        }

        private static AIProviderType TryParseProviderType(string? rawProvider)
        {
            return Enum.TryParse<AIProviderType>((rawProvider ?? string.Empty).Trim(), true, out var providerType)
                ? providerType
                : AIProviderType.Claude;
        }

        private async Task<(string Reply, string? ConversationId)> SubmitRemoteTextToAtlasAsync(
            string message,
            string? conversationId,
            string? deviceName,
            string? requestId,
            bool startNewConversation)
        {
            if (Application.Current == null)
            {
                throw new InvalidOperationException("Atlas UI is not available.");
            }

            var previousRequestId = _activeReplyRequestId.Value;
            _activeReplyRequestId.Value = requestId;

            try
            {
                return await Application.Current.Dispatcher.InvokeAsync(async () =>
                {
                    var chatWindow = await GetOrCreateRemoteChatBackendAsync().ConfigureAwait(true);
                    var result = await chatWindow.SubmitRemoteTextMessageAsync(message, conversationId, deviceName, startNewConversation).ConfigureAwait(true);
                    TryMirrorRemoteConversationTurn(message, result.Reply, startNewConversation);
                    return result;
                }).Task.Unwrap().ConfigureAwait(false);
            }
            finally
            {
                _activeReplyRequestId.Value = previousRequestId;
            }
        }

        private async Task<string?> StartRemoteConversationAsync(string? deviceName)
        {
            if (Application.Current == null)
            {
                throw new InvalidOperationException("Atlas UI is not available.");
            }

            var conversationId = await Application.Current.Dispatcher.InvokeAsync(async () =>
            {
                var chatWindow = await GetOrCreateRemoteChatBackendAsync().ConfigureAwait(true);
                TryPrepareVisibleAiChatForRemoteConversation(startNewConversation: true);
                return await chatWindow.StartRemoteConversationAsync(deviceName).ConfigureAwait(true);
            }).Task.Unwrap().ConfigureAwait(false);

            BindDeviceConversation(deviceName, conversationId);
            return conversationId;
        }

        private async Task<ChatWindow.RemoteConversationState> GetConversationStateAsync(
            string? conversationId,
            string? deviceName,
            bool createIfMissing)
        {
            if (Application.Current == null)
            {
                throw new InvalidOperationException("Atlas UI is not available.");
            }

            return await Application.Current.Dispatcher.InvokeAsync(async () =>
            {
                var chatWindow = await GetOrCreateRemoteChatBackendAsync().ConfigureAwait(true);
                return await chatWindow.GetRemoteConversationStateAsync(conversationId, deviceName, createIfMissing).ConfigureAwait(true);
            }).Task.Unwrap().ConfigureAwait(false);
        }

        private async Task<ChatWindow> GetOrCreateRemoteChatBackendAsync()
        {
            if (Application.Current == null)
            {
                throw new InvalidOperationException("Atlas UI is not available.");
            }

            var visibleChatWindow = Application.Current.Windows
                .OfType<ChatWindow>()
                .FirstOrDefault(window => window.IsLoaded && window.IsVisible);

            if (visibleChatWindow != null)
            {
                _remoteChatBackend = visibleChatWindow;
                await visibleChatWindow.EnsureRemoteBackendReadyAsync().ConfigureAwait(true);
                return visibleChatWindow;
            }

            if (_remoteChatBackend != null && _remoteChatBackend.IsLoaded)
            {
                await _remoteChatBackend.EnsureRemoteBackendReadyAsync().ConfigureAwait(true);
                return _remoteChatBackend;
            }

            var chatWindow = new ChatWindow
            {
                IsRemoteCompanionBackend = true,
                ShowActivated = false,
                ShowInTaskbar = false,
                WindowState = WindowState.Minimized,
                Left = -32000,
                Top = -32000,
                Width = 1,
                Height = 1,
                Opacity = 0,
            };

            chatWindow.Closed += (_, __) =>
            {
                if (ReferenceEquals(_remoteChatBackend, chatWindow))
                {
                    _remoteChatBackend = null;
                }
            };

            _remoteChatBackend = chatWindow;
            chatWindow.Show();
            chatWindow.Hide();
            await chatWindow.EnsureRemoteBackendReadyAsync().ConfigureAwait(true);
            return chatWindow;
        }

        private static void TryPrepareVisibleAiChatForRemoteConversation(bool startNewConversation)
        {
            try
            {
                var commandCenter = Application.Current?.Windows
                    .OfType<CommandCenterWindow>()
                    .OrderByDescending(window => window.IsActive)
                    .FirstOrDefault(window => window.IsLoaded && window.IsVisible);

                if (commandCenter == null)
                {
                    return;
                }

                _ = commandCenter.PrepareRemoteChatSurfaceAsync(startNewConversation);
            }
            catch
            {
            }
        }

        private static void TryMirrorRemoteConversationTurn(string userMessage, string assistantReply, bool startNewConversation)
        {
            try
            {
                var commandCenter = Application.Current?.Windows
                    .OfType<CommandCenterWindow>()
                    .OrderByDescending(window => window.IsActive)
                    .FirstOrDefault(window => window.IsLoaded && window.IsVisible);

                if (commandCenter == null)
                {
                    return;
                }

                _ = commandCenter.PresentRemoteConversationTurnAsync(userMessage, assistantReply, startNewConversation);
            }
            catch
            {
            }
        }

        private void RegisterConversationSubscription(ConversationSubscription subscription)
        {
            lock (_subscriptionLock)
            {
                CleanupDeadConversationSubscriptions_NoLock();
                _conversationSubscriptions[subscription.Id] = subscription;
            }
        }

        private void RegisterLiveSyncSubscription(LiveSyncSubscription subscription)
        {
            lock (_subscriptionLock)
            {
                CleanupDeadLiveSyncSubscriptions_NoLock();
                _liveSyncSubscriptions[subscription.Id] = subscription;
            }
        }

        private void UnregisterConversationSubscription(Guid subscriptionId)
        {
            lock (_subscriptionLock)
            {
                _conversationSubscriptions.Remove(subscriptionId);
            }
        }

        private void UnregisterLiveSyncSubscription(Guid subscriptionId)
        {
            lock (_subscriptionLock)
            {
                _liveSyncSubscriptions.Remove(subscriptionId);
                CleanupDeadLiveSyncSubscriptions_NoLock();

                if (_liveSyncSubscriptions.Count == 0)
                {
                    StopLiveSyncLoop_NoLock();
                }
            }
        }

        private async Task PublishConversationStateChangedAsync(string? conversationId)
        {
            ConversationSubscription[] subscriptions;

            lock (_subscriptionLock)
            {
                CleanupDeadConversationSubscriptions_NoLock();
                subscriptions = _conversationSubscriptions.Values.ToArray();
            }

            foreach (var subscription in subscriptions)
            {
                var targetConversationId = ResolveConversationIdForSubscription(subscription);
                if (!string.IsNullOrWhiteSpace(conversationId) &&
                    !string.Equals(targetConversationId, conversationId, StringComparison.Ordinal))
                {
                    continue;
                }

                await SendConversationSnapshotAsync(subscription, CancellationToken.None).ConfigureAwait(false);
            }
        }

        private async Task SendConversationSnapshotAsync(
            ConversationSubscription subscription,
            CancellationToken cancellationToken)
        {
            if (subscription.Socket.State != WebSocketState.Open)
            {
                UnregisterConversationSubscription(subscription.Id);
                return;
            }

            try
            {
                var state = await GetConversationStateAsync(
                        ResolveConversationIdForSubscription(subscription),
                        subscription.DeviceName,
                        createIfMissing: false)
                    .ConfigureAwait(false);
                var progress = GetConversationProgressState(state.ConversationId);

                await SendSocketMessageAsync(subscription.Socket, new
                {
                    type = "conversation_state",
                    conversationId = state.ConversationId,
                    title = state.Title,
                    isAssistantThinking = progress?.IsThinking ?? false,
                    assistantStatus = progress?.Status,
                    messages = state.Messages.Select(message => new
                    {
                        id = message.Id,
                        role = message.Role,
                        text = message.Text,
                        timestamp = message.Timestamp,
                        isVoiceInput = message.IsVoiceInput,
                    }).ToArray(),
                }, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AppLogger.LogWarning($"[Companion] Failed to push conversation state: {ex.Message}");
                UnregisterConversationSubscription(subscription.Id);
            }
        }

        private string? ResolveConversationIdForSubscription(ConversationSubscription subscription)
        {
            if (!string.IsNullOrWhiteSpace(subscription.ConversationId))
            {
                return subscription.ConversationId;
            }

            return GetDeviceConversationId(subscription.DeviceName);
        }

        private void CleanupDeadConversationSubscriptions_NoLock()
        {
            var staleSubscriptions = _conversationSubscriptions
                .Where(entry => entry.Value.Socket.State != WebSocketState.Open)
                .Select(entry => entry.Key)
                .ToArray();

            foreach (var subscriptionId in staleSubscriptions)
            {
                _conversationSubscriptions.Remove(subscriptionId);
            }
        }

        private void CleanupDeadLiveSyncSubscriptions_NoLock()
        {
            var staleSubscriptions = _liveSyncSubscriptions
                .Where(entry => entry.Value.Socket.State != WebSocketState.Open)
                .Select(entry => entry.Key)
                .ToArray();

            foreach (var subscriptionId in staleSubscriptions)
            {
                _liveSyncSubscriptions.Remove(subscriptionId);
            }
        }

        private void EnsureLiveSyncLoopRunning()
        {
            lock (_subscriptionLock)
            {
                if (_liveSyncLoop != null && !_liveSyncLoop.IsCompleted)
                {
                    return;
                }

                if (_cts == null || _liveSyncSubscriptions.Count == 0)
                {
                    return;
                }

                _liveSyncLoopCts?.Cancel();
                _liveSyncLoopCts?.Dispose();
                _liveSyncLoopCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
                _lastMediaLiveSyncSignature = null;
                _lastMediaLiveSyncSignature = null;
                _lastDownloaderLiveSyncSignature = null;
                _lastDjLiveSyncSignature = null;
                _liveSyncLoop = Task.Run(() => RunLiveSyncLoopAsync(_liveSyncLoopCts.Token));
            }
        }

        private void StopLiveSyncLoop_NoLock()
        {
            try
            {
                _liveSyncLoopCts?.Cancel();
            }
            catch
            {
            }

            try
            {
                _liveSyncLoopCts?.Dispose();
            }
            catch
            {
            }

            _liveSyncLoopCts = null;
            _liveSyncLoop = null;
            _lastMediaLiveSyncSignature = null;
            _lastDownloaderLiveSyncSignature = null;
            _lastDjLiveSyncSignature = null;
        }

        private async Task RunLiveSyncLoopAsync(CancellationToken cancellationToken)
        {
            using var timer = new PeriodicTimer(LiveSyncBroadcastInterval);

            try
            {
                while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
                {
                    await BroadcastLiveSyncMediaIfChangedAsync(cancellationToken).ConfigureAwait(false);
                    await BroadcastLiveSyncDownloaderIfChangedAsync(cancellationToken).ConfigureAwait(false);
                    await BroadcastLiveSyncDjIfChangedAsync(cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                AppLogger.LogWarning($"[Companion] Live sync broadcast loop failed: {ex.Message}");
            }
        }

        private async Task BroadcastLiveSyncDownloaderIfChangedAsync(CancellationToken cancellationToken)
        {
            LiveSyncSubscription[] subscriptions;

            lock (_subscriptionLock)
            {
                CleanupDeadLiveSyncSubscriptions_NoLock();
                if (_liveSyncSubscriptions.Count == 0)
                {
                    StopLiveSyncLoop_NoLock();
                    return;
                }

                subscriptions = _liveSyncSubscriptions.Values.ToArray();
            }

            var nextSignature = BuildLiveSyncDownloaderSignature();
            if (string.Equals(nextSignature, _lastDownloaderLiveSyncSignature, StringComparison.Ordinal))
            {
                return;
            }

            _lastDownloaderLiveSyncSignature = nextSignature;
            foreach (var subscription in subscriptions)
            {
                await SendLiveSyncDownloaderSnapshotAsync(subscription, cancellationToken, force: true).ConfigureAwait(false);
            }
        }

        private async Task BroadcastLiveSyncDjIfChangedAsync(CancellationToken cancellationToken)
        {
            LiveSyncSubscription[] subscriptions;

            lock (_subscriptionLock)
            {
                CleanupDeadLiveSyncSubscriptions_NoLock();
                if (_liveSyncSubscriptions.Count == 0)
                {
                    StopLiveSyncLoop_NoLock();
                    return;
                }

                subscriptions = _liveSyncSubscriptions.Values.ToArray();
            }

            var nextSignature = await BuildLiveSyncDjSignatureAsync().ConfigureAwait(false);
            if (string.Equals(nextSignature, _lastDjLiveSyncSignature, StringComparison.Ordinal))
            {
                return;
            }

            _lastDjLiveSyncSignature = nextSignature;
            foreach (var subscription in subscriptions)
            {
                await SendLiveSyncDjSnapshotAsync(subscription, cancellationToken, force: true).ConfigureAwait(false);
            }
        }

        private async Task SendLiveSyncDownloaderSnapshotAsync(
            LiveSyncSubscription subscription,
            CancellationToken cancellationToken,
            bool force)
        {
            if (subscription.Socket.State != WebSocketState.Open)
            {
                UnregisterLiveSyncSubscription(subscription.Id);
                return;
            }

            try
            {
                await SendSocketMessageAsync(subscription.Socket, new
                {
                    type = "downloader_state",
                    channel = "downloader",
                    timestamp = DateTime.UtcNow,
                    payload = BuildDownloaderStateResponse(),
                }, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AppLogger.LogWarning($"[Companion] Failed to push live sync downloader snapshot: {ex.Message}");
                UnregisterLiveSyncSubscription(subscription.Id);
            }
        }

        private async Task SendLiveSyncDjSnapshotAsync(
            LiveSyncSubscription subscription,
            CancellationToken cancellationToken,
            bool force)
        {
            if (subscription.Socket.State != WebSocketState.Open)
            {
                UnregisterLiveSyncSubscription(subscription.Id);
                return;
            }

            try
            {
                await SendSocketMessageAsync(subscription.Socket, new
                {
                    type = "dj_state",
                    channel = "dj",
                    timestamp = DateTime.UtcNow,
                    payload = await BuildLiveSyncDjPayloadAsync().ConfigureAwait(false),
                }, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AppLogger.LogWarning($"[Companion] Failed to push live sync DJ snapshot: {ex.Message}");
                UnregisterLiveSyncSubscription(subscription.Id);
            }
        }

        private async Task BroadcastLiveSyncMediaIfChangedAsync(CancellationToken cancellationToken)
        {
            LiveSyncSubscription[] subscriptions;

            lock (_subscriptionLock)
            {
                CleanupDeadLiveSyncSubscriptions_NoLock();
                if (_liveSyncSubscriptions.Count == 0)
                {
                    StopLiveSyncLoop_NoLock();
                    return;
                }

                subscriptions = _liveSyncSubscriptions.Values.ToArray();
            }

            var nextSignature = BuildLiveSyncMediaSignature();
            if (string.Equals(nextSignature, _lastMediaLiveSyncSignature, StringComparison.Ordinal))
            {
                return;
            }

            _lastMediaLiveSyncSignature = nextSignature;
            foreach (var subscription in subscriptions)
            {
                await SendLiveSyncMediaSnapshotAsync(subscription, cancellationToken, force: true).ConfigureAwait(false);
            }
        }

        private async Task SendLiveSyncMediaSnapshotAsync(
            LiveSyncSubscription subscription,
            CancellationToken cancellationToken,
            bool force)
        {
            if (subscription.Socket.State != WebSocketState.Open)
            {
                UnregisterLiveSyncSubscription(subscription.Id);
                return;
            }

            try
            {
                await SendSocketMessageAsync(subscription.Socket, new
                {
                    type = "media_state",
                    channel = "media",
                    timestamp = DateTime.UtcNow,
                    payload = BuildLiveSyncMediaPayload(),
                }, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AppLogger.LogWarning($"[Companion] Failed to push live sync media snapshot: {ex.Message}");
                UnregisterLiveSyncSubscription(subscription.Id);
            }
        }

        private static async Task WriteJsonAsync(HttpListenerContext context, object payload)
        {
            var json = JsonSerializer.Serialize(payload);
            var bytes = Encoding.UTF8.GetBytes(json);
            context.Response.ContentType = "application/json";
            context.Response.ContentEncoding = Encoding.UTF8;
            context.Response.ContentLength64 = bytes.Length;
            await context.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
            context.Response.Close();
        }

        private static Task SendSocketMessageAsync(WebSocket socket, object payload, CancellationToken cancellationToken)
        {
            var json = JsonSerializer.Serialize(payload);
            var bytes = Encoding.UTF8.GetBytes(json);
            return socket.SendAsync(
                new ArraySegment<byte>(bytes),
                WebSocketMessageType.Text,
                true,
                cancellationToken);
        }

            private static Task SendSocketBinaryAsync(WebSocket socket, byte[] payload, CancellationToken cancellationToken)
            {
                return socket.SendAsync(
                new ArraySegment<byte>(payload),
                WebSocketMessageType.Binary,
                true,
                cancellationToken);
            }

        private async Task WriteReplyMediaAsync(HttpListenerContext context, string path)
        {
            var fileName = Path.GetFileName(path);
            ReplyMediaItem? media;

            lock (_replyLock)
            {
                CleanupOldReplyMedia_NoLock();
                _replyMedia.TryGetValue(fileName, out media);
            }

            if (media == null || !File.Exists(media.FilePath))
            {
                context.Response.StatusCode = 404;
                context.Response.Close();
                return;
            }

            context.Response.StatusCode = 200;
            context.Response.ContentType = media.ContentType;
            context.Response.ContentLength64 = new FileInfo(media.FilePath).Length;

            await using var fileStream = File.OpenRead(media.FilePath);
            await fileStream.CopyToAsync(context.Response.OutputStream).ConfigureAwait(false);
            context.Response.Close();
        }

        private void RegisterPendingReply(string? requestId, WebSocket socket)
        {
            if (string.IsNullOrWhiteSpace(requestId) || socket.State != WebSocketState.Open)
            {
                return;
            }

            lock (_replyLock)
            {
                CleanupExpiredPendingReplies_NoLock();

                if (_pendingReplies.ContainsKey(requestId))
                {
                    _pendingReplies[requestId] = new PendingReplySession(requestId, socket, DateTime.UtcNow);
                    return;
                }

                _pendingReplies[requestId] = new PendingReplySession(requestId, socket, DateTime.UtcNow);
                _pendingReplyOrder.Enqueue(requestId);
            }
        }

        private void UnregisterPendingReply(string? requestId, WebSocket? socket)
        {
            lock (_replyLock)
            {
                if (!string.IsNullOrWhiteSpace(requestId) &&
                    _pendingReplies.TryGetValue(requestId, out var existing) &&
                    ReferenceEquals(existing.Socket, socket))
                {
                    _pendingReplies.Remove(requestId);
                }

                CleanupExpiredPendingReplies_NoLock();
                CleanupExpiredConversationReplies_NoLock();
            }
        }

        private Task<ReplyMediaItem?> RegisterPendingConversationReply(string requestId)
        {
            var pendingReply = new PendingConversationReply(
                requestId,
                new TaskCompletionSource<ReplyMediaItem?>(TaskCreationOptions.RunContinuationsAsynchronously),
                DateTime.UtcNow);

            lock (_replyLock)
            {
                CleanupExpiredConversationReplies_NoLock();
                _pendingConversationReplies[requestId] = pendingReply;
            }

            return pendingReply.Completion.Task;
        }

        private async Task<ReplyMediaItem?> AwaitConversationReplyAudioAsync(
            string requestId,
            Task<ReplyMediaItem?> pendingAudioTask)
        {
            try
            {
                if (pendingAudioTask.IsCompleted)
                {
                    return await pendingAudioTask.ConfigureAwait(false);
                }

                return null;
            }
            finally
            {
                lock (_replyLock)
                {
                    _pendingConversationReplies.Remove(requestId);
                }
            }
        }

        private void TryPrewarmRemoteChatBackend()
        {
            try
            {
                if (Application.Current == null)
                {
                    return;
                }

                _ = Application.Current.Dispatcher.BeginInvoke(async () =>
                {
                    try
                    {
                        await GetOrCreateRemoteChatBackendAsync().ConfigureAwait(true);
                    }
                    catch (Exception ex)
                    {
                        AppLogger.LogWarning($"[Companion] Remote chat backend prewarm failed: {ex.Message}");
                    }
                    catch
                    {
                    }
                }, System.Windows.Threading.DispatcherPriority.Background);
            }
            catch
            {
            }
        }

        private string? GetDeviceConversationId(string? deviceName)
        {
            if (string.IsNullOrWhiteSpace(deviceName))
            {
                return null;
            }

            lock (_stateLock)
            {
                return _deviceSessions.TryGetValue(deviceName, out var conversationId)
                    ? conversationId
                    : null;
            }
        }

        private void BindDeviceConversation(string? deviceName, string? conversationId)
        {
            if (string.IsNullOrWhiteSpace(deviceName))
            {
                return;
            }

            lock (_stateLock)
            {
                if (string.IsNullOrWhiteSpace(conversationId))
                {
                    _deviceSessions.Remove(deviceName);
                }
                else
                {
                    _deviceSessions[deviceName] = conversationId;
                }

                SaveDeviceSessionBindings_NoLock();
            }
        }

        private void LoadDeviceSessionBindings()
        {
            lock (_stateLock)
            {
                try
                {
                    if (!File.Exists(_deviceSessionMapPath))
                    {
                        return;
                    }

                    var json = File.ReadAllText(_deviceSessionMapPath);
                    var bindings = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                    if (bindings == null)
                    {
                        return;
                    }

                    _deviceSessions.Clear();
                    foreach (var binding in bindings)
                    {
                        if (!string.IsNullOrWhiteSpace(binding.Key) && !string.IsNullOrWhiteSpace(binding.Value))
                        {
                            _deviceSessions[binding.Key] = binding.Value;
                        }
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.LogWarning($"[Companion] Failed to load device session bindings: {ex.Message}");
                }
            }
        }

        private void SaveDeviceSessionBindings_NoLock()
        {
            try
            {
                var json = JsonSerializer.Serialize(_deviceSessions);
                File.WriteAllText(_deviceSessionMapPath, json);
            }
            catch (Exception ex)
            {
                AppLogger.LogWarning($"[Companion] Failed to save device session bindings: {ex.Message}");
            }
        }

        private PendingReplySession? TryTakePendingReplySession_NoLock(string? requestId)
        {
            if (string.IsNullOrWhiteSpace(requestId))
            {
                return null;
            }

            if (_pendingReplies.Remove(requestId, out var session) && session.Socket.State == WebSocketState.Open)
            {
                return session;
            }

            return null;
        }

        private ConversationProgressState? GetConversationProgressState(string? conversationId)
        {
            if (string.IsNullOrWhiteSpace(conversationId))
            {
                return null;
            }

            lock (_subscriptionLock)
            {
                return _conversationProgress.TryGetValue(conversationId, out var progress)
                    ? progress
                    : null;
            }
        }

        private PendingConversationReply? TryTakePendingConversationReply_NoLock(string? requestId)
        {
            if (string.IsNullOrWhiteSpace(requestId))
            {
                return null;
            }

            if (_pendingConversationReplies.Remove(requestId, out var reply))
            {
                return reply;
            }

            return null;
        }

        private PendingReplySession? DequeueNextPendingReply_NoLock()
        {
            while (_pendingReplyOrder.Count > 0)
            {
                var requestId = _pendingReplyOrder.Dequeue();
                if (_pendingReplies.Remove(requestId, out var session))
                {
                    if (session.Socket.State == WebSocketState.Open)
                    {
                        return session;
                    }
                }
            }

            return null;
        }

        private void CleanupExpiredPendingReplies_NoLock()
        {
            var expiry = DateTime.UtcNow.AddMinutes(-2);
            var expired = _pendingReplies
                .Where(entry => entry.Value.CreatedUtc < expiry || entry.Value.Socket.State != WebSocketState.Open)
                .Select(entry => entry.Key)
                .ToArray();

            foreach (var requestId in expired)
            {
                _pendingReplies.Remove(requestId);
            }
        }

        private void CleanupExpiredConversationReplies_NoLock()
        {
            var expiry = DateTime.UtcNow.AddMinutes(-2);
            var expired = _pendingConversationReplies
                .Where(entry => entry.Value.CreatedUtc < expiry)
                .Select(entry => entry.Key)
                .ToArray();

            foreach (var requestId in expired)
            {
                if (_pendingConversationReplies.Remove(requestId, out var pendingReply))
                {
                    pendingReply.Completion.TrySetResult(null);
                }
            }
        }

        private ReplyMediaItem? CopyReplyMedia(string sourcePath)
        {
            try
            {
                if (!File.Exists(sourcePath))
                {
                    return null;
                }

                var extension = Path.GetExtension(sourcePath);
                if (string.IsNullOrWhiteSpace(extension))
                {
                    extension = ".bin";
                }

                var fileName = $"{DateTime.UtcNow:yyyyMMddHHmmssfff}_{Guid.NewGuid():N}{extension}";
                var destinationPath = Path.Combine(_replyCacheDir, fileName);
                File.Copy(sourcePath, destinationPath, true);

                var item = new ReplyMediaItem(
                    fileName,
                    destinationPath,
                    GetContentType(extension),
                    DateTime.UtcNow);

                lock (_replyLock)
                {
                    _replyMedia[fileName] = item;
                    CleanupOldReplyMedia_NoLock();
                }

                return item;
            }
            catch (Exception ex)
            {
                AppLogger.LogWarning($"[Companion] Failed to copy reply audio: {ex.Message}");
                return null;
            }
        }

        private void CleanupOldReplyMedia_NoLock()
        {
            var expiry = DateTime.UtcNow.AddMinutes(-10);
            var staleFiles = _replyMedia
                .Where(entry => entry.Value.CreatedUtc < expiry)
                .Select(entry => entry.Key)
                .ToArray();

            foreach (var fileName in staleFiles)
            {
                if (_replyMedia.Remove(fileName, out var media))
                {
                    try
                    {
                        if (File.Exists(media.FilePath))
                        {
                            File.Delete(media.FilePath);
                        }
                    }
                    catch
                    {
                    }
                }
            }
        }

        private static string GetContentType(string extension)
        {
            return extension.ToLowerInvariant() switch
            {
                ".mp3" => "audio/mpeg",
                ".wav" => "audio/wav",
                ".ogg" => "audio/ogg",
                ".m4a" => "audio/mp4",
                _ => "application/octet-stream",
            };
        }

        public void Dispose()
        {
            _hardwareMetricsService.Dispose();
            Stop();
        }

        private void StartInternal(string trigger, bool scheduleRestartOnFailure, bool isAutoRestart, bool rethrowOnFailure)
        {
            Exception? failure = null;
            var shouldRaiseStatusChanged = false;
            var shouldPrewarmRemoteBackend = false;
            var startupWarning = string.Empty;

            lock (_stateLock)
            {
                if (_listener != null)
                {
                    return;
                }

                if (_lifecycleState == CompanionLifecycleState.Starting ||
                    _lifecycleState == CompanionLifecycleState.Restarting)
                {
                    return;
                }

                if (!isAutoRestart)
                {
                    CancelRestartLoop_NoLock(resetAttemptCount: false);
                }

                _lifecycleState = isAutoRestart
                    ? CompanionLifecycleState.Restarting
                    : CompanionLifecycleState.Starting;

                try
                {
                    _cts = new CancellationTokenSource();
                    var listener = StartListenerWithFallback(
                        out var activePrefixes,
                        out var boundLanAddresses,
                        out var loopbackOnlyMode,
                        out var startWarning,
                        out var prefixBindingResults,
                        out var urlAclFixCommand);

                    _activePrefixes = activePrefixes;
                    _boundLanAddresses = boundLanAddresses;
                    _loopbackOnlyMode = loopbackOnlyMode;
                    _prefixBindingResults = prefixBindingResults;
                    _urlAclFixCommand = urlAclFixCommand;

                    if (listener == null)
                    {
                        var failureMessage = string.IsNullOrWhiteSpace(startWarning)
                            ? $"No listener prefixes were available on port {Port}."
                            : startWarning;
                        throw new InvalidOperationException(failureMessage);
                    }

                    _listener = listener;
                    _lastStartupError = null;
                    _listenLoop = Task.Run(() => ListenLoopAsync(listener, _cts.Token));
                    _restartAttemptCount = 0;
                    _lifecycleState = CompanionLifecycleState.Running;
                    startupWarning = startWarning ?? string.Empty;
                    shouldPrewarmRemoteBackend = true;
                    shouldRaiseStatusChanged = true;
                }
                catch (Exception ex)
                {
                    failure = ex;
                    ClearRuntimeState_NoLock();
                    _lastStartupError = ex.Message;
                    _urlAclFixCommand ??= BuildUrlAclFixCommand();
                    _lifecycleState = CompanionLifecycleState.Error;
                    shouldRaiseStatusChanged = true;
                }
            }

            if (!string.IsNullOrWhiteSpace(startupWarning))
            {
                AppLogger.LogWarning($"[Companion] {startupWarning}");
            }

            if (failure == null)
            {
                AppLogger.LogInfo($"[Companion] Transport service started on port {Port}");
                if (shouldPrewarmRemoteBackend)
                {
                    TryPrewarmRemoteChatBackend();
                }

                if (shouldRaiseStatusChanged)
                {
                    OnStatusChanged();
                }

                return;
            }

            AppLogger.LogWarning($"[Companion] Startup failed during {trigger}: {failure.Message}");

            if (scheduleRestartOnFailure)
            {
                ScheduleRestart(trigger, failure.Message);
            }

            if (shouldRaiseStatusChanged)
            {
                OnStatusChanged();
            }

            if (rethrowOnFailure)
            {
                ExceptionDispatchInfo.Capture(failure).Throw();
            }
        }

        private void ScheduleRestart(string trigger, string failureMessage)
        {
            var shouldRaiseStatusChanged = false;

            lock (_stateLock)
            {
                if (_listener != null)
                {
                    return;
                }

                if (_restartLoop != null && !_restartLoop.IsCompleted)
                {
                    return;
                }

                CancelRestartLoop_NoLock(resetAttemptCount: false);
                _restartCts = new CancellationTokenSource();
                _lifecycleState = CompanionLifecycleState.Restarting;
                _restartLoop = Task.Run(() => RunRestartLoopAsync(trigger, failureMessage, _restartCts.Token), _restartCts.Token);
                shouldRaiseStatusChanged = true;
            }

            AppLogger.LogWarning($"[Companion] Scheduling auto-restart after {trigger}: {failureMessage}");

            if (shouldRaiseStatusChanged)
            {
                OnStatusChanged();
            }
        }

        private async Task RunRestartLoopAsync(string trigger, string failureMessage, CancellationToken cancellationToken)
        {
            try
            {
                for (var attempt = 1; attempt <= MaxAutoRestartAttempts; attempt++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await Task.Delay(AutoRestartDelay, cancellationToken).ConfigureAwait(false);

                    lock (_stateLock)
                    {
                        if (_listener != null)
                        {
                            return;
                        }

                        _restartAttemptCount = attempt;
                        _lifecycleState = CompanionLifecycleState.Restarting;
                    }

                    AppLogger.LogWarning($"[Companion] Auto-restart attempt {attempt}/{MaxAutoRestartAttempts} after {trigger}: {failureMessage}");
                    OnStatusChanged();

                    try
                    {
                        StartInternal($"auto-restart attempt {attempt}", scheduleRestartOnFailure: false, isAutoRestart: true, rethrowOnFailure: true);
                        return;
                    }
                    catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                    {
                        failureMessage = ex.Message;
                        AppLogger.LogWarning($"[Companion] Auto-restart attempt {attempt}/{MaxAutoRestartAttempts} failed: {ex.Message}");
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                var shouldRaiseStatusChanged = false;

                lock (_stateLock)
                {
                    if (_listener == null && _lifecycleState == CompanionLifecycleState.Restarting)
                    {
                        _lifecycleState = CompanionLifecycleState.Error;
                        shouldRaiseStatusChanged = true;
                    }

                    if (_restartCts != null)
                    {
                        try
                        {
                            _restartCts.Dispose();
                        }
                        catch
                        {
                        }
                    }

                    _restartCts = null;
                    _restartLoop = null;
                }

                if (shouldRaiseStatusChanged)
                {
                    OnStatusChanged();
                }
            }
        }

        private void HandleListenerLoopExit(HttpListener listener, CancellationToken cancellationToken, Exception? terminalException)
        {
            var failureMessage = string.Empty;
            var shouldRaiseStatusChanged = false;
            var shouldScheduleRestart = false;

            lock (_stateLock)
            {
                if (!ReferenceEquals(_listener, listener))
                {
                    return;
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    _listenLoop = null;
                    return;
                }

                failureMessage = terminalException switch
                {
                    HttpListenerException listenerException => $"Listener stopped unexpectedly ({listenerException.ErrorCode}): {listenerException.Message}",
                    ObjectDisposedException disposedException => $"Listener stopped unexpectedly: {disposedException.Message}",
                    _ when terminalException != null => $"Listener stopped unexpectedly: {terminalException.Message}",
                    _ => "Listener stopped unexpectedly.",
                };

                try
                {
                    listener.Close();
                }
                catch
                {
                }

                ClearRuntimeState_NoLock();
                _lastStartupError = failureMessage;
                _lifecycleState = CompanionLifecycleState.Error;
                shouldRaiseStatusChanged = true;
                shouldScheduleRestart = true;
            }

            AppLogger.LogWarning($"[Companion] {failureMessage}");

            if (shouldRaiseStatusChanged)
            {
                OnStatusChanged();
            }

            if (shouldScheduleRestart)
            {
                ScheduleRestart("unexpected listener shutdown", failureMessage);
            }
        }

        private void CancelRestartLoop_NoLock(bool resetAttemptCount)
        {
            try
            {
                _restartCts?.Cancel();
            }
            catch
            {
            }

            try
            {
                _restartCts?.Dispose();
            }
            catch
            {
            }

            _restartCts = null;
            _restartLoop = null;

            if (resetAttemptCount)
            {
                _restartAttemptCount = 0;
            }
        }

        private void ClearRuntimeState_NoLock()
        {
            _listener = null;
            _activePrefixes = Array.Empty<string>();
            _boundLanAddresses = Array.Empty<string>();
            _loopbackOnlyMode = false;
            StopLiveSyncLoop_NoLock();

            try
            {
                _cts?.Dispose();
            }
            catch
            {
            }

            _cts = null;
            _listenLoop = null;
        }

        private sealed record PendingReplySession(string RequestId, WebSocket Socket, DateTime CreatedUtc);

        private sealed record ConversationSubscription(
            Guid Id,
            WebSocket Socket,
            string? DeviceName,
            string? ConversationId,
            DateTime CreatedUtc);

        private sealed record ConversationProgressState(
            bool IsThinking,
            string Status,
            DateTime UpdatedUtc);

        private sealed record LiveSyncSubscription(
            Guid Id,
            WebSocket Socket,
            string? DeviceName,
            DateTime CreatedUtc);

        private sealed record PendingConversationReply(
            string RequestId,
            TaskCompletionSource<ReplyMediaItem?> Completion,
            DateTime CreatedUtc);

        private sealed class RemoteConversationRequest
        {
            public string? Message { get; init; }

            public string? Device { get; init; }

            public string? ConversationId { get; init; }

            public bool IncludeReplyAudio { get; init; }

            public bool StartNewConversation { get; init; }
        }

        private sealed class RemoteConversationResetRequest
        {
            public string? Device { get; init; }
        }

        private sealed class AssistantStateUpdateRequest
        {
            public string? AiProvider { get; init; }

            public string? PersonalityId { get; init; }

            public string? VoiceProvider { get; init; }

            public string? VoiceId { get; init; }
        }

        private sealed class AiChatRequest
        {
            public string? Message { get; init; }

            public string? ConversationId { get; init; }

            public string? Device { get; init; }

            public bool WaitForReply { get; init; } = true;
        }

        private sealed class AiVoiceRequest
        {
            public string? Audio { get; init; }

            public string? ConversationId { get; init; }

            public string? Device { get; init; }
        }

        private sealed class SmartHomeToggleAliasRequest
        {
            public bool? IsOn { get; init; }
        }

        private sealed class MediaControlRequest
        {
            public string? Action { get; init; }
            public double? Seconds { get; init; }
            public double? Progress { get; init; }
            public double? Speed { get; init; }
            public bool? Enabled { get; init; }
            public int? TrackId { get; init; }
        }

        private sealed class MediaVolumeRequest
        {
            public double Volume { get; init; }
        }

        private sealed class MediaQueueControlRequest
        {
            public string? Action { get; init; }
            public int? Index { get; init; }
            public int? TargetIndex { get; init; }
        }

        private sealed class MediaLibraryControlRequest
        {
            public string? Action { get; init; }
            public string? SourcePath { get; init; }
            public string? DisplayName { get; init; }
            public string? MediaType { get; init; }
            public double? Seconds { get; init; }
            public double? Progress { get; init; }
        }

        private sealed class DownloaderControlRequest
        {
            public string? Action { get; init; }
            public string? JobId { get; init; }
            public string? Input { get; init; }
            public List<string>? Urls { get; init; }
        }

        private sealed class DjControlRequest
        {
            public string? Action { get; init; }
            public string? Deck { get; init; }
            public string? Path { get; init; }
            public double? Value { get; init; }
            public string? Band { get; init; }
            public int? CueIndex { get; init; }
            public double? TransitionBeats { get; init; }
        }

        private sealed class CodeWorkspaceRequest
        {
            public string? Path { get; init; }
        }

        private sealed class CodeWriteRequest
        {
            public string? Path { get; init; }
            public string? Content { get; init; }
        }

        private sealed class CodePatchRequest
        {
            public string? Path { get; init; }
            public string? OldText { get; init; }
            public string? NewText { get; init; }
        }

        private sealed class CodeRunRequest
        {
            public string? Action { get; init; }
            public string? Command { get; init; }
            public int? TimeoutSeconds { get; init; }
        }

        private sealed record CompanionSecurityAlert(string Id, DateTime Timestamp, string Severity, string Message);

        private sealed record AssistantVoiceOption(
            string Id,
            string DisplayName,
            string Category,
            string Provider,
            string Description,
            bool IsCloud);

        private sealed record AssistantVoiceStateSnapshot(
            VoiceProviderType ActiveProvider,
            string? SelectedVoiceId,
            string? GlobalVoiceId,
            IReadOnlyList<AssistantVoiceOption> Voices);

        private sealed record AssistantVoiceUpdateResult(bool Success, string? ErrorMessage);

        private sealed class CompanionBillingModeRequest
        {
            public string? Mode { get; init; }
        }

        private sealed class CompanionBillingPlanChangeRequest
        {
            public string? PlanId { get; init; }

            public bool ResetCycle { get; init; } = true;

            public string? Source { get; init; }
        }

        private sealed class CompanionBillingTopUpRequest
        {
            public int Credits { get; init; }

            public string? Source { get; init; }

            public string? Note { get; init; }
        }

        private sealed class CompanionBillingQuoteRequest
        {
            public string? Module { get; init; }

            public string? Kind { get; init; }

            public int Units { get; init; }

            public string? Provider { get; init; }

            public string? Model { get; init; }

            public string? ActorId { get; init; }
        }

        private sealed class CompanionSmartHomeActionRequest
        {
            public string? ProviderId { get; init; }

            public string? Provider { get; init; }

            public string? DeviceId { get; init; }

            public string? Id { get; init; }

            public string? Sku { get; init; }

            public string? CapabilityType { get; init; }

            public string? CapabilityInstance { get; init; }

            public JsonElement Value { get; init; }

            public string? Action { get; init; }

            public bool? IsOn { get; init; }

            public double? Brightness { get; init; }

            public double? Level { get; init; }

            public double? TargetTemperature { get; init; }

            public bool? Locked { get; init; }

            public bool? Trigger { get; init; }
        }

        private sealed record ReplyMediaItem(string FileName, string FilePath, string ContentType, DateTime CreatedUtc);
    }
}