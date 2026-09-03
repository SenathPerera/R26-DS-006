using System;

namespace LaminarVR.AdaptiveMeditation.Physiology
{
    public enum PhysiologyWindowForwardingResult
    {
        Forwarded,
        Buffered,
        DuplicateOrOutOfOrder,
        WindowEndInvalid
    }

    public sealed class NewestPhysiologyWindowForwardingGate
    {
        private readonly double forwardingIntervalSeconds;

        private bool hasObservedTime;
        private double lastObservedMonotonicTimeSeconds;
        private bool hasSeenWindow;
        private double newestSeenWindowEndUtcUnixSeconds;
        private bool hasForwardedWindow;
        private double lastForwardedMonotonicTimeSeconds;
        private PhysiologyWindow pendingWindow;

        public NewestPhysiologyWindowForwardingGate(
            double forwardingIntervalSeconds)
        {
            if (!IsFinite(forwardingIntervalSeconds)
                || forwardingIntervalSeconds <= 0d)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(forwardingIntervalSeconds),
                    forwardingIntervalSeconds,
                    "Forwarding interval must be finite and positive.");
            }

            this.forwardingIntervalSeconds = forwardingIntervalSeconds;
        }

        public double ForwardingIntervalSeconds => forwardingIntervalSeconds;

        public PhysiologyWindowForwardingResult Observe(
            PhysiologyWindow window,
            double monotonicTimeSeconds,
            out PhysiologyWindow forwardedWindow)
        {
            if (window == null)
            {
                throw new ArgumentNullException(nameof(window));
            }

            ObserveTime(monotonicTimeSeconds);
            forwardedWindow = null;
            if (!IsFinite(window.WindowEndUtcUnixSeconds))
            {
                return PhysiologyWindowForwardingResult.WindowEndInvalid;
            }

            if (hasSeenWindow
                && window.WindowEndUtcUnixSeconds
                    <= newestSeenWindowEndUtcUnixSeconds)
            {
                return PhysiologyWindowForwardingResult
                    .DuplicateOrOutOfOrder;
            }

            hasSeenWindow = true;
            newestSeenWindowEndUtcUnixSeconds =
                window.WindowEndUtcUnixSeconds;
            pendingWindow = window;

            if (!hasForwardedWindow
                || HasIntervalElapsed(monotonicTimeSeconds))
            {
                forwardedWindow = ConsumePending(monotonicTimeSeconds);
                return PhysiologyWindowForwardingResult.Forwarded;
            }

            return PhysiologyWindowForwardingResult.Buffered;
        }

        public bool TryFlush(
            double monotonicTimeSeconds,
            out PhysiologyWindow forwardedWindow)
        {
            ObserveTime(monotonicTimeSeconds);
            forwardedWindow = null;
            if (pendingWindow == null
                || !hasForwardedWindow
                || !HasIntervalElapsed(monotonicTimeSeconds))
            {
                return false;
            }

            forwardedWindow = ConsumePending(monotonicTimeSeconds);
            return true;
        }

        public void Reset()
        {
            hasObservedTime = false;
            lastObservedMonotonicTimeSeconds = 0d;
            hasSeenWindow = false;
            newestSeenWindowEndUtcUnixSeconds = 0d;
            hasForwardedWindow = false;
            lastForwardedMonotonicTimeSeconds = 0d;
            pendingWindow = null;
        }

        private bool HasIntervalElapsed(double monotonicTimeSeconds)
        {
            return monotonicTimeSeconds - lastForwardedMonotonicTimeSeconds
                >= forwardingIntervalSeconds;
        }

        private PhysiologyWindow ConsumePending(double monotonicTimeSeconds)
        {
            var window = pendingWindow;
            pendingWindow = null;
            hasForwardedWindow = true;
            lastForwardedMonotonicTimeSeconds = monotonicTimeSeconds;
            return window;
        }

        private void ObserveTime(double monotonicTimeSeconds)
        {
            if (!IsFinite(monotonicTimeSeconds)
                || monotonicTimeSeconds < 0d)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(monotonicTimeSeconds));
            }

            if (hasObservedTime
                && monotonicTimeSeconds < lastObservedMonotonicTimeSeconds)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(monotonicTimeSeconds),
                    "Monotonic time cannot move backwards.");
            }

            hasObservedTime = true;
            lastObservedMonotonicTimeSeconds = monotonicTimeSeconds;
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
