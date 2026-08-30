using System;
using System.Collections.Generic;
using LaminarVR.AdaptiveMeditation.Environment;

namespace LaminarVR.AdaptiveMeditation.Stabilization
{
    public sealed class StabilizationSelectionConfiguration
    {
        public StabilizationSelectionConfiguration(
            string configurationId,
            int configurationVersion,
            int recentOutcomeCount,
            double rewardRecencyDecay,
            double preferenceDistancePenaltyWeight)
        {
            if (string.IsNullOrWhiteSpace(configurationId))
            {
                throw new ArgumentException(
                    "Stabilization configuration ID is required.",
                    nameof(configurationId));
            }

            if (configurationVersion < 1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(configurationVersion));
            }

            if (recentOutcomeCount < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(recentOutcomeCount));
            }

            ValidateFiniteRange(
                rewardRecencyDecay,
                double.Epsilon,
                1d,
                nameof(rewardRecencyDecay));
            ValidateFiniteRange(
                preferenceDistancePenaltyWeight,
                0d,
                double.MaxValue,
                nameof(preferenceDistancePenaltyWeight));

            // TODO(RESEARCH_DECISION): Freeze the recent-outcome count,
            // recency decay, and preference-distance penalty before pilot use.
            ConfigurationId = configurationId.Trim();
            ConfigurationVersion = configurationVersion;
            RecentOutcomeCount = recentOutcomeCount;
            RewardRecencyDecay = rewardRecencyDecay;
            PreferenceDistancePenaltyWeight =
                preferenceDistancePenaltyWeight;
        }

        public string ConfigurationId { get; }
        public int ConfigurationVersion { get; }
        public int RecentOutcomeCount { get; }
        public double RewardRecencyDecay { get; }
        public double PreferenceDistancePenaltyWeight { get; }

        private static void ValidateFiniteRange(
            double value,
            double minimum,
            double maximum,
            string parameterName)
        {
            if (double.IsNaN(value)
                || double.IsInfinity(value)
                || value < minimum
                || value > maximum)
            {
                throw new ArgumentOutOfRangeException(parameterName);
            }
        }
    }

    public readonly struct StabilizationOutcome
    {
        public StabilizationOutcome(
            string transitionId,
            long postPhysiologySequenceNumber,
            EnvironmentState state,
            double reward,
            bool discomfortReported,
            bool safetyConcernReported)
        {
            if (string.IsNullOrWhiteSpace(transitionId))
            {
                throw new ArgumentException(
                    "Transition ID is required.",
                    nameof(transitionId));
            }

            if (postPhysiologySequenceNumber < 1L)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(postPhysiologySequenceNumber));
            }

            if (!state.IsNormalized)
            {
                throw new ArgumentException(
                    "Stabilization outcomes must contain normalized state.",
                    nameof(state));
            }

            if (double.IsNaN(reward) || double.IsInfinity(reward))
            {
                throw new ArgumentOutOfRangeException(nameof(reward));
            }

            TransitionId = transitionId.Trim();
            PostPhysiologySequenceNumber = postPhysiologySequenceNumber;
            State = state;
            Reward = reward;
            DiscomfortReported = discomfortReported;
            SafetyConcernReported = safetyConcernReported;
        }

        public string TransitionId { get; }
        public long PostPhysiologySequenceNumber { get; }
        public EnvironmentState State { get; }
        public double Reward { get; }
        public bool DiscomfortReported { get; }
        public bool SafetyConcernReported { get; }
    }

    public enum StabilizationCandidateExclusionReason
    {
        None,
        DiscomfortReported,
        SafetyConcernReported,
        OutsideSafeLimits
    }

    public readonly struct StabilizationCandidateEvaluation
    {
        public StabilizationCandidateEvaluation(
            StabilizationOutcome outcome,
            int recencyIndex,
            double selectionScore,
            StabilizationCandidateExclusionReason exclusionReason)
        {
            Outcome = outcome;
            RecencyIndex = recencyIndex;
            SelectionScore = selectionScore;
            ExclusionReason = exclusionReason;
        }

        public StabilizationOutcome Outcome { get; }
        public int RecencyIndex { get; }
        public double SelectionScore { get; }
        public StabilizationCandidateExclusionReason ExclusionReason { get; }
        public bool Eligible =>
            ExclusionReason == StabilizationCandidateExclusionReason.None;
    }

    public sealed class StabilizationSelectionResult
    {
        private readonly StabilizationCandidateEvaluation[] evaluations;

        public StabilizationSelectionResult(
            EnvironmentState selectedState,
            bool usedPreferenceFallback,
            string selectedTransitionId,
            string reasonCode,
            IReadOnlyList<StabilizationCandidateEvaluation> evaluations)
        {
            SelectedState = selectedState;
            UsedPreferenceFallback = usedPreferenceFallback;
            SelectedTransitionId = selectedTransitionId;
            ReasonCode = reasonCode;
            this.evaluations = new StabilizationCandidateEvaluation[
                evaluations.Count];
            for (var index = 0; index < evaluations.Count; index++)
            {
                this.evaluations[index] = evaluations[index];
            }
        }

        public EnvironmentState SelectedState { get; }
        public bool UsedPreferenceFallback { get; }
        public string SelectedTransitionId { get; }
        public string ReasonCode { get; }
        public int EvaluationCount => evaluations.Length;

        public StabilizationCandidateEvaluation GetEvaluation(int index) =>
            evaluations[index];
    }

    public sealed class StabilizationStateSelector
    {
        public const string BestRecentReasonCode = "BEST_RECENT_SAFE_OUTCOME";
        public const string PreferenceFallbackReasonCode =
            "NO_ELIGIBLE_RECENT_OUTCOME";

        private readonly StabilizationSelectionConfiguration configuration;
        private readonly List<StabilizationOutcome> recentOutcomes;

        public StabilizationStateSelector(
            StabilizationSelectionConfiguration configuration)
        {
            this.configuration = configuration
                ?? throw new ArgumentNullException(nameof(configuration));
            recentOutcomes = new List<StabilizationOutcome>(
                configuration.RecentOutcomeCount);
        }

        public StabilizationSelectionConfiguration Configuration =>
            configuration;

        public int OutcomeCount => recentOutcomes.Count;

        public void RecordOutcome(StabilizationOutcome outcome)
        {
            if (recentOutcomes.Count == configuration.RecentOutcomeCount)
            {
                recentOutcomes.RemoveAt(0);
            }

            recentOutcomes.Add(outcome);
        }

        public StabilizationSelectionResult Select(
            EnvironmentState safePreferenceState,
            EnvironmentStateLimits safeLimits)
        {
            if (!safePreferenceState.IsNormalized
                || !safeLimits.Contains(safePreferenceState))
            {
                throw new ArgumentException(
                    "Preference fallback must be normalized and inside safe limits.",
                    nameof(safePreferenceState));
            }

            var evaluations = new StabilizationCandidateEvaluation[
                recentOutcomes.Count];
            var selectedIndex = -1;
            for (var index = 0; index < recentOutcomes.Count; index++)
            {
                var outcome = recentOutcomes[index];
                var recencyIndex = recentOutcomes.Count - 1 - index;
                var exclusion = DetermineExclusion(outcome, safeLimits);
                var score = CalculateScore(
                    outcome,
                    safePreferenceState,
                    recencyIndex);
                evaluations[index] = new StabilizationCandidateEvaluation(
                    outcome,
                    recencyIndex,
                    score,
                    exclusion);
                if (exclusion == StabilizationCandidateExclusionReason.None
                    && (selectedIndex < 0
                        || IsPreferred(
                            evaluations[index],
                            evaluations[selectedIndex])))
                {
                    selectedIndex = index;
                }
            }

            if (selectedIndex < 0)
            {
                return new StabilizationSelectionResult(
                    safePreferenceState,
                    true,
                    null,
                    PreferenceFallbackReasonCode,
                    evaluations);
            }

            var selected = evaluations[selectedIndex].Outcome;
            return new StabilizationSelectionResult(
                selected.State,
                false,
                selected.TransitionId,
                BestRecentReasonCode,
                evaluations);
        }

        public void Reset()
        {
            recentOutcomes.Clear();
        }

        private double CalculateScore(
            StabilizationOutcome outcome,
            EnvironmentState preference,
            int recencyIndex)
        {
            var recencyWeight = Math.Pow(
                configuration.RewardRecencyDecay,
                recencyIndex);
            return (outcome.Reward * recencyWeight)
                - (configuration.PreferenceDistancePenaltyWeight
                    * outcome.State.EuclideanDistanceTo(preference));
        }

        private static StabilizationCandidateExclusionReason DetermineExclusion(
            StabilizationOutcome outcome,
            EnvironmentStateLimits safeLimits)
        {
            if (outcome.DiscomfortReported)
            {
                return StabilizationCandidateExclusionReason
                    .DiscomfortReported;
            }

            if (outcome.SafetyConcernReported)
            {
                return StabilizationCandidateExclusionReason
                    .SafetyConcernReported;
            }

            return safeLimits.Contains(outcome.State)
                ? StabilizationCandidateExclusionReason.None
                : StabilizationCandidateExclusionReason.OutsideSafeLimits;
        }

        private static bool IsPreferred(
            StabilizationCandidateEvaluation challenger,
            StabilizationCandidateEvaluation incumbent)
        {
            if (challenger.SelectionScore != incumbent.SelectionScore)
            {
                return challenger.SelectionScore > incumbent.SelectionScore;
            }

            return challenger.Outcome.PostPhysiologySequenceNumber
                > incumbent.Outcome.PostPhysiologySequenceNumber;
        }
    }
}
