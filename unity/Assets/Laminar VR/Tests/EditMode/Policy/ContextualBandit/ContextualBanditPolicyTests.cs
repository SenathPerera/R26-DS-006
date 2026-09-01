using System;
using LaminarVR.AdaptiveMeditation.Environment;
using LaminarVR.AdaptiveMeditation.Policy;
using LaminarVR.AdaptiveMeditation.Policy.ContextualBandit;
using LaminarVR.AdaptiveMeditation.Policy.Static;
using NUnit.Framework;

namespace LaminarVR.AdaptiveMeditation.Tests.EditMode.Policy.ContextualBandit
{
    public sealed class ContextualBanditPolicyTests
    {
        [Test]
        public void SelectAction_BuildsOneFeatureVectorAndReturnsAllScores()
        {
            var builder = new CountingFeatureVectorBuilder();
            var model = CreateModel(builder, alpha: 0.2d);
            var policy = new ContextualBanditPolicy(builder, model);
            var observation = CreateObservation();

            var decision = policy.SelectAction(observation);

            Assert.That(builder.BuildCount, Is.EqualTo(1));
            Assert.That(decision.FeatureVector, Is.Not.Null);
            Assert.That(
                decision.FeatureVector.SchemaVersion,
                Is.EqualTo(builder.FeatureSchemaVersion));
            Assert.That(decision.CandidateScoreCount, Is.EqualTo(2));
            Assert.That(
                decision.GetCandidateScore(0).ExpectedReward,
                Is.Not.Null);
            Assert.That(
                decision.GetCandidateScore(0).ExplorationBonus,
                Is.Not.Null);
            Assert.That(
                decision.ReasonCode,
                Is.EqualTo(
                    ContextualBanditPolicy.LinUcbSelectionReasonCode));
            Assert.That(decision.ExpectedReward, Is.Not.Null);
            Assert.That(decision.Uncertainty, Is.Not.Null);
            Assert.That(decision.ExplorationUsed, Is.True);
        }

        [Test]
        public void LearnedWarmthResponse_SelectsWarmthReproducibly()
        {
            var builder = new PolicyFeatureVectorBuilder();
            var model = CreateModel(builder, alpha: 0d);
            var observation = CreateObservation();
            var features = builder.Build(observation);
            model.Update(
                EnvironmentAction.IncreaseWarmth,
                features,
                2d);
            var policy = new ContextualBanditPolicy(builder, model);

            var first = policy.SelectAction(observation);
            var second = policy.SelectAction(observation);

            Assert.That(
                first.SelectedAction,
                Is.EqualTo(EnvironmentAction.IncreaseWarmth));
            Assert.That(second.SelectedAction, Is.EqualTo(first.SelectedAction));
            Assert.That(first.ExpectedReward, Is.GreaterThan(0d));
            Assert.That(first.ExplorationUsed, Is.False);
        }

        [Test]
        public void ObserveOutcome_UpdatesOnlyExecutedActionUsingDecisionFeatures()
        {
            var builder = new PolicyFeatureVectorBuilder();
            var model = CreateModel(builder, alpha: 0d);
            var observation = CreateObservation();
            model.Update(
                EnvironmentAction.IncreaseWarmth,
                builder.Build(observation),
                2d);
            var policy = new ContextualBanditPolicy(builder, model);
            var decision = policy.SelectAction(observation);
            var selectedBefore = model.CaptureArmState(
                EnvironmentAction.IncreaseWarmth).UpdateCount;
            var executedBefore = model.CaptureArmState(
                EnvironmentAction.DecreaseIllumination).UpdateCount;

            policy.ObserveOutcome(
                new ActionOutcome(
                    "contextual-outcome",
                    decision,
                    EnvironmentAction.DecreaseIllumination,
                    0.75d,
                    decision.PhysiologySequenceNumber,
                    decision.PhysiologySequenceNumber + 1L));

            Assert.That(
                model.CaptureArmState(
                    EnvironmentAction.IncreaseWarmth).UpdateCount,
                Is.EqualTo(selectedBefore));
            Assert.That(
                model.CaptureArmState(
                    EnvironmentAction.DecreaseIllumination).UpdateCount,
                Is.EqualTo(executedBefore + 1L));
            Assert.That(policy.CaptureState().ObservedOutcomeCount, Is.EqualTo(1L));
        }

        [Test]
        public void ObserveOutcome_RejectsDecisionFromDifferentPolicy()
        {
            var builder = new PolicyFeatureVectorBuilder();
            var policy = new ContextualBanditPolicy(
                builder,
                CreateModel(builder, alpha: 0d));
            var staticPolicy = new StaticPersonalizedPolicy();
            var staticDecision = staticPolicy.SelectAction(CreateObservation());

            Assert.Throws<ArgumentException>(
                () => policy.ObserveOutcome(
                    new ActionOutcome(
                        "wrong-policy",
                        staticDecision,
                        EnvironmentAction.NoChange,
                        0d,
                        staticDecision.PhysiologySequenceNumber,
                        staticDecision.PhysiologySequenceNumber + 1L)));
        }

        [Test]
        public void SelectAction_RejectsObservationWithoutCandidateFiltering()
        {
            var builder = new PolicyFeatureVectorBuilder();
            var policy = new ContextualBanditPolicy(
                builder,
                CreateModel(builder, alpha: 0d));
            var environment = PolicyObservationTests.CreateState(0.5f);
            var observation = new PolicyObservation(
                PolicyObservationTests.CreateSnapshot(),
                environment,
                environment,
                environment);

            Assert.Throws<ArgumentException>(
                () => policy.SelectAction(observation));
        }

        [Test]
        public void Reset_ClearsPolicyAndModelState()
        {
            var builder = new PolicyFeatureVectorBuilder();
            var model = CreateModel(builder, alpha: 0d);
            var policy = new ContextualBanditPolicy(builder, model);
            var observation = CreateObservation();
            var decision = policy.SelectAction(observation);
            policy.ObserveOutcome(
                new ActionOutcome(
                    "reset-outcome",
                    decision,
                    decision.SelectedAction,
                    0.25d,
                    decision.PhysiologySequenceNumber,
                    decision.PhysiologySequenceNumber + 1L));

            policy.Reset(
                new PolicyResetContext(PolicyResetReason.NewSession));
            var state = policy.CaptureState();

            Assert.That(state.DecisionCount, Is.Zero);
            Assert.That(state.ObservedOutcomeCount, Is.Zero);
            Assert.That(state.ModelUpdateCount, Is.Zero);
            Assert.That(model.TotalUpdateCount, Is.Zero);
        }

        private static DisjointLinUcbModel CreateModel(
            IFeatureVectorBuilder builder,
            double alpha)
        {
            return new DisjointLinUcbModel(
                new LinUcbModelConfiguration(
                    "contextual-policy-test",
                    1,
                    builder.FeatureSchemaVersion,
                    builder.FeatureCount,
                    1d,
                    alpha));
        }

        private static PolicyObservation CreateObservation()
        {
            var current = PolicyObservationTests.CreateState(0.5f);
            var preferred = new EnvironmentState(
                0.5f,
                0.8f,
                0.5f,
                0.5f,
                0.5f);
            return new PolicyObservation(
                PolicyObservationTests.CreateSnapshot(),
                preferred,
                current,
                current,
                null,
                new[]
                {
                    new PolicyActionCandidate(
                        EnvironmentAction.NoChange,
                        0d),
                    new PolicyActionCandidate(
                        EnvironmentAction.IncreaseWarmth,
                        0.1d)
                });
        }

        private sealed class CountingFeatureVectorBuilder
            : IFeatureVectorBuilder
        {
            private readonly PolicyFeatureVectorBuilder inner =
                new PolicyFeatureVectorBuilder();

            public int BuildCount { get; private set; }

            public int FeatureCount => inner.FeatureCount;

            public string FeatureSchemaVersion =>
                inner.FeatureSchemaVersion;

            public string GetFeatureName(int index)
            {
                return inner.GetFeatureName(index);
            }

            public FeatureVector Build(PolicyObservation observation)
            {
                BuildCount++;
                return inner.Build(observation);
            }
        }
    }
}
