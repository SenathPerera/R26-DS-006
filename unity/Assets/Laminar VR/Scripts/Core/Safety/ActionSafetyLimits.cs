using System;

namespace LaminarVR.AdaptiveMeditation.Safety
{
    public readonly struct ActionSafetyLimits
    {
        public ActionSafetyLimits(
            int maximumConsecutiveSameDirectionActions,
            double maximumTotalVariation)
        {
            if (maximumConsecutiveSameDirectionActions < 1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maximumConsecutiveSameDirectionActions),
                    maximumConsecutiveSameDirectionActions,
                    "The consecutive-action limit must be at least 1.");
            }

            if (double.IsNaN(maximumTotalVariation)
                || double.IsInfinity(maximumTotalVariation)
                || maximumTotalVariation < 0d)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maximumTotalVariation),
                    maximumTotalVariation,
                    "Maximum total variation must be finite and non-negative.");
            }

            MaximumConsecutiveSameDirectionActions =
                maximumConsecutiveSameDirectionActions;
            MaximumTotalVariation = maximumTotalVariation;
        }

        public int MaximumConsecutiveSameDirectionActions { get; }

        public double MaximumTotalVariation { get; }
    }
}
