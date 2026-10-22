using System.Threading.Tasks;
using NUnit.Framework;

namespace LaminarVR.AdaptiveMeditation.ResearchEvaluation.Tests
{
    public sealed class EvaluationExperimentRunnerTests
    {
        [Test]
        public async Task RunCellAsync_AllFourPolicies_ProduceOracleMetricsSafely()
        {
            Assert.That(
                FrozenEvaluationProfileLoader.TryLoad(
                    out var bundle,
                    out var validationError),
                Is.True,
                validationError);
            var runner = new EvaluationExperimentRunner();

            for (var policyValue =
                    (int)EvaluationPolicyCondition.StaticPersonalized;
                policyValue <= (int)EvaluationPolicyCondition.RandomSafe;
                policyValue++)
            {
                var policy = (EvaluationPolicyCondition)policyValue;
                var summary = await runner.RunCellAsync(
                    bundle,
                    "phase-5-test",
                    SyntheticArchetypeId.A1PreferenceAlignedBalanced,
                    SyntheticFaultScenarioId.R0Nominal,
                    policy,
                    100000);

                Assert.That(summary.EligibleDecisionCount, Is.GreaterThan(0));
                Assert.That(summary.MeanPseudoRegret, Is.Not.Null);
                Assert.That(summary.CumulativePseudoRegret,
                    Is.GreaterThanOrEqualTo(0d));
                Assert.That(summary.SceneLimitViolationCount, Is.Zero);
                Assert.That(summary.MultiDimensionViolationCount, Is.Zero);
                Assert.That(summary.InvalidModelUpdateCount, Is.Zero);
                Assert.That(summary.AllAppliedStatesSafe, Is.True);
            }
        }

        [Test]
        public async Task RunCellAsync_RandomSafe_ReplaysFromDedicatedStream()
        {
            Assert.That(
                FrozenEvaluationProfileLoader.TryLoad(
                    out var bundle,
                    out var validationError),
                Is.True,
                validationError);
            var runner = new EvaluationExperimentRunner();

            var first = await runner.RunCellAsync(
                bundle,
                "phase-5-replay",
                SyntheticArchetypeId.A5MotionAverseThreshold,
                SyntheticFaultScenarioId.R0Nominal,
                EvaluationPolicyCondition.RandomSafe,
                100001);
            var replay = await runner.RunCellAsync(
                bundle,
                "phase-5-replay",
                SyntheticArchetypeId.A5MotionAverseThreshold,
                SyntheticFaultScenarioId.R0Nominal,
                EvaluationPolicyCondition.RandomSafe,
                100001);

            Assert.That(replay.EligibleDecisionCount,
                Is.EqualTo(first.EligibleDecisionCount));
            Assert.That(replay.CumulativePseudoRegret,
                Is.EqualTo(first.CumulativePseudoRegret).Within(1e-12d));
            Assert.That(replay.CumulativeProductionReward,
                Is.EqualTo(first.CumulativeProductionReward).Within(1e-12d));
            Assert.That(replay.ActionEntropyNats,
                Is.EqualTo(first.ActionEntropyNats).Within(1e-12d));
        }
    }
}
