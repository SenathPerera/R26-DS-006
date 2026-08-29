using System;
using LaminarVR.AdaptiveMeditation.Policy.ContextualBandit;
using NUnit.Framework;

namespace LaminarVR.AdaptiveMeditation.Tests.EditMode.Policy.ContextualBandit
{
    public sealed class LinUcbModelConfigurationTests
    {
        [Test]
        public void Constructor_PreservesVersionedResearchParameters()
        {
            var configuration = new LinUcbModelConfiguration(
                "linucb-pilot",
                3,
                "features/0.1-draft",
                24,
                0.75d,
                0.2d);

            Assert.That(configuration.ConfigurationId, Is.EqualTo("linucb-pilot"));
            Assert.That(configuration.ConfigurationVersion, Is.EqualTo(3));
            Assert.That(
                configuration.FeatureSchemaVersion,
                Is.EqualTo("features/0.1-draft"));
            Assert.That(configuration.FeatureCount, Is.EqualTo(24));
            Assert.That(configuration.RidgeRegularization, Is.EqualTo(0.75d));
            Assert.That(configuration.ExplorationCoefficient, Is.EqualTo(0.2d));
            Assert.That(configuration.ModelVersion, Is.EqualTo("linucb-pilot/3"));
        }

        [TestCase(0d)]
        [TestCase(-1d)]
        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        public void Constructor_RejectsInvalidRidgeRegularization(
            double ridgeRegularization)
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => CreateConfiguration(
                    ridgeRegularization,
                    explorationCoefficient: 0.1d));
        }

        [TestCase(-0.01d)]
        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        public void Constructor_RejectsInvalidExplorationCoefficient(
            double explorationCoefficient)
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => CreateConfiguration(
                    ridgeRegularization: 1d,
                    explorationCoefficient));
        }

        [Test]
        public void Constructor_AllowsZeroExplorationForDeterministicEvaluation()
        {
            Assert.DoesNotThrow(
                () => CreateConfiguration(1d, 0d));
        }

        private static LinUcbModelConfiguration CreateConfiguration(
            double ridgeRegularization,
            double explorationCoefficient)
        {
            return new LinUcbModelConfiguration(
                "configuration-test",
                1,
                "features/test",
                2,
                ridgeRegularization,
                explorationCoefficient);
        }
    }
}
