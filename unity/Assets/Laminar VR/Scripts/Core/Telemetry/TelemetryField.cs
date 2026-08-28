using System;

namespace LaminarVR.AdaptiveMeditation.Telemetry
{
    public enum TelemetryFieldValueType
    {
        Null,
        Boolean,
        Integer,
        Number,
        String
    }

    public readonly struct TelemetryField
    {
        private TelemetryField(
            string name,
            TelemetryFieldValueType valueType,
            bool booleanValue,
            long integerValue,
            double numberValue,
            string stringValue)
        {
            Name = ValidateName(name);
            ValueType = valueType;
            BooleanValue = booleanValue;
            IntegerValue = integerValue;
            NumberValue = numberValue;
            StringValue = stringValue;
        }

        public string Name { get; }

        public TelemetryFieldValueType ValueType { get; }

        public bool BooleanValue { get; }

        public long IntegerValue { get; }

        public double NumberValue { get; }

        public string StringValue { get; }

        public static TelemetryField Null(string name)
        {
            return new TelemetryField(
                name,
                TelemetryFieldValueType.Null,
                false,
                0L,
                0d,
                null);
        }

        public static TelemetryField Boolean(string name, bool value)
        {
            return new TelemetryField(
                name,
                TelemetryFieldValueType.Boolean,
                value,
                0L,
                0d,
                null);
        }

        public static TelemetryField Integer(string name, long value)
        {
            return new TelemetryField(
                name,
                TelemetryFieldValueType.Integer,
                false,
                value,
                0d,
                null);
        }

        public static TelemetryField Number(string name, double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    value,
                    "Telemetry numbers must be finite.");
            }

            return new TelemetryField(
                name,
                TelemetryFieldValueType.Number,
                false,
                0L,
                value,
                null);
        }

        public static TelemetryField String(string name, string value)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            return new TelemetryField(
                name,
                TelemetryFieldValueType.String,
                false,
                0L,
                0d,
                value);
        }

        private static string ValidateName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException(
                    "Telemetry field name is required.",
                    nameof(name));
            }

            var trimmed = name.Trim();
            if (!IsFieldNameStart(trimmed[0]))
            {
                throw new ArgumentException(
                    "Telemetry field names must start with a letter or underscore.",
                    nameof(name));
            }

            for (var index = 1; index < trimmed.Length; index++)
            {
                var character = trimmed[index];
                if (!char.IsLetterOrDigit(character)
                    && character != '_'
                    && character != '-'
                    && character != '.')
                {
                    throw new ArgumentException(
                        "Telemetry field names contain an unsupported character.",
                        nameof(name));
                }
            }

            return trimmed;
        }

        private static bool IsFieldNameStart(char character)
        {
            return char.IsLetter(character) || character == '_';
        }
    }
}
