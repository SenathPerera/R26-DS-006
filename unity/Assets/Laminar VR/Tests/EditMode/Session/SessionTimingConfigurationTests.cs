using System;
using LaminarVR.AdaptiveMeditation.Session;
using NUnit.Framework;

namespace LaminarVR.AdaptiveMeditation.Tests.EditMode.Session
{
    public sealed class SessionTimingConfigurationTests
    {
        [Test]
        public void Constructor_StoresValidatedTimingAndIdentity()
        {
            var configuration = new SessionTimingConfiguration(
                " pilot-session ",
                3,
                10d,
                20d,
                5d,
                4d);

            Assert.That(configuration.ConfigurationId, Is.EqualTo("pilot-session"));
            Assert.That(configuration.ConfigurationVersion, Is.EqualTo(3));
            Assert.That(configuration.AcclimatizationDurationSeconds, Is.EqualTo(10d));
            Assert.That(configuration.AdaptiveDurationSeconds, Is.EqualTo(20d));
            Assert.That(configuration.StabilizationDurationSeconds, Is.EqualTo(5d));
            Assert.That(configuration.DecisionIntervalSeconds, Is.EqualTo(4d));
            Assert.That(configuration.TimedSessionDurationSeconds, Is.EqualTo(35d));
        }

        [Test]
        public void Constructor_RejectsMissingIdentityOrInvalidVersion()
        {
            Assert.Throws<ArgumentException>(() =>
                new SessionTimingConfiguration(" ", 1, 1d, 1d, 1d, 1d));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new SessionTimingConfiguration("pilot", 0, 1d, 1d, 1d, 1d));
        }

        [TestCase(0d)]
        [TestCase(-1d)]
        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        public void Constructor_RejectsInvalidResearchTiming(double invalidDurationSeconds)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new SessionTimingConfiguration(
                    "pilot",
                    1,
                    invalidDurationSeconds,
                    1d,
                    1d,
                    1d));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new SessionTimingConfiguration(
                    "pilot",
                    1,
                    1d,
                    invalidDurationSeconds,
                    1d,
                    1d));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new SessionTimingConfiguration(
                    "pilot",
                    1,
                    1d,
                    1d,
                    invalidDurationSeconds,
                    1d));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new SessionTimingConfiguration(
                    "pilot",
                    1,
                    1d,
                    1d,
                    1d,
                    invalidDurationSeconds));
        }
    }
}

