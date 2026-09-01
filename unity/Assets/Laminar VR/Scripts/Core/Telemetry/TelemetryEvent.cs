using System;
using System.Collections.Generic;

namespace LaminarVR.AdaptiveMeditation.Telemetry
{
    public sealed class TelemetryEvent
    {
        private readonly TelemetryField[] fields;

        public TelemetryEvent(
            string eventSchemaId,
            string eventSchemaVersion,
            string loggingConfigurationId,
            int loggingConfigurationVersion,
            string eventId,
            long sequenceNumber,
            string sessionId,
            string participantPseudonym,
            string eventType,
            double utcTimestampUnixSeconds,
            double sessionElapsedSeconds,
            bool critical,
            IReadOnlyList<TelemetryField> fields)
        {
            EventSchemaId = RequireText(eventSchemaId, nameof(eventSchemaId));
            EventSchemaVersion = RequireText(
                eventSchemaVersion,
                nameof(eventSchemaVersion));
            LoggingConfigurationId = RequireText(
                loggingConfigurationId,
                nameof(loggingConfigurationId));
            if (loggingConfigurationVersion < 1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(loggingConfigurationVersion),
                    loggingConfigurationVersion,
                    "Logging configuration version must be at least 1.");
            }

            EventId = RequireText(eventId, nameof(eventId));
            if (sequenceNumber < 1L)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(sequenceNumber),
                    sequenceNumber,
                    "Telemetry sequence number must be at least 1.");
            }

            SessionId = RequireText(sessionId, nameof(sessionId));
            ParticipantPseudonym = RequireText(
                participantPseudonym,
                nameof(participantPseudonym));
            EventType = RequireText(eventType, nameof(eventType));
            ValidateFinite(
                utcTimestampUnixSeconds,
                nameof(utcTimestampUnixSeconds));
            ValidateNonNegativeFinite(
                sessionElapsedSeconds,
                nameof(sessionElapsedSeconds));
            if (fields == null)
            {
                throw new ArgumentNullException(nameof(fields));
            }

            this.fields = new TelemetryField[fields.Count];
            for (var index = 0; index < fields.Count; index++)
            {
                var field = fields[index];
                if (string.IsNullOrEmpty(field.Name))
                {
                    throw new ArgumentException(
                        "Telemetry fields must be initialized.",
                        nameof(fields));
                }

                for (var earlierIndex = 0;
                    earlierIndex < index;
                    earlierIndex++)
                {
                    if (string.Equals(
                            this.fields[earlierIndex].Name,
                            field.Name,
                            StringComparison.Ordinal))
                    {
                        throw new ArgumentException(
                            "Telemetry field names must be unique within an event.",
                            nameof(fields));
                    }
                }

                this.fields[index] = field;
            }

            LoggingConfigurationVersion = loggingConfigurationVersion;
            SequenceNumber = sequenceNumber;
            UtcTimestampUnixSeconds = utcTimestampUnixSeconds;
            SessionElapsedSeconds = sessionElapsedSeconds;
            Critical = critical;
        }

        public string EventSchemaId { get; }

        public string EventSchemaVersion { get; }

        public string LoggingConfigurationId { get; }

        public int LoggingConfigurationVersion { get; }

        public string EventId { get; }

        public long SequenceNumber { get; }

        public string SessionId { get; }

        public string ParticipantPseudonym { get; }

        public string EventType { get; }

        public double UtcTimestampUnixSeconds { get; }

        public double SessionElapsedSeconds { get; }

        public bool Critical { get; }

        public int FieldCount => fields.Length;

        public TelemetryField GetField(int index)
        {
            return fields[index];
        }

        public TelemetryField[] CopyFields()
        {
            var copy = new TelemetryField[fields.Length];
            Array.Copy(fields, copy, fields.Length);
            return copy;
        }

        private static string RequireText(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException(
                    "A non-empty value is required.",
                    parameterName);
            }

            return value.Trim();
        }

        private static void ValidateFinite(double value, string parameterName)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                throw new ArgumentOutOfRangeException(
                    parameterName,
                    value,
                    "Telemetry timestamps must be finite.");
            }
        }

        private static void ValidateNonNegativeFinite(
            double value,
            string parameterName)
        {
            ValidateFinite(value, parameterName);
            if (value < 0d)
            {
                throw new ArgumentOutOfRangeException(
                    parameterName,
                    value,
                    "Session elapsed time must be non-negative.");
            }
        }
    }
}
