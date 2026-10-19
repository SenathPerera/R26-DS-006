using System;
using LaminarVR.AdaptiveMeditation.Environment;
using LaminarVR.AdaptiveMeditation.Physiology;
using LaminarVR.AdaptiveMeditation.Policy;
using LaminarVR.AdaptiveMeditation.Policy.ContextualBandit;
using LaminarVR.AdaptiveMeditation.Rewards;
using LaminarVR.AdaptiveMeditation.Telemetry;

namespace LaminarVR.AdaptiveMeditation.ResearchEvaluation
{
    public readonly struct EvaluationPhysiologyRecord
    {
        public EvaluationPhysiologyRecord(
            int emissionSequence,
            double eventTimeSeconds,
            bool dropped,
            PhysiologyWindow window,
            PhysiologyIngestionResult? ingestion)
        {
            EmissionSequence = emissionSequence;
            EventTimeSeconds = eventTimeSeconds;
            Dropped = dropped;
            Window = window;
            Ingestion = ingestion;
        }

        public int EmissionSequence { get; }
        public double EventTimeSeconds { get; }
        public bool Dropped { get; }
        public PhysiologyWindow Window { get; }
        public PhysiologyIngestionResult? Ingestion { get; }
    }

    public readonly struct EvaluationDecisionRecord
    {
        public EvaluationDecisionRecord(
            int opportunitySequence,
            double eventTimeSeconds,
            PolicyDecisionCycleResult result,
            SafeOracleDecisionRecord? safeOracle)
        {
            OpportunitySequence = opportunitySequence;
            EventTimeSeconds = eventTimeSeconds;
            Result = result;
            SafeOracle = safeOracle;
        }

        public int OpportunitySequence { get; }
        public double EventTimeSeconds { get; }
        public PolicyDecisionCycleResult Result { get; }
        public SafeOracleDecisionRecord? SafeOracle { get; }
    }

    public readonly struct EvaluationRewardRecord
    {
        public EvaluationRewardRecord(
            double eventTimeSeconds,
            PolicyRewardCycleResult result)
        {
            EventTimeSeconds = eventTimeSeconds;
            Result = result;
        }

        public double EventTimeSeconds { get; }
        public PolicyRewardCycleResult Result { get; }
    }

    public sealed class EvaluationSessionResult
    {
        public EvaluationSessionResult(
            string protocolId,
            string profileBundleId,
            string physiologyModelId,
            EnvironmentState initialState,
            EnvironmentState finalAdaptiveState,
            EnvironmentState selectedStabilizationState,
            bool baselineAvailable,
            PolicyStateSnapshot policyState,
            EvaluationPhysiologyRecord[] physiologyRecords,
            EvaluationDecisionRecord[] decisions,
            EvaluationRewardRecord[] rewards,
            TelemetryEvent[] telemetryEvents,
            EnvironmentState[] appliedStates,
            bool allAppliedStatesSafe,
            LinUcbModelSnapshot modelSnapshot = null)
        {
            ProtocolId = protocolId;
            ProfileBundleId = profileBundleId;
            PhysiologyModelId = physiologyModelId;
            InitialState = initialState;
            FinalAdaptiveState = finalAdaptiveState;
            SelectedStabilizationState = selectedStabilizationState;
            BaselineAvailable = baselineAvailable;
            PolicyState = policyState;
            PhysiologyRecords = physiologyRecords ?? Array.Empty<EvaluationPhysiologyRecord>();
            Decisions = decisions ?? Array.Empty<EvaluationDecisionRecord>();
            Rewards = rewards ?? Array.Empty<EvaluationRewardRecord>();
            TelemetryEvents = telemetryEvents ?? Array.Empty<TelemetryEvent>();
            AppliedStates = appliedStates ?? Array.Empty<EnvironmentState>();
            AllAppliedStatesSafe = allAppliedStatesSafe;
            ModelSnapshot = modelSnapshot;
        }

        public string ProtocolId { get; }
        public string ProfileBundleId { get; }
        public string PhysiologyModelId { get; }
        public EnvironmentState InitialState { get; }
        public EnvironmentState FinalAdaptiveState { get; }
        public EnvironmentState SelectedStabilizationState { get; }
        public bool BaselineAvailable { get; }
        public PolicyStateSnapshot PolicyState { get; }
        public EvaluationPhysiologyRecord[] PhysiologyRecords { get; }
        public EvaluationDecisionRecord[] Decisions { get; }
        public EvaluationRewardRecord[] Rewards { get; }
        public TelemetryEvent[] TelemetryEvents { get; }
        public EnvironmentState[] AppliedStates { get; }
        public bool AllAppliedStatesSafe { get; }

        public LinUcbModelSnapshot ModelSnapshot { get; }

        public int RewardAppliedCount
        {
            get
            {
                var count = 0;
                for (var index = 0; index < Rewards.Length; index++)
                {
                    if (Rewards[index].Result.ResultCode
                        == PolicyRewardCycleResultCode.RewardApplied)
                    {
                        count++;
                    }
                }

                return count;
            }
        }
    }
}
