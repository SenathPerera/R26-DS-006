using LaminarVR.AdaptiveMeditation.Environment;
using LaminarVR.AdaptiveMeditation.Policy;
using LaminarVR.AdaptiveMeditation.Policy.ContextualBandit;
using NUnit.Framework;

namespace LaminarVR.AdaptiveMeditation.Tests.EditMode.Policy.ContextualBandit
{
    public sealed class LinUcbModelSnapshotTests
    {
        private const string FeatureSchema = "snapshot-features/1";
        private const string Participant = "P-SNAPSHOT";
        private const string PolicyId = "ContextualBanditPolicy";
        private const string PolicyVersion = "policy/1";

        [Test]
        public void CaptureAndRestore_RoundTripsEveryArmWithoutAliasing()
        {
            var source = CreateModel();
            source.Update(
                EnvironmentAction.IncreaseWarmth,
                Features(1d, 0.5d),
                2d);
            var snapshot = Capture(source);
            var restored = CreateModel();

            var result = restored.TryRestoreSnapshot(
                snapshot,
                Participant,
                PolicyId,
                PolicyVersion);

            Assert.That(result.Code,
                Is.EqualTo(LinUcbSnapshotRestoreResultCode.Restored));
            Assert.That(restored.TotalUpdateCount, Is.EqualTo(1L));
            var arm = restored.CaptureArmState(
                EnvironmentAction.IncreaseWarmth);
            Assert.That(arm.UpdateCount, Is.EqualTo(1L));
            Assert.That(arm.GetDesignMatrixValue(0, 1), Is.EqualTo(0.5d));
            Assert.That(arm.GetRewardVectorValue(0), Is.EqualTo(2d));

            var snapshotMatrix = snapshot.GetArmState(
                (int)EnvironmentAction.IncreaseWarmth).CopyDesignMatrix();
            snapshotMatrix[0, 0] = 999d;
            Assert.That(
                restored.CaptureArmState(EnvironmentAction.IncreaseWarmth)
                    .GetDesignMatrixValue(0, 0),
                Is.Not.EqualTo(999d));
        }

        [Test]
        public void Restore_RejectsParticipantMismatchWithoutChangingLiveModel()
        {
            var source = CreateModel();
            source.Update(EnvironmentAction.NoChange, Features(1d, 0d), 1d);
            var target = CreateModel();

            var result = target.TryRestoreSnapshot(
                Capture(source),
                "P-DIFFERENT",
                PolicyId,
                PolicyVersion);

            Assert.That(result.Code,
                Is.EqualTo(LinUcbSnapshotRestoreResultCode.ParticipantMismatch));
            Assert.That(target.TotalUpdateCount, Is.Zero);
            Assert.That(
                target.CaptureArmState(EnvironmentAction.NoChange).UpdateCount,
                Is.Zero);
        }

        [Test]
        public void Restore_RejectsEnabledForgettingAndSchemaMismatch()
        {
            var model = CreateModel();
            var snapshot = Capture(model);

            var forgetting = Copy(snapshot, forgettingEnabled: true);
            var forgettingResult = model.TryRestoreSnapshot(
                forgetting,
                Participant,
                PolicyId,
                PolicyVersion);
            var schema = Copy(snapshot, snapshotSchemaVersion: "future/2");
            var schemaResult = model.TryRestoreSnapshot(
                schema,
                Participant,
                PolicyId,
                PolicyVersion);

            Assert.That(forgettingResult.Code,
                Is.EqualTo(LinUcbSnapshotRestoreResultCode.ForgettingUnsupported));
            Assert.That(schemaResult.Code,
                Is.EqualTo(LinUcbSnapshotRestoreResultCode.SnapshotSchemaMismatch));
        }

        [Test]
        public void Restore_RejectsNonSymmetricMatrixTransactionally()
        {
            var model = CreateModel();
            var snapshot = Capture(model);
            var arms = snapshot.CopyArmStates();
            var first = arms[0];
            var matrix = first.CopyDesignMatrix();
            matrix[0, 1] = 0.25d;
            arms[0] = new LinUcbArmStateSnapshot(
                first.Action,
                matrix,
                first.CopyRewardVector(),
                first.UpdateCount);
            var invalid = Copy(snapshot, armStates: arms);

            var result = model.TryRestoreSnapshot(
                invalid,
                Participant,
                PolicyId,
                PolicyVersion);

            Assert.That(result.Code,
                Is.EqualTo(LinUcbSnapshotRestoreResultCode.MatrixNotSymmetric));
            Assert.That(model.TotalUpdateCount, Is.Zero);
        }

        private static DisjointLinUcbModel CreateModel()
        {
            return new DisjointLinUcbModel(
                new LinUcbModelConfiguration(
                    "snapshot-config",
                    3,
                    FeatureSchema,
                    2,
                    1d,
                    0.2d));
        }

        private static FeatureVector Features(double first, double second)
        {
            return new FeatureVector(FeatureSchema, new[] { first, second });
        }

        private static LinUcbModelSnapshot Capture(DisjointLinUcbModel model)
        {
            return model.CaptureSnapshot(
                new LinUcbSnapshotMetadata(
                    "snapshot-1",
                    Participant,
                    PolicyId,
                    PolicyVersion,
                    1000d,
                    1100d,
                    "uninformative-prior"));
        }

        private static LinUcbModelSnapshot Copy(
            LinUcbModelSnapshot source,
            string snapshotSchemaVersion = null,
            bool? forgettingEnabled = null,
            LinUcbArmStateSnapshot[] armStates = null)
        {
            return new LinUcbModelSnapshot(
                snapshotSchemaVersion ?? source.SnapshotSchemaVersion,
                source.SnapshotId,
                source.ParticipantPseudonym,
                source.PolicyId,
                source.PolicyVersion,
                source.ModelVersion,
                source.FeatureSchemaVersion,
                source.FeatureCount,
                source.ConfigurationId,
                source.ConfigurationVersion,
                source.RidgeRegularization,
                source.ExplorationCoefficient,
                forgettingEnabled ?? source.ForgettingEnabled,
                source.TotalUpdateCount,
                source.CreatedUtcUnixSeconds,
                source.UpdatedUtcUnixSeconds,
                source.TrainingModelSource,
                source.CopyActions(),
                armStates ?? source.CopyArmStates());
        }
    }
}
