using System;

namespace LaminarVR.AdaptiveMeditation.Coordination
{
    public enum AdaptationOwner { None, Visual, Audio, Content }

    public sealed class AdaptationStatus
    {
        public AdaptationOwner Owner { get; internal set; }
        public string Stage { get; internal set; }
        public string Reason { get; internal set; }
        public string DecisionId { get; internal set; }
        public double EligiblePostStartUtc { get; internal set; }
        public double DeadlineMonotonic { get; internal set; }
        public double PreWindowNotBeforeUtc { get; internal set; }
        public long CompletedTurns { get; internal set; }
        public long CancelledTurns { get; internal set; }
    }

    // One reservation covers execution, settling and the complete physiology window.
    // The lock also protects calls resumed by asynchronous visual telemetry writes.
    public sealed class AdaptationCoordinator
    {
        public const string SchemaVersion = "shared-adaptation-v1";
        private readonly object sync = new object();
        private AdaptationOwner owner;
        private AdaptationOwner nextOwner;
        private long generation;
        private long lease;
        private string decisionId = string.Empty;
        private string reason = "Waiting for an eligible agent";
        private bool suspended;
        private bool outcomeAccepted;
        private double deadline;
        private double eligiblePostStart = double.PositiveInfinity;
        private double preEnd;
        private double preNotBefore;
        private double lastConsumedPostEnd;
        private double handoverUntil;
        private double contentNotBefore;
        private double lastVisualPreEnd = double.NegativeInfinity;
        private double lastAudioPreEnd = double.NegativeInfinity;
        private long completed;
        private long cancelled;

        public AdaptationStatus Status
        {
            get
            {
                lock (sync) return new AdaptationStatus
                {
                    Owner = owner, DecisionId = decisionId, Reason = reason,
                    Stage = suspended ? "Suspended" : owner == AdaptationOwner.None ? "Waiting"
                        : outcomeAccepted ? "Completing" : double.IsPositiveInfinity(eligiblePostStart) ? "Transitioning" : "Measuring",
                    EligiblePostStartUtc = double.IsPositiveInfinity(eligiblePostStart) ? 0 : eligiblePostStart,
                    DeadlineMonotonic = owner == AdaptationOwner.None ? 0 : deadline,
                    PreWindowNotBeforeUtc = preNotBefore, CompletedTurns = completed, CancelledTurns = cancelled
                };
            }
        }

        public void SetSuspended(bool value, double utc, string message)
        {
            lock (sync)
            {
                if (value == suspended) return;
                suspended = value;
                CancelLocked(utc, value ? message : "Waiting for fresh physiology after resume");
            }
        }

        public void Tick(double monotonic, double utc)
        {
            lock (sync)
                if (owner != AdaptationOwner.None && monotonic > deadline)
                    CancelLocked(utc, "Attribution timeout");
        }

        public bool TryAcquire(AdaptationOwner candidate, string id, double windowStart, double windowEnd,
            double monotonic, double utc, double timeoutSeconds, out long reservation)
        {
            reservation = 0;
            if (candidate != AdaptationOwner.Visual && candidate != AdaptationOwner.Audio)
                throw new ArgumentOutOfRangeException(nameof(candidate));
            lock (sync)
            {
                if (!CanRequest(candidate, windowStart, windowEnd, monotonic, utc)) return false;
                BeginLocked(candidate, id, monotonic, timeoutSeconds);
                preEnd = windowEnd;
                if (candidate == AdaptationOwner.Visual) lastVisualPreEnd = windowEnd;
                else lastAudioPreEnd = windowEnd;
                reservation = lease;
                return true;
            }
        }

        public bool CanRequest(AdaptationOwner candidate, double windowStart, double windowEnd, double monotonic, double utc)
        {
            lock (sync) return (candidate == AdaptationOwner.Visual || candidate == AdaptationOwner.Audio)
                && !suspended && owner == AdaptationOwner.None && Finite(windowStart) && Finite(windowEnd)
                && windowStart < windowEnd && windowEnd <= utc + 2 && utc - windowEnd <= 90
                && windowStart >= preNotBefore && windowEnd >= lastConsumedPostEnd
                && windowEnd > (candidate == AdaptationOwner.Visual ? lastVisualPreEnd : lastAudioPreEnd)
                && (nextOwner == AdaptationOwner.None || candidate == nextOwner || monotonic >= handoverUntil);
        }

        public bool TryBeginContent(string id, double monotonic, double timeoutSeconds, out long reservation)
        {
            lock (sync)
            {
                reservation = 0;
                if (suspended || owner != AdaptationOwner.None || monotonic < contentNotBefore
                    || monotonic < handoverUntil) return false;
                BeginLocked(AdaptationOwner.Content, id, monotonic, timeoutSeconds);
                reservation = lease;
                return true;
            }
        }

        public bool IsCurrent(long reservation)
        {
            lock (sync) return reservation != 0 && lease == reservation && owner != AdaptationOwner.None && !suspended;
        }

        public bool MarkTransitionCompleted(long reservation, double utc, double settlingSeconds)
        {
            lock (sync)
            {
                if (!IsCurrent(reservation)) return false;
                if (!double.IsPositiveInfinity(eligiblePostStart)) return true;
                eligiblePostStart = utc + Math.Max(0, settlingSeconds);
                preNotBefore = Math.Max(preNotBefore, eligiblePostStart);
                reason = "Waiting for a complete post action window";
                return true;
            }
        }

        public bool TryAcceptOutcome(long reservation, double windowStart, double windowEnd)
        {
            lock (sync)
            {
                if (!IsCurrent(reservation) || owner == AdaptationOwner.Content || outcomeAccepted
                    || !Finite(windowStart) || !Finite(windowEnd) || windowStart >= windowEnd
                    || windowStart < eligiblePostStart || windowStart < preEnd || windowStart < lastConsumedPostEnd) return false;
                lastConsumedPostEnd = windowEnd;
                outcomeAccepted = true;
                return true;
            }
        }

        public bool TryCommitOutcome(long reservation, double windowStart, double windowEnd, Action update)
        {
            if (update == null) throw new ArgumentNullException(nameof(update));
            lock (sync)
            {
                if (!TryAcceptOutcome(reservation, windowStart, windowEnd)) return false;
                update();
                return true;
            }
        }

        public void Release(long reservation, double monotonic, double utc, string message)
        {
            lock (sync)
            {
                if (!IsCurrent(reservation)) return;
                if (owner == AdaptationOwner.Content)
                {
                    preNotBefore = Math.Max(preNotBefore, utc + 5);
                    // Repeated regeneration must leave time for at least one clean learning turn.
                    contentNotBefore = monotonic + 240;
                }
                else
                {
                    nextOwner = owner == AdaptationOwner.Visual ? AdaptationOwner.Audio : AdaptationOwner.Visual;
                    // Give the peer a chance to request its turn without blocking a missing agent indefinitely.
                    handoverUntil = monotonic + 5;
                    if (outcomeAccepted) { completed++; contentNotBefore = 0; }
                    else
                    {
                        cancelled++;
                        preNotBefore = Math.Max(preNotBefore, utc + 5);
                    }
                }
                ClearLocked(message);
            }
        }

        public void Invalidate(double utc, string message)
        {
            lock (sync) CancelLocked(utc, message);
        }

        public void CancelReservation(long reservation, double utc, string message)
        {
            lock (sync) if (IsCurrent(reservation)) CancelLocked(utc, message);
        }

        private void BeginLocked(AdaptationOwner candidate, string id, double monotonic, double timeout)
        {
            if (string.IsNullOrWhiteSpace(id) || !Finite(monotonic) || !Finite(timeout) || timeout <= 0)
                throw new ArgumentException("A decision identity and finite deadline are required.");
            owner = candidate;
            decisionId = id;
            lease = ++generation;
            deadline = monotonic + timeout;
            eligiblePostStart = double.PositiveInfinity;
            outcomeAccepted = false;
            reason = "Applying one coordinated change";
        }

        private void CancelLocked(double utc, string message)
        {
            if (!Finite(utc)) throw new ArgumentOutOfRangeException(nameof(utc));
            if (owner != AdaptationOwner.None) cancelled++;
            preNotBefore = Math.Max(preNotBefore, utc + 5);
            generation++;
            ClearLocked(message);
        }

        private void ClearLocked(string message)
        {
            owner = AdaptationOwner.None;
            lease = 0;
            decisionId = string.Empty;
            eligiblePostStart = double.PositiveInfinity;
            reason = message;
            outcomeAccepted = false;
        }

        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
