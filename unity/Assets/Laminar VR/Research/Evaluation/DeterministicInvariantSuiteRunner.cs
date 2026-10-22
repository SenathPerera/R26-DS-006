using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using LaminarVR.AdaptiveMeditation.Environment;
using LaminarVR.AdaptiveMeditation.Physiology;
using LaminarVR.AdaptiveMeditation.Policy;
using LaminarVR.AdaptiveMeditation.Rewards;
using LaminarVR.AdaptiveMeditation.Runtime.Networking;
using LaminarVR.AdaptiveMeditation.Safety;
using LaminarVR.AdaptiveMeditation.Session;

namespace LaminarVR.AdaptiveMeditation.ResearchEvaluation
{
    public sealed class InvariantCaseRecord
    {
        public InvariantCaseRecord(
            string caseId,
            string category,
            string exactInputJson,
            string proposedAction,
            string validationOutcome,
            EnvironmentState? executedState,
            string rewardDisposition,
            long modelUpdateCount,
            string expected,
            string actual,
            bool passed)
        {
            CaseId = caseId ?? throw new ArgumentNullException(nameof(caseId));
            Category = category ?? throw new ArgumentNullException(nameof(category));
            ExactInputJson = exactInputJson
                ?? throw new ArgumentNullException(nameof(exactInputJson));
            ProposedAction = proposedAction;
            ValidationOutcome = validationOutcome
                ?? throw new ArgumentNullException(nameof(validationOutcome));
            ExecutedState = executedState;
            RewardDisposition = rewardDisposition
                ?? throw new ArgumentNullException(nameof(rewardDisposition));
            ModelUpdateCount = modelUpdateCount;
            Expected = expected ?? throw new ArgumentNullException(nameof(expected));
            Actual = actual ?? throw new ArgumentNullException(nameof(actual));
            Passed = passed;
        }

        public string CaseId { get; }
        public string Category { get; }
        public string ExactInputJson { get; }
        public string ProposedAction { get; }
        public string ValidationOutcome { get; }
        public EnvironmentState? ExecutedState { get; }
        public string RewardDisposition { get; }
        public long ModelUpdateCount { get; }
        public string Expected { get; }
        public string Actual { get; }
        public bool Passed { get; }
    }

    public sealed class DeterministicInvariantSuiteResult
    {
        public DeterministicInvariantSuiteResult(InvariantCaseRecord[] cases)
        {
            Cases = cases ?? throw new ArgumentNullException(nameof(cases));
            var passed = 0;
            for (var index = 0; index < cases.Length; index++)
            {
                if (cases[index].Passed)
                {
                    passed++;
                }
            }

            PassedCaseCount = passed;
        }

        public InvariantCaseRecord[] Cases { get; }
        public int PassedCaseCount { get; }
        public bool AllPassed => PassedCaseCount == Cases.Length;
    }

    public sealed class DeterministicInvariantSuiteRunner
    {
        public const int ExpectedCaseCount = 206;

        private const float InvariantNarrowSceneMinimum = 0.2f;
        private const float InvariantNarrowSceneMaximum = 0.8f;

        private static readonly CultureInfo Invariant =
            CultureInfo.InvariantCulture;

        public DeterministicInvariantSuiteResult Run(
            EvaluationRuntimeBundle bundle,
            IProgress<EvaluationRunProgress> progress = null,
            CancellationToken cancellationToken = default)
        {
            if (bundle == null)
            {
                throw new ArgumentNullException(nameof(bundle));
            }

            var records = new List<InvariantCaseRecord>(ExpectedCaseCount);
            RunSafetyCases(bundle, records, progress, cancellationToken);
            RunAttributionCases(bundle, records, progress, cancellationToken);
            RunPhysiologyCases(bundle, records, progress, cancellationToken);
            RunSessionCases(bundle, records, progress, cancellationToken);
            if (records.Count != ExpectedCaseCount)
            {
                throw new InvalidOperationException(
                    "The deterministic invariant catalog produced "
                    + records.Count
                    + " cases; expected "
                    + ExpectedCaseCount
                    + ".");
            }

            return new DeterministicInvariantSuiteResult(records.ToArray());
        }

        private static void RunSafetyCases(
            EvaluationRuntimeBundle bundle,
            List<InvariantCaseRecord> records,
            IProgress<EvaluationRunProgress> progress,
            CancellationToken cancellationToken)
        {
            var validator = new ActionSafetyValidator();
            var middle = Midpoint(bundle.Scene.Limits);
            var clippingScene = CreateClippingSceneProfile(bundle.Scene);
            foreach (EnvironmentAction action in Enum.GetValues(
                typeof(EnvironmentAction)))
            {
                AddSafetyCase(
                    "safety-nominal-" + action,
                    "safety.nominal",
                    action,
                    middle,
                    SafetyRuntimeState.Ready,
                    bundle,
                    validator,
                    action == EnvironmentAction.NoChange
                        ? ActionValidationReasonCode.Accepted
                        : ActionValidationReasonCode.Accepted,
                    true,
                    false,
                    records,
                    progress,
                    cancellationToken);
            }

            foreach (var action in DirectionalActions())
            {
                AddSafetyCase(
                    "safety-boundary-" + action,
                    "safety.boundary",
                    action,
                    AtBoundary(middle, bundle.Scene.Limits, action),
                    SafetyRuntimeState.Ready,
                    bundle,
                    validator,
                    ActionValidationReasonCode.ParameterAtBoundary,
                    false,
                    true,
                    records,
                    progress,
                    cancellationToken);
                AddSafetyCase(
                    "safety-clipped-" + action,
                    "safety.scene_clipping",
                    action,
                    NearBoundary(
                        Midpoint(clippingScene.Limits),
                        clippingScene.Limits,
                        clippingScene.ActionSteps,
                        action),
                    SafetyRuntimeState.Ready,
                    bundle,
                    validator,
                    ActionValidationReasonCode.RangeClipped,
                    true,
                    true,
                    records,
                    progress,
                    cancellationToken,
                    clippingScene);
            }

            foreach (SafetyBlockReason blockReason in Enum.GetValues(
                typeof(SafetyBlockReason)))
            {
                if (blockReason == SafetyBlockReason.None)
                {
                    continue;
                }

                foreach (EnvironmentAction action in Enum.GetValues(
                    typeof(EnvironmentAction)))
                {
                    AddSafetyCase(
                        "safety-gate-" + blockReason + "-" + action,
                        "safety.gate",
                        action,
                        middle,
                        new SafetyRuntimeState(blockReason, null, 0, 0d),
                        bundle,
                        validator,
                        MapBlockReason(blockReason),
                        false,
                        action != EnvironmentAction.NoChange,
                        records,
                        progress,
                        cancellationToken);
                }
            }

            foreach (var action in DirectionalActions())
            {
                AddSafetyCase(
                    "safety-consecutive-limit-" + action,
                    "safety.consecutive_limit",
                    action,
                    middle,
                    new SafetyRuntimeState(
                        SafetyBlockReason.None,
                        action,
                        bundle.Coordinator.SafetyLimits
                            .MaximumConsecutiveSameDirectionActions,
                        0d),
                    bundle,
                    validator,
                    ActionValidationReasonCode.ConsecutiveDirectionLimit,
                    false,
                    true,
                    records,
                    progress,
                    cancellationToken);
                AddSafetyCase(
                    "safety-total-variation-limit-" + action,
                    "safety.total_variation_limit",
                    action,
                    middle,
                    new SafetyRuntimeState(
                        SafetyBlockReason.None,
                        null,
                        0,
                        bundle.Coordinator.SafetyLimits.MaximumTotalVariation),
                    bundle,
                    validator,
                    ActionValidationReasonCode.TotalVariationLimit,
                    false,
                    true,
                    records,
                    progress,
                    cancellationToken);
            }

            AddSafetyCase(
                "safety-invalid-action",
                "safety.configuration",
                (EnvironmentAction)99,
                middle,
                SafetyRuntimeState.Ready,
                bundle,
                validator,
                ActionValidationReasonCode.ConfigurationError,
                false,
                true,
                records,
                progress,
                cancellationToken);
            var outside = new EnvironmentState(
                -0.01f,
                middle.Warmth,
                middle.AtmosphericSoftness,
                middle.ColorRichness,
                middle.AmbientMotion);
            AddSafetyCase(
                "safety-current-state-outside-scene",
                "safety.configuration",
                EnvironmentAction.NoChange,
                outside,
                SafetyRuntimeState.Ready,
                bundle,
                validator,
                ActionValidationReasonCode.ConfigurationError,
                false,
                false,
                records,
                progress,
                cancellationToken);
        }

        private static void AddSafetyCase(
            string caseId,
            string category,
            EnvironmentAction action,
            EnvironmentState current,
            SafetyRuntimeState runtime,
            EvaluationRuntimeBundle bundle,
            ActionSafetyValidator validator,
            ActionValidationReasonCode expectedReason,
            bool expectedAccepted,
            bool expectedModified,
            List<InvariantCaseRecord> records,
            IProgress<EvaluationRunProgress> progress,
            CancellationToken cancellationToken,
            SceneEnvironmentProfile sceneProfile = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var activeScene = sceneProfile ?? bundle.Scene;
            var result = validator.Validate(
                action,
                current,
                activeScene,
                runtime,
                bundle.Coordinator.SafetyLimits);
            var passed = result.ReasonCode == expectedReason
                && result.Accepted == expectedAccepted
                && result.Modified == expectedModified
                && activeScene.Limits.Contains(result.SafeTarget)
                == activeScene.Limits.Contains(current)
                && (result.Accepted
                    || result.ExecutedAction == EnvironmentAction.NoChange);
            var input = "{\"current_state\":"
                + StateJson(current)
                + ",\"scene_limits\":"
                + LimitsJson(activeScene.Limits)
                + ",\"block_reason\":\""
                + runtime.BlockReason
                + "\",\"previous_action\":"
                + NullableStringJson(runtime.PreviousExecutedAction?.ToString())
                + ",\"consecutive_count\":"
                + runtime.ConsecutiveSameDirectionActions
                + ",\"total_variation\":"
                + Format(runtime.TotalVariation)
                + "}";
            Add(records, new InvariantCaseRecord(
                caseId,
                category,
                input,
                action.ToString(),
                result.ReasonCode.ToString(),
                result.SafeTarget,
                result.Accepted ? "eligible" : "not_opened",
                0L,
                expectedReason + "/accepted=" + expectedAccepted
                    + "/modified=" + expectedModified,
                result.ReasonCode + "/accepted=" + result.Accepted
                    + "/modified=" + result.Modified,
                passed), progress);
        }

        private static SceneEnvironmentProfile CreateClippingSceneProfile(
            SceneEnvironmentProfile source)
        {
            var narrow = new NormalizedRange(
                InvariantNarrowSceneMinimum,
                InvariantNarrowSceneMaximum);
            var limits = new EnvironmentStateLimits(
                narrow,
                narrow,
                narrow,
                narrow,
                narrow);
            return new SceneEnvironmentProfile(
                source.SceneId + "-invariant-narrow",
                source.DisplayName + " invariant narrow limits",
                Midpoint(limits),
                limits,
                source.ActionSteps,
                source.TransitionDurationSeconds,
                source.MinimumSecondsBetweenActions);
        }

        private static void RunAttributionCases(
            EvaluationRuntimeBundle bundle,
            List<InvariantCaseRecord> records,
            IProgress<EvaluationRunProgress> progress,
            CancellationToken cancellationToken)
        {
            foreach (VrSessionPhase phase in Enum.GetValues(
                typeof(VrSessionPhase)))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var context = CreateAttributionContext(bundle);
                var opened = context.Tracker.TryOpen(
                    context.Request,
                    phase,
                    true,
                    out var code);
                var expected = phase == VrSessionPhase.Adaptive
                    ? RewardAttributionOpenResultCode.Opened
                    : RewardAttributionOpenResultCode.InvalidPhase;
                Add(records, AttributionRecord(
                    "attribution-open-phase-" + phase,
                    "attribution.phase_open",
                    "{\"phase\":\"" + phase + "\",\"network_connected\":true}",
                    code.ToString(),
                    context,
                    expected.ToString(),
                    code == expected && opened == (phase == VrSessionPhase.Adaptive)),
                    progress);
            }

            foreach (VrSessionPhase phase in Enum.GetValues(
                typeof(VrSessionPhase)))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var context = CreateAttributionContext(bundle);
                Open(context);
                var matched = context.Tracker.TryResolve(
                    context.Buffer,
                    21d,
                    phase,
                    true,
                    out _,
                    out var code);
                var expected = phase == VrSessionPhase.Adaptive
                    ? RewardAttributionResolutionCode.WaitingForSettling
                    : RewardAttributionResolutionCode.InvalidatedForPhase;
                Add(records, AttributionRecord(
                    "attribution-resolve-phase-" + phase,
                    "attribution.phase_resolve",
                    "{\"phase\":\"" + phase + "\",\"network_connected\":true}",
                    code.ToString(),
                    context,
                    expected.ToString(),
                    !matched && code == expected), progress);
            }

            RunAttributionNetworkCases(bundle, records, progress, cancellationToken);
            RunAttributionWindowCases(bundle, records, progress, cancellationToken);
        }

        private static void RunAttributionNetworkCases(
            EvaluationRuntimeBundle bundle,
            List<InvariantCaseRecord> records,
            IProgress<EvaluationRunProgress> progress,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var openContext = CreateAttributionContext(bundle);
            var opened = openContext.Tracker.TryOpen(
                openContext.Request,
                VrSessionPhase.Adaptive,
                false,
                out var openCode);
            Add(records, AttributionRecord(
                "attribution-open-network-unavailable",
                "attribution.network",
                "{\"phase\":\"Adaptive\",\"network_connected\":false}",
                openCode.ToString(),
                openContext,
                RewardAttributionOpenResultCode.NetworkUnavailable.ToString(),
                !opened
                    && openCode
                        == RewardAttributionOpenResultCode.NetworkUnavailable),
                progress);

            var resolveContext = CreateAttributionContext(bundle);
            Open(resolveContext);
            var resolved = resolveContext.Tracker.TryResolve(
                resolveContext.Buffer,
                26d,
                VrSessionPhase.Adaptive,
                false,
                out _,
                out var resolveCode);
            Add(records, AttributionRecord(
                "attribution-resolve-network-loss",
                "attribution.network",
                "{\"phase\":\"Adaptive\",\"network_connected\":false}",
                resolveCode.ToString(),
                resolveContext,
                RewardAttributionResolutionCode.InvalidatedForNetwork.ToString(),
                !resolved
                    && resolveCode
                        == RewardAttributionResolutionCode.InvalidatedForNetwork),
                progress);
        }

        private static void RunAttributionWindowCases(
            EvaluationRuntimeBundle bundle,
            List<InvariantCaseRecord> records,
            IProgress<EvaluationRunProgress> progress,
            CancellationToken cancellationToken)
        {
            RunResolutionCase(
                bundle,
                "attribution-overlapping-window",
                "attribution.window_overlap",
                CreateWindow(1039d),
                30d,
                RewardAttributionResolutionCode
                    .WindowOverlapsTransitionOrSettling,
                false,
                records,
                progress,
                cancellationToken);
            RunResolutionCase(
                bundle,
                "attribution-low-quality-window",
                "attribution.signal_quality",
                CreateWindow(1040d, 0.1d),
                30d,
                RewardAttributionResolutionCode.WindowNotRewardUsable,
                false,
                records,
                progress,
                cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();
            var timeoutContext = CreateAttributionContext(bundle);
            Open(timeoutContext);
            var timedOut = timeoutContext.Tracker.TryResolve(
                timeoutContext.Buffer,
                20d + bundle.Reward.MaximumAttributionWaitSeconds + 0.01d,
                VrSessionPhase.Adaptive,
                true,
                out _,
                out var timeoutCode);
            Add(records, AttributionRecord(
                "attribution-timeout",
                "attribution.timeout",
                "{\"current_monotonic_seconds\":"
                    + Format(20d + bundle.Reward.MaximumAttributionWaitSeconds + 0.01d)
                    + "}",
                timeoutCode.ToString(),
                timeoutContext,
                RewardAttributionResolutionCode.TimedOut.ToString(),
                !timedOut
                    && timeoutCode == RewardAttributionResolutionCode.TimedOut),
                progress);

            var matchedContext = RunResolutionCase(
                bundle,
                "attribution-valid-match",
                "attribution.valid",
                CreateWindow(1040d),
                30d,
                RewardAttributionResolutionCode.Matched,
                true,
                records,
                progress,
                cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();
            if (!matchedContext.Buffer.TryGetLatestAccepted(out var matchedWindow))
            {
                throw new InvalidOperationException("Matched window was not buffered.");
            }

            var secondRequest = new RewardAttributionRequest(
                "invariant-transition-2",
                matchedWindow,
                EnvironmentAction.NoChange,
                matchedContext.Environment,
                matchedContext.Environment,
                35d,
                1045d);
            var secondOpened = matchedContext.Tracker.TryOpen(
                secondRequest,
                VrSessionPhase.Adaptive,
                true,
                out var secondOpenCode);
            var reused = matchedContext.Tracker.TryResolve(
                matchedContext.Buffer,
                41d,
                VrSessionPhase.Adaptive,
                true,
                out _,
                out var reuseCode);
            var updateCount = matchedContext.Policy.CaptureState().ModelUpdateCount;
            Add(records, new InvariantCaseRecord(
                "attribution-consumed-window-not-reused",
                "attribution.window_reuse",
                "{\"pre_window_sequence\":2,\"latest_consumed_sequence\":2}",
                EnvironmentAction.NoChange.ToString(),
                "not_applicable",
                matchedContext.Environment,
                reuseCode.ToString(),
                updateCount,
                "Opened/WaitingForWindow/model_updates=1",
                secondOpenCode + "/" + reuseCode + "/model_updates=" + updateCount,
                secondOpened
                    && !reused
                    && secondOpenCode == RewardAttributionOpenResultCode.Opened
                    && reuseCode == RewardAttributionResolutionCode.WaitingForWindow
                    && updateCount == 1L), progress);
        }

        private static AttributionContext RunResolutionCase(
            EvaluationRuntimeBundle bundle,
            string caseId,
            string category,
            PhysiologyWindow postWindow,
            double currentMonotonicTime,
            RewardAttributionResolutionCode expectedCode,
            bool expectMatch,
            List<InvariantCaseRecord> records,
            IProgress<EvaluationRunProgress> progress,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var context = CreateAttributionContext(bundle);
            Open(context);
            var ingestion = context.Buffer.Ingest(
                postWindow,
                postWindow.WindowEndUtcUnixSeconds,
                currentMonotonicTime - 1d);
            if (!ingestion.Accepted)
            {
                throw new InvalidOperationException(
                    "Invariant post window was unexpectedly rejected: "
                    + ingestion.ResultCode
                    + "/"
                    + ingestion.ValidationReasonCode);
            }

            var matched = context.Tracker.TryResolve(
                context.Buffer,
                currentMonotonicTime,
                VrSessionPhase.Adaptive,
                true,
                out var match,
                out var code);
            var rewardDisposition = code.ToString();
            if (matched)
            {
                var validation = new ActionSafetyValidator().Validate(
                    EnvironmentAction.NoChange,
                    context.Environment,
                    bundle.Scene,
                    SafetyRuntimeState.Ready,
                    bundle.Coordinator.SafetyLimits);
                var calculation = new RewardCalculator(bundle.Reward).Calculate(
                    match.Request.PreActionPhysiology,
                    match.PostActionPhysiology,
                    CreateBaseline(bundle),
                    EnvironmentAction.NoChange,
                    context.Environment,
                    context.Environment,
                    RewardPenaltyObservation.FromActionValidation(validation));
                rewardDisposition += "/" + calculation.ResultCode;
                if (calculation.Valid)
                {
                    context.Policy.ObserveOutcome(new ActionOutcome(
                        "invariant-decision",
                        context.Decision,
                        EnvironmentAction.NoChange,
                        calculation.Breakdown.TotalReward,
                        match.Request.PreActionPhysiology.SequenceNumber,
                        match.PostActionPhysiology.SequenceNumber));
                }
            }

            var updates = context.Policy.CaptureState().ModelUpdateCount;
            var passed = matched == expectMatch
                && code == expectedCode
                && updates == (expectMatch ? 1L : 0L);
            Add(records, new InvariantCaseRecord(
                caseId,
                category,
                "{\"post_window_start_utc\":"
                    + Format(postWindow.WindowStartUtcUnixSeconds)
                    + ",\"post_window_end_utc\":"
                    + Format(postWindow.WindowEndUtcUnixSeconds)
                    + ",\"signal_quality\":"
                    + Format(postWindow.SignalQuality)
                    + "}",
                EnvironmentAction.NoChange.ToString(),
                "not_applicable",
                context.Environment,
                rewardDisposition,
                updates,
                expectedCode + "/model_updates=" + (expectMatch ? 1 : 0),
                rewardDisposition + "/model_updates=" + updates,
                passed), progress);
            return context;
        }

        private static void RunPhysiologyCases(
            EvaluationRuntimeBundle bundle,
            List<InvariantCaseRecord> records,
            IProgress<EvaluationRunProgress> progress,
            CancellationToken cancellationToken)
        {
            var validator = new PhysiologyWindowValidator(bundle.Physiology);
            AddPhysiologyValidationCase(
                "physiology-payload-missing",
                "{\"window\":null}",
                null,
                1000d,
                PhysiologyValidationReasonCode.PayloadMissing,
                validator,
                records,
                progress,
                cancellationToken);
            AddPhysiologyValidationCase(
                "physiology-window-too-short",
                "{\"duration_seconds\":"
                    + Format(bundle.Physiology.MinimumWindowDurationSeconds / 2d)
                    + "}",
                CreateWindow(
                    1000d,
                    durationSeconds:
                        bundle.Physiology.MinimumWindowDurationSeconds / 2d),
                1000d,
                PhysiologyValidationReasonCode.WindowTooShort,
                validator,
                records,
                progress,
                cancellationToken);
            AddPhysiologyValidationCase(
                "physiology-future-timestamp",
                "{\"future_seconds\":"
                    + Format(bundle.Physiology.MaximumFutureClockSkewSeconds + 1d)
                    + "}",
                CreateWindow(1000d),
                999d - bundle.Physiology.MaximumFutureClockSkewSeconds,
                PhysiologyValidationReasonCode.FutureTimestamp,
                validator,
                records,
                progress,
                cancellationToken);
            AddPhysiologyValidationCase(
                "physiology-stale-at-receipt",
                "{\"age_seconds\":"
                    + Format(bundle.Physiology.StaleAfterSeconds + 1d)
                    + "}",
                CreateWindow(1000d),
                1001d + bundle.Physiology.StaleAfterSeconds,
                PhysiologyValidationReasonCode.StaleAtReceipt,
                validator,
                records,
                progress,
                cancellationToken);
            AddPhysiologyValidationCase(
                "physiology-heart-rate-non-finite",
                "{\"heart_rate_bpm\":\"NaN\"}",
                CreateWindow(1000d, heartRateBpm: double.NaN),
                1000d,
                PhysiologyValidationReasonCode.HeartRateInvalid,
                validator,
                records,
                progress,
                cancellationToken);
            AddPhysiologyValidationCase(
                "physiology-rmssd-non-finite",
                "{\"rmssd_ms\":\"Infinity\"}",
                CreateWindow(1000d, rmssdMs: double.PositiveInfinity),
                1000d,
                PhysiologyValidationReasonCode.RmssdInvalid,
                validator,
                records,
                progress,
                cancellationToken);
            AddPhysiologyValidationCase(
                "physiology-sdnn-negative",
                "{\"sdnn_ms\":-1}",
                CreateWindow(1000d, sdnnMs: -1d),
                1000d,
                PhysiologyValidationReasonCode.SdnnInvalid,
                validator,
                records,
                progress,
                cancellationToken);
            AddPhysiologyValidationCase(
                "physiology-probability-sum-invalid",
                "{\"probability_sum\":0.8}",
                CreateWindow(1000d, probabilitySumInvalid: true),
                1000d,
                PhysiologyValidationReasonCode.StressProbabilitySumInvalid,
                validator,
                records,
                progress,
                cancellationToken);
            AddPhysiologyValidationCase(
                "physiology-continuous-stress-non-finite",
                "{\"continuous_stress_score\":\"NaN\"}",
                CreateWindow(1000d, continuousStressScore: double.NaN),
                1000d,
                PhysiologyValidationReasonCode.ContinuousStressScoreInvalid,
                validator,
                records,
                progress,
                cancellationToken);

            RunPhysiologyBufferCases(bundle, records, progress, cancellationToken);
        }

        private static void RunPhysiologyBufferCases(
            EvaluationRuntimeBundle bundle,
            List<InvariantCaseRecord> records,
            IProgress<EvaluationRunProgress> progress,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var duplicateBuffer = new PhysiologyStateBuffer(bundle.Physiology);
            duplicateBuffer.Ingest(CreateWindow(1000d), 1000d, 1d);
            var duplicate = duplicateBuffer.Ingest(
                CreateWindow(1000d), 1000d, 2d);
            AddPhysiologyBufferRecord(
                "physiology-duplicate-window",
                "physiology.ingestion_order",
                "{\"window_end_utc\":1000,\"prior_window_end_utc\":1000}",
                PhysiologyIngestionResultCode.DuplicateWindow,
                duplicate.ResultCode,
                records,
                progress);

            var orderBuffer = new PhysiologyStateBuffer(bundle.Physiology);
            orderBuffer.Ingest(CreateWindow(1000d), 1000d, 1d);
            var outOfOrder = orderBuffer.Ingest(
                CreateWindow(990d), 990d, 2d);
            AddPhysiologyBufferRecord(
                "physiology-out-of-order-window",
                "physiology.ingestion_order",
                "{\"window_end_utc\":990,\"prior_window_end_utc\":1000}",
                PhysiologyIngestionResultCode.OutOfOrderWindow,
                outOfOrder.ResultCode,
                records,
                progress);

            var qualityBuffer = new PhysiologyStateBuffer(bundle.Physiology);
            qualityBuffer.Ingest(CreateWindow(1000d, 0d), 1000d, 1d);
            var qualityAvailable = qualityBuffer.TryGetLatestUsable(
                PhysiologyDataUse.Decision,
                1d,
                0L,
                out _,
                out var qualityCode);
            AddPhysiologyQueryRecord(
                "physiology-low-quality-query",
                "{\"signal_quality\":0,\"use\":\"Decision\"}",
                PhysiologyQueryResultCode.InsufficientSignalQuality,
                qualityCode,
                qualityAvailable,
                records,
                progress);

            var staleBuffer = new PhysiologyStateBuffer(bundle.Physiology);
            staleBuffer.Ingest(CreateWindow(1000d), 1000d, 1d);
            var staleAvailable = staleBuffer.TryGetLatestUsable(
                PhysiologyDataUse.Decision,
                1d + bundle.Physiology.StaleAfterSeconds + 0.01d,
                0L,
                out _,
                out var staleCode);
            AddPhysiologyQueryRecord(
                "physiology-stale-query",
                "{\"age_seconds\":"
                    + Format(bundle.Physiology.StaleAfterSeconds + 0.01d)
                    + ",\"use\":\"Decision\"}",
                PhysiologyQueryResultCode.Stale,
                staleCode,
                staleAvailable,
                records,
                progress);
        }

        private static void RunSessionCases(
            EvaluationRuntimeBundle bundle,
            List<InvariantCaseRecord> records,
            IProgress<EvaluationRunProgress> progress,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var session = CreateReadySession(bundle);
            var first = session.ProcessCommand(
                "duplicate-command",
                SessionCommandType.Start,
                0d,
                false);
            var duplicate = session.ProcessCommand(
                "duplicate-command",
                SessionCommandType.Start,
                0d,
                false);
            Add(records, new InvariantCaseRecord(
                "session-duplicate-command-id",
                "session.idempotency",
                "{\"command_id\":\"duplicate-command\",\"repetitions\":2}",
                null,
                duplicate.ResultCode.ToString(),
                null,
                "not_applicable",
                0L,
                "Accepted/DuplicateIgnored",
                first.ResultCode + "/" + duplicate.ResultCode,
                first.ResultCode == SessionCommandResultCode.Accepted
                    && duplicate.ResultCode
                        == SessionCommandResultCode.DuplicateIgnored), progress);

            var paused = CreateReadySession(bundle);
            paused.ProcessCommand("start", SessionCommandType.Start, 0d, false);
            paused.AdvanceTo(bundle.Timing.AcclimatizationDurationSeconds);
            paused.ProcessCommand(
                "pause",
                SessionCommandType.Pause,
                bundle.Timing.AcclimatizationDurationSeconds,
                false);
            var resume = paused.ProcessCommand(
                "resume",
                SessionCommandType.Resume,
                bundle.Timing.AcclimatizationDurationSeconds,
                false);
            Add(records, new InvariantCaseRecord(
                "session-resume-requires-fresh-physiology",
                "session.resume_gate",
                "{\"phase\":\"Paused\",\"fresh_physiology\":false}",
                null,
                resume.ResultCode.ToString(),
                null,
                "not_applicable",
                0L,
                SessionCommandResultCode.FreshPhysiologyRequired.ToString(),
                resume.ResultCode.ToString(),
                resume.ResultCode
                    == SessionCommandResultCode.FreshPhysiologyRequired
                    && paused.Phase == VrSessionPhase.Paused), progress);

            var sessionMatch = SessionRelayInboundSessionValidator.TryValidate(
                "session-active",
                "session-other",
                out var rejection);
            Add(records, new InvariantCaseRecord(
                "transport-session-mismatch",
                "transport.session_gate",
                "{\"active_session_id\":\"session-active\",\"inbound_session_id\":\"session-other\"}",
                null,
                rejection.ToString(),
                null,
                "not_applicable",
                0L,
                SessionRelayInboundRejectionReason.SessionMismatch.ToString(),
                rejection.ToString(),
                !sessionMatch
                    && rejection
                        == SessionRelayInboundRejectionReason.SessionMismatch),
                progress);
        }

        private static SessionStateMachine CreateReadySession(
            EvaluationRuntimeBundle bundle)
        {
            var session = new SessionStateMachine();
            session.Initialize(0d);
            session.AcceptConfiguration(bundle.Timing, 0d);
            session.MarkSceneLoaded(0d);
            return session;
        }

        private static void AddPhysiologyValidationCase(
            string caseId,
            string input,
            PhysiologyWindow window,
            double receiptUtc,
            PhysiologyValidationReasonCode expected,
            PhysiologyWindowValidator validator,
            List<InvariantCaseRecord> records,
            IProgress<EvaluationRunProgress> progress,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = validator.Validate(window, receiptUtc);
            Add(records, new InvariantCaseRecord(
                caseId,
                "physiology.validation",
                input,
                null,
                result.ReasonCode.ToString(),
                null,
                "rejected_before_attribution",
                0L,
                expected.ToString(),
                result.ReasonCode.ToString(),
                !result.Accepted && result.ReasonCode == expected), progress);
        }

        private static void AddPhysiologyBufferRecord(
            string caseId,
            string category,
            string input,
            PhysiologyIngestionResultCode expected,
            PhysiologyIngestionResultCode actual,
            List<InvariantCaseRecord> records,
            IProgress<EvaluationRunProgress> progress)
        {
            Add(records, new InvariantCaseRecord(
                caseId,
                category,
                input,
                null,
                actual.ToString(),
                null,
                "rejected_before_attribution",
                0L,
                expected.ToString(),
                actual.ToString(),
                actual == expected), progress);
        }

        private static void AddPhysiologyQueryRecord(
            string caseId,
            string input,
            PhysiologyQueryResultCode expected,
            PhysiologyQueryResultCode actual,
            bool available,
            List<InvariantCaseRecord> records,
            IProgress<EvaluationRunProgress> progress)
        {
            Add(records, new InvariantCaseRecord(
                caseId,
                "physiology.query_gate",
                input,
                null,
                actual.ToString(),
                null,
                "not_opened",
                0L,
                expected.ToString(),
                actual.ToString(),
                !available && actual == expected), progress);
        }

        private static InvariantCaseRecord AttributionRecord(
            string caseId,
            string category,
            string input,
            string disposition,
            AttributionContext context,
            string expected,
            bool passed)
        {
            var updates = context.Policy.CaptureState().ModelUpdateCount;
            return new InvariantCaseRecord(
                caseId,
                category,
                input,
                EnvironmentAction.NoChange.ToString(),
                "not_applicable",
                context.Environment,
                disposition,
                updates,
                expected + "/model_updates=0",
                disposition + "/model_updates=" + updates,
                passed && updates == 0L);
        }

        private static AttributionContext CreateAttributionContext(
            EvaluationRuntimeBundle bundle)
        {
            var buffer = new PhysiologyStateBuffer(bundle.Physiology);
            var ingestion = buffer.Ingest(CreateWindow(1000d), 1000d, 10d);
            if (!ingestion.Accepted
                || !buffer.TryGetLatestAccepted(out var preAction))
            {
                throw new InvalidOperationException(
                    "Could not create invariant pre-action physiology.");
            }

            var environment = Midpoint(bundle.Scene.Limits);
            var policy = bundle.CreatePolicy(StudyPolicyMode.ContextualBandit);
            var decision = policy.SelectAction(new PolicyObservation(
                preAction,
                environment,
                environment,
                bundle.Scene.SafeDefault,
                null,
                new[] { new PolicyActionCandidate(EnvironmentAction.NoChange, 0d) }));
            var request = new RewardAttributionRequest(
                "invariant-transition-1",
                preAction,
                EnvironmentAction.NoChange,
                environment,
                environment,
                20d,
                1005d);
            return new AttributionContext(
                buffer,
                new RewardAttributionTracker(bundle.Reward),
                request,
                policy,
                decision,
                environment);
        }

        private static void Open(AttributionContext context)
        {
            if (!context.Tracker.TryOpen(
                    context.Request,
                    VrSessionPhase.Adaptive,
                    true,
                    out var resultCode))
            {
                throw new InvalidOperationException(
                    "Could not open invariant attribution: " + resultCode);
            }
        }

        private static PhysiologyBaseline CreateBaseline(
            EvaluationRuntimeBundle bundle)
        {
            var count = bundle.Reward.MinimumBaselineSamples;
            return new PhysiologyBaseline(
                bundle.Reward.BaselineStandardDeviationMethod,
                new PhysiologyMetricStatistics(count, 2d, 1d),
                new PhysiologyMetricStatistics(count, 75d, 2d),
                new PhysiologyMetricStatistics(count, 35d, 2d));
        }

        private static PhysiologyWindow CreateWindow(
            double endUtc,
            double signalQuality = 0.95d,
            double heartRateBpm = 75d,
            double? rmssdMs = 35d,
            double? sdnnMs = 40d,
            double durationSeconds = 30d,
            bool probabilitySumInvalid = false,
            double continuousStressScore = 2d)
        {
            var probabilities = probabilitySumInvalid
                ? new StressProbabilityVector(0.2d, 0.2d, 0.2d, 0.2d)
                : new StressProbabilityVector(0.1d, 0.2d, 0.6d, 0.1d);
            return new PhysiologyWindow(
                endUtc,
                endUtc - durationSeconds,
                endUtc,
                heartRateBpm,
                rmssdMs,
                sdnnMs,
                new StressDecision(
                    StressDecisionMode.Point,
                    2,
                    null,
                    null,
                    "moderate",
                    0.6d,
                    false,
                    probabilities,
                    continuousStressScore),
                signalQuality);
        }

        private static EnvironmentAction[] DirectionalActions()
        {
            var actions = new EnvironmentAction[10];
            for (var index = 0; index < actions.Length; index++)
            {
                actions[index] = (EnvironmentAction)(index + 1);
            }

            return actions;
        }

        private static EnvironmentState Midpoint(EnvironmentStateLimits limits)
        {
            return new EnvironmentState(
                Middle(limits.Illumination),
                Middle(limits.Warmth),
                Middle(limits.AtmosphericSoftness),
                Middle(limits.ColorRichness),
                Middle(limits.AmbientMotion));
        }

        private static float Middle(NormalizedRange range)
        {
            return (range.Minimum + range.Maximum) * 0.5f;
        }

        private static EnvironmentState AtBoundary(
            EnvironmentState state,
            EnvironmentStateLimits limits,
            EnvironmentAction action)
        {
            return SetActionDimension(
                state,
                action,
                IsIncrease(action)
                    ? MaximumForAction(limits, action)
                    : MinimumForAction(limits, action));
        }

        private static EnvironmentState NearBoundary(
            EnvironmentState state,
            EnvironmentStateLimits limits,
            EnvironmentActionStepConfiguration steps,
            EnvironmentAction action)
        {
            var step = StepForAction(steps, action);
            var value = IsIncrease(action)
                ? MaximumForAction(limits, action) - (step * 0.5f)
                : MinimumForAction(limits, action) + (step * 0.5f);
            return SetActionDimension(state, action, value);
        }

        private static EnvironmentState SetActionDimension(
            EnvironmentState state,
            EnvironmentAction action,
            float value)
        {
            var dimension = ((int)action - 1) / 2;
            return new EnvironmentState(
                dimension == 0 ? value : state.Illumination,
                dimension == 1 ? value : state.Warmth,
                dimension == 2 ? value : state.AtmosphericSoftness,
                dimension == 3 ? value : state.ColorRichness,
                dimension == 4 ? value : state.AmbientMotion);
        }

        private static float MinimumForAction(
            EnvironmentStateLimits limits,
            EnvironmentAction action)
        {
            switch (((int)action - 1) / 2)
            {
                case 0: return limits.Illumination.Minimum;
                case 1: return limits.Warmth.Minimum;
                case 2: return limits.AtmosphericSoftness.Minimum;
                case 3: return limits.ColorRichness.Minimum;
                case 4: return limits.AmbientMotion.Minimum;
                default: throw new ArgumentOutOfRangeException(nameof(action));
            }
        }

        private static float MaximumForAction(
            EnvironmentStateLimits limits,
            EnvironmentAction action)
        {
            switch (((int)action - 1) / 2)
            {
                case 0: return limits.Illumination.Maximum;
                case 1: return limits.Warmth.Maximum;
                case 2: return limits.AtmosphericSoftness.Maximum;
                case 3: return limits.ColorRichness.Maximum;
                case 4: return limits.AmbientMotion.Maximum;
                default: throw new ArgumentOutOfRangeException(nameof(action));
            }
        }

        private static float StepForAction(
            EnvironmentActionStepConfiguration steps,
            EnvironmentAction action)
        {
            switch (((int)action - 1) / 2)
            {
                case 0: return steps.Illumination;
                case 1: return steps.Warmth;
                case 2: return steps.AtmosphericSoftness;
                case 3: return steps.ColorRichness;
                case 4: return steps.AmbientMotion;
                default: throw new ArgumentOutOfRangeException(nameof(action));
            }
        }

        private static bool IsIncrease(EnvironmentAction action)
        {
            return ((int)action % 2) == 1;
        }

        private static ActionValidationReasonCode MapBlockReason(
            SafetyBlockReason reason)
        {
            switch (reason)
            {
                case SafetyBlockReason.SessionNotAdaptive:
                    return ActionValidationReasonCode.SessionNotAdaptive;
                case SafetyBlockReason.SignalInvalid:
                    return ActionValidationReasonCode.SignalInvalid;
                case SafetyBlockReason.SignalStale:
                    return ActionValidationReasonCode.SignalStale;
                case SafetyBlockReason.CooldownActive:
                    return ActionValidationReasonCode.CooldownActive;
                case SafetyBlockReason.SensitivityRestriction:
                    return ActionValidationReasonCode.SensitivityRestriction;
                case SafetyBlockReason.Paused:
                    return ActionValidationReasonCode.Paused;
                case SafetyBlockReason.EmergencyStop:
                    return ActionValidationReasonCode.EmergencyStop;
                case SafetyBlockReason.TransitionActive:
                    return ActionValidationReasonCode.TransitionActive;
                case SafetyBlockReason.Stabilization:
                    return ActionValidationReasonCode.Stabilization;
                case SafetyBlockReason.ConfigurationError:
                    return ActionValidationReasonCode.ConfigurationError;
                default:
                    throw new ArgumentOutOfRangeException(nameof(reason));
            }
        }

        private static void Add(
            List<InvariantCaseRecord> records,
            InvariantCaseRecord record,
            IProgress<EvaluationRunProgress> progress)
        {
            records.Add(record);
            progress?.Report(new EvaluationRunProgress(
                EvaluationStudyId.StudyDDeterministicInvariants,
                records.Count,
                ExpectedCaseCount));
        }

        internal static string StateJson(EnvironmentState state)
        {
            return "{\"illumination\":" + Format(state.Illumination)
                + ",\"warmth\":" + Format(state.Warmth)
                + ",\"atmospheric_softness\":"
                + Format(state.AtmosphericSoftness)
                + ",\"color_richness\":" + Format(state.ColorRichness)
                + ",\"ambient_motion\":" + Format(state.AmbientMotion)
                + "}";
        }

        private static string LimitsJson(EnvironmentStateLimits limits)
        {
            return "{\"illumination\":" + RangeJson(limits.Illumination)
                + ",\"warmth\":" + RangeJson(limits.Warmth)
                + ",\"atmospheric_softness\":"
                + RangeJson(limits.AtmosphericSoftness)
                + ",\"color_richness\":" + RangeJson(limits.ColorRichness)
                + ",\"ambient_motion\":" + RangeJson(limits.AmbientMotion)
                + "}";
        }

        private static string RangeJson(NormalizedRange range)
        {
            return "{\"minimum\":" + Format(range.Minimum)
                + ",\"maximum\":" + Format(range.Maximum)
                + "}";
        }

        private static string NullableStringJson(string value)
        {
            return value == null ? "null" : "\"" + value + "\"";
        }

        private static string Format(double value)
        {
            return value.ToString("R", Invariant);
        }

        private sealed class AttributionContext
        {
            public AttributionContext(
                PhysiologyStateBuffer buffer,
                RewardAttributionTracker tracker,
                RewardAttributionRequest request,
                IEnvironmentPolicy policy,
                PolicyDecision decision,
                EnvironmentState environment)
            {
                Buffer = buffer;
                Tracker = tracker;
                Request = request;
                Policy = policy;
                Decision = decision;
                Environment = environment;
            }

            public PhysiologyStateBuffer Buffer { get; }
            public RewardAttributionTracker Tracker { get; }
            public RewardAttributionRequest Request { get; }
            public IEnvironmentPolicy Policy { get; }
            public PolicyDecision Decision { get; }
            public EnvironmentState Environment { get; }
        }
    }
}
