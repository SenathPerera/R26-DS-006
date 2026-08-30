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
                AssertColor(
                    mapping.NeutralDirectionalLightColor,
                    new Color(0.9f, 0.9f, 0.9f, 1f));
                Assert.That(mapping.FogDensityRange,
                    Is.EqualTo(new Vector2(0.001f, 0.01f)));
                Assert.That(mapping.SaturationRange,
                    Is.EqualTo(new Vector2(-20f, 20f)));
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

        [Test]
        public void RuntimeMapping_PreservesCoolNeutralAndWarmAnchors()
        {
            var profile = CreateApprovedProfile();

            try
            {
                Assert.That(profile.TryCreateRuntimeMapping(
                    out var mapping,
                    out var validationError),
                    Is.True,
                    validationError);

                AssertColor(
                    mapping.MapDirectionalLightColor(0f),
                    new Color(0.7f, 0.8f, 1f, 1f));
                AssertColor(
                    mapping.MapDirectionalLightColor(0.5f),
                    new Color(0.9f, 0.9f, 0.9f, 1f));
                AssertColor(
                    mapping.MapDirectionalLightColor(1f),
                    new Color(1f, 0.8f, 0.6f, 1f));
                AssertColor(
                    mapping.MapDirectionalLightColor(0.25f),
                    new Color(0.8f, 0.85f, 0.95f, 1f));
                AssertColor(
                    mapping.MapDirectionalLightColor(0.75f),
                    new Color(0.95f, 0.85f, 0.75f, 1f));
            }
            finally
            {
                Object.DestroyImmediate(profile);
            }
        }

        [TestCase(-0.01f)]
        [TestCase(1.01f)]
        [TestCase(float.NaN)]
        public void RuntimeMapping_RejectsWarmthOutsideNormalizedDomain(
            float normalizedWarmth)
        {
            var profile = CreateApprovedProfile();

            try
            {
                Assert.That(profile.TryCreateRuntimeMapping(
                    out var mapping,
                    out var validationError),
                    Is.True,
                    validationError);

                Assert.Throws<System.ArgumentOutOfRangeException>(
                    () => mapping.MapDirectionalLightColor(normalizedWarmth));
            }
            finally
            {
                Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void ApprovedProfile_RejectsInvalidNeutralWarmthAnchor()
        {
            var profile = CreateApprovedProfile();

            try
            {
                const string invalidNeutral = @"{
                    ""neutralDirectionalLightColor"": {
                        ""r"": 1.1,
                        ""g"": 0.9,
                        ""b"": 0.9,
                        ""a"": 1.0
                    }
                }";
                JsonUtility.FromJsonOverwrite(invalidNeutral, profile);

                var created = profile.TryCreateRuntimeMapping(
                    out var mapping,
                    out var validationError);

                Assert.That(created, Is.False);
                Assert.That(mapping, Is.Null);
                Assert.That(validationError, Does.Contain("colors"));
            }
            finally
            {
                Object.DestroyImmediate(profile);
            }
        }

        [TestCase(-101f, 10f)]
        [TestCase(-10f, 101f)]
        [TestCase(0f, 0f)]
        [TestCase(10f, -10f)]
        public void ApprovedProfile_RejectsInvalidSaturationRange(
            float minimum,
            float maximum)
        {
            var profile = CreateApprovedProfile();

            try
            {
                var json = "{\"saturationRange\":{\"x\":"
                    + minimum.ToString(
                        System.Globalization.CultureInfo.InvariantCulture)
                    + ",\"y\":"
                    + maximum.ToString(
                        System.Globalization.CultureInfo.InvariantCulture)
                    + "}}";
                JsonUtility.FromJsonOverwrite(json, profile);

                var created = profile.TryCreateRuntimeMapping(
                    out var mapping,
                    out var validationError);

                Assert.That(created, Is.False);
                Assert.That(mapping, Is.Null);
                Assert.That(validationError, Does.Contain("saturation"));
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
                ""neutralDirectionalLightColor"": { ""r"": 0.9, ""g"": 0.9, ""b"": 0.9, ""a"": 1.0 },
                ""warmDirectionalLightColor"": { ""r"": 1.0, ""g"": 0.8, ""b"": 0.6, ""a"": 1.0 },
                ""fogDensityRange"": { ""x"": 0.001, ""y"": 0.01 },
                ""clearFogColor"": { ""r"": 0.7, ""g"": 0.8, ""b"": 0.9, ""a"": 1.0 },
                ""softFogColor"": { ""r"": 0.8, ""g"": 0.8, ""b"": 0.8, ""a"": 1.0 },
                ""saturationRange"": { ""x"": -20.0, ""y"": 20.0 },
                ""waterMotionProperty"": ""_RippleMotion"",
                ""waterMotionRange"": { ""x"": 0.1, ""y"": 0.4 }
            }";
            var profile = ScriptableObject.CreateInstance<
                TemplePondEnvironmentMappingProfile>();
            JsonUtility.FromJsonOverwrite(json, profile);
            return profile;
        }

        private static void AssertColor(Color actual, Color expected)
        {
            Assert.That(actual.r, Is.EqualTo(expected.r).Within(1e-6f));
            Assert.That(actual.g, Is.EqualTo(expected.g).Within(1e-6f));
            Assert.That(actual.b, Is.EqualTo(expected.b).Within(1e-6f));
            Assert.That(actual.a, Is.EqualTo(expected.a).Within(1e-6f));
        }
    }
}
