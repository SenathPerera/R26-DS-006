using System;
using LaminarVR.AdaptiveMeditation.Environment;
using LaminarVR.AdaptiveMeditation.Physiology;
using LaminarVR.AdaptiveMeditation.Session;

namespace LaminarVR.AdaptiveMeditation.Rewards
{
    public enum RewardAttributionOpenResultCode
    {
        Opened,
        AlreadyPending,
        InvalidPhase,
        NetworkUnavailable,
        InvalidRequest
    }

    public enum RewardAttributionResolutionCode
    {
        Matched,
        NoPending,
        WaitingForSettling,
        WaitingForWindow,
        WindowNotRewardUsable,
        WindowOverlapsTransitionOrSettling,
        InvalidatedForPhase,
        InvalidatedForNetwork,
        TimedOut
    }

    public enum RewardAttributionInvalidationReason
    {
        Pause,
        NetworkLoss,
        EmergencyStop,
        SessionEnded,
        TransitionCancelled,
        Timeout,
        InvalidSessionPhase
    }

    public sealed class RewardAttributionRequest
    {
        public RewardAttributionRequest(
            string transitionId,
            PhysiologyWindowSnapshot preActionPhysiology,
            EnvironmentAction executedAction,
            EnvironmentState environmentBefore,
            EnvironmentState environmentAfter,
            double transitionCompletedMonotonicTimeSeconds,
            double transitionCompletedUtcUnixSeconds)
        {
            if (string.IsNullOrWhiteSpace(transitionId))
            {
                throw new ArgumentException(
                    "Transition ID is required.",
                    nameof(transitionId));
            }

            if (preActionPhysiology.SequenceNumber < 1L
                || preActionPhysiology.Window == null)
            {
                throw new ArgumentException(
                    "A validated pre-action physiology snapshot is required.",
                    nameof(preActionPhysiology));
            }

            if (!Enum.IsDefined(typeof(EnvironmentAction), executedAction))
            {
                throw new ArgumentOutOfRangeException(nameof(executedAction));
            }

            if (!environmentBefore.IsNormalized
                || !environmentAfter.IsNormalized)
            {
                throw new ArgumentException(
                    "Reward attribution environments must be normalized.");
            }

            ValidateNonNegativeFinite(
                transitionCompletedMonotonicTimeSeconds,
                nameof(transitionCompletedMonotonicTimeSeconds));
            ValidateNonNegativeFinite(
                transitionCompletedUtcUnixSeconds,
                nameof(transitionCompletedUtcUnixSeconds));
            if (transitionCompletedUtcUnixSeconds
                < preActionPhysiology.Window.WindowEndUtcUnixSeconds)
            {
                throw new ArgumentException(
                    "Transition completion cannot precede the pre-action window.",
                    nameof(transitionCompletedUtcUnixSeconds));
            }

            TransitionId = transitionId.Trim();
            PreActionPhysiology = preActionPhysiology;
            ExecutedAction = executedAction;
            EnvironmentBefore = environmentBefore;
            EnvironmentAfter = environmentAfter;
            TransitionCompletedMonotonicTimeSeconds =
                transitionCompletedMonotonicTimeSeconds;
            TransitionCompletedUtcUnixSeconds =
                transitionCompletedUtcUnixSeconds;
        }

        public string TransitionId { get; }

        public PhysiologyWindowSnapshot PreActionPhysiology { get; }

        public EnvironmentAction ExecutedAction { get; }

        public EnvironmentState EnvironmentBefore { get; }

        public EnvironmentState EnvironmentAfter { get; }

        public double TransitionCompletedMonotonicTimeSeconds { get; }

        public double TransitionCompletedUtcUnixSeconds { get; }

        private static void ValidateNonNegativeFinite(
            double value,
            string parameterName)
        {
            if (double.IsNaN(value)
                || double.IsInfinity(value)
                || value < 0d)
            {
                throw new ArgumentOutOfRangeException(parameterName);
            }
        }
    }

    public readonly struct RewardAttributionMatch
    {
        internal RewardAttributionMatch(
            RewardAttributionRequest request,
            PhysiologyWindowSnapshot postActionPhysiology)
        {
            Request = request;
            PostActionPhysiology = postActionPhysiology;
        }

        public RewardAttributionRequest Request { get; }

        public PhysiologyWindowSnapshot PostActionPhysiology { get; }
    }

    public readonly struct RewardAttributionInvalidation
    {
        internal RewardAttributionInvalidation(
            string transitionId,
            RewardAttributionInvalidationReason reason,
            double monotonicTimeSeconds)
        {
            TransitionId = transitionId;
            Reason = reason;
            MonotonicTimeSeconds = monotonicTimeSeconds;
        }

        public string TransitionId { get; }

        public RewardAttributionInvalidationReason Reason { get; }

        public double MonotonicTimeSeconds { get; }
    }

    public sealed class RewardAttributionTracker
    {
        private readonly RewardPipelineConfiguration configuration;
        private RewardAttributionRequest pending;
        private long latestConsumedPostWindowSequenceNumber;

        public RewardAttributionTracker(
            RewardPipelineConfiguration configuration)
        {
            this.configuration = configuration
                ?? throw new ArgumentNullException(nameof(configuration));
        }

        public bool HasPending => pending != null;

        public string PendingTransitionId => pending?.TransitionId;

        public long LatestConsumedPostWindowSequenceNumber =>
            latestConsumedPostWindowSequenceNumber;

        public RewardAttributionInvalidation? LastInvalidation { get; private set; }

        public bool TryOpen(
            RewardAttributionRequest request,
            VrSessionPhase phase,
            bool networkConnected,
            out RewardAttributionOpenResultCode resultCode)
        {
            if (request == null)
            {
                resultCode = RewardAttributionOpenResultCode.InvalidRequest;
                return false;
            }

            if (pending != null)
            {
                resultCode = RewardAttributionOpenResultCode.AlreadyPending;
                return false;
            }

            if (phase != VrSessionPhase.Adaptive)
            {
                resultCode = RewardAttributionOpenResultCode.InvalidPhase;
                return false;
            }

            if (!networkConnected)
            {
                resultCode = RewardAttributionOpenResultCode.NetworkUnavailable;
                return false;
            }

            if (request.PreActionPhysiology.SequenceNumber
                < latestConsumedPostWindowSequenceNumber)
            {
                resultCode = RewardAttributionOpenResultCode.InvalidRequest;
                return false;
            }

            pending = request;
            LastInvalidation = null;
            resultCode = RewardAttributionOpenResultCode.Opened;
            return true;
        }

        public bool TryResolve(
            PhysiologyStateBuffer physiologyBuffer,
            double currentMonotonicTimeSeconds,
            VrSessionPhase phase,
            bool networkConnected,
            out RewardAttributionMatch match,
            out RewardAttributionResolutionCode resultCode)
        {
            if (physiologyBuffer == null)
            {
                throw new ArgumentNullException(nameof(physiologyBuffer));
            }

            ValidateMonotonicTime(currentMonotonicTimeSeconds);
            match = default;
            if (pending == null)
            {
                resultCode = RewardAttributionResolutionCode.NoPending;
                return false;
            }

            if (phase != VrSessionPhase.Adaptive)
            {
                InvalidatePending(
                    RewardAttributionInvalidationReason.InvalidSessionPhase,
                    currentMonotonicTimeSeconds);
                resultCode = RewardAttributionResolutionCode.InvalidatedForPhase;
                return false;
            }

            if (!networkConnected)
            {
                InvalidatePending(
                    RewardAttributionInvalidationReason.NetworkLoss,
                    currentMonotonicTimeSeconds);
                resultCode = RewardAttributionResolutionCode.InvalidatedForNetwork;
                return false;
            }

            var eligibleMonotonicTime =
                pending.TransitionCompletedMonotonicTimeSeconds
                + configuration.SettlingSeconds;
            var timeoutAt = pending.TransitionCompletedMonotonicTimeSeconds
                + configuration.MaximumAttributionWaitSeconds;
            if (currentMonotonicTimeSeconds > timeoutAt)
            {
                InvalidatePending(
                    RewardAttributionInvalidationReason.Timeout,
                    currentMonotonicTimeSeconds);
                resultCode = RewardAttributionResolutionCode.TimedOut;
                return false;
            }

            if (currentMonotonicTimeSeconds < eligibleMonotonicTime)
            {
                resultCode = RewardAttributionResolutionCode.WaitingForSettling;
                return false;
            }

            var afterSequence = Math.Max(
                pending.PreActionPhysiology.SequenceNumber,
                latestConsumedPostWindowSequenceNumber);
            if (!physiologyBuffer.TryGetLatestUsable(
                PhysiologyDataUse.Reward,
                currentMonotonicTimeSeconds,
                afterSequence,
                out var postAction,
                out var queryResult))
            {
                resultCode = queryResult == PhysiologyQueryResultCode.NoData
                        || queryResult == PhysiologyQueryResultCode.NoNewWindow
                    ? RewardAttributionResolutionCode.WaitingForWindow
                    : RewardAttributionResolutionCode.WindowNotRewardUsable;
                return false;
            }

            if (!IsFiniteNonNegative(
                    postAction.ReceivedMonotonicTimeSeconds)
                || postAction.ReceivedMonotonicTimeSeconds
                    < eligibleMonotonicTime)
            {
                resultCode = RewardAttributionResolutionCode.WaitingForWindow;
                return false;
            }

            var eligibleWindowStartUtc =
                pending.TransitionCompletedUtcUnixSeconds
                + configuration.SettlingSeconds;
            if (postAction.Window.WindowStartUtcUnixSeconds
                < eligibleWindowStartUtc)
            {
                resultCode = RewardAttributionResolutionCode
                    .WindowOverlapsTransitionOrSettling;
                return false;
            }

            match = new RewardAttributionMatch(pending, postAction);
            latestConsumedPostWindowSequenceNumber = postAction.SequenceNumber;
            pending = null;
            resultCode = RewardAttributionResolutionCode.Matched;
            return true;
        }

        public bool TryInvalidate(
            RewardAttributionInvalidationReason reason,
            double monotonicTimeSeconds,
            out RewardAttributionInvalidation invalidation)
        {
            ValidateMonotonicTime(monotonicTimeSeconds);
            if (!Enum.IsDefined(
                typeof(RewardAttributionInvalidationReason),
                reason))
            {
                throw new ArgumentOutOfRangeException(nameof(reason));
            }

            if (pending == null)
            {
                invalidation = default;
                return false;
            }

            invalidation = InvalidatePending(reason, monotonicTimeSeconds);
            return true;
        }

        private RewardAttributionInvalidation InvalidatePending(
            RewardAttributionInvalidationReason reason,
            double monotonicTimeSeconds)
        {
            var invalidation = new RewardAttributionInvalidation(
                pending.TransitionId,
                reason,
                monotonicTimeSeconds);
            pending = null;
            LastInvalidation = invalidation;
            return invalidation;
        }

        private static void ValidateMonotonicTime(double value)
        {
            if (!IsFiniteNonNegative(value))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    value,
                    "Monotonic time must be finite and non-negative.");
            }
        }

        private static bool IsFiniteNonNegative(double value)
        {
            return !double.IsNaN(value)
                && !double.IsInfinity(value)
                && value >= 0d;
        }
    }
}
