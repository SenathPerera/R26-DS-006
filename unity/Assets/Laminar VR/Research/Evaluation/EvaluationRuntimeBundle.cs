using System;
using LaminarVR.AdaptiveMeditation.Application;
using LaminarVR.AdaptiveMeditation.Environment;
using LaminarVR.AdaptiveMeditation.Physiology;
using LaminarVR.AdaptiveMeditation.Policy;
using LaminarVR.AdaptiveMeditation.Policy.ContextualBandit;
using LaminarVR.AdaptiveMeditation.Policy.RuleBased;
using LaminarVR.AdaptiveMeditation.Preferences;
using LaminarVR.AdaptiveMeditation.Rewards;
using LaminarVR.AdaptiveMeditation.Session;
using LaminarVR.AdaptiveMeditation.Stabilization;
using LaminarVR.AdaptiveMeditation.Telemetry;

namespace LaminarVR.AdaptiveMeditation.ResearchEvaluation
{
    public sealed class EvaluationRuntimeBundle
    {
        public const string ProtocolId =
            "component-c-simulation-evaluation-v1.2";
        public const string PilotBundleId =
            "temple-pond-pilot-freeze-v2";
        public const double SyntheticWindowDurationSeconds = 30d;

        public EvaluationRuntimeBundle(
            SceneEnvironmentProfile scene,
            SessionTimingConfiguration timing,
            PhysiologyValidationConfiguration physiology,
            RewardPipelineConfiguration reward,
            ProductionCoordinatorConfiguration coordinator,
            PreferenceInitializationConfiguration preference,
            RuleBasedPolicyConfiguration ruleBased,
            LinUcbModelConfiguration linUcb,
            StabilizationSelectionConfiguration stabilization,
            TelemetryLoggingConfiguration telemetry,
            IFeatureVectorBuilder featureVectorBuilder)
        {
            Scene = scene ?? throw new ArgumentNullException(nameof(scene));
            Timing = timing ?? throw new ArgumentNullException(nameof(timing));
            Physiology = physiology
                ?? throw new ArgumentNullException(nameof(physiology));
            Reward = reward ?? throw new ArgumentNullException(nameof(reward));
            Coordinator = coordinator
                ?? throw new ArgumentNullException(nameof(coordinator));
            Preference = preference
                ?? throw new ArgumentNullException(nameof(preference));
            RuleBased = ruleBased
                ?? throw new ArgumentNullException(nameof(ruleBased));
            LinUcb = linUcb ?? throw new ArgumentNullException(nameof(linUcb));
            Stabilization = stabilization
                ?? throw new ArgumentNullException(nameof(stabilization));
            Telemetry = telemetry
                ?? throw new ArgumentNullException(nameof(telemetry));
            FeatureVectorBuilder = featureVectorBuilder
                ?? throw new ArgumentNullException(
                    nameof(featureVectorBuilder));

            ValidateFrozenIdentity();
            if (!Coordinator.TryValidateCompatibility(
                    Timing,
                    Physiology,
                    Reward,
                    out var compatibilityError))
            {
                throw new ArgumentException(
                    "The frozen evaluation bundle is incompatible: "
                    + compatibilityError,
                    nameof(coordinator));
            }
        }

        public SceneEnvironmentProfile Scene { get; }

        public SessionTimingConfiguration Timing { get; }

        public PhysiologyValidationConfiguration Physiology { get; }

        public RewardPipelineConfiguration Reward { get; }

        public ProductionCoordinatorConfiguration Coordinator { get; }

        public PreferenceInitializationConfiguration Preference { get; }

        public RuleBasedPolicyConfiguration RuleBased { get; }

        public LinUcbModelConfiguration LinUcb { get; }

        public StabilizationSelectionConfiguration Stabilization { get; }

        public TelemetryLoggingConfiguration Telemetry { get; }

        public IFeatureVectorBuilder FeatureVectorBuilder { get; }

        public int DecisionOpportunityCount
        {
            get
            {
                var ratio = Timing.AdaptiveDurationSeconds
                    / Timing.DecisionIntervalSeconds;
                return Math.Max(0, (int)Math.Ceiling(ratio) - 1);
            }
        }

        public IEnvironmentPolicy CreatePolicy(StudyPolicyMode mode)
        {
            if (!StudyPolicyFactory.TryCreate(
                    mode,
                    RuleBased,
                    LinUcb,
                    FeatureVectorBuilder,
                    out var policy,
                    out var resultCode))
            {
                throw new InvalidOperationException(
                    "Could not create evaluation policy "
                    + mode
                    + ": "
                    + resultCode
                    + ".");
            }

            return policy;
        }

        public EnvironmentState CreateInitialState(
            EnvironmentState preferredEnvironment)
        {
            var result = new PreferenceInitializer().Initialize(
                Scene,
                new EnvironmentPreference(preferredEnvironment),
                Preference);
            if (!result.Accepted)
            {
                throw new InvalidOperationException(
                    "Preference initialization failed: "
                    + result.FailureReason
                    + ".");
            }

            return result.SafeInitialState;
        }

        private void ValidateFrozenIdentity()
        {
            RequireIdentity(Scene.SceneId, "temple-pond", "scene");
            RequireIdentity(
                Timing.ConfigurationId,
                "temple-pond-session-pilot-v1",
                "timing");
            RequireIdentity(
                Physiology.ConfigurationId,
                "component-b-physiology-pilot-v1",
                "physiology");
            RequireIdentity(
                Reward.ConfigurationId,
                "adaptive-vr-reward-pilot-v2",
                "reward");
            RequireIdentity(
                Coordinator.ConfigurationId,
                "adaptive-vr-coordinator-pilot-v1",
                "coordinator");
            RequireIdentity(
                Preference.ConfigurationId,
                "adaptive-vr-preference-pilot-v1",
                "preference");
            RequireIdentity(
                RuleBased.ConfigurationId,
                "adaptive-vr-rule-based-pilot-v1",
                "rule-based policy");
            RequireIdentity(
                LinUcb.ConfigurationId,
                "adaptive-vr-linucb-pilot-v1",
                "LinUCB policy");
            RequireIdentity(
                Stabilization.ConfigurationId,
                "adaptive-vr-stabilization-pilot-v1",
                "stabilization");
            RequireIdentity(
                Telemetry.ConfigurationId,
                "adaptive-vr-telemetry-pilot-v3",
                "telemetry");
            RequireIdentity(
                Telemetry.EventSchemaVersion,
                "1.1-pilot",
                "telemetry schema");
            RequireIdentity(
                LinUcb.FeatureSchemaVersion,
                PolicyFeatureVectorBuilder.ProductionSchemaVersion,
                "policy feature schema");
        }

        private static void RequireIdentity(
            string actual,
            string expected,
            string concern)
        {
            if (!string.Equals(actual, expected, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "The "
                    + concern
                    + " identity is '"
                    + actual
                    + "'; expected '"
                    + expected
                    + "'.");
            }
        }
    }
}
