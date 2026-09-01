using System;
using LaminarVR.AdaptiveMeditation.Environment;

namespace LaminarVR.AdaptiveMeditation.Policy.RuleBased
{
    public sealed class RuleBasedAdaptivePolicy : IEnvironmentPolicy
    {
        public const string RulePolicyId = "RuleBasedAdaptivePolicy";
        public const string RulePolicyImplementationVersion =
            "0.1.0-draft";
        public const string RuleStateSchemaVersion =
            "rule-based-adaptive-policy-state/1.0";
        public const string MoveTowardPreferenceReasonCode =
            "RULE_MOVE_TOWARD_PREFERENCE";
        public const string TrendUnavailableReasonCode =
            "RULE_STRESS_TREND_UNAVAILABLE";
        public const string ActivationNotMetReasonCode =
            "RULE_ACTIVATION_NOT_MET";
        public const string PreferenceMatchedReasonCode =
            "RULE_PREFERENCE_DELTA_INSUFFICIENT";

        private readonly RuleBasedPolicyConfiguration configuration;
        private long decisionCount;
        private long observedOutcomeCount;

        public RuleBasedAdaptivePolicy(
            RuleBasedPolicyConfiguration configuration)
        {
            this.configuration = configuration
                ?? throw new ArgumentNullException(nameof(configuration));
            PolicyVersion = string.Concat(
                RulePolicyImplementationVersion,
                "/",
                configuration.ConfigurationId,
                "/",
                configuration.ConfigurationVersion);
        }

        public string PolicyId => RulePolicyId;

        public string PolicyVersion { get; }

        public string ConfigurationId => configuration.ConfigurationId;

        public int ConfigurationVersion =>
            configuration.ConfigurationVersion;

        public PolicyDecision SelectAction(PolicyObservation observation)
        {
            if (observation == null)
            {
                throw new ArgumentNullException(nameof(observation));
            }

            decisionCount = checked(decisionCount + 1L);
            if (!TryEvaluateActivation(
                observation,
                out var activated,
                out var unavailableRequiredTrend))
            {
                throw new InvalidOperationException(
                    "The configured activation mode is unsupported.");
            }

            if (unavailableRequiredTrend)
            {
                return CreateDecision(
                    observation,
                    EnvironmentAction.NoChange,
                    TrendUnavailableReasonCode);
            }

            if (!activated)
            {
                return CreateDecision(
                    observation,
                    EnvironmentAction.NoChange,
                    ActivationNotMetReasonCode);
            }

            if (!TrySelectPreferenceDirectedAction(
                observation.PreferredEnvironment,
                observation.CurrentEnvironment,
                configuration.MinimumPreferenceDelta,
                out var action))
            {
                return CreateDecision(
                    observation,
                    EnvironmentAction.NoChange,
                    PreferenceMatchedReasonCode);
            }

            return CreateDecision(
                observation,
                action,
                MoveTowardPreferenceReasonCode);
        }

        public void ObserveOutcome(ActionOutcome outcome)
        {
            if (outcome == null)
            {
                throw new ArgumentNullException(nameof(outcome));
            }

            if (!string.Equals(
                    outcome.Decision.PolicyId,
                    PolicyId,
                    StringComparison.Ordinal)
                || !string.Equals(
                    outcome.Decision.PolicyVersion,
                    PolicyVersion,
                    StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "The outcome was produced by a different policy or rule set.",
                    nameof(outcome));
            }

            observedOutcomeCount = checked(observedOutcomeCount + 1L);
        }

        public PolicyStateSnapshot CaptureState()
        {
            return new PolicyStateSnapshot(
                PolicyId,
                PolicyVersion,
                RuleStateSchemaVersion,
                decisionCount,
                observedOutcomeCount,
                modelUpdateCount: 0L);
        }

        public void Reset(PolicyResetContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            decisionCount = 0L;
            observedOutcomeCount = 0L;
        }

        private bool TryEvaluateActivation(
            PolicyObservation observation,
            out bool activated,
            out bool unavailableRequiredTrend)
        {
            var elevatedStress = observation.Physiology.Window.Stress
                .ContinuousScore
                >= configuration.MinimumContinuousStressScore;
            var trendAvailable = observation.PhysiologyTrend.HasValue
                && observation.PhysiologyTrend.Value.Available;
            var worseningTrend = trendAvailable
                && observation.PhysiologyTrend.Value.StressScorePerMinute
                    >= configuration.MinimumStressIncreasePerMinute;

            unavailableRequiredTrend = false;
            switch (configuration.ActivationMode)
            {
                case RuleActivationMode.WorseningStressTrend:
                    unavailableRequiredTrend = !trendAvailable;
                    activated = trendAvailable && worseningTrend;
                    return true;
                case RuleActivationMode.ElevatedStress:
                    activated = elevatedStress;
                    return true;
                case RuleActivationMode.WorseningTrendOrElevatedStress:
                    unavailableRequiredTrend =
                        !trendAvailable && !elevatedStress;
                    activated = elevatedStress || worseningTrend;
                    return true;
                case RuleActivationMode.WorseningTrendAndElevatedStress:
                    unavailableRequiredTrend = !trendAvailable;
                    activated = elevatedStress && worseningTrend;
                    return true;
                default:
                    activated = false;
                    return false;
            }
        }

        private PolicyDecision CreateDecision(
            PolicyObservation observation,
            EnvironmentAction action,
            string reasonCode)
        {
            return new PolicyDecision(
                PolicyId,
                PolicyVersion,
                action,
                observation.Physiology.SequenceNumber,
                reasonCode,
                expectedReward: null,
                uncertainty: null,
                explorationUsed: false,
                featureVector: null,
                candidateScores: Array.Empty<PolicyCandidateScore>());
        }

        private static bool TrySelectPreferenceDirectedAction(
            EnvironmentState preferred,
            EnvironmentState current,
            double minimumDelta,
            out EnvironmentAction action)
        {
            var bestIndex = -1;
            var bestDifference = 0d;
            var bestMagnitude = 0d;
            ConsiderDifference(
                preferred.Illumination - current.Illumination,
                0,
                minimumDelta,
                ref bestIndex,
                ref bestDifference,
                ref bestMagnitude);
            ConsiderDifference(
                preferred.Warmth - current.Warmth,
                1,
                minimumDelta,
                ref bestIndex,
                ref bestDifference,
                ref bestMagnitude);
            ConsiderDifference(
                preferred.AtmosphericSoftness
                    - current.AtmosphericSoftness,
                2,
                minimumDelta,
                ref bestIndex,
                ref bestDifference,
                ref bestMagnitude);
            ConsiderDifference(
                preferred.ColorRichness - current.ColorRichness,
                3,
                minimumDelta,
                ref bestIndex,
                ref bestDifference,
                ref bestMagnitude);
            ConsiderDifference(
                preferred.AmbientMotion - current.AmbientMotion,
                4,
                minimumDelta,
                ref bestIndex,
                ref bestDifference,
                ref bestMagnitude);

            if (bestIndex < 0)
            {
                action = EnvironmentAction.NoChange;
                return false;
            }

            action = (EnvironmentAction)(1
                + (bestIndex * 2)
                + (bestDifference < 0d ? 1 : 0));
            return true;
        }

        private static void ConsiderDifference(
            double difference,
            int dimensionIndex,
            double minimumDelta,
            ref int bestIndex,
            ref double bestDifference,
            ref double bestMagnitude)
        {
            var magnitude = Math.Abs(difference);
            if (magnitude < minimumDelta
                || (bestIndex >= 0 && magnitude <= bestMagnitude))
            {
                return;
            }

            bestIndex = dimensionIndex;
            bestDifference = difference;
            bestMagnitude = magnitude;
        }
    }
}
