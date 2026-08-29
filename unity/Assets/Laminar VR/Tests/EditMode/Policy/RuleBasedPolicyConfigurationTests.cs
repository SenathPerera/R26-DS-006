using System;
using LaminarVR.AdaptiveMeditation.Policy.RuleBased;
using NUnit.Framework;

namespace LaminarVR.AdaptiveMeditation.Tests.EditMode.Policy
{
    public sealed class RuleBasedPolicyConfigurationTests
    {
        [Test]
        public void Constructor_PreservesExplicitResearchConfiguration()
        {
            var configuration = CreateConfiguration();

            Assert.That(configuration.ConfigurationId, Is.EqualTo("rule-test"));
            Assert.That(configuration.ConfigurationVersion, Is.EqualTo(1));
            Assert.That(
                configuration.ActivationMode,
                Is.EqualTo(RuleActivationMode.WorseningStressTrend));
            Assert.That(
                configuration.MinimumContinuousStressScore,
                Is.EqualTo(2d));
            Assert.That(
                configuration.MinimumStressIncreasePerMinute,
                Is.EqualTo(0.5d));
            Assert.That(configuration.MinimumPreferenceDelta, Is.EqualTo(0.05d));
        }

        [TestCase(0d, 0.5d, 0.05d)]
        [TestCase(3.1d, 0.5d, 0.05d)]
        [TestCase(2d, 0d, 0.05d)]
        [TestCase(2d, 0.5d, 0d)]
        public void Constructor_RejectsInvalidResearchThresholds(
            double stressScore,
            double stressIncrease,
            double preferenceDelta)
        {
            Assert.Catch<ArgumentException>(
                () => CreateConfiguration(
                    minimumContinuousStressScore: stressScore,
                    minimumStressIncreasePerMinute: stressIncrease,
                    minimumPreferenceDelta: preferenceDelta));
        }

        internal static RuleBasedPolicyConfiguration CreateConfiguration(
            RuleActivationMode activationMode =
                RuleActivationMode.WorseningStressTrend,
            double minimumContinuousStressScore = 2d,
            double minimumStressIncreasePerMinute = 0.5d,
            double minimumPreferenceDelta = 0.05d,
            string configurationId = "rule-test",
            int configurationVersion = 1)
        {
            return new RuleBasedPolicyConfiguration(
                configurationId,
                configurationVersion,
                activationMode,
                minimumContinuousStressScore,
                minimumStressIncreasePerMinute,
                minimumPreferenceDelta);
        }
    }
}
