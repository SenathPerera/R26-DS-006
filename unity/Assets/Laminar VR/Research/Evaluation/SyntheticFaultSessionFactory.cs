using System;

namespace LaminarVR.AdaptiveMeditation.ResearchEvaluation
{
    public static class SyntheticFaultSessionFactory
    {
        public static ISyntheticPhysiologyModel CreatePhysiologyModel(
            SyntheticReplication replication,
            SyntheticFaultScenarioId scenarioId)
        {
            if (replication == null)
            {
                throw new ArgumentNullException(nameof(replication));
            }

            var scenario = SyntheticFaultScenarioCatalog.Get(scenarioId);
            return new FaultInjectingPhysiologyModel(
                replication.CreatePhysiologyModel(
                    scenario.NoiseMultiplier),
                scenario,
                replication.CreateRandomStream(
                    EvaluationRandomStreamId.MissingWindowSchedule));
        }

        public static IEvaluationNetworkSchedule CreateNetworkSchedule(
            SyntheticFaultScenarioId scenarioId)
        {
            return SyntheticFaultScenarioCatalog.Get(scenarioId)
                .CreateNetworkSchedule();
        }
    }
}
