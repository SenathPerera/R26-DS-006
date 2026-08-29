using System;
using System.Collections.Generic;
using LaminarVR.AdaptiveMeditation.Environment;

namespace LaminarVR.AdaptiveMeditation.Policy.ContextualBandit
{
    public readonly struct ContextualBanditCandidate
    {
        public ContextualBanditCandidate(
            EnvironmentAction action,
            double actionMagnitude)
        {
            ValidateAction(action);
            if (!IsFinite(actionMagnitude)
                || actionMagnitude < 0d
                || actionMagnitude > 1d)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(actionMagnitude));
            }

            if (action == EnvironmentAction.NoChange
                ? actionMagnitude != 0d
                : actionMagnitude <= 0d)
            {
                throw new ArgumentException(
                    "NoChange must have zero magnitude and changing actions "
                    + "must have positive normalized magnitude.",
                    nameof(actionMagnitude));
            }

            Action = action;
            ActionMagnitude = actionMagnitude;
        }

        public EnvironmentAction Action { get; }

        public double ActionMagnitude { get; }

        internal static void ValidateAction(EnvironmentAction action)
        {
            var actionValue = (int)action;
            if (actionValue < (int)EnvironmentAction.NoChange
                || actionValue
                    > (int)EnvironmentAction.DecreaseAmbientMotion)
            {
                throw new ArgumentOutOfRangeException(nameof(action));
            }
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }

    public readonly struct ContextualBanditActionScore
    {
        internal ContextualBanditActionScore(
            ContextualBanditCandidate candidate,
            double expectedReward,
            double standardError,
            double explorationBonus,
            double score)
        {
            Candidate = candidate;
            ExpectedReward = expectedReward;
            StandardError = standardError;
            ExplorationBonus = explorationBonus;
            Score = score;
        }

        public ContextualBanditCandidate Candidate { get; }

        public EnvironmentAction Action => Candidate.Action;

        public double ExpectedReward { get; }

        public double StandardError { get; }

        public double ExplorationBonus { get; }

        public double Score { get; }
    }

    public sealed class ContextualBanditSelection
    {
        private readonly ContextualBanditActionScore[] candidateScores;

        internal ContextualBanditSelection(
            ContextualBanditActionScore selected,
            IReadOnlyList<ContextualBanditActionScore> candidateScores)
        {
            if (candidateScores == null)
            {
                throw new ArgumentNullException(nameof(candidateScores));
            }

            this.candidateScores = new ContextualBanditActionScore[
                candidateScores.Count];
            for (var index = 0; index < candidateScores.Count; index++)
            {
                this.candidateScores[index] = candidateScores[index];
            }

            Selected = selected;
        }

        public ContextualBanditActionScore Selected { get; }

        public EnvironmentAction SelectedAction => Selected.Action;

        public int CandidateScoreCount => candidateScores.Length;

        public ContextualBanditActionScore GetCandidateScore(int index)
        {
            return candidateScores[index];
        }

        public ContextualBanditActionScore[] CopyCandidateScores()
        {
            var copy = new ContextualBanditActionScore[
                candidateScores.Length];
            Array.Copy(candidateScores, copy, candidateScores.Length);
            return copy;
        }
    }
}
