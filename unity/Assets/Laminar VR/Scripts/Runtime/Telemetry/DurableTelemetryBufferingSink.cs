using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using LaminarVR.AdaptiveMeditation.Telemetry;

namespace LaminarVR.AdaptiveMeditation.Runtime.Telemetry
{
    public sealed class DurableTelemetryBufferingSink
        : ITelemetryEventSink, IRecordedTelemetrySource
    {
        private readonly ITelemetryEventSink durableSink;
        private readonly Func<TelemetryEvent, bool> shouldExposeForTransfer;
        private readonly ConcurrentQueue<TelemetryEvent> recordedEvents =
            new ConcurrentQueue<TelemetryEvent>();

        public DurableTelemetryBufferingSink(
            ITelemetryEventSink durableSink,
            Func<TelemetryEvent, bool> shouldExposeForTransfer = null)
        {
            this.durableSink = durableSink
                ?? throw new ArgumentNullException(nameof(durableSink));
            this.shouldExposeForTransfer = shouldExposeForTransfer
                ?? (_ => true);
        }

        public int PendingEventCount => recordedEvents.Count;

        public async Task AppendAsync(
            TelemetryEvent telemetryEvent,
            CancellationToken cancellationToken)
        {
            await durableSink.AppendAsync(
                    telemetryEvent,
                    cancellationToken)
                .ConfigureAwait(false);
            if (shouldExposeForTransfer(telemetryEvent))
            {
                recordedEvents.Enqueue(telemetryEvent);
            }
        }

        public Task FlushAsync(CancellationToken cancellationToken)
        {
            return durableSink.FlushAsync(cancellationToken);
        }

        public bool TryDequeue(out TelemetryEvent telemetryEvent)
        {
            return recordedEvents.TryDequeue(out telemetryEvent);
        }
    }
}
