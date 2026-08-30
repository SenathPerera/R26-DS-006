using LaminarVR.AdaptiveMeditation.Environment;
using LaminarVR.AdaptiveMeditation.Physiology;
using LaminarVR.AdaptiveMeditation.Rewards;
using LaminarVR.AdaptiveMeditation.Session;
using NUnit.Framework;

namespace LaminarVR.AdaptiveMeditation.Tests.EditMode.Rewards
{
    public sealed class RewardAttributionTrackerTests
    {
        [Test]
        public void TryResolve_MatchesFirstCleanWindowAfterSettling()
        {
            var buffer = CreateBuffer();
            var preAction = IngestAndGetLatest(
                buffer,
                windowEndUtcUnixSeconds: 1000d,
                receivedMonotonicTimeSeconds: 10d);
            var tracker = CreateOpenTracker(preAction);

            var overlapping = buffer.Ingest(
                RewardCalculatorTests.CreateWindow(1060d),
                receivedTimestampUtcUnixSeconds: 1060d,
                receivedMonotonicTimeSeconds: 26d);
            Assert.That(overlapping.Accepted, Is.True);

            var matched = tracker.TryResolve(
                buffer,
                26d,
                VrSessionPhase.Adaptive,
                true,
                out _,
                out var overlappingCode);

            Assert.That(matched, Is.False);
            Assert.That(
                overlappingCode,
                Is.EqualTo(
                    RewardAttributionResolutionCode
                        .WindowOverlapsTransitionOrSettling));
            Assert.That(tracker.HasPending, Is.True);

            var clean = buffer.Ingest(
                RewardCalculatorTests.CreateWindow(1076d),
                receivedTimestampUtcUnixSeconds: 1076d,
                receivedMonotonicTimeSeconds: 27d);
            Assert.That(clean.Accepted, Is.True);

            matched = tracker.TryResolve(
                buffer,
                27d,
                VrSessionPhase.Adaptive,
                true,
                out var match,
                out var resultCode);

            Assert.That(matched, Is.True);
            Assert.That(resultCode, Is.EqualTo(RewardAttributionResolutionCode.Matched));
            Assert.That(match.Request.TransitionId, Is.EqualTo("transition-1"));
            Assert.That(match.PostActionPhysiology.SequenceNumber, Is.EqualTo(3L));
            Assert.That(tracker.LatestConsumedPostWindowSequenceNumber, Is.EqualTo(3L));
            Assert.That(tracker.HasPending, Is.False);
        }

        [Test]
        public void TryResolve_WaitsForSettlingBeforeReadingBuffer()
        {
            var buffer = CreateBuffer();
            var preAction = IngestAndGetLatest(buffer, 1000d, 10d);
            var tracker = CreateOpenTracker(preAction);

            var resolved = tracker.TryResolve(
                buffer,
                24.99d,
                VrSessionPhase.Adaptive,
                true,
                out _,
                out var resultCode);

            Assert.That(resolved, Is.False);
            Assert.That(
                resultCode,
                Is.EqualTo(RewardAttributionResolutionCode.WaitingForSettling));
            Assert.That(tracker.HasPending, Is.True);
        }

        [Test]
        public void TryOpen_RequiresAdaptivePhaseAndConnectivity()
        {
            var buffer = CreateBuffer();
            var preAction = IngestAndGetLatest(buffer, 1000d, 10d);
            var request = CreateRequest(preAction);
            var tracker = new RewardAttributionTracker(
                RewardPipelineConfigurationTests.CreateConfiguration());

            Assert.That(
                tracker.TryOpen(
                    request,
                    VrSessionPhase.Paused,
                    true,
                    out var phaseCode),
                Is.False);
            Assert.That(
                phaseCode,
                Is.EqualTo(RewardAttributionOpenResultCode.InvalidPhase));
            Assert.That(
                tracker.TryOpen(
                    request,
                    VrSessionPhase.Adaptive,
                    false,
                    out var networkCode),
                Is.False);
            Assert.That(
                networkCode,
                Is.EqualTo(RewardAttributionOpenResultCode.NetworkUnavailable));
        }

        [Test]
        public void TryOpen_RejectsASecondPendingAttribution()
        {
            var buffer = CreateBuffer();
            var preAction = IngestAndGetLatest(buffer, 1000d, 10d);
            var tracker = CreateOpenTracker(preAction);

            var opened = tracker.TryOpen(
                CreateRequest(preAction),
                VrSessionPhase.Adaptive,
                true,
                out var resultCode);

            Assert.That(opened, Is.False);
            Assert.That(
                resultCode,
                Is.EqualTo(RewardAttributionOpenResultCode.AlreadyPending));
            Assert.That(tracker.PendingTransitionId, Is.EqualTo("transition-1"));
        }

        [Test]
        public void TryResolve_RejectsPostWindowBelowRewardSignalThreshold()
        {
            var buffer = CreateBuffer();
            var tracker = CreateOpenTracker(
                IngestAndGetLatest(buffer, 1000d, 10d));
            var ingestion = buffer.Ingest(
                RewardCalculatorTests.CreateWindow(
                    1076d,
                    signalQuality: 0.5d),
                1076d,
                27d);
            Assert.That(ingestion.Accepted, Is.True);

            var resolved = tracker.TryResolve(
                buffer,
                27d,
                VrSessionPhase.Adaptive,
                true,
                out _,
                out var resultCode);

            Assert.That(resolved, Is.False);
            Assert.That(
                resultCode,
                Is.EqualTo(
                    RewardAttributionResolutionCode.WindowNotRewardUsable));
            Assert.That(tracker.HasPending, Is.True);
        }

        [Test]
        public void TryResolve_InvalidatesPendingAttributionWhenSessionPauses()
        {
            var buffer = CreateBuffer();
            var tracker = CreateOpenTracker(
                IngestAndGetLatest(buffer, 1000d, 10d));

            var resolved = tracker.TryResolve(
                buffer,
                21d,
                VrSessionPhase.Paused,
                true,
                out _,
                out var resultCode);

            Assert.That(resolved, Is.False);
            Assert.That(
                resultCode,
                Is.EqualTo(RewardAttributionResolutionCode.InvalidatedForPhase));
            Assert.That(tracker.HasPending, Is.False);
            Assert.That(
                tracker.LastInvalidation.Value.Reason,
                Is.EqualTo(
                    RewardAttributionInvalidationReason.InvalidSessionPhase));
        }

        [Test]
        public void TryResolve_InvalidatesPendingAttributionOnNetworkLoss()
        {
            var buffer = CreateBuffer();
            var tracker = CreateOpenTracker(
                IngestAndGetLatest(buffer, 1000d, 10d));

            var resolved = tracker.TryResolve(
                buffer,
                21d,
                VrSessionPhase.Adaptive,
                false,
                out _,
                out var resultCode);

            Assert.That(resolved, Is.False);
            Assert.That(
                resultCode,
                Is.EqualTo(RewardAttributionResolutionCode.InvalidatedForNetwork));
            Assert.That(
                tracker.LastInvalidation.Value.Reason,
                Is.EqualTo(RewardAttributionInvalidationReason.NetworkLoss));
        }

        [Test]
        public void TryResolve_TimesOutWithoutAValidPostActionWindow()
        {
            var buffer = CreateBuffer();
            var tracker = CreateOpenTracker(
                IngestAndGetLatest(buffer, 1000d, 10d));

            var resolved = tracker.TryResolve(
                buffer,
                140.01d,
                VrSessionPhase.Adaptive,
                true,
                out _,
                out var resultCode);

            Assert.That(resolved, Is.False);
            Assert.That(resultCode, Is.EqualTo(RewardAttributionResolutionCode.TimedOut));
            Assert.That(
                tracker.LastInvalidation.Value.Reason,
                Is.EqualTo(RewardAttributionInvalidationReason.Timeout));
        }

        [Test]
        public void TryInvalidate_RecordsExplicitEmergencyBoundary()
        {
            var buffer = CreateBuffer();
            var tracker = CreateOpenTracker(
                IngestAndGetLatest(buffer, 1000d, 10d));

            var invalidated = tracker.TryInvalidate(
                RewardAttributionInvalidationReason.EmergencyStop,
                22d,
                out var invalidation);

            Assert.That(invalidated, Is.True);
            Assert.That(invalidation.TransitionId, Is.EqualTo("transition-1"));
            Assert.That(
                invalidation.Reason,
                Is.EqualTo(RewardAttributionInvalidationReason.EmergencyStop));
            Assert.That(invalidation.MonotonicTimeSeconds, Is.EqualTo(22d));
            Assert.That(tracker.HasPending, Is.False);
        }

        [Test]
        public void MatchedPostWindow_CanBecomeNextActionsPreWindowButCannotBeReusedAsPost()
        {
            var buffer = CreateBuffer();
            var tracker = CreateOpenTracker(
                IngestAndGetLatest(buffer, 1000d, 10d));
            var clean = buffer.Ingest(
                RewardCalculatorTests.CreateWindow(1076d),
                1076d,
                27d);
            Assert.That(clean.Accepted, Is.True);
            Assert.That(
                tracker.TryResolve(
                    buffer,
                    27d,
                    VrSessionPhase.Adaptive,
                    true,
                    out var firstMatch,
                    out _),
                Is.True);

            var nextRequest = new RewardAttributionRequest(
                "transition-2",
                firstMatch.PostActionPhysiology,
                EnvironmentAction.NoChange,
                RewardCalculatorTests.CreateEnvironment(),
                RewardCalculatorTests.CreateEnvironment(),
                30d,
                1080d);

            Assert.That(
                tracker.TryOpen(
                    nextRequest,
                    VrSessionPhase.Adaptive,
                    true,
                    out var openCode),
                Is.True);
            Assert.That(openCode, Is.EqualTo(RewardAttributionOpenResultCode.Opened));
            Assert.That(
                tracker.TryResolve(
                    buffer,
                    35d,
                    VrSessionPhase.Adaptive,
                    true,
                    out _,
                    out var resolutionCode),
                Is.False);
            Assert.That(
                resolutionCode,
                Is.EqualTo(RewardAttributionResolutionCode.WaitingForWindow));
        }

        private static RewardAttributionTracker CreateOpenTracker(
            PhysiologyWindowSnapshot preAction)
        {
            var tracker = new RewardAttributionTracker(
                RewardPipelineConfigurationTests.CreateConfiguration());
            var opened = tracker.TryOpen(
                CreateRequest(preAction),
                VrSessionPhase.Adaptive,
                true,
                out var resultCode);

            Assert.That(opened, Is.True);
            Assert.That(resultCode, Is.EqualTo(RewardAttributionOpenResultCode.Opened));
            return tracker;
        }

        private static RewardAttributionRequest CreateRequest(
            PhysiologyWindowSnapshot preAction)
        {
            return new RewardAttributionRequest(
                "transition-1",
                preAction,
                EnvironmentAction.NoChange,
                RewardCalculatorTests.CreateEnvironment(),
                RewardCalculatorTests.CreateEnvironment(),
                transitionCompletedMonotonicTimeSeconds: 20d,
                transitionCompletedUtcUnixSeconds: 1010d);
        }

        private static PhysiologyWindowSnapshot IngestAndGetLatest(
            PhysiologyStateBuffer buffer,
            double windowEndUtcUnixSeconds,
            double receivedMonotonicTimeSeconds)
        {
            var ingestion = buffer.Ingest(
                RewardCalculatorTests.CreateWindow(windowEndUtcUnixSeconds),
                windowEndUtcUnixSeconds,
                receivedMonotonicTimeSeconds);
            Assert.That(ingestion.Accepted, Is.True);
            Assert.That(buffer.TryGetLatestAccepted(out var snapshot), Is.True);
            return snapshot;
        }

        private static PhysiologyStateBuffer CreateBuffer()
        {
            return new PhysiologyStateBuffer(
                new PhysiologyValidationConfiguration(
                    "reward-buffer-test",
                    1,
                    staleAfterSeconds: 500d,
                    minimumWindowDurationSeconds: 1d,
                    maximumFutureClockSkewSeconds: 1d,
                    sourceTimestampToleranceSeconds: 0d,
                    probabilitySumTolerance: 0.01d,
                    minimumDecisionSignalQuality: 0.8d,
                    minimumRewardSignalQuality: 0.8d,
                    maximumBufferedWindows: 8));
        }
    }
}
