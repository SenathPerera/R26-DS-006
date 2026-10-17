using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LaminarVR.AdaptiveMeditation.Application;
using LaminarVR.AdaptiveMeditation.Environment;
using LaminarVR.AdaptiveMeditation.Networking;
using LaminarVR.AdaptiveMeditation.Physiology;
using LaminarVR.AdaptiveMeditation.Policy;
using LaminarVR.AdaptiveMeditation.Preferences;
using LaminarVR.AdaptiveMeditation.Rewards;
using LaminarVR.AdaptiveMeditation.Runtime.Configuration;
using LaminarVR.AdaptiveMeditation.Runtime.Telemetry;
using LaminarVR.AdaptiveMeditation.Safety;
using LaminarVR.AdaptiveMeditation.Session;
using LaminarVR.AdaptiveMeditation.Stabilization;
using LaminarVR.AdaptiveMeditation.Telemetry;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LaminarVR.AdaptiveMeditation.Runtime.Application
{
    [AddComponentMenu(
        "Adaptive Meditation/Application/Production Session Coordinator")]
    [DisallowMultipleComponent]
    public sealed class ProductionSessionCoordinator : MonoBehaviour,
        IRecordedTelemetrySource
    {
        private const string HeadsetLifecyclePauseCommandPrefix =
            "headset-lifecycle-pause-";

        [Header("Composition Root")]
        [SerializeField]
        private ApplicationBootstrap applicationBootstrap = null;

        [Header("Approved Research Configuration")]
        [SerializeField]
        private SessionTimingProfile sessionTimingProfile = null;

        [SerializeField]
        private PreferenceInitializationProfile preferenceInitializationProfile =
            null;

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
        private CancellationTokenSource adaptationCancellation;
        private SessionStateMachine session;
        private PhysiologyStateBuffer physiologyBuffer;
        private PhysiologyBaselineAccumulator baselineAccumulator;
        private PreferenceInitializationConfiguration
            preferenceInitializationConfiguration;
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
        private bool preferenceInitializationTransitionStarted;
        private RewardAttributionInvalidationReason? pendingInvalidation;
        private long pausePhysiologySequenceNumber;
        private int decisionIdSequence;
        private double lastMonotonicTimeSeconds;
        private double clockAnchorMonotonicTimeSeconds;
        private double clockAnchorUtcUnixSeconds;
        private bool hasAdvancedClock;
        private bool headsetLifecycleSuspended;
        private double headsetLifecycleSuspendedAtMonotonicSeconds;

        private string sessionId;
        private string participantPseudonym;
        private EnvironmentState preferredEnvironment;
        private EnvironmentState initialEnvironment;
        private SessionContextSnapshot sessionContextSnapshot;
        private bool hasSessionContext;

        public event Action<SessionPhaseTransition> PhaseChanged;

        public bool IsInitialized { get; private set; }

        public string LastValidationError { get; private set; } = string.Empty;

        public string TelemetryFilePath => telemetrySink?.FilePath;

        public int PendingEventCount =>
            (telemetryBufferingSink?.PendingEventCount ?? 0)
            + telemetryQueue.Count
            + (activeOperation == null ? 0 : 1);

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

        // Keep completed evidence when subsequent polling returns Waiting or NoPending.
        public PolicyDecisionCycleResult? MonitorDecision { get; private set; }
        public PolicyRewardCycleResult? MonitorReward { get; private set; }
        public double MonitorDecisionAt { get; private set; }
        public double MonitorRewardAt { get; private set; }
        public PhysiologyWindow MonitorPhysiology { get; private set; }

        public int AcceptedBaselineWindowCount =>
            baselineAccumulator?.AcceptedWindowCount ?? 0;

        public bool HasPolicyController => policyController != null;

        public bool IsHeadsetLifecycleSuspended =>
            headsetLifecycleSuspended;

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
            if (headsetLifecycleSuspended)
            {
                if (IsInitialized)
                {
                    var suspendedMonotonicTimeSeconds =
                        Time.realtimeSinceStartupAsDouble;
                    Advance(
                        suspendedMonotonicTimeSeconds,
                        clockAnchorUtcUnixSeconds
                            + suspendedMonotonicTimeSeconds
                            - clockAnchorMonotonicTimeSeconds);
                }

                return;
            }

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
            adaptationCancellation?.Cancel();
            adaptationCancellation?.Dispose();
            adaptationCancellation = null;
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
            ProductionCoordinatorProfile productionProfile,
            PreferenceInitializationProfile preferenceProfile = null)
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
            preferenceInitializationProfile = preferenceProfile;
        }

        public void ConfigureSessionContext(
            string activeSessionId,
            string pseudonymousParticipantId,
            EnvironmentState safePreferredEnvironment)
        {
            ConfigureSessionContext(
                activeSessionId,
                pseudonymousParticipantId,
                safePreferredEnvironment,
                null);
        }

        public void ConfigureSessionContext(
            string activeSessionId,
            string pseudonymousParticipantId,
            EnvironmentState safePreferredEnvironment,
            SessionContextSnapshot contextSnapshot)
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
            MonitorDecision = null;
            MonitorReward = null;
            MonitorPhysiology = null;
            MonitorDecisionAt = MonitorRewardAt = 0d;
            participantPseudonym = pseudonymousParticipantId.Trim();
            preferredEnvironment = safePreferredEnvironment;
            sessionContextSnapshot = contextSnapshot;
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

            var preferenceInitialization = new PreferenceInitializer()
                .Initialize(
                    applicationBootstrap.SceneProfile,
                    new EnvironmentPreference(preferredEnvironment),
                    preferenceInitializationConfiguration);
            if (!preferenceInitialization.Accepted)
            {
                return Fail(
                    "Preference initialization was rejected: "
                    + preferenceInitialization.FailureReason
                    + ".",
                    out validationError);
            }

            initialEnvironment = preferenceInitialization.SafeInitialState;

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
                    new DurableTelemetryBufferingSink(
                        telemetrySink,
                        ShouldExposeForRelayTransfer);
                telemetryRecorder = new TelemetryRecorder(
                    telemetryConfiguration,
                    new TelemetrySessionIdentity(
                        sessionId,
                        participantPseudonym),
                    telemetryBufferingSink);
                lifetimeCancellation = new CancellationTokenSource();
                adaptationCancellation =
                    CancellationTokenSource.CreateLinkedTokenSource(
                        lifetimeCancellation.Token);

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

                preferenceInitializationTransitionStarted =
                    BeginPreferenceInitialization(now);
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
                TelemetryEventTypes.ApplicationVersion,
                true,
                CreateApplicationVersionTelemetryFields());
            QueueTelemetry(
                TelemetryEventTypes.DeviceInformation,
                false,
                CreateDeviceInformationTelemetryFields());
            QueueSceneReadinessTelemetry();
            QueuePerformanceTelemetry("session_initialized");
            var sessionConfigurationFields = new List<TelemetryField>
            {
                    TelemetryField.String(
                        "coordinator_configuration_id",
                        coordinatorConfiguration.ConfigurationId),
                    TelemetryField.Integer(
                        "coordinator_configuration_version",
                        coordinatorConfiguration.ConfigurationVersion),
                    TelemetryField.String(
                        "session_timing_configuration_id",
                        timing.ConfigurationId),
                    TelemetryField.Integer(
                        "session_timing_configuration_version",
                        timing.ConfigurationVersion),
                    TelemetryField.String(
                        "physiology_configuration_id",
                        physiologyValidation.ConfigurationId),
                    TelemetryField.Integer(
                        "physiology_configuration_version",
                        physiologyValidation.ConfigurationVersion),
                    TelemetryField.String(
                        "preference_configuration_id",
                        preferenceInitializationConfiguration.ConfigurationId),
                    TelemetryField.Integer(
                        "preference_configuration_version",
                        preferenceInitializationConfiguration
                            .ConfigurationVersion),
                    TelemetryField.Number(
                        "preference_weight",
                        preferenceInitializationConfiguration.PreferenceWeight),
                    TelemetryField.String(
                        "reward_configuration_id",
                        rewardConfiguration.ConfigurationId),
                    TelemetryField.Integer(
                        "reward_configuration_version",
                        rewardConfiguration.ConfigurationVersion),
                    TelemetryField.String(
                        "stabilization_configuration_id",
                        stabilizationSelector.Configuration.ConfigurationId),
                    TelemetryField.Integer(
                        "stabilization_configuration_version",
                        stabilizationSelector.Configuration
                            .ConfigurationVersion),
                    TelemetryField.String(
                        "telemetry_configuration_id",
                        telemetryConfiguration.ConfigurationId),
                    TelemetryField.Integer(
                        "telemetry_configuration_version",
                        telemetryConfiguration.ConfigurationVersion),
                    TelemetryField.String(
                        "telemetry_event_schema_id",
                        telemetryConfiguration.EventSchemaId),
                    TelemetryField.String(
                        "telemetry_event_schema_version",
                        telemetryConfiguration.EventSchemaVersion),
                    TelemetryField.Number(
                        "expected_physiology_output_interval_seconds",
                        coordinatorConfiguration
                            .ExpectedPhysiologyOutputIntervalSeconds),
                    TelemetryField.String(
                        "study_condition",
                        StudyConditionCodec.ToWireValue(
                            applicationBootstrap.StudyPolicyMode)),
                    TelemetryField.String(
                        "policy_id",
                        applicationBootstrap.Policy.PolicyId),
                    TelemetryField.String(
                        "policy_version",
                        applicationBootstrap.Policy.PolicyVersion),
                    TelemetryField.Number(
                        "preferred_illumination",
                        preferredEnvironment.Illumination),
                    TelemetryField.Number(
                        "preferred_warmth",
                        preferredEnvironment.Warmth),
                    TelemetryField.Number(
                        "preferred_atmospheric_softness",
                        preferredEnvironment.AtmosphericSoftness),
                    TelemetryField.Number(
                        "preferred_color_richness",
                        preferredEnvironment.ColorRichness),
                    TelemetryField.Number(
                        "preferred_ambient_motion",
                        preferredEnvironment.AmbientMotion),
                    TelemetryField.Number(
                        "initial_illumination",
                        initialEnvironment.Illumination),
                    TelemetryField.Number(
                        "initial_warmth",
                        initialEnvironment.Warmth),
                    TelemetryField.Number(
                        "initial_atmospheric_softness",
                        initialEnvironment.AtmosphericSoftness),
                    TelemetryField.Number(
                        "initial_color_richness",
                        initialEnvironment.ColorRichness),
                    TelemetryField.Number(
                        "initial_ambient_motion",
                        initialEnvironment.AmbientMotion),
                    TelemetryField.Boolean(
                        "preference_initialization_transition_started",
                        preferenceInitializationTransitionStarted)
            };
            if (sessionContextSnapshot != null)
            {
                sessionConfigurationFields.AddRange(
                    CreateSessionContextTelemetryFields(
                        sessionContextSnapshot));
            }

            QueueTelemetry(
                TelemetryEventTypes.SessionConfigReceived,
                true,
                sessionConfigurationFields.ToArray());
            if (preferenceInitializationTransitionStarted)
            {
                QueueTelemetry(
                    TelemetryEventTypes.TransitionStarted,
                    true,
                    new[]
                    {
                        TelemetryField.String(
                            "transition_id",
                            PreferenceInitializationTransitionId()),
                        TelemetryField.String(
                            "transition_reason",
                            "participant_preference_initialization"),
                        TelemetryField.Number(
                            "duration_seconds",
                            applicationBootstrap.SceneProfile
                                .TransitionDurationSeconds)
                    });
            }

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

        public bool ReportTelemetryTransferSucceeded(
            string acknowledgementId,
            int eventCount,
            long firstSequenceNumber,
            long lastSequenceNumber)
        {
            if (!IsInitialized)
            {
                return false;
            }

            QueueTelemetry(
                TelemetryEventTypes.LocalLogUploadSucceeded,
                true,
                CreateTelemetryTransferFields(
                    acknowledgementId,
                    eventCount,
                    firstSequenceNumber,
                    lastSequenceNumber,
                    null));
            return true;
        }

        public bool ReportTelemetryTransferFailed(
            string diagnosticCode,
            int eventCount,
            long firstSequenceNumber,
            long lastSequenceNumber)
        {
            if (!IsInitialized)
            {
                return false;
            }

            QueueTelemetry(
                TelemetryEventTypes.LocalLogUploadFailed,
                true,
                CreateTelemetryTransferFields(
                    null,
                    eventCount,
                    firstSequenceNumber,
                    lastSequenceNumber,
                    diagnosticCode));
            return true;
        }

        public bool SuspendForHeadsetLifecycle(
            HeadsetLifecycleInterruptionReason reason,
            double monotonicTimeSeconds,
            double utcTimestampUnixSeconds)
        {
            ValidateTime(monotonicTimeSeconds, nameof(monotonicTimeSeconds));
            ValidateTime(
                utcTimestampUnixSeconds,
                nameof(utcTimestampUnixSeconds));
            if (!Enum.IsDefined(
                    typeof(HeadsetLifecycleInterruptionReason),
                    reason))
            {
                throw new ArgumentOutOfRangeException(nameof(reason));
            }

            if (headsetLifecycleSuspended)
            {
                return false;
            }

            if (IsInitialized)
            {
                ValidateForwardClock(monotonicTimeSeconds);
            }

            headsetLifecycleSuspended = true;
            headsetLifecycleSuspendedAtMonotonicSeconds =
                monotonicTimeSeconds;
            if (!IsInitialized)
            {
                return true;
            }

            RenewAdaptationCancellation();
            session.AdvanceTo(monotonicTimeSeconds);
            hasAdvancedClock = true;
            lastMonotonicTimeSeconds = monotonicTimeSeconds;
            var phaseAtLoss = session.Phase;

            if (phaseAtLoss == VrSessionPhase.Adaptive)
            {
                var pauseResult = session.ProcessCommand(
                    HeadsetLifecyclePauseCommandPrefix
                        + Guid.NewGuid().ToString("N"),
                    SessionCommandType.Pause,
                    monotonicTimeSeconds,
                    false);
                LastCommandResult = pauseResult;
                if (pauseResult.Applied)
                {
                    pausePhysiologySequenceNumber =
                        physiologyBuffer.LatestAcceptedSequenceNumber;
                }
            }

            QueueTelemetry(
                TelemetryEventTypes.ApplicationFocusLost,
                true,
                new[]
                {
                    TelemetryField.String(
                        "interruption_reason",
                        reason.ToString()),
                    TelemetryField.String(
                        "phase_at_loss",
                        phaseAtLoss.ToString())
                });
            return true;
        }

        public bool RestoreHeadsetLifecycle(
            double monotonicTimeSeconds,
            double utcTimestampUnixSeconds)
        {
            ValidateTime(monotonicTimeSeconds, nameof(monotonicTimeSeconds));
            ValidateTime(
                utcTimestampUnixSeconds,
                nameof(utcTimestampUnixSeconds));
            if (!headsetLifecycleSuspended)
            {
                return false;
            }

            var suspendedDurationSeconds = Math.Max(
                0d,
                monotonicTimeSeconds
                    - headsetLifecycleSuspendedAtMonotonicSeconds);
            if (!IsInitialized)
            {
                headsetLifecycleSuspended = false;
                return true;
            }

            ValidateForwardClock(monotonicTimeSeconds);
            headsetLifecycleSuspended = false;
            session.ReanchorClockWithoutAdvancing(monotonicTimeSeconds);
            applicationBootstrap.EnvironmentManager.DelayActiveTransition(
                suspendedDurationSeconds);
            hasAdvancedClock = true;
            lastMonotonicTimeSeconds = monotonicTimeSeconds;
            QueueTelemetry(
                TelemetryEventTypes.ApplicationFocusRestored,
                true,
                new[]
                {
                    TelemetryField.String(
                        "phase_on_restore",
                        session.Phase.ToString()),
                    TelemetryField.Number(
                        "suspended_duration_seconds",
                        suspendedDurationSeconds),
                    TelemetryField.Boolean(
                        "automatic_resume",
                        false)
                });
            return true;
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
            ValidateForwardClock(monotonicTimeSeconds);

            hasAdvancedClock = true;
            lastMonotonicTimeSeconds = monotonicTimeSeconds;
            ObserveCompletedOperation(monotonicTimeSeconds);
            ObserveNetworkStateChange();
            if (headsetLifecycleSuspended)
            {
                if (activeOperation == null)
                {
                    StartNextSuspendedOperation(
                        monotonicTimeSeconds,
                        utcTimestampUnixSeconds);
                }

                return;
            }

            DrainPhysiology(monotonicTimeSeconds);
            DrainCommands(monotonicTimeSeconds);
            session.AdvanceTo(monotonicTimeSeconds);
            TryCreatePolicyController();

            if (applicationBootstrap.EnvironmentManager.IsTransitionActive
                && session.Phase != VrSessionPhase.Adaptive
                && !pendingInvalidation.HasValue)
            {
                var progress = applicationBootstrap.EnvironmentManager
                    .AdvanceTransition(monotonicTimeSeconds);
                if (progress.Status
                    == EnvironmentTransitionStatus.Completed)
                {
                    QueueNonAdaptiveTransitionCompleted(progress);
                    QueuePerformanceTelemetry("transition_completed");
                }
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

            if (preferenceInitializationProfile == null)
            {
                return Fail(
                    "Assign an approved PreferenceInitializationProfile.",
                    out validationError);
            }

            if (!preferenceInitializationProfile.TryCreateRuntimeConfiguration(
                    out preferenceInitializationConfiguration,
                    out var preferenceError))
            {
                return Fail(preferenceError, out validationError);
            }

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
                    CreatePhysiologyTelemetryFields(
                        queued,
                        result));

                if (!result.Accepted)
                {
                    continue;
                }

                rewardCheckRequested = true;
                MonitorPhysiology = queued.Window;
                if (session.Phase == VrSessionPhase.Acclimatization
                    && physiologyBuffer.TryGetLatestAccepted(out var snapshot))
                {
                    baselineAccumulator.TryAdd(snapshot);
                }
            }
        }

        private bool BeginPreferenceInitialization(
            double monotonicTimeSeconds)
        {
            var environmentManager = applicationBootstrap.EnvironmentManager;
            if (environmentManager.CurrentState.Equals(initialEnvironment))
            {
                return false;
            }

            // The session boundary accepts only a normalized preference that
            // was already checked against this scene's approved limits. Apply
            // that safe target through the same gradual manager used for
            // policy transitions; it is not a policy decision or reward event.
            environmentManager.BeginTransition(
                PreferenceInitializationTransitionId(),
                initialEnvironment,
                monotonicTimeSeconds,
                applicationBootstrap.SceneProfile.TransitionDurationSeconds);
            return true;
        }

        private static TelemetryField[] CreateSessionContextTelemetryFields(
            SessionContextSnapshot context)
        {
            return new[]
            {
                TelemetryField.String(
                    "session_context_schema_version",
                    context.SchemaVersion),
                TelemetryField.String(
                    "session_context_collected_at",
                    context.CollectedAt),
                TelemetryField.Number(
                    "session_context_subjective_stress",
                    context.SubjectiveStress),
                TelemetryField.Number(
                    "session_context_mood_valence",
                    context.MoodValence),
                TelemetryField.Number(
                    "session_context_fatigue",
                    context.Fatigue),
                TelemetryField.Number(
                    "session_context_sleep_quality",
                    context.SleepQuality),
                TelemetryField.Boolean(
                    "session_context_headache_or_eye_strain",
                    context.HeadacheOrEyeStrainToday),
                TelemetryField.Integer(
                    "session_context_time_of_day_minutes",
                    context.TimeOfDayMinutes),
                TelemetryField.Integer(
                    "session_context_session_sequence_number",
                    context.SessionSequenceNumber),
                context.DaysSincePreviousSession.HasValue
                    ? TelemetryField.Integer(
                        "session_context_days_since_previous_session",
                        context.DaysSincePreviousSession.Value)
                    : TelemetryField.Null(
                        "session_context_days_since_previous_session")
            };
        }

        private string PreferenceInitializationTransitionId()
        {
            return sessionId + "-preference-initialization";
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
                    utcTimestampUnixSeconds,
                    session.ActiveSessionElapsedSeconds);
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

        private void StartNextSuspendedOperation(
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
                adaptationCancellation.Token);
            if (LastDecisionResult.Value.Decision != null)
            {
                MonitorDecision = LastDecisionResult;
                MonitorDecisionAt = utcTimestampUnixSeconds;
            }
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
                adaptationCancellation.Token);
        }

        private async Task ResolveRewardAsync(
            double monotonicTimeSeconds,
            double utcTimestampUnixSeconds)
        {
            LastRewardResult = await policyController.TryResolveRewardAsync(
                monotonicTimeSeconds,
                utcTimestampUnixSeconds,
                session.ActiveSessionElapsedSeconds,
                session.Phase,
                networkConnected,
                adaptationCancellation.Token);
            if (LastRewardResult.Value.ResultCode != PolicyRewardCycleResultCode.Waiting
                && LastRewardResult.Value.ResultCode != PolicyRewardCycleResultCode.NoPending)
            {
                MonitorReward = LastRewardResult;
                MonitorRewardAt = utcTimestampUnixSeconds;
            }
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
                adaptationCancellation.Token);
        }

        private async Task SelectStabilizationStateAsync(
            double monotonicTimeSeconds,
            double utcTimestampUnixSeconds,
            double sessionElapsedSeconds)
        {
            var result = await policyController.SelectStabilizationStateAsync(
                session.Phase,
                preferredEnvironment,
                initialEnvironment,
                utcTimestampUnixSeconds,
                sessionElapsedSeconds,
                adaptationCancellation.Token);
            if (result.SelectedState
                != applicationBootstrap.EnvironmentManager.CurrentState)
            {
                var transitionId = sessionId + "-stabilization";
                applicationBootstrap.EnvironmentManager.BeginTransition(
                    transitionId,
                    result.SelectedState,
                    monotonicTimeSeconds,
                    applicationBootstrap.SceneProfile
                        .TransitionDurationSeconds);
                await telemetryRecorder.RecordAsync(
                        TelemetryEventTypes.TransitionStarted,
                        utcTimestampUnixSeconds,
                        sessionElapsedSeconds,
                        false,
                        new[]
                        {
                            TelemetryField.String(
                                "transition_id",
                                transitionId),
                            TelemetryField.String(
                                "transition_reason",
                                "stabilization_selection"),
                            TelemetryField.Number(
                                "duration_seconds",
                                applicationBootstrap.SceneProfile
                                    .TransitionDurationSeconds)
                        },
                        adaptationCancellation.Token)
                    .ConfigureAwait(false);
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
            QueuePerformanceTelemetry("decision_opportunity");
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
                    decisionQueue.Clear();
                    RequestInvalidation(
                        RewardAttributionInvalidationReason.Pause);
                    QueueTelemetry(
                        TelemetryEventTypes.SessionPaused,
                        true,
                        new[]
                        {
                            TelemetryField.String(
                                "reason",
                                transition.Reason.ToString())
                        });
                    break;
                case VrSessionPhase.Adaptive:
                    if (transition.PreviousPhase == VrSessionPhase.Paused)
                    {
                        QueueTelemetry(
                            TelemetryEventTypes.SessionResumed,
                            true,
                            new[]
                            {
                                TelemetryField.String(
                                    "reason",
                                    transition.Reason.ToString())
                            });
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
                        CreateTerminalTelemetryFields(transition));
                    QueuePerformanceTelemetry("session_completed");
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
                        CreateTerminalTelemetryFields(transition));
                    QueuePerformanceTelemetry("session_aborted");
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
                var environmentManager =
                    applicationBootstrap?.EnvironmentManager;
                if (environmentManager != null
                    && environmentManager.CancelTransition(
                        out var cancelledTransitionId))
                {
                    QueueTelemetry(
                        TelemetryEventTypes.TransitionCancelled,
                        true,
                        new[]
                        {
                            TelemetryField.String(
                                "transition_id",
                                cancelledTransitionId),
                            TelemetryField.String(
                                "reason",
                                reason.ToString())
                        });
                    QueuePerformanceTelemetry("transition_cancelled");
                }
            }
        }

        private static TelemetryField[]
            CreateApplicationVersionTelemetryFields()
        {
            return new[]
            {
                StringOrNull("application_version", UnityEngine.Application.version),
                StringOrNull("unity_version", UnityEngine.Application.unityVersion),
                StringOrNull("build_guid", UnityEngine.Application.buildGUID),
                StringOrNull("platform", UnityEngine.Application.platform.ToString())
            };
        }

        private static TelemetryField[] CreateDeviceInformationTelemetryFields()
        {
            return new[]
            {
                StringOrNull("device_model", SystemInfo.deviceModel),
                StringOrNull("device_type", SystemInfo.deviceType.ToString()),
                StringOrNull("operating_system", SystemInfo.operatingSystem),
                StringOrNull("processor_type", SystemInfo.processorType),
                TelemetryField.Integer(
                    "processor_count",
                    SystemInfo.processorCount),
                TelemetryField.Integer(
                    "system_memory_mb",
                    SystemInfo.systemMemorySize),
                StringOrNull(
                    "graphics_device_name",
                    SystemInfo.graphicsDeviceName),
                StringOrNull(
                    "graphics_device_type",
                    SystemInfo.graphicsDeviceType.ToString()),
                TelemetryField.Integer(
                    "graphics_memory_mb",
                    SystemInfo.graphicsMemorySize)
            };
        }

        private void QueueSceneReadinessTelemetry()
        {
            var activeScene = SceneManager.GetActiveScene();
            var sceneFields = new[]
            {
                TelemetryField.String(
                    "scene_id",
                    applicationBootstrap.SceneProfile.SceneId),
                StringOrNull("unity_scene_name", activeScene.name),
                TelemetryField.Integer(
                    "unity_scene_build_index",
                    activeScene.buildIndex),
                TelemetryField.String(
                    "load_observation",
                    "preloaded_at_session_initialization")
            };
            QueueTelemetry(
                TelemetryEventTypes.SceneLoadStarted,
                false,
                sceneFields);

            var bindingValidation =
                applicationBootstrap.LastBindingValidation;
            QueueTelemetry(
                TelemetryEventTypes.SceneBindingValidation,
                bindingValidation == null || !bindingValidation.IsValid,
                new[]
                {
                    TelemetryField.String(
                        "scene_id",
                        applicationBootstrap.SceneProfile.SceneId),
                    TelemetryField.Boolean(
                        "valid",
                        bindingValidation != null
                            && bindingValidation.IsValid),
                    StringOrNull(
                        "validation_code",
                        bindingValidation?.Code.ToString()),
                    StringOrNull(
                        "validation_detail",
                        bindingValidation?.Detail)
                });
            QueueTelemetry(
                TelemetryEventTypes.SceneLoadCompleted,
                true,
                sceneFields);
        }

        private void QueuePerformanceTelemetry(string checkpoint)
        {
            if (telemetryRecorder == null)
            {
                return;
            }

            var deltaTimeSeconds = (double)Time.unscaledDeltaTime;
            var fields = new List<TelemetryField>
            {
                TelemetryField.String("checkpoint", checkpoint),
                TelemetryField.String("phase", Phase.ToString()),
                FiniteNumberOrNull(
                    "unscaled_delta_time_seconds",
                    deltaTimeSeconds),
                deltaTimeSeconds > 0d && IsFinite(deltaTimeSeconds)
                    ? TelemetryField.Number(
                        "estimated_frames_per_second",
                        1d / deltaTimeSeconds)
                    : TelemetryField.Null(
                        "estimated_frames_per_second"),
                TelemetryField.Integer(
                    "managed_memory_bytes",
                    GC.GetTotalMemory(false)),
                TelemetryField.Integer(
                    "target_frame_rate",
                    UnityEngine.Application.targetFrameRate),
                TelemetryField.Integer(
                    "quality_vsync_count",
                    QualitySettings.vSyncCount)
            };
            var refreshRateHertz =
                Screen.currentResolution.refreshRateRatio.value;
            fields.Add(
                FiniteNumberOrNull(
                    "display_refresh_rate_hz",
                    refreshRateHertz));
            QueueTelemetry(
                TelemetryEventTypes.PerformanceSample,
                false,
                fields.ToArray());
        }

        private static TelemetryField[] CreatePhysiologyTelemetryFields(
            QueuedPhysiology queued,
            PhysiologyIngestionResult result)
        {
            var window = queued.Window;
            var stress = window.Stress;
            var fields = new List<TelemetryField>
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
                FiniteNumberOrNull(
                    "received_utc_unix_seconds",
                    queued.ReceivedUtcUnixSeconds),
                FiniteNumberOrNull(
                    "source_timestamp_utc_unix_seconds",
                    window.SourceTimestampUtcUnixSeconds),
                FiniteNumberOrNull(
                    "window_start_utc_unix_seconds",
                    window.WindowStartUtcUnixSeconds),
                FiniteNumberOrNull(
                    "window_end_utc_unix_seconds",
                    window.WindowEndUtcUnixSeconds),
                FiniteNumberOrNull(
                    "window_duration_seconds",
                    window.WindowDurationSeconds),
                FiniteNumberOrNull("heart_rate_bpm", window.HeartRateBpm),
                OptionalFiniteNumberOrNull("rmssd_ms", window.RmssdMs),
                OptionalFiniteNumberOrNull("sdnn_ms", window.SdnnMs),
                FiniteNumberOrNull("signal_quality", window.SignalQuality)
            };

            if (stress == null)
            {
                fields.Add(TelemetryField.Null("stress_mode"));
                fields.Add(TelemetryField.Null("stress_point_level"));
                fields.Add(TelemetryField.Null("stress_band_low_level"));
                fields.Add(TelemetryField.Null("stress_band_high_level"));
                fields.Add(TelemetryField.Null("stress_label"));
                fields.Add(TelemetryField.Null("stress_confidence"));
                fields.Add(TelemetryField.Null("stress_adjacent"));
                fields.Add(TelemetryField.Null("stress_probability_relaxed"));
                fields.Add(TelemetryField.Null("stress_probability_mild"));
                fields.Add(TelemetryField.Null("stress_probability_moderate"));
                fields.Add(TelemetryField.Null("stress_probability_high"));
                fields.Add(TelemetryField.Null("continuous_stress_score"));
                return fields.ToArray();
            }

            fields.Add(
                TelemetryField.String(
                    "stress_mode",
                    stress.Mode.ToString()));
            fields.Add(
                OptionalIntegerOrNull(
                    "stress_point_level",
                    stress.PointLevel));
            fields.Add(
                OptionalIntegerOrNull(
                    "stress_band_low_level",
                    stress.BandLowLevel));
            fields.Add(
                OptionalIntegerOrNull(
                    "stress_band_high_level",
                    stress.BandHighLevel));
            fields.Add(StringOrNull("stress_label", stress.Label));
            fields.Add(
                FiniteNumberOrNull(
                    "stress_confidence",
                    stress.Confidence));
            fields.Add(
                stress.Adjacent.HasValue
                    ? TelemetryField.Boolean(
                        "stress_adjacent",
                        stress.Adjacent.Value)
                    : TelemetryField.Null("stress_adjacent"));
            fields.Add(
                FiniteNumberOrNull(
                    "stress_probability_relaxed",
                    stress.Probabilities.Level0Probability));
            fields.Add(
                FiniteNumberOrNull(
                    "stress_probability_mild",
                    stress.Probabilities.Level1Probability));
            fields.Add(
                FiniteNumberOrNull(
                    "stress_probability_moderate",
                    stress.Probabilities.Level2Probability));
            fields.Add(
                FiniteNumberOrNull(
                    "stress_probability_high",
                    stress.Probabilities.Level3Probability));
            fields.Add(
                FiniteNumberOrNull(
                    "continuous_stress_score",
                    stress.ContinuousScore));
            return fields.ToArray();
        }

        private void QueueNonAdaptiveTransitionCompleted(
            EnvironmentTransitionProgress progress)
        {
            var fields = new List<TelemetryField>
            {
                TelemetryField.String(
                    "transition_id",
                    progress.TransitionId),
                TelemetryField.String(
                    "transition_reason",
                    ResolveNonAdaptiveTransitionReason(
                        progress.TransitionId)),
                TelemetryField.Number(
                    "completed_monotonic_seconds",
                    progress.CompletedMonotonicTimeSeconds.Value)
            };
            AddEnvironmentTelemetryFields(
                fields,
                "state_after",
                progress.State);
            QueueTelemetry(
                TelemetryEventTypes.TransitionCompleted,
                false,
                fields.ToArray());
        }

        private string ResolveNonAdaptiveTransitionReason(
            string transitionId)
        {
            if (string.Equals(
                    transitionId,
                    PreferenceInitializationTransitionId(),
                    StringComparison.Ordinal))
            {
                return "participant_preference_initialization";
            }

            if (string.Equals(
                    transitionId,
                    sessionId + "-stabilization",
                    StringComparison.Ordinal))
            {
                return "stabilization_selection";
            }

            return "non_adaptive_transition";
        }

        private TelemetryField[] CreateTerminalTelemetryFields(
            SessionPhaseTransition transition)
        {
            var fields = new List<TelemetryField>
            {
                TelemetryField.String(
                    "terminal_phase",
                    transition.CurrentPhase.ToString()),
                TelemetryField.String(
                    "reason",
                    transition.Reason.ToString()),
                TelemetryField.String(
                    "policy_id",
                    applicationBootstrap.Policy.PolicyId),
                TelemetryField.String(
                    "policy_version",
                    applicationBootstrap.Policy.PolicyVersion),
                TelemetryField.Integer(
                    "accepted_physiology_window_count",
                    physiologyBuffer.LatestAcceptedSequenceNumber)
            };
            if (policyController != null)
            {
                var state = applicationBootstrap.Policy.CaptureState();
                fields.Add(
                    TelemetryField.Integer(
                        "policy_decision_count",
                        state.DecisionCount));
                fields.Add(
                    TelemetryField.Integer(
                        "policy_observed_outcome_count",
                        state.ObservedOutcomeCount));
                fields.Add(
                    TelemetryField.Integer(
                        "model_update_count",
                        state.ModelUpdateCount));
                fields.Add(
                    TelemetryField.Number(
                        "total_environment_variation",
                        policyController.TotalVariation));
            }
            else
            {
                fields.Add(TelemetryField.Integer("policy_decision_count", 0));
                fields.Add(
                    TelemetryField.Integer(
                        "policy_observed_outcome_count",
                        0));
                fields.Add(TelemetryField.Integer("model_update_count", 0));
                fields.Add(
                    TelemetryField.Number(
                        "total_environment_variation",
                        0d));
            }

            AddEnvironmentTelemetryFields(
                fields,
                "final_state",
                applicationBootstrap.EnvironmentManager.CurrentState);
            return fields.ToArray();
        }

        private static TelemetryField[] CreateTelemetryTransferFields(
            string acknowledgementId,
            int eventCount,
            long firstSequenceNumber,
            long lastSequenceNumber,
            string diagnosticCode)
        {
            return new[]
            {
                TelemetryField.String(
                    "transfer_kind",
                    "relay_telemetry_batch"),
                StringOrNull("acknowledgement_id", acknowledgementId),
                TelemetryField.Integer(
                    "event_count",
                    Math.Max(0, eventCount)),
                TelemetryField.Integer(
                    "first_sequence_number",
                    Math.Max(0L, firstSequenceNumber)),
                TelemetryField.Integer(
                    "last_sequence_number",
                    Math.Max(0L, lastSequenceNumber)),
                StringOrNull("diagnostic_code", diagnosticCode)
            };
        }

        private static void AddEnvironmentTelemetryFields(
            List<TelemetryField> fields,
            string prefix,
            EnvironmentState state)
        {
            fields.Add(
                TelemetryField.Number(
                    prefix + "_illumination",
                    state.Illumination));
            fields.Add(
                TelemetryField.Number(
                    prefix + "_warmth",
                    state.Warmth));
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

        private static TelemetryField FiniteNumberOrNull(
            string name,
            double value)
        {
            return IsFinite(value)
                ? TelemetryField.Number(name, value)
                : TelemetryField.Null(name);
        }

        private static TelemetryField OptionalFiniteNumberOrNull(
            string name,
            double? value)
        {
            return value.HasValue && IsFinite(value.Value)
                ? TelemetryField.Number(name, value.Value)
                : TelemetryField.Null(name);
        }

        private static TelemetryField OptionalIntegerOrNull(
            string name,
            int? value)
        {
            return value.HasValue
                ? TelemetryField.Integer(name, value.Value)
                : TelemetryField.Null(name);
        }

        private static TelemetryField StringOrNull(
            string name,
            string value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? TelemetryField.Null(name)
                : TelemetryField.String(name, value.Trim());
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private static bool ShouldExposeForRelayTransfer(
            TelemetryEvent telemetryEvent)
        {
            return telemetryEvent != null
                && !string.Equals(
                    telemetryEvent.EventType,
                    TelemetryEventTypes.LocalLogUploadSucceeded,
                    StringComparison.Ordinal)
                && !string.Equals(
                    telemetryEvent.EventType,
                    TelemetryEventTypes.LocalLogUploadFailed,
                    StringComparison.Ordinal);
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

        private void ValidateForwardClock(double monotonicTimeSeconds)
        {
            if (hasAdvancedClock
                && monotonicTimeSeconds < lastMonotonicTimeSeconds)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(monotonicTimeSeconds),
                    "Monotonic time cannot move backwards.");
            }
        }

        private void RenewAdaptationCancellation()
        {
            adaptationCancellation?.Cancel();
            adaptationCancellation?.Dispose();
            adaptationCancellation =
                CancellationTokenSource.CreateLinkedTokenSource(
                    lifetimeCancellation.Token);
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
            adaptationCancellation?.Cancel();
            adaptationCancellation?.Dispose();
            adaptationCancellation = null;
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
