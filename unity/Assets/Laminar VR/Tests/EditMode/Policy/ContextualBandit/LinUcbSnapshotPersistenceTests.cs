using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using LaminarVR.AdaptiveMeditation.Environment;
using LaminarVR.AdaptiveMeditation.Policy;
using LaminarVR.AdaptiveMeditation.Policy.ContextualBandit;
using LaminarVR.AdaptiveMeditation.Runtime.Policy.ContextualBandit;
using LaminarVR.AdaptiveMeditation.Telemetry;
using NUnit.Framework;

namespace LaminarVR.AdaptiveMeditation.Tests.EditMode.Policy.ContextualBandit
{
    public sealed class LinUcbSnapshotPersistenceTests
    {
        [Test]
        public void PathResolver_UsesPseudonymousParticipantScopedFile()
        {
            var path = LinUcbSnapshotFilePathResolver
                .ResolveParticipantSnapshotPath("persistent-root", "P-001");

            Assert.That(
                path,
                Is.EqualTo(
                    Path.Combine(
                        "persistent-root",
                        "AdaptiveMeditationPolicyModels",
                        "P-001.linucb.json")));
            Assert.Throws<ArgumentException>(
                () => LinUcbSnapshotFilePathResolver
                    .ResolveParticipantSnapshotPath(
                        "persistent-root",
                        "../participant"));
        }

        [Test]
        public void JsonCodec_RoundTripsVersionedMatricesAndActionList()
        {
            var policy = CreatePolicy();
            policy.Model.Update(
                EnvironmentAction.IncreaseIllumination,
                Features(),
                0.75d);
            var snapshot = Capture(policy);
            var codec = new LinUcbModelSnapshotJsonCodec();

            var json = codec.Serialize(snapshot);
            var decoded = codec.TryDeserialize(
                json,
                out var roundTripped,
                out var reason);

            Assert.That(decoded, Is.True, reason);
            Assert.That(json, Does.Contain("\"snapshotSchemaVersion\""));
            Assert.That(json, Does.Contain("\"designMatrixRowMajor\""));
            Assert.That(roundTripped.ActionCount, Is.EqualTo(11));
            Assert.That(roundTripped.TotalUpdateCount, Is.EqualTo(1L));
            Assert.That(
                roundTripped.GetArmState(
                    (int)EnvironmentAction.IncreaseIllumination).UpdateCount,
                Is.EqualTo(1L));
        }

        [Test]
        public async Task LocalStore_SaveThenLoadReturnsCompatibleSnapshot()
        {
            var directory = TemporaryDirectory();
            var path = Path.Combine(directory, "model.json");
            try
            {
                using (var store = new LocalLinUcbModelSnapshotStore(path))
                {
                    await store.SaveAsync(Capture(CreatePolicy()),
                        CancellationToken.None);
                    var load = await store.LoadAsync(CancellationToken.None);

                    Assert.That(load.Code,
                        Is.EqualTo(LinUcbSnapshotLoadCode.Loaded));
                    Assert.That(load.Snapshot.ParticipantPseudonym,
                        Is.EqualTo("P-PERSIST"));
                    Assert.That(load.Snapshot.ForgettingEnabled, Is.False);
                    var restore = CreatePolicy().TryRestoreModelSnapshot(
                        load.Snapshot,
                        "P-PERSIST");
                    Assert.That(restore.Code,
                        Is.EqualTo(
                            LinUcbSnapshotRestoreResultCode.Restored));
                }
            }
            finally
            {
                DeleteDirectory(directory);
            }
        }

        [Test]
        public async Task Persistence_LogsSaveAndExplicitParticipantRejection()
        {
            var directory = TemporaryDirectory();
            var path = Path.Combine(directory, "model.json");
            try
            {
                var sink = new RecordingSink();
                var telemetry = new TelemetryRecorder(
                    new TelemetryLoggingConfiguration(
                        "persistence-log",
                        1,
                        "test-events",
                        "1",
                        1),
                    new TelemetrySessionIdentity("session-1", "P-PERSIST"),
                    sink,
                    () => Guid.NewGuid().ToString("N"));
                using (var store = new LocalLinUcbModelSnapshotStore(path))
                {
                    var persistence = new ContextualBanditModelPersistence(
                        store,
                        telemetry);
                    await persistence.SaveAsync(
                        CreatePolicy(),
                        "snapshot-1",
                        "P-PERSIST",
                        1000d,
                        1010d,
                        10d,
                        "uninformative-prior",
                        CancellationToken.None);
                    var result = await persistence.LoadAsync(
                        CreatePolicy(),
                        "P-OTHER",
                        1020d,
                        20d,
                        CancellationToken.None);

                    Assert.That(result.Code,
                        Is.EqualTo(
                            LinUcbSnapshotRestoreResultCode.ParticipantMismatch));
                    Assert.That(sink.EventTypes,
                        Does.Contain(TelemetryEventTypes.BanditSnapshotSaved));
                    Assert.That(sink.EventTypes,
                        Does.Contain(TelemetryEventTypes.BanditSnapshotRejected));
                }
            }
            finally
            {
                DeleteDirectory(directory);
            }
        }

        [Test]
        public async Task LocalStore_ReportsCorruptedJsonWithoutFabricatingState()
        {
            var directory = TemporaryDirectory();
            var path = Path.Combine(directory, "model.json");
            try
            {
                Directory.CreateDirectory(directory);
                File.WriteAllText(path, "{not-json");
                using (var store = new LocalLinUcbModelSnapshotStore(path))
                {
                    var load = await store.LoadAsync(CancellationToken.None);
                    Assert.That(load.Code,
                        Is.EqualTo(LinUcbSnapshotLoadCode.InvalidJson));
                    Assert.That(load.Snapshot, Is.Null);
                }
            }
            finally
            {
                DeleteDirectory(directory);
            }
        }

        private static ContextualBanditPolicy CreatePolicy()
        {
            var builder = new TwoFeatureBuilder();
            return new ContextualBanditPolicy(
                builder,
                new DisjointLinUcbModel(
                    new LinUcbModelConfiguration(
                        "persistence-config",
                        1,
                        builder.FeatureSchemaVersion,
                        builder.FeatureCount,
                        1d,
                        0.1d)));
        }

        private static LinUcbModelSnapshot Capture(
            ContextualBanditPolicy policy)
        {
            return policy.CaptureModelSnapshot(
                "snapshot-1",
                "P-PERSIST",
                1000d,
                1010d,
                "uninformative-prior");
        }

        private static FeatureVector Features()
        {
            return new FeatureVector("persistence-features/1", new[] { 1d, 0.5d });
        }

        private static string TemporaryDirectory()
        {
            return Path.Combine(
                Path.GetTempPath(),
                "LaminarVR.ModelPersistence.Tests",
                Guid.NewGuid().ToString("N"));
        }

        private static void DeleteDirectory(string path)
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
        }

        private sealed class TwoFeatureBuilder : IFeatureVectorBuilder
        {
            public string FeatureSchemaVersion => "persistence-features/1";
            public int FeatureCount => 2;

            public string GetFeatureName(int index)
            {
                if (index < 0 || index >= FeatureCount)
                {
                    throw new ArgumentOutOfRangeException(nameof(index));
                }

                return "feature_" + index;
            }

            public FeatureVector Build(PolicyObservation observation)
            {
                return Features();
            }
        }

        private sealed class RecordingSink : ITelemetryEventSink
        {
            private readonly List<string> eventTypes = new List<string>();
            public IEnumerable<string> EventTypes => eventTypes;

            public Task AppendAsync(
                TelemetryEvent telemetryEvent,
                CancellationToken cancellationToken)
            {
                eventTypes.Add(telemetryEvent.EventType);
                return Task.CompletedTask;
            }

            public Task FlushAsync(CancellationToken cancellationToken)
            {
                return Task.CompletedTask;
            }
        }
    }
}
