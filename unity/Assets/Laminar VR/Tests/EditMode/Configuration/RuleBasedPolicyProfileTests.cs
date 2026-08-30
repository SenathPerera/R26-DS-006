using LaminarVR.AdaptiveMeditation.Policy.RuleBased;
using LaminarVR.AdaptiveMeditation.Runtime.Configuration;
using NUnit.Framework;
using UnityEngine;

namespace LaminarVR.AdaptiveMeditation.Tests.EditMode.Configuration
{
    public sealed class RuleBasedPolicyProfileTests
    {
        [Test]
        public void NewProfile_IsRejectedUntilResearchConfigurationIsApproved()
        {
            var asset = ScriptableObject.CreateInstance<RuleBasedPolicyProfile>();

            try
            {
                var created = asset.TryCreateRuntimeConfiguration(
                    out var configuration,
                    out var validationError);

                Assert.That(created, Is.False);
                Assert.That(configuration, Is.Null);
                Assert.That(validationError, Does.Contain("not approved"));
            }
            finally
            {
                Object.DestroyImmediate(asset);
            }
        }

        [Test]
        public void ApprovedProfileWithPlaceholderValues_IsRejected()
        {
            const string json = @"{
                ""researchConfigurationApproved"": true
            }";
            var asset = ScriptableObject.CreateInstance<RuleBasedPolicyProfile>();

            try
            {
                JsonUtility.FromJsonOverwrite(json, asset);

                var created = asset.TryCreateRuntimeConfiguration(
                    out var configuration,
                    out var validationError);

                Assert.That(created, Is.False);
                Assert.That(configuration, Is.Null);
                Assert.That(validationError, Is.Not.Empty);
            }
            finally
            {
                Object.DestroyImmediate(asset);
            }
        }

        [Test]
        public void ApprovedValidProfile_CreatesImmutableRuntimeConfiguration()
        {
            const string json = @"{
                ""configurationId"": ""rule-pilot"",
                ""configurationVersion"": 2,
                ""researchConfigurationApproved"": true,
                ""activationMode"": 2,
                ""minimumContinuousStressScore"": 2.0,
                ""minimumStressIncreasePerMinute"": 0.5,
                ""minimumPreferenceDelta"": 0.05
            }";
            var asset = ScriptableObject.CreateInstance<RuleBasedPolicyProfile>();

            try
            {
                JsonUtility.FromJsonOverwrite(json, asset);

                var created = asset.TryCreateRuntimeConfiguration(
                    out var configuration,
                    out var validationError);

                Assert.That(created, Is.True, validationError);
                Assert.That(configuration, Is.Not.Null);
                Assert.That(configuration.ConfigurationId, Is.EqualTo("rule-pilot"));
                Assert.That(configuration.ConfigurationVersion, Is.EqualTo(2));
                Assert.That(
                    configuration.ActivationMode,
                    Is.EqualTo(
                        RuleActivationMode.WorseningTrendOrElevatedStress));
                Assert.That(configuration.MinimumPreferenceDelta, Is.EqualTo(0.05d).Within(1e-6d));
            }
            finally
            {
                Object.DestroyImmediate(asset);
            }
        }
    }
}
