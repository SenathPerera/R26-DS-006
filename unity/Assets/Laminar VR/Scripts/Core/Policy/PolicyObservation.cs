using System;
using System.Collections.Generic;
using LaminarVR.AdaptiveMeditation.Environment;
using LaminarVR.AdaptiveMeditation.Physiology;

namespace LaminarVR.AdaptiveMeditation.Policy
{
    public sealed class PolicyObservation
    {
        private readonly PolicyActionCandidate[] actionCandidates;

        public PolicyObservation(
            PhysiologyWindowSnapshot physiology,
            EnvironmentState preferredEnvironment,
            EnvironmentState currentEnvironment,
            EnvironmentState safeDefaultEnvironment)
            : this(
                physiology,
                preferredEnvironment,
                currentEnvironment,
                safeDefaultEnvironment,
                null,
                null)
        {
        }

        public PolicyObservation(
            PhysiologyWindowSnapshot physiology,
            EnvironmentState preferredEnvironment,
            EnvironmentState currentEnvironment,
            EnvironmentState safeDefaultEnvironment,
            PhysiologyTrendResult? physiologyTrend)
            : this(
                physiology,
                preferredEnvironment,
                currentEnvironment,
                safeDefaultEnvironment,
                physiologyTrend,
                null)
        {
        }

        public PolicyObservation(
            PhysiologyWindowSnapshot physiology,
            EnvironmentState preferredEnvironment,
            EnvironmentState currentEnvironment,
            EnvironmentState safeDefaultEnvironment,
            PhysiologyTrendResult? physiologyTrend,
            IReadOnlyList<PolicyActionCandidate> actionCandidates)
        {
            if (physiology.SequenceNumber < 1L || physiology.Window == null)
            {
                throw new ArgumentException(
                    "A validated physiology window snapshot is required.",
                    nameof(physiology));
            }

            ValidateNormalized(
                preferredEnvironment,
                nameof(preferredEnvironment));
            ValidateNormalized(currentEnvironment, nameof(currentEnvironment));
            ValidateNormalized(
                safeDefaultEnvironment,
                nameof(safeDefaultEnvironment));

            var stress = physiology.Window.Stress;
            if (stress == null
                || !stress.Probabilities.IsFiniteAndInUnitRange
                || !IsFiniteInRange(stress.ContinuousScore, 0d, 3d)
                || !IsFiniteInRange(
                    physiology.Window.SignalQuality,
                    0d,
                    1d))
            {
                throw new ArgumentException(
                    "The physiology snapshot does not contain bounded policy inputs.",
                    nameof(physiology));
            }

            if (physiologyTrend.HasValue
                && physiologyTrend.Value.Available
                && (physiologyTrend.Value.SampleCount < 2
                    || physiologyTrend.Value.LastSequenceNumber
                        != physiology.SequenceNumber
                    || !IsFinite(
                        physiologyTrend.Value.StressScorePerMinute)
                    || !IsFinite(
                        physiologyTrend.Value.HeartRateBpmPerMinute)
                    || (physiologyTrend.Value.RmssdMsPerMinute.HasValue
                        && !IsFinite(
                            physiologyTrend.Value.RmssdMsPerMinute.Value))))
            {
                throw new ArgumentException(
                    "An available trend must end at the observation window.",
                    nameof(physiologyTrend));
            }

            Physiology = physiology;
            PreferredEnvironment = preferredEnvironment;
            CurrentEnvironment = currentEnvironment;
            SafeDefaultEnvironment = safeDefaultEnvironment;
            PhysiologyTrend = physiologyTrend;
            this.actionCandidates = CopyAndValidateCandidates(
                actionCandidates);
        }

        public PhysiologyWindowSnapshot Physiology { get; }

        public EnvironmentState PreferredEnvironment { get; }

        public EnvironmentState CurrentEnvironment { get; }

        public EnvironmentState SafeDefaultEnvironment { get; }

        public PhysiologyTrendResult? PhysiologyTrend { get; }

        public int ActionCandidateCount => actionCandidates.Length;

        public PolicyActionCandidate GetActionCandidate(int index)
        {
            return actionCandidates[index];
        }

        public PolicyActionCandidate[] CopyActionCandidates()
        {
            var copy = new PolicyActionCandidate[actionCandidates.Length];
            Array.Copy(actionCandidates, copy, actionCandidates.Length);
            return copy;
        }

        private static PolicyActionCandidate[] CopyAndValidateCandidates(
            IReadOnlyList<PolicyActionCandidate> candidates)
        {
            if (candidates == null)
            {
                return Array.Empty<PolicyActionCandidate>();
            }

            if (candidates.Count == 0)
            {
                throw new ArgumentException(
                    "An explicit candidate set cannot be empty.",
                    nameof(candidates));
            }

            var actionCount =
                (int)EnvironmentAction.DecreaseAmbientMotion + 1;
            var seen = new bool[actionCount];
            var containsNoChange = false;
            var copy = new PolicyActionCandidate[candidates.Count];
            for (var index = 0; index < candidates.Count; index++)
            {
                var candidate = candidates[index];
                var actionIndex = (int)candidate.Action;
                if (actionIndex < 0
                    || actionIndex >= actionCount
                    || seen[actionIndex])
                {
                    throw new ArgumentException(
                        "Policy candidates must be supported and unique.",
                        nameof(candidates));
                }

                seen[actionIndex] = true;
                containsNoChange |=
                    candidate.Action == EnvironmentAction.NoChange;
                copy[index] = candidate;
            }

            if (!containsNoChange)
            {
                throw new ArgumentException(
                    "NoChange must remain available.",
                    nameof(candidates));
            }

            return copy;
        }

        private static void ValidateNormalized(
            EnvironmentState state,
            string parameterName)
        {
            if (!state.IsNormalized)
            {
                throw new ArgumentException(
                    "Policy environment inputs must be normalized.",
                    parameterName);
            }
        }

        private static bool IsFiniteInRange(
            double value,
            double minimum,
            double maximum)
        {
            return !double.IsNaN(value)
                && !double.IsInfinity(value)
                && value >= minimum
                && value <= maximum;
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}

