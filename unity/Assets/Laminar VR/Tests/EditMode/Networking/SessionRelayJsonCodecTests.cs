using System.Collections.Generic;
using LaminarVR.AdaptiveMeditation.Networking;
using LaminarVR.AdaptiveMeditation.Runtime.Networking;
using LaminarVR.AdaptiveMeditation.Session;
using LaminarVR.AdaptiveMeditation.Telemetry;
using NUnit.Framework;

namespace LaminarVR.AdaptiveMeditation.Tests.EditMode.Networking
{
    public sealed class SessionRelayJsonCodecTests
    {
        private const string SchemaVersion = "mindsync-relay-test-v1";

        private readonly SessionRelayJsonCodec codec =
            new SessionRelayJsonCodec(SchemaVersion);

        [Test]
        public void SerializePairingRequest_UsesQuestRoleAndRuntimeIdentity()
        {
            var json = codec.SerializePairingRequest(
                "pair-message-1",
                "482913",
                "quest-install-7",
                "1.2.0");

            Assert.That(json, Does.Contain("\"schemaVersion\":\"mindsync-relay-test-v1\""));
            Assert.That(json, Does.Contain("\"messageType\":\"pairing_request\""));
            Assert.That(json, Does.Contain("\"pairingCode\":\"482913\""));
            Assert.That(json, Does.Contain("\"clientRole\":\"quest\""));
            Assert.That(json, Does.Contain("\"questClientId\":\"quest-install-7\""));
            Assert.That(json, Does.Contain("\"appVersion\":\"1.2.0\""));
        }

        [Test]
        public void TryParsePairingResult_AcceptsBoundSession()
        {
            const string Json =
                "{\"schemaVersion\":\"mindsync-relay-test-v1\","
                + "\"messageId\":\"pair-result-1\","
                + "\"messageType\":\"pairing_result\","
                + "\"payload\":{\"accepted\":true,"
                + "\"sessionId\":\"session-42\"}}";

            var parsed = codec.TryParsePairingResult(
                Json,
                out var result,
                out var reason);

            Assert.That(parsed, Is.True);
            Assert.That(reason, Is.EqualTo(SessionRelayPairingParseReasonCode.Accepted));
            Assert.That(result.Accepted, Is.True);
            Assert.That(result.SessionId, Is.EqualTo("session-42"));
        }

        [Test]
        public void TryParsePairingResult_PreservesNonSensitiveRejectionCode()
        {
            const string Json =
                "{\"schemaVersion\":\"mindsync-relay-test-v1\","
                + "\"messageId\":\"pair-result-2\","
                + "\"messageType\":\"pairing_result\","
                + "\"payload\":{\"accepted\":false,"
                + "\"rejectionCode\":\"pairing-code-expired\"}}";

            var parsed = codec.TryParsePairingResult(
                Json,
                out var result,
                out var reason);

            Assert.That(parsed, Is.True);
            Assert.That(reason, Is.EqualTo(SessionRelayPairingParseReasonCode.PairingRejected));
            Assert.That(result.Accepted, Is.False);
            Assert.That(result.RejectionCode, Is.EqualTo("pairing-code-expired"));
        }

        [Test]
        public void SerializeQuestState_MapsPhaseToStableWireValue()
        {
            var state = new SessionRelayQuestState(
                "state-1",
                "session-42",
                VrSessionPhase.AwaitingConfig,
                1787282898.4d);

            var json = codec.SerializeQuestState(state);

            Assert.That(json, Does.Contain("\"messageType\":\"quest_state\""));
            Assert.That(json, Does.Contain("\"sessionId\":\"session-42\""));
            Assert.That(json, Does.Contain("\"phase\":\"awaiting_config\""));
        }

        [Test]
        public void SerializeTelemetryBatch_PreservesTypedFields()
        {
            var telemetryEvent = new TelemetryEvent(
                "visual-event",
                "1",
                "logging-test",
                1,
                "event-1",
                1L,
                "session-42",
                "P017",
                "session.phase_changed",
                1787282898.4d,
                10d,
                false,
                new List<TelemetryField>
                {
                    TelemetryField.String("phase", "adaptive"),
                    TelemetryField.Number("stress", 1.5d)
                });

            var json = codec.SerializeTelemetryBatch(
                "batch-1",
                new[] { telemetryEvent });

            Assert.That(json, Does.Contain("\"messageType\":\"visual_telemetry_batch\""));
            Assert.That(json, Does.Contain("\"eventId\":\"event-1\""));
            Assert.That(json, Does.Contain("\"valueType\":\"string\""));
            Assert.That(json, Does.Contain("\"stringValue\":\"adaptive\""));
            Assert.That(json, Does.Contain("\"valueType\":\"number\""));
        }
    }
}
