using System;
using LaminarVR.AdaptiveMeditation.Networking;
using NUnit.Framework;

namespace LaminarVR.AdaptiveMeditation.Tests.EditMode.Networking
{
    public sealed class ReconnectBackoffConfigurationTests
    {
        [Test]
        public void Constructor_StoresIdentityAndBuildsBoundedSchedule()
        {
            var configuration = new ReconnectBackoffConfiguration(
                " reconnect-pilot ",
                2,
                4,
                1d,
                5d,
                2d);

            Assert.That(configuration.ConfigurationId, Is.EqualTo("reconnect-pilot"));
            Assert.That(configuration.ConfigurationVersion, Is.EqualTo(2));
            Assert.That(configuration.MaximumAttempts, Is.EqualTo(4));
            Assert.That(configuration.GetDelaySeconds(1), Is.EqualTo(1d));
            Assert.That(configuration.GetDelaySeconds(2), Is.EqualTo(2d));
            Assert.That(configuration.GetDelaySeconds(3), Is.EqualTo(4d));
            Assert.That(configuration.GetDelaySeconds(4), Is.EqualTo(5d));
        }

        [TestCase(null, 1, 1, 1d, 4d, 2d)]
        [TestCase("test", 0, 1, 1d, 4d, 2d)]
        [TestCase("test", 1, 0, 1d, 4d, 2d)]
        [TestCase("test", 1, 1, -1d, 4d, 2d)]
        [TestCase("test", 1, 1, 5d, 4d, 2d)]
        [TestCase("test", 1, 1, 1d, 4d, 0.5d)]
        [TestCase("test", 1, 1, 1d, double.PositiveInfinity, 2d)]
        public void Constructor_RejectsInvalidConfiguration(
            string configurationId,
            int configurationVersion,
            int maximumAttempts,
            double initialDelaySeconds,
            double maximumDelaySeconds,
            double delayMultiplier)
        {
            Assert.Catch<ArgumentException>(
                () => new ReconnectBackoffConfiguration(
                    configurationId,
                    configurationVersion,
                    maximumAttempts,
                    initialDelaySeconds,
                    maximumDelaySeconds,
                    delayMultiplier));
        }

        [Test]
        public void GetDelaySeconds_RejectsAttemptOutsideSchedule()
        {
            var configuration = new ReconnectBackoffConfiguration(
                "test",
                1,
                2,
                0d,
                1d,
                2d);

            Assert.Throws<ArgumentOutOfRangeException>(
                () => configuration.GetDelaySeconds(0));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => configuration.GetDelaySeconds(3));
        }
    }
}
