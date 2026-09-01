using LaminarVR.AdaptiveMeditation.Application;
using LaminarVR.AdaptiveMeditation.Physiology;
using LaminarVR.AdaptiveMeditation.Rewards;
using LaminarVR.AdaptiveMeditation.Session;
using NUnit.Framework;

namespace LaminarVR.AdaptiveMeditation.Tests.EditMode.Application
{
    public sealed class ProductionCoordinatorConfigurationTests
    {
        [Test]
        public void Compatibility_AcceptsSixtySecondComponentCadence()
        {
            var configuration = CreateCoordinator(60d);

            var compatible = configuration.TryValidateCompatibility(
                CreateSessionTiming(60d),
                CreatePhysiologyValidation(90d),
                CreateRewardPipeline(125d),
                out var validationError);

            Assert.That(compatible, Is.True, validationError);
        }

        [TestCase(59d, 90d, 125d, "Decision interval")]
        [TestCase(60d, 59d, 125d, "stale-after")]
        [TestCase(60d, 90d, 59d, "reward-attribution")]
        public void Compatibility_RejectsConfigurationExpectingFasterOutput(
            double decisionIntervalSeconds,
            double staleAfterSeconds,
            double maximumAttributionWaitSeconds,
            string expectedMessage)
        {
            var configuration = CreateCoordinator(60d);

            var compatible = configuration.TryValidateCompatibility(
                CreateSessionTiming(decisionIntervalSeconds),
                CreatePhysiologyValidation(staleAfterSeconds),
                CreateRewardPipeline(maximumAttributionWaitSeconds),
                out var validationError);

            Assert.That(compatible, Is.False);
            Assert.That(validationError, Does.Contain(expectedMessage).IgnoreCase);
        }

        private static ProductionCoordinatorConfiguration CreateCoordinator(
            double expectedOutputIntervalSeconds)
        {
            return new ProductionCoordinatorConfiguration(
                "coordinator-test",
                1,
                expectedOutputIntervalSeconds,
                2,
                1d);
        }

        private static SessionTimingConfiguration CreateSessionTiming(
            double decisionIntervalSeconds)
        {
            return new SessionTimingConfiguration(
                "session-test",
                1,
                120d,
                900d,
                180d,
                decisionIntervalSeconds);
        }

        private static PhysiologyValidationConfiguration
            CreatePhysiologyValidation(double staleAfterSeconds)
        {
            return new PhysiologyValidationConfiguration(
                "physiology-test",
                1,
                staleAfterSeconds,
                60d,
                1d,
                1d,
                0.01d,
                0.8d,
                0.8d,
                16);
        }

        private static RewardPipelineConfiguration CreateRewardPipeline(
            double maximumAttributionWaitSeconds)
        {
            return new RewardPipelineConfiguration(
                "reward-test",
                1,
                BaselineStandardDeviationMethod.Population,
                2,
                0.01d,
                2,
                2,
                5d,
                maximumAttributionWaitSeconds,
                1d,
                0d,
                0d,
                0.1d,
                1d,
                1d);
        }
    }
}
