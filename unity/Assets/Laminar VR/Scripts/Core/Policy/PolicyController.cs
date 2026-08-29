using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LaminarVR.AdaptiveMeditation.Environment;
using LaminarVR.AdaptiveMeditation.Physiology;
using LaminarVR.AdaptiveMeditation.Rewards;
using LaminarVR.AdaptiveMeditation.Safety;
using LaminarVR.AdaptiveMeditation.Session;
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
            TelemetryRecorder telemetry)
        {
            this.policy = policy
                ?? throw new ArgumentNullException(nameof(policy));
            this.safetyValidator = safetyValidator
                ?? throw new ArgumentNullException(nameof(safetyValidator));
            this.environmentManager = environmentManager
                ?? throw new ArgumentNullException(nameof(environmentManager));
            this.sceneProfile = sceneProfile
                ?? throw new ArgumentNullException(nameof(sceneProfile));
            if (!sceneProfile.Limits.Contains(environmentManager.CurrentState))
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

            await RecordAsync(
                TelemetryEventTypes.DecisionRequested,
                utcTimestampUnixSeconds,
                sessionElapsedSeconds,
                false,
                new[]
                {
                    TelemetryField.String("decision_id", decisionId.Trim()),
                    TelemetryField.Integer(
                        "decision_sequence",
                        opportunity.SequenceNumber),
                    TelemetryField.String("policy_id", policy.PolicyId),
                    TelemetryField.String("phase", phase.ToString())
                },
                cancellationToken).ConfigureAwait(false);

            if (phase != VrSessionPhase.Adaptive)
            {
                return Skipped(
                    PolicyDecisionCycleResultCode.SkippedInvalidPhase);
            }

            if (!networkConnected)
            {
                return Skipped(
                    PolicyDecisionCycleResultCode
                        .SkippedNetworkUnavailable);
            }

            if (environmentManager.IsTransitionActive)
            {
                return Skipped(
                    PolicyDecisionCycleResultCode.SkippedTransitionActive);
            }

            if (pendingDecision != null || attributionTracker.HasPending)
            {
                return Skipped(
                    PolicyDecisionCycleResultCode.SkippedRewardPending);
            }

            if (!physiologyBuffer.TryGetLatestUsable(
                PhysiologyDataUse.Decision,
                opportunity.MonotonicTimeSeconds,
                latestDecisionPhysiologySequenceNumber,
                out var physiology,
                out var physiologyQueryResult))
            {
                return new PolicyDecisionCycleResult(
                    PolicyDecisionCycleResultCode
                        .SkippedPhysiologyUnavailable,
                    null,
                    null,
                    physiologyQueryResult);
            }

            var recentWindows = physiologyBuffer.GetRecentAccepted(
                rewardConfiguration.TrendWindowCount);
            var trend = PhysiologyTrendCalculator.Calculate(
                recentWindows,
                rewardConfiguration.MinimumTrendSamples);
            var observation = new PolicyObservation(
                physiology,
                preferredEnvironment,
                environmentManager.CurrentState,
                sceneProfile.SafeDefault,
                trend);
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
            policy.ObserveOutcome(
                new ActionOutcome(
                    completedDecision.DecisionId,
                    completedDecision.Decision,
                    match.Request.ExecutedAction,
                    calculation.Breakdown.TotalReward,
                    match.Request.PreActionPhysiology.SequenceNumber,
                    match.PostActionPhysiology.SequenceNumber));
            pendingDecision = null;

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
            return new PolicyRewardCycleResult(
                PolicyRewardCycleResultCode.RewardApplied,
                attributionCode,
                calculation);
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
            await RecordAsync(
                TelemetryEventTypes.ActionProposed,
                utcTimestampUnixSeconds,
                sessionElapsedSeconds,
                false,
                new[]
                {
                    TelemetryField.String("decision_id", decisionId.Trim()),
                    TelemetryField.String(
                        "proposed_action",
                        decision.SelectedAction.ToString()),
                    TelemetryField.String(
                        "reason_code",
                        decision.ReasonCode),
                    TelemetryField.Integer(
                        "physiology_sequence",
                        decision.PhysiologySequenceNumber)
                },
                cancellationToken).ConfigureAwait(false);

            for (var index = 0;
                index < decision.CandidateScoreCount;
                index++)
            {
                var score = decision.GetCandidateScore(index);
                await RecordAsync(
                    TelemetryEventTypes.PolicyCandidateScore,
                    utcTimestampUnixSeconds,
                    sessionElapsedSeconds,
                    false,
                    new[]
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
                            score.Uncertainty)
                    },
                    cancellationToken).ConfigureAwait(false);
            }
        }

        private Task RecordActionValidationAsync(
            string decisionId,
            ActionValidationResult validation,
            double utcTimestampUnixSeconds,
            double sessionElapsedSeconds,
            CancellationToken cancellationToken)
        {
            return RecordAsync(
                TelemetryEventTypes.ActionValidated,
                utcTimestampUnixSeconds,
                sessionElapsedSeconds,
                !validation.Accepted,
                new[]
                {
                    TelemetryField.String("decision_id", decisionId.Trim()),
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
                },
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

        private static PolicyDecisionCycleResult Skipped(
            PolicyDecisionCycleResultCode resultCode)
        {
            return new PolicyDecisionCycleResult(
                resultCode,
                null,
                null,
                null);
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
