using System;
using LaminarVR.AdaptiveMeditation.Environment;
using LaminarVR.AdaptiveMeditation.Physiology;
using LaminarVR.AdaptiveMeditation.Policy;
using NUnit.Framework;

namespace LaminarVR.AdaptiveMeditation.Tests.EditMode.Policy
{
    public sealed class PolicyObservationTests
    {
        [Test]
        public void Constructor_AcceptsValidatedSnapshotWithMissingOptionalHrv()
        {
            var snapshot = CreateSnapshot(rmssdMs: null, sdnnMs: null);
            var environment = CreateState(0.5f);

            var observation = new PolicyObservation(
                snapshot,
                environment,
                environment,
                environment);

            Assert.That(observation.Physiology.SequenceNumber, Is.EqualTo(7L));
            Assert.That(observation.Physiology.Window.RmssdMs, Is.Null);
            Assert.That(observation.Physiology.Window.SdnnMs, Is.Null);
        }

        [Test]
        public void Constructor_RejectsMissingOrUnsequencedSnapshot()
        {
            var environment = CreateState(0.5f);

            Assert.Throws<ArgumentException>(
                () => new PolicyObservation(
                    default,
                    environment,
                    environment,
                    environment));
            Assert.Throws<ArgumentException>(
                () => new PolicyObservation(
                    new PhysiologyWindowSnapshot(0L, CreateWindow(), 0d),
                    environment,
                    environment,
                    environment));
        }

        [Test]
        public void Constructor_RejectsUnboundedPhysiologyPolicyInputs()
        {
            var environment = CreateState(0.5f);
            var invalidStress = CreateStress(
                probabilities: new StressProbabilityVector(1.1d, 0d, 0d, 0d));
            var invalidSnapshot = new PhysiologyWindowSnapshot(
                7L,
                CreateWindow(stress: invalidStress),
                0d);

            Assert.Throws<ArgumentException>(
                () => new PolicyObservation(
                    invalidSnapshot,
                    environment,
                    environment,
                    environment));
        }

        [Test]
        public void Constructor_RejectsNonNormalizedEnvironmentInputs()
        {
            var snapshot = CreateSnapshot();
            var normalized = CreateState(0.5f);
            var outsideDomain = CreateState(1.1f);

            Assert.Throws<ArgumentException>(
                () => new PolicyObservation(
                    snapshot,
                    outsideDomain,
                    normalized,
                    normalized));
            Assert.Throws<ArgumentException>(
                () => new PolicyObservation(
                    snapshot,
                    normalized,
                    outsideDomain,
                    normalized));
            Assert.Throws<ArgumentException>(
                () => new PolicyObservation(
                    snapshot,
                    normalized,
                    normalized,
                    outsideDomain));
        }

        internal static PhysiologyWindowSnapshot CreateSnapshot(
            double? rmssdMs = 34d,
            double? sdnnMs = 42d)
        {
            return new PhysiologyWindowSnapshot(
                7L,
                CreateWindow(rmssdMs, sdnnMs),
                0d);
        }

        internal static PhysiologyWindow CreateWindow(
            double? rmssdMs = 34d,
            double? sdnnMs = 42d,
            StressDecision stress = null)
        {
            return new PhysiologyWindow(
                1000d,
                940d,
                1000d,
                78d,
                rmssdMs,
                sdnnMs,
                stress ?? CreateStress(),
                0.9d);
        }

        internal static StressDecision CreateStress(
            StressProbabilityVector? probabilities = null)
        {
            return new StressDecision(
                StressDecisionMode.Point,
                2,
                null,
                null,
                "moderate",
                0.6d,
                false,
                probabilities
                    ?? new StressProbabilityVector(0.1d, 0.2d, 0.6d, 0.1d),
                1.8d);
        }

        internal static EnvironmentState CreateState(float value)
        {
            return new EnvironmentState(value, value, value, value, value);
        }
    }
}
