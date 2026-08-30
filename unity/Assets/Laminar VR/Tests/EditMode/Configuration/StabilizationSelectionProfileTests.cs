using LaminarVR.AdaptiveMeditation.Runtime.Configuration;
using NUnit.Framework;
using UnityEngine;

namespace LaminarVR.AdaptiveMeditation.Tests.EditMode.Configuration
{
    public sealed class StabilizationSelectionProfileTests
    {
        [Test]
        public void Profile_FailsClosedUntilResearchConfigurationIsApproved()
        {
            var profile = ScriptableObject.CreateInstance<
                StabilizationSelectionProfile>();
            try
            {
                Assert.That(
                    profile.TryCreateRuntimeConfiguration(
                        out var configuration,
                        out var error),
                    Is.False);
                Assert.That(configuration, Is.Null);
                Assert.That(error, Does.Contain("not approved"));
            }
            finally
            {
                Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void Profile_CreatesOnlyExplicitVersionedValues()
        {
            var profile = ScriptableObject.CreateInstance<
                StabilizationSelectionProfile>();
            try
            {
                JsonUtility.FromJsonOverwrite(
                    "{\"configurationId\":\"pilot-stabilization\","
                    + "\"configurationVersion\":2,"
                    + "\"researchConfigurationApproved\":true,"
                    + "\"recentOutcomeCount\":4,"
                    + "\"rewardRecencyDecay\":0.8,"
                    + "\"preferenceDistancePenaltyWeight\":0.25}",
                    profile);

                Assert.That(
                    profile.TryCreateRuntimeConfiguration(
                        out var configuration,
                        out var error),
                    Is.True,
                    error);
                Assert.That(configuration.ConfigurationId,
                    Is.EqualTo("pilot-stabilization"));
                Assert.That(configuration.ConfigurationVersion, Is.EqualTo(2));
                Assert.That(configuration.RecentOutcomeCount, Is.EqualTo(4));
                Assert.That(configuration.RewardRecencyDecay,
                    Is.EqualTo(0.8d).Within(1e-6d));
                Assert.That(configuration.PreferenceDistancePenaltyWeight,
                    Is.EqualTo(0.25d).Within(1e-6d));
            }
            finally
            {
                Object.DestroyImmediate(profile);
            }
        }
    }
}
