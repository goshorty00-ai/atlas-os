using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using AtlasAI.AI;
using AtlasAI.Settings;

namespace AtlasAI.Monetization
{
    internal sealed class AtlasEconomyService
    {
        private const int MaxLedgerEntries = 2048;
        private static readonly Lazy<AtlasEconomyService> _instance = new(() => new AtlasEconomyService());
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
        };

        private static readonly string[] AllEntitlements =
        {
            AtlasEconomyEntitlements.BasicChat,
            AtlasEconomyEntitlements.AdvancedTools,
            AtlasEconomyEntitlements.SecurityAnalysis,
            AtlasEconomyEntitlements.MediaCopilot,
            AtlasEconomyEntitlements.AutomationGeneration,
            AtlasEconomyEntitlements.CompanionRemote,
            AtlasEconomyEntitlements.VoiceReplyAudio,
            AtlasEconomyEntitlements.BestQualityMode,
            AtlasEconomyEntitlements.PriorityRouting,
        };

        private static readonly AtlasEconomyPlanDefinition[] PlanCatalog =
        {
            new()
            {
                Id = "free",
                DisplayName = "Atlas Free",
                MonthlyPriceUsd = 0m,
                MonthlyCredits = 150,
                DefaultMode = AtlasEconomyModes.Economy,
                MaxMode = AtlasEconomyModes.Economy,
                Entitlements = new[]
                {
                    AtlasEconomyEntitlements.BasicChat,
                    AtlasEconomyEntitlements.CompanionRemote,
                    AtlasEconomyEntitlements.VoiceReplyAudio,
                },
            },
            new()
            {
                Id = "plus",
                DisplayName = "Atlas Plus",
                MonthlyPriceUsd = 19m,
                MonthlyCredits = 1500,
                DefaultMode = AtlasEconomyModes.Balanced,
                MaxMode = AtlasEconomyModes.Balanced,
                Entitlements = new[]
                {
                    AtlasEconomyEntitlements.BasicChat,
                    AtlasEconomyEntitlements.AdvancedTools,
                    AtlasEconomyEntitlements.SecurityAnalysis,
                    AtlasEconomyEntitlements.MediaCopilot,
                    AtlasEconomyEntitlements.AutomationGeneration,
                    AtlasEconomyEntitlements.CompanionRemote,
                    AtlasEconomyEntitlements.VoiceReplyAudio,
                },
            },
            new()
            {
                Id = "pro",
                DisplayName = "Atlas Pro",
                MonthlyPriceUsd = 49m,
                MonthlyCredits = 5000,
                DefaultMode = AtlasEconomyModes.BestQuality,
                MaxMode = AtlasEconomyModes.BestQuality,
                Entitlements = new[]
                {
                    AtlasEconomyEntitlements.BasicChat,
                    AtlasEconomyEntitlements.AdvancedTools,
                    AtlasEconomyEntitlements.SecurityAnalysis,
                    AtlasEconomyEntitlements.MediaCopilot,
                    AtlasEconomyEntitlements.AutomationGeneration,
                    AtlasEconomyEntitlements.CompanionRemote,
                    AtlasEconomyEntitlements.VoiceReplyAudio,
                    AtlasEconomyEntitlements.BestQualityMode,
                    AtlasEconomyEntitlements.PriorityRouting,
                },
            },
        };

        private static readonly AtlasEconomyPlanDefinition InternalUnlimitedPlan =
            new()
            {
                Id = "internal-unlimited",
                DisplayName = "Atlas Internal",
                MonthlyPriceUsd = 0m,
                MonthlyCredits = 0,
                DefaultMode = AtlasEconomyModes.BestQuality,
                MaxMode = AtlasEconomyModes.BestQuality,
                Entitlements = AllEntitlements,
            };

        private readonly object _sync = new();
        private readonly string _ledgerPath;
        private readonly HashSet<string> _consumedAuthorizationIds = new(StringComparer.OrdinalIgnoreCase);
        private List<AtlasEconomyLedgerEntry> _ledgerEntries;

        public static AtlasEconomyService Instance => _instance.Value;

        private AtlasEconomyService()
        {
            var appData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "AtlasAI");
            Directory.CreateDirectory(appData);
            _ledgerPath = Path.Combine(appData, "economy-ledger.json");
            _ledgerEntries = LoadLedger();
        }

        public IReadOnlyList<AtlasEconomyPlanDefinition> GetPlans() => PlanCatalog;

        public AtlasEconomySnapshot GetSnapshot()
        {
            lock (_sync)
            {
                var state = EnsureStateCurrentLocked();
                return BuildSnapshotLocked(state);
            }
        }

        public IReadOnlyList<AtlasEconomyLedgerEntry> GetLedgerEntries(int limit = 50)
        {
            lock (_sync)
            {
                EnsureStateCurrentLocked();
                return _ledgerEntries
                    .Take(Math.Max(1, limit))
                    .Select(CloneLedgerEntry)
                    .ToArray();
            }
        }

        public IReadOnlyList<AtlasEconomyPlanDefinition> GetPlanCatalog() => PlanCatalog;

        public string ResolveEffectiveCostMode(string fallbackCostMode)
        {
            lock (_sync)
            {
                var state = EnsureStateCurrentLocked();
                var context = AtlasDistributionPolicyService.Instance.GetCurrentContext();
                var plan = GetEffectivePlanDefinition(state, context);
                var requestedMode = NormalizeMode(fallbackCostMode, state.PreferredMode);
                var effectiveMode = ClampModeForPlan(requestedMode, plan);

                if (!string.Equals(state.PreferredMode, effectiveMode, StringComparison.Ordinal))
                {
                    state.PreferredMode = effectiveMode;
                    PersistStateLocked(state);
                }

                return MapModeToCostMode(effectiveMode);
            }
        }

        public AtlasEconomySnapshot SetMode(string requestedMode)
        {
            lock (_sync)
            {
                var state = EnsureStateCurrentLocked();
                var context = AtlasDistributionPolicyService.Instance.GetCurrentContext();
                var plan = GetEffectivePlanDefinition(state, context);
                state.PreferredMode = ClampModeForPlan(requestedMode, plan);
                PersistStateLocked(state);
                return BuildSnapshotLocked(state);
            }
        }

        public AtlasEconomySnapshot ChangePlan(string planId, bool resetCycle, string source)
        {
            lock (_sync)
            {
                var state = EnsureStateCurrentLocked();
                var previousPlan = GetPlanDefinition(state.ActivePlanId);
                var nextPlan = GetPlanDefinition(planId);

                state.ActivePlanId = nextPlan.Id;
                state.SubscriptionStatus = "active";
                state.AutoRenew = true;

                if (resetCycle)
                {
                    var now = DateTime.UtcNow;
                    state.BillingPeriodStartedUtc = now.Date;
                    state.BillingPeriodEndsUtc = now.Date.AddMonths(1);
                    state.IncludedCreditsBalance = nextPlan.MonthlyCredits;
                    state.IncludedCreditsUsedThisPeriod = 0;
                    state.TopUpCreditsUsedThisPeriod = 0;
                    state.LastRenewedAtUtc = now;

                    AppendLedgerEntryLocked(new AtlasEconomyLedgerEntry
                    {
                        Type = "subscription_reset",
                        CreditsDelta = nextPlan.MonthlyCredits,
                        BalanceAfter = state.IncludedCreditsBalance + state.TopUpCreditsBalance,
                        Description = $"Plan changed from {previousPlan.DisplayName} to {nextPlan.DisplayName}.",
                        Reference = source ?? string.Empty,
                        Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["fromPlanId"] = previousPlan.Id,
                            ["toPlanId"] = nextPlan.Id,
                            ["source"] = source ?? string.Empty,
                        },
                    });
                }
                else
                {
                    AppendLedgerEntryLocked(new AtlasEconomyLedgerEntry
                    {
                        Type = "subscription_change",
                        CreditsDelta = 0,
                        BalanceAfter = state.IncludedCreditsBalance + state.TopUpCreditsBalance,
                        Description = $"Plan changed from {previousPlan.DisplayName} to {nextPlan.DisplayName}.",
                        Reference = source ?? string.Empty,
                        Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["fromPlanId"] = previousPlan.Id,
                            ["toPlanId"] = nextPlan.Id,
                            ["source"] = source ?? string.Empty,
                        },
                    });
                }

                state.PreferredMode = ClampModeForPlan(state.PreferredMode, nextPlan);
                PersistStateLocked(state);
                SaveLedgerLocked();
                return BuildSnapshotLocked(state);
            }
        }

        public AtlasEconomySnapshot PurchaseTopUp(int credits, string source, string note)
        {
            if (credits <= 0)
                throw new ArgumentOutOfRangeException(nameof(credits), "Top-up credits must be positive.");

            lock (_sync)
            {
                var state = EnsureStateCurrentLocked();
                state.TopUpCreditsBalance += credits;

                AppendLedgerEntryLocked(new AtlasEconomyLedgerEntry
                {
                    Type = "credit_top_up",
                    CreditsDelta = credits,
                    BalanceAfter = state.IncludedCreditsBalance + state.TopUpCreditsBalance,
                    Description = $"Added {credits} top-up credits.",
                    Reference = source ?? string.Empty,
                    Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["source"] = source ?? string.Empty,
                        ["note"] = note ?? string.Empty,
                    },
                });

                PersistStateLocked(state);
                SaveLedgerLocked();
                return BuildSnapshotLocked(state);
            }
        }

        public AtlasEconomyUsageQuote QuoteUsage(string module, string kind, int units, AIProviderType providerType, string modelId, string? actorId = null)
        {
            lock (_sync)
            {
                var state = EnsureStateCurrentLocked();
                return BuildUsageQuoteLocked(state, module, kind, units, providerType, modelId, actorId);
            }
        }

        public AtlasEconomyUsageAuthorization AuthorizeUsage(string module, string kind, int units, AIProviderType providerType, string modelId, string? actorId = null)
        {
            lock (_sync)
            {
                var state = EnsureStateCurrentLocked();
                var quote = BuildUsageQuoteLocked(state, module, kind, units, providerType, modelId, actorId);
                return new AtlasEconomyUsageAuthorization
                {
                    AuthorizationId = Guid.NewGuid().ToString("N"),
                    AuthorizedAtUtc = DateTime.UtcNow,
                    Module = quote.Module,
                    Kind = quote.Kind,
                    RequestedMode = quote.RequestedMode,
                    EffectiveMode = quote.EffectiveMode,
                    RequiredEntitlement = quote.RequiredEntitlement,
                    Feature = quote.Feature,
                    EstimatedCredits = quote.EstimatedCredits,
                    AvailableCredits = quote.AvailableCredits,
                    DistributionMode = quote.DistributionMode,
                    BillingMode = quote.BillingMode,
                    EffectivePlanId = quote.EffectivePlanId,
                    Disposition = quote.Disposition,
                    ShouldChargeCredits = quote.ShouldChargeCredits,
                    ShouldEnforceFeatureAccess = quote.ShouldEnforceFeatureAccess,
                    ShouldTrackUsage = quote.ShouldTrackUsage,
                    IsShadowCharge = quote.IsShadowCharge,
                    IsAdmin = quote.IsAdmin,
                    IsInternalActor = quote.IsInternalActor,
                    IsSimulatingPublicPlan = quote.IsSimulatingPublicPlan,
                    Allowed = quote.Allowed,
                    Reason = quote.Reason,
                    Provider = providerType.ToString(),
                    Model = modelId ?? string.Empty,
                };
            }
        }

        public void CommitUsage(AtlasEconomyUsageAuthorization authorization)
        {
            if (authorization == null || !authorization.Allowed || !authorization.ShouldTrackUsage)
                return;

            lock (_sync)
            {
                if (!_consumedAuthorizationIds.Add(authorization.AuthorizationId))
                    return;

                var state = EnsureStateCurrentLocked();

                if (!authorization.ShouldChargeCredits)
                {
                    AppendLedgerEntryLocked(new AtlasEconomyLedgerEntry
                    {
                        Type = authorization.IsShadowCharge ? "usage_shadow" : "usage_observed",
                        CreditsDelta = 0,
                        BalanceAfter = state.IncludedCreditsBalance + state.TopUpCreditsBalance,
                        Description = authorization.IsShadowCharge
                            ? $"Shadow-charged {authorization.EstimatedCredits} credits for {authorization.Feature}."
                            : $"Observed {authorization.Feature} usage without charging credits.",
                        Reference = authorization.AuthorizationId,
                        Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["module"] = authorization.Module,
                            ["kind"] = authorization.Kind,
                            ["mode"] = authorization.EffectiveMode,
                            ["provider"] = authorization.Provider,
                            ["model"] = authorization.Model,
                            ["entitlement"] = authorization.RequiredEntitlement,
                            ["distributionMode"] = authorization.DistributionMode,
                            ["billingMode"] = authorization.BillingMode,
                            ["disposition"] = authorization.Disposition,
                        },
                    });

                    SaveLedgerLocked();
                    return;
                }

                if (authorization.EstimatedCredits <= 0)
                    return;

                if (authorization.EstimatedCredits > state.IncludedCreditsBalance + state.TopUpCreditsBalance)
                    throw new InvalidOperationException("Insufficient credits remain to commit the authorized usage.");

                var remaining = authorization.EstimatedCredits;
                var includedApplied = Math.Min(state.IncludedCreditsBalance, remaining);
                state.IncludedCreditsBalance -= includedApplied;
                state.IncludedCreditsUsedThisPeriod += includedApplied;
                remaining -= includedApplied;

                var topUpApplied = Math.Min(state.TopUpCreditsBalance, remaining);
                state.TopUpCreditsBalance -= topUpApplied;
                state.TopUpCreditsUsedThisPeriod += topUpApplied;
                remaining -= topUpApplied;

                if (remaining > 0)
                    throw new InvalidOperationException("Credit accounting drift detected while applying usage charges.");

                state.LifetimeCreditsConsumed += authorization.EstimatedCredits;
                state.LastChargeAtUtc = DateTime.UtcNow;

                AppendLedgerEntryLocked(new AtlasEconomyLedgerEntry
                {
                    Type = "usage_charge",
                    CreditsDelta = -authorization.EstimatedCredits,
                    BalanceAfter = state.IncludedCreditsBalance + state.TopUpCreditsBalance,
                    Description = $"Charged {authorization.EstimatedCredits} credits for {authorization.Feature}.",
                    Reference = authorization.AuthorizationId,
                    Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["module"] = authorization.Module,
                        ["kind"] = authorization.Kind,
                        ["mode"] = authorization.EffectiveMode,
                        ["provider"] = authorization.Provider,
                        ["model"] = authorization.Model,
                        ["entitlement"] = authorization.RequiredEntitlement,
                        ["distributionMode"] = authorization.DistributionMode,
                        ["billingMode"] = authorization.BillingMode,
                        ["disposition"] = authorization.Disposition,
                        ["includedCreditsUsed"] = includedApplied.ToString(),
                        ["topUpCreditsUsed"] = topUpApplied.ToString(),
                    },
                });

                PersistStateLocked(state);
                SaveLedgerLocked();
            }
        }

        private AtlasEconomySnapshot BuildSnapshotLocked(AtlasEconomyRuntimeSettings state)
        {
            var context = AtlasDistributionPolicyService.Instance.GetCurrentContext();
            var plan = GetEffectivePlanDefinition(state, context);
            return new AtlasEconomySnapshot
            {
                GeneratedAtUtc = DateTime.UtcNow,
                DistributionMode = context.DistributionMode,
                BillingMode = context.BillingMode,
                IsInternalBuild = context.IsInternalBuild,
                IsAdmin = context.IsAdmin,
                IsInternalActor = context.IsInternalActor,
                IsSimulatingPublicPlan = context.IsSimulatingPublicPlan,
                ActivePlanId = plan.Id,
                ActivePlanName = plan.DisplayName,
                SubscriptionStatus = state.SubscriptionStatus,
                AutoRenew = state.AutoRenew,
                MonthlyPriceUsd = plan.MonthlyPriceUsd,
                MonthlyCredits = plan.MonthlyCredits,
                BillingPeriodStartedUtc = state.BillingPeriodStartedUtc,
                BillingPeriodEndsUtc = state.BillingPeriodEndsUtc,
                CurrentMode = ClampModeForPlan(state.PreferredMode, plan),
                AvailableModes = GetAvailableModes(plan),
                IncludedCreditsRemaining = state.IncludedCreditsBalance,
                TopUpCreditsRemaining = state.TopUpCreditsBalance,
                TotalCreditsRemaining = state.IncludedCreditsBalance + state.TopUpCreditsBalance,
                CreditsUsedThisPeriod = state.IncludedCreditsUsedThisPeriod + state.TopUpCreditsUsedThisPeriod,
                LifetimeCreditsConsumed = state.LifetimeCreditsConsumed,
                Entitlements = plan.Entitlements.OrderBy(static value => value, StringComparer.Ordinal).ToArray(),
            };
        }

        private AtlasEconomyUsageQuote BuildUsageQuoteLocked(AtlasEconomyRuntimeSettings state, string module, string kind, int units, AIProviderType providerType, string modelId, string? actorId = null)
        {
            var context = AtlasDistributionPolicyService.Instance.GetCurrentContext(actorId);
            var plan = GetEffectivePlanDefinition(state, context);
            var requestedMode = NormalizeMode(SettingsStore.Current.AiRuntime?.CostMode, state.PreferredMode);
            var effectiveMode = ClampModeForPlan(requestedMode, plan);
            var requiredEntitlement = ResolveRequiredEntitlement(module, kind, units);
            var feature = DescribeEntitlement(requiredEntitlement);
            var availableCredits = state.IncludedCreditsBalance + state.TopUpCreditsBalance;
            var estimatedCredits = EstimateCredits(module, kind, units, providerType, modelId, effectiveMode);
            var hasEntitlement = HasEntitlement(plan, requiredEntitlement);
            var hasSufficientCredits = estimatedCredits <= availableCredits;
            var decision = AtlasDistributionPolicyService.Instance.EvaluateRequest(
                context.ActorId,
                plan.Id,
                hasEntitlement,
                hasSufficientCredits);

            if (string.Equals(decision.Disposition, AtlasBillingDispositions.Block, StringComparison.Ordinal))
            {
                return new AtlasEconomyUsageQuote
                {
                    Module = NormalizeModule(module),
                    Kind = NormalizeKind(kind),
                    RequestedMode = requestedMode,
                    EffectiveMode = effectiveMode,
                    RequiredEntitlement = requiredEntitlement,
                    Feature = feature,
                    EstimatedCredits = estimatedCredits,
                    AvailableCredits = availableCredits,
                    DistributionMode = decision.DistributionMode,
                    BillingMode = decision.BillingMode,
                    EffectivePlanId = decision.EffectivePlanId,
                    Disposition = decision.Disposition,
                    ShouldChargeCredits = decision.ShouldChargeCredits,
                    ShouldEnforceFeatureAccess = decision.ShouldEnforceFeatureAccess,
                    ShouldTrackUsage = decision.ShouldTrackUsage,
                    IsShadowCharge = decision.ShouldShadowCharge,
                    IsAdmin = decision.IsAdmin,
                    IsInternalActor = decision.IsInternalActor,
                    IsSimulatingPublicPlan = decision.IsSimulatingPublicPlan,
                    Allowed = false,
                    Reason = decision.Reason,
                };
            }

            return new AtlasEconomyUsageQuote
            {
                Module = NormalizeModule(module),
                Kind = NormalizeKind(kind),
                RequestedMode = requestedMode,
                EffectiveMode = effectiveMode,
                RequiredEntitlement = requiredEntitlement,
                Feature = feature,
                EstimatedCredits = estimatedCredits,
                AvailableCredits = availableCredits,
                DistributionMode = decision.DistributionMode,
                BillingMode = decision.BillingMode,
                EffectivePlanId = decision.EffectivePlanId,
                Disposition = decision.Disposition,
                ShouldChargeCredits = decision.ShouldChargeCredits,
                ShouldEnforceFeatureAccess = decision.ShouldEnforceFeatureAccess,
                ShouldTrackUsage = decision.ShouldTrackUsage,
                IsShadowCharge = decision.ShouldShadowCharge,
                IsAdmin = decision.IsAdmin,
                IsInternalActor = decision.IsInternalActor,
                IsSimulatingPublicPlan = decision.IsSimulatingPublicPlan,
                Allowed = true,
                Reason = decision.Reason,
            };
        }

        private AtlasEconomyRuntimeSettings EnsureStateCurrentLocked()
        {
            var settings = SettingsStore.Current;
            var state = CloneState(settings.Economy);
            var context = AtlasDistributionPolicyService.Instance.GetCurrentContext();
            var configuredPlan = GetConfiguredPlanDefinition(state, context);
            var effectivePlan = GetEffectivePlanDefinition(state, context);
            var now = DateTime.UtcNow;
            var stateChanged = false;
            var ledgerChanged = false;

            var aiMode = NormalizeMode(settings.AiRuntime?.CostMode, state.PreferredMode);
            if (!string.Equals(state.PreferredMode, aiMode, StringComparison.Ordinal))
            {
                state.PreferredMode = aiMode;
                stateChanged = true;
            }

            if (state.BillingPeriodStartedUtc == DateTime.MinValue ||
                state.BillingPeriodEndsUtc <= state.BillingPeriodStartedUtc)
            {
                state.BillingPeriodStartedUtc = now.Date;
                state.BillingPeriodEndsUtc = now.Date.AddMonths(1);
                state.IncludedCreditsBalance = configuredPlan.MonthlyCredits;
                state.IncludedCreditsUsedThisPeriod = 0;
                state.TopUpCreditsUsedThisPeriod = 0;
                state.SubscriptionStatus = "active";
                state.LastRenewedAtUtc = now;
                stateChanged = true;

                AppendLedgerEntryLocked(new AtlasEconomyLedgerEntry
                {
                    Type = "subscription_initialized",
                    CreditsDelta = configuredPlan.MonthlyCredits,
                    BalanceAfter = state.IncludedCreditsBalance + state.TopUpCreditsBalance,
                    Description = $"Initialized {configuredPlan.DisplayName} with {configuredPlan.MonthlyCredits} monthly credits.",
                    Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["planId"] = configuredPlan.Id,
                    },
                });
                ledgerChanged = true;
            }

            while (now >= state.BillingPeriodEndsUtc)
            {
                state.BillingPeriodStartedUtc = state.BillingPeriodEndsUtc;
                state.BillingPeriodEndsUtc = state.BillingPeriodEndsUtc.AddMonths(1);
                state.IncludedCreditsBalance = configuredPlan.MonthlyCredits;
                state.IncludedCreditsUsedThisPeriod = 0;
                state.TopUpCreditsUsedThisPeriod = 0;
                state.SubscriptionStatus = "active";
                state.LastRenewedAtUtc = now;
                stateChanged = true;

                AppendLedgerEntryLocked(new AtlasEconomyLedgerEntry
                {
                    Type = "monthly_renewal",
                    CreditsDelta = configuredPlan.MonthlyCredits,
                    BalanceAfter = state.IncludedCreditsBalance + state.TopUpCreditsBalance,
                    Description = $"Renewed {configuredPlan.DisplayName} for a new billing cycle.",
                    Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["planId"] = configuredPlan.Id,
                        ["periodStartUtc"] = state.BillingPeriodStartedUtc.ToString("O"),
                        ["periodEndUtc"] = state.BillingPeriodEndsUtc.ToString("O"),
                    },
                });
                ledgerChanged = true;
            }

            var clampedMode = ClampModeForPlan(state.PreferredMode, effectivePlan);
            if (!string.Equals(state.PreferredMode, clampedMode, StringComparison.Ordinal))
            {
                state.PreferredMode = clampedMode;
                stateChanged = true;
            }

            if (stateChanged)
                PersistStateLocked(state);
            if (ledgerChanged)
                SaveLedgerLocked();

            return state;
        }

        private void PersistStateLocked(AtlasEconomyRuntimeSettings state)
        {
            SettingsStore.Update(settings =>
            {
                settings.Economy ??= new AtlasEconomyRuntimeSettings();
                settings.Economy.ActivePlanId = state.ActivePlanId;
                settings.Economy.SubscriptionStatus = state.SubscriptionStatus;
                settings.Economy.AutoRenew = state.AutoRenew;
                settings.Economy.PreferredMode = state.PreferredMode;
                settings.Economy.BillingPeriodStartedUtc = state.BillingPeriodStartedUtc;
                settings.Economy.BillingPeriodEndsUtc = state.BillingPeriodEndsUtc;
                settings.Economy.IncludedCreditsBalance = state.IncludedCreditsBalance;
                settings.Economy.TopUpCreditsBalance = state.TopUpCreditsBalance;
                settings.Economy.IncludedCreditsUsedThisPeriod = state.IncludedCreditsUsedThisPeriod;
                settings.Economy.TopUpCreditsUsedThisPeriod = state.TopUpCreditsUsedThisPeriod;
                settings.Economy.LifetimeCreditsConsumed = state.LifetimeCreditsConsumed;
                settings.Economy.LastChargeAtUtc = state.LastChargeAtUtc;
                settings.Economy.LastRenewedAtUtc = state.LastRenewedAtUtc;
                settings.AiRuntime.CostMode = MapModeToCostMode(state.PreferredMode);
            });
        }

        private static AtlasEconomyPlanDefinition GetPlanDefinition(string? planId)
        {
            return PlanCatalog.FirstOrDefault(plan =>
                       string.Equals(plan.Id, planId, StringComparison.OrdinalIgnoreCase))
                   ?? PlanCatalog[0];
        }

        private static AtlasEconomyPlanDefinition GetConfiguredPlanDefinition(AtlasEconomyRuntimeSettings state, AtlasDistributionContext context)
        {
            if (context.IsSimulatingPublicPlan && !string.IsNullOrWhiteSpace(context.SimulatedPlanId))
                return GetPlanDefinition(context.SimulatedPlanId);

            return GetPlanDefinition(state.ActivePlanId);
        }

        private static AtlasEconomyPlanDefinition GetEffectivePlanDefinition(AtlasEconomyRuntimeSettings state, AtlasDistributionContext context)
        {
            if (context.IsInternalBuild && !context.IsSimulatingPublicPlan)
                return InternalUnlimitedPlan;

            return GetConfiguredPlanDefinition(state, context);
        }

        private static string[] GetAvailableModes(AtlasEconomyPlanDefinition plan)
        {
            var modes = new List<string> { AtlasEconomyModes.Economy };
            if (GetModeRank(plan.MaxMode) >= GetModeRank(AtlasEconomyModes.Balanced))
                modes.Add(AtlasEconomyModes.Balanced);
            if (GetModeRank(plan.MaxMode) >= GetModeRank(AtlasEconomyModes.BestQuality))
                modes.Add(AtlasEconomyModes.BestQuality);
            return modes.ToArray();
        }

        private static bool HasEntitlement(AtlasEconomyPlanDefinition plan, string entitlement)
        {
            return plan.Entitlements.Contains(entitlement, StringComparer.OrdinalIgnoreCase);
        }

        private static string ResolveRequiredEntitlement(string module, string kind, int units)
        {
            var normalizedKind = NormalizeKind(kind);
            var normalizedModule = NormalizeModule(module);

            if (string.Equals(normalizedKind, "tts", StringComparison.Ordinal))
                return AtlasEconomyEntitlements.VoiceReplyAudio;

            if (normalizedModule.Contains("security", StringComparison.OrdinalIgnoreCase))
                return AtlasEconomyEntitlements.SecurityAnalysis;

            if (normalizedModule.Contains("media", StringComparison.OrdinalIgnoreCase) ||
                normalizedModule.Contains("lyrics", StringComparison.OrdinalIgnoreCase) ||
                normalizedModule.Contains("servers", StringComparison.OrdinalIgnoreCase))
                return AtlasEconomyEntitlements.MediaCopilot;

            if (normalizedModule.Contains("automation", StringComparison.OrdinalIgnoreCase))
                return AtlasEconomyEntitlements.AutomationGeneration;

            if (normalizedModule.Contains("code", StringComparison.OrdinalIgnoreCase) ||
                normalizedModule.Contains("agent", StringComparison.OrdinalIgnoreCase) ||
                normalizedModule.Contains("repair", StringComparison.OrdinalIgnoreCase) ||
                units >= 1800)
                return AtlasEconomyEntitlements.AdvancedTools;

            return AtlasEconomyEntitlements.BasicChat;
        }

        private static string DescribeEntitlement(string entitlement)
        {
            return entitlement switch
            {
                AtlasEconomyEntitlements.AdvancedTools => "advanced Atlas tools",
                AtlasEconomyEntitlements.SecurityAnalysis => "security analysis",
                AtlasEconomyEntitlements.MediaCopilot => "media copilot",
                AtlasEconomyEntitlements.AutomationGeneration => "automation generation",
                AtlasEconomyEntitlements.CompanionRemote => "companion remote control",
                AtlasEconomyEntitlements.VoiceReplyAudio => "voice reply audio",
                _ => "assistant chat",
            };
        }

        private static int EstimateCredits(string module, string kind, int units, AIProviderType providerType, string modelId, string effectiveMode)
        {
            units = Math.Max(1, units);
            var normalizedKind = NormalizeKind(kind);
            var normalizedModule = NormalizeModule(module);
            var normalizedModel = (modelId ?? string.Empty).Trim().ToLowerInvariant();

            var baseCredits = normalizedKind switch
            {
                "tts" => Math.Max(1, (int)Math.Ceiling(units / 900m)),
                "heavy" => Math.Max(4, units * 4),
                _ when units <= 400 => 1,
                _ when units <= 900 => 2,
                _ when units <= 1800 => 4,
                _ => 6,
            };

            if (normalizedModule.Contains("code", StringComparison.OrdinalIgnoreCase) ||
                normalizedModule.Contains("agent", StringComparison.OrdinalIgnoreCase) ||
                normalizedModule.Contains("repair", StringComparison.OrdinalIgnoreCase) ||
                normalizedModule.Contains("security", StringComparison.OrdinalIgnoreCase))
            {
                baseCredits += 2;
            }

            if (normalizedModel.Contains("opus", StringComparison.Ordinal) ||
                normalizedModel.Contains("pro", StringComparison.Ordinal) ||
                normalizedModel.Contains("sonnet", StringComparison.Ordinal) ||
                (providerType == AIProviderType.OpenAI && normalizedModel.Contains("4o", StringComparison.Ordinal) && !normalizedModel.Contains("mini", StringComparison.Ordinal)))
            {
                baseCredits += 1;
            }

            var multiplier = NormalizeMode(effectiveMode) switch
            {
                AtlasEconomyModes.Economy => 1.0m,
                AtlasEconomyModes.BestQuality => 2.25m,
                _ => 1.5m,
            };

            return Math.Max(1, (int)Math.Ceiling(baseCredits * multiplier));
        }

        private static string NormalizeModule(string? module)
        {
            return string.IsNullOrWhiteSpace(module) ? "conversation" : module.Trim();
        }

        private static string NormalizeKind(string? kind)
        {
            return string.IsNullOrWhiteSpace(kind) ? "chat" : kind.Trim().ToLowerInvariant();
        }

        private static string NormalizeMode(string? value, string? fallback = null)
        {
            return (value ?? fallback ?? AtlasEconomyModes.Balanced).Trim().ToLowerInvariant() switch
            {
                "budget" => AtlasEconomyModes.Economy,
                "cheap" => AtlasEconomyModes.Economy,
                AtlasEconomyModes.Economy => AtlasEconomyModes.Economy,
                "performance" => AtlasEconomyModes.BestQuality,
                "best" => AtlasEconomyModes.BestQuality,
                "bestquality" => AtlasEconomyModes.BestQuality,
                AtlasEconomyModes.BestQuality => AtlasEconomyModes.BestQuality,
                _ => AtlasEconomyModes.Balanced,
            };
        }

        private static string MapModeToCostMode(string mode)
        {
            return NormalizeMode(mode) switch
            {
                AtlasEconomyModes.Economy => "budget",
                AtlasEconomyModes.BestQuality => "performance",
                _ => "balanced",
            };
        }

        private static int GetModeRank(string mode)
        {
            return NormalizeMode(mode) switch
            {
                AtlasEconomyModes.Economy => 0,
                AtlasEconomyModes.BestQuality => 2,
                _ => 1,
            };
        }

        private static string ClampModeForPlan(string? requestedMode, AtlasEconomyPlanDefinition plan)
        {
            var normalizedRequestedMode = NormalizeMode(requestedMode, plan.DefaultMode);
            return GetModeRank(normalizedRequestedMode) <= GetModeRank(plan.MaxMode)
                ? normalizedRequestedMode
                : plan.MaxMode;
        }

        private AtlasEconomyRuntimeSettings CloneState(AtlasEconomyRuntimeSettings? source)
        {
            source ??= new AtlasEconomyRuntimeSettings();
            return new AtlasEconomyRuntimeSettings
            {
                ActivePlanId = string.IsNullOrWhiteSpace(source.ActivePlanId) ? "free" : source.ActivePlanId,
                SubscriptionStatus = string.IsNullOrWhiteSpace(source.SubscriptionStatus) ? "active" : source.SubscriptionStatus,
                AutoRenew = source.AutoRenew,
                PreferredMode = NormalizeMode(source.PreferredMode),
                BillingPeriodStartedUtc = source.BillingPeriodStartedUtc,
                BillingPeriodEndsUtc = source.BillingPeriodEndsUtc,
                IncludedCreditsBalance = Math.Max(0, source.IncludedCreditsBalance),
                TopUpCreditsBalance = Math.Max(0, source.TopUpCreditsBalance),
                IncludedCreditsUsedThisPeriod = Math.Max(0, source.IncludedCreditsUsedThisPeriod),
                TopUpCreditsUsedThisPeriod = Math.Max(0, source.TopUpCreditsUsedThisPeriod),
                LifetimeCreditsConsumed = Math.Max(0, source.LifetimeCreditsConsumed),
                LastChargeAtUtc = source.LastChargeAtUtc,
                LastRenewedAtUtc = source.LastRenewedAtUtc,
            };
        }

        private List<AtlasEconomyLedgerEntry> LoadLedger()
        {
            try
            {
                if (!File.Exists(_ledgerPath))
                    return new List<AtlasEconomyLedgerEntry>();

                var json = File.ReadAllText(_ledgerPath);
                return JsonSerializer.Deserialize<List<AtlasEconomyLedgerEntry>>(json, JsonOptions)
                    ?? new List<AtlasEconomyLedgerEntry>();
            }
            catch
            {
                return new List<AtlasEconomyLedgerEntry>();
            }
        }

        private void SaveLedgerLocked()
        {
            var json = JsonSerializer.Serialize(_ledgerEntries, JsonOptions);
            File.WriteAllText(_ledgerPath, json);
        }

        private void AppendLedgerEntryLocked(AtlasEconomyLedgerEntry entry)
        {
            entry.Id = string.IsNullOrWhiteSpace(entry.Id) ? Guid.NewGuid().ToString("N") : entry.Id;
            entry.CreatedAtUtc = entry.CreatedAtUtc == default ? DateTime.UtcNow : entry.CreatedAtUtc;
            _ledgerEntries.Insert(0, entry);

            if (_ledgerEntries.Count > MaxLedgerEntries)
                _ledgerEntries.RemoveRange(MaxLedgerEntries, _ledgerEntries.Count - MaxLedgerEntries);
        }

        private static AtlasEconomyLedgerEntry CloneLedgerEntry(AtlasEconomyLedgerEntry entry)
        {
            return new AtlasEconomyLedgerEntry
            {
                Id = entry.Id,
                Type = entry.Type,
                CreatedAtUtc = entry.CreatedAtUtc,
                CreditsDelta = entry.CreditsDelta,
                BalanceAfter = entry.BalanceAfter,
                Description = entry.Description,
                Reference = entry.Reference,
                Metadata = new Dictionary<string, string>(entry.Metadata, StringComparer.OrdinalIgnoreCase),
            };
        }
    }
}