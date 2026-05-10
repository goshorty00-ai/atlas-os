using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace AtlasAI.Settings
{
    internal enum ModelTier
    {
        Cheap,
        Auto,
        Best
    }
    internal enum ConfirmMode
    {
        SingleConfirm,
        DoubleConfirm
    }

    internal sealed class AtlasSettings
    {
        public bool EnableAppLaunch { get; set; } = true;
        public bool EnableOpenFolders { get; set; } = true;
        public bool EnableFindFiles { get; set; } = true;
        public bool EnableOrganizeFiles { get; set; } = false;
        public bool EnableCleanupTemp { get; set; } = false;
        public bool EnableSystemDiagnostics { get; set; } = true;
        public bool EnableWindowsSecurityScan { get; set; } = true;
        public bool EnableCommandExecution { get; set; } = true;
        public bool EnableRegistryActions { get; set; } = false;
        public bool EnableSystem32Actions { get; set; } = false;

        // Skill-style switches
        public bool AllowAppLaunch { get; set; } = true;
        public bool AllowOpenFolder { get; set; } = true;
        public bool AllowSearchFiles { get; set; } = true;
        public bool AllowFileOrganization { get; set; } = true;
        public bool AllowSystemDiagnostics { get; set; } = true;
        public bool AllowSystemFixes { get; set; } = false;
        public bool AllowAutoOptimization { get; set; } = false;
        public bool AllowRegistryAccess { get; set; } = false;
        public bool AllowRegistryEdits { get; set; } = false;
        public bool AllowStartupManagement { get; set; } = true;
        public bool AllowCommandExecution { get; set; } = true;
        public bool AllowSystemFileDeletes { get; set; } = false;
        public bool EnableProactiveHealthMonitoring { get; set; } = false;
        public bool EnableAutoModelSwitching { get; set; } = true;

        public bool SafeImmediate { get; set; } = true;
        public bool RequireConfirmForSafeActions { get; set; } = false;

        public int MonitoringIntervalSeconds { get; set; } = 60;

        public ConfirmMode ConfirmModeNonDestructive { get; set; } = ConfirmMode.SingleConfirm;
        public ConfirmMode ConfirmModeRisky { get; set; } = ConfirmMode.DoubleConfirm;
        public string DoubleConfirmPhrase { get; set; } = "CONFIRM DANGEROUS";

        public int ProfanityRate { get; set; } = 1;
        public bool DebugShowPlanJson { get; set; } = false;

        public bool DebugVerboseLogs { get; set; } = false;

#if PERSONAL_BUILD
        public string PersonalitySelected { get; set; } = "Unfiltered";
#else
        public string PersonalitySelected { get; set; } = "Atlas";
#endif

        // Salutation preferences for Butler personality
        public string SalutationPreference { get; set; } = "auto"; // Options: "auto", "sir", "ma'am", "name", "none"
        public string PreferredName { get; set; } = "";
        public bool SalutationAsked { get; set; } = false;

        // Unfiltered personality settings
        public string UnfilteredStyle { get; set; } = "Casual"; // Options: "Casual", "Banter", "ChaosTesting"
        public bool UnfilteredAllowProfanity { get; set; } = true;
        public bool UnfilteredAllowUserInsults { get; set; } = true;
#if PERSONAL_BUILD
        public int UnfilteredChaosIntensity { get; set; } = 4; // 1-5 scale
#else
        public int UnfilteredChaosIntensity { get; set; } = 3; // 1-5 scale
#endif
        public DateTime UnfilteredChillModeUntil { get; set; } = DateTime.MinValue;
        public List<string> CustomStartupGreetings { get; set; } = new();
        public List<string> CustomChatGreetings { get; set; } = new();
        public List<string> CustomQuickResponses { get; set; } = new();
        public List<SpeechPhraseResponseRule> CustomSpeechRules { get; set; } = new();

        public string RouterBasicModelId { get; set; } = "CheapModel";
        public string RouterAdvancedModelId { get; set; } = "HighModel";
        public int RouterTokenThreshold { get; set; } = 512;
        public int RouterComplexityThreshold { get; set; } = 9;

        public string TtsProvider { get; set; } = "EdgeTTS";
        public double TtsSpeechRate { get; set; } = 1.0;
        public int TtsLeadingSilenceMs { get; set; } = 120;
        public bool TtsWaitForReady { get; set; } = true;

        // Content language preference for media centre (ISO 639-1 code, e.g. "en", "fr", "de", "es", or "any")
        public string PreferredContentLanguage { get; set; } = "en";

        // Read-aloud offer for long responses
        public bool LongResponseReadAloudOfferEnabled { get; set; } = false;  // DISABLED - it reads anyway
        public int LongResponseThresholdChars { get; set; } = 420;
        public bool DisableScreenshotNotifications { get; set; } = false;

        // Model Tiering (Auto Mode)
        public bool ModelAutoModeEnabled { get; set; } = false;
        public string? Tier1Model { get; set; } = "claude-3-haiku-20240307";
        public string? Tier2Model { get; set; } = "claude-3-5-sonnet-20241022";
        public string? Tier3Model { get; set; } = "claude-3-5-sonnet-20241022";
        public decimal MaxCostPerDay { get; set; } = 5.00m;  // Soft cap

        // Execution Policy
        public bool AutoExecuteSafeActions { get; set; } = true;
        public bool MediumRiskRequiresConfirm { get; set; } = true;
        public bool HighRiskRequiresDoubleConfirm { get; set; } = true;
        public bool ProtectedLocationsLock { get; set; } = true;

        // Debug / Telemetry
        public bool DebugLogsEnabled { get; set; } = false;
        public bool ShowSkillMatchBadges { get; set; } = false;  // Dev only

        // First run
        public bool HasSeenFirstRunIntro { get; set; } = false;

        // First-run intro flag
        public bool FirstRunIntroShown { get; set; } = false;

        // Profiles / Proactive
        public bool AllowProactive { get; set; } = false;
        public bool AutoOrganizeDownloads { get; set; } = false;
        public bool AggressivePerformanceMonitoring { get; set; } = false;
        public string ActiveProfile { get; set; } = "Home";
        public ModelTier ModelTier { get; set; } = ModelTier.Auto;
        public AtlasAiRuntimeSettings AiRuntime { get; set; } = new();
        public AtlasEconomyRuntimeSettings Economy { get; set; } = new();
        public AtlasVoiceRuntimeSettings VoiceRuntime { get; set; } = new();
        public AtlasDistributionSettings Distribution { get; set; } = new();
        public Dictionary<string, string> AiProviderKeys { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> VoiceProviderKeys { get; set; } = new(StringComparer.OrdinalIgnoreCase);

                public SmartHomeSettings SmartHome { get; set; } = new();
                public CompanionFileTransferSettings CompanionFileTransfer { get; set; } = new();
    }

        internal sealed class AtlasAiRuntimeSettings
        {
                public string ActiveProvider { get; set; } = AI.AIProviderType.OpenAI.ToString();
                public bool AutoModeEnabled { get; set; } = false;
                public string RoutingMode { get; set; } = "balanced";
                public string CostMode { get; set; } = "balanced";
                public int RouterTokenThreshold { get; set; } = 512;
                public int RouterComplexityThreshold { get; set; } = 9;
                public decimal DailySpendCap { get; set; } = 5.00m;
                public AtlasAiRuntimeUsageSettings Usage { get; set; } = new();
                public Dictionary<string, string> ManualModels { get; set; } = new(StringComparer.OrdinalIgnoreCase);
                public Dictionary<string, AtlasAutoModelSettings> AutoModels { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        }

        internal sealed class AtlasAiRuntimeUsageSettings
        {
                public DateTime UsageDayUtc { get; set; } = DateTime.UtcNow.Date;
                public decimal SpendUsed { get; set; } = 0m;
                public int ChatTokensUsed { get; set; } = 0;
                public int TtsCharactersUsed { get; set; } = 0;
                public int HeavyCallsUsed { get; set; } = 0;
        }

        internal sealed class AtlasAutoModelSettings
        {
                public string Cheap { get; set; } = string.Empty;
                public string Smart { get; set; } = string.Empty;
        }

        internal sealed class AtlasEconomyRuntimeSettings
        {
                public string ActivePlanId { get; set; } = "free";
                public string SubscriptionStatus { get; set; } = "active";
                public bool AutoRenew { get; set; } = true;
                public string PreferredMode { get; set; } = "balanced";
                public DateTime BillingPeriodStartedUtc { get; set; } = DateTime.MinValue;
                public DateTime BillingPeriodEndsUtc { get; set; } = DateTime.MinValue;
                public int IncludedCreditsBalance { get; set; } = 0;
                public int TopUpCreditsBalance { get; set; } = 0;
                public int IncludedCreditsUsedThisPeriod { get; set; } = 0;
                public int TopUpCreditsUsedThisPeriod { get; set; } = 0;
                public int LifetimeCreditsConsumed { get; set; } = 0;
                public DateTime LastChargeAtUtc { get; set; } = DateTime.MinValue;
                public DateTime LastRenewedAtUtc { get; set; } = DateTime.MinValue;
        }

        internal sealed class AtlasVoiceRuntimeSettings
        {
                public string Provider { get; set; } = "ElevenLabs";
                public string VoiceId { get; set; } = string.Empty;
        }

        internal sealed class AtlasDistributionSettings
        {
                public string BillingMode { get; set; } = "enforced";
                public List<string> AdminActors { get; set; } = new();
                public List<string> InternalActors { get; set; } = new();
                public bool TreatCurrentWindowsUserAsInternalAdmin { get; set; } = true;
                public bool EnablePublicPlanSimulation { get; set; } = false;
                public string SimulatedPlanId { get; set; } = "free";
                public string SimulatedBillingMode { get; set; } = "shadow";
        }

        internal sealed class SmartHomeSettings
        {
                public PhilipsHueSettings PhilipsHue { get; set; } = new();
                public GoveeSettings Govee { get; set; } = new();
                public RingSettings Ring { get; set; } = new();
                public LgWebOsSettings LgWebOs { get; set; } = new();
                public SmartThingsSettings SmartThings { get; set; } = new();
                public HomeAssistantSettings HomeAssistant { get; set; } = new();
                public TapoKasaSettings TapoKasa { get; set; } = new();
                public OnvifRtspSettings OnvifRtsp { get; set; } = new();
                public SmartHomeAgentSettings Agent { get; set; } = new();
                public List<SmartHomeGreetingSetting> CustomGreetings { get; set; } = new();
                public List<SmartHomeCustomCommandSetting> CustomCommands { get; set; } = new();
                public List<SmartHomeSceneSetting> CustomScenes { get; set; } = new();
        }

        internal sealed class SpeechPhraseResponseRule
        {
                public string Id { get; set; } = Guid.NewGuid().ToString("N");
                public bool Enabled { get; set; } = true;
                public string Phrase { get; set; } = string.Empty;
                public string ResponseText { get; set; } = string.Empty;
        }

        internal sealed class SmartHomeAgentSettings
        {
                public bool VoiceCommandsEnabled { get; set; } = true;
                public bool AnswerDoorEnabled { get; set; } = true;
                public bool ShowDeviceShortcutsInSidebar { get; set; } = true;
                public int DefaultVolumeStep { get; set; } = 5;
        }

        internal sealed class SmartHomeCustomCommandSetting
        {
                public string Id { get; set; } = Guid.NewGuid().ToString("N");
                public bool Enabled { get; set; } = true;
                public string Phrase { get; set; } = string.Empty;
                public string TargetKind { get; set; } = "device";
                public string TargetScope { get; set; } = string.Empty;
                public string TargetLabel { get; set; } = string.Empty;
                public string ProviderId { get; set; } = string.Empty;
                public string DeviceId { get; set; } = string.Empty;
                public string Sku { get; set; } = string.Empty;
                public string CapabilityType { get; set; } = string.Empty;
                public string CapabilityInstance { get; set; } = string.Empty;
                public string ValueJson { get; set; } = "null";
                public string ResponseText { get; set; } = string.Empty;
                public string DoorbellResponseText { get; set; } = string.Empty;
        }

        internal sealed class SmartHomeGreetingSetting
        {
                public string Id { get; set; } = Guid.NewGuid().ToString("N");
                public bool Enabled { get; set; } = true;
                public string Phrase { get; set; } = string.Empty;
                public string ResponseText { get; set; } = string.Empty;
        }

        internal sealed class SmartHomeSceneSetting
        {
                public string Id { get; set; } = Guid.NewGuid().ToString("N");
                public bool Enabled { get; set; } = true;
                public string Name { get; set; } = string.Empty;
                public string Phrase { get; set; } = string.Empty;
                public List<string> PreviewColors { get; set; } = new();
                public List<SmartHomeSceneActionSetting> Actions { get; set; } = new();
        }

        internal sealed class SmartHomeSceneActionSetting
        {
                public string ProviderId { get; set; } = string.Empty;
                public string DeviceId { get; set; } = string.Empty;
                public string DeviceName { get; set; } = string.Empty;
                public string Sku { get; set; } = string.Empty;
                public string CapabilityType { get; set; } = string.Empty;
                public string CapabilityInstance { get; set; } = string.Empty;
                public string ValueJson { get; set; } = "null";
                public string HexColor { get; set; } = string.Empty;
        }

        internal sealed class CompanionFileTransferSettings
        {
                public bool UseDedicatedAccount { get; set; } = false;
                public string DedicatedUsername { get; set; } = "atlas_sftp";
                public string DedicatedFolderPath { get; set; } = string.Empty;
                public string VncHost { get; set; } = string.Empty;
                public int VncPort { get; set; } = 5900;
                public string VncPassword { get; set; } = string.Empty;
                public string RemoteHost { get; set; } = string.Empty;
                public int RemotePort { get; set; } = 3000;
                public bool RemoteUseTls { get; set; } = false;
        }

        internal sealed class PhilipsHueSettings
        {
                public bool Enabled { get; set; } = false;

                [JsonPropertyName("bridge_ip")]
                public string BridgeIp { get; set; } = string.Empty;

                [JsonPropertyName("application_key")]
                public string ApplicationKey { get; set; } = string.Empty;
        }

        internal sealed class GoveeSettings
        {
                public bool Enabled { get; set; } = false;

                [JsonPropertyName("api_key")]
                public string ApiKey { get; set; } = string.Empty;
        }

        internal sealed class RingSettings
        {
                public bool Enabled { get; set; } = false;

                [JsonPropertyName("refresh_token")]
                public string RefreshToken { get; set; } = string.Empty;
        }

        internal sealed class LgWebOsSettings
        {
                public bool Enabled { get; set; } = false;

                [JsonPropertyName("host")]
                public string Host { get; set; } = string.Empty;

                [JsonPropertyName("client_key")]
                public string ClientKey { get; set; } = string.Empty;

                [JsonPropertyName("mac_address")]
                public string MacAddress { get; set; } = string.Empty;
        }

        internal sealed class SmartThingsSettings
        {
                public bool Enabled { get; set; } = false;

                [JsonPropertyName("access_token")]
                public string AccessToken { get; set; } = string.Empty;

                [JsonPropertyName("location_id")]
                public string LocationId { get; set; } = string.Empty;
        }

        internal sealed class HomeAssistantSettings
        {
                public bool Enabled { get; set; } = false;

                [JsonPropertyName("base_url")]
                public string BaseUrl { get; set; } = string.Empty;

                [JsonPropertyName("access_token")]
                public string AccessToken { get; set; } = string.Empty;
        }

        internal sealed class TapoKasaSettings
        {
                public bool Enabled { get; set; } = false;

                [JsonPropertyName("host")]
                public string Host { get; set; } = string.Empty;

                [JsonPropertyName("username")]
                public string Username { get; set; } = string.Empty;

                [JsonPropertyName("password")]
                public string Password { get; set; } = string.Empty;
        }

        internal sealed class OnvifRtspSettings
        {
                public bool Enabled { get; set; } = false;

                [JsonPropertyName("host")]
                public string Host { get; set; } = string.Empty;

                [JsonPropertyName("username")]
                public string Username { get; set; } = string.Empty;

                [JsonPropertyName("password")]
                public string Password { get; set; } = string.Empty;

                [JsonPropertyName("rtsp_url")]
                public string RtspUrl { get; set; } = string.Empty;
        }
}
