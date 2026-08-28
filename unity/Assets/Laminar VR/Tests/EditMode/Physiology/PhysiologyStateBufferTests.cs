using LaminarVR.AdaptiveMeditation.Physiology;
using NUnit.Framework;

namespace LaminarVR.AdaptiveMeditation.Tests.EditMode.Physiology
{
    public sealed class PhysiologyStateBufferTests
    {
        [Test]
        public void Ingest_AcceptsValidWindowAndAssignsSequence()
        {
            var buffer = CreateBuffer();

            var result = buffer.Ingest(CreateWindow(1000d), 1000d, 5d);

            Assert.That(result.Accepted, Is.True);
            Assert.That(result.AcceptedSequenceNumber, Is.EqualTo(1L));
            Assert.That(buffer.Count, Is.EqualTo(1));
            Assert.That(buffer.LatestAcceptedSequenceNumber, Is.EqualTo(1L));
            Assert.That(buffer.TryGetLatestAccepted(out var latest), Is.True);
            Assert.That(latest.Window.WindowEndUtcUnixSeconds, Is.EqualTo(1000d));
        }

        [Test]
        public void Ingest_RejectsInvalidPayloadWithoutReplacingLatestWindow()
        {
            var buffer = CreateBuffer();
            buffer.Ingest(CreateWindow(1000d), 1000d, 5d);
            var invalid = CreateWindow(1001d, heartRateBpm: 0d);

            var result = buffer.Ingest(invalid, 1001d, 6d);

            Assert.That(
                result.ResultCode,
                Is.EqualTo(PhysiologyIngestionResultCode.PayloadRejected));
            Assert.That(
                result.ValidationReasonCode,
                Is.EqualTo(PhysiologyValidationReasonCode.HeartRateInvalid));
            Assert.That(buffer.LatestAcceptedSequenceNumber, Is.EqualTo(1L));
            Assert.That(buffer.TryGetLatestAccepted(out var latest), Is.True);
            Assert.That(latest.Window.WindowEndUtcUnixSeconds, Is.EqualTo(1000d));
        }

        [Test]
        public void Ingest_RejectsDuplicateAndOutOfOrderWindows()
        {
            var buffer = CreateBuffer();
            buffer.Ingest(CreateWindow(1000d), 1000d, 5d);

            var duplicate = buffer.Ingest(CreateWindow(1000d), 1000d, 6d);
            var outOfOrder = buffer.Ingest(CreateWindow(999d), 1000d, 7d);

            Assert.That(
                duplicate.ResultCode,
                Is.EqualTo(PhysiologyIngestionResultCode.DuplicateWindow));
            Assert.That(
                outOfOrder.ResultCode,
                Is.EqualTo(PhysiologyIngestionResultCode.OutOfOrderWindow));
            Assert.That(buffer.Count, Is.EqualTo(1));
            Assert.That(buffer.LatestAcceptedSequenceNumber, Is.EqualTo(1L));
        }

        [Test]
        public void Ingest_RejectsReceiptClockMovingBackwards()
        {
            var buffer = CreateBuffer();
            buffer.Ingest(CreateWindow(1000d), 1000d, 5d);

            var result = buffer.Ingest(CreateWindow(1001d), 1001d, 4d);

            Assert.That(
                result.ResultCode,
                Is.EqualTo(PhysiologyIngestionResultCode.NonMonotonicReceiptTime));
            Assert.That(buffer.LatestAcceptedSequenceNumber, Is.EqualTo(1L));
        }

        [Test]
        public void Query_UsesMonotonicAgeToRejectStaleWindow()
        {
            var buffer = CreateBuffer();
            buffer.Ingest(CreateWindow(1000d), 1000d, 5d);

            var fresh = buffer.TryGetLatestUsable(
                PhysiologyDataUse.Decision,
                14d,
                0L,
                out var freshSnapshot,
                out var freshCode);
            var stale = buffer.TryGetLatestUsable(
                PhysiologyDataUse.Decision,
                16d,
                0L,
                out _,
                out var staleCode);

            Assert.That(fresh, Is.True);
            Assert.That(freshCode, Is.EqualTo(PhysiologyQueryResultCode.Available));
            Assert.That(freshSnapshot.AgeSeconds, Is.EqualTo(9d));
            Assert.That(stale, Is.False);
            Assert.That(staleCode, Is.EqualTo(PhysiologyQueryResultCode.Stale));
        }

        [Test]
        public void Query_AppliesIndependentDecisionAndRewardQualityThresholds()
        {
            var buffer = CreateBuffer();
            buffer.Ingest(CreateWindow(1000d, signalQuality: 0.85d), 1000d, 5d);

            var decisionAvailable = buffer.TryGetLatestUsable(
                PhysiologyDataUse.Decision,
                5d,
                0L,
                out _,
                out var decisionCode);
            var rewardAvailable = buffer.TryGetLatestUsable(
                PhysiologyDataUse.Reward,
                5d,
                0L,
                out _,
                out var rewardCode);

            Assert.That(decisionAvailable, Is.True);
            Assert.That(decisionCode, Is.EqualTo(PhysiologyQueryResultCode.Available));
            Assert.That(rewardAvailable, Is.False);
            Assert.That(
                rewardCode,
                Is.EqualTo(PhysiologyQueryResultCode.InsufficientSignalQuality));
        }

        [Test]
        public void Query_KeepsLowQualityWindowForDisplayButBlocksDecision()
        {
            var buffer = CreateBuffer();
            buffer.Ingest(CreateWindow(1000d, signalQuality: 0.4d), 1000d, 5d);

            var displayAvailable = buffer.TryGetLatestUsable(
                PhysiologyDataUse.Display,
                5d,
                0L,
                out _,
                out var displayCode);
            var decisionAvailable = buffer.TryGetLatestUsable(
                PhysiologyDataUse.Decision,
                5d,
                0L,
                out _,
                out var decisionCode);

            Assert.That(displayAvailable, Is.True);
            Assert.That(displayCode, Is.EqualTo(PhysiologyQueryResultCode.Available));
            Assert.That(decisionAvailable, Is.False);
            Assert.That(
                decisionCode,
                Is.EqualTo(PhysiologyQueryResultCode.InsufficientSignalQuality));
        }

        [Test]
        public void ResumeGate_RequiresNewFreshDecisionQualityWindow()
        {
            var buffer = CreateBuffer();
            buffer.Ingest(CreateWindow(1000d), 1000d, 5d);
            var sequenceAtPause = buffer.LatestAcceptedSequenceNumber;

            Assert.That(buffer.HasFreshDecisionWindowAfter(sequenceAtPause, 6d), Is.False);

            buffer.Ingest(
                CreateWindow(1001d, signalQuality: 0.5d),
                1001d,
                6d);
            Assert.That(buffer.HasFreshDecisionWindowAfter(sequenceAtPause, 6d), Is.False);

            buffer.Ingest(CreateWindow(1002d), 1002d, 7d);
            Assert.That(buffer.HasFreshDecisionWindowAfter(sequenceAtPause, 7d), Is.True);
        }

        [Test]
        public void Query_AfterSequencePreventsReusingSameWindow()
        {
            var buffer = CreateBuffer();
            buffer.Ingest(CreateWindow(1000d), 1000d, 5d);

            var available = buffer.TryGetLatestUsable(
                PhysiologyDataUse.Decision,
                5d,
                1L,
                out _,
                out var resultCode);

            Assert.That(available, Is.False);
            Assert.That(resultCode, Is.EqualTo(PhysiologyQueryResultCode.NoNewWindow));
        }

        [Test]
        public void CircularBuffer_RetainsConfiguredNumberOfRecentWindows()
        {
            var buffer = CreateBuffer(capacity: 2);
            buffer.Ingest(CreateWindow(1000d), 1000d, 1d);
            buffer.Ingest(CreateWindow(1001d), 1001d, 2d);
            buffer.Ingest(CreateWindow(1002d), 1002d, 3d);

            Assert.That(buffer.Count, Is.EqualTo(2));
            Assert.That(buffer.Capacity, Is.EqualTo(2));
            Assert.That(buffer.TryGetAcceptedBySequence(1L, out _), Is.False);
            Assert.That(buffer.TryGetAcceptedBySequence(2L, out _), Is.True);
            Assert.That(buffer.TryGetAcceptedBySequence(3L, out _), Is.True);
        }

        [Test]
        public void Query_RejectsTimeBeforeReceipt()
        {
            var buffer = CreateBuffer();
            buffer.Ingest(CreateWindow(1000d), 1000d, 5d);

            var available = buffer.TryGetLatestUsable(
                PhysiologyDataUse.Decision,
                4d,
                0L,
                out _,
                out var resultCode);

            Assert.That(available, Is.False);
            Assert.That(
                resultCode,
                Is.EqualTo(PhysiologyQueryResultCode.InvalidQueryTime));
        }

        private static PhysiologyStateBuffer CreateBuffer(int capacity = 4)
        {
            return new PhysiologyStateBuffer(
                new PhysiologyValidationConfiguration(
                    "buffer-test",
                    1,
                    10d,
                    1d,
                    1d,
                    0d,
                    0.01d,
                    0.8d,
                    0.9d,
                    capacity));
        }

        private static PhysiologyWindow CreateWindow(
            double windowEndUtcUnixSeconds,
            double heartRateBpm = 78d,
            double signalQuality = 0.95d)
        {
            return new PhysiologyWindow(
                windowEndUtcUnixSeconds,
                windowEndUtcUnixSeconds - 60d,
                windowEndUtcUnixSeconds,
                heartRateBpm,
                null,
                null,
                new StressDecision(
                    StressDecisionMode.Point,
                    2,
                    null,
                    null,
                    "moderate",
                    0.5d,
                    false,
                    new StressProbabilityVector(0.1d, 0.2d, 0.6d, 0.1d),
                    1.7d),
                signalQuality);
        }
    }
}
