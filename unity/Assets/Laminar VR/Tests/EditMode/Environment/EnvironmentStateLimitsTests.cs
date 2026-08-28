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

        private static EnvironmentStateLimits CreateLimits()
        {
            return new EnvironmentStateLimits(
                new NormalizedRange(0.2f, 0.8f),
                new NormalizedRange(0.2f, 0.8f),
                new NormalizedRange(0.3f, 0.7f),
                new NormalizedRange(0.3f, 0.7f),
                new NormalizedRange(0.2f, 0.6f));
        }
    }
}
