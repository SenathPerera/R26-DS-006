using System;
using LaminarVR.AdaptiveMeditation.Application;
using LaminarVR.AdaptiveMeditation.Physiology;
using LaminarVR.AdaptiveMeditation.Policy.ContextualBandit;
using LaminarVR.AdaptiveMeditation.Rewards;
using LaminarVR.AdaptiveMeditation.Session;

namespace LaminarVR.AdaptiveMeditation.Coordination
{
    // Runtime copies keep the approved pilot assets unchanged.
    public static class ResponsiveTestingPreset
    {
        public const string Id = "responsive-testing-v1";
        public const double MinimumQuality = 0.35;
        public const double MinimumAudioConfidence = 0.20;
        public const double DecisionIntervalSeconds = 15;
        public const double ForwardIntervalSeconds = 5;
        public const double RewardTimeoutSeconds = 240;
        public const double ExplorationCoefficient = 0.50;

        private static string TestingId(string original) => original + "-" + Id;

        public static SessionTimingConfiguration Apply(SessionTimingConfiguration value) =>
            new SessionTimingConfiguration(TestingId(value.ConfigurationId), value.ConfigurationVersion,
                value.AcclimatizationDurationSeconds, value.AdaptiveDurationSeconds,
                value.StabilizationDurationSeconds, DecisionIntervalSeconds);

        public static PhysiologyValidationConfiguration Apply(PhysiologyValidationConfiguration value) =>
            new PhysiologyValidationConfiguration(TestingId(value.ConfigurationId), value.ConfigurationVersion,
                value.StaleAfterSeconds, value.MinimumWindowDurationSeconds,
                value.MaximumFutureClockSkewSeconds, value.SourceTimestampToleranceSeconds,
                value.ProbabilitySumTolerance, MinimumQuality, MinimumQuality, value.MaximumBufferedWindows);

        public static ProductionCoordinatorConfiguration Apply(ProductionCoordinatorConfiguration value) =>
            new ProductionCoordinatorConfiguration(TestingId(value.ConfigurationId), value.ConfigurationVersion,
                ForwardIntervalSeconds, value.SafetyLimits.MaximumConsecutiveSameDirectionActions,
                value.SafetyLimits.MaximumTotalVariation);

        public static RewardPipelineConfiguration Apply(RewardPipelineConfiguration value) =>
            new RewardPipelineConfiguration(TestingId(value.ConfigurationId), value.ConfigurationVersion,
                value.BaselineStandardDeviationMethod, value.MinimumBaselineSamples,
                value.MinimumBaselineStandardDeviation, value.TrendWindowCount, value.MinimumTrendSamples,
                value.SettlingSeconds, Math.Max(value.MaximumAttributionWaitSeconds, RewardTimeoutSeconds),
                value.StressWeight, value.RmssdWeight, value.HeartRateWeight, value.ChangePenaltyWeight,
                value.DiscomfortPenaltyWeight, value.SafetyPenaltyWeight);

        public static LinUcbModelConfiguration Apply(LinUcbModelConfiguration value) =>
            new LinUcbModelConfiguration(TestingId(value.ConfigurationId), value.ConfigurationVersion,
                value.FeatureSchemaVersion, value.FeatureCount, value.RidgeRegularization,
                Math.Max(value.ExplorationCoefficient, ExplorationCoefficient));
    }
}
