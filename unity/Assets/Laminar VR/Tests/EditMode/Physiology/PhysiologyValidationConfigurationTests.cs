using System;
using LaminarVR.AdaptiveMeditation.Physiology;
using NUnit.Framework;

namespace LaminarVR.AdaptiveMeditation.Tests.EditMode.Physiology
{
    public sealed class PhysiologyValidationConfigurationTests
    {
        [Test]
        public void Constructor_StoresValidatedConfiguration()
        {
            var configuration = CreateConfiguration();

            Assert.That(configuration.ConfigurationId, Is.EqualTo("test-physiology"));
            Assert.That(configuration.ConfigurationVersion, Is.EqualTo(2));
            Assert.That(configuration.StaleAfterSeconds, Is.EqualTo(90d));
            Assert.That(configuration.MinimumWindowDurationSeconds, Is.EqualTo(30d));
            Assert.That(configuration.MaximumFutureClockSkewSeconds, Is.EqualTo(2d));
            Assert.That(configuration.SourceTimestampToleranceSeconds, Is.EqualTo(0.001d));
            Assert.That(configuration.ProbabilitySumTolerance, Is.EqualTo(0.005d));
            Assert.That(configuration.MinimumDecisionSignalQuality, Is.EqualTo(0.8d));
            Assert.That(configuration.MinimumRewardSignalQuality, Is.EqualTo(0.9d));
            Assert.That(configuration.MaximumBufferedWindows, Is.EqualTo(4));
        }

        [Test]
        public void Constructor_RejectsMissingIdentityAndInvalidCapacity()
        {
            Assert.Throws<ArgumentException>(() => CreateConfiguration(" ", 2, 4));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                CreateConfiguration("test", 0, 4));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                CreateConfiguration("test", 2, 0));
        }

        [TestCase(0d)]
        [TestCase(-1d)]
        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        public void Constructor_RejectsInvalidPositiveTiming(double invalidValue)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new PhysiologyValidationConfiguration(
                    "test",
                    1,
                    invalidValue,
                    30d,
                    2d,
                    0.001d,
                    0.005d,
                    0.8d,
                    0.9d,
                    4));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new PhysiologyValidationConfiguration(
                    "test",
                    1,
                    90d,
                    invalidValue,
                    2d,
                    0.001d,
                    0.005d,
                    0.8d,
                    0.9d,
                    4));
        }

        [TestCase(-0.1d)]
        [TestCase(1.1d)]
        [TestCase(double.NaN)]
        public void Constructor_RejectsSignalQualityThresholdOutsideUnitRange(
            double invalidValue)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new PhysiologyValidationConfiguration(
                    "test",
                    1,
                    90d,
                    30d,
                    2d,
                    0.001d,
                    0.005d,
                    invalidValue,
                    0.9d,
                    4));
        }

        private static PhysiologyValidationConfiguration CreateConfiguration(
            string configurationId = " test-physiology ",
            int version = 2,
            int capacity = 4)
        {
            return new PhysiologyValidationConfiguration(
                configurationId,
                version,
                90d,
                30d,
                2d,
                0.001d,
                0.005d,
                0.8d,
                0.9d,
                capacity);
        }
    }
}

