using System;
using System.Collections.Generic;
using LaminarVR.AdaptiveMeditation.Environment;

namespace LaminarVR.AdaptiveMeditation.Policy
{
    public readonly struct PolicyCandidateScore
    {
        public PolicyCandidateScore(
            EnvironmentAction action,
            double score,
            double uncertainty)
            : this(
                action,
                score,
                uncertainty,
                null,
                null)
        {
        }

        public PolicyCandidateScore(
            EnvironmentAction action,
            double score,
            double uncertainty,
            double? expectedReward,
            double? explorationBonus)
        {
            if (!Enum.IsDefined(typeof(EnvironmentAction), action))
            {
                throw new ArgumentOutOfRangeException(nameof(action));
            }

            if (!IsFinite(score))
            {
                throw new ArgumentOutOfRangeException(nameof(score));
            }

            if (!IsFinite(uncertainty) || uncertainty < 0d)
            {
                throw new ArgumentOutOfRangeException(nameof(uncertainty));
            }

            ValidateOptionalFinite(
                expectedReward,
                nameof(expectedReward));
            ValidateOptionalFinite(
                explorationBonus,
                nameof(explorationBonus));
            if (explorationBonus.HasValue
                && explorationBonus.Value < 0d)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(explorationBonus));
            }

            Action = action;
            Score = score;
            Uncertainty = uncertainty;
            ExpectedReward = expectedReward;
            ExplorationBonus = explorationBonus;
        }

        public EnvironmentAction Action { get; }

        public double Score { get; }

        public double Uncertainty { get; }

        public double? ExpectedReward { get; }

        public double? ExplorationBonus { get; }

        private static void ValidateOptionalFinite(
            double? value,
            string parameterName)
        {
            if (value.HasValue && !IsFinite(value.Value))
            {
                throw new ArgumentOutOfRangeException(parameterName);
            }
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }

    public sealed class PolicyDecision
    {
        private readonly PolicyCandidateScore[] candidateScores;

        public PolicyDecision(
            string policyId,
            string policyVersion,
            EnvironmentAction selectedAction,
            long physiologySequenceNumber,
            string reasonCode,
            double? expectedReward,
            double? uncertainty,
            bool explorationUsed,
            FeatureVector featureVector,
            IReadOnlyList<PolicyCandidateScore> candidateScores)
        {
            PolicyId = RequireIdentity(policyId, nameof(policyId));
            PolicyVersion = RequireIdentity(
                policyVersion,
                nameof(policyVersion));
            if (!Enum.IsDefined(typeof(EnvironmentAction), selectedAction))
            {
                throw new ArgumentOutOfRangeException(nameof(selectedAction));
            }

            if (physiologySequenceNumber < 1L)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(physiologySequenceNumber));
            }

            if (string.IsNullOrWhiteSpace(reasonCode))
            {
                throw new ArgumentException(
                    "A policy decision reason code is required.",
                    nameof(reasonCode));
            }

            ValidateOptionalFinite(expectedReward, nameof(expectedReward));
            ValidateOptionalFinite(uncertainty, nameof(uncertainty));
            if (uncertainty.HasValue && uncertainty.Value < 0d)
            {
                throw new ArgumentOutOfRangeException(nameof(uncertainty));
            }

            if (candidateScores == null)
            {
                throw new ArgumentNullException(nameof(candidateScores));
            }

            this.candidateScores = new PolicyCandidateScore[
                candidateScores.Count];
            for (var index = 0; index < candidateScores.Count; index++)
            {
                this.candidateScores[index] = candidateScores[index];
            }

            SelectedAction = selectedAction;
            PhysiologySequenceNumber = physiologySequenceNumber;
            ReasonCode = reasonCode.Trim();
            ExpectedReward = expectedReward;
            Uncertainty = uncertainty;
            ExplorationUsed = explorationUsed;
            FeatureVector = featureVector;
        }

        public string PolicyId { get; }

        public string PolicyVersion { get; }

        public EnvironmentAction SelectedAction { get; }

        public long PhysiologySequenceNumber { get; }

        public string ReasonCode { get; }

        public double? ExpectedReward { get; }

        public double? Uncertainty { get; }

        public bool ExplorationUsed { get; }

        public FeatureVector FeatureVector { get; }

        public int CandidateScoreCount => candidateScores.Length;

        public PolicyCandidateScore GetCandidateScore(int index)
        {
            return candidateScores[index];
        }

        public PolicyCandidateScore[] CopyCandidateScores()
        {
            var copy = new PolicyCandidateScore[candidateScores.Length];
            Array.Copy(candidateScores, copy, candidateScores.Length);
            return copy;
        }

        private static void ValidateOptionalFinite(
            double? value,
            string parameterName)
        {
            if (value.HasValue
                && (double.IsNaN(value.Value)
                    || double.IsInfinity(value.Value)))
            {
                throw new ArgumentOutOfRangeException(parameterName);
            }
        }

        private static string RequireIdentity(
            string value,
            string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException(
                    "Policy identity values are required.",
                    parameterName);
            }

            return value.Trim();
        }
    }
}
