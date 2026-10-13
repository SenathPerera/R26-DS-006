using LaminarVR.AdaptiveMeditation.Environment;
using LaminarVR.AdaptiveMeditation.Physiology;
using LaminarVR.AdaptiveMeditation.Rewards;
using LaminarVR.AdaptiveMeditation.Safety;
using NUnit.Framework;

namespace LaminarVR.AdaptiveMeditation.Tests.EditMode.Rewards
{
    public sealed class RewardCalculatorTests
    {
        [Test]
        public void Calculate_ReturnsDeterministicReconstructableBreakdown()
        {
            var calculator = new RewardCalculator(
                RewardPipelineConfigurationTests.CreateConfiguration());
            var before = CreateEnvironment();
            var after = new EnvironmentState(
                0.5f,
                0.58f,
                0.5f,
                0.5f,
                0.5f);

            var result = calculator.Calculate(
                CreateSnapshot(1L, 1000d, 2d, 70d, 30d),
                CreateSnapshot(2L, 1060d, 1d, 72d, 35d),
                CreateBaseline(),
                EnvironmentAction.IncreaseWarmth,
                before,
                after,
                RewardPenaltyObservation.CreateForTesting(
                    0.5d,
                    0d,
                    false));

            Assert.That(result.Valid, Is.True);
            Assert.That(result.Breakdown.StressScoreImprovement, Is.EqualTo(1d));
            Assert.That(result.Breakdown.NormalizedStressImprovement, Is.EqualTo(2d));
            Assert.That(result.Breakdown.NormalizedRmssdImprovement, Is.EqualTo(1d));
            Assert.That(
                result.Breakdown.NormalizedHeartRateIncrease,
                Is.EqualTo(0.2d));
            Assert.That(
                result.Breakdown.ActionMagnitude,
                Is.EqualTo(0.08d).Within(1e-6d));
            Assert.That(
                result.Breakdown.TotalReward,
                Is.EqualTo(1.312d).Within(1e-6d));
        }

        [Test]
        public void Calculate_RejectsUnavailableBaseline()
        {
            var calculator = new RewardCalculator(
                RewardPipelineConfigurationTests.CreateConfiguration());
            var invalidBaseline = new PhysiologyBaseline(
                BaselineStandardDeviationMethod.Population,
                new PhysiologyMetricStatistics(3, 2d, 0d),
                new PhysiologyMetricStatistics(3, 70d, 10d),
                new PhysiologyMetricStatistics(3, 30d, 5d));

            var result = CalculateNoChange(calculator, invalidBaseline);

            Assert.That(
                result.ResultCode,
                Is.EqualTo(RewardCalculationResultCode.BaselineUnavailable));
        }

        [Test]
        public void Calculate_RejectsMissingRmssdWhenWeighted()
        {
            var calculator = new RewardCalculator(
                RewardPipelineConfigurationTests.CreateConfiguration());

            var result = calculator.Calculate(
                CreateSnapshot(1L, 1000d, 2d, 70d, null),
                CreateSnapshot(2L, 1060d, 1d, 72d, 35d),
                CreateBaseline(),
                EnvironmentAction.NoChange,
                CreateEnvironment(),
                CreateEnvironment(),
                ObservedNoPenalty());

            Assert.That(
                result.ResultCode,
                Is.EqualTo(RewardCalculationResultCode.MissingRmssd));
        }

        [Test]
        public void Calculate_RejectsOverlappingPhysiologyWindows()
        {
            var calculator = new RewardCalculator(
                RewardPipelineConfigurationTests.CreateConfiguration());

            var result = calculator.Calculate(
                CreateSnapshot(1L, 1000d, 2d, 70d, 30d),
                CreateSnapshot(2L, 1050d, 1d, 72d, 35d),
                CreateBaseline(),
                EnvironmentAction.NoChange,
                CreateEnvironment(),
                CreateEnvironment(),
                ObservedNoPenalty());

            Assert.That(
                result.ResultCode,
                Is.EqualTo(
                    RewardCalculationResultCode.OverlappingPhysiologyWindows));
        }

        [Test]
        public void Calculate_RejectsActionThatDoesNotMatchEnvironmentDelta()
        {
            var calculator = new RewardCalculator(
                RewardPipelineConfigurationTests.CreateConfiguration());
            var after = new EnvironmentState(
                0.5f,
                0.58f,
                0.5f,
                0.5f,
                0.5f);

            var result = calculator.Calculate(
                CreateSnapshot(1L, 1000d, 2d, 70d, 30d),
                CreateSnapshot(2L, 1060d, 1d, 72d, 35d),
                CreateBaseline(),
                EnvironmentAction.IncreaseIllumination,
                CreateEnvironment(),
                after,
                ObservedNoPenalty());

            Assert.That(
                result.ResultCode,
                Is.EqualTo(
                    RewardCalculationResultCode.ActionEnvironmentMismatch));
        }

        [Test]
        public void Calculate_RejectsMissingDiscomfortWhenWeighted()
        {
            var calculator = new RewardCalculator(
                RewardPipelineConfigurationTests.CreateConfiguration());

            var result = calculator.Calculate(
                CreateSnapshot(1L, 1000d, 2d, 70d, 30d),
                CreateSnapshot(2L, 1060d, 1d, 72d, 35d),
                CreateBaseline(),
                EnvironmentAction.NoChange,
                CreateEnvironment(),
                CreateEnvironment(),
                RewardPenaltyObservation.CreateForTesting(
                    null,
                    0d,
                    false));

            Assert.That(
                result.ResultCode,
                Is.EqualTo(
                    RewardCalculationResultCode.MissingDiscomfortSeverity));
        }

        [Test]
        public void Calculate_PreservesMissingDiscomfortWhenWeightDisabled()
        {
            var calculator = new RewardCalculator(
                RewardPipelineConfigurationTests.CreateConfiguration(
                    discomfortPenaltyWeight: 0d));

            var result = calculator.Calculate(
                CreateSnapshot(1L, 1000d, 2d, 70d, 30d),
                CreateSnapshot(2L, 1060d, 1d, 72d, 35d),
                CreateBaseline(),
                EnvironmentAction.NoChange,
                CreateEnvironment(),
                CreateEnvironment(),
                RewardPenaltyObservation.CreateForTesting(
                    null,
                    0d,
                    false));

            Assert.That(result.Valid, Is.True);
            Assert.That(result.Breakdown.DiscomfortSeverity, Is.Null);
            Assert.That(
                result.Breakdown.DiscomfortPenaltyComponent,
                Is.Zero);
        }

        [Test]
        public void PenaltyObservation_UsesAcceptedSafetyModification()
        {
            var current = CreateEnvironment();
            var range = new NormalizedRange(0f, 0.55f);
            var profile = new SceneEnvironmentProfile(
                "reward-safety-test",
                "Reward Safety Test",
                current,
                new EnvironmentStateLimits(
                    range,
                    new NormalizedRange(0f, 1f),
                    new NormalizedRange(0f, 1f),
                    new NormalizedRange(0f, 1f),
                    new NormalizedRange(0f, 1f)),
                new EnvironmentActionStepConfiguration(
                    0.1f,
                    0.1f,
                    0.1f,
                    0.1f,
                    0.1f),
                1f,
                0f);
            var validation = new ActionSafetyValidator().Validate(
                EnvironmentAction.IncreaseIllumination,
                current,
                profile,
                new SafetyRuntimeState(
                    SafetyBlockReason.None,
                    null,
                    0,
                    0d),
                new ActionSafetyLimits(2, 1d));

            var observation =
                RewardPenaltyObservation.FromActionValidation(validation);

            Assert.That(validation.Accepted, Is.True);
            Assert.That(validation.Modified, Is.True);
            Assert.That(observation.DiscomfortSeverity, Is.Null);
            Assert.That(observation.SafetySeverity, Is.EqualTo(1d));
            Assert.That(observation.SafetyInterventionOccurred, Is.True);
        }

        [Test]
        public void PenaltyObservation_UsesZeroForUnmodifiedAcceptedAction()
        {
            var current = CreateEnvironment();
            var range = new NormalizedRange(0f, 1f);
            var profile = new SceneEnvironmentProfile(
                "reward-safety-test",
                "Reward Safety Test",
                current,
                new EnvironmentStateLimits(
                    range,
                    range,
                    range,
                    range,
                    range),
                new EnvironmentActionStepConfiguration(
                    0.1f,
                    0.1f,
                    0.1f,
                    0.1f,
                    0.1f),
                1f,
                0f);
            var validation = new ActionSafetyValidator().Validate(
                EnvironmentAction.NoChange,
                current,
                profile,
                new SafetyRuntimeState(
                    SafetyBlockReason.None,
                    null,
                    0,
                    0d),
                new ActionSafetyLimits(2, 1d));

            var observation =
                RewardPenaltyObservation.FromActionValidation(validation);

            Assert.That(validation.Accepted, Is.True);
            Assert.That(validation.Modified, Is.False);
            Assert.That(observation.DiscomfortSeverity, Is.Null);
            Assert.That(observation.SafetySeverity, Is.Zero);
            Assert.That(observation.SafetyInterventionOccurred, Is.False);
        }

        private static RewardCalculationResult CalculateNoChange(
            RewardCalculator calculator,
            PhysiologyBaseline baseline)
        {
            return calculator.Calculate(
                CreateSnapshot(1L, 1000d, 2d, 70d, 30d),
                CreateSnapshot(2L, 1060d, 1d, 72d, 35d),
                baseline,
                EnvironmentAction.NoChange,
                CreateEnvironment(),
                CreateEnvironment(),
                ObservedNoPenalty());
        }

        private static RewardPenaltyObservation ObservedNoPenalty()
        {
            return RewardPenaltyObservation.CreateForTesting(
                0d,
                0d,
                false);
        }

        private static PhysiologyBaseline CreateBaseline()
        {
            return new PhysiologyBaseline(
                BaselineStandardDeviationMethod.Population,
                new PhysiologyMetricStatistics(3, 2d, 0.5d),
                new PhysiologyMetricStatistics(3, 70d, 10d),
                new PhysiologyMetricStatistics(3, 30d, 5d));
        }

        internal static PhysiologyWindowSnapshot CreateSnapshot(
            long sequenceNumber,
            double windowEndUtcUnixSeconds,
            double stressScore,
            double heartRateBpm,
            double? rmssdMs,
            double receivedMonotonicTimeSeconds = 0d,
            double signalQuality = 0.95d)
        {
            return new PhysiologyWindowSnapshot(
                sequenceNumber,
                CreateWindow(
                    windowEndUtcUnixSeconds,
                    stressScore,
                    heartRateBpm,
                    rmssdMs,
                    signalQuality),
                0d,
                receivedMonotonicTimeSeconds);
        }

        internal static PhysiologyWindow CreateWindow(
            double windowEndUtcUnixSeconds,
            double stressScore = 1.5d,
            double heartRateBpm = 70d,
            double? rmssdMs = 30d,
            double signalQuality = 0.95d)
        {
            return new PhysiologyWindow(
                windowEndUtcUnixSeconds,
                windowEndUtcUnixSeconds - 60d,
                windowEndUtcUnixSeconds,
                heartRateBpm,
                rmssdMs,
                40d,
                new StressDecision(
                    StressDecisionMode.Point,
                    2,
                    null,
                    null,
                    "moderate",
                    0.5d,
                    false,
                    new StressProbabilityVector(0.1d, 0.2d, 0.6d, 0.1d),
                    stressScore),
                signalQuality);
        }

        internal static EnvironmentState CreateEnvironment()
        {
            return new EnvironmentState(0.5f, 0.5f, 0.5f, 0.5f, 0.5f);
        }
    }
}
