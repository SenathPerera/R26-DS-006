using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using LaminarVR.AdaptiveMeditation.Policy.ContextualBandit;
using LaminarVR.AdaptiveMeditation.Telemetry;

namespace LaminarVR.AdaptiveMeditation.Runtime.Policy.ContextualBandit
{
    public sealed class ContextualBanditModelPersistence
    {
        private readonly LocalLinUcbModelSnapshotStore store;
        private readonly TelemetryRecorder telemetry;

        public ContextualBanditModelPersistence(
            LocalLinUcbModelSnapshotStore store,
            TelemetryRecorder telemetry)
        {
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            this.telemetry = telemetry
                ?? throw new ArgumentNullException(nameof(telemetry));
        }

        public async Task<LinUcbModelSnapshot> SaveAsync(
            ContextualBanditPolicy policy,
            string snapshotId,
            string participantPseudonym,
            double createdUtcUnixSeconds,
            double updatedUtcUnixSeconds,
            double sessionElapsedSeconds,
            string trainingModelSource,
            CancellationToken cancellationToken)
        {
            if (policy == null)
            {
                throw new ArgumentNullException(nameof(policy));
            }

            var snapshot = policy.CaptureModelSnapshot(
                snapshotId,
                participantPseudonym,
                createdUtcUnixSeconds,
                updatedUtcUnixSeconds,
                trainingModelSource);
            try
            {
                await store.SaveAsync(snapshot, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception exception) when (
                exception is IOException
                || exception is UnauthorizedAccessException)
            {
                await RecordAsync(
                    TelemetryEventTypes.BanditSnapshotSaveFailed,
                    updatedUtcUnixSeconds,
                    sessionElapsedSeconds,
                    true,
                    SnapshotFields(snapshot, exception.GetType().Name),
                    cancellationToken).ConfigureAwait(false);
                throw;
            }

            await RecordAsync(
                TelemetryEventTypes.BanditSnapshotSaved,
                updatedUtcUnixSeconds,
                sessionElapsedSeconds,
                true,
                SnapshotFields(snapshot, null),
                cancellationToken).ConfigureAwait(false);
            return snapshot;
        }

        public async Task<LinUcbSnapshotRestoreResult> LoadAsync(
            ContextualBanditPolicy policy,
            string expectedParticipantPseudonym,
            double utcTimestampUnixSeconds,
            double sessionElapsedSeconds,
            CancellationToken cancellationToken)
        {
            if (policy == null)
            {
                throw new ArgumentNullException(nameof(policy));
            }

            var load = await store.LoadAsync(cancellationToken)
                .ConfigureAwait(false);
            if (load.Code != LinUcbSnapshotLoadCode.Loaded)
            {
                var missingResult = new LinUcbSnapshotRestoreResult(
                    load.Code == LinUcbSnapshotLoadCode.NotFound
                        ? LinUcbSnapshotRestoreResultCode.SnapshotMissing
                        : LinUcbSnapshotRestoreResultCode.SnapshotFormatInvalid,
                    load.Reason);
                await RecordRestoreAsync(
                    null,
                    missingResult,
                    utcTimestampUnixSeconds,
                    sessionElapsedSeconds,
                    cancellationToken).ConfigureAwait(false);
                return missingResult;
            }

            var result = policy.TryRestoreModelSnapshot(
                load.Snapshot,
                expectedParticipantPseudonym);
            await RecordRestoreAsync(
                load.Snapshot,
                result,
                utcTimestampUnixSeconds,
                sessionElapsedSeconds,
                cancellationToken).ConfigureAwait(false);
            return result;
        }

        private Task RecordRestoreAsync(
            LinUcbModelSnapshot snapshot,
            LinUcbSnapshotRestoreResult result,
            double utcTimestampUnixSeconds,
            double sessionElapsedSeconds,
            CancellationToken cancellationToken)
        {
            var fields = new List<TelemetryField>();
            if (snapshot != null)
            {
                fields.AddRange(SnapshotFields(snapshot, null));
            }

            fields.Add(
                TelemetryField.String("result_code", result.Code.ToString()));
            fields.Add(TelemetryField.String("reason", result.Reason));
            return RecordAsync(
                result.Restored
                    ? TelemetryEventTypes.BanditSnapshotLoaded
                    : TelemetryEventTypes.BanditSnapshotRejected,
                utcTimestampUnixSeconds,
                sessionElapsedSeconds,
                !result.Restored,
                fields,
                cancellationToken);
        }

        private static TelemetryField[] SnapshotFields(
            LinUcbModelSnapshot snapshot,
            string reason)
        {
            var fields = new List<TelemetryField>
            {
                TelemetryField.String("snapshot_id", snapshot.SnapshotId ?? string.Empty),
                TelemetryField.String(
                    "snapshot_schema_version",
                    snapshot.SnapshotSchemaVersion ?? string.Empty),
                TelemetryField.String("policy_id", snapshot.PolicyId ?? string.Empty),
                TelemetryField.String(
                    "policy_version",
                    snapshot.PolicyVersion ?? string.Empty),
                TelemetryField.String(
                    "model_version",
                    snapshot.ModelVersion ?? string.Empty),
                TelemetryField.String(
                    "feature_schema_version",
                    snapshot.FeatureSchemaVersion ?? string.Empty),
                TelemetryField.String(
                    "configuration_id",
                    snapshot.ConfigurationId ?? string.Empty),
                TelemetryField.Integer(
                    "configuration_version",
                    snapshot.ConfigurationVersion),
                TelemetryField.Integer(
                    "model_update_count",
                    snapshot.TotalUpdateCount),
                TelemetryField.Boolean(
                    "forgetting_enabled",
                    snapshot.ForgettingEnabled)
            };
            if (reason != null)
            {
                fields.Add(TelemetryField.String("reason", reason));
            }

            return fields.ToArray();
        }

        private Task RecordAsync(
            string eventType,
            double utcTimestampUnixSeconds,
            double sessionElapsedSeconds,
            bool critical,
            IReadOnlyList<TelemetryField> fields,
            CancellationToken cancellationToken)
        {
            return telemetry.RecordAsync(
                eventType,
                utcTimestampUnixSeconds,
                sessionElapsedSeconds,
                critical,
                fields,
                cancellationToken);
        }
    }
}
