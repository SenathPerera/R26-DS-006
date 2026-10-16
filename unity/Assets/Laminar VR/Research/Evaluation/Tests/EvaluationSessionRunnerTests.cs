using System.Threading.Tasks;
using LaminarVR.AdaptiveMeditation.Environment;
using LaminarVR.AdaptiveMeditation.Policy;
using NUnit.Framework;

namespace LaminarVR.AdaptiveMeditation.ResearchEvaluation.Tests
{
    public sealed class EvaluationSessionRunnerTests
    {
        [Test]
        public async Task RunAsync_NominalContextualBandit_CompletesSafely()
        {
            Assert.That(
                FrozenEvaluationProfileLoader.TryLoad(
                    out var bundle,
                    out var validationError),
                Is.True,
                validationError);
            var request = new EvaluationSessionRequest(
                bundle,
                "phase-2-test-session",
                "synthetic-test",
                StudyPolicyMode.ContextualBandit,
                new EnvironmentState(0.45f, 0.65f, 0.45f, 0.55f, 0.35f),
                new Phase2SmokePhysiologyModel(),
                1700000000d);

            var result = await new EvaluationSessionRunner().RunAsync(request);

            Assert.That(result.BaselineAvailable, Is.True);
            Assert.That(
                result.Decisions.Length,
                Is.EqualTo(bundle.DecisionOpportunityCount));
            Assert.That(result.RewardAppliedCount, Is.GreaterThan(0));
            Assert.That(
                result.PolicyState.ModelUpdateCount,
                Is.GreaterThan(0));
            Assert.That(result.AllAppliedStatesSafe, Is.True);
            Assert.That(result.TelemetryEvents, Is.Not.Empty);
            Assert.That(
                result.Decisions[0].Result.Decision.PhysiologySequenceNumber,
                Is.EqualTo(3L),
                "The 180-second window must feed the first decision.");
            Assert.That(
                result.Decisions[3].Result.Decision.PhysiologySequenceNumber,
                Is.EqualTo(7L),
                "The 420-second window must precede its same-time decision.");
            Assert.That(
                bundle.Scene.Limits.Contains(
                    result.SelectedStabilizationState),
                Is.True);
        }
    }
}
