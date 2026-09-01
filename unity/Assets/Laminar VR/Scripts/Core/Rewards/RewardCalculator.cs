using System;
using LaminarVR.AdaptiveMeditation.Environment;
using LaminarVR.AdaptiveMeditation.Physiology;

namespace LaminarVR.AdaptiveMeditation.Rewards
{
    public sealed class RewardCalculator
    {
        private const double EnvironmentComparisonTolerance = 1e-6d;
        private readonly RewardPipelineConfiguration configuration;

        public RewardCalculator(RewardPipelineConfiguration configuration)
        {
            this.configuration = configuration
                ?? throw new ArgumentNullException(nameof(configuration));
        }

        public RewardCalculationResult Calculate(
            PhysiologyWindowSnapshot preAction,
            PhysiologyWindowSnapshot postAction,
            PhysiologyBaseline baseline,
            EnvironmentAction executedAction,
            EnvironmentState environmentBefore,
            EnvironmentState environmentAfter,
            double discomfortSeverity,
            double safetySeverity)
        {
            if (!IsValidSnapshot(preAction) || !IsValidSnapshot(postAction))
            {
                return RewardCalculationResult.Invalid(
                    RewardCalculationResultCode.InvalidPhysiologyWindow);
            }

            if (postAction.SequenceNumber <= preAction.SequenceNumber)
            {
                return RewardCalculationResult.Invalid(
                    RewardCalculationResultCode.NonIncreasingWindowSequence);
            }

            if (postAction.Window.WindowStartUtcUnixSeconds
                < preAction.Window.WindowEndUtcUnixSeconds)
            {
                return RewardCalculationResult.Invalid(
                    RewardCalculationResultCode.OverlappingPhysiologyWindows);
            }

            if (!environmentBefore.IsNormalized
                || !environmentAfter.IsNormalized)
            {
                return RewardCalculationResult.Invalid(
                    RewardCalculationResultCode.InvalidEnvironmentState);
            }

            if (!IsUnitInterval(discomfortSeverity)
                || !IsUnitInterval(safetySeverity))
            {
                return RewardCalculationResult.Invalid(
                    RewardCalculationResultCode.InvalidPenaltySeverity);
            }

            if (!IsActionConsistent(
                executedAction,
                environmentBefore,
                environmentAfter))
            {
                return RewardCalculationResult.Invalid(
                    RewardCalculationResultCode.ActionEnvironmentMismatch);
            }

            if (!HasRequiredBaseline(baseline))
            {
                return RewardCalculationResult.Invalid(
                    RewardCalculationResultCode.BaselineUnavailable);
            }

            if (configuration.RmssdWeight > 0d
                && (!preAction.Window.RmssdMs.HasValue
                    || !postAction.Window.RmssdMs.HasValue))
            {
                return RewardCalculationResult.Invalid(
                    RewardCalculationResultCode.MissingRmssd);
            }

            var stressImprovement =
                preAction.Window.Stress.ContinuousScore
                - postAction.Window.Stress.ContinuousScore;
            var rmssdImprovement = configuration.RmssdWeight > 0d
                ? postAction.Window.RmssdMs.Value
                    - preAction.Window.RmssdMs.Value
                : 0d;
            var heartRateIncrease = postAction.Window.HeartRateBpm
                - preAction.Window.HeartRateBpm;
            var normalizedStress = configuration.StressWeight > 0d
                ? stressImprovement / baseline.Stress.StandardDeviation
                : 0d;
            var normalizedRmssd = configuration.RmssdWeight > 0d
                ? rmssdImprovement / baseline.Rmssd.StandardDeviation
                : 0d;
            var normalizedHeartRate = configuration.HeartRateWeight > 0d
                ? heartRateIncrease / baseline.HeartRate.StandardDeviation
                : 0d;
            var actionMagnitude =
                environmentBefore.L1DistanceTo(environmentAfter);

            var stressComponent =
                configuration.StressWeight * normalizedStress;
            var rmssdComponent =
                configuration.RmssdWeight * normalizedRmssd;
            var heartRateComponent =
                -configuration.HeartRateWeight * normalizedHeartRate;
            var changePenaltyComponent =
                -configuration.ChangePenaltyWeight * actionMagnitude;
            var discomfortPenaltyComponent =
                -configuration.DiscomfortPenaltyWeight * discomfortSeverity;
            var safetyPenaltyComponent =
                -configuration.SafetyPenaltyWeight * safetySeverity;
            var totalReward = stressComponent
                + rmssdComponent
                + heartRateComponent
                + changePenaltyComponent
                + discomfortPenaltyComponent
                + safetyPenaltyComponent;

            if (!IsFinite(totalReward))
            {
                return RewardCalculationResult.Invalid(
                    RewardCalculationResultCode.NonFiniteReward);
            }

            return new RewardCalculationResult(
                RewardCalculationResultCode.Valid,
                new RewardBreakdown(
                    executedAction,
                    preAction.SequenceNumber,
                    postAction.SequenceNumber,
                    stressImprovement,
                    rmssdImprovement,
                    heartRateIncrease,
                    normalizedStress,
                    normalizedRmssd,
                    normalizedHeartRate,
                    actionMagnitude,
                    discomfortSeverity,
                    safetySeverity,
                    stressComponent,
                    rmssdComponent,
                    heartRateComponent,
                    changePenaltyComponent,
                    discomfortPenaltyComponent,
                    safetyPenaltyComponent,
                    totalReward));
        }

        private bool HasRequiredBaseline(PhysiologyBaseline baseline)
        {
            if (baseline == null
                || baseline.StandardDeviationMethod
                    != configuration.BaselineStandardDeviationMethod)
            {
                return false;
            }

            return IsMetricAvailable(
                    baseline.Stress,
                    configuration.StressWeight)
                && IsMetricAvailable(
                    baseline.Rmssd,
                    configuration.RmssdWeight)
                && IsMetricAvailable(
                    baseline.HeartRate,
                    configuration.HeartRateWeight);
        }

        private bool IsMetricAvailable(
            PhysiologyMetricStatistics statistics,
            double weight)
        {
            return weight == 0d
                || (statistics.SampleCount >= configuration.MinimumBaselineSamples
                    && IsFinite(statistics.StandardDeviation)
                    && statistics.StandardDeviation
                        >= configuration.MinimumBaselineStandardDeviation);
        }

        private static bool IsValidSnapshot(PhysiologyWindowSnapshot snapshot)
        {
            if (snapshot.SequenceNumber < 1L
                || snapshot.Window == null
                || snapshot.Window.Stress == null)
            {
                return false;
            }

            return IsFinite(snapshot.Window.WindowStartUtcUnixSeconds)
                && IsFinite(snapshot.Window.WindowEndUtcUnixSeconds)
                && IsFinite(snapshot.Window.HeartRateBpm)
                && IsFinite(snapshot.Window.Stress.ContinuousScore)
                && (!snapshot.Window.RmssdMs.HasValue
                    || IsFinite(snapshot.Window.RmssdMs.Value));
        }

        private static bool IsActionConsistent(
            EnvironmentAction action,
            EnvironmentState before,
            EnvironmentState after)
        {
            var differences = new[]
            {
                (double)after.Illumination - before.Illumination,
                (double)after.Warmth - before.Warmth,
                (double)after.AtmosphericSoftness - before.AtmosphericSoftness,
                (double)after.ColorRichness - before.ColorRichness,
                (double)after.AmbientMotion - before.AmbientMotion
            };
            var changedIndex = -1;
            for (var index = 0; index < differences.Length; index++)
            {
                if (Math.Abs(differences[index])
                    <= EnvironmentComparisonTolerance)
                {
                    continue;
                }

                if (changedIndex >= 0)
                {
                    return false;
                }

                changedIndex = index;
            }

            if (action == EnvironmentAction.NoChange)
            {
                return changedIndex < 0;
            }

            if (changedIndex < 0)
            {
                return false;
            }

            var actionValue = (int)action;
            if (actionValue < (int)EnvironmentAction.IncreaseIllumination
                || actionValue > (int)EnvironmentAction.DecreaseAmbientMotion)
            {
                return false;
            }

            var expectedIndex = (actionValue - 1) / 2;
            var expectedIncrease = actionValue % 2 == 1;
            return changedIndex == expectedIndex
                && (expectedIncrease
                    ? differences[changedIndex] > 0d
                    : differences[changedIndex] < 0d);
        }

        private static bool IsUnitInterval(double value)
        {
            return IsFinite(value) && value >= 0d && value <= 1d;
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
