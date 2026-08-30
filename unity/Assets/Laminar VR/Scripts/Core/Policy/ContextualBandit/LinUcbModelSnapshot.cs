using System;
using System.Collections.Generic;
using LaminarVR.AdaptiveMeditation.Environment;

namespace LaminarVR.AdaptiveMeditation.Policy.ContextualBandit
{
    public interface ILinUcbModelSnapshotPersistence
    {
        LinUcbModelSnapshot CaptureSnapshot(LinUcbSnapshotMetadata metadata);

        LinUcbSnapshotRestoreResult TryRestoreSnapshot(
            LinUcbModelSnapshot snapshot,
            string expectedParticipantPseudonym,
            string expectedPolicyId,
            string expectedPolicyVersion);
    }

    public sealed class LinUcbSnapshotMetadata
    {
        public LinUcbSnapshotMetadata(
            string snapshotId,
            string participantPseudonym,
            string policyId,
            string policyVersion,
            double createdUtcUnixSeconds,
            double updatedUtcUnixSeconds,
            string trainingModelSource)
        {
            SnapshotId = RequireText(snapshotId, nameof(snapshotId));
            ParticipantPseudonym = RequireText(
                participantPseudonym,
                nameof(participantPseudonym));
            PolicyId = RequireText(policyId, nameof(policyId));
            PolicyVersion = RequireText(policyVersion, nameof(policyVersion));
            ValidateTimestamp(
                createdUtcUnixSeconds,
                nameof(createdUtcUnixSeconds));
            ValidateTimestamp(
                updatedUtcUnixSeconds,
                nameof(updatedUtcUnixSeconds));
            if (updatedUtcUnixSeconds < createdUtcUnixSeconds)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(updatedUtcUnixSeconds));
            }

            TrainingModelSource = RequireText(
                trainingModelSource,
                nameof(trainingModelSource));
            CreatedUtcUnixSeconds = createdUtcUnixSeconds;
            UpdatedUtcUnixSeconds = updatedUtcUnixSeconds;
        }

        public string SnapshotId { get; }

        public string ParticipantPseudonym { get; }

        public string PolicyId { get; }

        public string PolicyVersion { get; }

        public double CreatedUtcUnixSeconds { get; }

        public double UpdatedUtcUnixSeconds { get; }

        public string TrainingModelSource { get; }

        private static string RequireText(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException(
                    "Snapshot identity values are required.",
                    parameterName);
            }

            return value.Trim();
        }

        private static void ValidateTimestamp(double value, string parameterName)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value < 0d)
            {
                throw new ArgumentOutOfRangeException(parameterName);
            }
        }
    }

    public sealed class LinUcbModelSnapshot
    {
        private readonly EnvironmentAction[] actions;
        private readonly LinUcbArmStateSnapshot[] armStates;

        public LinUcbModelSnapshot(
            string snapshotSchemaVersion,
            string snapshotId,
            string participantPseudonym,
            string policyId,
            string policyVersion,
            string modelVersion,
            string featureSchemaVersion,
            int featureCount,
            string configurationId,
            int configurationVersion,
            double ridgeRegularization,
            double explorationCoefficient,
            bool forgettingEnabled,
            long totalUpdateCount,
            double createdUtcUnixSeconds,
            double updatedUtcUnixSeconds,
            string trainingModelSource,
            IReadOnlyList<EnvironmentAction> actions,
            IReadOnlyList<LinUcbArmStateSnapshot> armStates)
        {
            SnapshotSchemaVersion = snapshotSchemaVersion;
            SnapshotId = snapshotId;
            ParticipantPseudonym = participantPseudonym;
            PolicyId = policyId;
            PolicyVersion = policyVersion;
            ModelVersion = modelVersion;
            FeatureSchemaVersion = featureSchemaVersion;
            FeatureCount = featureCount;
            ConfigurationId = configurationId;
            ConfigurationVersion = configurationVersion;
            RidgeRegularization = ridgeRegularization;
            ExplorationCoefficient = explorationCoefficient;
            ForgettingEnabled = forgettingEnabled;
            TotalUpdateCount = totalUpdateCount;
            CreatedUtcUnixSeconds = createdUtcUnixSeconds;
            UpdatedUtcUnixSeconds = updatedUtcUnixSeconds;
            TrainingModelSource = trainingModelSource;
            this.actions = Copy(actions, nameof(actions));
            this.armStates = Copy(armStates, nameof(armStates));
        }

        public string SnapshotSchemaVersion { get; }
        public string SnapshotId { get; }
        public string ParticipantPseudonym { get; }
        public string PolicyId { get; }
        public string PolicyVersion { get; }
        public string ModelVersion { get; }
        public string FeatureSchemaVersion { get; }
        public int FeatureCount { get; }
        public string ConfigurationId { get; }
        public int ConfigurationVersion { get; }
        public double RidgeRegularization { get; }
        public double ExplorationCoefficient { get; }
        public bool ForgettingEnabled { get; }
        public long TotalUpdateCount { get; }
        public double CreatedUtcUnixSeconds { get; }
        public double UpdatedUtcUnixSeconds { get; }
        public string TrainingModelSource { get; }
        public int ActionCount => actions.Length;
        public int ArmStateCount => armStates.Length;

        public EnvironmentAction GetAction(int index) => actions[index];

        public LinUcbArmStateSnapshot GetArmState(int index) => armStates[index];

        public EnvironmentAction[] CopyActions() =>
            (EnvironmentAction[])actions.Clone();

        public LinUcbArmStateSnapshot[] CopyArmStates() =>
            (LinUcbArmStateSnapshot[])armStates.Clone();

        private static T[] Copy<T>(IReadOnlyList<T> source, string parameterName)
        {
            if (source == null)
            {
                throw new ArgumentNullException(parameterName);
            }

            var copy = new T[source.Count];
            for (var index = 0; index < source.Count; index++)
            {
                copy[index] = source[index];
            }

            return copy;
        }
    }

    public enum LinUcbSnapshotRestoreResultCode
    {
        Restored,
        SnapshotMissing,
        SnapshotFormatInvalid,
        SnapshotIdentityInvalid,
        SnapshotSchemaMismatch,
        ParticipantMismatch,
        PolicyMismatch,
        ConfigurationMismatch,
        ModelVersionMismatch,
        FeatureSchemaMismatch,
        HyperparameterMismatch,
        ForgettingUnsupported,
        ActionContractMismatch,
        ArmStateInvalid,
        NonFiniteState,
        MatrixNotSymmetric,
        MatrixNotPositiveDefinite,
        UpdateCountMismatch,
        TimestampInvalid
    }

    public readonly struct LinUcbSnapshotRestoreResult
    {
        public LinUcbSnapshotRestoreResult(
            LinUcbSnapshotRestoreResultCode code,
            string reason)
        {
            Code = code;
            Reason = reason ?? string.Empty;
        }

        public LinUcbSnapshotRestoreResultCode Code { get; }
        public string Reason { get; }
        public bool Restored => Code == LinUcbSnapshotRestoreResultCode.Restored;
    }
}
