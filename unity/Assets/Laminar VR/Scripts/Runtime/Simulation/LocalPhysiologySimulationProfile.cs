#if UNITY_EDITOR || DEVELOPMENT_BUILD
using LaminarVR.AdaptiveMeditation.Physiology;
using UnityEngine;

namespace LaminarVR.AdaptiveMeditation.Runtime.Simulation
{
    [CreateAssetMenu(
        fileName = "LocalPhysiologySimulationProfile",
        menuName = "Adaptive Meditation/Development/Local Physiology Simulation Profile")]
    public sealed class LocalPhysiologySimulationProfile : ScriptableObject
    {
        [Tooltip(
            "Explicitly enables this development-only mock. This asset must never "
            + "be used as participant physiology in a study build.")]
        [SerializeField]
        private bool developmentSimulationEnabled = false;

        [Header("Stream Timing")]
        [SerializeField, Min(0f)]
        private float emissionIntervalSeconds = 0f;

        [SerializeField, Min(0f)]
        private float windowDurationSeconds = 0f;

        [Tooltip(
            "Development fault injection: negative values create stale windows; "
            + "positive values create future-dated windows.")]
        [SerializeField]
        private float sourceTimestampOffsetSeconds = 0f;

        [Header("Physiology")]
        [SerializeField]
        private float heartRateBpm = 0f;

        [SerializeField]
        private bool includeRmssd = false;

        [SerializeField]
        private float rmssdMs = 0f;

        [SerializeField]
        private bool includeSdnn = false;

        [SerializeField]
        private float sdnnMs = 0f;

        [Header("Authoritative Point Stress Decision")]
        [SerializeField, Range(0, 3)]
        private int pointStressLevel = 0;

        [SerializeField]
        private string stressLabel = string.Empty;

        [SerializeField]
        private float stressConfidence = 0f;

        [Header("Supplementary Stress Values")]
        [SerializeField]
        private float level0Probability = 0f;

        [SerializeField]
        private float level1Probability = 0f;

        [SerializeField]
        private float level2Probability = 0f;

        [SerializeField]
        private float level3Probability = 0f;

        [SerializeField]
        private float continuousStressScore = 0f;

        [SerializeField]
        private float signalQuality = 0f;

        public bool TryGetEmissionInterval(
            out double intervalSeconds,
            out string validationError)
        {
            intervalSeconds = emissionIntervalSeconds;

            if (!developmentSimulationEnabled)
            {
                validationError = "Development physiology simulation is not enabled.";
                return false;
            }

            if (!IsFinitePositive(emissionIntervalSeconds))
            {
                validationError =
                    "Simulation emission interval must be finite and greater than 0.";
                return false;
            }

            if (!IsFinitePositive(windowDurationSeconds))
            {
                validationError =
                    "Simulation window duration must be finite and greater than 0.";
                return false;
            }

            if (float.IsNaN(sourceTimestampOffsetSeconds)
                || float.IsInfinity(sourceTimestampOffsetSeconds))
            {
                validationError = "Simulation timestamp offset must be finite.";
                return false;
            }

            validationError = string.Empty;
            return true;
        }

        public PhysiologyWindow CreateWindow(double receivedUtcUnixSeconds)
        {
            var windowEndUtcUnixSeconds = receivedUtcUnixSeconds
                + sourceTimestampOffsetSeconds;
            var stress = new StressDecision(
                StressDecisionMode.Point,
                pointStressLevel,
                null,
                null,
                stressLabel,
                stressConfidence,
                false,
                new StressProbabilityVector(
                    level0Probability,
                    level1Probability,
                    level2Probability,
                    level3Probability),
                continuousStressScore);

            return new PhysiologyWindow(
                windowEndUtcUnixSeconds,
                windowEndUtcUnixSeconds - windowDurationSeconds,
                windowEndUtcUnixSeconds,
                heartRateBpm,
                includeRmssd ? rmssdMs : (double?)null,
                includeSdnn ? sdnnMs : (double?)null,
                stress,
                signalQuality);
        }

        private static bool IsFinitePositive(float value)
        {
            return !float.IsNaN(value)
                && !float.IsInfinity(value)
                && value > 0f;
        }
    }
}
#endif

