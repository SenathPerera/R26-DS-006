using LaminarVR.AdaptiveMeditation.Runtime.Telemetry;
using LaminarVR.AdaptiveMeditation.Telemetry;
using NUnit.Framework;

namespace LaminarVR.AdaptiveMeditation.Tests.EditMode.Telemetry
{
    public sealed class JsonLinesTelemetryFormatterTests
    {
        [Test]
        public void Format_ProducesDeterministicSingleLineJsonWithTypedData()
        {
            var telemetryEvent = TelemetryFieldAndEventTests.CreateEvent(
                critical: true,
                fields: new[]
                {
                    TelemetryField.String("message", "line\n\"quoted\""),
                    TelemetryField.Integer("count", 3L),
                    TelemetryField.Number("score", 1.25d),
                    TelemetryField.Boolean("valid", true),
                    TelemetryField.Null("missing")
                });
            var formatter = new JsonLinesTelemetryFormatter();

            var json = formatter.Format(telemetryEvent);

            const string expected =
                "{\"schemaId\":\"adaptive-vr-telemetry\"," 
                + "\"schemaVersion\":\"0.1-draft\"," 
                + "\"eventId\":\"event-1\",\"sequenceNumber\":1," 
                + "\"sessionId\":\"session-1\"," 
                + "\"participantPseudonym\":\"P017\"," 
                + "\"eventType\":\"session.phase_changed\"," 
                + "\"utcTimestampUnixSeconds\":1000.25," 
                + "\"sessionElapsedSeconds\":12.5,\"critical\":true," 
                + "\"loggingConfigurationId\":\"logging-test\"," 
                + "\"loggingConfigurationVersion\":2,\"data\":{" 
                + "\"message\":\"line\\n\\\"quoted\\\"\"," 
                + "\"count\":3,\"score\":1.25,\"valid\":true," 
                + "\"missing\":null}}";
            Assert.That(json, Is.EqualTo(expected));
            Assert.That(json, Does.Not.Contain("\n"));
            Assert.That(json, Does.Not.Contain("\r"));
        }

        [Test]
        public void Format_RejectsMissingEvent()
        {
            var formatter = new JsonLinesTelemetryFormatter();

            Assert.That(
                () => formatter.Format(null),
                Throws.ArgumentNullException);
        }
    }
}
