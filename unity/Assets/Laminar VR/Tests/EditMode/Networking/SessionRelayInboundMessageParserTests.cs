using LaminarVR.AdaptiveMeditation.Networking;
using LaminarVR.AdaptiveMeditation.Runtime.Networking;
using LaminarVR.AdaptiveMeditation.Session;
using NUnit.Framework;

namespace LaminarVR.AdaptiveMeditation.Tests.EditMode.Networking
{
    public sealed class SessionRelayInboundMessageParserTests
    {
        private const string SchemaVersion = "mindsync-relay-test-v1";

        private readonly SessionRelayInboundMessageParser parser =
            new SessionRelayInboundMessageParser(SchemaVersion);

        [Test]
        public void TryParse_MapsNormalizedSessionConfiguration()
        {
            const string Json =
                "{\"schemaVersion\":\"mindsync-relay-test-v1\","
                + "\"messageId\":\"config-message-1\"," 
                + "\"messageType\":\"session_configuration\"," 
                + "\"payload\":{\"sessionId\":\"session-42\"," 
                + "\"participantPseudonym\":\"P017\"," 
                + "\"sceneId\":\"temple-pond\"," 
                + "\"preferredEnvironment\":{\"illumination\":0.3," 
                + "\"warmth\":0.6,\"atmosphericSoftness\":0.2," 
                + "\"colorRichness\":0.7,\"ambientMotion\":0.4}}}";

            var parsed = parser.TryParse(Json, out var message, out var reason);

            Assert.That(parsed, Is.True);
            Assert.That(reason, Is.EqualTo(SessionRelayMessageParseReasonCode.Accepted));
            Assert.That(
                message.Kind,
                Is.EqualTo(SessionRelayInboundMessageKind.SessionConfiguration));
            Assert.That(message.Command, Is.Null);
            Assert.That(message.Configuration.SchemaVersion, Is.EqualTo(SchemaVersion));
            Assert.That(message.Configuration.MessageId, Is.EqualTo("config-message-1"));
            Assert.That(message.Configuration.SessionId, Is.EqualTo("session-42"));
            Assert.That(message.Configuration.ParticipantPseudonym, Is.EqualTo("P017"));
            Assert.That(message.Configuration.SceneId, Is.EqualTo("temple-pond"));
            Assert.That(
                message.Configuration.PreferredEnvironment.Illumination,
                Is.EqualTo(0.3f));
            Assert.That(
                message.Configuration.PreferredEnvironment.AmbientMotion,
                Is.EqualTo(0.4f));
        }

        [TestCase("start", SessionCommandType.Start)]
        [TestCase("pause", SessionCommandType.Pause)]
        [TestCase("resume", SessionCommandType.Resume)]
        [TestCase("stop", SessionCommandType.Stop)]
        [TestCase("emergency_stop", SessionCommandType.EmergencyStop)]
        public void TryParse_MapsIdempotentSessionCommand(
            string wireCommand,
            SessionCommandType expectedCommand)
        {
            var json = CreateCommandJson(
                SchemaVersion,
                "command-message-7",
                wireCommand);

            var parsed = parser.TryParse(json, out var message, out var reason);

            Assert.That(parsed, Is.True);
            Assert.That(reason, Is.EqualTo(SessionRelayMessageParseReasonCode.Accepted));
            Assert.That(
                message.Kind,
                Is.EqualTo(SessionRelayInboundMessageKind.SessionCommand));
            Assert.That(message.Configuration, Is.Null);
            Assert.That(message.Command.MessageId, Is.EqualTo("command-message-7"));
            Assert.That(message.Command.SessionId, Is.EqualTo("session-42"));
            Assert.That(message.Command.CommandType, Is.EqualTo(expectedCommand));
        }

        [TestCase(null, SessionRelayMessageParseReasonCode.PayloadEmpty)]
        [TestCase("", SessionRelayMessageParseReasonCode.PayloadEmpty)]
        [TestCase("{", SessionRelayMessageParseReasonCode.JsonMalformed)]
        public void TryParse_RejectsEmptyOrMalformedJson(
            string json,
            SessionRelayMessageParseReasonCode expectedReason)
        {
            var parsed = parser.TryParse(json, out var message, out var reason);

            Assert.That(parsed, Is.False);
            Assert.That(message, Is.Null);
            Assert.That(reason, Is.EqualTo(expectedReason));
        }

        [Test]
        public void TryParse_RejectsUnapprovedSchemaVersion()
        {
            var json = CreateCommandJson(
                "mindsync-relay-other-v2",
                "command-message-1",
                "start");

            var parsed = parser.TryParse(json, out var message, out var reason);

            Assert.That(parsed, Is.False);
            Assert.That(message, Is.Null);
            Assert.That(
                reason,
                Is.EqualTo(
                    SessionRelayMessageParseReasonCode.SchemaVersionMismatch));
        }

        [Test]
        public void TryParse_RejectsUnsupportedMessageType()
        {
            const string Json =
                "{\"schemaVersion\":\"mindsync-relay-test-v1\"," 
                + "\"messageId\":\"message-1\"," 
                + "\"messageType\":\"physiology\",\"payload\":{}}";

            var parsed = parser.TryParse(Json, out var message, out var reason);

            Assert.That(parsed, Is.False);
            Assert.That(message, Is.Null);
            Assert.That(
                reason,
                Is.EqualTo(
                    SessionRelayMessageParseReasonCode.MessageTypeUnsupported));
        }

        [Test]
        public void TryParse_RejectsMissingPreferenceDimension()
        {
            const string Json =
                "{\"schemaVersion\":\"mindsync-relay-test-v1\"," 
                + "\"messageId\":\"config-message-1\"," 
                + "\"messageType\":\"session_configuration\"," 
                + "\"payload\":{\"sessionId\":\"session-42\"," 
                + "\"participantPseudonym\":\"P017\"," 
                + "\"sceneId\":\"temple-pond\"," 
                + "\"preferredEnvironment\":{\"illumination\":0.3," 
                + "\"warmth\":0.6,\"atmosphericSoftness\":0.2," 
                + "\"colorRichness\":0.7}}}";

            var parsed = parser.TryParse(Json, out var message, out var reason);

            Assert.That(parsed, Is.False);
            Assert.That(message, Is.Null);
            Assert.That(
                reason,
                Is.EqualTo(
                    SessionRelayMessageParseReasonCode.RequiredFieldMissing));
        }

        [Test]
        public void TryParse_RejectsPreferenceOutsideNormalizedDomain()
        {
            const string Json =
                "{\"schemaVersion\":\"mindsync-relay-test-v1\"," 
                + "\"messageId\":\"config-message-1\"," 
                + "\"messageType\":\"session_configuration\"," 
                + "\"payload\":{\"sessionId\":\"session-42\"," 
                + "\"participantPseudonym\":\"P017\"," 
                + "\"sceneId\":\"temple-pond\"," 
                + "\"preferredEnvironment\":{\"illumination\":1.1," 
                + "\"warmth\":0.6,\"atmosphericSoftness\":0.2," 
                + "\"colorRichness\":0.7,\"ambientMotion\":0.4}}}";

            var parsed = parser.TryParse(Json, out var message, out var reason);

            Assert.That(parsed, Is.False);
            Assert.That(message, Is.Null);
            Assert.That(
                reason,
                Is.EqualTo(
                    SessionRelayMessageParseReasonCode
                        .PreferredEnvironmentInvalid));
        }

        [Test]
        public void TryParse_RejectsUnsupportedCommand()
        {
            var json = CreateCommandJson(
                SchemaVersion,
                "command-message-1",
                "restart");

            var parsed = parser.TryParse(json, out var message, out var reason);

            Assert.That(parsed, Is.False);
            Assert.That(message, Is.Null);
            Assert.That(
                reason,
                Is.EqualTo(
                    SessionRelayMessageParseReasonCode.CommandTypeUnsupported));
        }

        [Test]
        public void TryParse_RejectsBlankStableMessageId()
        {
            var json = CreateCommandJson(SchemaVersion, string.Empty, "start");

            var parsed = parser.TryParse(json, out var message, out var reason);

            Assert.That(parsed, Is.False);
            Assert.That(message, Is.Null);
            Assert.That(
                reason,
                Is.EqualTo(
                    SessionRelayMessageParseReasonCode.RequiredFieldMissing));
        }

        private static string CreateCommandJson(
            string schemaVersion,
            string messageId,
            string command)
        {
            return "{\"schemaVersion\":\""
                + schemaVersion
                + "\",\"messageId\":\""
                + messageId
                + "\",\"messageType\":\"session_command\"," 
                + "\"payload\":{\"sessionId\":\"session-42\"," 
                + "\"command\":\""
                + command
                + "\"}}";
        }
    }
}
