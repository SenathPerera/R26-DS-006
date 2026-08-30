using LaminarVR.AdaptiveMeditation.Runtime.Environment;
using NUnit.Framework;
using UnityEngine;

namespace LaminarVR.AdaptiveMeditation.Tests.EditMode.Configuration
{
    public sealed class TemplePondEnvironmentMappingProfileTests
    {
        [Test]
        public void NewProfile_IsRejectedUntilResearchConfigurationIsApproved()
        {
            var profile = ScriptableObject.CreateInstance<
                TemplePondEnvironmentMappingProfile>();

            try
            {
                var created = profile.TryCreateRuntimeMapping(
                    out var mapping,
                    out var validationError);

                Assert.That(created, Is.False);
                Assert.That(mapping, Is.Null);
                Assert.That(validationError, Does.Contain("not approved"));
            }
            finally
            {
                Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void ApprovedProfile_CreatesImmutableFiveDimensionMapping()
        {
            var profile = CreateApprovedProfile();

            try
            {
                var created = profile.TryCreateRuntimeMapping(
                    out var mapping,
                    out var validationError);

                Assert.That(created, Is.True, validationError);
                Assert.That(mapping.ConfigurationId,
                    Is.EqualTo("temple-mapping-test"));
                Assert.That(mapping.ConfigurationVersion, Is.EqualTo(1));
                Assert.That(mapping.DirectionalLightIntensityRange,
                    Is.EqualTo(new Vector2(1f, 3f)));
                Assert.That(mapping.FogDensityRange,
                    Is.EqualTo(new Vector2(0.001f, 0.01f)));
                Assert.That(mapping.WaterColorProperty,
                    Is.EqualTo("_BaseColor"));
                Assert.That(mapping.WaterMotionProperty,
                    Is.EqualTo("_RippleMotion"));
                Assert.That(mapping.WaterMotionRange,
                    Is.EqualTo(new Vector2(0.1f, 0.4f)));
            }
            finally
            {
                Object.DestroyImmediate(profile);
            }
        }

        private static TemplePondEnvironmentMappingProfile
            CreateApprovedProfile()
        {
            const string json = @"{
                ""configurationId"": ""temple-mapping-test"",
                ""configurationVersion"": 1,
                ""researchConfigurationApproved"": true,
                ""directionalLightIntensityRange"": { ""x"": 1.0, ""y"": 3.0 },
                ""coolDirectionalLightColor"": { ""r"": 0.7, ""g"": 0.8, ""b"": 1.0, ""a"": 1.0 },
                ""warmDirectionalLightColor"": { ""r"": 1.0, ""g"": 0.8, ""b"": 0.6, ""a"": 1.0 },
                ""fogDensityRange"": { ""x"": 0.001, ""y"": 0.01 },
                ""clearFogColor"": { ""r"": 0.7, ""g"": 0.8, ""b"": 0.9, ""a"": 1.0 },
                ""softFogColor"": { ""r"": 0.8, ""g"": 0.8, ""b"": 0.8, ""a"": 1.0 },
                ""waterColorProperty"": ""_BaseColor"",
                ""mutedWaterColor"": { ""r"": 0.1, ""g"": 0.2, ""b"": 0.2, ""a"": 1.0 },
                ""richWaterColor"": { ""r"": 0.0, ""g"": 0.4, ""b"": 0.6, ""a"": 1.0 },
                ""waterMotionProperty"": ""_RippleMotion"",
                ""waterMotionRange"": { ""x"": 0.1, ""y"": 0.4 }
            }";
            var profile = ScriptableObject.CreateInstance<
                TemplePondEnvironmentMappingProfile>();
            JsonUtility.FromJsonOverwrite(json, profile);
            return profile;
        }
    }
}
