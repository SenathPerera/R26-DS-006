using System;
using System.Collections.Generic;
using LaminarVR.AdaptiveMeditation.Networking;
using LaminarVR.AdaptiveMeditation.Session;
using LaminarVR.AdaptiveMeditation.Telemetry;
using UnityEngine;

namespace LaminarVR.AdaptiveMeditation.Runtime.Networking
{
    public enum SessionRelayPairingParseReasonCode
    {
        Accepted,
        PayloadEmpty,
        JsonMalformed,
        RequiredFieldMissing,
        SchemaVersionMismatch,
        MessageTypeUnsupported,
        PairingRejected
    }

    public sealed class SessionRelayJsonCodec
    {
        private const string PairingRequestMessageType = "pairing_request";
        private const string PairingResultMessageType = "pairing_result";
        private const string QuestStateMessageType = "quest_state";
        private const string TelemetryBatchMessageType =
            "visual_telemetry_batch";

        private readonly string schemaVersion;

        public SessionRelayJsonCodec(string schemaVersion)
        {
            if (string.IsNullOrWhiteSpace(schemaVersion))
            {
                throw new ArgumentException(
                    "A relay schema version is required.",
                    nameof(schemaVersion));
            }

            this.schemaVersion = schemaVersion.Trim();
        }

        public string SchemaVersion => schemaVersion;

        public string SerializePairingRequest(
            string messageId,
            string pairingCode,
            string questClientId,
            string appVersion)
        {
            return JsonUtility.ToJson(
                new PairingRequestEnvelopeDto
                {
                    schemaVersion = schemaVersion,
                    messageId = RequireText(messageId, nameof(messageId)),
                    messageType = PairingRequestMessageType,
                    payload = new PairingRequestPayloadDto
                    {
                        pairingCode = RequireText(
                            pairingCode,
                            nameof(pairingCode)),
                        clientRole = "quest",
                        questClientId = RequireText(
                            questClientId,
                            nameof(questClientId)),
                        appVersion = RequireText(
                            appVersion,
                            nameof(appVersion))
                    }
                });
        }

        public bool TryParsePairingResult(
            string json,
            out SessionRelayPairingResult result,
            out SessionRelayPairingParseReasonCode reasonCode)
        {
            result = null;
            reasonCode = SessionRelayPairingParseReasonCode.Accepted;
            if (string.IsNullOrWhiteSpace(json))
            {
                reasonCode = SessionRelayPairingParseReasonCode.PayloadEmpty;
                return false;
            }

            PairingResultEnvelopeDto envelope;
            try
            {
                envelope = JsonUtility.FromJson<PairingResultEnvelopeDto>(json);
            }
            catch (ArgumentException)
            {
                reasonCode = SessionRelayPairingParseReasonCode.JsonMalformed;
                return false;
            }

            if (envelope == null)
            {
                reasonCode = SessionRelayPairingParseReasonCode.JsonMalformed;
                return false;
            }

            if (!HasNonNullProperty(json, "schemaVersion")
                || !HasNonNullProperty(json, "messageId")
                || !HasNonNullProperty(json, "messageType")
                || !HasNonNullProperty(json, "payload")
                || !HasNonNullProperty(json, "accepted")
                || envelope.payload == null
                || string.IsNullOrWhiteSpace(envelope.schemaVersion)
                || string.IsNullOrWhiteSpace(envelope.messageId)
                || string.IsNullOrWhiteSpace(envelope.messageType))
            {
                reasonCode =
                    SessionRelayPairingParseReasonCode.RequiredFieldMissing;
                return false;
            }

            if (!string.Equals(
                    envelope.schemaVersion,
                    schemaVersion,
                    StringComparison.Ordinal))
            {
                reasonCode = SessionRelayPairingParseReasonCode
                    .SchemaVersionMismatch;
                return false;
            }

            if (!string.Equals(
                    envelope.messageType,
                    PairingResultMessageType,
                    StringComparison.Ordinal))
            {
                reasonCode = SessionRelayPairingParseReasonCode
                    .MessageTypeUnsupported;
                return false;
            }

            if (envelope.payload.accepted)
            {
                if (string.IsNullOrWhiteSpace(envelope.payload.sessionId))
                {
                    reasonCode = SessionRelayPairingParseReasonCode
                        .RequiredFieldMissing;
                    return false;
                }

                result = SessionRelayPairingResult.Accept(
                    envelope.schemaVersion,
                    envelope.messageId,
                    envelope.payload.sessionId);
                return true;
            }

            if (string.IsNullOrWhiteSpace(envelope.payload.rejectionCode))
            {
                reasonCode =
                    SessionRelayPairingParseReasonCode.RequiredFieldMissing;
                return false;
            }

            result = SessionRelayPairingResult.Reject(
                envelope.schemaVersion,
                envelope.messageId,
                envelope.payload.rejectionCode);
            reasonCode = SessionRelayPairingParseReasonCode.PairingRejected;
            return true;
        }

        public string SerializeQuestState(SessionRelayQuestState state)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            return JsonUtility.ToJson(
                new QuestStateEnvelopeDto
                {
                    schemaVersion = schemaVersion,
                    messageId = state.MessageId,
                    messageType = QuestStateMessageType,
                    payload = new QuestStatePayloadDto
                    {
                        sessionId = state.SessionId,
                        phase = MapPhase(state.Phase),
                        timestamp = state.UtcTimestampUnixSeconds
                    }
                });
        }

        public string SerializeTelemetryBatch(
            string messageId,
            IReadOnlyList<TelemetryEvent> telemetryEvents)
        {
            if (telemetryEvents == null)
            {
                throw new ArgumentNullException(nameof(telemetryEvents));
            }

            if (telemetryEvents.Count == 0)
            {
                throw new ArgumentException(
                    "A telemetry batch must contain at least one event.",
                    nameof(telemetryEvents));
            }

            var eventDtos = new TelemetryEventDto[telemetryEvents.Count];
            for (var index = 0; index < telemetryEvents.Count; index++)
            {
                var telemetryEvent = telemetryEvents[index]
                    ?? throw new ArgumentException(
                        "A telemetry batch cannot contain null events.",
                        nameof(telemetryEvents));
                eventDtos[index] = MapTelemetryEvent(telemetryEvent);
            }

            return JsonUtility.ToJson(
                new TelemetryBatchEnvelopeDto
                {
                    schemaVersion = schemaVersion,
                    messageId = RequireText(messageId, nameof(messageId)),
                    messageType = TelemetryBatchMessageType,
                    payload = new TelemetryBatchPayloadDto
                    {
                        events = eventDtos
                    }
                });
        }

        private static TelemetryEventDto MapTelemetryEvent(
            TelemetryEvent telemetryEvent)
        {
            var fields = new TelemetryFieldDto[telemetryEvent.FieldCount];
            for (var index = 0; index < fields.Length; index++)
            {
                var field = telemetryEvent.GetField(index);
                fields[index] = new TelemetryFieldDto
                {
                    name = field.Name,
                    valueType = MapFieldType(field.ValueType),
                    booleanValue = field.BooleanValue,
                    integerValue = field.IntegerValue,
                    numberValue = field.NumberValue,
                    stringValue = field.StringValue
                };
            }

            return new TelemetryEventDto
            {
                eventSchemaId = telemetryEvent.EventSchemaId,
                eventSchemaVersion = telemetryEvent.EventSchemaVersion,
                loggingConfigurationId = telemetryEvent.LoggingConfigurationId,
                loggingConfigurationVersion =
                    telemetryEvent.LoggingConfigurationVersion,
                eventId = telemetryEvent.EventId,
                sequenceNumber = telemetryEvent.SequenceNumber,
                sessionId = telemetryEvent.SessionId,
                participantPseudonym = telemetryEvent.ParticipantPseudonym,
                eventType = telemetryEvent.EventType,
                timestamp = telemetryEvent.UtcTimestampUnixSeconds,
                sessionElapsedSeconds = telemetryEvent.SessionElapsedSeconds,
                critical = telemetryEvent.Critical,
                fields = fields
            };
        }

        private static string MapFieldType(TelemetryFieldValueType valueType)
        {
            switch (valueType)
            {
                case TelemetryFieldValueType.Null:
                    return "null";
                case TelemetryFieldValueType.Boolean:
                    return "boolean";
                case TelemetryFieldValueType.Integer:
                    return "integer";
                case TelemetryFieldValueType.Number:
                    return "number";
                case TelemetryFieldValueType.String:
                    return "string";
                default:
                    throw new ArgumentOutOfRangeException(nameof(valueType));
            }
        }

        private static string MapPhase(VrSessionPhase phase)
        {
            switch (phase)
            {
                case VrSessionPhase.Boot:
                    return "boot";
                case VrSessionPhase.AwaitingConfig:
                    return "awaiting_config";
                case VrSessionPhase.LoadingScene:
                    return "loading_scene";
                case VrSessionPhase.Ready:
                    return "ready";
                case VrSessionPhase.Acclimatization:
                    return "acclimatization";
                case VrSessionPhase.Adaptive:
                    return "adaptive";
                case VrSessionPhase.Paused:
                    return "paused";
                case VrSessionPhase.Stabilization:
                    return "stabilization";
                case VrSessionPhase.Completed:
                    return "completed";
                case VrSessionPhase.Aborted:
                    return "aborted";
                default:
                    throw new ArgumentOutOfRangeException(nameof(phase));
            }
        }

        private static string RequireText(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException(
                    "A non-empty relay message value is required.",
                    parameterName);
            }

            return value.Trim();
        }

        private static bool HasNonNullProperty(
            string json,
            string propertyName)
        {
            var token = "\"" + propertyName + "\"";
            var propertyIndex = json.IndexOf(
                token,
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

            if (valueIndex >= json.Length || json[valueIndex] != ':')
            {
                return false;
            }

            valueIndex++;
            while (valueIndex < json.Length
                && char.IsWhiteSpace(json[valueIndex]))
            {
                valueIndex++;
            }

            const string NullToken = "null";
            return valueIndex < json.Length
                && (valueIndex + NullToken.Length > json.Length
                    || string.Compare(
                        json,
                        valueIndex,
                        NullToken,
                        0,
                        NullToken.Length,
                        StringComparison.Ordinal) != 0);
        }

#pragma warning disable 0649
        [Serializable]
        private sealed class PairingRequestEnvelopeDto
        {
            public string schemaVersion;
            public string messageId;
            public string messageType;
            public PairingRequestPayloadDto payload;
        }

        [Serializable]
        private sealed class PairingRequestPayloadDto
        {
            public string pairingCode;
            public string clientRole;
            public string questClientId;
            public string appVersion;
        }

        [Serializable]
        private sealed class PairingResultEnvelopeDto
        {
            public string schemaVersion;
            public string messageId;
            public string messageType;
            public PairingResultPayloadDto payload;
        }

        [Serializable]
        private sealed class PairingResultPayloadDto
        {
            public bool accepted;
            public string sessionId;
            public string rejectionCode;
        }

        [Serializable]
        private sealed class QuestStateEnvelopeDto
        {
            public string schemaVersion;
            public string messageId;
            public string messageType;
            public QuestStatePayloadDto payload;
        }

        [Serializable]
        private sealed class QuestStatePayloadDto
        {
            public string sessionId;
            public string phase;
            public double timestamp;
        }

        [Serializable]
        private sealed class TelemetryBatchEnvelopeDto
        {
            public string schemaVersion;
            public string messageId;
            public string messageType;
            public TelemetryBatchPayloadDto payload;
        }

        [Serializable]
        private sealed class TelemetryBatchPayloadDto
        {
            public TelemetryEventDto[] events;
        }

        [Serializable]
        private sealed class TelemetryEventDto
        {
            public string eventSchemaId;
            public string eventSchemaVersion;
            public string loggingConfigurationId;
            public int loggingConfigurationVersion;
            public string eventId;
            public long sequenceNumber;
            public string sessionId;
            public string participantPseudonym;
            public string eventType;
            public double timestamp;
            public double sessionElapsedSeconds;
            public bool critical;
            public TelemetryFieldDto[] fields;
        }

        [Serializable]
        private sealed class TelemetryFieldDto
        {
            public string name;
            public string valueType;
            public bool booleanValue;
            public long integerValue;
            public double numberValue;
            public string stringValue;
        }
#pragma warning restore 0649
    }
}
