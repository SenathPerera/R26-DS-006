using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LaminarVR.AdaptiveMeditation.Environment;
using LaminarVR.AdaptiveMeditation.Physiology;
using LaminarVR.AdaptiveMeditation.Policy.ContextualBandit;
using LaminarVR.AdaptiveMeditation.Rewards;
using LaminarVR.AdaptiveMeditation.Safety;
using LaminarVR.AdaptiveMeditation.Session;
using LaminarVR.AdaptiveMeditation.Stabilization;
using LaminarVR.AdaptiveMeditation.Telemetry;

namespace LaminarVR.AdaptiveMeditation.Policy
{
    public sealed class PolicyController
    {
        private readonly IEnvironmentPolicy policy;
        private readonly IActionSafetyValidator safetyValidator;
        private readonly IEnvironmentParameterManager environmentManager;
        private readonly SceneEnvironmentProfile sceneProfile;
        private readonly ActionSafetyLimits safetyLimits;
        private readonly PhysiologyStateBuffer physiologyBuffer;
        private readonly RewardPipelineConfiguration rewardConfiguration;
        private readonly RewardAttributionTracker attributionTracker;
        private readonly RewardCalculator rewardCalculator;
        private readonly PhysiologyBaseline baseline;
        private readonly TelemetryRecorder telemetry;
        private readonly StabilizationStateSelector stabilizationSelector;

        private PendingDecision pendingDecision;
        private long latestDecisionPhysiologySequenceNumber;
        private EnvironmentAction? previousExecutedAction;
        private int consecutiveSameDirectionActions;
        private double totalVariation;
        private double? lastActionCompletedMonotonicTimeSeconds;

        public PolicyController(
            IEnvironmentPolicy policy,
            IActionSafetyValidator safetyValidator,
            IEnvironmentParameterManager environmentManager,
            SceneEnvironmentProfile sceneProfile,
            ActionSafetyLimits safetyLimits,
            PhysiologyStateBuffer physiologyBuffer,
            RewardPipelineConfiguration rewardConfiguration,
            PhysiologyBaseline baseline,
            TelemetryRecorder telemetry,
            EnvironmentStateLimits? sessionAdaptationLimits = null,
            StabilizationStateSelector stabilizationSelector = null)
        {
            this.policy = policy
                ?? throw new ArgumentNullException(nameof(policy));
            this.safetyValidator = safetyValidator
                ?? throw new ArgumentNullException(nameof(safetyValidator));
            this.environmentManager = environmentManager
                ?? throw new ArgumentNullException(nameof(environmentManager));
            this.sceneProfile = CreateEffectiveSceneProfile(
                sceneProfile,
                sessionAdaptationLimits);
            if (!this.sceneProfile.Limits.Contains(
                environmentManager.CurrentState))
            {
                throw new ArgumentException(
                    "The current environment must be inside scene limits.",
                    nameof(environmentManager));
            }

            if (safetyLimits.MaximumConsecutiveSameDirectionActions < 1)
            {
                throw new ArgumentException(
                    "Initialized safety limits are required.",
                    nameof(safetyLimits));
            }

            this.safetyLimits = safetyLimits;
            this.physiologyBuffer = physiologyBuffer
                ?? throw new ArgumentNullException(nameof(physiologyBuffer));
            this.rewardConfiguration = rewardConfiguration
                ?? throw new ArgumentNullException(
                    nameof(rewardConfiguration));
            this.baseline = baseline
                ?? throw new ArgumentNullException(nameof(baseline));
            this.telemetry = telemetry
                ?? throw new ArgumentNullException(nameof(telemetry));
            this.stabilizationSelector = stabilizationSelector;

            attributionTracker = new RewardAttributionTracker(
                rewardConfiguration);
            rewardCalculator = new RewardCalculator(rewardConfiguration);
        }

        public IEnvironmentPolicy Policy => policy;

        public bool HasPendingOutcome => pendingDecision != null;

        public double TotalVariation => totalVariation;

        public async Task<PolicyDecisionCycleResult> ProcessDecisionAsync(
            string decisionId,
            SessionDecisionOpportunity opportunity,
            VrSessionPhase phase,
            bool networkConnected,
            EnvironmentState preferredEnvironment,
            double utcTimestampUnixSeconds,
            double sessionElapsedSeconds,
            CancellationToken cancellationToken)
        {
            ValidateIdentity(decisionId, nameof(decisionId));
            ValidateTimes(
                opportunity.MonotonicTimeSeconds,
                utcTimestampUnixSeconds,
                sessionElapsedSeconds);
            ValidateEnvironment(
                preferredEnvironment,
                nameof(preferredEnvironment));

            var requestFields = new List<TelemetryField>
            {
                TelemetryField.String("decision_id", decisionId.Trim()),
                TelemetryField.Integer(
                    "decision_sequence",
                    opportunity.SequenceNumber),
                TelemetryField.String("policy_id", policy.PolicyId),
                TelemetryField.String("policy_version", policy.PolicyVersion),
                TelemetryField.String("phase", phase.ToString())
            };
            AddEnvironmentFields(
                requestFields,
                "state_before",
                environmentManager.CurrentState);
            AddEnvironmentFields(
                requestFields,
                "preference_state",
                preferredEnvironment);
            AddBanditIdentityFields(requestFields);
            await RecordAsync(
                TelemetryEventTypes.DecisionRequested,
                utcTimestampUnixSeconds,
                sessionElapsedSeconds,
                false,
                requestFields,
                cancellationToken).ConfigureAwait(false);

            if (phase != VrSessionPhase.Adaptive)
            {
                return await SkipDecisionAsync(
                    decisionId,
                    PolicyDecisionCycleResultCode.SkippedInvalidPhase,
                    phase.ToString(),
                    null,
                    utcTimestampUnixSeconds,
                    sessionElapsedSeconds,
                    cancellationToken).ConfigureAwait(false);
            }

            if (!networkConnected)
            {
                return await SkipDecisionAsync(
                    decisionId,
                    PolicyDecisionCycleResultCode.SkippedNetworkUnavailable,
                    "NetworkUnavailable",
                    null,
                    utcTimestampUnixSeconds,
                    sessionElapsedSeconds,
                    cancellationToken).ConfigureAwait(false);
            }

            if (environmentManager.IsTransitionActive)
            {
                return await SkipDecisionAsync(
                    decisionId,
                    PolicyDecisionCycleResultCode.SkippedTransitionActive,
                    environmentManager.ActiveTransitionId,
                    null,
                    utcTimestampUnixSeconds,
                    sessionElapsedSeconds,
                    cancellationToken).ConfigureAwait(false);
            }

            if (pendingDecision != null || attributionTracker.HasPending)
            {
                return await SkipDecisionAsync(
                    decisionId,
                    PolicyDecisionCycleResultCode.SkippedRewardPending,
                    "RewardPending",
                    null,
                    utcTimestampUnixSeconds,
                    sessionElapsedSeconds,
                    cancellationToken).ConfigureAwait(false);
            }

            if (!physiologyBuffer.TryGetLatestUsable(
                PhysiologyDataUse.Decision,
                opportunity.MonotonicTimeSeconds,
                latestDecisionPhysiologySequenceNumber,
                out var physiology,
                out var physiologyQueryResult))
            {
                return await SkipDecisionAsync(
                    decisionId,
                    PolicyDecisionCycleResultCode
                        .SkippedPhysiologyUnavailable,
                    physiologyQueryResult.ToString(),
                    physiologyQueryResult,
                    utcTimestampUnixSeconds,
                    sessionElapsedSeconds,
                    cancellationToken).ConfigureAwait(false);
            }

            var recentWindows = physiologyBuffer.GetRecentAccepted(
                rewardConfiguration.TrendWindowCount);
            var trend = PhysiologyTrendCalculator.Calculate(
                recentWindows,
                rewardConfiguration.MinimumTrendSamples);
            var actionCandidates = BuildPolicyActionCandidates(
                opportunity.MonotonicTimeSeconds,
                phase);
            var observation = new PolicyObservation(
                physiology,
                preferredEnvironment,
                environmentManager.CurrentState,
                sceneProfile.SafeDefault,
                trend,
                actionCandidates);
            var decision = policy.SelectAction(observation);
            latestDecisionPhysiologySequenceNumber =
                physiology.SequenceNumber;

            await RecordActionProposalAsync(
                decisionId,
                decision,
                utcTimestampUnixSeconds,
                sessionElapsedSeconds,
                cancellationToken).ConfigureAwait(false);

            var blockReason = DetermineSafetyBlockReason(
                opportunity.MonotonicTimeSeconds,
                phase,
                decision.SelectedAction);
            var validation = safetyValidator.Validate(
                decision.SelectedAction,
                environmentManager.CurrentState,
                sceneProfile,
                new SafetyRuntimeState(
                    blockReason,
                    previousExecutedAction,
                    consecutiveSameDirectionActions,
                    totalVariation),
                safetyLimits);

            await RecordActionValidationAsync(
                decisionId,
                decision,
                validation,
                utcTimestampUnixSeconds,
                sessionElapsedSeconds,
                cancellationToken).ConfigureAwait(false);
            if (!validation.Accepted)
            {
                return new PolicyDecisionCycleResult(
                    PolicyDecisionCycleResultCode.SafetyRejected,
                    decision,
                    validation,
                    null);
            }

            pendingDecision = new PendingDecision(
                decisionId.Trim(),
                decision,
                physiology,
                validation,
                environmentManager.CurrentState);

            if (validation.ExecutedAction == EnvironmentAction.NoChange)
            {
                OpenRewardAttribution(
                    opportunity.MonotonicTimeSeconds,
                    utcTimestampUnixSeconds,
                    phase,
                    networkConnected,
                    out _);
                await RecordActionExecutedAsync(
                    pendingDecision,
                    utcTimestampUnixSeconds,
                    sessionElapsedSeconds,
                    cancellationToken).ConfigureAwait(false);
                await RecordRewardWindowOpenedAsync(
                    decisionId,
                    utcTimestampUnixSeconds,
                    sessionElapsedSeconds,
                    cancellationToken).ConfigureAwait(false);
                return new PolicyDecisionCycleResult(
                    PolicyDecisionCycleResultCode.RewardWindowOpened,
                    decision,
                    validation,
                    null);
            }

            environmentManager.BeginTransition(
                decisionId,
                validation.SafeTarget,
                opportunity.MonotonicTimeSeconds,
                sceneProfile.TransitionDurationSeconds);
            await RecordAsync(
                TelemetryEventTypes.TransitionStarted,
                utcTimestampUnixSeconds,
                sessionElapsedSeconds,
                false,
                new[]
                {
                    TelemetryField.String("transition_id", decisionId.Trim()),
                    TelemetryField.Number(
                        "duration_seconds",
                        sceneProfile.TransitionDurationSeconds)
                },
                cancellationToken).ConfigureAwait(false);
            return new PolicyDecisionCycleResult(
                PolicyDecisionCycleResultCode.TransitionStarted,
                decision,
                validation,
                null);
        }

        public async Task<EnvironmentTransitionProgress>
            AdvanceTransitionAsync(
                double currentMonotonicTimeSeconds,
                double utcTimestampUnixSeconds,
                double sessionElapsedSeconds,
                VrSessionPhase phase,
                bool networkConnected,
                CancellationToken cancellationToken)
        {
            ValidateTimes(
                currentMonotonicTimeSeconds,
                utcTimestampUnixSeconds,
                sessionElapsedSeconds);
            if (!networkConnected || phase != VrSessionPhase.Adaptive)
            {
                var reason = !networkConnected
                    ? RewardAttributionInvalidationReason.NetworkLoss
                    : phase == VrSessionPhase.Paused
                        ? RewardAttributionInvalidationReason.Pause
                        : RewardAttributionInvalidationReason
                            .InvalidSessionPhase;
                await InvalidatePendingAsync(
                    reason,
                    currentMonotonicTimeSeconds,
                    utcTimestampUnixSeconds,
                    sessionElapsedSeconds,
                    cancellationToken).ConfigureAwait(false);
                return EnvironmentTransitionProgress.Idle(
                    environmentManager.CurrentState);
            }

            var progress = environmentManager.AdvanceTransition(
                currentMonotonicTimeSeconds);
            if (progress.Status != EnvironmentTransitionStatus.Completed)
            {
                return progress;
            }

            var completedAt = progress.CompletedMonotonicTimeSeconds.Value;
            var completedUtc = utcTimestampUnixSeconds
                - (currentMonotonicTimeSeconds - completedAt);
            var executedDecision = pendingDecision;
            var rewardWindowOpened = OpenRewardAttribution(
                completedAt,
                completedUtc,
                phase,
                networkConnected,
                out var openResultCode);
            await RecordAsync(
                TelemetryEventTypes.TransitionCompleted,
                utcTimestampUnixSeconds,
                sessionElapsedSeconds,
                false,
                new[]
                {
                    TelemetryField.String(
                        "transition_id",
                        progress.TransitionId),
                    TelemetryField.Number(
                        "completed_monotonic_seconds",
                        completedAt)
                },
                cancellationToken).ConfigureAwait(false);
            await RecordActionExecutedAsync(
                executedDecision,
                utcTimestampUnixSeconds,
                sessionElapsedSeconds,
                cancellationToken).ConfigureAwait(false);
            if (rewardWindowOpened)
            {
                await RecordRewardWindowOpenedAsync(
                    progress.TransitionId,
                    utcTimestampUnixSeconds,
                    sessionElapsedSeconds,
                    cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await RecordRewardInvalidationAsync(
                    openResultCode.ToString(),
                    utcTimestampUnixSeconds,
                    sessionElapsedSeconds,
                    cancellationToken).ConfigureAwait(false);
            }

            return progress;
        }

        public async Task<PolicyRewardCycleResult> TryResolveRewardAsync(
            double currentMonotonicTimeSeconds,
            double utcTimestampUnixSeconds,
            double sessionElapsedSeconds,
            VrSessionPhase phase,
            bool networkConnected,
            double discomfortSeverity,
            double safetySeverity,
            CancellationToken cancellationToken)
        {
            ValidateTimes(
                currentMonotonicTimeSeconds,
                utcTimestampUnixSeconds,
                sessionElapsedSeconds);
            if (!attributionTracker.TryResolve(
                physiologyBuffer,
                currentMonotonicTimeSeconds,
                phase,
                networkConnected,
                out var match,
                out var attributionCode))
            {
                if (attributionCode
                        == RewardAttributionResolutionCode.NoPending)
                {
                    return new PolicyRewardCycleResult(
                        PolicyRewardCycleResultCode.NoPending,
                        attributionCode,
                        default);
                }

                if (attributionCode
                        == RewardAttributionResolutionCode
                            .InvalidatedForPhase
                    || attributionCode
                        == RewardAttributionResolutionCode
                            .InvalidatedForNetwork
                    || attributionCode
                        == RewardAttributionResolutionCode.TimedOut)
                {
                    await RecordBanditUpdateSkippedAsync(
                        attributionCode.ToString(),
                        utcTimestampUnixSeconds,
                        sessionElapsedSeconds,
                        cancellationToken).ConfigureAwait(false);
                    await RecordRewardInvalidationAsync(
                        attributionCode.ToString(),
                        utcTimestampUnixSeconds,
                        sessionElapsedSeconds,
                        cancellationToken).ConfigureAwait(false);
                    pendingDecision = null;
                    return new PolicyRewardCycleResult(
                        PolicyRewardCycleResultCode.AttributionInvalidated,
                        attributionCode,
                        default);
                }

                return new PolicyRewardCycleResult(
                    PolicyRewardCycleResultCode.Waiting,
                    attributionCode,
                    default);
            }

            var calculation = rewardCalculator.Calculate(
                match.Request.PreActionPhysiology,
                match.PostActionPhysiology,
                baseline,
                match.Request.ExecutedAction,
                match.Request.EnvironmentBefore,
                match.Request.EnvironmentAfter,
                discomfortSeverity,
                safetySeverity);
            await RecordAsync(
                TelemetryEventTypes.RewardWindowClosed,
                utcTimestampUnixSeconds,
                sessionElapsedSeconds,
                false,
                new[]
                {
                    TelemetryField.String(
                        "transition_id",
                        match.Request.TransitionId),
                    TelemetryField.Integer(
                        "post_window_sequence",
                        match.PostActionPhysiology.SequenceNumber)
                },
                cancellationToken).ConfigureAwait(false);
            if (!calculation.Valid)
            {
                await RecordBanditUpdateSkippedAsync(
                    calculation.ResultCode.ToString(),
                    utcTimestampUnixSeconds,
                    sessionElapsedSeconds,
                    cancellationToken).ConfigureAwait(false);
                await RecordRewardInvalidationAsync(
                    calculation.ResultCode.ToString(),
                    utcTimestampUnixSeconds,
                    sessionElapsedSeconds,
                    cancellationToken).ConfigureAwait(false);
                pendingDecision = null;
                return new PolicyRewardCycleResult(
                    PolicyRewardCycleResultCode.RewardInvalid,
                    attributionCode,
                    calculation);
            }

            var completedDecision = pendingDecision
                ?? throw new InvalidOperationException(
                    "A matched reward has no pending policy decision.");
            if (stabilizationSelector != null)
            {
                // TODO(RESEARCH_DECISION): Freeze discomfort and safety
                // eligibility thresholds. Until then, any positive severity
                // excludes the state from final stabilization selection.
                stabilizationSelector.RecordOutcome(
                    new StabilizationOutcome(
                        match.Request.TransitionId,
                        match.PostActionPhysiology.SequenceNumber,
                        match.Request.EnvironmentAfter,
                        calculation.Breakdown.TotalReward,
                        discomfortSeverity > 0d,
                        safetySeverity > 0d));
            }

            await RecordAsync(
                TelemetryEventTypes.RewardCalculated,
                utcTimestampUnixSeconds,
                sessionElapsedSeconds,
                false,
                new[]
                {
                    TelemetryField.String(
                        "transition_id",
                        match.Request.TransitionId),
                    TelemetryField.Number(
                        "total_reward",
                        calculation.Breakdown.TotalReward),
                    TelemetryField.Number(
                        "stress_component",
                        calculation.Breakdown.StressComponent),
                    TelemetryField.Number(
                        "rmssd_component",
                        calculation.Breakdown.RmssdComponent),
                    TelemetryField.Number(
                        "heart_rate_component",
                        calculation.Breakdown.HeartRateComponent),
                    TelemetryField.Number(
                        "change_penalty_component",
                        calculation.Breakdown.ChangePenaltyComponent),
                    TelemetryField.Number(
                        "discomfort_penalty_component",
                        calculation.Breakdown.DiscomfortPenaltyComponent),
                    TelemetryField.Number(
                        "safety_penalty_component",
                        calculation.Breakdown.SafetyPenaltyComponent),
                    TelemetryField.Number(
                        "action_magnitude",
                        calculation.Breakdown.ActionMagnitude),
                    TelemetryField.Integer(
                        "pre_window_sequence",
                        match.Request.PreActionPhysiology.SequenceNumber),
                    TelemetryField.Integer(
                        "post_window_sequence",
                        match.PostActionPhysiology.SequenceNumber)
                },
                cancellationToken).ConfigureAwait(false);
            pendingDecision = null;
            var policyStateBeforeUpdate = policy.CaptureState();
            PolicyStateSnapshot policyStateAfterUpdate;
            try
            {
                policy.ObserveOutcome(
                    new ActionOutcome(
                        completedDecision.DecisionId,
                        completedDecision.Decision,
                        match.Request.ExecutedAction,
                        calculation.Breakdown.TotalReward,
                        match.Request.PreActionPhysiology.SequenceNumber,
                        match.PostActionPhysiology.SequenceNumber));
                policyStateAfterUpdate = policy.CaptureState();
            }
            catch (Exception exception) when (
                exception is ArgumentException
                || exception is InvalidOperationException
                || exception is OverflowException)
            {
                var skippedFields = new List<TelemetryField>
                {
                    TelemetryField.String(
                        "decision_id",
                        completedDecision.DecisionId),
                    TelemetryField.String("policy_id", policy.PolicyId),
                    TelemetryField.String(
                        "policy_version",
                        policy.PolicyVersion),
                    TelemetryField.String(
                        "executed_action",
                        match.Request.ExecutedAction.ToString()),
                    TelemetryField.String(
                        "reason",
                        exception.GetType().Name)
                };
                AddBanditIdentityFields(skippedFields);
                if (policy is ContextualBanditPolicy)
                {
                    skippedFields.Add(
                        TelemetryField.String(
                            "snapshot_schema_version",
                            DisjointLinUcbModel.SnapshotSchemaVersion));
                }

                await RecordAsync(
                    TelemetryEventTypes.BanditUpdateSkipped,
                    utcTimestampUnixSeconds,
                    sessionElapsedSeconds,
                    true,
                    skippedFields,
                    cancellationToken).ConfigureAwait(false);
                return new PolicyRewardCycleResult(
                    PolicyRewardCycleResultCode.PolicyUpdateSkipped,
                    attributionCode,
                    calculation);
            }

            if (policyStateAfterUpdate.ModelUpdateCount
                > policyStateBeforeUpdate.ModelUpdateCount)
            {
                var updateFields = new List<TelemetryField>
                {
                    TelemetryField.String(
                        "decision_id",
                        completedDecision.DecisionId),
                    TelemetryField.String(
                        "policy_id",
                        policyStateAfterUpdate.PolicyId),
                    TelemetryField.String(
                        "policy_version",
                        policyStateAfterUpdate.PolicyVersion),
                    TelemetryField.String(
                        "executed_action",
                        match.Request.ExecutedAction.ToString()),
                    TelemetryField.Integer(
                        "model_update_count",
                        policyStateAfterUpdate.ModelUpdateCount)
                };
                if (completedDecision.Decision.FeatureVector != null)
                {
                    updateFields.Add(
                        TelemetryField.String(
                            "feature_schema_version",
                            completedDecision.Decision.FeatureVector
                                .SchemaVersion));
                }

                AddBanditIdentityFields(updateFields);
                updateFields.Add(
                    TelemetryField.String(
                        "snapshot_schema_version",
                        DisjointLinUcbModel.SnapshotSchemaVersion));

                await RecordAsync(
                    TelemetryEventTypes.BanditUpdated,
                    utcTimestampUnixSeconds,
                    sessionElapsedSeconds,
                    false,
                    updateFields,
                    cancellationToken).ConfigureAwait(false);
            }

            return new PolicyRewardCycleResult(
                PolicyRewardCycleResultCode.RewardApplied,
                attributionCode,
                calculation);
        }

        public async Task<StabilizationSelectionResult>
            SelectStabilizationStateAsync(
                VrSessionPhase phase,
                EnvironmentState safePreferenceState,
                double utcTimestampUnixSeconds,
                double sessionElapsedSeconds,
                CancellationToken cancellationToken)
        {
            ValidateFiniteNonNegative(
                utcTimestampUnixSeconds,
                nameof(utcTimestampUnixSeconds));
            ValidateFiniteNonNegative(
                sessionElapsedSeconds,
                nameof(sessionElapsedSeconds));
            if (phase != VrSessionPhase.Stabilization)
            {
                throw new InvalidOperationException(
                    "Final state selection is only valid during stabilization.");
            }

            if (stabilizationSelector == null)
            {
                throw new InvalidOperationException(
                    "A stabilization selector was not configured.");
            }

            if (environmentManager.IsTransitionActive
                || pendingDecision != null
                || attributionTracker.HasPending)
            {
                throw new InvalidOperationException(
                    "Pending adaptive work must be closed before stabilization selection.");
            }

            var result = stabilizationSelector.Select(
                safePreferenceState,
                sceneProfile.Limits);
            for (var index = 0; index < result.EvaluationCount; index++)
            {
                var evaluation = result.GetEvaluation(index);
                var fields = new List<TelemetryField>
                {
                    TelemetryField.String(
                        "transition_id",
                        evaluation.Outcome.TransitionId),
                    TelemetryField.Integer(
                        "post_physiology_sequence",
                        evaluation.Outcome.PostPhysiologySequenceNumber),
                    TelemetryField.Integer(
                        "recency_index",
                        evaluation.RecencyIndex),
                    TelemetryField.Number("reward", evaluation.Outcome.Reward),
                    TelemetryField.Number(
                        "selection_score",
                        evaluation.SelectionScore),
                    TelemetryField.Boolean("eligible", evaluation.Eligible),
                    TelemetryField.String(
                        "exclusion_reason",
                        evaluation.ExclusionReason.ToString())
                };
                AddEnvironmentFields(
                    fields,
                    "candidate_state",
                    evaluation.Outcome.State);
                await RecordAsync(
                    TelemetryEventTypes.StabilizationCandidateScored,
                    utcTimestampUnixSeconds,
                    sessionElapsedSeconds,
                    false,
                    fields,
                    cancellationToken).ConfigureAwait(false);
            }

            var selectionFields = new List<TelemetryField>
            {
                TelemetryField.String(
                    "configuration_id",
                    stabilizationSelector.Configuration.ConfigurationId),
                TelemetryField.Integer(
                    "configuration_version",
                    stabilizationSelector.Configuration.ConfigurationVersion),
                TelemetryField.String("reason_code", result.ReasonCode),
                TelemetryField.Boolean(
                    "used_preference_fallback",
                    result.UsedPreferenceFallback),
                result.SelectedTransitionId == null
                    ? TelemetryField.Null("selected_transition_id")
                    : TelemetryField.String(
                        "selected_transition_id",
                        result.SelectedTransitionId)
            };
            AddEnvironmentFields(
                selectionFields,
                "selected_state",
                result.SelectedState);
            await RecordAsync(
                TelemetryEventTypes.StabilizationStateSelected,
                utcTimestampUnixSeconds,
                sessionElapsedSeconds,
                true,
                selectionFields,
                cancellationToken).ConfigureAwait(false);
            return result;
        }

        public async Task<bool> InvalidatePendingAsync(
            RewardAttributionInvalidationReason reason,
            double currentMonotonicTimeSeconds,
            double utcTimestampUnixSeconds,
            double sessionElapsedSeconds,
            CancellationToken cancellationToken)
        {
            ValidateTimes(
                currentMonotonicTimeSeconds,
                utcTimestampUnixSeconds,
                sessionElapsedSeconds);
            var invalidated = attributionTracker.TryInvalidate(
                reason,
                currentMonotonicTimeSeconds,
                out _);
            var cancelled = environmentManager.CancelTransition(
                out var cancelledTransitionId);
            if (cancelled)
            {
                await RecordAsync(
                    TelemetryEventTypes.TransitionCancelled,
                    utcTimestampUnixSeconds,
                    sessionElapsedSeconds,
                    true,
                    new[]
                    {
                        TelemetryField.String(
                            "transition_id",
                            cancelledTransitionId),
                        TelemetryField.String("reason", reason.ToString())
                    },
                    cancellationToken).ConfigureAwait(false);
            }

            if (invalidated || cancelled || pendingDecision != null)
            {
                await RecordBanditUpdateSkippedAsync(
                    reason.ToString(),
                    utcTimestampUnixSeconds,
                    sessionElapsedSeconds,
                    cancellationToken).ConfigureAwait(false);
                await RecordRewardInvalidationAsync(
                    reason.ToString(),
                    utcTimestampUnixSeconds,
                    sessionElapsedSeconds,
                    cancellationToken).ConfigureAwait(false);
                pendingDecision = null;
                return true;
            }

            return false;
        }

        private bool OpenRewardAttribution(
            double completedMonotonicTimeSeconds,
            double completedUtcUnixSeconds,
            VrSessionPhase phase,
            bool networkConnected,
            out RewardAttributionOpenResultCode resultCode)
        {
            var decision = pendingDecision
                ?? throw new InvalidOperationException(
                    "A transition completed without a pending decision.");
            var request = new RewardAttributionRequest(
                decision.DecisionId,
                decision.PreActionPhysiology,
                decision.Validation.ExecutedAction,
                decision.EnvironmentBefore,
                decision.Validation.SafeTarget,
                completedMonotonicTimeSeconds,
                completedUtcUnixSeconds);
            UpdateSafetyHistory(
                decision.Validation.ExecutedAction,
                decision.Validation.AppliedVariation,
                completedMonotonicTimeSeconds);
            if (!attributionTracker.TryOpen(
                request,
                phase,
                networkConnected,
                out resultCode))
            {
                pendingDecision = null;
                return false;
            }

            return true;
        }

        private SafetyBlockReason DetermineSafetyBlockReason(
            double currentMonotonicTimeSeconds,
            VrSessionPhase phase,
            EnvironmentAction proposedAction)
        {
            if (phase != VrSessionPhase.Adaptive)
            {
                return phase == VrSessionPhase.Paused
                    ? SafetyBlockReason.Paused
                    : phase == VrSessionPhase.Stabilization
                        ? SafetyBlockReason.Stabilization
                        : SafetyBlockReason.SessionNotAdaptive;
            }

            if (environmentManager.IsTransitionActive)
            {
                return SafetyBlockReason.TransitionActive;
            }

            if (proposedAction != EnvironmentAction.NoChange
                && lastActionCompletedMonotonicTimeSeconds.HasValue
                && currentMonotonicTimeSeconds
                    - lastActionCompletedMonotonicTimeSeconds.Value
                    < sceneProfile.MinimumSecondsBetweenActions)
            {
                return SafetyBlockReason.CooldownActive;
            }

            return SafetyBlockReason.None;
        }

        private PolicyActionCandidate[] BuildPolicyActionCandidates(
            double currentMonotonicTimeSeconds,
            VrSessionPhase phase)
        {
            var candidates = new List<PolicyActionCandidate>();
            for (var actionValue = (int)EnvironmentAction.NoChange;
                actionValue
                    <= (int)EnvironmentAction.DecreaseAmbientMotion;
                actionValue++)
            {
                var action = (EnvironmentAction)actionValue;
                var blockReason = DetermineSafetyBlockReason(
                    currentMonotonicTimeSeconds,
                    phase,
                    action);
                var validation = safetyValidator.Validate(
                    action,
                    environmentManager.CurrentState,
                    sceneProfile,
                    new SafetyRuntimeState(
                        blockReason,
                        previousExecutedAction,
                        consecutiveSameDirectionActions,
                        totalVariation),
                    safetyLimits);
                if (validation.Accepted)
                {
                    candidates.Add(
                        new PolicyActionCandidate(
                            action,
                            validation.AppliedVariation));
                }
            }

            return candidates.ToArray();
        }

        private void UpdateSafetyHistory(
            EnvironmentAction executedAction,
            double appliedVariation,
            double completedMonotonicTimeSeconds)
        {
            if (executedAction == EnvironmentAction.NoChange)
            {
                previousExecutedAction = executedAction;
                consecutiveSameDirectionActions = 0;
                return;
            }
            else
            {
                consecutiveSameDirectionActions =
                    previousExecutedAction == executedAction
                        ? consecutiveSameDirectionActions + 1
                        : 1;
                previousExecutedAction = executedAction;
            }

            totalVariation += appliedVariation;
            lastActionCompletedMonotonicTimeSeconds =
                completedMonotonicTimeSeconds;
        }

        private async Task RecordActionProposalAsync(
            string decisionId,
            PolicyDecision decision,
            double utcTimestampUnixSeconds,
            double sessionElapsedSeconds,
            CancellationToken cancellationToken)
        {
            var proposalFields = new List<TelemetryField>
            {
                TelemetryField.String("decision_id", decisionId.Trim()),
                TelemetryField.String("policy_id", decision.PolicyId),
                TelemetryField.String(
                    "policy_version",
                    decision.PolicyVersion),
                TelemetryField.String(
                    "proposed_action",
                    decision.SelectedAction.ToString()),
                TelemetryField.String(
                    "reason_code",
                    decision.ReasonCode),
                TelemetryField.Integer(
                    "physiology_sequence",
                    decision.PhysiologySequenceNumber),
                TelemetryField.Boolean(
                    "exploration_used",
                    decision.ExplorationUsed),
                decision.ExpectedReward.HasValue
                    ? TelemetryField.Number(
                        "expected_reward",
                        decision.ExpectedReward.Value)
                    : TelemetryField.Null("expected_reward"),
                decision.Uncertainty.HasValue
                    ? TelemetryField.Number(
                        "uncertainty",
                        decision.Uncertainty.Value)
                    : TelemetryField.Null("uncertainty"),
                decision.FeatureVector != null
                    ? TelemetryField.String(
                        "feature_schema_version",
                        decision.FeatureVector.SchemaVersion)
                    : TelemetryField.Null("feature_schema_version")
            };
            AddBanditIdentityFields(proposalFields);
            if (decision.FeatureVector != null)
            {
                for (var index = 0;
                    index < decision.FeatureVector.Count;
                    index++)
                {
                    proposalFields.Add(
                        TelemetryField.Number(
                            "feature_" + index,
                            decision.FeatureVector[index]));
                }
            }

            await RecordAsync(
                TelemetryEventTypes.ActionProposed,
                utcTimestampUnixSeconds,
                sessionElapsedSeconds,
                false,
                proposalFields,
                cancellationToken).ConfigureAwait(false);

            for (var index = 0;
                index < decision.CandidateScoreCount;
                index++)
            {
                var score = decision.GetCandidateScore(index);
                var scoreFields = new[]
                {
                    TelemetryField.String(
                        "decision_id",
                        decisionId.Trim()),
                    TelemetryField.String(
                        "action",
                        score.Action.ToString()),
                    TelemetryField.Number("score", score.Score),
                    TelemetryField.Number(
                        "uncertainty",
                        score.Uncertainty),
                    score.ExpectedReward.HasValue
                        ? TelemetryField.Number(
                            "expected_reward",
                            score.ExpectedReward.Value)
                        : TelemetryField.Null("expected_reward"),
                    score.ExplorationBonus.HasValue
                        ? TelemetryField.Number(
                            "exploration_bonus",
                            score.ExplorationBonus.Value)
                        : TelemetryField.Null("exploration_bonus")
                };
                await RecordAsync(
                    TelemetryEventTypes.PolicyCandidateScore,
                    utcTimestampUnixSeconds,
                    sessionElapsedSeconds,
                    false,
                    scoreFields,
                    cancellationToken).ConfigureAwait(false);
            }
        }

        private Task RecordActionValidationAsync(
            string decisionId,
            PolicyDecision decision,
            ActionValidationResult validation,
            double utcTimestampUnixSeconds,
            double sessionElapsedSeconds,
            CancellationToken cancellationToken)
        {
            var fields = new List<TelemetryField>
            {
                TelemetryField.String("decision_id", decisionId.Trim()),
                TelemetryField.String(
                    "proposed_action",
                    decision.SelectedAction.ToString()),
                TelemetryField.Boolean("accepted", validation.Accepted),
                TelemetryField.Boolean("modified", validation.Modified),
                TelemetryField.String(
                    "executed_action",
                    validation.ExecutedAction.ToString()),
                TelemetryField.String(
                    "reason_code",
                    validation.ReasonCode.ToString()),
                TelemetryField.Number(
                    "applied_variation",
                    validation.AppliedVariation)
            };
            AddEnvironmentFields(
                fields,
                "state_before",
                environmentManager.CurrentState);
            AddEnvironmentFields(fields, "safe_target", validation.SafeTarget);
            return RecordAsync(
                TelemetryEventTypes.ActionValidated,
                utcTimestampUnixSeconds,
                sessionElapsedSeconds,
                !validation.Accepted,
                fields,
                cancellationToken);
        }

        private Task RecordActionExecutedAsync(
            PendingDecision decision,
            double utcTimestampUnixSeconds,
            double sessionElapsedSeconds,
            CancellationToken cancellationToken)
        {
            if (decision == null)
            {
                throw new InvalidOperationException(
                    "An executed action must have a pending decision.");
            }

            var fields = new List<TelemetryField>
            {
                TelemetryField.String("decision_id", decision.DecisionId),
                TelemetryField.String(
                    "executed_action",
                    decision.Validation.ExecutedAction.ToString()),
                TelemetryField.Boolean(
                    "safety_modified",
                    decision.Validation.Modified)
            };
            AddEnvironmentFields(
                fields,
                "state_before",
                decision.EnvironmentBefore);
            AddEnvironmentFields(
                fields,
                "state_after",
                decision.Validation.SafeTarget);
            return RecordAsync(
                TelemetryEventTypes.ActionExecuted,
                utcTimestampUnixSeconds,
                sessionElapsedSeconds,
                false,
                fields,
                cancellationToken);
        }

        private Task RecordRewardWindowOpenedAsync(
            string transitionId,
            double utcTimestampUnixSeconds,
            double sessionElapsedSeconds,
            CancellationToken cancellationToken)
        {
            return RecordAsync(
                TelemetryEventTypes.RewardWindowOpened,
                utcTimestampUnixSeconds,
                sessionElapsedSeconds,
                false,
                new[]
                {
                    TelemetryField.String(
                        "transition_id",
                        transitionId.Trim()),
                    TelemetryField.Number(
                        "settling_seconds",
                        rewardConfiguration.SettlingSeconds)
                },
                cancellationToken);
        }

        private Task RecordRewardInvalidationAsync(
            string reason,
            double utcTimestampUnixSeconds,
            double sessionElapsedSeconds,
            CancellationToken cancellationToken)
        {
            var fields = new List<TelemetryField>
            {
                TelemetryField.String("reason", reason)
            };
            if (pendingDecision != null)
            {
                fields.Add(
                    TelemetryField.String(
                        "decision_id",
                        pendingDecision.DecisionId));
            }

            return RecordAsync(
                TelemetryEventTypes.RewardInvalidated,
                utcTimestampUnixSeconds,
                sessionElapsedSeconds,
                true,
                fields,
                cancellationToken);
        }

        private Task RecordBanditUpdateSkippedAsync(
            string reason,
            double utcTimestampUnixSeconds,
            double sessionElapsedSeconds,
            CancellationToken cancellationToken)
        {
            if (!(policy is ContextualBanditPolicy contextualBandit)
                || pendingDecision == null)
            {
                return Task.CompletedTask;
            }

            return RecordAsync(
                TelemetryEventTypes.BanditUpdateSkipped,
                utcTimestampUnixSeconds,
                sessionElapsedSeconds,
                true,
                new[]
                {
                    TelemetryField.String(
                        "decision_id",
                        pendingDecision.DecisionId),
                    TelemetryField.String("policy_id", policy.PolicyId),
                    TelemetryField.String(
                        "model_version",
                        contextualBandit.Model.ModelVersion),
                    TelemetryField.String(
                        "snapshot_schema_version",
                        DisjointLinUcbModel.SnapshotSchemaVersion),
                    TelemetryField.String("reason", reason)
                },
                cancellationToken);
        }

        private async Task RecordAsync(
            string eventType,
            double utcTimestampUnixSeconds,
            double sessionElapsedSeconds,
            bool critical,
            IReadOnlyList<TelemetryField> fields,
            CancellationToken cancellationToken)
        {
            await telemetry.RecordAsync(
                eventType,
                utcTimestampUnixSeconds,
                sessionElapsedSeconds,
                critical,
                fields,
                cancellationToken).ConfigureAwait(false);
        }

        private async Task<PolicyDecisionCycleResult> SkipDecisionAsync(
            string decisionId,
            PolicyDecisionCycleResultCode resultCode,
            string reason,
            PhysiologyQueryResultCode? physiologyQueryResult,
            double utcTimestampUnixSeconds,
            double sessionElapsedSeconds,
            CancellationToken cancellationToken)
        {
            var fields = new List<TelemetryField>
            {
                TelemetryField.String("decision_id", decisionId.Trim()),
                TelemetryField.String("result_code", resultCode.ToString()),
                TelemetryField.String("reason", reason ?? string.Empty)
            };
            if (physiologyQueryResult.HasValue)
            {
                fields.Add(
                    TelemetryField.String(
                        "physiology_query_result",
                        physiologyQueryResult.Value.ToString()));
            }

            await RecordAsync(
                TelemetryEventTypes.DecisionSkipped,
                utcTimestampUnixSeconds,
                sessionElapsedSeconds,
                true,
                fields,
                cancellationToken).ConfigureAwait(false);
            return new PolicyDecisionCycleResult(
                resultCode,
                null,
                null,
                physiologyQueryResult);
        }

        private void AddBanditIdentityFields(List<TelemetryField> fields)
        {
            if (policy is ContextualBanditPolicy contextualBandit)
            {
                fields.Add(
                    TelemetryField.String(
                        "model_version",
                        contextualBandit.Model.ModelVersion));
            }
        }

        private static void AddEnvironmentFields(
            List<TelemetryField> fields,
            string prefix,
            EnvironmentState state)
        {
            fields.Add(
                TelemetryField.Number(
                    prefix + "_illumination",
                    state.Illumination));
            fields.Add(
                TelemetryField.Number(prefix + "_warmth", state.Warmth));
            fields.Add(
                TelemetryField.Number(
                    prefix + "_atmospheric_softness",
                    state.AtmosphericSoftness));
            fields.Add(
                TelemetryField.Number(
                    prefix + "_color_richness",
                    state.ColorRichness));
            fields.Add(
                TelemetryField.Number(
                    prefix + "_ambient_motion",
                    state.AmbientMotion));
        }

        private static void ValidateIdentity(
            string value,
            string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException(
                    "A non-empty identity is required.",
                    parameterName);
            }
        }

        private static void ValidateEnvironment(
            EnvironmentState state,
            string parameterName)
        {
            if (!state.IsNormalized)
            {
                throw new ArgumentException(
                    "Environment state must be normalized.",
                    parameterName);
            }
        }

        private static SceneEnvironmentProfile CreateEffectiveSceneProfile(
            SceneEnvironmentProfile sceneProfile,
            EnvironmentStateLimits? sessionAdaptationLimits)
        {
            if (sceneProfile == null)
            {
                throw new ArgumentNullException(nameof(sceneProfile));
            }

            if (!sessionAdaptationLimits.HasValue)
            {
                return sceneProfile;
            }

            var limits = sessionAdaptationLimits.Value;
            if (!IsSubset(limits, sceneProfile.Limits))
            {
                throw new ArgumentException(
                    "Session adaptation limits must be inside scene limits.",
                    nameof(sessionAdaptationLimits));
            }

            return new SceneEnvironmentProfile(
                sceneProfile.SceneId,
                sceneProfile.DisplayName,
                limits.Clamp(sceneProfile.SafeDefault),
                limits,
                sceneProfile.ActionStep,
                sceneProfile.TransitionDurationSeconds,
                sceneProfile.MinimumSecondsBetweenActions);
        }

        private static bool IsSubset(
            EnvironmentStateLimits candidate,
            EnvironmentStateLimits container)
        {
            return IsSubset(candidate.Illumination, container.Illumination)
                && IsSubset(candidate.Warmth, container.Warmth)
                && IsSubset(
                    candidate.AtmosphericSoftness,
                    container.AtmosphericSoftness)
                && IsSubset(
                    candidate.ColorRichness,
                    container.ColorRichness)
                && IsSubset(
                    candidate.AmbientMotion,
                    container.AmbientMotion);
        }

        private static bool IsSubset(
            NormalizedRange candidate,
            NormalizedRange container)
        {
            return candidate.Minimum >= container.Minimum
                && candidate.Maximum <= container.Maximum;
        }

        private static void ValidateTimes(
            double monotonicTimeSeconds,
            double utcTimestampUnixSeconds,
            double sessionElapsedSeconds)
        {
            ValidateFiniteNonNegative(
                monotonicTimeSeconds,
                nameof(monotonicTimeSeconds));
            ValidateFiniteNonNegative(
                utcTimestampUnixSeconds,
                nameof(utcTimestampUnixSeconds));
            ValidateFiniteNonNegative(
                sessionElapsedSeconds,
                nameof(sessionElapsedSeconds));
        }

        private static void ValidateFiniteNonNegative(
            double value,
            string parameterName)
        {
            if (double.IsNaN(value)
                || double.IsInfinity(value)
                || value < 0d)
            {
                throw new ArgumentOutOfRangeException(parameterName);
            }
        }

        private sealed class PendingDecision
        {
            public PendingDecision(
                string decisionId,
                PolicyDecision decision,
                PhysiologyWindowSnapshot preActionPhysiology,
                ActionValidationResult validation,
                EnvironmentState environmentBefore)
            {
                DecisionId = decisionId;
                Decision = decision;
                PreActionPhysiology = preActionPhysiology;
                Validation = validation;
                EnvironmentBefore = environmentBefore;
            }

            public string DecisionId { get; }

            public PolicyDecision Decision { get; }

            public PhysiologyWindowSnapshot PreActionPhysiology { get; }

            public ActionValidationResult Validation { get; }

            public EnvironmentState EnvironmentBefore { get; }
        }
    }
}
