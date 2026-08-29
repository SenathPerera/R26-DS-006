using System;
using System.Collections.Generic;
using LaminarVR.AdaptiveMeditation.Environment;

namespace LaminarVR.AdaptiveMeditation.Policy.ContextualBandit
{
    public sealed class DisjointLinUcbModel
        : IContextualBanditModel, ILinUcbModelSnapshotPersistence
    {
        public const string SnapshotSchemaVersion =
            "disjoint-linucb-model-snapshot/1.0";

        private const int ActionCount =
            (int)EnvironmentAction.DecreaseAmbientMotion + 1;

        private readonly LinUcbModelConfiguration configuration;
        private readonly ArmState[] arms = new ArmState[ActionCount];

        public DisjointLinUcbModel(LinUcbModelConfiguration configuration)
        {
            this.configuration = configuration
                ?? throw new ArgumentNullException(nameof(configuration));
            Reset();
        }

        public string ModelVersion => configuration.ModelVersion;

        public string FeatureSchemaVersion =>
            configuration.FeatureSchemaVersion;

        public int FeatureCount => configuration.FeatureCount;

        public long TotalUpdateCount { get; private set; }

        public LinUcbModelSnapshot CaptureSnapshot(
            LinUcbSnapshotMetadata metadata)
        {
            if (metadata == null)
            {
                throw new ArgumentNullException(nameof(metadata));
            }

            var actions = new EnvironmentAction[ActionCount];
            var states = new LinUcbArmStateSnapshot[ActionCount];
            for (var actionIndex = 0; actionIndex < ActionCount; actionIndex++)
            {
                var action = (EnvironmentAction)actionIndex;
                actions[actionIndex] = action;
                states[actionIndex] = CaptureArmState(action);
            }

            return new LinUcbModelSnapshot(
                SnapshotSchemaVersion,
                metadata.SnapshotId,
                metadata.ParticipantPseudonym,
                metadata.PolicyId,
                metadata.PolicyVersion,
                ModelVersion,
                FeatureSchemaVersion,
                FeatureCount,
                configuration.ConfigurationId,
                configuration.ConfigurationVersion,
                configuration.RidgeRegularization,
                configuration.ExplorationCoefficient,
                false,
                TotalUpdateCount,
                metadata.CreatedUtcUnixSeconds,
                metadata.UpdatedUtcUnixSeconds,
                metadata.TrainingModelSource,
                actions,
                states);
        }

        public LinUcbSnapshotRestoreResult TryRestoreSnapshot(
            LinUcbModelSnapshot snapshot,
            string expectedParticipantPseudonym,
            string expectedPolicyId,
            string expectedPolicyVersion)
        {
            var validation = ValidateSnapshot(
                snapshot,
                expectedParticipantPseudonym,
                expectedPolicyId,
                expectedPolicyVersion,
                out var restoredArms);
            if (!validation.Restored)
            {
                return validation;
            }

            for (var index = 0; index < ActionCount; index++)
            {
                arms[index] = restoredArms[index];
            }

            TotalUpdateCount = snapshot.TotalUpdateCount;
            return validation;
        }

        public ContextualBanditSelection Select(
            FeatureVector featureVector,
            IReadOnlyList<ContextualBanditCandidate> candidates)
        {
            ValidateFeatureVector(featureVector);
            ValidateCandidates(candidates);

            var values = featureVector.ToArray();
            var scores = new ContextualBanditActionScore[candidates.Count];
            var selectedIndex = 0;
            for (var index = 0; index < candidates.Count; index++)
            {
                scores[index] = Score(candidates[index], values);
                if (index > 0
                    && IsPreferred(scores[index], scores[selectedIndex]))
                {
                    selectedIndex = index;
                }
            }

            return new ContextualBanditSelection(
                scores[selectedIndex],
                scores);
        }

        public void Update(
            EnvironmentAction executedAction,
            FeatureVector featureVector,
            double reward)
        {
            ContextualBanditCandidate.ValidateAction(executedAction);
            ValidateFeatureVector(featureVector);
            if (!IsFinite(reward))
            {
                throw new ArgumentOutOfRangeException(nameof(reward));
            }

            var values = featureVector.ToArray();
            var arm = arms[(int)executedAction];
            var updatedMatrix = (double[,])arm.DesignMatrix.Clone();
            var updatedVector = (double[])arm.RewardVector.Clone();
            for (var row = 0; row < FeatureCount; row++)
            {
                var rewardContribution = values[row] * reward;
                EnsureFinite(
                    rewardContribution,
                    "The reward update overflowed.");
                updatedVector[row] += rewardContribution;
                EnsureFinite(
                    updatedVector[row],
                    "The accumulated reward vector is non-finite.");

                for (var column = 0;
                    column < FeatureCount;
                    column++)
                {
                    var designContribution =
                        values[row] * values[column];
                    updatedMatrix[row, column] += designContribution;
                    EnsureFinite(
                        updatedMatrix[row, column],
                        "The accumulated design matrix is non-finite.");
                }
            }

            ValidatePositiveDefinite(updatedMatrix);
            arm.DesignMatrix = updatedMatrix;
            arm.RewardVector = updatedVector;
            arm.UpdateCount++;
            TotalUpdateCount++;
        }

        public LinUcbArmStateSnapshot CaptureArmState(
            EnvironmentAction action)
        {
            ContextualBanditCandidate.ValidateAction(action);
            var arm = arms[(int)action];
            return new LinUcbArmStateSnapshot(
                action,
                arm.DesignMatrix,
                arm.RewardVector,
                arm.UpdateCount);
        }

        public void Reset()
        {
            for (var actionIndex = 0;
                actionIndex < ActionCount;
                actionIndex++)
            {
                var designMatrix = new double[FeatureCount, FeatureCount];
                for (var index = 0; index < FeatureCount; index++)
                {
                    designMatrix[index, index] =
                        configuration.RidgeRegularization;
                }

                arms[actionIndex] = new ArmState(
                    designMatrix,
                    new double[FeatureCount]);
            }

            TotalUpdateCount = 0L;
        }

        private LinUcbSnapshotRestoreResult ValidateSnapshot(
            LinUcbModelSnapshot snapshot,
            string expectedParticipantPseudonym,
            string expectedPolicyId,
            string expectedPolicyVersion,
            out ArmState[] restoredArms)
        {
            restoredArms = null;
            if (snapshot == null)
            {
                return Rejected(
                    LinUcbSnapshotRestoreResultCode.SnapshotMissing,
                    "Snapshot is missing.");
            }

            if (!HasText(snapshot.SnapshotId)
                || !HasText(snapshot.ParticipantPseudonym)
                || !HasText(snapshot.PolicyId)
                || !HasText(snapshot.PolicyVersion)
                || !HasText(snapshot.ConfigurationId)
                || !HasText(snapshot.ModelVersion)
                || !HasText(snapshot.FeatureSchemaVersion)
                || !HasText(snapshot.TrainingModelSource))
            {
                return Rejected(
                    LinUcbSnapshotRestoreResultCode.SnapshotIdentityInvalid,
                    "Snapshot identity metadata is incomplete.");
            }

            if (!string.Equals(
                    snapshot.SnapshotSchemaVersion,
                    SnapshotSchemaVersion,
                    StringComparison.Ordinal))
            {
                return Rejected(
                    LinUcbSnapshotRestoreResultCode.SnapshotSchemaMismatch,
                    "Snapshot schema version is incompatible.");
            }

            if (!string.Equals(
                    snapshot.ParticipantPseudonym,
                    expectedParticipantPseudonym,
                    StringComparison.Ordinal))
            {
                return Rejected(
                    LinUcbSnapshotRestoreResultCode.ParticipantMismatch,
                    "Snapshot participant does not match the active participant.");
            }

            if (!string.Equals(snapshot.PolicyId, expectedPolicyId, StringComparison.Ordinal)
                || !string.Equals(
                    snapshot.PolicyVersion,
                    expectedPolicyVersion,
                    StringComparison.Ordinal))
            {
                return Rejected(
                    LinUcbSnapshotRestoreResultCode.PolicyMismatch,
                    "Snapshot policy identity is incompatible.");
            }

            if (!string.Equals(
                    snapshot.ConfigurationId,
                    configuration.ConfigurationId,
                    StringComparison.Ordinal)
                || snapshot.ConfigurationVersion
                    != configuration.ConfigurationVersion)
            {
                return Rejected(
                    LinUcbSnapshotRestoreResultCode.ConfigurationMismatch,
                    "Snapshot configuration is incompatible.");
            }

            if (!string.Equals(snapshot.ModelVersion, ModelVersion, StringComparison.Ordinal))
            {
                return Rejected(
                    LinUcbSnapshotRestoreResultCode.ModelVersionMismatch,
                    "Snapshot model version is incompatible.");
            }

            if (!string.Equals(
                    snapshot.FeatureSchemaVersion,
                    FeatureSchemaVersion,
                    StringComparison.Ordinal)
                || snapshot.FeatureCount != FeatureCount)
            {
                return Rejected(
                    LinUcbSnapshotRestoreResultCode.FeatureSchemaMismatch,
                    "Snapshot feature schema is incompatible.");
            }

            if (snapshot.RidgeRegularization
                    != configuration.RidgeRegularization
                || snapshot.ExplorationCoefficient
                    != configuration.ExplorationCoefficient)
            {
                return Rejected(
                    LinUcbSnapshotRestoreResultCode.HyperparameterMismatch,
                    "Snapshot hyperparameters are incompatible.");
            }

            if (snapshot.ForgettingEnabled)
            {
                return Rejected(
                    LinUcbSnapshotRestoreResultCode.ForgettingUnsupported,
                    "Forgetting is disabled for the initial implementation.");
            }

            if (!IsTimestamp(snapshot.CreatedUtcUnixSeconds)
                || !IsTimestamp(snapshot.UpdatedUtcUnixSeconds)
                || snapshot.UpdatedUtcUnixSeconds
                    < snapshot.CreatedUtcUnixSeconds)
            {
                return Rejected(
                    LinUcbSnapshotRestoreResultCode.TimestampInvalid,
                    "Snapshot timestamps are invalid.");
            }

            if (snapshot.ActionCount != ActionCount
                || snapshot.ArmStateCount != ActionCount)
            {
                return Rejected(
                    LinUcbSnapshotRestoreResultCode.ActionContractMismatch,
                    "Snapshot action or arm count is incompatible.");
            }

            var candidateArms = new ArmState[ActionCount];
            long calculatedUpdateCount = 0L;
            for (var actionIndex = 0; actionIndex < ActionCount; actionIndex++)
            {
                var expectedAction = (EnvironmentAction)actionIndex;
                var state = snapshot.GetArmState(actionIndex);
                if (snapshot.GetAction(actionIndex) != expectedAction
                    || state == null
                    || state.Action != expectedAction)
                {
                    return Rejected(
                        LinUcbSnapshotRestoreResultCode.ActionContractMismatch,
                        "Snapshot action ordering is incompatible.");
                }

                if (state.FeatureCount != FeatureCount
                    || state.UpdateCount < 0L)
                {
                    return Rejected(
                        LinUcbSnapshotRestoreResultCode.ArmStateInvalid,
                        "Snapshot arm dimensions or counters are invalid.");
                }

                var matrix = state.CopyDesignMatrix();
                var vector = state.CopyRewardVector();
                if (matrix.GetLength(0) != FeatureCount
                    || matrix.GetLength(1) != FeatureCount
                    || vector.Length != FeatureCount)
                {
                    return Rejected(
                        LinUcbSnapshotRestoreResultCode.ArmStateInvalid,
                        "Snapshot arm dimensions are incompatible.");
                }

                for (var row = 0; row < FeatureCount; row++)
                {
                    if (!IsFinite(vector[row]))
                    {
                        return Rejected(
                            LinUcbSnapshotRestoreResultCode.NonFiniteState,
                            "Snapshot reward vector contains a non-finite value.");
                    }

                    for (var column = 0; column < FeatureCount; column++)
                    {
                        if (!IsFinite(matrix[row, column]))
                        {
                            return Rejected(
                                LinUcbSnapshotRestoreResultCode.NonFiniteState,
                                "Snapshot design matrix contains a non-finite value.");
                        }

                        if (Math.Abs(matrix[row, column] - matrix[column, row])
                            > 1e-10d)
                        {
                            return Rejected(
                                LinUcbSnapshotRestoreResultCode.MatrixNotSymmetric,
                                "Snapshot design matrix is not symmetric.");
                        }
                    }
                }

                try
                {
                    ValidatePositiveDefinite(matrix);
                    calculatedUpdateCount = checked(
                        calculatedUpdateCount + state.UpdateCount);
                }
                catch (LinUcbNumericalException)
                {
                    return Rejected(
                        LinUcbSnapshotRestoreResultCode.MatrixNotPositiveDefinite,
                        "Snapshot design matrix is not positive definite.");
                }
                catch (OverflowException)
                {
                    return Rejected(
                        LinUcbSnapshotRestoreResultCode.UpdateCountMismatch,
                        "Snapshot update count overflowed.");
                }

                candidateArms[actionIndex] = new ArmState(matrix, vector)
                {
                    UpdateCount = state.UpdateCount
                };
            }

            if (snapshot.TotalUpdateCount < 0L
                || calculatedUpdateCount != snapshot.TotalUpdateCount)
            {
                return Rejected(
                    LinUcbSnapshotRestoreResultCode.UpdateCountMismatch,
                    "Snapshot update counters are inconsistent.");
            }

            restoredArms = candidateArms;
            return new LinUcbSnapshotRestoreResult(
                LinUcbSnapshotRestoreResultCode.Restored,
                "Snapshot restored.");
        }

        private static LinUcbSnapshotRestoreResult Rejected(
            LinUcbSnapshotRestoreResultCode code,
            string reason)
        {
            return new LinUcbSnapshotRestoreResult(code, reason);
        }

        private static bool HasText(string value)
        {
            return !string.IsNullOrWhiteSpace(value);
        }

        private static bool IsTimestamp(double value)
        {
            return IsFinite(value) && value >= 0d;
        }

        private ContextualBanditActionScore Score(
            ContextualBanditCandidate candidate,
            double[] featureValues)
        {
            var arm = arms[(int)candidate.Action];
            var cholesky = CholeskyDecompose(arm.DesignMatrix);
            var theta = SolveFromCholesky(
                cholesky,
                arm.RewardVector);
            var expectedReward = Dot(featureValues, theta);

            var transformedFeatures = ForwardSubstitute(
                cholesky,
                featureValues);
            var variance = Dot(
                transformedFeatures,
                transformedFeatures);
            EnsureFinite(variance, "LinUCB variance is non-finite.");
            if (variance < 0d)
            {
                throw new LinUcbNumericalException(
                    "LinUCB variance cannot be negative.");
            }

            var standardError = Math.Sqrt(variance);
            var explorationBonus =
                configuration.ExplorationCoefficient * standardError;
            var score = expectedReward + explorationBonus;
            EnsureFinite(expectedReward, "Expected reward is non-finite.");
            EnsureFinite(standardError, "Standard error is non-finite.");
            EnsureFinite(
                explorationBonus,
                "Exploration bonus is non-finite.");
            EnsureFinite(score, "LinUCB score is non-finite.");

            return new ContextualBanditActionScore(
                candidate,
                expectedReward,
                standardError,
                explorationBonus,
                score);
        }

        private void ValidateFeatureVector(FeatureVector featureVector)
        {
            if (featureVector == null)
            {
                throw new ArgumentNullException(nameof(featureVector));
            }

            if (featureVector.Count != FeatureCount)
            {
                throw new ArgumentException(
                    "Feature dimension does not match the model.",
                    nameof(featureVector));
            }

            if (!string.Equals(
                featureVector.SchemaVersion,
                FeatureSchemaVersion,
                StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "Feature schema does not match the model.",
                    nameof(featureVector));
            }

            for (var index = 0; index < featureVector.Count; index++)
            {
                var value = featureVector[index];
                if (!IsFinite(value) || value < -1d || value > 1d)
                {
                    throw new ArgumentException(
                        "LinUCB features must be finite and bounded to [-1, 1].",
                        nameof(featureVector));
                }
            }
        }

        private static void ValidateCandidates(
            IReadOnlyList<ContextualBanditCandidate> candidates)
        {
            if (candidates == null)
            {
                throw new ArgumentNullException(nameof(candidates));
            }

            if (candidates.Count == 0 || candidates.Count > ActionCount)
            {
                throw new ArgumentException(
                    "At least one unique supported candidate is required.",
                    nameof(candidates));
            }

            var seen = new bool[ActionCount];
            var containsNoChange = false;
            for (var index = 0; index < candidates.Count; index++)
            {
                var action = candidates[index].Action;
                ContextualBanditCandidate.ValidateAction(action);
                var actionIndex = (int)action;
                if (seen[actionIndex])
                {
                    throw new ArgumentException(
                        "Candidate actions must be unique.",
                        nameof(candidates));
                }

                seen[actionIndex] = true;
                containsNoChange |= action == EnvironmentAction.NoChange;
            }

            if (!containsNoChange)
            {
                throw new ArgumentException(
                    "NoChange must remain available.",
                    nameof(candidates));
            }
        }

        private static bool IsPreferred(
            ContextualBanditActionScore challenger,
            ContextualBanditActionScore incumbent)
        {
            if (challenger.Score != incumbent.Score)
            {
                return challenger.Score > incumbent.Score;
            }

            if (challenger.Candidate.ActionMagnitude
                != incumbent.Candidate.ActionMagnitude)
            {
                return challenger.Candidate.ActionMagnitude
                    < incumbent.Candidate.ActionMagnitude;
            }

            var challengerNoChange =
                challenger.Action == EnvironmentAction.NoChange;
            var incumbentNoChange =
                incumbent.Action == EnvironmentAction.NoChange;
            if (challengerNoChange != incumbentNoChange)
            {
                return challengerNoChange;
            }

            return (int)challenger.Action < (int)incumbent.Action;
        }

        private static double[,] CholeskyDecompose(double[,] matrix)
        {
            var dimension = matrix.GetLength(0);
            if (dimension < 1 || matrix.GetLength(1) != dimension)
            {
                throw new LinUcbNumericalException(
                    "The design matrix must be non-empty and square.");
            }

            var lower = new double[dimension, dimension];
            for (var row = 0; row < dimension; row++)
            {
                for (var column = 0; column <= row; column++)
                {
                    var value = matrix[row, column];
                    for (var index = 0; index < column; index++)
                    {
                        value -= lower[row, index]
                            * lower[column, index];
                    }

                    EnsureFinite(
                        value,
                        "Cholesky decomposition produced a non-finite value.");
                    if (row == column)
                    {
                        if (value <= 0d)
                        {
                            throw new LinUcbNumericalException(
                                "The design matrix is not positive definite.");
                        }

                        lower[row, column] = Math.Sqrt(value);
                    }
                    else
                    {
                        lower[row, column] = value / lower[column, column];
                        EnsureFinite(
                            lower[row, column],
                            "Cholesky decomposition is non-finite.");
                    }
                }
            }

            return lower;
        }

        private static double[] SolveFromCholesky(
            double[,] lower,
            double[] rightHandSide)
        {
            var intermediate = ForwardSubstitute(lower, rightHandSide);
            var dimension = intermediate.Length;
            var solution = new double[dimension];
            for (var row = dimension - 1; row >= 0; row--)
            {
                var value = intermediate[row];
                for (var column = row + 1;
                    column < dimension;
                    column++)
                {
                    value -= lower[column, row] * solution[column];
                }

                solution[row] = value / lower[row, row];
                EnsureFinite(
                    solution[row],
                    "The linear-system solution is non-finite.");
            }

            return solution;
        }

        private static double[] ForwardSubstitute(
            double[,] lower,
            double[] rightHandSide)
        {
            var dimension = lower.GetLength(0);
            if (rightHandSide.Length != dimension)
            {
                throw new LinUcbNumericalException(
                    "Linear-system dimensions do not match.");
            }

            var solution = new double[dimension];
            for (var row = 0; row < dimension; row++)
            {
                var value = rightHandSide[row];
                for (var column = 0; column < row; column++)
                {
                    value -= lower[row, column] * solution[column];
                }

                solution[row] = value / lower[row, row];
                EnsureFinite(
                    solution[row],
                    "The triangular-system solution is non-finite.");
            }

            return solution;
        }

        private static double Dot(double[] left, double[] right)
        {
            if (left.Length != right.Length)
            {
                throw new LinUcbNumericalException(
                    "Vector dimensions do not match.");
            }

            var result = 0d;
            for (var index = 0; index < left.Length; index++)
            {
                result += left[index] * right[index];
            }

            return result;
        }

        private static void ValidatePositiveDefinite(double[,] matrix)
        {
            CholeskyDecompose(matrix);
        }

        private static void EnsureFinite(double value, string message)
        {
            if (!IsFinite(value))
            {
                throw new LinUcbNumericalException(message);
            }
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private sealed class ArmState
        {
            public ArmState(
                double[,] designMatrix,
                double[] rewardVector)
            {
                DesignMatrix = designMatrix;
                RewardVector = rewardVector;
            }

            public double[,] DesignMatrix { get; set; }

            public double[] RewardVector { get; set; }

            public long UpdateCount { get; set; }
        }
    }
}
