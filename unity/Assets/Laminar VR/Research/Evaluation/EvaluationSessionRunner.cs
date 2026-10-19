using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LaminarVR.AdaptiveMeditation.Environment;
using LaminarVR.AdaptiveMeditation.Physiology;
using LaminarVR.AdaptiveMeditation.Policy;
using LaminarVR.AdaptiveMeditation.Policy.ContextualBandit;
using LaminarVR.AdaptiveMeditation.Rewards;
using LaminarVR.AdaptiveMeditation.Safety;
using LaminarVR.AdaptiveMeditation.Session;
using LaminarVR.AdaptiveMeditation.Stabilization;
using LaminarVR.AdaptiveMeditation.Telemetry;

namespace LaminarVR.AdaptiveMeditation.ResearchEvaluation
{
    public sealed class EvaluationSessionRunner
    {
        private const double TimeTolerance = 1e-8d;

        public async Task<EvaluationSessionResult> RunAsync(
            EvaluationSessionRequest request,
            CancellationToken cancellationToken = default)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            var bundle = request.Bundle;
            var safePreferenceState = bundle.Scene.Limits.Clamp(
                request.PreferredEnvironment);
            var initialState = bundle.CreateInitialState(
                request.PreferredEnvironment);
            var adapter = new RecordingEvaluationSceneAdapter(bundle.Scene);
            var environmentManager = new EnvironmentParameterManager(
                initialState,
                adapter);
            var physiologyBuffer = new PhysiologyStateBuffer(
                bundle.Physiology);
            var baselineAccumulator = new PhysiologyBaselineAccumulator();
            var telemetrySink = new InMemoryTelemetryEventSink();
            var nextTelemetryId = 0;
            var telemetry = new TelemetryRecorder(
                bundle.Telemetry,
                new TelemetrySessionIdentity(
                    request.SessionId,
                    request.ParticipantPseudonym),
                telemetrySink,
                () => request.SessionId
                    + "-event-"
                    + (++nextTelemetryId).ToString("D5"));
            var stabilizationSelector = new StabilizationStateSelector(
                bundle.Stabilization);
            var innerPolicy = request.CreatePolicy();
            var policy = new EvaluationObservationRecorderPolicy(innerPolicy);
            policy.Reset(new PolicyResetContext(PolicyResetReason.NewSession));
            if (request.PriorModelSnapshot != null)
            {
                var bandit = innerPolicy as ContextualBanditPolicy;
                if (bandit == null)
                {
                    throw new InvalidOperationException(
                        "Snapshot restore requires the contextual-bandit policy.");
                }
                var restore = bandit.TryRestoreModelSnapshot(
                    request.PriorModelSnapshot,
                    request.ParticipantPseudonym);
                if (!restore.Restored)
                {
                    throw new InvalidOperationException(
                        "Longitudinal model restore failed: "
                        + restore.Code);
                }
            }
            request.PhysiologyModel.Initialize(
                new SyntheticPhysiologySessionContext(
                    request.SessionId,
                    request.BaseUtcUnixSeconds,
                    initialState));
            var safeOracle = request.PhysiologyModel
                    is ISyntheticSafeOracleModel oracleModel
                ? new SafeOracleEvaluator(bundle.Scene, oracleModel)
                : null;

            var physiologyRecords = new List<EvaluationPhysiologyRecord>();
            var decisions = new List<EvaluationDecisionRecord>();
            var rewards = new List<EvaluationRewardRecord>();
            var deliveries = new List<PendingSyntheticDelivery>();

            var phase = VrSessionPhase.Acclimatization;
            var acclimatizationEnd = bundle.Timing
                .AcclimatizationDurationSeconds;
            var adaptiveEnd = acclimatizationEnd
                + bundle.Timing.AdaptiveDurationSeconds;
            var sessionEnd = bundle.Timing.TimedSessionDurationSeconds;
            var nextEmission = bundle.Coordinator
                .ExpectedPhysiologyOutputIntervalSeconds;
            var nextDecision = acclimatizationEnd
                + bundle.Timing.DecisionIntervalSeconds;
            var emissionSequence = 0;
            var decisionSequence = 0;
            double? transitionCompletion = null;
            PendingExposureSeed? exposureSeed = null;
            SyntheticActionExposure? unconsumedExposure = null;
            PolicyController controller = null;
            var baselineAvailable = false;
            var selectedStabilizationState = initialState;
            var nextNetworkChange = request.NetworkSchedule
                .GetNextChangeAfter(0d);

            while (phase != VrSessionPhase.Completed)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var eventTime = FindNextEventTime(
                    phase,
                    acclimatizationEnd,
                    adaptiveEnd,
                    sessionEnd,
                    nextEmission,
                    nextDecision,
                    transitionCompletion,
                    nextNetworkChange,
                    deliveries);
                var networkConnected = request.NetworkSchedule.IsConnected(
                    eventTime);

                if (nextNetworkChange.HasValue
                    && SameTime(eventTime, nextNetworkChange.Value))
                {
                    if (!networkConnected && controller != null)
                    {
                        await controller.InvalidatePendingAsync(
                            RewardAttributionInvalidationReason.NetworkLoss,
                            eventTime,
                            ToUtc(request, eventTime),
                            eventTime,
                            cancellationToken).ConfigureAwait(false);
                        transitionCompletion = null;
                        exposureSeed = null;
                        unconsumedExposure = null;
                    }

                    nextNetworkChange = request.NetworkSchedule
                        .GetNextChangeAfter(eventTime);
                }

                if (transitionCompletion.HasValue
                    && SameTime(eventTime, transitionCompletion.Value))
                {
                    var progress = await controller.AdvanceTransitionAsync(
                        eventTime,
                        ToUtc(request, eventTime),
                        eventTime,
                        phase,
                        networkConnected,
                        cancellationToken).ConfigureAwait(false);
                    if (progress.Status
                            == EnvironmentTransitionStatus.Completed
                        && exposureSeed.HasValue)
                    {
                        var seed = exposureSeed.Value;
                        unconsumedExposure = new SyntheticActionExposure(
                            seed.DecisionId,
                            seed.Action,
                            seed.EnvironmentBefore,
                            environmentManager.CurrentState,
                            eventTime);
                    }

                    exposureSeed = null;
                    transitionCompletion = null;
                }

                if (nextEmission <= sessionEnd + TimeTolerance
                    && SameTime(eventTime, nextEmission))
                {
                    emissionSequence++;
                    var eligibleExposure = GetEligibleExposure(
                        unconsumedExposure,
                        nextEmission,
                        bundle.Reward.SettlingSeconds,
                        EvaluationRuntimeBundle
                            .SyntheticWindowDurationSeconds);
                    var emission = request.PhysiologyModel.CreateEmission(
                        new SyntheticPhysiologyRequest(
                            emissionSequence,
                            nextEmission,
                            EvaluationRuntimeBundle
                                .SyntheticWindowDurationSeconds,
                            eligibleExposure));
                    if (eligibleExposure.HasValue)
                    {
                        unconsumedExposure = null;
                    }

                    if (emission.Dropped)
                    {
                        physiologyRecords.Add(
                            new EvaluationPhysiologyRecord(
                                emissionSequence,
                                nextEmission,
                                true,
                                null,
                                null));
                    }
                    else
                    {
                        if (emission.DeliveryMonotonicTimeSeconds
                            + TimeTolerance < nextEmission)
                        {
                            throw new InvalidOperationException(
                                "Synthetic physiology cannot arrive before emission.");
                        }

                        deliveries.Add(new PendingSyntheticDelivery(
                            emissionSequence,
                            emission));
                    }

                    nextEmission += bundle.Coordinator
                        .ExpectedPhysiologyOutputIntervalSeconds;
                }

                SortDeliveries(deliveries);
                while (deliveries.Count > 0
                    && deliveries[0].Emission.DeliveryMonotonicTimeSeconds
                        <= eventTime + TimeTolerance)
                {
                    var delivery = deliveries[0];
                    deliveries.RemoveAt(0);
                    var receiptTime = Math.Max(
                        eventTime,
                        delivery.Emission.DeliveryMonotonicTimeSeconds);
                    var ingestion = physiologyBuffer.Ingest(
                        delivery.Emission.Window,
                        ToUtc(request, receiptTime),
                        receiptTime);
                    physiologyRecords.Add(new EvaluationPhysiologyRecord(
                        delivery.EmissionSequence,
                        receiptTime,
                        false,
                        delivery.Emission.Window,
                        ingestion));

                    if (phase == VrSessionPhase.Acclimatization
                        && ingestion.Accepted
                        && physiologyBuffer.TryGetAcceptedBySequence(
                            ingestion.AcceptedSequenceNumber,
                            out var baselineWindow))
                    {
                        baselineAccumulator.TryAdd(
                            new PhysiologyWindowSnapshot(
                                ingestion.AcceptedSequenceNumber,
                                baselineWindow,
                                0d,
                                receiptTime));
                    }

                    if (phase == VrSessionPhase.Adaptive
                        && controller != null)
                    {
                        var reward = await controller.TryResolveRewardAsync(
                            receiptTime,
                            ToUtc(request, receiptTime),
                            receiptTime,
                            phase,
                            networkConnected,
                            cancellationToken).ConfigureAwait(false);
                        if (reward.ResultCode
                            != PolicyRewardCycleResultCode.NoPending
                            && reward.ResultCode
                            != PolicyRewardCycleResultCode.Waiting)
                        {
                            rewards.Add(new EvaluationRewardRecord(
                                receiptTime,
                                reward));
                        }
                    }
                }

                if (phase == VrSessionPhase.Acclimatization
                    && SameTime(eventTime, acclimatizationEnd))
                {
                    baselineAvailable = baselineAccumulator.AcceptedWindowCount
                        >= bundle.Reward.MinimumBaselineSamples;
                    if (baselineAvailable)
                    {
                        controller = new PolicyController(
                            policy,
                            new ActionSafetyValidator(),
                            environmentManager,
                            bundle.Scene,
                            bundle.Coordinator.SafetyLimits,
                            physiologyBuffer,
                            bundle.Reward,
                            baselineAccumulator.CreateBaseline(
                                bundle.Reward.BaselineStandardDeviationMethod),
                            telemetry,
                            null,
                            stabilizationSelector);
                    }

                    phase = VrSessionPhase.Adaptive;
                }

                if (phase == VrSessionPhase.Adaptive
                    && nextDecision < adaptiveEnd - TimeTolerance
                    && SameTime(eventTime, nextDecision))
                {
                    decisionSequence++;
                    if (controller != null)
                    {
                        var stateBefore = environmentManager.CurrentState;
                        var decisionId = request.SessionId
                            + "-decision-"
                            + decisionSequence.ToString("D2");
                        var result = await controller.ProcessDecisionAsync(
                            decisionId,
                            new SessionDecisionOpportunity(
                                decisionSequence,
                                nextDecision,
                                nextDecision - acclimatizationEnd),
                            phase,
                            networkConnected,
                            safePreferenceState,
                            ToUtc(request, nextDecision),
                            nextDecision,
                            cancellationToken).ConfigureAwait(false);
                        SafeOracleDecisionRecord? oracleRecord = null;
                        if (policy.TryTakeLatestObservation(
                                out var policyObservation))
                        {
                            if (result.Decision == null)
                            {
                                throw new InvalidOperationException(
                                    "A selected policy observation has no decision.");
                            }

                            if (safeOracle != null)
                            {
                                oracleRecord = safeOracle.Evaluate(
                                    policyObservation,
                                    result.Decision.SelectedAction,
                                    result.Validation.HasValue
                                        ? result.Validation.Value.ExecutedAction
                                        : result.Decision.SelectedAction,
                                    nextDecision);
                            }
                        }

                        decisions.Add(new EvaluationDecisionRecord(
                            decisionSequence,
                            nextDecision,
                            result,
                            oracleRecord));

                        if (result.Validation.HasValue
                            && result.Validation.Value.Accepted)
                        {
                            var validation = result.Validation.Value;
                            if (validation.ExecutedAction
                                == EnvironmentAction.NoChange)
                            {
                                unconsumedExposure =
                                    new SyntheticActionExposure(
                                        decisionId,
                                        validation.ExecutedAction,
                                        stateBefore,
                                        stateBefore,
                                        nextDecision);
                            }
                            else if (result.ResultCode
                                == PolicyDecisionCycleResultCode
                                    .TransitionStarted)
                            {
                                transitionCompletion = nextDecision
                                    + bundle.Scene.TransitionDurationSeconds;
                                exposureSeed = new PendingExposureSeed(
                                    decisionId,
                                    validation.ExecutedAction,
                                    stateBefore);
                            }
                        }
                    }

                    nextDecision += bundle.Timing.DecisionIntervalSeconds;
                }

                if (phase == VrSessionPhase.Adaptive
                    && SameTime(eventTime, adaptiveEnd))
                {
                    if (controller != null)
                    {
                        await controller.InvalidatePendingAsync(
                            RewardAttributionInvalidationReason.SessionEnded,
                            eventTime,
                            ToUtc(request, eventTime),
                            eventTime,
                            cancellationToken).ConfigureAwait(false);
                        var selection = await controller
                            .SelectStabilizationStateAsync(
                                VrSessionPhase.Stabilization,
                                safePreferenceState,
                                initialState,
                                ToUtc(request, eventTime),
                                eventTime,
                                cancellationToken).ConfigureAwait(false);
                        selectedStabilizationState = selection.SelectedState;
                    }

                    phase = VrSessionPhase.Stabilization;
                    transitionCompletion = null;
                    exposureSeed = null;
                    unconsumedExposure = null;
                }

                if (phase == VrSessionPhase.Stabilization
                    && SameTime(eventTime, sessionEnd))
                {
                    phase = VrSessionPhase.Completed;
                }
            }

            await telemetry.FlushAsync(cancellationToken).ConfigureAwait(false);
            LinUcbModelSnapshot modelSnapshot = null;
            if (request.ProtocolId != EvaluationRuntimeBundle.ProtocolId
                && innerPolicy is ContextualBanditPolicy trainedBandit)
            {
                modelSnapshot = trainedBandit.CaptureModelSnapshot(
                    request.SessionId + "-final-model",
                    request.ParticipantPseudonym,
                    request.PriorModelSnapshot?.CreatedUtcUnixSeconds
                        ?? request.BaseUtcUnixSeconds,
                    request.BaseUtcUnixSeconds + sessionEnd,
                    "longitudinal-simulation");
            }
            return new EvaluationSessionResult(
                request.ProtocolId,
                request.ProfileBundleId,
                request.PhysiologyModel.ModelId,
                initialState,
                environmentManager.CurrentState,
                selectedStabilizationState,
                baselineAvailable,
                policy.CaptureState(),
                physiologyRecords.ToArray(),
                decisions.ToArray(),
                rewards.ToArray(),
                telemetrySink.CopyEvents(),
                adapter.CopyAppliedStates(),
                adapter.AllAppliedStatesSafe,
                modelSnapshot);
        }

        private static double FindNextEventTime(
            VrSessionPhase phase,
            double acclimatizationEnd,
            double adaptiveEnd,
            double sessionEnd,
            double nextEmission,
            double nextDecision,
            double? transitionCompletion,
            double? nextNetworkChange,
            List<PendingSyntheticDelivery> deliveries)
        {
            var next = phase == VrSessionPhase.Acclimatization
                ? acclimatizationEnd
                : phase == VrSessionPhase.Adaptive
                    ? adaptiveEnd
                    : sessionEnd;
            if (nextEmission <= sessionEnd + TimeTolerance)
            {
                next = Math.Min(next, nextEmission);
            }

            if (phase == VrSessionPhase.Adaptive
                && nextDecision < adaptiveEnd - TimeTolerance)
            {
                next = Math.Min(next, nextDecision);
            }

            if (transitionCompletion.HasValue)
            {
                next = Math.Min(next, transitionCompletion.Value);
            }

            if (nextNetworkChange.HasValue)
            {
                next = Math.Min(next, nextNetworkChange.Value);
            }

            for (var index = 0; index < deliveries.Count; index++)
            {
                next = Math.Min(
                    next,
                    deliveries[index].Emission.DeliveryMonotonicTimeSeconds);
            }

            return next;
        }

        private static SyntheticActionExposure? GetEligibleExposure(
            SyntheticActionExposure? exposure,
            double emissionTime,
            double settlingSeconds,
            double windowDurationSeconds)
        {
            if (!exposure.HasValue)
            {
                return null;
            }

            var windowStart = emissionTime - windowDurationSeconds;
            return windowStart + TimeTolerance
                    >= exposure.Value.CompletedMonotonicTimeSeconds
                        + settlingSeconds
                ? exposure
                : null;
        }

        private static void SortDeliveries(
            List<PendingSyntheticDelivery> deliveries)
        {
            deliveries.Sort((left, right) =>
            {
                var timeComparison = left.Emission
                    .DeliveryMonotonicTimeSeconds.CompareTo(
                        right.Emission.DeliveryMonotonicTimeSeconds);
                return timeComparison != 0
                    ? timeComparison
                    : left.EmissionSequence.CompareTo(right.EmissionSequence);
            });
        }

        private static double ToUtc(
            EvaluationSessionRequest request,
            double monotonicSeconds)
        {
            return request.BaseUtcUnixSeconds + monotonicSeconds;
        }

        private static bool SameTime(double left, double right)
        {
            return Math.Abs(left - right) <= TimeTolerance;
        }

        private readonly struct PendingExposureSeed
        {
            public PendingExposureSeed(
                string decisionId,
                EnvironmentAction action,
                EnvironmentState environmentBefore)
            {
                DecisionId = decisionId;
                Action = action;
                EnvironmentBefore = environmentBefore;
            }

            public string DecisionId { get; }
            public EnvironmentAction Action { get; }
            public EnvironmentState EnvironmentBefore { get; }
        }
    }
}
