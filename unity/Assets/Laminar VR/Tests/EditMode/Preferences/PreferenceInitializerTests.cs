using System;
using LaminarVR.AdaptiveMeditation.Environment;
using LaminarVR.AdaptiveMeditation.Preferences;
using NUnit.Framework;

namespace LaminarVR.AdaptiveMeditation.Tests.EditMode.Preferences
{
    public sealed class PreferenceInitializerTests
    {
        private readonly PreferenceInitializer initializer =
            new PreferenceInitializer();

        [Test]
        public void Initialize_BlendsSafeDefaultAndPreferencePerDimension()
        {
            var profile = CreateSceneProfile();
            var preference = new EnvironmentPreference(
                new EnvironmentState(0.8f, 0.7f, 0.6f, 0.5f, 0.4f));

            var result = initializer.Initialize(
                profile,
                preference,
                CreateConfiguration(0.25d));

            Assert.That(result.Accepted, Is.True);
            Assert.That(result.WasAdjusted, Is.False);
            AssertState(
                result.SafeInitialState,
                new EnvironmentState(0.575f, 0.55f, 0.525f, 0.5f, 0.475f));
        }

        [TestCase(0d, 0.5f)]
        [TestCase(1d, 0.8f)]
        public void Initialize_RespectsPreferenceWeightEndpoints(
            double preferenceWeight,
            float expectedIllumination)
        {
            var profile = CreateSceneProfile();
            var preference = new EnvironmentPreference(
                new EnvironmentState(0.8f, 0.7f, 0.6f, 0.5f, 0.4f));

            var result = initializer.Initialize(
                profile,
                preference,
                CreateConfiguration(preferenceWeight));

            Assert.That(
                result.SafeInitialState.Illumination,
                Is.EqualTo(expectedIllumination).Within(0.00001f));
        }

        [Test]
        public void Initialize_ClampsPreferenceToNormalizedDomainThenSceneLimits()
        {
            var profile = CreateSceneProfile();
            var preference = new EnvironmentPreference(
                new EnvironmentState(-0.2f, 1.2f, 0.1f, 0.9f, 0.5f));

            var result = initializer.Initialize(
                profile,
                preference,
                CreateConfiguration(1d));

            Assert.That(result.Accepted, Is.True);
            Assert.That(
                result.Adjustments,
                Is.EqualTo(
                    PreferenceInitializationAdjustment.NormalizedDomainClamp
                    | PreferenceInitializationAdjustment.SceneRangeClamp));
            Assert.That(
                result.SceneClampedPreference,
                Is.EqualTo(new EnvironmentState(0.2f, 0.8f, 0.2f, 0.8f, 0.5f)));
            Assert.That(
                result.SafeInitialState,
                Is.EqualTo(result.SceneClampedPreference));
        }

        [Test]
        public void Initialize_ClampsBlendedStateToSensitivityLimits()
        {
            var sensitivityLimits = CreateUniformLimits(0.45f, 0.55f);
            var preference = new EnvironmentPreference(
                new EnvironmentState(0.8f, 0.8f, 0.8f, 0.8f, 0.8f),
                sensitivityLimits);

            var result = initializer.Initialize(
                CreateSceneProfile(),
                preference,
                CreateConfiguration(1d));

            Assert.That(result.Accepted, Is.True);
            Assert.That(
                result.Adjustments,
                Is.EqualTo(
                    PreferenceInitializationAdjustment.SensitivityRangeClamp));
            Assert.That(
                result.SafeInitialState,
                Is.EqualTo(new EnvironmentState(0.55f, 0.55f, 0.55f, 0.55f, 0.55f)));
            Assert.That(result.EffectiveLimits, Is.EqualTo(sensitivityLimits));
        }

        [Test]
        public void Initialize_UsesIntersectionOfSceneAndSensitivityLimits()
        {
            var sensitivityLimits = CreateUniformLimits(0.1f, 0.6f);
            var preference = new EnvironmentPreference(
                new EnvironmentState(0.1f, 0.1f, 0.1f, 0.1f, 0.1f),
                sensitivityLimits);

            var result = initializer.Initialize(
                CreateSceneProfile(),
                preference,
                CreateConfiguration(1d));

            Assert.That(result.Accepted, Is.True);
            Assert.That(
                result.EffectiveLimits.Illumination,
                Is.EqualTo(new NormalizedRange(0.2f, 0.6f)));
            Assert.That(
                result.SafeInitialState,
                Is.EqualTo(new EnvironmentState(0.2f, 0.2f, 0.2f, 0.2f, 0.2f)));
        }

        [Test]
        public void Initialize_FailsSafeWhenSensitivityAndSceneDoNotOverlap()
        {
            var sensitivityLimits = CreateUniformLimits(0f, 0.1f);
            var preference = new EnvironmentPreference(
                new EnvironmentState(0.05f, 0.05f, 0.05f, 0.05f, 0.05f),
                sensitivityLimits);
            var profile = CreateSceneProfile();

            var result = initializer.Initialize(
                profile,
                preference,
                CreateConfiguration(1d));

            Assert.That(result.Accepted, Is.False);
            Assert.That(
                result.FailureReason,
                Is.EqualTo(
                    PreferenceInitializationFailureReason
                        .SensitivityLimitsDoNotOverlapScene));
            Assert.That(result.SafeInitialState, Is.EqualTo(profile.SafeDefault));
            Assert.That(result.EffectiveLimits, Is.EqualTo(profile.Limits));
        }

        [Test]
        public void Initialize_RejectsMissingInputs()
        {
            var profile = CreateSceneProfile();
            var preference = new EnvironmentPreference(profile.SafeDefault);
            var configuration = CreateConfiguration(0.5d);

            Assert.Throws<ArgumentNullException>(
                () => initializer.Initialize(null, preference, configuration));
            Assert.Throws<ArgumentNullException>(
                () => initializer.Initialize(profile, null, configuration));
            Assert.Throws<ArgumentNullException>(
                () => initializer.Initialize(profile, preference, null));
        }

        private static PreferenceInitializationConfiguration CreateConfiguration(
            double preferenceWeight)
        {
            return new PreferenceInitializationConfiguration(
                "preference-test",
                1,
                preferenceWeight);
        }

        private static SceneEnvironmentProfile CreateSceneProfile()
        {
            return new SceneEnvironmentProfile(
                "test-scene",
                "Test Scene",
                new EnvironmentState(0.5f, 0.5f, 0.5f, 0.5f, 0.5f),
                CreateUniformLimits(0.2f, 0.8f),
                0.05f,
                5f,
                30f);
        }

        private static EnvironmentStateLimits CreateUniformLimits(
            float minimum,
            float maximum)
        {
            var range = new NormalizedRange(minimum, maximum);
            return new EnvironmentStateLimits(range, range, range, range, range);
        }

        private static void AssertState(
            EnvironmentState actual,
            EnvironmentState expected)
        {
            Assert.That(actual.ApproximatelyEquals(expected, 0.00001f), Is.True);
        }
    }
}
