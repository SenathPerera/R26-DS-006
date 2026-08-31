using System;
using System.Threading;
using System.Threading.Tasks;
using LaminarVR.AdaptiveMeditation.Runtime.Telemetry;
using LaminarVR.AdaptiveMeditation.Telemetry;
using NUnit.Framework;

namespace LaminarVR.AdaptiveMeditation.Tests.EditMode.Telemetry
{
    public sealed class DurableTelemetryBufferingSinkTests
    {
        [Test]
        public async Task AppendAsync_BuffersOnlyAfterDurableAppendSucceeds()
        {
            var durableSink = new RecordingDurableSink();
            var sink = new DurableTelemetryBufferingSink(durableSink);
            var telemetryEvent = CreateEvent();

            await sink.AppendAsync(
                telemetryEvent,
                CancellationToken.None);

            Assert.That(durableSink.AppendCount, Is.EqualTo(1));
            Assert.That(sink.PendingEventCount, Is.EqualTo(1));
            Assert.That(sink.TryDequeue(out var recorded), Is.True);
            Assert.That(recorded, Is.SameAs(telemetryEvent));
            Assert.That(sink.PendingEventCount, Is.Zero);
        }

        [Test]
        public void FailedDurableAppend_IsNotOfferedForRelayPublishing()
        {
            var durableSink = new RecordingDurableSink
            {
                FailAppend = true
            };
            var sink = new DurableTelemetryBufferingSink(durableSink);

            Assert.ThrowsAsync<InvalidOperationException>(
                async () => await sink.AppendAsync(
                    CreateEvent(),
                    CancellationToken.None));

            Assert.That(sink.PendingEventCount, Is.Zero);
            Assert.That(sink.TryDequeue(out _), Is.False);
        }

        private static TelemetryEvent CreateEvent()
        {
            return new TelemetryEvent(
                "visual-event",
                "1",
                "telemetry-test",
                1,
                "event-1",
                1,
                "session-42",
                "P017",
                "session_started",
                1787282898.4d,
                0d,
                true,
                Array.Empty<TelemetryField>());
        }

        private sealed class RecordingDurableSink : ITelemetryEventSink
        {
            public int AppendCount { get; private set; }

            public bool FailAppend { get; set; }

            public Task AppendAsync(
                TelemetryEvent telemetryEvent,
                CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                AppendCount++;
                if (FailAppend)
                {
                    throw new InvalidOperationException(
                        "Simulated durable append failure.");
                }

                return Task.CompletedTask;
            }

            public Task FlushAsync(CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Task.CompletedTask;
            }
        }
    }
}
