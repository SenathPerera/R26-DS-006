using System;
using LaminarVR.AdaptiveMeditation.Environment;
using LaminarVR.AdaptiveMeditation.Policy;

namespace LaminarVR.AdaptiveMeditation.ResearchEvaluation
{
    public enum EvaluationPolicyCondition
    {
        StaticPersonalized = 0,
        RuleBasedAdaptive = 1,
        ContextualBandit = 2,
        RandomSafe = 3
    }

    public static class EvaluationPolicyConditionCodec
    {
        public static EvaluationPolicyCondition FromProductionMode(
            StudyPolicyMode mode)
        {
            switch (mode)
            {
                case StudyPolicyMode.StaticPersonalized:
                    return EvaluationPolicyCondition.StaticPersonalized;
                case StudyPolicyMode.RuleBasedAdaptive:
                    return EvaluationPolicyCondition.RuleBasedAdaptive;
                case StudyPolicyMode.ContextualBandit:
                    return EvaluationPolicyCondition.ContextualBandit;
                default:
                    throw new ArgumentOutOfRangeException(nameof(mode));
            }
        }

        public static bool TryGetProductionMode(
            EvaluationPolicyCondition condition,
            out StudyPolicyMode mode)
        {
            switch (condition)
            {
                case EvaluationPolicyCondition.StaticPersonalized:
                    mode = StudyPolicyMode.StaticPersonalized;
                    return true;
                case EvaluationPolicyCondition.RuleBasedAdaptive:
                    mode = StudyPolicyMode.RuleBasedAdaptive;
                    return true;
                case EvaluationPolicyCondition.ContextualBandit:
                    mode = StudyPolicyMode.ContextualBandit;
                    return true;
                case EvaluationPolicyCondition.RandomSafe:
                    mode = default;
                    return false;
                default:
                    throw new ArgumentOutOfRangeException(nameof(condition));
            }
        }
    }

    /// <summary>
    /// Evaluation-only comparator. It samples uniformly from the candidate set
    /// already filtered by the production safety path.
    /// </summary>
    public sealed class RandomSafeEvaluationPolicy : IEnvironmentPolicy
    {
        public const string RandomSafePolicyId =
            "RandomSafeEvaluationComparator";
        public const string RandomSafePolicyVersion = "1.0.0";
        public const string StateSchemaVersion =
            "random-safe-evaluation-policy-state/1.0";
        public const string SelectionReasonCode =
            "EVALUATION_RANDOM_SAFE_UNIFORM";

        private readonly Pcg32 random;
        private long decisionCount;
        private long observedOutcomeCount;

        public RandomSafeEvaluationPolicy(Pcg32 random)
        {
            this.random = random ?? throw new ArgumentNullException(
                nameof(random));
        }

        public string PolicyId => RandomSafePolicyId;

        public string PolicyVersion => RandomSafePolicyVersion;

        public PolicyDecision SelectAction(PolicyObservation observation)
        {
            if (observation == null)
            {
                throw new ArgumentNullException(nameof(observation));
            }

            if (observation.ActionCandidateCount < 1)
            {
                throw new InvalidOperationException(
                    "Random Safe requires the production candidate set.");
            }

            decisionCount = checked(decisionCount + 1L);
            var selected = observation.GetActionCandidate(
                random.NextInt(observation.ActionCandidateCount));
            return new PolicyDecision(
                PolicyId,
                PolicyVersion,
                selected.Action,
                observation.Physiology.SequenceNumber,
                SelectionReasonCode,
                expectedReward: null,
                uncertainty: null,
                explorationUsed: true,
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
                    "The outcome belongs to another policy.",
                    nameof(outcome));
            }

            observedOutcomeCount = checked(observedOutcomeCount + 1L);
        }

        public PolicyStateSnapshot CaptureState()
        {
            return new PolicyStateSnapshot(
                PolicyId,
                PolicyVersion,
                StateSchemaVersion,
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

    internal sealed class EvaluationObservationRecorderPolicy
        : IEnvironmentPolicy
    {
        private readonly IEnvironmentPolicy inner;
        private PolicyObservation latestObservation;

        public EvaluationObservationRecorderPolicy(IEnvironmentPolicy inner)
        {
            this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        public string PolicyId => inner.PolicyId;

        public string PolicyVersion => inner.PolicyVersion;

        public PolicyDecision SelectAction(PolicyObservation observation)
        {
            latestObservation = observation ?? throw new ArgumentNullException(
                nameof(observation));
            return inner.SelectAction(observation);
        }

        public void ObserveOutcome(ActionOutcome outcome)
        {
            inner.ObserveOutcome(outcome);
        }

        public PolicyStateSnapshot CaptureState()
        {
            return inner.CaptureState();
        }

        public void Reset(PolicyResetContext context)
        {
            latestObservation = null;
            inner.Reset(context);
        }

        public bool TryTakeLatestObservation(out PolicyObservation observation)
        {
            observation = latestObservation;
            latestObservation = null;
            return observation != null;
        }
    }
}
