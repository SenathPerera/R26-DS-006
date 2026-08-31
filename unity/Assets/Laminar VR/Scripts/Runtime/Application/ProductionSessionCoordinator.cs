using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LaminarVR.AdaptiveMeditation.Application;
using LaminarVR.AdaptiveMeditation.Environment;
using LaminarVR.AdaptiveMeditation.Physiology;
using LaminarVR.AdaptiveMeditation.Policy;
using LaminarVR.AdaptiveMeditation.Rewards;
using LaminarVR.AdaptiveMeditation.Runtime.Configuration;
using LaminarVR.AdaptiveMeditation.Runtime.Telemetry;
using LaminarVR.AdaptiveMeditation.Safety;
using LaminarVR.AdaptiveMeditation.Session;
using LaminarVR.AdaptiveMeditation.Stabilization;
using LaminarVR.AdaptiveMeditation.Telemetry;
using UnityEngine;

namespace LaminarVR.AdaptiveMeditation.Runtime.Application
{
    [AddComponentMenu(
        "Adaptive Meditation/Application/Production Session Coordinator")]
    [DisallowMultipleComponent]
    public sealed class ProductionSessionCoordinator : MonoBehaviour,
        IRecordedTelemetrySource
    {
        [Header("Composition Root")]
        [SerializeField]
        private ApplicationBootstrap applicationBootstrap = null;

        [Header("Approved Research Configuration")]
        [SerializeField]
        private SessionTimingProfile sessionTimingProfile = null;

        [SerializeField]
        private PhysiologyValidationProfile physiologyValidationProfile = null;

        [SerializeField]
        private RewardPipelineProfile rewardPipelineProfile = null;

        [SerializeField]
        private StabilizationSelectionProfile stabilizationSelectionProfile = null;

        [SerializeField]
        private TelemetryLoggingProfile telemetryLoggingProfile = null;

        [SerializeField]
        private ProductionCoordinatorProfile coordinatorProfile = null;

        [Header("Startup")]
        [Tooltip(
            "Initialize automatically once session identity and the safe "
            + "normalized preference have been supplied by the session boundary.")]
        [SerializeField]
        private bool initializeWhenSessionContextAvailable = true;

        private readonly ConcurrentQueue<QueuedPhysiology> physiologyQueue =
            new ConcurrentQueue<QueuedPhysiology>();
        private readonly ConcurrentQueue<QueuedCommand> commandQueue =
            new ConcurrentQueue<QueuedCommand>();
        private readonly Queue<SessionDecisionOpportunity> decisionQueue =
            new Queue<SessionDecisionOpportunity>();
        private readonly Queue<PendingTelemetry> telemetryQueue =
            new Queue<PendingTelemetry>();

        private CancellationTokenSource lifetimeCancellation;
        private SessionStateMachine session;
        private PhysiologyStateBuffer physiologyBuffer;
        private PhysiologyBaselineAccumulator baselineAccumulator;
        private RewardPipelineConfiguration rewardConfiguration;
        private ProductionCoordinatorConfiguration coordinatorConfiguration;
        private StabilizationStateSelector stabilizationSelector;
        private PolicyController policyController;
        private TelemetryRecorder telemetryRecorder;
        private LocalJsonLinesTelemetrySink telemetrySink;
        private DurableTelemetryBufferingSink telemetryBufferingSink;
        private Task activeOperation;
        private bool previousNetworkConnected;
        private volatile bool networkConnected;
        private bool rewardCheckRequested;
        private bool stabilizationSelectionRequested;
        private bool stabilizationTransitionStarted;
        private RewardAttributionInvalidationReason? pendingInvalidation;
        private long pausePhysiologySequenceNumber;
        private int decisionIdSequence;
        private double lastMonotonicTimeSeconds;
        private double clockAnchorMonotonicTimeSeconds;
        private double clockAnchorUtcUnixSeconds;
        private bool hasAdvancedClock;

        private string sessionId;
        private string participantPseudonym;
        private EnvironmentState preferredEnvironment;
        private bool hasSessionContext;

        public event Action<SessionPhaseTransition> PhaseChanged;

        public bool IsInitialized { get; private set; }

        public string LastValidationError { get; private set; } = string.Empty;

        public string TelemetryFilePath => telemetrySink?.FilePath;

        public int PendingEventCount =>
            telemetryBufferingSink?.PendingEventCount ?? 0;

        public VrSessionPhase Phase => session == null
            ? VrSessionPhase.Boot
            : session.Phase;

        public bool IsNetworkConnected => networkConnected;

        public double ExpectedPhysiologyOutputIntervalSeconds =>
            coordinatorConfiguration
                ?.ExpectedPhysiologyOutputIntervalSeconds
            ?? 0d;

        public PhysiologyIngestionResult LastPhysiologyIngestionResult
        {
            get;
            private set;
        }

        public SessionCommandResult LastCommandResult { get; private set; }

        public PolicyDecisionCycleResult? LastDecisionResult { get; private set; }

        public PolicyRewardCycleResult? LastRewardResult { get; private set; }

        public int AcceptedBaselineWindowCount =>
            baselineAccumulator?.AcceptedWindowCount ?? 0;

        public bool HasPolicyController => policyController != null;

        private void Start()
        {
            if (!initializeWhenSessionContextAvailable || !hasSessionContext)
            {
                return;
            }

            if (!TryInitialize(out var validationError))
            {
                enabled = false;
                Debug.LogError(
                    "[ProductionSessionCoordinator] initialization_failed reason="
                    + validationError,
                    this);
            }
        }

        private void Update()
        {
            if (!IsInitialized)
            {
                if (initializeWhenSessionContextAvailable && hasSessionContext)
                {
                    if (!TryInitialize(out var validationError))
                    {
                        enabled = false;
                        Debug.LogError(
                            "[ProductionSessionCoordinator] initialization_failed reason="
                            + validationError,
                            this);
                    }
                }

                return;
            }

            var monotonicTimeSeconds = Time.realtimeSinceStartupAsDouble;
            Advance(
                monotonicTimeSeconds,
                clockAnchorUtcUnixSeconds
                    + monotonicTimeSeconds
                    - clockAnchorMonotonicTimeSeconds);
        }

        private void OnDestroy()
        {
            if (session != null)
            {
                session.PhaseChanged -= HandlePhaseChanged;
                session.DecisionOpportunityReached -=
                    HandleDecisionOpportunityReached;
            }

            lifetimeCancellation?.Cancel();
            lifetimeCancellation?.Dispose();
            lifetimeCancellation = null;

            if (activeOperation == null || activeOperation.IsCompleted)
            {
                telemetrySink?.Dispose();
            }
            else
            {
                var sinkToDispose = telemetrySink;
                activeOperation.ContinueWith(
                    _ => sinkToDispose?.Dispose(),
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }

            telemetrySink = null;
            telemetryBufferingSink = null;
        }

        public void Configure(
            ApplicationBootstrap bootstrap,
            SessionTimingProfile timingProfile,
            PhysiologyValidationProfile validationProfile,
            RewardPipelineProfile rewardProfile,
            StabilizationSelectionProfile stabilizationProfile,
            TelemetryLoggingProfile loggingProfile,
            ProductionCoordinatorProfile productionProfile)
        {
            if (IsInitialized)
            {
                throw new InvalidOperationException(
                    "The production coordinator cannot be reconfigured after initialization.");
            }

            applicationBootstrap = bootstrap;
            sessionTimingProfile = timingProfile;
            physiologyValidationProfile = validationProfile;
            rewardPipelineProfile = rewardProfile;
            stabilizationSelectionProfile = stabilizationProfile;
            telemetryLoggingProfile = loggingProfile;
            coordinatorProfile = productionProfile;
        }

        public void ConfigureSessionContext(
            string activeSessionId,
            string pseudonymousParticipantId,
            EnvironmentState safePreferredEnvironment)
        {
            if (IsInitialized)
            {
                throw new InvalidOperationException(
                    "Session context cannot change after initialization.");
            }

            if (string.IsNullOrWhiteSpace(activeSessionId))
            {
                throw new ArgumentException(
                    "Session ID is required by the Quest session boundary.",
                    nameof(activeSessionId));
            }

            if (string.IsNullOrWhiteSpace(pseudonymousParticipantId))
            {
                throw new ArgumentException(
                    "A pseudonymous participant ID is required.",
                    nameof(pseudonymousParticipantId));
            }

            if (!safePreferredEnvironment.IsNormalized)
            {
                throw new ArgumentException(
                    "Preferred environment must be normalized.",
                    nameof(safePreferredEnvironment));
            }

            sessionId = activeSessionId.Trim();
            participantPseudonym = pseudonymousParticipantId.Trim();
            preferredEnvironment = safePreferredEnvironment;
            hasSessionContext = true;
        }

        public bool TryInitialize(out string validationError)
        {
            if (IsInitialized)
            {
                validationError = string.Empty;
                return true;
            }

            if (!hasSessionContext)
            {
                return Fail(
                    "Configure session identity and safe preference before initialization.",
                    out validationError);
            }

            if (applicationBootstrap == null)
            {
                return Fail(
                    "Assign an ApplicationBootstrap.",
                    out validationError);
            }

            if (!applicationBootstrap.TryInitialize(out var bootstrapError))
            {
                return Fail(bootstrapError, out validationError);
            }

            if (!applicationBootstrap.SceneProfile.Limits.Contains(
                    preferredEnvironment))
            {
                return Fail(
                    "The safe preferred environment is outside scene limits.",
                    out validationError);
            }

            if (!TryCreateConfigurations(
                    out var timing,
                    out var physiologyValidation,
                    out var telemetryConfiguration,
                    out validationError))
            {
                return false;
            }

            if (!coordinatorConfiguration.TryValidateCompatibility(
                    timing,
                    physiologyValidation,
                    rewardConfiguration,
                    out var compatibilityError))
            {
                return Fail(compatibilityError, out validationError);
            }

            try
            {
                physiologyBuffer = new PhysiologyStateBuffer(
                    physiologyValidation);
                baselineAccumulator = new PhysiologyBaselineAccumulator();
                var telemetryPath = TelemetryFilePathResolver
                    .ResolveSessionJsonLinesPath(
                        UnityEngine.Application.persistentDataPath,
                        sessionId);
                telemetrySink = new LocalJsonLinesTelemetrySink(
                    telemetryPath,
                    telemetryConfiguration);
                telemetryBufferingSink =
                    new DurableTelemetryBufferingSink(telemetrySink);
                telemetryRecorder = new TelemetryRecorder(
                    telemetryConfiguration,
                    new TelemetrySessionIdentity(
                        sessionId,
                        participantPseudonym),
                    telemetryBufferingSink);
                lifetimeCancellation = new CancellationTokenSource();

                session = new SessionStateMachine();
                session.PhaseChanged += HandlePhaseChanged;
                session.DecisionOpportunityReached +=
                    HandleDecisionOpportunityReached;

                var now = Time.realtimeSinceStartupAsDouble;
                clockAnchorMonotonicTimeSeconds = now;
                clockAnchorUtcUnixSeconds = UtcNowUnixSeconds();
                hasAdvancedClock = true;
                lastMonotonicTimeSeconds = now;
                if (!session.Initialize(now)
                    || !session.AcceptConfiguration(timing, now)
                    || !session.MarkSceneLoaded(now))
                {
                    throw new InvalidOperationException(
                        "Session state machine failed to reach Ready.");
                }
            }
            catch (Exception exception) when (
                exception is ArgumentException
                || exception is InvalidOperationException
                || exception is System.IO.IOException
                || exception is UnauthorizedAccessException)
            {
                CleanUpFailedInitialization();
                return Fail(exception.Message, out validationError);
            }

            IsInitialized = true;
            LastValidationError = string.Empty;
            validationError = string.Empty;
            previousNetworkConnected = networkConnected;

            QueueTelemetry(
                TelemetryEventTypes.ApplicationStarted,
                true,
                Array.Empty<TelemetryField>());
            QueueTelemetry(
                TelemetryEventTypes.SessionConfigReceived,
                true,
                new[]
                {
                    TelemetryField.String(
                        "coordinator_configuration_id",
                        coordinatorConfiguration.ConfigurationId),
                    TelemetryField.Integer(
                        "coordinator_configuration_version",
                        coordinatorConfiguration.ConfigurationVersion),
                    TelemetryField.Number(
                        "expected_physiology_output_interval_seconds",
                        coordinatorConfiguration
                            .ExpectedPhysiologyOutputIntervalSeconds)
                });

            var conservativeWait = coordinatorConfiguration
                    .ExpectedPhysiologyOutputIntervalSeconds
                + physiologyValidation.MinimumWindowDurationSeconds
                + rewardConfiguration.SettlingSeconds;
            if (rewardConfiguration.MaximumAttributionWaitSeconds
                < conservativeWait)
            {
                Debug.LogWarning(
                    "[ProductionSessionCoordinator] reward attribution may "
                    + "time out before a fully post-transition Component B "
                    + "window arrives. Review maximumAttributionWaitSeconds "
                    + "against cadence + window duration + settling."
                    + " configured_wait="
                    + rewardConfiguration.MaximumAttributionWaitSeconds
                    + " conservative_wait="
                    + conservativeWait,
                    this);
            }

            return true;
        }

        public void SubmitPhysiology(PhysiologyWindow window)
        {
            if (window == null)
            {
                throw new ArgumentNullException(nameof(window));
            }

            physiologyQueue.Enqueue(
                new QueuedPhysiology(window, UtcNowUnixSeconds()));
        }

        public void SubmitCommand(
            string commandId,
            SessionCommandType commandType)
        {
            commandQueue.Enqueue(new QueuedCommand(commandId, commandType));
        }

        public void SetNetworkConnected(bool connected)
        {
            networkConnected = connected;
        }

        public bool TryDequeue(out TelemetryEvent telemetryEvent)
        {
            if (telemetryBufferingSink == null)
            {
                telemetryEvent = null;
                return false;
            }

            return telemetryBufferingSink.TryDequeue(out telemetryEvent);
        }

        public void Advance(
            double monotonicTimeSeconds,
            double utcTimestampUnixSeconds)
        {
            if (!IsInitialized)
            {
                throw new InvalidOperationException(
                    "Initialize the production coordinator before advancing it.");
            }

            ValidateTime(monotonicTimeSeconds, nameof(monotonicTimeSeconds));
            ValidateTime(utcTimestampUnixSeconds, nameof(utcTimestampUnixSeconds));
            if (hasAdvancedClock
                && monotonicTimeSeconds < lastMonotonicTimeSeconds)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(monotonicTimeSeconds),
                    "Monotonic time cannot move backwards.");
            }

            hasAdvancedClock = true;
            lastMonotonicTimeSeconds = monotonicTimeSeconds;
            ObserveCompletedOperation(monotonicTimeSeconds);
            ObserveNetworkStateChange();
            DrainPhysiology(monotonicTimeSeconds);
            DrainCommands(monotonicTimeSeconds);
            session.AdvanceTo(monotonicTimeSeconds);
            TryCreatePolicyController();

            if (applicationBootstrap.EnvironmentManager.IsTransitionActive
                && session.Phase != VrSessionPhase.Adaptive
                && !pendingInvalidation.HasValue)
            {
                applicationBootstrap.EnvironmentManager.AdvanceTransition(
                    monotonicTimeSeconds);
            }

            if (activeOperation != null)
            {
                return;
            }

            StartNextOperation(
                monotonicTimeSeconds,
                utcTimestampUnixSeconds);
        }

        private bool TryCreateConfigurations(
            out SessionTimingConfiguration timing,
            out PhysiologyValidationConfiguration physiologyValidation,
            out TelemetryLoggingConfiguration telemetryConfiguration,
            out string validationError)
        {
            timing = null;
            physiologyValidation = null;
            telemetryConfiguration = null;

            if (sessionTimingProfile == null)
            {
                return Fail(
                    "Assign an approved SessionTimingProfile.",
                    out validationError);
            }

            if (!sessionTimingProfile.TryCreateRuntimeConfiguration(
                    out timing,
                    out var timingError))
            {
                return Fail(timingError, out validationError);
            }

            if (physiologyValidationProfile == null)
            {
                return Fail(
                    "Assign an approved PhysiologyValidationProfile.",
                    out validationError);
            }

            if (!physiologyValidationProfile.TryCreateRuntimeConfiguration(
                    out physiologyValidation,
                    out var physiologyError))
            {
                return Fail(physiologyError, out validationError);
            }

            if (rewardPipelineProfile == null)
            {
                return Fail(
                    "Assign an approved RewardPipelineProfile.",
                    out validationError);
            }

            if (!rewardPipelineProfile.TryCreateRuntimeConfiguration(
                    out rewardConfiguration,
                    out var rewardError))
            {
                return Fail(rewardError, out validationError);
            }

            if (stabilizationSelectionProfile == null)
            {
                return Fail(
                    "Assign an approved StabilizationSelectionProfile.",
                    out validationError);
            }

            if (!stabilizationSelectionProfile.TryCreateRuntimeConfiguration(
                    out var stabilizationConfiguration,
                    out var stabilizationError))
            {
                return Fail(stabilizationError, out validationError);
            }

            if (telemetryLoggingProfile == null)
            {
                return Fail(
                    "Assign an approved TelemetryLoggingProfile.",
                    out validationError);
            }

            if (!telemetryLoggingProfile.TryCreateRuntimeConfiguration(
                    out telemetryConfiguration,
                    out var telemetryError))
            {
                return Fail(telemetryError, out validationError);
            }

            if (coordinatorProfile == null)
            {
                return Fail(
                    "Assign an approved ProductionCoordinatorProfile.",
                    out validationError);
            }

            if (!coordinatorProfile.TryCreateRuntimeConfiguration(
                    out coordinatorConfiguration,
                    out var coordinatorError))
            {
                return Fail(coordinatorError, out validationError);
            }

            stabilizationSelector = new StabilizationStateSelector(
                stabilizationConfiguration);
            validationError = string.Empty;
            return true;
        }

        private void DrainCommands(double monotonicTimeSeconds)
        {
            while (commandQueue.TryDequeue(out var queued))
            {
                var hasFreshPhysiologyForResume = session.Phase
                        == VrSessionPhase.Paused
                    && physiologyBuffer.HasFreshDecisionWindowAfter(
                        pausePhysiologySequenceNumber,
                        monotonicTimeSeconds);
                var result = session.ProcessCommand(
                    queued.CommandId,
                    queued.CommandType,
                    monotonicTimeSeconds,
                    hasFreshPhysiologyForResume);
                LastCommandResult = result;

                if (!result.Applied)
                {
                    continue;
                }

                if (queued.CommandType == SessionCommandType.Pause)
                {
                    pausePhysiologySequenceNumber =
                        physiologyBuffer.LatestAcceptedSequenceNumber;
                }
            }
        }

        private void DrainPhysiology(double monotonicTimeSeconds)
        {
            while (physiologyQueue.TryDequeue(out var queued))
            {
                var result = physiologyBuffer.Ingest(
                    queued.Window,
                    queued.ReceivedUtcUnixSeconds,
                    monotonicTimeSeconds);
                LastPhysiologyIngestionResult = result;
                var eventType = result.Accepted
                    ? TelemetryEventTypes.PhysiologyReceived
                    : TelemetryEventTypes.PhysiologyRejected;
                QueueTelemetry(
                    eventType,
                    !result.Accepted,
                    new[]
                    {
                        TelemetryField.String(
                            "ingestion_result",
                            result.ResultCode.ToString()),
                        TelemetryField.String(
                            "validation_reason",
                            result.ValidationReasonCode.ToString()),
                        TelemetryField.Integer(
                            "accepted_sequence",
                            result.AcceptedSequenceNumber),
                        TelemetryField.Number(
                            "source_timestamp_utc_unix_seconds",
                            queued.Window.SourceTimestampUtcUnixSeconds),
                        TelemetryField.Number(
                            "window_start_utc_unix_seconds",
                            queued.Window.WindowStartUtcUnixSeconds),
                        TelemetryField.Number(
                            "window_end_utc_unix_seconds",
                            queued.Window.WindowEndUtcUnixSeconds),
                        TelemetryField.Number(
                            "signal_quality",
                            queued.Window.SignalQuality)
                    });

                if (!result.Accepted)
                {
                    continue;
                }

                rewardCheckRequested = true;
                if (session.Phase == VrSessionPhase.Acclimatization
                    && physiologyBuffer.TryGetLatestAccepted(out var snapshot))
                {
                    baselineAccumulator.TryAdd(snapshot);
                }
            }
        }

        private void TryCreatePolicyController()
        {
            if (policyController != null
                || session.Phase != VrSessionPhase.Adaptive
                || baselineAccumulator.AcceptedWindowCount
                    < rewardConfiguration.MinimumBaselineSamples)
            {
                return;
            }

            var baseline = baselineAccumulator.CreateBaseline(
                rewardConfiguration.BaselineStandardDeviationMethod);
            policyController = new PolicyController(
                applicationBootstrap.Policy,
                new ActionSafetyValidator(),
                applicationBootstrap.EnvironmentManager,
                applicationBootstrap.SceneProfile,
                coordinatorConfiguration.SafetyLimits,
                physiologyBuffer,
                rewardConfiguration,
                baseline,
                telemetryRecorder,
                null,
                stabilizationSelector);
        }

        private void StartNextOperation(
            double monotonicTimeSeconds,
            double utcTimestampUnixSeconds)
        {
            if (pendingInvalidation.HasValue && policyController != null)
            {
                var reason = pendingInvalidation.Value;
                pendingInvalidation = null;
                activeOperation = InvalidateAsync(
                    reason,
                    monotonicTimeSeconds,
                    utcTimestampUnixSeconds);
                return;
            }

            if (session.Phase == VrSessionPhase.Adaptive
                && policyController != null)
            {
                if (applicationBootstrap.EnvironmentManager.IsTransitionActive)
                {
                    var progress = policyController.AdvanceTransitionFrame(
                        monotonicTimeSeconds);
                    if (progress.Status
                        == EnvironmentTransitionStatus.Completed)
                    {
                        rewardCheckRequested = false;
                        activeOperation = CompleteAdaptiveTransitionAsync(
                            progress,
                            monotonicTimeSeconds,
                            utcTimestampUnixSeconds);
                    }

                    return;
                }

                if (policyController.HasPendingOutcome
                    && rewardCheckRequested)
                {
                    rewardCheckRequested = false;
                    activeOperation = ResolveRewardAsync(
                        monotonicTimeSeconds,
                        utcTimestampUnixSeconds);
                    return;
                }

                if (decisionQueue.Count > 0)
                {
                    var opportunity = decisionQueue.Dequeue();
                    if (policyController.HasPendingOutcome)
                    {
                        rewardCheckRequested = true;
                    }

                    activeOperation = ProcessDecisionAsync(
                        opportunity,
                        utcTimestampUnixSeconds);
                    return;
                }
            }

            if (stabilizationSelectionRequested
                && !stabilizationTransitionStarted
                && policyController != null)
            {
                stabilizationSelectionRequested = false;
                activeOperation = SelectStabilizationStateAsync(
                    monotonicTimeSeconds,
                    utcTimestampUnixSeconds);
                return;
            }

            if (telemetryQueue.Count > 0)
            {
                var pendingTelemetry = telemetryQueue.Dequeue();
                activeOperation = telemetryRecorder.RecordAsync(
                    pendingTelemetry.EventType,
                    utcTimestampUnixSeconds,
                    session.ActiveSessionElapsedSeconds,
                    pendingTelemetry.Critical,
                    pendingTelemetry.Fields,
                    lifetimeCancellation.Token);
            }
        }

        private async Task ProcessDecisionAsync(
            SessionDecisionOpportunity opportunity,
            double utcTimestampUnixSeconds)
        {
            decisionIdSequence++;
            var decisionId = sessionId + "-decision-" + decisionIdSequence;
            LastDecisionResult = await policyController.ProcessDecisionAsync(
                decisionId,
                opportunity,
                session.Phase,
                networkConnected,
                preferredEnvironment,
                utcTimestampUnixSeconds,
                session.ActiveSessionElapsedSeconds,
                lifetimeCancellation.Token);
        }

        private async Task CompleteAdaptiveTransitionAsync(
            EnvironmentTransitionProgress progress,
            double monotonicTimeSeconds,
            double utcTimestampUnixSeconds)
        {
            await policyController.CompleteTransitionAsync(
                progress,
                monotonicTimeSeconds,
                utcTimestampUnixSeconds,
                session.ActiveSessionElapsedSeconds,
                session.Phase,
                networkConnected,
                lifetimeCancellation.Token);
        }

        private async Task ResolveRewardAsync(
            double monotonicTimeSeconds,
            double utcTimestampUnixSeconds)
        {
            // TODO(RESEARCH_DECISION): Wire approved participant discomfort
            // and safety-report inputs. Zero means no report was received; it
            // is not an inferred comfort assessment.
            LastRewardResult = await policyController.TryResolveRewardAsync(
                monotonicTimeSeconds,
                utcTimestampUnixSeconds,
                session.ActiveSessionElapsedSeconds,
                session.Phase,
                networkConnected,
                0d,
                0d,
                lifetimeCancellation.Token);
        }

        private async Task InvalidateAsync(
            RewardAttributionInvalidationReason reason,
            double monotonicTimeSeconds,
            double utcTimestampUnixSeconds)
        {
            await policyController.InvalidatePendingAsync(
                reason,
                monotonicTimeSeconds,
                utcTimestampUnixSeconds,
                session.ActiveSessionElapsedSeconds,
                lifetimeCancellation.Token);
        }

        private async Task SelectStabilizationStateAsync(
            double monotonicTimeSeconds,
            double utcTimestampUnixSeconds)
        {
            var result = await policyController.SelectStabilizationStateAsync(
                session.Phase,
                preferredEnvironment,
                utcTimestampUnixSeconds,
                session.ActiveSessionElapsedSeconds,
                lifetimeCancellation.Token);
            if (result.SelectedState
                != applicationBootstrap.EnvironmentManager.CurrentState)
            {
                applicationBootstrap.EnvironmentManager.BeginTransition(
                    sessionId + "-stabilization",
                    result.SelectedState,
                    monotonicTimeSeconds,
                    applicationBootstrap.SceneProfile
                        .TransitionDurationSeconds);
            }

            stabilizationTransitionStarted = true;
        }

        private void ObserveCompletedOperation(double monotonicTimeSeconds)
        {
            if (activeOperation == null || !activeOperation.IsCompleted)
            {
                return;
            }

            if (activeOperation.IsFaulted)
            {
                var exception = activeOperation.Exception?.GetBaseException();
                LastValidationError = exception?.Message
                    ?? "Unknown coordinator operation failure.";
                Debug.LogError(
                    "[ProductionSessionCoordinator] operation_failed reason="
                    + LastValidationError,
                    this);
                session.AbortForFatalError(monotonicTimeSeconds);
            }

            activeOperation = null;
        }

        private void ObserveNetworkStateChange()
        {
            if (networkConnected == previousNetworkConnected)
            {
                return;
            }

            previousNetworkConnected = networkConnected;
            QueueTelemetry(
                networkConnected
                    ? TelemetryEventTypes.NetworkConnected
                    : TelemetryEventTypes.NetworkDisconnected,
                !networkConnected,
                Array.Empty<TelemetryField>());
            if (!networkConnected)
            {
                RequestInvalidation(
                    RewardAttributionInvalidationReason.NetworkLoss);
            }
        }

        private void HandleDecisionOpportunityReached(
            SessionDecisionOpportunity opportunity)
        {
            decisionQueue.Enqueue(opportunity);
        }

        private void HandlePhaseChanged(SessionPhaseTransition transition)
        {
            QueueTelemetry(
                TelemetryEventTypes.SessionPhaseChanged,
                transition.CurrentPhase == VrSessionPhase.Aborted
                    || transition.CurrentPhase == VrSessionPhase.Completed,
                new[]
                {
                    TelemetryField.String(
                        "previous_phase",
                        transition.PreviousPhase.ToString()),
                    TelemetryField.String(
                        "current_phase",
                        transition.CurrentPhase.ToString()),
                    TelemetryField.String(
                        "reason",
                        transition.Reason.ToString()),
                    TelemetryField.Number(
                        "transition_monotonic_seconds",
                        transition.MonotonicTimeSeconds)
                });

            switch (transition.CurrentPhase)
            {
                case VrSessionPhase.Paused:
                    RequestInvalidation(
                        RewardAttributionInvalidationReason.Pause);
                    QueueTelemetry(
                        TelemetryEventTypes.SessionPaused,
                        true,
                        Array.Empty<TelemetryField>());
                    break;
                case VrSessionPhase.Adaptive:
                    if (transition.PreviousPhase == VrSessionPhase.Paused)
                    {
                        QueueTelemetry(
                            TelemetryEventTypes.SessionResumed,
                            true,
                            Array.Empty<TelemetryField>());
                    }
                    break;
                case VrSessionPhase.Stabilization:
                    decisionQueue.Clear();
                    RequestInvalidation(
                        RewardAttributionInvalidationReason.SessionEnded);
                    stabilizationSelectionRequested = true;
                    break;
                case VrSessionPhase.Completed:
                    decisionQueue.Clear();
                    RequestInvalidation(
                        RewardAttributionInvalidationReason.SessionEnded);
                    QueueTelemetry(
                        TelemetryEventTypes.SessionCompleted,
                        true,
                        Array.Empty<TelemetryField>());
                    break;
                case VrSessionPhase.Aborted:
                    decisionQueue.Clear();
                    RequestInvalidation(
                        transition.Reason
                            == SessionTransitionReason.EmergencyStopCommand
                            ? RewardAttributionInvalidationReason.EmergencyStop
                            : RewardAttributionInvalidationReason.SessionEnded);
                    QueueTelemetry(
                        transition.Reason
                            == SessionTransitionReason.EmergencyStopCommand
                            ? TelemetryEventTypes.EmergencyStop
                            : TelemetryEventTypes.SessionAborted,
                        true,
                        Array.Empty<TelemetryField>());
                    break;
            }

            PhaseChanged?.Invoke(transition);
        }

        private void RequestInvalidation(
            RewardAttributionInvalidationReason reason)
        {
            if (policyController != null)
            {
                pendingInvalidation = reason;
            }
            else
            {
                applicationBootstrap?.EnvironmentManager?.CancelTransition(
                    out _);
            }
        }

        private void QueueTelemetry(
            string eventType,
            bool critical,
            IReadOnlyList<TelemetryField> fields)
        {
            telemetryQueue.Enqueue(
                new PendingTelemetry(eventType, critical, fields));
        }

        private bool Fail(string reason, out string validationError)
        {
            LastValidationError = string.IsNullOrWhiteSpace(reason)
                ? "Unknown production coordinator validation error."
                : reason.Trim();
            validationError = LastValidationError;
            return false;
        }

        private void CleanUpFailedInitialization()
        {
            if (session != null)
            {
                session.PhaseChanged -= HandlePhaseChanged;
                session.DecisionOpportunityReached -=
                    HandleDecisionOpportunityReached;
                session = null;
            }

            lifetimeCancellation?.Cancel();
            lifetimeCancellation?.Dispose();
            lifetimeCancellation = null;
            telemetrySink?.Dispose();
            telemetrySink = null;
            telemetryBufferingSink = null;
            telemetryRecorder = null;
        }

        private static void ValidateTime(double value, string parameterName)
        {
            if (double.IsNaN(value)
                || double.IsInfinity(value)
                || value < 0d)
            {
                throw new ArgumentOutOfRangeException(parameterName);
            }
        }

        private static double UtcNowUnixSeconds()
        {
            return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000d;
        }

        private readonly struct QueuedPhysiology
        {
            public QueuedPhysiology(
                PhysiologyWindow window,
                double receivedUtcUnixSeconds)
            {
                Window = window;
                ReceivedUtcUnixSeconds = receivedUtcUnixSeconds;
            }

            public PhysiologyWindow Window { get; }

            public double ReceivedUtcUnixSeconds { get; }
        }

        private readonly struct QueuedCommand
        {
            public QueuedCommand(
                string commandId,
                SessionCommandType commandType)
            {
                CommandId = commandId;
                CommandType = commandType;
            }

            public string CommandId { get; }

            public SessionCommandType CommandType { get; }
        }

        private readonly struct PendingTelemetry
        {
            public PendingTelemetry(
                string eventType,
                bool critical,
                IReadOnlyList<TelemetryField> fields)
            {
                EventType = eventType;
                Critical = critical;
                Fields = fields;
            }

            public string EventType { get; }

            public bool Critical { get; }

            public IReadOnlyList<TelemetryField> Fields { get; }
        }
    }
}
