using System;
using System.Collections.Generic;

namespace AtlasAI.Monetization
{
    internal static class AtlasEconomyModes
    {
        public const string Economy = "economy";
        public const string Balanced = "balanced";
        public const string BestQuality = "best_quality";
    }

    internal static class AtlasEconomyEntitlements
    {
        public const string BasicChat = "ai.chat.basic";
        public const string AdvancedTools = "ai.chat.advanced";
        public const string SecurityAnalysis = "ai.security.analysis";
        public const string MediaCopilot = "ai.media.copilot";
        public const string AutomationGeneration = "ai.automation.generation";
        public const string CompanionRemote = "ai.companion.remote";
        public const string VoiceReplyAudio = "ai.voice.reply_audio";
        public const string BestQualityMode = "mode.best_quality";
        public const string PriorityRouting = "ai.priority.routing";
    }

    internal sealed class AtlasEconomyPlanDefinition
    {
        public string Id { get; init; } = string.Empty;
        public string DisplayName { get; init; } = string.Empty;
        public decimal MonthlyPriceUsd { get; init; }
        public int MonthlyCredits { get; init; }
        public string DefaultMode { get; init; } = AtlasEconomyModes.Balanced;
        public string MaxMode { get; init; } = AtlasEconomyModes.Balanced;
        public string[] Entitlements { get; init; } = Array.Empty<string>();
    }

    internal sealed class AtlasEconomyLedgerEntry
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Type { get; set; } = string.Empty;
        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
        public int CreditsDelta { get; set; }
        public int BalanceAfter { get; set; }
        public string Description { get; set; } = string.Empty;
        public string Reference { get; set; } = string.Empty;
        public Dictionary<string, string> Metadata { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    internal sealed class AtlasEconomySnapshot
    {
        public DateTime GeneratedAtUtc { get; init; }
        public string DistributionMode { get; init; } = AtlasDistributionModes.Public;
        public string BillingMode { get; init; } = AtlasBillingModes.Enforced;
        public bool IsInternalBuild { get; init; }
        public bool IsAdmin { get; init; }
        public bool IsInternalActor { get; init; }
        public bool IsSimulatingPublicPlan { get; init; }
        public string ActivePlanId { get; init; } = string.Empty;
        public string ActivePlanName { get; init; } = string.Empty;
        public string SubscriptionStatus { get; init; } = string.Empty;
        public bool AutoRenew { get; init; }
        public decimal MonthlyPriceUsd { get; init; }
        public int MonthlyCredits { get; init; }
        public DateTime BillingPeriodStartedUtc { get; init; }
        public DateTime BillingPeriodEndsUtc { get; init; }
        public string CurrentMode { get; init; } = AtlasEconomyModes.Balanced;
        public string[] AvailableModes { get; init; } = Array.Empty<string>();
        public int IncludedCreditsRemaining { get; init; }
        public int TopUpCreditsRemaining { get; init; }
        public int TotalCreditsRemaining { get; init; }
        public int CreditsUsedThisPeriod { get; init; }
        public int LifetimeCreditsConsumed { get; init; }
        public string[] Entitlements { get; init; } = Array.Empty<string>();
    }

    internal class AtlasEconomyUsageQuote
    {
        public string Module { get; init; } = string.Empty;
        public string Kind { get; init; } = string.Empty;
        public string RequestedMode { get; init; } = AtlasEconomyModes.Balanced;
        public string EffectiveMode { get; init; } = AtlasEconomyModes.Balanced;
        public string RequiredEntitlement { get; init; } = AtlasEconomyEntitlements.BasicChat;
        public string Feature { get; init; } = string.Empty;
        public int EstimatedCredits { get; init; }
        public int AvailableCredits { get; init; }
        public string DistributionMode { get; init; } = AtlasDistributionModes.Public;
        public string BillingMode { get; init; } = AtlasBillingModes.Enforced;
        public string EffectivePlanId { get; init; } = string.Empty;
        public string Disposition { get; init; } = AtlasBillingDispositions.Allow;
        public bool ShouldChargeCredits { get; init; }
        public bool ShouldEnforceFeatureAccess { get; init; }
        public bool ShouldTrackUsage { get; init; } = true;
        public bool IsShadowCharge { get; init; }
        public bool IsAdmin { get; init; }
        public bool IsInternalActor { get; init; }
        public bool IsSimulatingPublicPlan { get; init; }
        public bool Allowed { get; init; }
        public string Reason { get; init; } = string.Empty;
    }

    internal sealed class AtlasEconomyUsageAuthorization : AtlasEconomyUsageQuote
    {
        public string AuthorizationId { get; init; } = Guid.NewGuid().ToString("N");
        public DateTime AuthorizedAtUtc { get; init; } = DateTime.UtcNow;
        public string Provider { get; init; } = string.Empty;
        public string Model { get; init; } = string.Empty;
    }
}