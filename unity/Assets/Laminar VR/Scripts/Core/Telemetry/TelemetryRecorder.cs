using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace LaminarVR.AdaptiveMeditation.Telemetry
{
    public sealed class TelemetryRecorder
    {
        private readonly TelemetryLoggingConfiguration configuration;
        private readonly TelemetrySessionIdentity identity;
        private readonly ITelemetryEventSink sink;
        private readonly Func<string> eventIdFactory;
        private readonly SemaphoreSlim recordGate = new SemaphoreSlim(1, 1);
        private long sequenceNumber;
        private double lastSessionElapsedSeconds = -1d;

        public TelemetryRecorder(
            TelemetryLoggingConfiguration configuration,
            TelemetrySessionIdentity identity,
            ITelemetryEventSink sink,
            Func<string> eventIdFactory = null)
        {
            this.configuration = configuration
                ?? throw new ArgumentNullException(nameof(configuration));
            this.identity = identity
                ?? throw new ArgumentNullException(nameof(identity));
            this.sink = sink ?? throw new ArgumentNullException(nameof(sink));
            this.eventIdFactory = eventIdFactory
                ?? (() => Guid.NewGuid().ToString("N"));
        }

        public long LastWrittenSequenceNumber =>
            Interlocked.Read(ref sequenceNumber);

        public async Task<TelemetryEvent> RecordAsync(
            string eventType,
            double utcTimestampUnixSeconds,
            double sessionElapsedSeconds,
            bool critical,
            IReadOnlyList<TelemetryField> fields,
            CancellationToken cancellationToken)
        {
            await recordGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (sessionElapsedSeconds < lastSessionElapsedSeconds)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(sessionElapsedSeconds),
                        sessionElapsedSeconds,
                        "Telemetry session time must not move backwards.");
                }

                var nextSequenceNumber = sequenceNumber + 1L;
                var telemetryEvent = new TelemetryEvent(
                    configuration.EventSchemaId,
                    configuration.EventSchemaVersion,
                    configuration.ConfigurationId,
                    configuration.ConfigurationVersion,
                    eventIdFactory(),
                    nextSequenceNumber,
                    identity.SessionId,
                    identity.ParticipantPseudonym,
                    eventType,
                    utcTimestampUnixSeconds,
                    sessionElapsedSeconds,
                    critical,
                    fields);

                await sink.AppendAsync(telemetryEvent, cancellationToken)
                    .ConfigureAwait(false);
                sequenceNumber = nextSequenceNumber;
                lastSessionElapsedSeconds = sessionElapsedSeconds;
                return telemetryEvent;
            }
            finally
            {
                recordGate.Release();
            }
        }

        public Task FlushAsync(CancellationToken cancellationToken)
        {
            return sink.FlushAsync(cancellationToken);
        }
    }
}
