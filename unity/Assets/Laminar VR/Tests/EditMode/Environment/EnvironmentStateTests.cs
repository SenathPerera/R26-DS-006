using System;
using LaminarVR.AdaptiveMeditation.Environment;
using NUnit.Framework;

namespace LaminarVR.AdaptiveMeditation.Tests.EditMode.Environment
{
    public sealed class EnvironmentStateTests
    {
        [Test]
        public void Constructor_PreservesAllDimensions()
        {
            var state = new EnvironmentState(0.1f, 0.2f, 0.3f, 0.4f, 0.5f);

            Assert.That(state.Illumination, Is.EqualTo(0.1f));
            Assert.That(state.Warmth, Is.EqualTo(0.2f));
            Assert.That(state.AtmosphericSoftness, Is.EqualTo(0.3f));
            Assert.That(state.ColorRichness, Is.EqualTo(0.4f));
            Assert.That(state.AmbientMotion, Is.EqualTo(0.5f));
        }

        [Test]
        public void Constructor_RejectsNonFiniteDimensions()
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new EnvironmentState(float.NaN, 0f, 0f, 0f, 0f));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new EnvironmentState(0f, float.PositiveInfinity, 0f, 0f, 0f));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new EnvironmentState(0f, 0f, float.NegativeInfinity, 0f, 0f));
        }

        [Test]
        public void Clamp01_ClampsEveryDimensionToNormalizedBounds()
        {
            var state = new EnvironmentState(-0.2f, 1.2f, 0.3f, 2f, -1f);

            var clamped = state.Clamp01();

            Assert.That(
                clamped,
                Is.EqualTo(new EnvironmentState(0f, 1f, 0.3f, 1f, 0f)));
            Assert.That(clamped.IsNormalized, Is.True);
        }

        [Test]
        public void IsNormalized_ReportsWhetherEveryDimensionIsWithinBounds()
        {
            Assert.That(new EnvironmentState(0f, 0.25f, 0.5f, 0.75f, 1f).IsNormalized, Is.True);
            Assert.That(new EnvironmentState(-0.01f, 0.25f, 0.5f, 0.75f, 1f).IsNormalized, Is.False);
            Assert.That(new EnvironmentState(0f, 0.25f, 0.5f, 0.75f, 1.01f).IsNormalized, Is.False);
        }

        [Test]
        public void DistanceMethods_ReturnExpectedFiveDimensionalDistances()
        {
            var minimum = new EnvironmentState(0f, 0f, 0f, 0f, 0f);
            var maximum = new EnvironmentState(1f, 1f, 1f, 1f, 1f);

            Assert.That(minimum.L1DistanceTo(maximum), Is.EqualTo(5d));
            Assert.That(
                minimum.EuclideanDistanceTo(maximum),
                Is.EqualTo(Math.Sqrt(5d)).Within(1e-12d));
        }

        [Test]
        public void ApproximatelyEquals_UsesExplicitTolerance()
        {
            var state = new EnvironmentState(0.5f, 0.5f, 0.5f, 0.5f, 0.5f);
            var close = new EnvironmentState(0.5005f, 0.5f, 0.5f, 0.5f, 0.5f);

            Assert.That(state.ApproximatelyEquals(close, 0.001f), Is.True);
            Assert.That(state.ApproximatelyEquals(close, 0.0001f), Is.False);
            Assert.Throws<ArgumentOutOfRangeException>(
                () => state.ApproximatelyEquals(close, -0.1f));
        }
    }
}
