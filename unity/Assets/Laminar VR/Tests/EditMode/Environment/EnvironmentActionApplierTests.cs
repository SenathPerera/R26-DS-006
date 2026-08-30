using System;
using LaminarVR.AdaptiveMeditation.Environment;
using NUnit.Framework;

namespace LaminarVR.AdaptiveMeditation.Tests.EditMode.Environment
{
    public sealed class EnvironmentActionApplierTests
    {
        private static readonly EnvironmentActionStepConfiguration ActionSteps =
            new EnvironmentActionStepConfiguration(
                0.1f,
                0.25f,
                0.3f,
                0.2f,
                0.2f);

        [Test]
        public void ActionSpace_ContainsExpectedElevenActions()
        {
            Assert.That(Enum.GetValues(typeof(EnvironmentAction)), Has.Length.EqualTo(11));
        }

        [Test]
        public void NoChange_PreservesTheCurrentState()
        {
            var current = CreateNeutralState();

            var result = EnvironmentActionApplier.Apply(
                current,
                EnvironmentAction.NoChange,
                ActionSteps);

            Assert.That(result, Is.EqualTo(current));
        }

        [TestCase(EnvironmentAction.IncreaseIllumination, 0.6f, 0.5f, 0.5f, 0.5f, 0.5f)]
        [TestCase(EnvironmentAction.DecreaseIllumination, 0.4f, 0.5f, 0.5f, 0.5f, 0.5f)]
        [TestCase(EnvironmentAction.IncreaseWarmth, 0.5f, 0.75f, 0.5f, 0.5f, 0.5f)]
        [TestCase(EnvironmentAction.DecreaseWarmth, 0.5f, 0.25f, 0.5f, 0.5f, 0.5f)]
        [TestCase(EnvironmentAction.IncreaseAtmosphericSoftness, 0.5f, 0.5f, 0.8f, 0.5f, 0.5f)]
        [TestCase(EnvironmentAction.DecreaseAtmosphericSoftness, 0.5f, 0.5f, 0.2f, 0.5f, 0.5f)]
        [TestCase(EnvironmentAction.IncreaseColorRichness, 0.5f, 0.5f, 0.5f, 0.7f, 0.5f)]
        [TestCase(EnvironmentAction.DecreaseColorRichness, 0.5f, 0.5f, 0.5f, 0.3f, 0.5f)]
        [TestCase(EnvironmentAction.IncreaseAmbientMotion, 0.5f, 0.5f, 0.5f, 0.5f, 0.7f)]
        [TestCase(EnvironmentAction.DecreaseAmbientMotion, 0.5f, 0.5f, 0.5f, 0.5f, 0.3f)]
        public void Apply_ChangesOnlyTheActionDimension(
            EnvironmentAction action,
            float expectedIllumination,
            float expectedWarmth,
            float expectedAtmosphericSoftness,
            float expectedColorRichness,
            float expectedAmbientMotion)
        {
            var result = EnvironmentActionApplier.Apply(
                CreateNeutralState(),
                action,
                ActionSteps);

            var expected = new EnvironmentState(
                expectedIllumination,
                expectedWarmth,
                expectedAtmosphericSoftness,
                expectedColorRichness,
                expectedAmbientMotion);

            Assert.That(result.ApproximatelyEquals(expected, 0.00001f), Is.True);
        }

        [Test]
        public void Apply_ClampsTheChangedDimensionAtNormalizedBounds()
        {
            var nearBounds = new EnvironmentState(0.95f, 0.05f, 0.5f, 0.5f, 0.5f);

            var increased = EnvironmentActionApplier.Apply(
                nearBounds,
                EnvironmentAction.IncreaseIllumination,
                ActionSteps);
            var decreased = EnvironmentActionApplier.Apply(
                nearBounds,
                EnvironmentAction.DecreaseWarmth,
                ActionSteps);

            Assert.That(increased.Illumination, Is.EqualTo(1f));
            Assert.That(decreased.Warmth, Is.EqualTo(0f));
        }

        [Test]
        public void Apply_RejectsAnUnnormalizedCurrentState()
        {
            var unnormalized = new EnvironmentState(1.1f, 0.5f, 0.5f, 0.5f, 0.5f);

            Assert.Throws<ArgumentException>(
                () => EnvironmentActionApplier.Apply(
                    unnormalized,
                    EnvironmentAction.NoChange,
                    ActionSteps));
        }

        [Test]
        public void Apply_RejectsMissingActionSteps()
        {
            var current = CreateNeutralState();

            Assert.Throws<ArgumentNullException>(
                () => EnvironmentActionApplier.Apply(
                    current,
                    EnvironmentAction.NoChange,
                    null));
        }

        [Test]
        public void ActionStepConfiguration_RejectsInvalidDimensionSteps()
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new EnvironmentActionStepConfiguration(
                    0f, 0.1f, 0.1f, 0.1f, 0.1f));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new EnvironmentActionStepConfiguration(
                    0.1f, 0f, 0.1f, 0.1f, 0.1f));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new EnvironmentActionStepConfiguration(
                    0.1f, 0.1f, 0f, 0.1f, 0.1f));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new EnvironmentActionStepConfiguration(
                    0.1f, 0.1f, 0.1f, 0f, 0.1f));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new EnvironmentActionStepConfiguration(
                    0.1f, 0.1f, 0.1f, 0.1f, 0f));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new EnvironmentActionStepConfiguration(
                    float.NaN, 0.1f, 0.1f, 0.1f, 0.1f));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new EnvironmentActionStepConfiguration(
                    1.01f, 0.1f, 0.1f, 0.1f, 0.1f));
        }

        [Test]
        public void Apply_RejectsUnknownActions()
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => EnvironmentActionApplier.Apply(
                    CreateNeutralState(),
                    (EnvironmentAction)99,
                    ActionSteps));
        }

        private static EnvironmentState CreateNeutralState()
        {
            return new EnvironmentState(0.5f, 0.5f, 0.5f, 0.5f, 0.5f);
        }
    }
}
