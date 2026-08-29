using LaminarVR.AdaptiveMeditation.Policy.RuleBased;
using LaminarVR.AdaptiveMeditation.Policy.Static;

namespace LaminarVR.AdaptiveMeditation.Policy
{
    public enum StudyPolicyMode
    {
        StaticPersonalized,
        RuleBasedAdaptive,
        ContextualBandit
    }

    public enum StudyPolicyCreationResultCode
    {
        Created,
        ConfigurationRequired,
        NotImplemented,
        UnsupportedMode
    }

    public static class StudyPolicyFactory
    {
        public static bool TryCreate(
            StudyPolicyMode mode,
            RuleBasedPolicyConfiguration ruleBasedConfiguration,
            out IEnvironmentPolicy policy,
            out StudyPolicyCreationResultCode resultCode)
        {
            switch (mode)
            {
                case StudyPolicyMode.StaticPersonalized:
                    policy = new StaticPersonalizedPolicy();
                    resultCode = StudyPolicyCreationResultCode.Created;
                    return true;
                case StudyPolicyMode.RuleBasedAdaptive:
                    if (ruleBasedConfiguration == null)
                    {
                        policy = null;
                        resultCode = StudyPolicyCreationResultCode
                            .ConfigurationRequired;
                        return false;
                    }

                    policy = new RuleBasedAdaptivePolicy(
                        ruleBasedConfiguration);
                    resultCode = StudyPolicyCreationResultCode.Created;
                    return true;
                case StudyPolicyMode.ContextualBandit:
                    policy = null;
                    resultCode = StudyPolicyCreationResultCode.NotImplemented;
                    return false;
                default:
                    policy = null;
                    resultCode = StudyPolicyCreationResultCode.UnsupportedMode;
                    return false;
            }
        }
    }
}
