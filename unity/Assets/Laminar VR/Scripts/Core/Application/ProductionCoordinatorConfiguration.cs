using System;
using LaminarVR.AdaptiveMeditation.Physiology;
using LaminarVR.AdaptiveMeditation.Rewards;
using LaminarVR.AdaptiveMeditation.Safety;
using LaminarVR.AdaptiveMeditation.Session;

namespace LaminarVR.AdaptiveMeditation.Application
{
    public sealed class ProductionCoordinatorConfiguration
    {
        private const double ComparisonTolerance = 1e-9d;

        public ProductionCoordinatorConfiguration(
            string configurationId,
            int configurationVersion,
            double expectedPhysiologyOutputIntervalSeconds,
            int maximumConsecutiveSameDirectionActions,
            double maximumTotalVariation)
        {
            if (string.IsNullOrWhiteSpace(configurationId))
            {
                throw new ArgumentException(
                    "Coordinator configuration ID is required.",
                    nameof(configurationId));
            }

            if (configurationVersion < 1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(configurationVersion));
            }

            if (!IsFinite(expectedPhysiologyOutputIntervalSeconds)
                || expectedPhysiologyOutputIntervalSeconds <= 0d)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(expectedPhysiologyOutputIntervalSeconds),
                    expectedPhysiologyOutputIntervalSeconds,
                    "Expected physiology output interval must be finite and positive.");
            }

            ConfigurationId = configurationId.Trim();
            ConfigurationVersion = configurationVersion;
            ExpectedPhysiologyOutputIntervalSeconds =
                expectedPhysiologyOutputIntervalSeconds;
            SafetyLimits = new ActionSafetyLimits(
                maximumConsecutiveSameDirectionActions,
                maximumTotalVariation);
        }

        public string ConfigurationId { get; }

        public int ConfigurationVersion { get; }

        public double ExpectedPhysiologyOutputIntervalSeconds { get; }

        public ActionSafetyLimits SafetyLimits { get; }

        public bool TryValidateCompatibility(
            SessionTimingConfiguration sessionTiming,
            PhysiologyValidationConfiguration physiologyValidation,
            RewardPipelineConfiguration rewardPipeline,
            out string validationError)
        {
            if (sessionTiming == null)
            {
                throw new ArgumentNullException(nameof(sessionTiming));
            }

            if (physiologyValidation == null)
            {
                throw new ArgumentNullException(nameof(physiologyValidation));
            }

            if (rewardPipeline == null)
            {
                throw new ArgumentNullException(nameof(rewardPipeline));
            }

            if (sessionTiming.DecisionIntervalSeconds + ComparisonTolerance
                < ExpectedPhysiologyOutputIntervalSeconds)
            {
                validationError =
                    "Decision interval cannot be shorter than the expected "
                    + "physiology output interval. The coordinator must not "
                    + "expect Component B output more frequently than configured.";
                return false;
            }

            if (physiologyValidation.StaleAfterSeconds + ComparisonTolerance
                < ExpectedPhysiologyOutputIntervalSeconds)
            {
                validationError =
                    "Physiology stale-after time cannot be shorter than the "
                    + "expected Component B output interval.";
                return false;
            }

            if (rewardPipeline.MaximumAttributionWaitSeconds
                    + ComparisonTolerance
                < ExpectedPhysiologyOutputIntervalSeconds)
            {
                validationError =
                    "Maximum reward-attribution wait cannot be shorter than "
                    + "the expected Component B output interval.";
                return false;
            }

            validationError = string.Empty;
            return true;
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
