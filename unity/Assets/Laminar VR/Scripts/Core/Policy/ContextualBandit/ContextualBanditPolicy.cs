using System;
using LaminarVR.AdaptiveMeditation.Environment;

namespace LaminarVR.AdaptiveMeditation.Policy.ContextualBandit
{
    public sealed class ContextualBanditPolicy : IEnvironmentPolicy
    {
        public const string ContextualBanditPolicyId =
            "ContextualBanditPolicy";
        public const string ImplementationVersion = "0.1.0-draft";
        public const string StateSchemaVersion =
            "contextual-bandit-policy-state/0.1-draft";
        public const string LinUcbSelectionReasonCode =
            "CONTEXTUAL_BANDIT_LINUCB_SELECTION";

        private readonly IFeatureVectorBuilder featureVectorBuilder;
        private readonly IContextualBanditModel model;
        private long decisionCount;
        private long observedOutcomeCount;

        public ContextualBanditPolicy(
            IFeatureVectorBuilder featureVectorBuilder,
            IContextualBanditModel model)
        {
            this.featureVectorBuilder = featureVectorBuilder
                ?? throw new ArgumentNullException(
                    nameof(featureVectorBuilder));
            this.model = model
                ?? throw new ArgumentNullException(nameof(model));
            if (featureVectorBuilder.FeatureCount != model.FeatureCount)
            {
                throw new ArgumentException(
                    "Feature builder and model dimensions do not match.",
                    nameof(model));
            }

            if (!string.Equals(
                featureVectorBuilder.FeatureSchemaVersion,
                model.FeatureSchemaVersion,
                StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "Feature builder and model schemas do not match.",
                    nameof(model));
            }

            PolicyVersion = string.Concat(
                ImplementationVersion,
                "/",
                model.ModelVersion,
                "/",
                model.FeatureSchemaVersion);
        }

        public string PolicyId => ContextualBanditPolicyId;

        public string PolicyVersion { get; }

        public IContextualBanditModel Model => model;

        public LinUcbModelSnapshot CaptureModelSnapshot(
            string snapshotId,
            string participantPseudonym,
            double createdUtcUnixSeconds,
            double updatedUtcUnixSeconds,
            string trainingModelSource)
        {
            return RequireSnapshotPersistence().CaptureSnapshot(
                new LinUcbSnapshotMetadata(
                    snapshotId,
                    participantPseudonym,
                    PolicyId,
                    PolicyVersion,
                    createdUtcUnixSeconds,
                    updatedUtcUnixSeconds,
                    trainingModelSource));
        }

        public LinUcbSnapshotRestoreResult TryRestoreModelSnapshot(
            LinUcbModelSnapshot snapshot,
            string expectedParticipantPseudonym)
        {
            var result = RequireSnapshotPersistence().TryRestoreSnapshot(
                snapshot,
                expectedParticipantPseudonym,
                PolicyId,
                PolicyVersion);
            if (result.Restored)
            {
                observedOutcomeCount = model.TotalUpdateCount;
            }

            return result;
        }

        private ILinUcbModelSnapshotPersistence RequireSnapshotPersistence()
        {
            if (model is ILinUcbModelSnapshotPersistence persistence)
            {
                return persistence;
            }

            throw new NotSupportedException(
                "The configured contextual-bandit model does not support "
                + "the LinUCB snapshot contract.");
        }

        public PolicyDecision SelectAction(PolicyObservation observation)
        {
            if (observation == null)
            {
                throw new ArgumentNullException(nameof(observation));
            }

            if (observation.ActionCandidateCount == 0)
            {
                throw new ArgumentException(
                    "Contextual-bandit observations require safety-filtered "
                    + "action candidates.",
                    nameof(observation));
            }

            var featureVector = featureVectorBuilder.Build(observation);
            var candidates = new ContextualBanditCandidate[
                observation.ActionCandidateCount];
            for (var index = 0;
                index < observation.ActionCandidateCount;
                index++)
            {
                var candidate = observation.GetActionCandidate(index);
                candidates[index] = new ContextualBanditCandidate(
                    candidate.Action,
                    candidate.ActionMagnitude);
            }

            var selection = model.Select(featureVector, candidates);
            var candidateScores = new PolicyCandidateScore[
                selection.CandidateScoreCount];
            for (var index = 0;
                index < selection.CandidateScoreCount;
                index++)
            {
                var score = selection.GetCandidateScore(index);
                candidateScores[index] = new PolicyCandidateScore(
                    score.Action,
                    score.Score,
                    score.StandardError,
                    score.ExpectedReward,
                    score.ExplorationBonus);
            }

            decisionCount = checked(decisionCount + 1L);
            return new PolicyDecision(
                PolicyId,
                PolicyVersion,
                selection.SelectedAction,
                observation.Physiology.SequenceNumber,
                LinUcbSelectionReasonCode,
                selection.Selected.ExpectedReward,
                selection.Selected.StandardError,
                selection.Selected.ExplorationBonus > 0d,
                featureVector,
                candidateScores);
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
                    "The outcome was produced by a different policy model.",
                    nameof(outcome));
            }

            if (outcome.Decision.FeatureVector == null)
            {
                throw new ArgumentException(
                    "A contextual-bandit outcome requires its decision features.",
                    nameof(outcome));
            }

            model.Update(
                outcome.ExecutedAction,
                outcome.Decision.FeatureVector,
                outcome.Reward);
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
                model.TotalUpdateCount);
        }

        public void Reset(PolicyResetContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            model.Reset();
            decisionCount = 0L;
            observedOutcomeCount = 0L;
        }
    }
}
