using System;
using LaminarVR.AdaptiveMeditation.Policy;
using LaminarVR.AdaptiveMeditation.Policy.ContextualBandit;
using LaminarVR.AdaptiveMeditation.Policy.RuleBased;
using LaminarVR.AdaptiveMeditation.Policy.Static;
using NUnit.Framework;

namespace LaminarVR.AdaptiveMeditation.Tests.EditMode.Policy
{
    public sealed class StudyPolicyFactoryTests
    {
        [Test]
        public void TryCreate_CreatesStaticAndRuleBasedPolicies()
        {
            var staticCreated = StudyPolicyFactory.TryCreate(
                StudyPolicyMode.StaticPersonalized,
                null,
                out var staticPolicy,
                out var staticCode);
            var ruleCreated = StudyPolicyFactory.TryCreate(
                StudyPolicyMode.RuleBasedAdaptive,
                CreateRuleConfiguration(),
                out var rulePolicy,
                out var ruleCode);

            Assert.That(staticCreated, Is.True);
            Assert.That(staticCode, Is.EqualTo(StudyPolicyCreationResultCode.Created));
            Assert.That(staticPolicy, Is.TypeOf<StaticPersonalizedPolicy>());
            Assert.That(ruleCreated, Is.True);
            Assert.That(ruleCode, Is.EqualTo(StudyPolicyCreationResultCode.Created));
            Assert.That(rulePolicy, Is.TypeOf<RuleBasedAdaptivePolicy>());
        }

        [Test]
        public void TryCreate_RuleBasedFailsClosedWithoutConfiguration()
        {
            var created = StudyPolicyFactory.TryCreate(
                StudyPolicyMode.RuleBasedAdaptive,
                null,
                out var policy,
                out var code);

            Assert.That(created, Is.False);
            Assert.That(policy, Is.Null);
            Assert.That(
                code,
                Is.EqualTo(
                    StudyPolicyCreationResultCode.ConfigurationRequired));
        }

        [Test]
        public void TryCreate_ContextualBanditFailsClosedWithoutConfiguration()
        {
            var created = StudyPolicyFactory.TryCreate(
                StudyPolicyMode.ContextualBandit,
                null,
                out var policy,
                out var code);

            Assert.That(created, Is.False);
            Assert.That(policy, Is.Null);
            Assert.That(
                code,
                Is.EqualTo(
                    StudyPolicyCreationResultCode.ConfigurationRequired));
        }

        [Test]
        public void TryCreate_CreatesConfiguredContextualBanditPolicy()
        {
            var builder = new PolicyFeatureVectorBuilder();

            var created = StudyPolicyFactory.TryCreate(
                StudyPolicyMode.ContextualBandit,
                null,
                new LinUcbModelConfiguration(
                    "factory-linucb",
                    1,
                    builder.FeatureSchemaVersion,
                    builder.FeatureCount,
                    1d,
                    0.1d),
                builder,
                out var policy,
                out var code);

            Assert.That(created, Is.True);
            Assert.That(code, Is.EqualTo(StudyPolicyCreationResultCode.Created));
            Assert.That(policy, Is.TypeOf<ContextualBanditPolicy>());
        }

        [TestCase("StaticPersonalized", StudyPolicyMode.StaticPersonalized)]
        [TestCase("RuleBasedAdaptive", StudyPolicyMode.RuleBasedAdaptive)]
        [TestCase("ContextualBandit", StudyPolicyMode.ContextualBandit)]
        public void StudyConditionCodec_RoundTripsApprovedWireValues(
            string wireValue,
            StudyPolicyMode expectedMode)
        {
            Assert.That(
                StudyConditionCodec.TryParse(wireValue, out var mode),
                Is.True);
            Assert.That(mode, Is.EqualTo(expectedMode));
            Assert.That(
                StudyConditionCodec.ToWireValue(mode),
                Is.EqualTo(wireValue));
        }

        [TestCase("")]
        [TestCase("contextual_bandit")]
        [TestCase("ExperimentalPolicy")]
        public void StudyConditionCodec_RejectsNonContractValues(
            string wireValue)
        {
            Assert.That(
                StudyConditionCodec.TryParse(wireValue, out _),
                Is.False);
        }

        [Test]
        public void StudyConditionCodec_RejectsUndefinedMode()
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => StudyConditionCodec.ToWireValue((StudyPolicyMode)99));
        }

        private static RuleBasedPolicyConfiguration CreateRuleConfiguration()
        {
            return new RuleBasedPolicyConfiguration(
                "factory-rule-test",
                1,
                RuleActivationMode.WorseningStressTrend,
                2d,
                0.1d,
                0.05d);
        }
    }
}
