using System;
using LaminarVR.AdaptiveMeditation.Environment;
using LaminarVR.AdaptiveMeditation.Physiology;
using LaminarVR.AdaptiveMeditation.Policy;
using NUnit.Framework;

namespace LaminarVR.AdaptiveMeditation.Tests.EditMode.Policy
{
    public sealed class PolicyFeatureVectorBuilderTests
    {
        private readonly PolicyFeatureVectorBuilder builder =
            new PolicyFeatureVectorBuilder();

        [Test]
        public void Build_UsesVersionedDraftSchemaAndDeterministicFeatureOrder()
        {
            var preferred = new EnvironmentState(0.1f, 0.2f, 0.3f, 0.4f, 0.5f);
            var current = new EnvironmentState(0.6f, 0.5f, 0.4f, 0.3f, 0.2f);
            var safeDefault = new EnvironmentState(0.5f, 0.5f, 0.5f, 0.5f, 0.5f);
            var observation = new PolicyObservation(
                PolicyObservationTests.CreateSnapshot(),
                preferred,
                current,
                safeDefault);

            var vector = builder.Build(observation);

            Assert.That(
                vector.SchemaVersion,
                Is.EqualTo(PolicyFeatureVectorBuilder.DraftSchemaVersion));
            Assert.That(vector.Count, Is.EqualTo(24));
            Assert.That(vector[0], Is.EqualTo(1d));
            Assert.That(vector[1], Is.EqualTo(0.6d).Within(0.00001d));
            Assert.That(
                Slice(vector, 2, 5),
                Is.EqualTo(new[] { 0.1d, 0.2d, 0.6d, 0.1d }));
            Assert.That(vector[6], Is.EqualTo(0.9d));
            Assert.That(
                Slice(vector, 7, 11),
                Is.EqualTo(new[] { 0.1d, 0.2d, 0.3d, 0.4d, 0.5d })
                    .Within(0.00001d));
            Assert.That(
                Slice(vector, 12, 16),
                Is.EqualTo(new[] { 0.6d, 0.5d, 0.4d, 0.3d, 0.2d })
                    .Within(0.00001d));
            Assert.That(
                Slice(vector, 17, 21),
                Is.EqualTo(new[] { 0.5d, 0.3d, 0.1d, -0.1d, -0.3d })
                    .Within(0.00001d));
        }

        [Test]
        public void Build_NormalizesEnvironmentDistancesToUnitInterval()
        {
            var observation = new PolicyObservation(
                PolicyObservationTests.CreateSnapshot(),
                PolicyObservationTests.CreateState(0f),
                PolicyObservationTests.CreateState(1f),
                PolicyObservationTests.CreateState(0f));

            var vector = builder.Build(observation);

            Assert.That(vector[22], Is.EqualTo(1d).Within(0.00001d));
            Assert.That(vector[23], Is.EqualTo(1d).Within(0.00001d));
        }

        [Test]
        public void Build_DoesNotFabricateFeaturesForMissingOptionalHrv()
        {
            var environment = PolicyObservationTests.CreateState(0.5f);
            var observation = new PolicyObservation(
                PolicyObservationTests.CreateSnapshot(null, null),
                environment,
                environment,
                environment);

            var vector = builder.Build(observation);

            Assert.That(vector.Count, Is.EqualTo(builder.FeatureCount));
            Assert.That(vector.ToArray(), Has.All.Not.NaN);
        }

        [Test]
        public void GetFeatureName_ExposesStableNameForEveryDraftIndex()
        {
            var expectedNames = new[]
            {
                "bias",
                "continuous_stress_score_01",
                "stress_level_0_probability",
                "stress_level_1_probability",
                "stress_level_2_probability",
                "stress_level_3_probability",
                "signal_quality",
                "preferred_illumination",
                "preferred_warmth",
                "preferred_atmospheric_softness",
                "preferred_color_richness",
                "preferred_ambient_motion",
                "current_illumination",
                "current_warmth",
                "current_atmospheric_softness",
                "current_color_richness",
                "current_ambient_motion",
                "illumination_delta_from_preference",
                "warmth_delta_from_preference",
                "atmospheric_softness_delta_from_preference",
                "color_richness_delta_from_preference",
                "ambient_motion_delta_from_preference",
                "distance_from_preference_01",
                "distance_from_safe_default_01"
            };

            Assert.That(builder.FeatureCount, Is.EqualTo(expectedNames.Length));
            for (var index = 0; index < expectedNames.Length; index++)
            {
                Assert.That(builder.GetFeatureName(index), Is.EqualTo(expectedNames[index]));
            }
        }

        [Test]
        public void BuildAndGetFeatureName_RejectInvalidArguments()
        {
            Assert.Throws<ArgumentNullException>(() => builder.Build(null));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => builder.GetFeatureName(-1));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => builder.GetFeatureName(builder.FeatureCount));
        }

        private static double[] Slice(
            FeatureVector vector,
            int startIndex,
            int endIndexInclusive)
        {
            var values = new double[endIndexInclusive - startIndex + 1];
            for (var index = startIndex; index <= endIndexInclusive; index++)
            {
                values[index - startIndex] = vector[index];
            }

            return values;
        }
    }
}
