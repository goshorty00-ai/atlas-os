using System;
using System.Collections.Generic;
using System.Linq;
using AtlasAI.Settings;

namespace AtlasAI.Monetization
{
    internal static class AtlasDistributionModes
    {
        public const string Internal = "internal";
        public const string Public = "public";
    }

    internal static class AtlasBillingModes
    {
        public const string Disabled = "disabled";
        public const string Shadow = "shadow";
        public const string Enforced = "enforced";
    }

    internal static class AtlasBillingDispositions
    {
        public const string Allow = "allow";
        public const string ShadowCharge = "shadow_charge";
        public const string Block = "block";
    }

    internal sealed class AtlasDistributionContext
    {
        public string DistributionMode { get; init; } = AtlasDistributionModes.Public;
        public string BillingMode { get; init; } = AtlasBillingModes.Enforced;
        public string ActorId { get; init; } = string.Empty;
        public bool IsInternalBuild { get; init; }
        public bool IsPublicBuild => !IsInternalBuild;
        public bool IsAdmin { get; init; }
        public bool IsInternalActor { get; init; }
        public bool IsSimulatingPublicPlan { get; init; }
        public string SimulatedPlanId { get; init; } = string.Empty;
        public bool ShouldChargeCredits { get; init; }
        public bool ShouldEnforceFeatureAccess { get; init; }
    }

    internal sealed class AtlasPolicyDecision
    {
        public string DistributionMode { get; init; } = AtlasDistributionModes.Public;
        public string BillingMode { get; init; } = AtlasBillingModes.Enforced;
        public string Disposition { get; init; } = AtlasBillingDispositions.Allow;
        public string ActorId { get; init; } = string.Empty;
        public bool IsAdmin { get; init; }
        public bool IsInternalActor { get; init; }
        public bool ShouldChargeCredits { get; init; }
        public bool ShouldEnforceFeatureAccess { get; init; }
        public bool ShouldTrackUsage { get; init; }
        public bool ShouldShadowCharge { get; init; }
        public bool IsSimulatingPublicPlan { get; init; }
        public string EffectivePlanId { get; init; } = string.Empty;
        public string Reason { get; init; } = string.Empty;
    }

    internal sealed class AtlasDistributionPolicyService
    {
        private static readonly Lazy<AtlasDistributionPolicyService> _instance = new(() => new AtlasDistributionPolicyService());

        public static AtlasDistributionPolicyService Instance => _instance.Value;

        private AtlasDistributionPolicyService()
        {
        }

        public string GetBuildDistributionMode()
        {
#if PERSONAL_BUILD
            return AtlasDistributionModes.Internal;
#else
            return AtlasDistributionModes.Public;
#endif
        }

        public string GetDefaultBillingMode()
        {
#if PERSONAL_BUILD
            return AtlasBillingModes.Disabled;
#else
            return AtlasBillingModes.Enforced;
#endif
        }

        public AtlasDistributionContext GetCurrentContext(string? actorId = null)
        {
            var settings = SettingsStore.Current;
            var buildMode = GetBuildDistributionMode();
            var distributionMode = buildMode;
            var billingMode = NormalizeBillingMode(settings.Distribution?.BillingMode, GetDefaultBillingMode());
            var normalizedActorId = NormalizeActorId(actorId);

            var isAdmin = Contains(settings.Distribution?.AdminActors, normalizedActorId);
            var isInternalActor = buildMode == AtlasDistributionModes.Internal
                || Contains(settings.Distribution?.InternalActors, normalizedActorId)
                || (buildMode == AtlasDistributionModes.Internal && settings.Distribution?.TreatCurrentWindowsUserAsInternalAdmin == true &&
                    string.Equals(normalizedActorId, NormalizeActorId(Environment.UserName), StringComparison.OrdinalIgnoreCase));

            var isSimulatingPublicPlan = buildMode == AtlasDistributionModes.Internal && settings.Distribution?.EnablePublicPlanSimulation == true;
            if (isSimulatingPublicPlan)
            {
                distributionMode = AtlasDistributionModes.Public;
                billingMode = NormalizeBillingMode(settings.Distribution?.SimulatedBillingMode, AtlasBillingModes.Shadow);
            }

            return new AtlasDistributionContext
            {
                DistributionMode = distributionMode,
                BillingMode = billingMode,
                ActorId = normalizedActorId,
                IsInternalBuild = buildMode == AtlasDistributionModes.Internal,
                IsAdmin = isAdmin,
                IsInternalActor = isInternalActor,
                IsSimulatingPublicPlan = isSimulatingPublicPlan,
                SimulatedPlanId = settings.Distribution?.SimulatedPlanId ?? string.Empty,
                ShouldChargeCredits = string.Equals(billingMode, AtlasBillingModes.Enforced, StringComparison.Ordinal),
                ShouldEnforceFeatureAccess = string.Equals(billingMode, AtlasBillingModes.Enforced, StringComparison.Ordinal),
            };
        }

        public AtlasPolicyDecision EvaluateRequest(
            string? actorId,
            string effectivePlanId,
            bool hasEntitlement,
            bool hasSufficientCredits)
        {
            var context = GetCurrentContext(actorId);

            if (string.Equals(context.BillingMode, AtlasBillingModes.Disabled, StringComparison.Ordinal))
            {
                return BuildDecision(context, effectivePlanId, AtlasBillingDispositions.Allow, false, false, true,
                    "Billing is disabled for this build flavor.");
            }

            if (context.IsAdmin || context.IsInternalActor)
            {
                return BuildDecision(context, effectivePlanId, AtlasBillingDispositions.ShadowCharge, false, false, true,
                    "Internal/admin override bypassed public enforcement.");
            }

            if (string.Equals(context.BillingMode, AtlasBillingModes.Shadow, StringComparison.Ordinal))
            {
                return BuildDecision(context, effectivePlanId, AtlasBillingDispositions.ShadowCharge, false, false, true,
                    "Billing is in shadow mode; the request is tracked but not enforced.");
            }

            if (!hasEntitlement)
            {
                return BuildDecision(context, effectivePlanId, AtlasBillingDispositions.Block, true, true, true,
                    "The active plan does not include the required entitlement.");
            }

            if (!hasSufficientCredits)
            {
                return BuildDecision(context, effectivePlanId, AtlasBillingDispositions.Block, true, true, true,
                    "The active plan does not have enough credits remaining.");
            }

            return BuildDecision(context, effectivePlanId, AtlasBillingDispositions.Allow, true, true, true,
                "The request is allowed under the active public billing policy.");
        }

        private static AtlasPolicyDecision BuildDecision(
            AtlasDistributionContext context,
            string effectivePlanId,
            string disposition,
            bool shouldChargeCredits,
            bool shouldEnforceFeatureAccess,
            bool shouldTrackUsage,
            string reason)
        {
            return new AtlasPolicyDecision
            {
                DistributionMode = context.DistributionMode,
                BillingMode = context.BillingMode,
                Disposition = disposition,
                ActorId = context.ActorId,
                IsAdmin = context.IsAdmin,
                IsInternalActor = context.IsInternalActor,
                ShouldChargeCredits = shouldChargeCredits,
                ShouldEnforceFeatureAccess = shouldEnforceFeatureAccess,
                ShouldTrackUsage = shouldTrackUsage,
                ShouldShadowCharge = string.Equals(disposition, AtlasBillingDispositions.ShadowCharge, StringComparison.Ordinal),
                IsSimulatingPublicPlan = context.IsSimulatingPublicPlan,
                EffectivePlanId = effectivePlanId,
                Reason = reason,
            };
        }

        private static bool Contains(IEnumerable<string>? values, string actorId)
        {
            if (values == null || string.IsNullOrWhiteSpace(actorId))
                return false;

            return values.Any(value => string.Equals(NormalizeActorId(value), actorId, StringComparison.OrdinalIgnoreCase));
        }

        private static string NormalizeActorId(string? actorId)
        {
            if (string.IsNullOrWhiteSpace(actorId))
                return Environment.UserName.Trim();

            return actorId.Trim();
        }

        private static string NormalizeDistributionMode(string? configuredMode, string fallback)
        {
            return (configuredMode ?? fallback ?? AtlasDistributionModes.Public).Trim().ToLowerInvariant() switch
            {
                AtlasDistributionModes.Internal => AtlasDistributionModes.Internal,
                AtlasDistributionModes.Public => AtlasDistributionModes.Public,
                _ => fallback,
            };
        }

        private static string NormalizeBillingMode(string? configuredMode, string fallback)
        {
            return (configuredMode ?? fallback ?? AtlasBillingModes.Enforced).Trim().ToLowerInvariant() switch
            {
                AtlasBillingModes.Disabled => AtlasBillingModes.Disabled,
                AtlasBillingModes.Shadow => AtlasBillingModes.Shadow,
                AtlasBillingModes.Enforced => AtlasBillingModes.Enforced,
                _ => fallback,
            };
        }
    }
}