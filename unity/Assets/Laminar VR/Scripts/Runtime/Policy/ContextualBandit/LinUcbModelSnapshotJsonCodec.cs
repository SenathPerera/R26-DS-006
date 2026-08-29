using System;
using LaminarVR.AdaptiveMeditation.Environment;
using LaminarVR.AdaptiveMeditation.Policy.ContextualBandit;
using UnityEngine;

namespace LaminarVR.AdaptiveMeditation.Runtime.Policy.ContextualBandit
{
    public sealed class LinUcbModelSnapshotJsonCodec
    {
        public string Serialize(LinUcbModelSnapshot snapshot)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            var dto = new SnapshotDto
            {
                snapshotSchemaVersion = snapshot.SnapshotSchemaVersion,
                snapshotId = snapshot.SnapshotId,
                participantPseudonym = snapshot.ParticipantPseudonym,
                policyId = snapshot.PolicyId,
                policyVersion = snapshot.PolicyVersion,
                modelVersion = snapshot.ModelVersion,
                featureSchemaVersion = snapshot.FeatureSchemaVersion,
                featureCount = snapshot.FeatureCount,
                configurationId = snapshot.ConfigurationId,
                configurationVersion = snapshot.ConfigurationVersion,
                ridgeRegularization = snapshot.RidgeRegularization,
                explorationCoefficient = snapshot.ExplorationCoefficient,
                forgettingEnabled = snapshot.ForgettingEnabled,
                totalUpdateCount = snapshot.TotalUpdateCount,
                createdUtcUnixSeconds = snapshot.CreatedUtcUnixSeconds,
                updatedUtcUnixSeconds = snapshot.UpdatedUtcUnixSeconds,
                trainingModelSource = snapshot.TrainingModelSource,
                actions = new string[snapshot.ActionCount],
                arms = new ArmDto[snapshot.ArmStateCount]
            };

            for (var index = 0; index < snapshot.ActionCount; index++)
            {
                dto.actions[index] = snapshot.GetAction(index).ToString();
            }

            for (var index = 0; index < snapshot.ArmStateCount; index++)
            {
                var state = snapshot.GetArmState(index);
                var matrix = state.CopyDesignMatrix();
                var flattenedMatrix = new double[
                    matrix.GetLength(0) * matrix.GetLength(1)];
                var flattenedIndex = 0;
                for (var row = 0; row < matrix.GetLength(0); row++)
                {
                    for (var column = 0;
                        column < matrix.GetLength(1);
                        column++)
                    {
                        flattenedMatrix[flattenedIndex++] =
                            matrix[row, column];
                    }
                }

                dto.arms[index] = new ArmDto
                {
                    action = state.Action.ToString(),
                    featureCount = state.FeatureCount,
                    updateCount = state.UpdateCount,
                    designMatrixRowMajor = flattenedMatrix,
                    rewardVector = state.CopyRewardVector()
                };
            }

            return JsonUtility.ToJson(dto, false);
        }

        public bool TryDeserialize(
            string json,
            out LinUcbModelSnapshot snapshot,
            out string rejectionReason)
        {
            snapshot = null;
            rejectionReason = null;
            if (string.IsNullOrWhiteSpace(json))
            {
                rejectionReason = "Snapshot JSON is empty.";
                return false;
            }

            try
            {
                var dto = JsonUtility.FromJson<SnapshotDto>(json);
                if (dto == null || dto.actions == null || dto.arms == null)
                {
                    rejectionReason = "Snapshot JSON is incomplete.";
                    return false;
                }

                var actions = new EnvironmentAction[dto.actions.Length];
                for (var index = 0; index < actions.Length; index++)
                {
                    if (!TryParseAction(dto.actions[index], out actions[index]))
                    {
                        rejectionReason = "Snapshot action list is invalid.";
                        return false;
                    }
                }

                var armStates = new LinUcbArmStateSnapshot[dto.arms.Length];
                for (var index = 0; index < dto.arms.Length; index++)
                {
                    var arm = dto.arms[index];
                    if (arm == null
                        || arm.featureCount < 1
                        || arm.designMatrixRowMajor == null
                        || arm.rewardVector == null
                        || arm.designMatrixRowMajor.Length
                            != checked(arm.featureCount * arm.featureCount)
                        || arm.rewardVector.Length != arm.featureCount
                        || !TryParseAction(arm.action, out var action))
                    {
                        rejectionReason = "Snapshot arm data is invalid.";
                        return false;
                    }

                    var matrix = new double[
                        arm.featureCount,
                        arm.featureCount];
                    var flattenedIndex = 0;
                    for (var row = 0; row < arm.featureCount; row++)
                    {
                        for (var column = 0;
                            column < arm.featureCount;
                            column++)
                        {
                            matrix[row, column] =
                                arm.designMatrixRowMajor[flattenedIndex++];
                        }
                    }

                    armStates[index] = new LinUcbArmStateSnapshot(
                        action,
                        matrix,
                        arm.rewardVector,
                        arm.updateCount);
                }

                snapshot = new LinUcbModelSnapshot(
                    dto.snapshotSchemaVersion,
                    dto.snapshotId,
                    dto.participantPseudonym,
                    dto.policyId,
                    dto.policyVersion,
                    dto.modelVersion,
                    dto.featureSchemaVersion,
                    dto.featureCount,
                    dto.configurationId,
                    dto.configurationVersion,
                    dto.ridgeRegularization,
                    dto.explorationCoefficient,
                    dto.forgettingEnabled,
                    dto.totalUpdateCount,
                    dto.createdUtcUnixSeconds,
                    dto.updatedUtcUnixSeconds,
                    dto.trainingModelSource,
                    actions,
                    armStates);
                return true;
            }
            catch (Exception exception) when (
                exception is ArgumentException
                || exception is OverflowException)
            {
                rejectionReason = "Snapshot JSON could not be decoded: "
                    + exception.GetType().Name;
                return false;
            }
        }

        private static bool TryParseAction(
            string value,
            out EnvironmentAction action)
        {
            return Enum.TryParse(value, false, out action)
                && Enum.IsDefined(typeof(EnvironmentAction), action);
        }

        [Serializable]
        private sealed class SnapshotDto
        {
            public string snapshotSchemaVersion;
            public string snapshotId;
            public string participantPseudonym;
            public string policyId;
            public string policyVersion;
            public string modelVersion;
            public string featureSchemaVersion;
            public int featureCount;
            public string configurationId;
            public int configurationVersion;
            public double ridgeRegularization;
            public double explorationCoefficient;
            public bool forgettingEnabled;
            public long totalUpdateCount;
            public double createdUtcUnixSeconds;
            public double updatedUtcUnixSeconds;
            public string trainingModelSource;
            public string[] actions;
            public ArmDto[] arms;
        }

        [Serializable]
        private sealed class ArmDto
        {
            public string action;
            public int featureCount;
            public long updateCount;
            public double[] designMatrixRowMajor;
            public double[] rewardVector;
        }
    }
}
