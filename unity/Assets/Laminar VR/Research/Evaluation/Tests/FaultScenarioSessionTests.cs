using System.Threading.Tasks;
using LaminarVR.AdaptiveMeditation.Policy;
using NUnit.Framework;

namespace LaminarVR.AdaptiveMeditation.ResearchEvaluation.Tests
{
    public sealed class FaultScenarioSessionTests
    {
        [Test]
        public async Task RunAsync_AllStudyBFamilies_PreserveSafetyAndUpdates()
        {
            Assert.That(
                FrozenEvaluationProfileLoader.TryLoad(
                    out var bundle,
                    out var validationError),
                Is.True,
                validationError);
            var archetypes = new[]
            {
                SyntheticArchetypeId.A1PreferenceAlignedBalanced,
                SyntheticArchetypeId.A5MotionAverseThreshold
            };

            for (var archetypeIndex = 0;
                archetypeIndex < archetypes.Length;
                archetypeIndex++)
            {
                for (var scenarioIndex = 0;
                    scenarioIndex < SyntheticFaultScenarioCatalog.Count;
                    scenarioIndex++)
                {
                    var scenario = SyntheticFaultScenarioCatalog.GetAt(
                        scenarioIndex);
                    var replication = SyntheticReplicationFactory.Create(
                        200000,
                        archetypes[archetypeIndex]);
                    var result = await new EvaluationSessionRunner().RunAsync(
                        new EvaluationSessionRequest(
                            bundle,
                            "phase-4-"
                                + replication.Archetype.Code
                                + "-"
                                + scenario.Code,
                            "synthetic-test",
                            StudyPolicyMode.ContextualBandit,
                            replication.Preference,
                            SyntheticFaultSessionFactory.CreatePhysiologyModel(
                                replication,
                                scenario.Id),
                            1700000000d,
                            scenario.CreateNetworkSchedule()));

                    Assert.That(
                        result.AllAppliedStatesSafe,
                        Is.True,
                        replication.Archetype.Code + "/" + scenario.Code);
                    Assert.That(
                        result.PolicyState.ModelUpdateCount,
                        Is.EqualTo(result.RewardAppliedCount),
                        replication.Archetype.Code + "/" + scenario.Code);
                    Assert.That(
                        result.Decisions.Length,
                        Is.LessThanOrEqualTo(bundle.DecisionOpportunityCount),
                        replication.Archetype.Code + "/" + scenario.Code);
                }
            }
        }

        [Test]
        public async Task RunAsync_NetworkGap_SkipsGapDecisionsAndRecovers()
        {
            Assert.That(
                FrozenEvaluationProfileLoader.TryLoad(
                    out var bundle,
                    out var validationError),
                Is.True,
                validationError);
            var replication = SyntheticReplicationFactory.Create(
                200000,
                SyntheticArchetypeId.A1PreferenceAlignedBalanced);
            var scenario = SyntheticFaultScenarioCatalog.Get(
                SyntheticFaultScenarioId.R10NetworkGap);

            var result = await new EvaluationSessionRunner().RunAsync(
                new EvaluationSessionRequest(
                    bundle,
                    "phase-4-network-gap",
                    "synthetic-test",
                    StudyPolicyMode.ContextualBandit,
                    replication.Preference,
                    SyntheticFaultSessionFactory.CreatePhysiologyModel(
                        replication,
                        scenario.Id),
                    1700000000d,
                    scenario.CreateNetworkSchedule()));

            Assert.That(
                result.Decisions[3].Result.ResultCode,
                Is.EqualTo(
                    PolicyDecisionCycleResultCode.SkippedNetworkUnavailable));
            Assert.That(
                result.Decisions[4].Result.ResultCode,
                Is.EqualTo(
                    PolicyDecisionCycleResultCode.SkippedNetworkUnavailable));
            Assert.That(
                result.Decisions[5].Result.ResultCode,
                Is.EqualTo(
                    PolicyDecisionCycleResultCode.SkippedNetworkUnavailable));
            Assert.That(
                result.Decisions[6].Result.ResultCode,
                Is.Not.EqualTo(
                    PolicyDecisionCycleResultCode.SkippedNetworkUnavailable));
        }
    }
}
