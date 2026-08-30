using System;
using LaminarVR.AdaptiveMeditation.Preferences;
using NUnit.Framework;

namespace LaminarVR.AdaptiveMeditation.Tests.EditMode.Preferences
{
    public sealed class PreferenceInitializationConfigurationTests
    {
        [Test]
        public void Constructor_StoresTrimmedIdentityAndWeight()
        {
            var configuration = new PreferenceInitializationConfiguration(
                "  pilot-preferences  ",
                2,
                0.65d);

            Assert.That(configuration.ConfigurationId, Is.EqualTo("pilot-preferences"));
            Assert.That(configuration.ConfigurationVersion, Is.EqualTo(2));
            Assert.That(configuration.PreferenceWeight, Is.EqualTo(0.65d));
        }

        [TestCase(null, 1, 0.5d)]
        [TestCase(" ", 1, 0.5d)]
        [TestCase("test", 0, 0.5d)]
        [TestCase("test", 1, -0.01d)]
        [TestCase("test", 1, 1.01d)]
        [TestCase("test", 1, double.NaN)]
        [TestCase("test", 1, double.PositiveInfinity)]
        public void Constructor_RejectsInvalidConfiguration(
            string configurationId,
            int configurationVersion,
            double preferenceWeight)
        {
            Assert.Catch<ArgumentException>(
                () => new PreferenceInitializationConfiguration(
                    configurationId,
                    configurationVersion,
                    preferenceWeight));
        }
    }
}
