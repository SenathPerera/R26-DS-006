using System;
using LaminarVR.AdaptiveMeditation.Physiology;

namespace LaminarVR.AdaptiveMeditation.ResearchEvaluation
{
    public sealed class FaultInjectingPhysiologyModel
        : ISyntheticPhysiologyModel, ISyntheticSafeOracleModel
    {
        public const double PoorSignalQuality = 0.4d;
        public const double StaleSourceOffsetSeconds = 120d;
        public const double OutOfOrderHeldDeliverySeconds = 541d;

        private readonly ProtocolSyntheticPhysiologyModel inner;
        private readonly SyntheticFaultScenarioDefinition scenario;
        private readonly Pcg32 missingWindowRandom;

        public FaultInjectingPhysiologyModel(
            ProtocolSyntheticPhysiologyModel inner,
            SyntheticFaultScenarioDefinition scenario,
            Pcg32 missingWindowRandom)
        {
            this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
            this.scenario = scenario ?? throw new ArgumentNullException(
                nameof(scenario));
            this.missingWindowRandom = missingWindowRandom
                ?? throw new ArgumentNullException(nameof(missingWindowRandom));
        }

        public string ModelId => inner.ModelId + "/" + scenario.Code;

        public SyntheticFaultScenarioDefinition Scenario => scenario;

        public double CalculateUtility(
            LaminarVR.AdaptiveMeditation.Environment.EnvironmentState environment,
            double sessionElapsedSeconds)
        {
            return inner.CalculateUtility(environment, sessionElapsedSeconds);
        }

        public double CalculateExpectedStressReduction(
            LaminarVR.AdaptiveMeditation.Environment.EnvironmentState
                environmentBefore,
            LaminarVR.AdaptiveMeditation.Environment.EnvironmentState
                environmentAfter,
            double sessionElapsedSeconds)
        {
            return inner.CalculateExpectedStressReduction(
                environmentBefore,
                environmentAfter,
                sessionElapsedSeconds);
        }

        public void Initialize(SyntheticPhysiologySessionContext context)
        {
            inner.Initialize(context);
        }

        public SyntheticPhysiologyEmission CreateEmission(
            SyntheticPhysiologyRequest request)
        {
            // Generate first so a dropped transport window does not stop the
            // latent physiology process or shift its observation-noise stream.
            var generated = inner.CreateEmission(request);
            if (scenario.DropProbability > 0d
                && missingWindowRandom.NextUnitDouble()
                    < scenario.DropProbability)
            {
                return SyntheticPhysiologyEmission.Drop(
                    request.EmissionMonotonicTimeSeconds);
            }

            var window = generated.Window;
            var sourceTimestamp = window.SourceTimestampUtcUnixSeconds;
            if (scenario.StaleBurst
                && request.EmissionMonotonicTimeSeconds >= 420d
                && request.EmissionMonotonicTimeSeconds <= 540d)
            {
                sourceTimestamp -= StaleSourceOffsetSeconds;
            }

            var signalQuality = scenario.LowQualityEveryNthEmission > 0
                && request.EmissionSequence
                    % scenario.LowQualityEveryNthEmission == 0
                    ? PoorSignalQuality
                    : window.SignalQuality;
            var transformed = new PhysiologyWindow(
                sourceTimestamp,
                window.WindowStartUtcUnixSeconds,
                window.WindowEndUtcUnixSeconds,
                window.HeartRateBpm,
                window.RmssdMs,
                window.SdnnMs,
                window.Stress,
                signalQuality);
            var deliveryTime = request.EmissionMonotonicTimeSeconds
                + scenario.ArrivalLatencySeconds;
            if (scenario.ReverseOutOfOrderPair
                && request.EmissionMonotonicTimeSeconds == 480d)
            {
                deliveryTime = OutOfOrderHeldDeliverySeconds;
            }

            return SyntheticPhysiologyEmission.Deliver(
                transformed,
                deliveryTime);
        }
    }
}
