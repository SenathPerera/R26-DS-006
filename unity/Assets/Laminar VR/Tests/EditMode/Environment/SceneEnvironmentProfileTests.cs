using System;
using LaminarVR.AdaptiveMeditation.Environment;
using NUnit.Framework;

namespace LaminarVR.AdaptiveMeditation.Tests.EditMode.Environment
{
    public sealed class SceneEnvironmentProfileTests
    {
        [Test]
        public void Constructor_PreservesValidatedConfiguration()
        {
            var safeDefault = new EnvironmentState(0.5f, 0.5f, 0.5f, 0.5f, 0.5f);
            var limits = CreateLimits();

            var profile = new SceneEnvironmentProfile(
                "temple-pond",
                "Temple Pond",
                safeDefault,
                limits,
                0.1f,
                2f,
                5f);

            Assert.That(profile.SceneId, Is.EqualTo("temple-pond"));
            Assert.That(profile.DisplayName, Is.EqualTo("Temple Pond"));
            Assert.That(profile.SafeDefault, Is.EqualTo(safeDefault));
            Assert.That(profile.ActionStep, Is.EqualTo(0.1f));
            Assert.That(profile.TransitionDurationSeconds, Is.EqualTo(2f));
            Assert.That(profile.MinimumSecondsBetweenActions, Is.EqualTo(5f));
        }

        [Test]
        public void Constructor_RejectsSafeDefaultOutsideSceneLimits()
        {
            var outsideLimits = new EnvironmentState(0.1f, 0.5f, 0.5f, 0.5f, 0.5f);

            Assert.Throws<ArgumentException>(
                () => new SceneEnvironmentProfile(
                    "temple-pond",
                    "Temple Pond",
                    outsideLimits,
                    CreateLimits(),
                    0.1f,
                    2f,
                    5f));
        }

        [Test]
        public void Constructor_RejectsMissingIdentityOrInvalidTiming()
        {
            var safeDefault = new EnvironmentState(0.5f, 0.5f, 0.5f, 0.5f, 0.5f);
            var limits = CreateLimits();

            Assert.Throws<ArgumentException>(
                () => new SceneEnvironmentProfile(
                    string.Empty,
                    "Temple Pond",
                    safeDefault,
                    limits,
                    0.1f,
                    2f,
                    5f));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new SceneEnvironmentProfile(
                    "temple-pond",
                    "Temple Pond",
                    safeDefault,
                    limits,
                    0f,
                    2f,
                    5f));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new SceneEnvironmentProfile(
                    "temple-pond",
                    "Temple Pond",
                    safeDefault,
                    limits,
                    0.1f,
                    0f,
                    5f));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new SceneEnvironmentProfile(
                    "temple-pond",
                    "Temple Pond",
                    safeDefault,
                    limits,
                    0.1f,
                    2f,
                    -1f));
        }

        private static EnvironmentStateLimits CreateLimits()
        {
            var range = new NormalizedRange(0.2f, 0.8f);
            return new EnvironmentStateLimits(range, range, range, range, range);
        }
    }
}
