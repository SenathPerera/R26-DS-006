using LaminarVR.AdaptiveMeditation.Runtime.Configuration;
using NUnit.Framework;
using UnityEngine;

namespace LaminarVR.AdaptiveMeditation.Tests.EditMode.Configuration
{
    public sealed class ProductionCoordinatorProfileTests
    {
        [Test]
        public void DefaultProfile_IsNotApprovedForRuntimeUse()
        {
            var profile = ScriptableObject.CreateInstance<
                ProductionCoordinatorProfile>();
            try
            {
                var created = profile.TryCreateRuntimeConfiguration(
                    out var configuration,
                    out var validationError);

                Assert.That(created, Is.False);
                Assert.That(configuration, Is.Null);
                Assert.That(validationError, Does.Contain("not approved"));
            }
            finally
            {
                Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void ApprovedProfile_UsesConfiguredSixtySecondCadence()
        {
            const string json = @"{
                ""configurationId"": ""coordinator-test"",
                ""configurationVersion"": 1,
                ""researchConfigurationApproved"": true,
                ""expectedPhysiologyOutputIntervalSeconds"": 60.0,
                ""maximumConsecutiveSameDirectionActions"": 2,
                ""maximumTotalVariation"": 0.8
            }";
            var profile = ScriptableObject.CreateInstance<
                ProductionCoordinatorProfile>();
            try
            {
                JsonUtility.FromJsonOverwrite(json, profile);

                var created = profile.TryCreateRuntimeConfiguration(
                    out var configuration,
                    out var validationError);

                Assert.That(created, Is.True, validationError);
                Assert.That(
                    configuration.ExpectedPhysiologyOutputIntervalSeconds,
                    Is.EqualTo(60d));
                Assert.That(
                    configuration.SafetyLimits
                        .MaximumConsecutiveSameDirectionActions,
                    Is.EqualTo(2));
                Assert.That(
                    configuration.SafetyLimits.MaximumTotalVariation,
                    Is.EqualTo(0.8d).Within(1e-6d));
            }
            finally
            {
                Object.DestroyImmediate(profile);
            }
        }
    }
}
