using LaminarVR.AdaptiveMeditation.Runtime.Configuration;
using NUnit.Framework;
using UnityEngine;

namespace LaminarVR.AdaptiveMeditation.Tests.EditMode.Configuration
{
    public sealed class SceneParameterProfileTests
    {
        private const string ApprovedProfileJson = @"{
            ""sceneId"": ""temple-pond"",
            ""displayName"": ""Temple Pond"",
            ""researchConfigurationApproved"": true,
            ""defaultIllumination"": 0.5,
            ""defaultWarmth"": 0.5,
            ""defaultAtmosphericSoftness"": 0.5,
            ""defaultColorRichness"": 0.5,
            ""defaultAmbientMotion"": 0.5,
            ""illuminationRange"": { ""x"": 0.2, ""y"": 0.8 },
            ""warmthRange"": { ""x"": 0.2, ""y"": 0.8 },
            ""atmosphericSoftnessRange"": { ""x"": 0.2, ""y"": 0.8 },
            ""colorRichnessRange"": { ""x"": 0.2, ""y"": 0.8 },
            ""ambientMotionRange"": { ""x"": 0.2, ""y"": 0.8 },
            ""illuminationActionStep"": 0.1,
            ""warmthActionStep"": 0.25,
            ""atmosphericSoftnessActionStep"": 0.3,
            ""colorRichnessActionStep"": 0.2,
            ""ambientMotionActionStep"": 0.2,
            ""transitionDurationSeconds"": 2.0,
            ""minimumSecondsBetweenActions"": 5.0
        }";

        [Test]
        public void NewProfile_IsRejectedUntilResearchConfigurationIsApproved()
        {
            var asset = ScriptableObject.CreateInstance<SceneParameterProfile>();

            try
            {
                var created = asset.TryCreateRuntimeProfile(
                    out var runtimeProfile,
                    out var validationError);

                Assert.That(created, Is.False);
                Assert.That(runtimeProfile, Is.Null);
                Assert.That(validationError, Does.Contain("not approved"));
            }
            finally
            {
                Object.DestroyImmediate(asset);
            }
        }

        [Test]
        public void ApprovedValidProfile_CreatesImmutableRuntimeConfiguration()
        {
            var asset = ScriptableObject.CreateInstance<SceneParameterProfile>();

            try
            {
                JsonUtility.FromJsonOverwrite(ApprovedProfileJson, asset);

                var created = asset.TryCreateRuntimeProfile(
                    out var runtimeProfile,
                    out var validationError);

                Assert.That(created, Is.True, validationError);
                Assert.That(runtimeProfile, Is.Not.Null);
                Assert.That(runtimeProfile.SceneId, Is.EqualTo("temple-pond"));
                Assert.That(runtimeProfile.Limits.Contains(runtimeProfile.SafeDefault), Is.True);
                Assert.That(runtimeProfile.ActionSteps.Illumination,
                    Is.EqualTo(0.1f));
                Assert.That(runtimeProfile.ActionSteps.Warmth,
                    Is.EqualTo(0.25f));
                Assert.That(runtimeProfile.ActionSteps.AtmosphericSoftness,
                    Is.EqualTo(0.3f));
                Assert.That(runtimeProfile.ActionSteps.ColorRichness,
                    Is.EqualTo(0.2f));
                Assert.That(runtimeProfile.ActionSteps.AmbientMotion,
                    Is.EqualTo(0.2f));
            }
            finally
            {
                Object.DestroyImmediate(asset);
            }
        }

        [Test]
        public void ApprovedProfile_RejectsAnUnconfiguredDimensionActionStep()
        {
            var asset = ScriptableObject.CreateInstance<SceneParameterProfile>();

            try
            {
                JsonUtility.FromJsonOverwrite(ApprovedProfileJson, asset);
                JsonUtility.FromJsonOverwrite(
                    "{\"ambientMotionActionStep\":0}",
                    asset);

                var created = asset.TryCreateRuntimeProfile(
                    out var runtimeProfile,
                    out var validationError);

                Assert.That(created, Is.False);
                Assert.That(runtimeProfile, Is.Null);
                Assert.That(validationError,
                    Does.Contain("ambientMotion"));
            }
            finally
            {
                Object.DestroyImmediate(asset);
            }
        }
    }
}
