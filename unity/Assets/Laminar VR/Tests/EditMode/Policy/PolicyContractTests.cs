using System;
using LaminarVR.AdaptiveMeditation.Environment;
using LaminarVR.AdaptiveMeditation.Policy;
using NUnit.Framework;

namespace LaminarVR.AdaptiveMeditation.Tests.EditMode.Policy
{
    public sealed class PolicyContractTests
    {
        [Test]
        public void PolicyDecision_CopiesCandidateScoresAndIdentity()
        {
            var source = new[]
            {
                new PolicyCandidateScore(
                    EnvironmentAction.NoChange,
                    0.25d,
                    0.1d)
            };
            var decision = CreateDecision(candidateScores: source);

            source[0] = new PolicyCandidateScore(
                EnvironmentAction.IncreaseWarmth,
                1d,
                0d);
            var copied = decision.CopyCandidateScores();
            copied[0] = source[0];

            Assert.That(decision.PolicyId, Is.EqualTo("test-policy"));
            Assert.That(decision.PolicyVersion, Is.EqualTo("1.0.0"));
            Assert.That(decision.CandidateScoreCount, Is.EqualTo(1));
            Assert.That(
                decision.GetCandidateScore(0).Action,
                Is.EqualTo(EnvironmentAction.NoChange));
        }

        [Test]
        public void PolicyDecision_RejectsInvalidIdentityAndNonFiniteScores()
        {
            Assert.Throws<ArgumentException>(
                () => CreateDecision(policyId: " "));
            Assert.Catch<ArgumentException>(
                () => CreateDecision(expectedReward: double.NaN));
            Assert.Catch<ArgumentException>(
                () => CreateDecision(uncertainty: -0.1d));
        }

        [Test]
        public void ActionOutcome_PreservesExecutedActionAndValidRewardWindow()
        {
            var decision = CreateDecision();
            var outcome = new ActionOutcome(
                "decision-7",
                decision,
                EnvironmentAction.DecreaseWarmth,
                0.75d,
                preWindowSequenceNumber: 7L,
                postWindowSequenceNumber: 8L);

            Assert.That(outcome.Decision, Is.SameAs(decision));
            Assert.That(outcome.DecisionId, Is.EqualTo("decision-7"));
            Assert.That(
                outcome.ExecutedAction,
                Is.EqualTo(EnvironmentAction.DecreaseWarmth));
            Assert.That(outcome.Reward, Is.EqualTo(0.75d));
        }

        [Test]
        public void ActionOutcome_RejectsMismatchedOrNonIncreasingWindows()
        {
            var decision = CreateDecision();

            Assert.Catch<ArgumentException>(
                () => new ActionOutcome(
                    "decision-7",
                    decision,
                    EnvironmentAction.NoChange,
                    0d,
                    preWindowSequenceNumber: 6L,
                    postWindowSequenceNumber: 8L));
            Assert.Catch<ArgumentException>(
                () => new ActionOutcome(
                    "decision-7",
                    decision,
                    EnvironmentAction.NoChange,
                    0d,
                    preWindowSequenceNumber: 7L,
                    postWindowSequenceNumber: 7L));
            Assert.Throws<ArgumentException>(
                () => new ActionOutcome(
                    " ",
                    decision,
                    EnvironmentAction.NoChange,
                    0d,
                    preWindowSequenceNumber: 7L,
                    postWindowSequenceNumber: 8L));
        }

        [Test]
        public void PolicyStateSnapshot_RejectsNegativeCounters()
        {
            Assert.Catch<ArgumentException>(
                () => new PolicyStateSnapshot(
                    "test-policy",
                    "1.0.0",
                    "test-state/1.0",
                    decisionCount: -1L,
                    observedOutcomeCount: 0L,
                    modelUpdateCount: 0L));
        }

        internal static PolicyDecision CreateDecision(
            string policyId = "test-policy",
            double? expectedReward = 0.25d,
            double? uncertainty = 0.1d,
            PolicyCandidateScore[] candidateScores = null)
        {
            return new PolicyDecision(
                policyId,
                "1.0.0",
                EnvironmentAction.NoChange,
                physiologySequenceNumber: 7L,
                "TEST_REASON",
                expectedReward,
                uncertainty,
                explorationUsed: false,
                featureVector: null,
                candidateScores
                    ?? Array.Empty<PolicyCandidateScore>());
        }
    }
}
