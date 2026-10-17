using System;

namespace LaminarVR.AdaptiveMeditation.ResearchEvaluation
{
    public interface IEvaluationNetworkSchedule
    {
        bool IsConnected(double monotonicTimeSeconds);

        double? GetNextChangeAfter(double monotonicTimeSeconds);
    }

    public sealed class AlwaysConnectedEvaluationNetworkSchedule
        : IEvaluationNetworkSchedule
    {
        public static readonly AlwaysConnectedEvaluationNetworkSchedule
            Instance = new AlwaysConnectedEvaluationNetworkSchedule();

        private AlwaysConnectedEvaluationNetworkSchedule()
        {
        }

        public bool IsConnected(double monotonicTimeSeconds)
        {
            ValidateTime(monotonicTimeSeconds);
            return true;
        }

        public double? GetNextChangeAfter(double monotonicTimeSeconds)
        {
            ValidateTime(monotonicTimeSeconds);
            return null;
        }

        private static void ValidateTime(double value)
        {
            if (double.IsNaN(value)
                || double.IsInfinity(value)
                || value < 0d)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }
        }
    }

    public sealed class IntervalEvaluationNetworkSchedule
        : IEvaluationNetworkSchedule
    {
        public IntervalEvaluationNetworkSchedule(
            double disconnectedStartSeconds,
            double disconnectedEndSeconds)
        {
            if (!IsFiniteNonNegative(disconnectedStartSeconds)
                || !IsFiniteNonNegative(disconnectedEndSeconds)
                || disconnectedEndSeconds <= disconnectedStartSeconds)
            {
                throw new ArgumentException(
                    "A finite, positive disconnection interval is required.");
            }

            DisconnectedStartSeconds = disconnectedStartSeconds;
            DisconnectedEndSeconds = disconnectedEndSeconds;
        }

        public double DisconnectedStartSeconds { get; }

        public double DisconnectedEndSeconds { get; }

        public bool IsConnected(double monotonicTimeSeconds)
        {
            ValidateTime(monotonicTimeSeconds);
            return monotonicTimeSeconds < DisconnectedStartSeconds
                || monotonicTimeSeconds >= DisconnectedEndSeconds;
        }

        public double? GetNextChangeAfter(double monotonicTimeSeconds)
        {
            ValidateTime(monotonicTimeSeconds);
            if (monotonicTimeSeconds < DisconnectedStartSeconds)
            {
                return DisconnectedStartSeconds;
            }

            return monotonicTimeSeconds < DisconnectedEndSeconds
                ? DisconnectedEndSeconds
                : (double?)null;
        }

        private static void ValidateTime(double value)
        {
            if (!IsFiniteNonNegative(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }
        }

        private static bool IsFiniteNonNegative(double value)
        {
            return !double.IsNaN(value)
                && !double.IsInfinity(value)
                && value >= 0d;
        }
    }
}
