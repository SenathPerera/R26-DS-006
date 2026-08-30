using System;

namespace LaminarVR.AdaptiveMeditation.Policy
{
    public sealed class FeatureVector
    {
        private readonly double[] values;

        public FeatureVector(string schemaVersion, double[] values)
        {
            if (string.IsNullOrWhiteSpace(schemaVersion))
            {
                throw new ArgumentException(
                    "Feature schema version is required.",
                    nameof(schemaVersion));
            }

            if (values == null)
            {
                throw new ArgumentNullException(nameof(values));
            }

            if (values.Length == 0)
            {
                throw new ArgumentException(
                    "A feature vector must contain at least one value.",
                    nameof(values));
            }

            this.values = new double[values.Length];
            for (var index = 0; index < values.Length; index++)
            {
                var value = values[index];
                if (double.IsNaN(value) || double.IsInfinity(value))
                {
                    throw new ArgumentException(
                        "Feature values must be finite.",
                        nameof(values));
                }

                this.values[index] = value;
            }

            SchemaVersion = schemaVersion.Trim();
        }

        public string SchemaVersion { get; }

        public int Count => values.Length;

        public double this[int index] => values[index];

        public double[] ToArray()
        {
            var copy = new double[values.Length];
            Array.Copy(values, copy, values.Length);
            return copy;
        }

        public void CopyTo(double[] destination, int destinationIndex)
        {
            if (destination == null)
            {
                throw new ArgumentNullException(nameof(destination));
            }

            Array.Copy(values, 0, destination, destinationIndex, values.Length);
        }
    }
}

