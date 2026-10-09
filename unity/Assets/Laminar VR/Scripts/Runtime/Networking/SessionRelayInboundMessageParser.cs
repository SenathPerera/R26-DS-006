using System;
using LaminarVR.AdaptiveMeditation.Environment;
using LaminarVR.AdaptiveMeditation.Networking;
using LaminarVR.AdaptiveMeditation.Session;
using UnityEngine;

namespace LaminarVR.AdaptiveMeditation.Runtime.Networking
{
    public enum SessionRelayMessageParseReasonCode
    {
        Accepted,
        PayloadEmpty,
        JsonMalformed,
        RequiredFieldMissing,
        SchemaVersionMismatch,
        MessageTypeUnsupported,
        PreferredEnvironmentInvalid,
        CommandTypeUnsupported
    }

    public sealed class SessionRelayInboundMessageParser
    {
        private const string ConfigurationMessageType =
            "session_configuration";
        private const string CommandMessageType = "session_command";

        private static readonly string[] RequiredEnvelopeProperties =
        {
            "schemaVersion",
            "messageId",
            "messageType",
            "payload"
        };

        private static readonly string[] RequiredConfigurationProperties =
        {
            "sessionId",
            "participantPseudonym",
            "sceneId",
            "preferredEnvironment",
            "illumination",
            "warmth",
            "atmosphericSoftness",
            "colorRichness",
            "ambientMotion"
        };

        private static readonly string[] RequiredCommandProperties =
        {
            "sessionId",
            "command"
        };

        private readonly string expectedSchemaVersion;

        public SessionRelayInboundMessageParser(string expectedSchemaVersion)
        {
            if (string.IsNullOrWhiteSpace(expectedSchemaVersion))
            {
                throw new ArgumentException(
                    "An expected relay schema version is required.",
                    nameof(expectedSchemaVersion));
            }

            this.expectedSchemaVersion = expectedSchemaVersion.Trim();
        }

        public string ExpectedSchemaVersion => expectedSchemaVersion;

        public bool TryParse(
            string json,
            out SessionRelayInboundMessage message,
            out SessionRelayMessageParseReasonCode reasonCode)
        {
            message = null;
            reasonCode = SessionRelayMessageParseReasonCode.Accepted;
            if (string.IsNullOrWhiteSpace(json))
            {
                reasonCode = SessionRelayMessageParseReasonCode.PayloadEmpty;
                return false;
            }

            SessionRelayEnvelopeDto envelope;
            try
            {
                envelope = JsonUtility.FromJson<SessionRelayEnvelopeDto>(json);
            }
            catch (ArgumentException)
            {
                reasonCode = SessionRelayMessageParseReasonCode.JsonMalformed;
                return false;
            }

            if (envelope == null)
            {
                reasonCode = SessionRelayMessageParseReasonCode.JsonMalformed;
                return false;
            }

            if (!HasNonNullProperties(json, RequiredEnvelopeProperties)
                || envelope.payload == null
                || string.IsNullOrWhiteSpace(envelope.messageId)
                || string.IsNullOrWhiteSpace(envelope.messageType)
                || string.IsNullOrWhiteSpace(envelope.schemaVersion))
            {
                reasonCode =
                    SessionRelayMessageParseReasonCode.RequiredFieldMissing;
                return false;
            }

            if (!string.Equals(
                    envelope.schemaVersion,
                    expectedSchemaVersion,
                    StringComparison.Ordinal))
            {
                reasonCode =
                    SessionRelayMessageParseReasonCode.SchemaVersionMismatch;
                return false;
            }

            switch (envelope.messageType)
            {
                case ConfigurationMessageType:
                    return TryMapConfiguration(
                        json,
                        envelope,
                        out message,
                        out reasonCode);
                case CommandMessageType:
                    return TryMapCommand(
                        json,
                        envelope,
                        out message,
                        out reasonCode);
                default:
                    reasonCode = SessionRelayMessageParseReasonCode
                        .MessageTypeUnsupported;
                    return false;
            }
        }

        private static bool TryMapConfiguration(
            string json,
            SessionRelayEnvelopeDto envelope,
            out SessionRelayInboundMessage message,
            out SessionRelayMessageParseReasonCode reasonCode)
        {
            message = null;
            reasonCode = SessionRelayMessageParseReasonCode.Accepted;
            if (!HasNonNullProperties(
                    json,
                    RequiredConfigurationProperties)
                || string.IsNullOrWhiteSpace(envelope.payload.sessionId)
                || string.IsNullOrWhiteSpace(
                    envelope.payload.participantPseudonym)
                || string.IsNullOrWhiteSpace(envelope.payload.sceneId)
                || envelope.payload.preferredEnvironment == null)
            {
                reasonCode =
                    SessionRelayMessageParseReasonCode.RequiredFieldMissing;
                return false;
            }

            var preferenceDto = envelope.payload.preferredEnvironment;
            EnvironmentState preference;
            try
            {
                preference = new EnvironmentState(
                    preferenceDto.illumination,
                    preferenceDto.warmth,
                    preferenceDto.atmosphericSoftness,
                    preferenceDto.colorRichness,
                    preferenceDto.ambientMotion);
            }
            catch (ArgumentOutOfRangeException)
            {
                reasonCode = SessionRelayMessageParseReasonCode
                    .PreferredEnvironmentInvalid;
                return false;
            }

            if (!preference.IsNormalized)
            {
                reasonCode = SessionRelayMessageParseReasonCode
                    .PreferredEnvironmentInvalid;
                return false;
            }

            try
            {
                var configuration = new SessionRelayConfigurationMessage(
                    envelope.schemaVersion,
                    envelope.messageId,
                    envelope.payload.sessionId,
                    envelope.payload.participantPseudonym,
                    envelope.payload.sceneId,
                    preference);
                message = SessionRelayInboundMessage.ForConfiguration(
                    configuration);
                return true;
            }
            catch (ArgumentException)
            {
                reasonCode =
                    SessionRelayMessageParseReasonCode.RequiredFieldMissing;
                return false;
            }
        }

        private static bool TryMapCommand(
            string json,
            SessionRelayEnvelopeDto envelope,
            out SessionRelayInboundMessage message,
            out SessionRelayMessageParseReasonCode reasonCode)
        {
            message = null;
            reasonCode = SessionRelayMessageParseReasonCode.Accepted;
            if (!HasNonNullProperties(json, RequiredCommandProperties)
                || string.IsNullOrWhiteSpace(envelope.payload.sessionId)
                || string.IsNullOrWhiteSpace(envelope.payload.command))
            {
                reasonCode =
                    SessionRelayMessageParseReasonCode.RequiredFieldMissing;
                return false;
            }

            if (!TryMapCommandType(
                    envelope.payload.command,
                    out var commandType))
            {
                reasonCode = SessionRelayMessageParseReasonCode
                    .CommandTypeUnsupported;
                return false;
            }

            var command = new SessionRelayCommandMessage(
                envelope.schemaVersion,
                envelope.messageId,
                envelope.payload.sessionId,
                commandType);
            message = SessionRelayInboundMessage.ForCommand(command);
            return true;
        }

        private static bool TryMapCommandType(
            string command,
            out SessionCommandType commandType)
        {
            switch (command)
            {
                case "start":
                    commandType = SessionCommandType.Start;
                    return true;
                case "pause":
                    commandType = SessionCommandType.Pause;
                    return true;
                case "resume":
                    commandType = SessionCommandType.Resume;
                    return true;
                case "stop":
                    commandType = SessionCommandType.Stop;
                    return true;
                case "emergency_stop":
                    commandType = SessionCommandType.EmergencyStop;
                    return true;
                default:
                    commandType = default;
                    return false;
            }
        }

        private static bool HasNonNullProperties(
            string json,
            string[] propertyNames)
        {
            for (var index = 0; index < propertyNames.Length; index++)
            {
                if (!HasNonNullProperty(json, propertyNames[index]))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool HasNonNullProperty(
            string json,
            string propertyName)
        {
            var token = "\"" + propertyName + "\"";
            var searchStartIndex = 0;
            while (searchStartIndex < json.Length)
            {
                var propertyIndex = json.IndexOf(
                    token,
                    searchStartIndex,
                    StringComparison.Ordinal);
                if (propertyIndex < 0)
                {
                    return false;
                }

                var valueIndex = propertyIndex + token.Length;
                while (valueIndex < json.Length
                    && char.IsWhiteSpace(json[valueIndex]))
                {
                    valueIndex++;
                }

                if (valueIndex < json.Length && json[valueIndex] == ':')
                {
                    valueIndex++;
                    while (valueIndex < json.Length
                        && char.IsWhiteSpace(json[valueIndex]))
                    {
                        valueIndex++;
                    }

                    return valueIndex < json.Length
                        && !StartsWithJsonNull(json, valueIndex);
                }

                searchStartIndex = propertyIndex + token.Length;
            }

            return false;
        }

        private static bool StartsWithJsonNull(string json, int valueIndex)
        {
            const string NullToken = "null";
            return valueIndex + NullToken.Length <= json.Length
                && string.Compare(
                    json,
                    valueIndex,
                    NullToken,
                    0,
                    NullToken.Length,
                    StringComparison.Ordinal) == 0;
        }

#pragma warning disable 0649
        [Serializable]
        private sealed class SessionRelayEnvelopeDto
        {
            public string schemaVersion;
            public string messageId;
            public string messageType;
            public SessionRelayPayloadDto payload;
        }

        [Serializable]
        private sealed class SessionRelayPayloadDto
        {
            public string sessionId;
            public string participantPseudonym;
            public string sceneId;
            public PreferredEnvironmentDto preferredEnvironment;
            public string command;
        }

        [Serializable]
        private sealed class PreferredEnvironmentDto
        {
            public float illumination;
            public float warmth;
            public float atmosphericSoftness;
            public float colorRichness;
            public float ambientMotion;
        }
#pragma warning restore 0649
    }
}
