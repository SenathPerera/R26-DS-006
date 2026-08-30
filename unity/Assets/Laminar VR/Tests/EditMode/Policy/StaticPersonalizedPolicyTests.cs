using System;
using LaminarVR.AdaptiveMeditation.Environment;
using LaminarVR.AdaptiveMeditation.Policy;
using LaminarVR.AdaptiveMeditation.Policy.Static;
using NUnit.Framework;

namespace LaminarVR.AdaptiveMeditation.Tests.EditMode.Policy
{
    public sealed class StaticPersonalizedPolicyTests
    {
        [Test]
        public void Identity_IsStableThroughCommonPolicyInterface()
        {
            IEnvironmentPolicy policy = new StaticPersonalizedPolicy();

            Assert.That(
                policy.PolicyId,
                Is.EqualTo(StaticPersonalizedPolicy.StaticPolicyId));
            Assert.That(
                policy.PolicyVersion,
                Is.EqualTo(StaticPersonalizedPolicy.StaticPolicyVersion));
        }

        [Test]
        public void SelectAction_AlwaysReturnsNoChangeWithoutExplorationOrScore()
        {
            var policy = new StaticPersonalizedPolicy();
            var observation = CreateObservation(
                preferredValue: 0.2f,
                currentValue: 0.8f);

            var decision = policy.SelectAction(observation);

            Assert.That(
                decision.SelectedAction,
                Is.EqualTo(EnvironmentAction.NoChange));
            Assert.That(
                decision.PhysiologySequenceNumber,
                Is.EqualTo(observation.Physiology.SequenceNumber));
            Assert.That(
                decision.ReasonCode,
                Is.EqualTo(
                    StaticPersonalizedPolicy.NoAdaptationReasonCode));
            Assert.That(decision.ExpectedReward, Is.Null);
            Assert.That(decision.Uncertainty, Is.Null);
            Assert.That(decision.ExplorationUsed, Is.False);
            Assert.That(decision.FeatureVector, Is.Null);
            Assert.That(decision.CandidateScoreCount, Is.Zero);
        }

        [Test]
        public void CaptureState_RecordsDecisionAndOutcomeCountsWithoutLearning()
        {
            var policy = new StaticPersonalizedPolicy();
            var decision = policy.SelectAction(CreateObservation());
            policy.SelectAction(CreateObservation());
            policy.ObserveOutcome(CreateOutcome(decision));

            var snapshot = policy.CaptureState();

            Assert.That(snapshot.PolicyId, Is.EqualTo(policy.PolicyId));
            Assert.That(snapshot.PolicyVersion, Is.EqualTo(policy.PolicyVersion));
            Assert.That(
                snapshot.StateSchemaVersion,
                Is.EqualTo(
                    StaticPersonalizedPolicy.StaticStateSchemaVersion));
            Assert.That(snapshot.DecisionCount, Is.EqualTo(2L));
            Assert.That(snapshot.ObservedOutcomeCount, Is.EqualTo(1L));
            Assert.That(snapshot.ModelUpdateCount, Is.Zero);
        }

        [Test]
        public void ObserveOutcome_RejectsDecisionFromAnotherPolicy()
        {
            var policy = new StaticPersonalizedPolicy();
            var otherDecision = PolicyContractTests.CreateDecision();
            var otherOutcome = CreateOutcome(otherDecision);

            Assert.Throws<ArgumentException>(
                () => policy.ObserveOutcome(otherOutcome));
            Assert.That(
                policy.CaptureState().ObservedOutcomeCount,
                Is.Zero);
        }

        [Test]
        public void ObserveOutcome_RejectsAnyEnvironmentChange()
        {
            var policy = new StaticPersonalizedPolicy();
            var staticDecision = policy.SelectAction(CreateObservation());
            var changedOutcome = new ActionOutcome(
                "decision-static-7",
                staticDecision,
                EnvironmentAction.IncreaseWarmth,
                reward: 0d,
                preWindowSequenceNumber:
                    staticDecision.PhysiologySequenceNumber,
                postWindowSequenceNumber:
                    staticDecision.PhysiologySequenceNumber + 1L);

            Assert.Throws<ArgumentException>(
                () => policy.ObserveOutcome(changedOutcome));
            Assert.That(
                policy.CaptureState().ObservedOutcomeCount,
                Is.Zero);
        }

        [Test]
        public void Reset_ClearsSessionCountersWithoutChangingIdentity()
        {
            var policy = new StaticPersonalizedPolicy();
            var decision = policy.SelectAction(CreateObservation());
            policy.ObserveOutcome(CreateOutcome(decision));

            policy.Reset(
                new PolicyResetContext(PolicyResetReason.NewSession));
            var snapshot = policy.CaptureState();

            Assert.That(snapshot.PolicyId, Is.EqualTo(policy.PolicyId));
            Assert.That(snapshot.DecisionCount, Is.Zero);
            Assert.That(snapshot.ObservedOutcomeCount, Is.Zero);
            Assert.That(snapshot.ModelUpdateCount, Is.Zero);
        }

        [Test]
        public void PublicOperations_RejectMissingRequiredInputs()
        {
            var policy = new StaticPersonalizedPolicy();

            Assert.Throws<ArgumentNullException>(
                () => policy.SelectAction(null));
            Assert.Throws<ArgumentNullException>(
                () => policy.ObserveOutcome(null));
            Assert.Throws<ArgumentNullException>(
                () => policy.Reset(null));
        }

        private static PolicyObservation CreateObservation(
            float preferredValue = 0.4f,
            float currentValue = 0.4f)
        {
            return new PolicyObservation(
                PolicyObservationTests.CreateSnapshot(),
                PolicyObservationTests.CreateState(preferredValue),
                PolicyObservationTests.CreateState(currentValue),
                PolicyObservationTests.CreateState(0.5f));
        }

        private static ActionOutcome CreateOutcome(PolicyDecision decision)
        {
            return new ActionOutcome(
                "decision-static-7",
                decision,
                EnvironmentAction.NoChange,
                reward: 0.5d,
                preWindowSequenceNumber: decision.PhysiologySequenceNumber,
                postWindowSequenceNumber:
                    decision.PhysiologySequenceNumber + 1L);
        }
    }
}
