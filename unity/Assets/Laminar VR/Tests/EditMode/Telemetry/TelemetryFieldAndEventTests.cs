using System;
using LaminarVR.AdaptiveMeditation.Telemetry;
using NUnit.Framework;

namespace LaminarVR.AdaptiveMeditation.Tests.EditMode.Telemetry
{
    public sealed class TelemetryFieldAndEventTests
    {
        [Test]
        public void FieldFactories_PreserveTypedScalarValues()
        {
            var fields = new[]
            {
                TelemetryField.Null("missing"),
                TelemetryField.Boolean("accepted", true),
                TelemetryField.Integer("sequence", 42L),
                TelemetryField.Number("stress.score", 1.25d),
                TelemetryField.String("phase", "Adaptive")
            };

            Assert.That(fields[0].ValueType, Is.EqualTo(TelemetryFieldValueType.Null));
            Assert.That(fields[1].BooleanValue, Is.True);
            Assert.That(fields[2].IntegerValue, Is.EqualTo(42L));
            Assert.That(fields[3].NumberValue, Is.EqualTo(1.25d));
            Assert.That(fields[4].StringValue, Is.EqualTo("Adaptive"));
        }

        [Test]
        public void FieldFactories_RejectInvalidNamesAndValues()
        {
            Assert.Throws<ArgumentException>(() => TelemetryField.Null(" "));
            Assert.Throws<ArgumentException>(() => TelemetryField.Null("1invalid"));
            Assert.Throws<ArgumentException>(() => TelemetryField.Null("invalid/value"));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => TelemetryField.Number("score", double.NaN));
            Assert.Throws<ArgumentNullException>(
                () => TelemetryField.String("message", null));
        }

        [Test]
        public void Event_DefensivelyCopiesFields()
        {
            var source = new[] { TelemetryField.Integer("count", 1L) };
            var telemetryEvent = CreateEvent(fields: source);
            source[0] = TelemetryField.Integer("count", 99L);

            var copy = telemetryEvent.CopyFields();
            copy[0] = TelemetryField.Integer("count", 77L);

            Assert.That(telemetryEvent.FieldCount, Is.EqualTo(1));
            Assert.That(telemetryEvent.GetField(0).IntegerValue, Is.EqualTo(1L));
        }

        [Test]
        public void Event_RejectsDuplicateOrUninitializedFields()
        {
            Assert.Throws<ArgumentException>(
                () => CreateEvent(
                    fields: new[]
                    {
                        TelemetryField.Integer("count", 1L),
                        TelemetryField.Integer("count", 2L)
                    }));
            Assert.Throws<ArgumentException>(
                () => CreateEvent(fields: new TelemetryField[1]));
        }

        [Test]
        public void Event_RejectsInvalidEnvelopeValues()
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => CreateEvent(sequenceNumber: 0L));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => CreateEvent(utcTimestampUnixSeconds: double.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => CreateEvent(sessionElapsedSeconds: -0.01d));
            Assert.Throws<ArgumentNullException>(
                () => new TelemetryEvent(
                    "adaptive-vr-telemetry",
                    "0.1-draft",
                    "logging-test",
                    2,
                    "event-1",
                    1L,
                    "session-1",
                    "P017",
                    TelemetryEventTypes.SessionPhaseChanged,
                    1000d,
                    0d,
                    false,
                    null));
        }

        internal static TelemetryEvent CreateEvent(
            long sequenceNumber = 1L,
            double utcTimestampUnixSeconds = 1000.25d,
            double sessionElapsedSeconds = 12.5d,
            bool critical = false,
            TelemetryField[] fields = null,
            string schemaId = "adaptive-vr-telemetry",
            string schemaVersion = "0.1-draft",
            string configurationId = "logging-test",
            int configurationVersion = 2)
        {
            return new TelemetryEvent(
                schemaId,
                schemaVersion,
                configurationId,
                configurationVersion,
                "event-" + sequenceNumber,
                sequenceNumber,
                "session-1",
                "P017",
                TelemetryEventTypes.SessionPhaseChanged,
                utcTimestampUnixSeconds,
                sessionElapsedSeconds,
                critical,
                fields ?? Array.Empty<TelemetryField>());
        }
    }
}
