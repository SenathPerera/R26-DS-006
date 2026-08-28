using System;
using LaminarVR.AdaptiveMeditation.Physiology;
using LaminarVR.AdaptiveMeditation.Rewards;
using NUnit.Framework;

namespace LaminarVR.AdaptiveMeditation.Tests.EditMode.Rewards
{
    public sealed class RewardPipelineConfigurationTests
    {
        [Test]
        public void Constructor_PreservesExplicitResearchConfiguration()
        {
            var configuration = CreateConfiguration();

            Assert.That(configuration.ConfigurationId, Is.EqualTo("reward-test"));
            Assert.That(configuration.ConfigurationVersion, Is.EqualTo(1));
            Assert.That(
                configuration.BaselineStandardDeviationMethod,
                Is.EqualTo(BaselineStandardDeviationMethod.Population));
            Assert.That(configuration.MinimumBaselineSamples, Is.EqualTo(3));
            Assert.That(configuration.TrendWindowCount, Is.EqualTo(5));
            Assert.That(configuration.MinimumTrendSamples, Is.EqualTo(3));
            Assert.That(configuration.SettlingSeconds, Is.EqualTo(5d));
            Assert.That(
                configuration.MaximumAttributionWaitSeconds,
                Is.EqualTo(120d));
        }

        [Test]
        public void Constructor_RequiresAPhysiologicalRewardComponent()
        {
            Assert.Throws<ArgumentException>(
                () => CreateConfiguration(
                    stressWeight: 0d,
                    rmssdWeight: 0d,
                    heartRateWeight: 0d));
        }

        [TestCase(1, 5, 3, 5d, 120d)]
        [TestCase(3, 1, 1, 5d, 120d)]
        [TestCase(3, 5, 6, 5d, 120d)]
        [TestCase(3, 5, 3, 5d, 5d)]
        public void Constructor_RejectsInvalidSamplingOrTiming(
            int baselineSamples,
            int trendWindows,
            int trendSamples,
            double settlingSeconds,
            double maximumWaitSeconds)
        {
            Assert.Catch<ArgumentException>(
                () => CreateConfiguration(
                    minimumBaselineSamples: baselineSamples,
                    trendWindowCount: trendWindows,
                    minimumTrendSamples: trendSamples,
                    settlingSeconds: settlingSeconds,
                    maximumAttributionWaitSeconds: maximumWaitSeconds));
        }

        internal static RewardPipelineConfiguration CreateConfiguration(
            int minimumBaselineSamples = 3,
            int trendWindowCount = 5,
            int minimumTrendSamples = 3,
            double settlingSeconds = 5d,
            double maximumAttributionWaitSeconds = 120d,
            double stressWeight = 1d,
            double rmssdWeight = 0.35d,
            double heartRateWeight = 0.15d)
        {
            return new RewardPipelineConfiguration(
                "reward-test",
                1,
                BaselineStandardDeviationMethod.Population,
                minimumBaselineSamples,
                0.01d,
                trendWindowCount,
                minimumTrendSamples,
                settlingSeconds,
                maximumAttributionWaitSeconds,
                stressWeight,
                rmssdWeight,
                heartRateWeight,
                0.1d,
                2d,
                2d);
        }
    }
}
