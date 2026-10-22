using System;
using LaminarVR.AdaptiveMeditation.Environment;
using LaminarVR.AdaptiveMeditation.Policy;
using LaminarVR.AdaptiveMeditation.Policy.ContextualBandit;

namespace LaminarVR.AdaptiveMeditation.ResearchEvaluation
{
    public sealed class EvaluationSessionRequest
    {
        public EvaluationSessionRequest(
            EvaluationRuntimeBundle bundle,
            string sessionId,
            string participantPseudonym,
            StudyPolicyMode policyMode,
            EnvironmentState preferredEnvironment,
            ISyntheticPhysiologyModel physiologyModel,
            double baseUtcUnixSeconds,
            IEvaluationNetworkSchedule networkSchedule = null)
            : this(
                bundle,
                sessionId,
                participantPseudonym,
                EvaluationPolicyConditionCodec.FromProductionMode(policyMode),
                preferredEnvironment,
                physiologyModel,
                baseUtcUnixSeconds,
                networkSchedule,
                null)
        {
        }

        public EvaluationSessionRequest(
            EvaluationRuntimeBundle bundle,
            string sessionId,
            string participantPseudonym,
            EvaluationPolicyCondition policyCondition,
            EnvironmentState preferredEnvironment,
            ISyntheticPhysiologyModel physiologyModel,
            double baseUtcUnixSeconds,
            IEvaluationNetworkSchedule networkSchedule = null,
            Pcg32 randomSafeActionStream = null,
            LinUcbModelSnapshot priorModelSnapshot = null,
            string protocolId = null,
            string profileBundleId = null)
        {
            Bundle = bundle ?? throw new ArgumentNullException(nameof(bundle));
            SessionId = RequireText(sessionId, nameof(sessionId));
            ParticipantPseudonym = RequireText(
                participantPseudonym,
                nameof(participantPseudonym));
            if (!Enum.IsDefined(
                    typeof(EvaluationPolicyCondition),
                    policyCondition))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(policyCondition));
            }

            if (policyCondition == EvaluationPolicyCondition.RandomSafe
                && randomSafeActionStream == null)
            {
                throw new ArgumentNullException(
                    nameof(randomSafeActionStream),
                    "Random Safe requires its dedicated PRNG stream.");
            }

            if (priorModelSnapshot != null
                && policyCondition != EvaluationPolicyCondition.ContextualBandit)
            {
                throw new ArgumentException(
                    "Only LinUCB sessions can restore a model snapshot.",
                    nameof(priorModelSnapshot));
            }

            if (!preferredEnvironment.IsNormalized)
            {
                throw new ArgumentException(
                    "The preference must be normalized.",
                    nameof(preferredEnvironment));
            }

            if (double.IsNaN(baseUtcUnixSeconds)
                || double.IsInfinity(baseUtcUnixSeconds)
                || baseUtcUnixSeconds < 0d)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(baseUtcUnixSeconds));
            }

            PolicyCondition = policyCondition;
            PreferredEnvironment = preferredEnvironment;
            PhysiologyModel = physiologyModel ?? throw new ArgumentNullException(
                nameof(physiologyModel));
            BaseUtcUnixSeconds = baseUtcUnixSeconds;
            NetworkSchedule = networkSchedule
                ?? AlwaysConnectedEvaluationNetworkSchedule.Instance;
            RandomSafeActionStream = randomSafeActionStream;
            PriorModelSnapshot = priorModelSnapshot;
            ProtocolId = protocolId ?? EvaluationRuntimeBundle.ProtocolId;
            ProfileBundleId = profileBundleId
                ?? EvaluationRuntimeBundle.PilotBundleId;
        }

        public EvaluationRuntimeBundle Bundle { get; }

        public string SessionId { get; }

        public string ParticipantPseudonym { get; }

        public EvaluationPolicyCondition PolicyCondition { get; }

        public EnvironmentState PreferredEnvironment { get; }

        public ISyntheticPhysiologyModel PhysiologyModel { get; }

        public double BaseUtcUnixSeconds { get; }

        public IEvaluationNetworkSchedule NetworkSchedule { get; }

        public Pcg32 RandomSafeActionStream { get; }

        public LinUcbModelSnapshot PriorModelSnapshot { get; }

        public string ProtocolId { get; }

        public string ProfileBundleId { get; }

        public IEnvironmentPolicy CreatePolicy()
        {
            if (EvaluationPolicyConditionCodec.TryGetProductionMode(
                    PolicyCondition,
                    out var productionMode))
            {
                return Bundle.CreatePolicy(productionMode);
            }

            return new RandomSafeEvaluationPolicy(RandomSafeActionStream);
        }

        private static string RequireText(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException(
                    "A non-empty value is required.",
                    parameterName);
            }

            return value.Trim();
        }
    }
}
