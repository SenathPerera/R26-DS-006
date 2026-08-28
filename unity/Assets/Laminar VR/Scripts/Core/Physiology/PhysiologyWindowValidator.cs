using System;

namespace LaminarVR.AdaptiveMeditation.Physiology
{
    public sealed class PhysiologyWindowValidator
    {
        private readonly PhysiologyValidationConfiguration configuration;

        public PhysiologyWindowValidator(
            PhysiologyValidationConfiguration configuration)
        {
            this.configuration = configuration
                ?? throw new ArgumentNullException(nameof(configuration));
        }

        public PhysiologyValidationResult Validate(
            PhysiologyWindow window,
            double receivedTimestampUtcUnixSeconds)
        {
            if (window == null)
            {
                return PhysiologyValidationResult.Reject(
                    PhysiologyValidationReasonCode.PayloadMissing);
            }

            if (!IsFinite(receivedTimestampUtcUnixSeconds))
            {
                return PhysiologyValidationResult.Reject(
                    PhysiologyValidationReasonCode.ReceiptTimestampInvalid);
            }

            if (!IsFinite(window.SourceTimestampUtcUnixSeconds)
                || !IsFinite(window.WindowStartUtcUnixSeconds)
                || !IsFinite(window.WindowEndUtcUnixSeconds))
            {
                return PhysiologyValidationResult.Reject(
                    PhysiologyValidationReasonCode.TimestampInvalid);
            }

            if (Math.Abs(
                    window.SourceTimestampUtcUnixSeconds
                    - window.WindowEndUtcUnixSeconds)
                > configuration.SourceTimestampToleranceSeconds)
            {
                return PhysiologyValidationResult.Reject(
                    PhysiologyValidationReasonCode.SourceTimestampMismatch);
            }

            if (window.WindowStartUtcUnixSeconds
                >= window.WindowEndUtcUnixSeconds)
            {
                return PhysiologyValidationResult.Reject(
                    PhysiologyValidationReasonCode.WindowOrderInvalid);
            }

            if (window.WindowDurationSeconds
                < configuration.MinimumWindowDurationSeconds)
            {
                return PhysiologyValidationResult.Reject(
                    PhysiologyValidationReasonCode.WindowTooShort);
            }

            if (window.WindowEndUtcUnixSeconds
                    - receivedTimestampUtcUnixSeconds
                > configuration.MaximumFutureClockSkewSeconds)
            {
                return PhysiologyValidationResult.Reject(
                    PhysiologyValidationReasonCode.FutureTimestamp);
            }

            if (receivedTimestampUtcUnixSeconds
                    - window.WindowEndUtcUnixSeconds
                > configuration.StaleAfterSeconds)
            {
                return PhysiologyValidationResult.Reject(
                    PhysiologyValidationReasonCode.StaleAtReceipt);
            }

            if (!IsFinite(window.HeartRateBpm) || window.HeartRateBpm <= 0d)
            {
                return PhysiologyValidationResult.Reject(
                    PhysiologyValidationReasonCode.HeartRateInvalid);
            }

            if (!IsValidOptionalNonNegative(window.RmssdMs))
            {
                return PhysiologyValidationResult.Reject(
                    PhysiologyValidationReasonCode.RmssdInvalid);
            }

            if (!IsValidOptionalNonNegative(window.SdnnMs))
            {
                return PhysiologyValidationResult.Reject(
                    PhysiologyValidationReasonCode.SdnnInvalid);
            }

            if (!IsInUnitInterval(window.SignalQuality))
            {
                return PhysiologyValidationResult.Reject(
                    PhysiologyValidationReasonCode.SignalQualityInvalid);
            }

            return ValidateStressDecision(window.Stress);
        }

        private PhysiologyValidationResult ValidateStressDecision(
            StressDecision stress)
        {
            if (stress == null)
            {
                return PhysiologyValidationResult.Reject(
                    PhysiologyValidationReasonCode.StressDecisionMissing);
            }

            if (!IsSupportedMode(stress.Mode))
            {
                return PhysiologyValidationResult.Reject(
                    PhysiologyValidationReasonCode.StressModeInvalid);
            }

            if (!HasValidLevels(stress))
            {
                return PhysiologyValidationResult.Reject(
                    PhysiologyValidationReasonCode.StressLevelsInvalid);
            }

            if (string.IsNullOrWhiteSpace(stress.Label))
            {
                return PhysiologyValidationResult.Reject(
                    PhysiologyValidationReasonCode.StressLabelMissing);
            }

            if (!IsInUnitInterval(stress.Confidence))
            {
                return PhysiologyValidationResult.Reject(
                    PhysiologyValidationReasonCode.StressConfidenceInvalid);
            }

            if (!stress.Probabilities.IsFiniteAndInUnitRange)
            {
                return PhysiologyValidationResult.Reject(
                    PhysiologyValidationReasonCode.StressProbabilitiesInvalid);
            }

            if (Math.Abs(stress.Probabilities.Sum - 1d)
                > configuration.ProbabilitySumTolerance)
            {
                return PhysiologyValidationResult.Reject(
                    PhysiologyValidationReasonCode.StressProbabilitySumInvalid);
            }

            if (!IsFinite(stress.ContinuousScore)
                || stress.ContinuousScore < 0d
                || stress.ContinuousScore > 3d)
            {
                return PhysiologyValidationResult.Reject(
                    PhysiologyValidationReasonCode.ContinuousStressScoreInvalid);
            }

            return PhysiologyValidationResult.Valid;
        }

        private static bool HasValidLevels(StressDecision stress)
        {
            switch (stress.Mode)
            {
                case StressDecisionMode.Point:
                    return IsValidLevel(stress.PointLevel)
                        && !stress.BandLowLevel.HasValue
                        && !stress.BandHighLevel.HasValue
                        && stress.Adjacent != true;
                case StressDecisionMode.Band:
                    if (stress.PointLevel.HasValue
                        || !IsValidLevel(stress.BandLowLevel)
                        || !IsValidLevel(stress.BandHighLevel)
                        || stress.BandLowLevel.Value >= stress.BandHighLevel.Value)
                    {
                        return false;
                    }

                    return !stress.Adjacent.HasValue
                        || stress.Adjacent.Value
                            == (stress.BandHighLevel.Value
                                - stress.BandLowLevel.Value
                                == 1);
                default:
                    return false;
            }
        }

        private static bool IsValidLevel(int? level)
        {
            return level.HasValue && level.Value >= 0 && level.Value <= 3;
        }

        private static bool IsSupportedMode(StressDecisionMode mode)
        {
            return mode == StressDecisionMode.Point
                || mode == StressDecisionMode.Band;
        }

        private static bool IsValidOptionalNonNegative(double? value)
        {
            return !value.HasValue
                || (IsFinite(value.Value) && value.Value >= 0d);
        }

        private static bool IsInUnitInterval(double value)
        {
            return IsFinite(value) && value >= 0d && value <= 1d;
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}

