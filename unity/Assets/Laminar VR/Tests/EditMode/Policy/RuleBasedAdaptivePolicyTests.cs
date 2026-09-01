using System;
using LaminarVR.AdaptiveMeditation.Environment;
using LaminarVR.AdaptiveMeditation.Physiology;
using LaminarVR.AdaptiveMeditation.Policy;
using LaminarVR.AdaptiveMeditation.Policy.RuleBased;
using NUnit.Framework;

namespace LaminarVR.AdaptiveMeditation.Tests.EditMode.Policy
{
    public sealed class RuleBasedAdaptivePolicyTests
    {
        [Test]
        public void WorseningTrend_MovesLargestDeltaTowardExplicitPreference()
        {
            var policy = CreatePolicy();
            var preferred = new EnvironmentState(
                0.6f,
                0.9f,
                0.5f,
                0.5f,
                0.5f);

            var decision = policy.SelectAction(
                CreateObservation(preferred, CreateState(0.5f)));

            Assert.That(
                decision.SelectedAction,
                Is.EqualTo(EnvironmentAction.IncreaseWarmth));
            Assert.That(
                decision.ReasonCode,
                Is.EqualTo(
                    RuleBasedAdaptivePolicy.MoveTowardPreferenceReasonCode));
            Assert.That(decision.ExplorationUsed, Is.False);
            Assert.That(decision.ExpectedReward, Is.Null);
        }

        [Test]
        public void ActivatedRule_CanSelectDecreaseWithoutUniversalDirectionAssumption()
        {
            var policy = CreatePolicy();
            var preferred = new EnvironmentState(
                0.5f,
                0.5f,
                0.5f,
                0.5f,
                0.1f);

            var decision = policy.SelectAction(
                CreateObservation(preferred, CreateState(0.5f)));

            Assert.That(
                decision.SelectedAction,
                Is.EqualTo(EnvironmentAction.DecreaseAmbientMotion));
        }

        [Test]
        public void EqualPreferenceDeltas_UseStableDimensionOrder()
        {
            var policy = CreatePolicy();
            var preferred = new EnvironmentState(
                0.8f,
                0.8f,
                0.5f,
                0.5f,
                0.5f);

            var decision = policy.SelectAction(
                CreateObservation(preferred, CreateState(0.5f)));

            Assert.That(
                decision.SelectedAction,
                Is.EqualTo(EnvironmentAction.IncreaseIllumination));
        }

        [Test]
        public void WorseningTrendMode_ReturnsNoChangeWhenTrendUnavailable()
        {
            var policy = CreatePolicy();

            var decision = policy.SelectAction(
                CreateObservationWithoutTrend(
                    CreateState(0.8f),
                    CreateState(0.5f)));

            Assert.That(
                decision.SelectedAction,
                Is.EqualTo(EnvironmentAction.NoChange));
            Assert.That(
                decision.ReasonCode,
                Is.EqualTo(
                    RuleBasedAdaptivePolicy.TrendUnavailableReasonCode));
        }

        [Test]
        public void NonWorseningTrend_ReturnsNoChange()
        {
            var policy = CreatePolicy();
            var observation = CreateObservation(
                CreateState(0.8f),
                CreateState(0.5f),
                stressScores: new[] { 2d, 1.5d, 1d });

            var decision = policy.SelectAction(observation);

            Assert.That(
                decision.SelectedAction,
                Is.EqualTo(EnvironmentAction.NoChange));
            Assert.That(
                decision.ReasonCode,
                Is.EqualTo(
                    RuleBasedAdaptivePolicy.ActivationNotMetReasonCode));
        }

        [Test]
        public void ElevatedStressMode_DoesNotRequireTrend()
        {
            var policy = CreatePolicy(
                RuleActivationMode.ElevatedStress);
            var observation = CreateObservationWithoutTrend(
                CreateState(0.8f),
                CreateState(0.5f),
                continuousStressScore: 2.5d);

            var decision = policy.SelectAction(observation);

            Assert.That(
                decision.SelectedAction,
                Is.EqualTo(EnvironmentAction.IncreaseIllumination));
        }

        [Test]
        public void ActivatedRule_ReturnsNoChangeWhenPreferenceDeltaIsTooSmall()
        {
            var policy = CreatePolicy();
            var decision = policy.SelectAction(
                CreateObservation(CreateState(0.52f), CreateState(0.5f)));

            Assert.That(
                decision.SelectedAction,
                Is.EqualTo(EnvironmentAction.NoChange));
            Assert.That(
                decision.ReasonCode,
                Is.EqualTo(
                    RuleBasedAdaptivePolicy.PreferenceMatchedReasonCode));
        }

        [Test]
        public void ObserveOutcome_TracksEvaluationWithoutModelUpdates()
        {
            var policy = CreatePolicy();
            var decision = policy.SelectAction(
                CreateObservation(CreateState(0.8f), CreateState(0.5f)));
            policy.ObserveOutcome(
                new ActionOutcome(
                    "rule-decision-7",
                    decision,
                    decision.SelectedAction,
                    0.5d,
                    decision.PhysiologySequenceNumber,
                    decision.PhysiologySequenceNumber + 1L));

            var snapshot = policy.CaptureState();

            Assert.That(snapshot.DecisionCount, Is.EqualTo(1L));
            Assert.That(snapshot.ObservedOutcomeCount, Is.EqualTo(1L));
            Assert.That(snapshot.ModelUpdateCount, Is.Zero);
            Assert.That(
                snapshot.PolicyVersion,
                Does.Contain("rule-test/1"));
        }

        [Test]
        public void ObserveOutcome_RejectsDifferentRuleConfiguration()
        {
            var first = CreatePolicy();
            var second = new RuleBasedAdaptivePolicy(
                RuleBasedPolicyConfigurationTests.CreateConfiguration(
                    configurationId: "other-rules"));
            var otherDecision = second.SelectAction(
                CreateObservation(CreateState(0.8f), CreateState(0.5f)));
            var otherOutcome = new ActionOutcome(
                "other-rule-decision",
                otherDecision,
                otherDecision.SelectedAction,
                0d,
                otherDecision.PhysiologySequenceNumber,
                otherDecision.PhysiologySequenceNumber + 1L);

            Assert.Throws<ArgumentException>(
                () => first.ObserveOutcome(otherOutcome));
        }

        [Test]
        public void Reset_ClearsCountersAndPreservesRuleIdentity()
        {
            var policy = CreatePolicy();
            policy.SelectAction(
                CreateObservation(CreateState(0.8f), CreateState(0.5f)));

            policy.Reset(
                new PolicyResetContext(PolicyResetReason.NewSession));
            var snapshot = policy.CaptureState();

            Assert.That(snapshot.DecisionCount, Is.Zero);
            Assert.That(snapshot.ObservedOutcomeCount, Is.Zero);
            Assert.That(snapshot.ModelUpdateCount, Is.Zero);
            Assert.That(
                snapshot.PolicyId,
                Is.EqualTo(RuleBasedAdaptivePolicy.RulePolicyId));
        }

        private static RuleBasedAdaptivePolicy CreatePolicy(
            RuleActivationMode activationMode =
                RuleActivationMode.WorseningStressTrend)
        {
            return new RuleBasedAdaptivePolicy(
                RuleBasedPolicyConfigurationTests.CreateConfiguration(
                    activationMode));
        }

        private static PolicyObservation CreateObservation(
            EnvironmentState preferred,
            EnvironmentState current,
            double[] stressScores = null)
        {
            var scores = stressScores ?? new[] { 1d, 2d, 3d };
            var snapshots = new[]
            {
                CreateSnapshot(5L, 1000d, scores[0], 0.9d),
                CreateSnapshot(6L, 1060d, scores[1], 0.9d),
                CreateSnapshot(7L, 1120d, scores[2], 0.9d)
            };
            var trend = PhysiologyTrendCalculator.Calculate(snapshots, 3);
            return new PolicyObservation(
                snapshots[2],
                preferred,
                current,
                CreateState(0.5f),
                trend);
        }

        private static PolicyObservation CreateObservationWithoutTrend(
            EnvironmentState preferred,
            EnvironmentState current,
            double continuousStressScore = 1.8d)
        {
            return new PolicyObservation(
                CreateSnapshot(7L, 1120d, continuousStressScore, 0.9d),
                preferred,
                current,
                CreateState(0.5f));
        }

        private static PhysiologyWindowSnapshot CreateSnapshot(
            long sequenceNumber,
            double windowEndUtcUnixSeconds,
            double stressScore,
            double signalQuality)
        {
            return new PhysiologyWindowSnapshot(
                sequenceNumber,
                new PhysiologyWindow(
                    windowEndUtcUnixSeconds,
                    windowEndUtcUnixSeconds - 60d,
                    windowEndUtcUnixSeconds,
                    78d,
                    34d,
                    42d,
                    new StressDecision(
                        StressDecisionMode.Point,
                        2,
                        null,
                        null,
                        "moderate",
                        0.6d,
                        false,
                        new StressProbabilityVector(
                            0.1d,
                            0.2d,
                            0.6d,
                            0.1d),
                        stressScore),
                    signalQuality),
                0d,
                sequenceNumber);
        }

        private static EnvironmentState CreateState(float value)
        {
            return new EnvironmentState(value, value, value, value, value);
        }
    }
}
