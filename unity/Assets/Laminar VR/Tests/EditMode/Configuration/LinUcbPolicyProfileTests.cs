using LaminarVR.AdaptiveMeditation.Policy;
using LaminarVR.AdaptiveMeditation.Runtime.Configuration;
using NUnit.Framework;
using UnityEngine;

namespace LaminarVR.AdaptiveMeditation.Tests.EditMode.Configuration
{
    public sealed class LinUcbPolicyProfileTests
    {
        [Test]
        public void NewProfile_IsRejectedUntilResearchConfigurationIsApproved()
        {
            var asset = ScriptableObject.CreateInstance<LinUcbPolicyProfile>();

            try
            {
                var created = asset.TryCreateRuntimeConfiguration(
                    new PolicyFeatureVectorBuilder(),
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
            var asset = ScriptableObject.CreateInstance<LinUcbPolicyProfile>();

            try
            {
                JsonUtility.FromJsonOverwrite(json, asset);

                var created = asset.TryCreateRuntimeConfiguration(
                    new PolicyFeatureVectorBuilder(),
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
        public void ApprovedProfile_BindsExactFeatureSchemaAndDimension()
        {
            const string json = @"{
                ""configurationId"": ""linucb-pilot"",
                ""configurationVersion"": 2,
                ""researchConfigurationApproved"": true,
                ""ridgeRegularization"": 0.75,
                ""explorationCoefficient"": 0.2
            }";
            var asset = ScriptableObject.CreateInstance<LinUcbPolicyProfile>();
            var builder = new PolicyFeatureVectorBuilder();

            try
            {
                JsonUtility.FromJsonOverwrite(json, asset);

                var created = asset.TryCreateRuntimeConfiguration(
                    builder,
                    out var configuration,
                    out var validationError);

                Assert.That(created, Is.True, validationError);
                Assert.That(configuration.ConfigurationId, Is.EqualTo("linucb-pilot"));
                Assert.That(configuration.ConfigurationVersion, Is.EqualTo(2));
                Assert.That(configuration.FeatureCount, Is.EqualTo(builder.FeatureCount));
                Assert.That(
                    configuration.FeatureSchemaVersion,
                    Is.EqualTo(builder.FeatureSchemaVersion));
                Assert.That(
                    configuration.RidgeRegularization,
                    Is.EqualTo(0.75d).Within(1e-6d));
                Assert.That(
                    configuration.ExplorationCoefficient,
                    Is.EqualTo(0.2d).Within(1e-6d));
            }
            finally
            {
                Object.DestroyImmediate(asset);
            }
        }
    }
}
