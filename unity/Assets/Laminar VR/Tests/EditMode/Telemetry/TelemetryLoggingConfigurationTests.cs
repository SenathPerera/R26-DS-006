using System;
using LaminarVR.AdaptiveMeditation.Telemetry;
using NUnit.Framework;

namespace LaminarVR.AdaptiveMeditation.Tests.EditMode.Telemetry
{
    public sealed class TelemetryLoggingConfigurationTests
    {
        [Test]
        public void Constructor_StoresTrimmedValidatedConfiguration()
        {
            var configuration = new TelemetryLoggingConfiguration(
                " logging-pilot ",
                2,
                " adaptive-vr-telemetry ",
                " 0.1-draft ",
                8);

            Assert.That(configuration.ConfigurationId, Is.EqualTo("logging-pilot"));
            Assert.That(configuration.ConfigurationVersion, Is.EqualTo(2));
            Assert.That(
                configuration.EventSchemaId,
                Is.EqualTo("adaptive-vr-telemetry"));
            Assert.That(configuration.EventSchemaVersion, Is.EqualTo("0.1-draft"));
            Assert.That(configuration.FlushEveryEventCount, Is.EqualTo(8));
        }

        [TestCase(null, 1, "schema", "1", 1)]
        [TestCase("config", 0, "schema", "1", 1)]
        [TestCase("config", 1, " ", "1", 1)]
        [TestCase("config", 1, "schema", null, 1)]
        [TestCase("config", 1, "schema", "1", 0)]
        public void Constructor_RejectsInvalidConfiguration(
            string configurationId,
            int configurationVersion,
            string eventSchemaId,
            string eventSchemaVersion,
            int flushEveryEventCount)
        {
            Assert.Catch<ArgumentException>(
                () => new TelemetryLoggingConfiguration(
                    configurationId,
                    configurationVersion,
                    eventSchemaId,
                    eventSchemaVersion,
                    flushEveryEventCount));
        }

        [Test]
        public void SessionIdentity_RequiresNonControlPseudonymousIdentifiers()
        {
            var identity = new TelemetrySessionIdentity(" session-1 ", " P017 ");

            Assert.That(identity.SessionId, Is.EqualTo("session-1"));
            Assert.That(identity.ParticipantPseudonym, Is.EqualTo("P017"));
            Assert.Throws<ArgumentException>(
                () => new TelemetrySessionIdentity(" ", "P017"));
            Assert.Throws<ArgumentException>(
                () => new TelemetrySessionIdentity("session-1", "P\n017"));
        }
    }
}
