using System;

namespace LaminarVR.AdaptiveMeditation.Physiology
{
    public sealed class PhysiologyStateBuffer
    {
        private readonly PhysiologyValidationConfiguration configuration;
        private readonly PhysiologyWindowValidator validator;
        private readonly BufferedWindow[] windows;

        private int count;
        private int nextWriteIndex;
        private int latestIndex = -1;
        private long latestAcceptedSequenceNumber;

        public PhysiologyStateBuffer(
            PhysiologyValidationConfiguration configuration)
        {
            this.configuration = configuration
                ?? throw new ArgumentNullException(nameof(configuration));
            validator = new PhysiologyWindowValidator(configuration);
            windows = new BufferedWindow[configuration.MaximumBufferedWindows];
        }

        public int Count => count;

        public int Capacity => windows.Length;

        public long LatestAcceptedSequenceNumber =>
            latestAcceptedSequenceNumber;

        public PhysiologyIngestionResult Ingest(
            PhysiologyWindow window,
            double receivedTimestampUtcUnixSeconds,
            double receivedMonotonicTimeSeconds)
        {
            if (!IsFiniteNonNegative(receivedMonotonicTimeSeconds))
            {
                return new PhysiologyIngestionResult(
                    PhysiologyIngestionResultCode.InvalidReceiptTime,
                    PhysiologyValidationReasonCode.Accepted,
                    0L);
            }

            var validation = validator.Validate(
                window,
                receivedTimestampUtcUnixSeconds);
            if (!validation.Accepted)
            {
                return new PhysiologyIngestionResult(
                    PhysiologyIngestionResultCode.PayloadRejected,
                    validation.ReasonCode,
                    0L);
            }

            if (latestIndex >= 0)
            {
                if (receivedMonotonicTimeSeconds
                    < windows[latestIndex].ReceivedMonotonicTimeSeconds)
                {
                    return new PhysiologyIngestionResult(
                        PhysiologyIngestionResultCode.NonMonotonicReceiptTime,
                        PhysiologyValidationReasonCode.Accepted,
                        0L);
                }

                var latestWindowEnd =
                    windows[latestIndex].Window.WindowEndUtcUnixSeconds;
                if (window.WindowEndUtcUnixSeconds == latestWindowEnd)
                {
                    return new PhysiologyIngestionResult(
                        PhysiologyIngestionResultCode.DuplicateWindow,
                        PhysiologyValidationReasonCode.Accepted,
                        0L);
                }

                if (window.WindowEndUtcUnixSeconds < latestWindowEnd)
                {
                    return new PhysiologyIngestionResult(
                        PhysiologyIngestionResultCode.OutOfOrderWindow,
                        PhysiologyValidationReasonCode.Accepted,
                        0L);
                }
            }

            latestAcceptedSequenceNumber++;
            var ageAtReceiptSeconds = Math.Max(
                0d,
                receivedTimestampUtcUnixSeconds
                    - window.WindowEndUtcUnixSeconds);
            windows[nextWriteIndex] = new BufferedWindow(
                latestAcceptedSequenceNumber,
                window,
                receivedMonotonicTimeSeconds,
                ageAtReceiptSeconds);
            latestIndex = nextWriteIndex;
            nextWriteIndex = (nextWriteIndex + 1) % windows.Length;
            if (count < windows.Length)
            {
                count++;
            }

            return new PhysiologyIngestionResult(
                PhysiologyIngestionResultCode.Accepted,
                PhysiologyValidationReasonCode.Accepted,
                latestAcceptedSequenceNumber);
        }

        public bool TryGetLatestAccepted(out PhysiologyWindowSnapshot snapshot)
        {
            if (latestIndex < 0)
            {
                snapshot = default;
                return false;
            }

            var latest = windows[latestIndex];
            snapshot = new PhysiologyWindowSnapshot(
                latest.SequenceNumber,
                latest.Window,
                latest.AgeAtReceiptSeconds,
                latest.ReceivedMonotonicTimeSeconds);
            return true;
        }

        public bool TryGetLatestUsable(
            PhysiologyDataUse dataUse,
            double currentMonotonicTimeSeconds,
            long afterSequenceNumberExclusive,
            out PhysiologyWindowSnapshot snapshot,
            out PhysiologyQueryResultCode resultCode)
        {
            snapshot = default;

            if (latestIndex < 0)
            {
                resultCode = PhysiologyQueryResultCode.NoData;
                return false;
            }

            if (!IsFiniteNonNegative(currentMonotonicTimeSeconds))
            {
                resultCode = PhysiologyQueryResultCode.InvalidQueryTime;
                return false;
            }

            var latest = windows[latestIndex];
            if (currentMonotonicTimeSeconds
                < latest.ReceivedMonotonicTimeSeconds)
            {
                resultCode = PhysiologyQueryResultCode.InvalidQueryTime;
                return false;
            }

            if (latest.SequenceNumber <= afterSequenceNumberExclusive)
            {
                resultCode = PhysiologyQueryResultCode.NoNewWindow;
                return false;
            }

            var ageSeconds = latest.AgeAtReceiptSeconds
                + currentMonotonicTimeSeconds
                - latest.ReceivedMonotonicTimeSeconds;
            if (ageSeconds > configuration.StaleAfterSeconds)
            {
                resultCode = PhysiologyQueryResultCode.Stale;
                return false;
            }

            if (!TryGetMinimumSignalQuality(dataUse, out var minimumQuality))
            {
                resultCode = PhysiologyQueryResultCode.UnsupportedUse;
                return false;
            }

            if (latest.Window.SignalQuality < minimumQuality)
            {
                resultCode = PhysiologyQueryResultCode.InsufficientSignalQuality;
                return false;
            }

            snapshot = new PhysiologyWindowSnapshot(
                latest.SequenceNumber,
                latest.Window,
                ageSeconds,
                latest.ReceivedMonotonicTimeSeconds);
            resultCode = PhysiologyQueryResultCode.Available;
            return true;
        }

        public PhysiologyWindowSnapshot[] GetRecentAccepted(int maximumCount)
        {
            if (maximumCount < 1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maximumCount),
                    maximumCount,
                    "At least one recent window must be requested.");
            }

            var resultCount = Math.Min(count, maximumCount);
            var result = new PhysiologyWindowSnapshot[resultCount];
            var startIndex = nextWriteIndex - resultCount;
            if (startIndex < 0)
            {
                startIndex += windows.Length;
            }

            for (var resultIndex = 0;
                resultIndex < resultCount;
                resultIndex++)
            {
                var bufferIndex = (startIndex + resultIndex) % windows.Length;
                var buffered = windows[bufferIndex];
                result[resultIndex] = new PhysiologyWindowSnapshot(
                    buffered.SequenceNumber,
                    buffered.Window,
                    buffered.AgeAtReceiptSeconds,
                    buffered.ReceivedMonotonicTimeSeconds);
            }

            return result;
        }

        public bool HasFreshDecisionWindowAfter(
            long sequenceNumber,
            double currentMonotonicTimeSeconds)
        {
            return TryGetLatestUsable(
                PhysiologyDataUse.Resume,
                currentMonotonicTimeSeconds,
                sequenceNumber,
                out _,
                out _);
        }

        public bool TryGetAcceptedBySequence(
            long sequenceNumber,
            out PhysiologyWindow window)
        {
            for (var index = 0; index < count; index++)
            {
                if (windows[index].SequenceNumber == sequenceNumber)
                {
                    window = windows[index].Window;
                    return true;
                }
            }

            window = null;
            return false;
        }

        private bool TryGetMinimumSignalQuality(
            PhysiologyDataUse dataUse,
            out double minimumQuality)
        {
            switch (dataUse)
            {
                case PhysiologyDataUse.Display:
                    minimumQuality = 0d;
                    return true;
                case PhysiologyDataUse.Decision:
                case PhysiologyDataUse.Resume:
                    minimumQuality =
                        configuration.MinimumDecisionSignalQuality;
                    return true;
                case PhysiologyDataUse.Reward:
                    minimumQuality =
                        configuration.MinimumRewardSignalQuality;
                    return true;
                default:
                    minimumQuality = 0d;
                    return false;
            }
        }

        private static bool IsFiniteNonNegative(double value)
        {
            return !double.IsNaN(value)
                && !double.IsInfinity(value)
                && value >= 0d;
        }

        private readonly struct BufferedWindow
        {
            public BufferedWindow(
                long sequenceNumber,
                PhysiologyWindow window,
                double receivedMonotonicTimeSeconds,
                double ageAtReceiptSeconds)
            {
                SequenceNumber = sequenceNumber;
                Window = window;
                ReceivedMonotonicTimeSeconds = receivedMonotonicTimeSeconds;
                AgeAtReceiptSeconds = ageAtReceiptSeconds;
            }

            public long SequenceNumber { get; }

            public PhysiologyWindow Window { get; }

            public double ReceivedMonotonicTimeSeconds { get; }

            public double AgeAtReceiptSeconds { get; }
        }
    }
}
