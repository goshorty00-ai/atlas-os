using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AtlasAI.SmartHome
{
    internal interface ISmartHomeProvider
    {
        string ProviderId { get; }
        string DisplayName { get; }
        SmartHomeProviderDescriptor GetDescriptor();
        Task<SmartHomeProviderState> GetStateAsync(CancellationToken cancellationToken);
        Task<SmartHomeActionResult> ExecuteActionAsync(SmartHomeActionRequest request, CancellationToken cancellationToken);
    }

    internal sealed class SmartHomeActionRequest
    {
        public required string ProviderId { get; init; }
        public required string DeviceId { get; init; }
        public required string Sku { get; init; }
        public required string CapabilityType { get; init; }
        public required string CapabilityInstance { get; init; }
        public JsonElement Value { get; init; }
    }

    internal sealed class SmartHomeActionResult
    {
        public bool Ok { get; init; }
        public string Message { get; init; } = string.Empty;
        public string Outcome { get; init; } = string.Empty;
        public string ProviderId { get; init; } = string.Empty;
        public string DeviceId { get; init; } = string.Empty;
        public string CapabilityType { get; init; } = string.Empty;
        public string CapabilityInstance { get; init; } = string.Empty;
    }

    internal sealed class SmartHomeSceneActionState
    {
        public string ProviderId { get; init; } = string.Empty;
        public string DeviceId { get; init; } = string.Empty;
        public string DeviceName { get; init; } = string.Empty;
        public string Sku { get; init; } = string.Empty;
        public string CapabilityType { get; init; } = string.Empty;
        public string CapabilityInstance { get; init; } = string.Empty;
        public JsonElement Value { get; init; }
        public string HexColor { get; init; } = string.Empty;
    }

    internal sealed class RingLiveSessionStartRequest
    {
        public required string DeviceId { get; init; }
        public required string OfferSdp { get; init; }
    }

    internal sealed class RingLiveSessionStartResult
    {
        public bool Ok { get; init; }
        public string Message { get; init; } = string.Empty;
        public string SessionId { get; init; } = string.Empty;
        public string AnswerSdp { get; init; } = string.Empty;
    }

    internal sealed class RingLiveSessionStopResult
    {
        public bool Ok { get; init; }
        public string Message { get; init; } = string.Empty;
    }

    internal sealed class RingLiveSessionSpeakerResult
    {
        public bool Ok { get; init; }
        public string Message { get; init; } = string.Empty;
        public string SessionId { get; init; } = string.Empty;
    }

    internal sealed class RingManagedLiveViewStartResult
    {
        public bool Ok { get; init; }
        public string Message { get; init; } = string.Empty;
        public string CameraId { get; init; } = string.Empty;
        public string PlayerUrl { get; init; } = string.Empty;
        public string ManifestUrl { get; init; } = string.Empty;
    }

    internal sealed class RingManagedLiveViewStatusResult
    {
        public bool Ok { get; init; }
        public string State { get; init; } = string.Empty;
        public string Message { get; init; } = string.Empty;
        public string CameraId { get; init; } = string.Empty;
        public string PlayerUrl { get; init; } = string.Empty;
        public string ManifestUrl { get; init; } = string.Empty;
    }

    internal sealed class RingManagedLiveViewStopResult
    {
        public bool Ok { get; init; }
        public string CameraId { get; init; } = string.Empty;
        public string Message { get; init; } = string.Empty;
    }

    internal sealed class SmartHomeCameraRecordingResult
    {
        public bool Ok { get; init; }
        public string Message { get; init; } = string.Empty;
        public string RecordingId { get; init; } = string.Empty;
        public string OutputPath { get; init; } = string.Empty;
    }

    internal sealed class SmartHomeProviderDescriptor
    {
        public required string ProviderId { get; init; }
        public required string DisplayName { get; init; }
        public required string Status { get; init; }
        public required bool IsConfigured { get; init; }
        public required IReadOnlyList<string> RequiredFields { get; init; }
        public IReadOnlyList<string> ConfiguredFields { get; init; } = Array.Empty<string>();
        public string Detail { get; init; } = string.Empty;
    }

    internal sealed class SmartHomeProviderState
    {
        public required string ProviderId { get; init; }
        public required string DisplayName { get; init; }
        public required SmartHomeProviderDescriptor Descriptor { get; init; }
        public SmartHomeProviderFormState SavedSettings { get; init; } = new();
        public IReadOnlyList<SmartHomeDevice> Devices { get; init; } = Array.Empty<SmartHomeDevice>();
        public string Error { get; init; } = string.Empty;
        public string RuntimeStatus => Descriptor?.Status ?? string.Empty;
        public bool IsAvailable => Descriptor?.IsConfigured == true && string.IsNullOrWhiteSpace(Error);
        public string StatusDetail => !string.IsNullOrWhiteSpace(Error) ? Error : Descriptor?.Detail ?? string.Empty;
    }

    internal sealed class SmartHomeProviderFormState
    {
        public bool Enabled { get; init; }
        public string ApiKey { get; init; } = string.Empty;
        public string BridgeIp { get; init; } = string.Empty;
        public string ApplicationKey { get; init; } = string.Empty;
        public string RefreshToken { get; init; } = string.Empty;
        public string Host { get; init; } = string.Empty;
        public string ClientKey { get; init; } = string.Empty;
        public string AccessToken { get; init; } = string.Empty;
        public string BaseUrl { get; init; } = string.Empty;
        public string Username { get; init; } = string.Empty;
        public string Password { get; init; } = string.Empty;
        public string RtspUrl { get; init; } = string.Empty;
        public string LocationId { get; init; } = string.Empty;
    }

    internal sealed class SmartHomeDevice
    {
        public required string DeviceId { get; init; }
        public required string Name { get; init; }
        public string Sku { get; init; } = string.Empty;
        public string DeviceType { get; init; } = string.Empty;
        public bool? IsOnline { get; init; }
        public string PreviewImageUrl { get; init; } = string.Empty;
        public string PreviewVideoUrl { get; init; } = string.Empty;
        public string ExternalUrl { get; init; } = string.Empty;
        public IReadOnlyList<SmartHomeCapability> Capabilities { get; init; } = Array.Empty<SmartHomeCapability>();
        public string Availability => IsOnline == false ? "offline" : "online";
        public string SupportStatus => Capabilities.Count > 0 ? "supported" : "unsupported";
    }

    internal sealed class SmartHomeCapability
    {
        public required string Type { get; init; }
        public required string Instance { get; init; }
        public string DataType { get; init; } = string.Empty;
        public string Unit { get; init; } = string.Empty;
        public int? Min { get; init; }
        public int? Max { get; init; }
        public bool HasState { get; init; }
        public JsonElement StateValue { get; init; }
        public IReadOnlyList<SmartHomeCapabilityOption> Options { get; init; } = Array.Empty<SmartHomeCapabilityOption>();
    }

    internal sealed class SmartHomeCapabilityOption
    {
        public string Name { get; init; } = string.Empty;
        public JsonElement Value { get; init; }
    }

    internal sealed class SmartHomeSnapshot
    {
        public required DateTime GeneratedAtUtc { get; init; }
        public required IReadOnlyList<SmartHomeProviderState> Providers { get; init; }
        public int TotalDevices { get; init; }
        public int OnlineDevices { get; init; }
        public int ConfiguredProviders { get; init; }
        public SmartHomeAgentSettingsState AgentSettings { get; init; } = new();
        public IReadOnlyList<SmartHomeSavedGreeting> CustomGreetings { get; init; } = Array.Empty<SmartHomeSavedGreeting>();
        public IReadOnlyList<SmartHomeSavedCommand> CustomCommands { get; init; } = Array.Empty<SmartHomeSavedCommand>();
        public IReadOnlyList<SmartHomeSavedScene> CustomScenes { get; init; } = Array.Empty<SmartHomeSavedScene>();
        public IReadOnlyList<SmartHomeAlertState> Alerts { get; init; } = Array.Empty<SmartHomeAlertState>();
        public IReadOnlyList<SmartHomeAutomationState> Automations { get; init; } = Array.Empty<SmartHomeAutomationState>();
        public SmartHomeSecurityState Security { get; init; } = new();
        public SmartHomeCompanionPairingState CompanionPairing { get; init; } = new();
        public SmartHomeNetworkDiscoveryState NetworkDiscovery { get; init; } = new();
    }

    internal sealed class SmartHomeAlertState
    {
        public string Id { get; init; } = string.Empty;
        public DateTime TimestampUtc { get; init; }
        public string Category { get; init; } = string.Empty;
        public string Severity { get; init; } = string.Empty;
        public string Title { get; init; } = string.Empty;
        public string Detail { get; init; } = string.Empty;
        public string Source { get; init; } = string.Empty;
        public string ProviderId { get; init; } = string.Empty;
        public string DeviceId { get; init; } = string.Empty;
        public bool IsResolved { get; init; }
    }

    internal sealed class SmartHomeAutomationState
    {
        public string Id { get; init; } = string.Empty;
        public string Trigger { get; init; } = string.Empty;
        public IReadOnlyList<string> Actions { get; init; } = Array.Empty<string>();
        public string Schedule { get; init; } = string.Empty;
        public DateTime CreatedAtUtc { get; init; }
        public DateTime? LastTriggeredUtc { get; init; }
        public int TriggerCount { get; init; }
        public bool IsEnabled { get; init; }
    }

    internal sealed class SmartHomeSecurityState
    {
        public string Mode { get; init; } = string.Empty;
        public int ThreatLevel { get; init; }
        public bool IsScanning { get; init; }
        public double ScanProgress { get; init; }
        public int RecentSecurityEventCount { get; init; }
        public int CriticalAlertCount { get; init; }
        public int ActiveCameraCount { get; init; }
        public bool SirenActive { get; init; }
    }

    internal sealed class SmartHomeCompanionPairingState
    {
        public bool IsAvailable { get; init; }
        public string AvailabilityMessage { get; init; } = string.Empty;
        public string BaseUrl { get; init; } = string.Empty;
        public string Protocol { get; init; } = string.Empty;
        public string Host { get; init; } = string.Empty;
        public int Port { get; init; }
        public string DisplayName { get; init; } = string.Empty;
        public string ApiVersion { get; init; } = string.Empty;
        public string PayloadFormat { get; init; } = string.Empty;
        public string Payload { get; init; } = string.Empty;
        public string QrCodeDataUrl { get; init; } = string.Empty;
    }

    internal sealed class SmartHomeNetworkDiscoveryState
    {
        public bool IsScanning { get; init; }
        public DateTime? LastScanUtc { get; init; }
        public string Summary { get; init; } = string.Empty;
        public string LocalIp { get; init; } = string.Empty;
        public string SubnetMask { get; init; } = string.Empty;
        public string Gateway { get; init; } = string.Empty;
        public string DnsServer { get; init; } = string.Empty;
        public string AdapterName { get; init; } = string.Empty;
        public IReadOnlyList<SmartHomeNetworkDeviceState> Devices { get; init; } = Array.Empty<SmartHomeNetworkDeviceState>();
    }

    internal sealed class SmartHomeNetworkDeviceState
    {
        public string IpAddress { get; init; } = string.Empty;
        public string Hostname { get; init; } = string.Empty;
        public string MacAddress { get; init; } = string.Empty;
        public string DeviceType { get; init; } = string.Empty;
        public bool IsOnline { get; init; }
        public int ResponseTime { get; init; }
        public DateTime LastSeenUtc { get; init; }
        public IReadOnlyList<int> OpenPorts { get; init; } = Array.Empty<int>();
        public string PortServices { get; init; } = string.Empty;
        public string Vendor { get; init; } = string.Empty;
    }

    internal sealed class SmartHomeOperationResult
    {
        public bool Ok { get; init; }
        public string Message { get; init; } = string.Empty;
    }

    internal sealed class SmartHomeAgentSettingsState
    {
        public bool VoiceCommandsEnabled { get; init; }
        public bool AnswerDoorEnabled { get; init; }
        public bool ShowDeviceShortcutsInSidebar { get; init; }
        public int DefaultVolumeStep { get; init; }
    }

    internal sealed class SmartHomeSavedCommand
    {
        public string Id { get; init; } = string.Empty;
        public bool Enabled { get; init; }
        public string Phrase { get; init; } = string.Empty;
        public string TargetKind { get; init; } = "device";
        public string TargetScope { get; init; } = string.Empty;
        public string TargetLabel { get; init; } = string.Empty;
        public string ProviderId { get; init; } = string.Empty;
        public string DeviceId { get; init; } = string.Empty;
        public string Sku { get; init; } = string.Empty;
        public string CapabilityType { get; init; } = string.Empty;
        public string CapabilityInstance { get; init; } = string.Empty;
        public JsonElement Value { get; init; }
        public string ResponseText { get; init; } = string.Empty;
        public string DoorbellResponseText { get; init; } = string.Empty;
    }

    internal sealed class SmartHomeSavedScene
    {
        public string Id { get; init; } = string.Empty;
        public bool Enabled { get; init; }
        public string Name { get; init; } = string.Empty;
        public string Phrase { get; init; } = string.Empty;
        public IReadOnlyList<string> PreviewColors { get; init; } = Array.Empty<string>();
        public IReadOnlyList<SmartHomeSceneActionState> Actions { get; init; } = Array.Empty<SmartHomeSceneActionState>();
    }

    internal sealed class SmartHomeSavedGreeting
    {
        public string Id { get; init; } = string.Empty;
        public bool Enabled { get; init; }
        public string Phrase { get; init; } = string.Empty;
        public string ResponseText { get; init; } = string.Empty;
    }
}