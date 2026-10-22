using System;
using LaminarVR.AdaptiveMeditation.Policy.ContextualBandit;
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

    public static class StudyConditionCodec
    {
        public const string StaticPersonalized = "StaticPersonalized";
        public const string RuleBasedAdaptive = "RuleBasedAdaptive";
        public const string ContextualBandit = "ContextualBandit";

        public static bool TryParse(
            string value,
            out StudyPolicyMode mode)
        {
            switch (value)
            {
                case StaticPersonalized:
                    mode = StudyPolicyMode.StaticPersonalized;
                    return true;
                case RuleBasedAdaptive:
                    mode = StudyPolicyMode.RuleBasedAdaptive;
                    return true;
                case ContextualBandit:
                    mode = StudyPolicyMode.ContextualBandit;
                    return true;
                default:
                    mode = default;
                    return false;
            }
        }

        public static string ToWireValue(StudyPolicyMode mode)
        {
            switch (mode)
            {
                case StudyPolicyMode.StaticPersonalized:
                    return StaticPersonalized;
                case StudyPolicyMode.RuleBasedAdaptive:
                    return RuleBasedAdaptive;
                case StudyPolicyMode.ContextualBandit:
                    return ContextualBandit;
                default:
                    throw new ArgumentOutOfRangeException(nameof(mode));
            }
        }
    }

    public static class StudyPolicyFactory
    {
        public static bool TryCreate(
            StudyPolicyMode mode,
            RuleBasedPolicyConfiguration ruleBasedConfiguration,
            out IEnvironmentPolicy policy,
            out StudyPolicyCreationResultCode resultCode)
        {
            return TryCreate(
                mode,
                ruleBasedConfiguration,
                null,
                null,
                out policy,
                out resultCode);
        }

        public static bool TryCreate(
            StudyPolicyMode mode,
            RuleBasedPolicyConfiguration ruleBasedConfiguration,
            LinUcbModelConfiguration linUcbConfiguration,
            IFeatureVectorBuilder featureVectorBuilder,
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
                    if (linUcbConfiguration == null
                        || featureVectorBuilder == null)
                    {
                        policy = null;
                        resultCode = StudyPolicyCreationResultCode
                            .ConfigurationRequired;
                        return false;
                    }

                    policy = new ContextualBanditPolicy(
                        featureVectorBuilder,
                        new DisjointLinUcbModel(linUcbConfiguration));
                    resultCode = StudyPolicyCreationResultCode.Created;
                    return true;
                default:
                    policy = null;
                    resultCode = StudyPolicyCreationResultCode.UnsupportedMode;
                    return false;
            }
        }
    }
}
