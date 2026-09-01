using System;
using System.Globalization;
using System.Text;
using LaminarVR.AdaptiveMeditation.Telemetry;

namespace LaminarVR.AdaptiveMeditation.Runtime.Telemetry
{
    public sealed class JsonLinesTelemetryFormatter
    {
        public string Format(TelemetryEvent telemetryEvent)
        {
            if (telemetryEvent == null)
            {
                throw new ArgumentNullException(nameof(telemetryEvent));
            }

            var builder = new StringBuilder(512);
            builder.Append('{');
            AppendStringProperty(
                builder,
                "schemaId",
                telemetryEvent.EventSchemaId);
            AppendStringProperty(
                builder,
                "schemaVersion",
                telemetryEvent.EventSchemaVersion);
            AppendStringProperty(builder, "eventId", telemetryEvent.EventId);
            AppendIntegerProperty(
                builder,
                "sequenceNumber",
                telemetryEvent.SequenceNumber);
            AppendStringProperty(builder, "sessionId", telemetryEvent.SessionId);
            AppendStringProperty(
                builder,
                "participantPseudonym",
                telemetryEvent.ParticipantPseudonym);
            AppendStringProperty(builder, "eventType", telemetryEvent.EventType);
            AppendNumberProperty(
                builder,
                "utcTimestampUnixSeconds",
                telemetryEvent.UtcTimestampUnixSeconds);
            AppendNumberProperty(
                builder,
                "sessionElapsedSeconds",
                telemetryEvent.SessionElapsedSeconds);
            AppendBooleanProperty(builder, "critical", telemetryEvent.Critical);
            AppendStringProperty(
                builder,
                "loggingConfigurationId",
                telemetryEvent.LoggingConfigurationId);
            AppendIntegerProperty(
                builder,
                "loggingConfigurationVersion",
                telemetryEvent.LoggingConfigurationVersion);
            builder.Append(",\"data\":{");
            for (var index = 0; index < telemetryEvent.FieldCount; index++)
            {
                if (index > 0)
                {
                    builder.Append(',');
                }

                AppendEscapedString(
                    builder,
                    telemetryEvent.GetField(index).Name);
                builder.Append(':');
                AppendFieldValue(builder, telemetryEvent.GetField(index));
            }

            builder.Append("}}");
            return builder.ToString();
        }

        private static void AppendFieldValue(
            StringBuilder builder,
            TelemetryField field)
        {
            switch (field.ValueType)
            {
                case TelemetryFieldValueType.Null:
                    builder.Append("null");
                    return;
                case TelemetryFieldValueType.Boolean:
                    builder.Append(field.BooleanValue ? "true" : "false");
                    return;
                case TelemetryFieldValueType.Integer:
                    builder.Append(
                        field.IntegerValue.ToString(CultureInfo.InvariantCulture));
                    return;
                case TelemetryFieldValueType.Number:
                    builder.Append(
                        field.NumberValue.ToString(
                            "R",
                            CultureInfo.InvariantCulture));
                    return;
                case TelemetryFieldValueType.String:
                    AppendEscapedString(builder, field.StringValue);
                    return;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(field),
                        field.ValueType,
                        "Unsupported telemetry field value type.");
            }
        }

        private static void AppendStringProperty(
            StringBuilder builder,
            string name,
            string value)
        {
            AppendSeparator(builder);
            AppendEscapedString(builder, name);
            builder.Append(':');
            AppendEscapedString(builder, value);
        }

        private static void AppendIntegerProperty(
            StringBuilder builder,
            string name,
            long value)
        {
            AppendSeparator(builder);
            AppendEscapedString(builder, name);
            builder.Append(':');
            builder.Append(value.ToString(CultureInfo.InvariantCulture));
        }

        private static void AppendNumberProperty(
            StringBuilder builder,
            string name,
            double value)
        {
            AppendSeparator(builder);
            AppendEscapedString(builder, name);
            builder.Append(':');
            builder.Append(value.ToString("R", CultureInfo.InvariantCulture));
        }

        private static void AppendBooleanProperty(
            StringBuilder builder,
            string name,
            bool value)
        {
            AppendSeparator(builder);
            AppendEscapedString(builder, name);
            builder.Append(':');
            builder.Append(value ? "true" : "false");
        }

        private static void AppendSeparator(StringBuilder builder)
        {
            if (builder.Length > 1)
            {
                builder.Append(',');
            }
        }

        private static void AppendEscapedString(
            StringBuilder builder,
            string value)
        {
            builder.Append('"');
            for (var index = 0; index < value.Length; index++)
            {
                var character = value[index];
                switch (character)
                {
                    case '"': builder.Append("\\\""); break;
                    case '\\': builder.Append("\\\\"); break;
                    case '\b': builder.Append("\\b"); break;
                    case '\f': builder.Append("\\f"); break;
                    case '\n': builder.Append("\\n"); break;
                    case '\r': builder.Append("\\r"); break;
                    case '\t': builder.Append("\\t"); break;
                    default:
                        if (character < 0x20)
                        {
                            builder.Append("\\u");
                            builder.Append(
                                ((int)character).ToString(
                                    "x4",
                                    CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            builder.Append(character);
                        }

                        break;
                }
            }

            builder.Append('"');
        }
    }
}
