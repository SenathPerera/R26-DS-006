using System;
using LaminarVR.AdaptiveMeditation.Environment;

namespace LaminarVR.AdaptiveMeditation.Policy.Static
{
    public sealed class StaticPersonalizedPolicy : IEnvironmentPolicy
    {
        public const string StaticPolicyId = "StaticPersonalizedPolicy";
        public const string StaticPolicyVersion = "1.0.0";
        public const string StaticStateSchemaVersion =
            "static-personalized-policy-state/1.0";
        public const string NoAdaptationReasonCode =
            "STATIC_PERSONALIZED_NO_ADAPTATION";

        private long decisionCount;
        private long observedOutcomeCount;

        public string PolicyId => StaticPolicyId;

        public string PolicyVersion => StaticPolicyVersion;

        public PolicyDecision SelectAction(PolicyObservation observation)
        {
            if (observation == null)
            {
                throw new ArgumentNullException(nameof(observation));
            }

            decisionCount = checked(decisionCount + 1L);
            return new PolicyDecision(
                PolicyId,
                PolicyVersion,
                EnvironmentAction.NoChange,
                observation.Physiology.SequenceNumber,
                NoAdaptationReasonCode,
                expectedReward: null,
                uncertainty: null,
                explorationUsed: false,
                featureVector: null,
                candidateScores: Array.Empty<PolicyCandidateScore>());
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
                    "The outcome was produced by a different policy.",
                    nameof(outcome));
            }

            if (outcome.Decision.SelectedAction
                    != EnvironmentAction.NoChange
                || outcome.ExecutedAction != EnvironmentAction.NoChange)
            {
                throw new ArgumentException(
                    "Static personalized outcomes must not change the environment.",
                    nameof(outcome));
            }

            observedOutcomeCount = checked(observedOutcomeCount + 1L);
        }

        public PolicyStateSnapshot CaptureState()
        {
            return new PolicyStateSnapshot(
                PolicyId,
                PolicyVersion,
                StaticStateSchemaVersion,
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
    }
}
