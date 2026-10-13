using System;

namespace AdaptiveAudioVR.RL.Agent
{
    [Serializable]
    public sealed class AudioRLTransition
    {
        public int schemaVersion = 2;
        public string eventType = "outcome";
        public string decisionId;
        public int conditionSegment;
        public AudioRLAction proposedPolicyAction;
        public bool rewardValid;
        public bool onlineUpdateApplied;
        public string outcomeReason;
        public int proposedArm;
        public int executedArm;
        public string featureSchema;
        public double[] contextFeatures;
        public double predictedReward;
        public double uncertainty;
        public double selectionScore;
        public int onlineUpdateCount;
        public double eligiblePostStartUtc;
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
                schemaVersion = schemaVersion,
                eventType = eventType,
                decisionId = decisionId,
                conditionSegment = conditionSegment,
                proposedPolicyAction = proposedPolicyAction,
                rewardValid = rewardValid,
                onlineUpdateApplied = onlineUpdateApplied,
                outcomeReason = outcomeReason,
                proposedArm = proposedArm,
                executedArm = executedArm,
                featureSchema = featureSchema,
                contextFeatures = contextFeatures != null ? (double[])contextFeatures.Clone() : null,
                predictedReward = predictedReward,
                uncertainty = uncertainty,
                selectionScore = selectionScore,
                onlineUpdateCount = onlineUpdateCount,
                eligiblePostStartUtc = eligiblePostStartUtc,
                sessionId = sessionId,
                userId = userId,
                createdUtc = createdUtc,
                state = state?.Snapshot(),
                ruleBaselineAction = ruleBaselineAction,
                residualPolicyAction = residualPolicyAction,
                finalSafeAction = finalSafeAction,
                reward = reward?.Snapshot(),
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
