using System;
using LaminarVR.AdaptiveMeditation.Environment;
using LaminarVR.AdaptiveMeditation.Physiology;

namespace LaminarVR.AdaptiveMeditation.ResearchEvaluation
{
    public interface ISyntheticPhysiologyModel
    {
        string ModelId { get; }

        void Initialize(SyntheticPhysiologySessionContext context);

        SyntheticPhysiologyEmission CreateEmission(
            SyntheticPhysiologyRequest request);
    }

    public sealed class SyntheticPhysiologySessionContext
    {
        public SyntheticPhysiologySessionContext(
            string sessionId,
            double baseUtcUnixSeconds,
            EnvironmentState initialEnvironment)
        {
            SessionId = sessionId ?? throw new ArgumentNullException(
                nameof(sessionId));
            BaseUtcUnixSeconds = baseUtcUnixSeconds;
            InitialEnvironment = initialEnvironment;
        }

        public string SessionId { get; }

        public double BaseUtcUnixSeconds { get; }

        public EnvironmentState InitialEnvironment { get; }
    }

    public readonly struct SyntheticActionExposure
    {
        public SyntheticActionExposure(
            string decisionId,
            EnvironmentAction action,
            EnvironmentState environmentBefore,
            EnvironmentState environmentAfter,
            double completedMonotonicTimeSeconds)
        {
            DecisionId = decisionId;
            Action = action;
            EnvironmentBefore = environmentBefore;
            EnvironmentAfter = environmentAfter;
            CompletedMonotonicTimeSeconds = completedMonotonicTimeSeconds;
        }

        public string DecisionId { get; }

        public EnvironmentAction Action { get; }

        public EnvironmentState EnvironmentBefore { get; }

        public EnvironmentState EnvironmentAfter { get; }

        public double CompletedMonotonicTimeSeconds { get; }
    }

    public readonly struct SyntheticPhysiologyRequest
    {
        public SyntheticPhysiologyRequest(
            int emissionSequence,
            double emissionMonotonicTimeSeconds,
            double windowDurationSeconds,
            SyntheticActionExposure? eligibleActionExposure)
        {
            EmissionSequence = emissionSequence;
            EmissionMonotonicTimeSeconds = emissionMonotonicTimeSeconds;
            WindowDurationSeconds = windowDurationSeconds;
            EligibleActionExposure = eligibleActionExposure;
        }

        public int EmissionSequence { get; }

        public double EmissionMonotonicTimeSeconds { get; }

        public double WindowDurationSeconds { get; }

        public SyntheticActionExposure? EligibleActionExposure { get; }
    }

    public readonly struct SyntheticPhysiologyEmission
    {
        private SyntheticPhysiologyEmission(
            bool dropped,
            PhysiologyWindow window,
            double deliveryMonotonicTimeSeconds)
        {
            Dropped = dropped;
            Window = window;
            DeliveryMonotonicTimeSeconds = deliveryMonotonicTimeSeconds;
        }

        public bool Dropped { get; }

        public PhysiologyWindow Window { get; }

        public double DeliveryMonotonicTimeSeconds { get; }

        public static SyntheticPhysiologyEmission Deliver(
            PhysiologyWindow window,
            double deliveryMonotonicTimeSeconds)
        {
            return new SyntheticPhysiologyEmission(
                false,
                window ?? throw new ArgumentNullException(nameof(window)),
                deliveryMonotonicTimeSeconds);
        }

        public static SyntheticPhysiologyEmission Drop(
            double emissionMonotonicTimeSeconds)
        {
            return new SyntheticPhysiologyEmission(
                true,
                null,
                emissionMonotonicTimeSeconds);
        }
    }
}
