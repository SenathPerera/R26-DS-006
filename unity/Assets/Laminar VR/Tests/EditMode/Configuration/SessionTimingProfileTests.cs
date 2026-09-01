using LaminarVR.AdaptiveMeditation.Runtime.Configuration;
using NUnit.Framework;
using UnityEngine;

namespace LaminarVR.AdaptiveMeditation.Tests.EditMode.Configuration
{
    public sealed class SessionTimingProfileTests
    {
        [Test]
        public void NewProfile_IsRejectedUntilResearchConfigurationIsApproved()
        {
            var asset = ScriptableObject.CreateInstance<SessionTimingProfile>();

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
        public void ApprovedProfileWithPlaceholderTiming_IsRejected()
        {
            const string json = @"{
                ""configurationId"": ""pilot-session"",
                ""configurationVersion"": 1,
                ""researchConfigurationApproved"": true
            }";
            var asset = ScriptableObject.CreateInstance<SessionTimingProfile>();

            try
            {
                JsonUtility.FromJsonOverwrite(json, asset);

                var created = asset.TryCreateRuntimeConfiguration(
                    out var configuration,
                    out var validationError);

                Assert.That(created, Is.False);
                Assert.That(configuration, Is.Null);
                Assert.That(validationError, Does.Contain("greater than 0"));
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
                ""configurationId"": ""pilot-session"",
                ""configurationVersion"": 2,
                ""researchConfigurationApproved"": true,
                ""acclimatizationDurationSeconds"": 10.0,
                ""adaptiveDurationSeconds"": 20.0,
                ""stabilizationDurationSeconds"": 5.0,
                ""decisionIntervalSeconds"": 4.0
            }";
            var asset = ScriptableObject.CreateInstance<SessionTimingProfile>();

            try
            {
                JsonUtility.FromJsonOverwrite(json, asset);

                var created = asset.TryCreateRuntimeConfiguration(
                    out var configuration,
                    out var validationError);

                Assert.That(created, Is.True, validationError);
                Assert.That(configuration, Is.Not.Null);
                Assert.That(configuration.ConfigurationId, Is.EqualTo("pilot-session"));
                Assert.That(configuration.ConfigurationVersion, Is.EqualTo(2));
                Assert.That(configuration.TimedSessionDurationSeconds, Is.EqualTo(35d));
                Assert.That(configuration.DecisionIntervalSeconds, Is.EqualTo(4d));
            }
            finally
            {
                Object.DestroyImmediate(asset);
            }
        }
    }
}

