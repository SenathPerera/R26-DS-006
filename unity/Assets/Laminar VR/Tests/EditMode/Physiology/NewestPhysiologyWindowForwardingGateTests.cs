using System;
using LaminarVR.AdaptiveMeditation.Physiology;
using NUnit.Framework;

namespace LaminarVR.AdaptiveMeditation.Tests.EditMode.Physiology
{
    public sealed class NewestPhysiologyWindowForwardingGateTests
    {
        [Test]
        public void Observe_ForwardsFirstWindowImmediately()
        {
            var gate = new NewestPhysiologyWindowForwardingGate(60d);
            var window = CreateWindow(1000d);

            var result = gate.Observe(window, 10d, out var forwarded);

            Assert.That(result, Is.EqualTo(PhysiologyWindowForwardingResult.Forwarded));
            Assert.That(forwarded, Is.SameAs(window));
        }

        [Test]
        public void Observe_BuffersAndFlushesNewestWindowAtInterval()
        {
            var gate = new NewestPhysiologyWindowForwardingGate(60d);
            gate.Observe(CreateWindow(1000d), 10d, out _);
            var olderPending = CreateWindow(1004d);
            var newestPending = CreateWindow(1008d);

            var firstResult = gate.Observe(olderPending, 20d, out var first);
            var secondResult = gate.Observe(newestPending, 30d, out var second);
            var flushed = gate.TryFlush(70d, out var forwarded);

            Assert.That(firstResult, Is.EqualTo(PhysiologyWindowForwardingResult.Buffered));
            Assert.That(secondResult, Is.EqualTo(PhysiologyWindowForwardingResult.Buffered));
            Assert.That(first, Is.Null);
            Assert.That(second, Is.Null);
            Assert.That(flushed, Is.True);
            Assert.That(forwarded, Is.SameAs(newestPending));
        }

        [Test]
        public void Observe_RejectsDuplicateAndOutOfOrderWindowEnds()
        {
            var gate = new NewestPhysiologyWindowForwardingGate(60d);
            gate.Observe(CreateWindow(1000d), 10d, out _);

            var duplicateResult = gate.Observe(
                CreateWindow(1000d),
                20d,
                out var duplicate);
            var olderResult = gate.Observe(
                CreateWindow(999d),
                30d,
                out var older);

            Assert.That(
                duplicateResult,
                Is.EqualTo(
                    PhysiologyWindowForwardingResult.DuplicateOrOutOfOrder));
            Assert.That(
                olderResult,
                Is.EqualTo(
                    PhysiologyWindowForwardingResult.DuplicateOrOutOfOrder));
            Assert.That(duplicate, Is.Null);
            Assert.That(older, Is.Null);
        }

        [Test]
        public void Reset_StartsANewSessionWithoutCarryingWindowIdentity()
        {
            var gate = new NewestPhysiologyWindowForwardingGate(60d);
            gate.Observe(CreateWindow(1000d), 10d, out _);

            gate.Reset();
            var newSessionWindow = CreateWindow(500d);
            var result = gate.Observe(
                newSessionWindow,
                1d,
                out var forwarded);

            Assert.That(result, Is.EqualTo(PhysiologyWindowForwardingResult.Forwarded));
            Assert.That(forwarded, Is.SameAs(newSessionWindow));
        }

        [Test]
        public void Observe_RejectsInvalidWindowEndAndBackwardTime()
        {
            var gate = new NewestPhysiologyWindowForwardingGate(60d);

            var invalidResult = gate.Observe(
                CreateWindow(double.NaN),
                10d,
                out var forwarded);

            Assert.That(
                invalidResult,
                Is.EqualTo(PhysiologyWindowForwardingResult.WindowEndInvalid));
            Assert.That(forwarded, Is.Null);
            Assert.Throws<ArgumentOutOfRangeException>(
                () => gate.TryFlush(9d, out _));
        }

        private static PhysiologyWindow CreateWindow(double windowEnd)
        {
            return new PhysiologyWindow(
                windowEnd,
                windowEnd - 60d,
                windowEnd,
                72d,
                30d,
                40d,
                new StressDecision(
                    StressDecisionMode.Point,
                    1,
                    null,
                    null,
                    "mild",
                    0.5d,
                    false,
                    new StressProbabilityVector(0.1d, 0.7d, 0.15d, 0.05d),
                    1.15d),
                0.9d);
        }
    }
}
