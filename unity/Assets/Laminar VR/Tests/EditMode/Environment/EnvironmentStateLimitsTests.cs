using System;
using LaminarVR.AdaptiveMeditation.Environment;
using NUnit.Framework;

namespace LaminarVR.AdaptiveMeditation.Tests.EditMode.Environment
{
    public sealed class EnvironmentStateLimitsTests
    {
        [Test]
        public void NormalizedRange_RejectsInvalidBounds()
        {
            Assert.Throws<ArgumentException>(() => new NormalizedRange(-0.1f, 0.5f));
            Assert.Throws<ArgumentException>(() => new NormalizedRange(0.5f, 1.1f));
            Assert.Throws<ArgumentException>(() => new NormalizedRange(0.7f, 0.3f));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new NormalizedRange(float.NaN, 0.5f));
        }

        [Test]
        public void NormalizedRange_ContainsAndClampsValuesInclusively()
        {
            var range = new NormalizedRange(0.25f, 0.75f);

            Assert.That(range.Contains(0.25f), Is.True);
            Assert.That(range.Contains(0.75f), Is.True);
            Assert.That(range.Contains(0.1f), Is.False);
            Assert.That(range.Contains(float.NaN), Is.False);
            Assert.That(range.Clamp(0.1f), Is.EqualTo(0.25f));
            Assert.That(range.Clamp(0.9f), Is.EqualTo(0.75f));
            Assert.Throws<ArgumentOutOfRangeException>(() => range.Clamp(float.NaN));
        }

        [Test]
        public void NormalizedRange_TryIntersect_ReturnsSharedInclusiveRange()
        {
            var left = new NormalizedRange(0.2f, 0.7f);
            var right = new NormalizedRange(0.5f, 0.9f);

            var intersects = left.TryIntersect(right, out var intersection);

            Assert.That(intersects, Is.True);
            Assert.That(intersection, Is.EqualTo(new NormalizedRange(0.5f, 0.7f)));
        }

        [Test]
        public void NormalizedRange_TryIntersect_AcceptsTouchingBoundary()
        {
            var left = new NormalizedRange(0.2f, 0.5f);
            var right = new NormalizedRange(0.5f, 0.9f);

            var intersects = left.TryIntersect(right, out var intersection);

            Assert.That(intersects, Is.True);
            Assert.That(intersection, Is.EqualTo(new NormalizedRange(0.5f, 0.5f)));
        }

        [Test]
        public void NormalizedRange_TryIntersect_RejectsDisjointRanges()
        {
            var left = new NormalizedRange(0.2f, 0.4f);
            var right = new NormalizedRange(0.5f, 0.9f);

            var intersects = left.TryIntersect(right, out _);

            Assert.That(intersects, Is.False);
        }

        [Test]
        public void StateLimits_ClampEachDimensionToItsOwnRange()
        {
            var limits = CreateLimits();
            var state = new EnvironmentState(0.1f, 0.9f, 0.2f, 0.8f, 0.1f);

            var clamped = limits.Clamp(state);

            Assert.That(
                clamped,
                Is.EqualTo(new EnvironmentState(0.2f, 0.8f, 0.3f, 0.7f, 0.2f)));
            Assert.That(limits.Contains(clamped), Is.True);
        }

        [Test]
        public void StateLimits_RejectStatesOutsideAnyDimensionRange()
        {
            var limits = CreateLimits();
            var inside = new EnvironmentState(0.5f, 0.5f, 0.5f, 0.5f, 0.5f);
            var outside = new EnvironmentState(0.1f, 0.5f, 0.5f, 0.5f, 0.5f);

            Assert.That(limits.Contains(inside), Is.True);
            Assert.That(limits.Contains(outside), Is.False);
        }

        [Test]
        public void StateLimits_TryIntersect_IntersectsEveryDimension()
        {
            var left = CreateLimits();
            var right = CreateUniformLimits(0.4f, 0.9f);

            var intersects = left.TryIntersect(right, out var intersection);

            Assert.That(intersects, Is.True);
            Assert.That(
                intersection.Illumination,
                Is.EqualTo(new NormalizedRange(0.4f, 0.8f)));
            Assert.That(
                intersection.AtmosphericSoftness,
                Is.EqualTo(new NormalizedRange(0.4f, 0.7f)));
            Assert.That(
                intersection.AmbientMotion,
                Is.EqualTo(new NormalizedRange(0.4f, 0.6f)));
        }

        [Test]
        public void StateLimits_TryIntersect_RejectsAnyDisjointDimension()
        {
            var left = CreateLimits();
            var right = new EnvironmentStateLimits(
                new NormalizedRange(0.81f, 0.9f),
                new NormalizedRange(0.4f, 0.6f),
                new NormalizedRange(0.4f, 0.6f),
                new NormalizedRange(0.4f, 0.6f),
                new NormalizedRange(0.4f, 0.6f));

            var intersects = left.TryIntersect(right, out _);

            Assert.That(intersects, Is.False);
        }

        private static EnvironmentStateLimits CreateLimits()
        {
            return new EnvironmentStateLimits(
                new NormalizedRange(0.2f, 0.8f),
                new NormalizedRange(0.2f, 0.8f),
                new NormalizedRange(0.3f, 0.7f),
                new NormalizedRange(0.3f, 0.7f),
                new NormalizedRange(0.2f, 0.6f));
        }

        private static EnvironmentStateLimits CreateUniformLimits(
            float minimum,
            float maximum)
        {
            var range = new NormalizedRange(minimum, maximum);
            return new EnvironmentStateLimits(range, range, range, range, range);
        }
    }
}
