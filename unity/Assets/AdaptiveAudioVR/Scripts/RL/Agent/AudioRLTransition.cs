using System;

namespace AdaptiveAudioVR.RL.Agent
{
    [Serializable]
    public sealed class AudioRLTransition
    {
        public string sessionId;
        public string userId;
        public string createdUtc;
        public AudioRLState state;
        public AudioRLAction ruleBaselineAction;
        public AudioRLAction residualPolicyAction;
        public AudioRLAction finalSafeAction;
        public AudioRLRewardBreakdown reward;
        public AudioRLState nextState;
        public string policyMode;
        public string policyStatus;
        public string controllerMode;
        public string safetyMode;
        public string safetyReason;
        public bool usedProductionPhysiology;

        public AudioRLTransition Snapshot()
        {
            return new AudioRLTransition
            {
                sessionId = sessionId,
                userId = userId,
                createdUtc = createdUtc,
                state = state?.Snapshot(),
                ruleBaselineAction = ruleBaselineAction,
                residualPolicyAction = residualPolicyAction,
                finalSafeAction = finalSafeAction,
                reward = reward,
                nextState = nextState?.Snapshot(),
                policyMode = policyMode,
                policyStatus = policyStatus,
                controllerMode = controllerMode,
                safetyMode = safetyMode,
                safetyReason = safetyReason,
                usedProductionPhysiology = usedProductionPhysiology
            };
        }
    }
}
