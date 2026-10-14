using System;
using LaminarVR.AdaptiveMeditation.Physiology;

namespace LaminarVR.AdaptiveMeditation.ResearchEvaluation
{
    public sealed class SyntheticSessionParameters
    {
        public SyntheticSessionParameters(
            double initialStressScore,
            double initialHeartRateBpm,
            double initialRmssdMs,
            double naturalRecoveryPerEmission)
        {
            InitialStressScore = RequireRange(
                initialStressScore,
                1.7d,
                2.7d,
                nameof(initialStressScore));
            InitialHeartRateBpm = RequireRange(
                initialHeartRateBpm,
                62d,
                82d,
                nameof(initialHeartRateBpm));
            InitialRmssdMs = RequireRange(
                initialRmssdMs,
                25d,
                65d,
                nameof(initialRmssdMs));
            NaturalRecoveryPerEmission = RequireRange(
                naturalRecoveryPerEmission,
                0.02d,
                0.06d,
                nameof(naturalRecoveryPerEmission));
        }

        public double InitialStressScore { get; }
        public double InitialHeartRateBpm { get; }
        public double InitialRmssdMs { get; }
        public double NaturalRecoveryPerEmission { get; }

        private static double RequireRange(
            double value,
            double minimum,
            double maximum,
            string parameterName)
        {
            if (double.IsNaN(value)
                || double.IsInfinity(value)
                || value < minimum
                || value > maximum)
            {
                throw new ArgumentOutOfRangeException(parameterName);
            }

            return value;
        }
    }

    public sealed class ProtocolSyntheticPhysiologyModel
        : ISyntheticPhysiologyModel, ISyntheticSafeOracleModel
    {
        public const string GeneratorVersion =
            "component-c-synthetic-physiology/1.0";
        public const double NominalSignalQuality = 0.9d;
        public const double StressNoiseStandardDeviation = 0.04d;
        public const double HeartRateNoiseStandardDeviation = 0.8d;
        public const double RmssdNoiseStandardDeviation = 1.2d;

        private readonly SyntheticArchetypeDefinition archetype;
        private readonly LaminarVR.AdaptiveMeditation.Environment.EnvironmentState
            preference;
        private readonly SyntheticSessionParameters parameters;
        private readonly Pcg32 physiologyNoise;
        private readonly double noiseMultiplier;

        private SyntheticPhysiologySessionContext context;
        private double stressScore;
        private double heartRateBpm;
        private double rmssdMs;

        public ProtocolSyntheticPhysiologyModel(
            SyntheticArchetypeDefinition archetype,
            LaminarVR.AdaptiveMeditation.Environment.EnvironmentState preference,
            SyntheticSessionParameters parameters,
            Pcg32 physiologyNoise,
            double noiseMultiplier = 1d)
        {
            this.archetype = archetype ?? throw new ArgumentNullException(
                nameof(archetype));
            if (!preference.IsNormalized)
            {
                throw new ArgumentException(
                    "The synthetic preference must be normalized.",
                    nameof(preference));
            }

            this.preference = preference;
            this.parameters = parameters ?? throw new ArgumentNullException(
                nameof(parameters));
            this.physiologyNoise = physiologyNoise
                ?? throw new ArgumentNullException(nameof(physiologyNoise));
            if (double.IsNaN(noiseMultiplier)
                || double.IsInfinity(noiseMultiplier)
                || noiseMultiplier <= 0d)
            {
                throw new ArgumentOutOfRangeException(nameof(noiseMultiplier));
            }

            this.noiseMultiplier = noiseMultiplier;
        }

        public string ModelId => GeneratorVersion + "/" + archetype.Code;

        public SyntheticArchetypeDefinition Archetype => archetype;

        public double NoiseMultiplier => noiseMultiplier;

        public double CalculateUtility(
            LaminarVR.AdaptiveMeditation.Environment.EnvironmentState environment,
            double sessionElapsedSeconds)
        {
            return archetype.CalculateUtility(
                preference,
                environment,
                sessionElapsedSeconds);
        }

        public double CalculateExpectedStressReduction(
            LaminarVR.AdaptiveMeditation.Environment.EnvironmentState
                environmentBefore,
            LaminarVR.AdaptiveMeditation.Environment.EnvironmentState
                environmentAfter,
            double sessionElapsedSeconds)
        {
            var utilityBefore = CalculateUtility(
                environmentBefore,
                sessionElapsedSeconds);
            var utilityAfter = CalculateUtility(
                environmentAfter,
                sessionElapsedSeconds);
            return archetype.ResponseGain * (utilityAfter - utilityBefore);
        }

        public void Initialize(SyntheticPhysiologySessionContext sessionContext)
        {
            context = sessionContext ?? throw new ArgumentNullException(
                nameof(sessionContext));
            stressScore = parameters.InitialStressScore;
            heartRateBpm = parameters.InitialHeartRateBpm;
            rmssdMs = parameters.InitialRmssdMs;
        }

        public SyntheticPhysiologyEmission CreateEmission(
            SyntheticPhysiologyRequest request)
        {
            if (context == null)
            {
                throw new InvalidOperationException(
                    "The synthetic model must be initialized first.");
            }

            if (request.EmissionSequence < 1
                || request.EmissionMonotonicTimeSeconds <= 0d
                || request.WindowDurationSeconds <= 0d)
            {
                throw new ArgumentOutOfRangeException(nameof(request));
            }

            double emittedStress;
            double emittedHeartRate;
            double emittedRmssd;
            if (request.EmissionMonotonicTimeSeconds <= 120d)
            {
                var sign = request.EmissionMonotonicTimeSeconds <= 60d
                    ? -1d
                    : 1d;
                emittedStress = Clamp(
                    parameters.InitialStressScore + (sign * 0.1d),
                    0d,
                    3d);
                emittedHeartRate = Clamp(
                    parameters.InitialHeartRateBpm + (sign * 2d),
                    45d,
                    130d);
                emittedRmssd = Clamp(
                    parameters.InitialRmssdMs - (sign * 3d),
                    5d,
                    150d);
            }
            else
            {
                var previousStress = stressScore;
                var utilityChange = CalculateUtilityChange(request);
                stressScore = Clamp(
                    stressScore
                    - parameters.NaturalRecoveryPerEmission
                    - (archetype.ResponseGain * utilityChange)
                    + physiologyNoise.NextGaussian(
                        0d,
                        StressNoiseStandardDeviation * noiseMultiplier),
                    0d,
                    3d);
                var stressChange = stressScore - previousStress;
                heartRateBpm = Clamp(
                    heartRateBpm
                    + (2.5d * stressChange)
                    + physiologyNoise.NextGaussian(
                        0d,
                        HeartRateNoiseStandardDeviation * noiseMultiplier),
                    45d,
                    130d);
                rmssdMs = Clamp(
                    rmssdMs
                    - (4d * stressChange)
                    + physiologyNoise.NextGaussian(
                        0d,
                        RmssdNoiseStandardDeviation * noiseMultiplier),
                    5d,
                    150d);
                emittedStress = stressScore;
                emittedHeartRate = heartRateBpm;
                emittedRmssd = rmssdMs;
            }

            var endUtc = context.BaseUtcUnixSeconds
                + request.EmissionMonotonicTimeSeconds;
            return SyntheticPhysiologyEmission.Deliver(
                new PhysiologyWindow(
                    endUtc,
                    endUtc - request.WindowDurationSeconds,
                    endUtc,
                    emittedHeartRate,
                    emittedRmssd,
                    null,
                    SyntheticStressDecisionFactory.FromContinuousScore(
                        emittedStress),
                    NominalSignalQuality),
                request.EmissionMonotonicTimeSeconds);
        }

        private double CalculateUtilityChange(
            SyntheticPhysiologyRequest request)
        {
            if (!request.EligibleActionExposure.HasValue)
            {
                return 0d;
            }

            var exposure = request.EligibleActionExposure.Value;
            var before = archetype.CalculateUtility(
                preference,
                exposure.EnvironmentBefore,
                request.EmissionMonotonicTimeSeconds);
            var after = archetype.CalculateUtility(
                preference,
                exposure.EnvironmentAfter,
                request.EmissionMonotonicTimeSeconds);
            return after - before;
        }

        private static double Clamp(
            double value,
            double minimum,
            double maximum)
        {
            return Math.Min(maximum, Math.Max(minimum, value));
        }
    }
}
