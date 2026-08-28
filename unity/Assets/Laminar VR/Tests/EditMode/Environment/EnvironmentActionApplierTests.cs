using System;
using LaminarVR.AdaptiveMeditation.Environment;
using NUnit.Framework;

namespace LaminarVR.AdaptiveMeditation.Tests.EditMode.Environment
{
    public sealed class EnvironmentActionApplierTests
    {
        private const float ActionStep = 0.1f;

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
                ActionStep);

            Assert.That(result, Is.EqualTo(current));
        }

        [TestCase(EnvironmentAction.IncreaseIllumination, 0.6f, 0.5f, 0.5f, 0.5f, 0.5f)]
        [TestCase(EnvironmentAction.DecreaseIllumination, 0.4f, 0.5f, 0.5f, 0.5f, 0.5f)]
        [TestCase(EnvironmentAction.IncreaseWarmth, 0.5f, 0.6f, 0.5f, 0.5f, 0.5f)]
        [TestCase(EnvironmentAction.DecreaseWarmth, 0.5f, 0.4f, 0.5f, 0.5f, 0.5f)]
        [TestCase(EnvironmentAction.IncreaseAtmosphericSoftness, 0.5f, 0.5f, 0.6f, 0.5f, 0.5f)]
        [TestCase(EnvironmentAction.DecreaseAtmosphericSoftness, 0.5f, 0.5f, 0.4f, 0.5f, 0.5f)]
        [TestCase(EnvironmentAction.IncreaseColorRichness, 0.5f, 0.5f, 0.5f, 0.6f, 0.5f)]
        [TestCase(EnvironmentAction.DecreaseColorRichness, 0.5f, 0.5f, 0.5f, 0.4f, 0.5f)]
        [TestCase(EnvironmentAction.IncreaseAmbientMotion, 0.5f, 0.5f, 0.5f, 0.5f, 0.6f)]
        [TestCase(EnvironmentAction.DecreaseAmbientMotion, 0.5f, 0.5f, 0.5f, 0.5f, 0.4f)]
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
                ActionStep);

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
                ActionStep);
            var decreased = EnvironmentActionApplier.Apply(
                nearBounds,
                EnvironmentAction.DecreaseWarmth,
                ActionStep);

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
                    ActionStep));
        }

        [Test]
        public void Apply_RejectsInvalidActionSteps()
        {
            var current = CreateNeutralState();

            Assert.Throws<ArgumentOutOfRangeException>(
                () => EnvironmentActionApplier.Apply(current, EnvironmentAction.NoChange, 0f));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => EnvironmentActionApplier.Apply(current, EnvironmentAction.NoChange, 1.01f));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => EnvironmentActionApplier.Apply(current, EnvironmentAction.NoChange, float.NaN));
        }

        [Test]
        public void Apply_RejectsUnknownActions()
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => EnvironmentActionApplier.Apply(
                    CreateNeutralState(),
                    (EnvironmentAction)99,
                    ActionStep));
        }

        private static EnvironmentState CreateNeutralState()
        {
            return new EnvironmentState(0.5f, 0.5f, 0.5f, 0.5f, 0.5f);
        }
    }
}
