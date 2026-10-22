using System.Threading.Tasks;
using LaminarVR.AdaptiveMeditation.Policy;
using NUnit.Framework;

namespace LaminarVR.AdaptiveMeditation.ResearchEvaluation.Tests
{
    public sealed class ProtocolArchetypeSessionTests
    {
        [TestCase(SyntheticArchetypeId.A1PreferenceAlignedBalanced)]
        [TestCase(SyntheticArchetypeId.A2IlluminationDominant)]
        [TestCase(SyntheticArchetypeId.A3WarmthDominant)]
        [TestCase(SyntheticArchetypeId.A4SoftnessRichnessInteraction)]
        [TestCase(SyntheticArchetypeId.A5MotionAverseThreshold)]
        [TestCase(SyntheticArchetypeId.A6PreferencePhysiologyConflict)]
        [TestCase(SyntheticArchetypeId.A7WeakResponder)]
        [TestCase(SyntheticArchetypeId.A8NonStationaryResponder)]
        [TestCase(SyntheticArchetypeId.A9PhysiologicalNonResponder)]
        public async Task RunAsync_ProtocolArchetype_ProducesSafeValidSession(
            SyntheticArchetypeId archetypeId)
        {
            Assert.That(
                FrozenEvaluationProfileLoader.TryLoad(
                    out var bundle,
                    out var validationError),
                Is.True,
                validationError);
            var replication = SyntheticReplicationFactory.Create(
                100000,
                archetypeId);

            var result = await new EvaluationSessionRunner().RunAsync(
                new EvaluationSessionRequest(
                    bundle,
                    "phase-3-" + replication.Archetype.Code,
                    "synthetic-test",
                    StudyPolicyMode.ContextualBandit,
                    replication.Preference,
                    replication.CreatePhysiologyModel(),
                    1700000000d));

            Assert.That(result.BaselineAvailable, Is.True);
            Assert.That(
                result.Decisions.Length,
                Is.EqualTo(bundle.DecisionOpportunityCount));
            Assert.That(result.RewardAppliedCount, Is.GreaterThan(0));
            Assert.That(result.PolicyState.ModelUpdateCount, Is.GreaterThan(0));
            Assert.That(result.AllAppliedStatesSafe, Is.True);
            Assert.That(result.TelemetryEvents, Is.Not.Empty);
        }
    }
}
