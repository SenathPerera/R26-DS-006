using System;
using LaminarVR.AdaptiveMeditation.Physiology;

namespace LaminarVR.AdaptiveMeditation.ResearchEvaluation
{
    // Developer smoke source only. It is not one of the protocol's study
    // archetypes and its outputs must not be used as research observations.
    public sealed class Phase2SmokePhysiologyModel
        : ISyntheticPhysiologyModel
    {
        private SyntheticPhysiologySessionContext context;

        public string ModelId => "phase-2-developer-smoke-v1";

        public void Initialize(SyntheticPhysiologySessionContext sessionContext)
        {
            context = sessionContext ?? throw new ArgumentNullException(
                nameof(sessionContext));
        }

        public SyntheticPhysiologyEmission CreateEmission(
            SyntheticPhysiologyRequest request)
        {
            if (context == null)
            {
                throw new InvalidOperationException(
                    "The synthetic model must be initialized first.");
            }

            double stress;
            double heartRate;
            double rmssd;
            if (request.EmissionMonotonicTimeSeconds <= 60d)
            {
                stress = 2.1d;
                heartRate = 72d;
                rmssd = 40d;
            }
            else if (request.EmissionMonotonicTimeSeconds <= 120d)
            {
                stress = 2.3d;
                heartRate = 74d;
                rmssd = 38d;
            }
            else
            {
                var adaptiveProgress = Math.Max(
                    0d,
                    request.EmissionMonotonicTimeSeconds - 120d) / 900d;
                var responseBonus = request.EligibleActionExposure.HasValue
                    ? 0.08d
                    : 0d;
                stress = Math.Max(
                    0.7d,
                    2.25d - (0.9d * adaptiveProgress) - responseBonus);
                heartRate = 74d
                    - (6d * adaptiveProgress)
                    - responseBonus;
                rmssd = 38d
                    + (12d * adaptiveProgress)
                    + responseBonus;
            }
            var endUtc = context.BaseUtcUnixSeconds
                + request.EmissionMonotonicTimeSeconds;
            var window = new PhysiologyWindow(
                endUtc,
                endUtc - request.WindowDurationSeconds,
                endUtc,
                heartRate,
                rmssd,
                null,
                SyntheticStressDecisionFactory.FromContinuousScore(stress),
                0.9d);

            return SyntheticPhysiologyEmission.Deliver(
                window,
                request.EmissionMonotonicTimeSeconds);
        }
    }
}
