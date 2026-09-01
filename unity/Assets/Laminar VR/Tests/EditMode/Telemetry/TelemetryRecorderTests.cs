using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LaminarVR.AdaptiveMeditation.Telemetry;
using NUnit.Framework;

namespace LaminarVR.AdaptiveMeditation.Tests.EditMode.Telemetry
{
    public sealed class TelemetryRecorderTests
    {
        [Test]
        public async Task RecordAsync_AssignsOrderedIdentityAndMetadata()
        {
            var sink = new RecordingSink();
            var eventIdSequence = 0;
            var recorder = CreateRecorder(
                sink,
                () => "deterministic-event-" + (++eventIdSequence));

            var first = await recorder.RecordAsync(
                TelemetryEventTypes.ApplicationStarted,
                1000d,
                0d,
                true,
                Array.Empty<TelemetryField>(),
                CancellationToken.None);
            var second = await recorder.RecordAsync(
                TelemetryEventTypes.SessionPhaseChanged,
                1001d,
                1d,
                false,
                new[] { TelemetryField.String("phase", "Ready") },
                CancellationToken.None);

            Assert.That(first.SequenceNumber, Is.EqualTo(1L));
            Assert.That(first.EventId, Is.EqualTo("deterministic-event-1"));
            Assert.That(first.Critical, Is.True);
            Assert.That(second.SequenceNumber, Is.EqualTo(2L));
            Assert.That(second.EventSchemaVersion, Is.EqualTo("0.1-draft"));
            Assert.That(second.ParticipantPseudonym, Is.EqualTo("P017"));
            Assert.That(recorder.LastWrittenSequenceNumber, Is.EqualTo(2L));
            Assert.That(sink.Events, Has.Count.EqualTo(2));
        }

        [Test]
        public async Task RecordAsync_RejectsSessionTimeMovingBackwards()
        {
            var recorder = CreateRecorder(new RecordingSink(), () => "event-id");
            await recorder.RecordAsync(
                TelemetryEventTypes.ApplicationStarted,
                1000d,
                2d,
                false,
                Array.Empty<TelemetryField>(),
                CancellationToken.None);

            Assert.ThrowsAsync<ArgumentOutOfRangeException>(
                async () => await recorder.RecordAsync(
                    TelemetryEventTypes.SessionPhaseChanged,
                    1001d,
                    1d,
                    false,
                    Array.Empty<TelemetryField>(),
                    CancellationToken.None));
        }

        [Test]
        public async Task RecordAsync_DoesNotAdvanceSequenceWhenSinkRejectsWrite()
        {
            var sink = new RecordingSink { RejectNextAppend = true };
            var recorder = CreateRecorder(sink, () => "event-id");

            Assert.ThrowsAsync<InvalidOperationException>(
                async () => await recorder.RecordAsync(
                    TelemetryEventTypes.ApplicationStarted,
                    1000d,
                    0d,
                    false,
                    Array.Empty<TelemetryField>(),
                    CancellationToken.None));

            var accepted = await recorder.RecordAsync(
                TelemetryEventTypes.ApplicationStarted,
                1000d,
                0d,
                false,
                Array.Empty<TelemetryField>(),
                CancellationToken.None);

            Assert.That(accepted.SequenceNumber, Is.EqualTo(1L));
            Assert.That(recorder.LastWrittenSequenceNumber, Is.EqualTo(1L));
        }

        [Test]
        public async Task FlushAsync_DelegatesToSink()
        {
            var sink = new RecordingSink();
            var recorder = CreateRecorder(sink, () => "event-id");

            await recorder.FlushAsync(CancellationToken.None);

            Assert.That(sink.FlushCount, Is.EqualTo(1));
        }

        private static TelemetryRecorder CreateRecorder(
            ITelemetryEventSink sink,
            Func<string> eventIdFactory)
        {
            return new TelemetryRecorder(
                new TelemetryLoggingConfiguration(
                    "logging-test",
                    2,
                    "adaptive-vr-telemetry",
                    "0.1-draft",
                    4),
                new TelemetrySessionIdentity("session-1", "P017"),
                sink,
                eventIdFactory);
        }

        private sealed class RecordingSink : ITelemetryEventSink
        {
            public List<TelemetryEvent> Events { get; } =
                new List<TelemetryEvent>();

            public bool RejectNextAppend { get; set; }

            public int FlushCount { get; private set; }

            public Task AppendAsync(
                TelemetryEvent telemetryEvent,
                CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (RejectNextAppend)
                {
                    RejectNextAppend = false;
                    throw new InvalidOperationException("Synthetic sink failure.");
                }

                Events.Add(telemetryEvent);
                return Task.CompletedTask;
            }

            public Task FlushAsync(CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                FlushCount++;
                return Task.CompletedTask;
            }
        }
    }
}
