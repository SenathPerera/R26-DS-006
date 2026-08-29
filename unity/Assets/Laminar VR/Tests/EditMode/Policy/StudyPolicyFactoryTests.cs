using LaminarVR.AdaptiveMeditation.Policy;
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
        public void TryCreate_ContextualBanditRemainsUnavailableUntilStep10()
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
                Is.EqualTo(StudyPolicyCreationResultCode.NotImplemented));
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
