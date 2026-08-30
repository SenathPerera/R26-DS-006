using System;
using System.Collections.Generic;

namespace LaminarVR.AdaptiveMeditation.Session
{
    public sealed class SessionStateMachine
    {
        private const double TimeComparisonTolerance = 1e-9d;

        private readonly HashSet<string> processedCommandIds =
            new HashSet<string>(StringComparer.Ordinal);

        private SessionTimingConfiguration timingConfiguration;
        private bool hasObservedClock;
        private double lastMonotonicTimeSeconds;
        private double acclimatizationElapsedSeconds;
        private double adaptiveElapsedSeconds;
        private double stabilizationElapsedSeconds;
        private double nextDecisionAtAdaptiveSeconds;
        private int decisionSequenceNumber;

        public event Action<SessionPhaseTransition> PhaseChanged;

        public event Action<SessionDecisionOpportunity> DecisionOpportunityReached;

        public VrSessionPhase Phase { get; private set; } = VrSessionPhase.Boot;

        public SessionTimingConfiguration TimingConfiguration => timingConfiguration;

        public double AcclimatizationElapsedSeconds => acclimatizationElapsedSeconds;

        public double AdaptiveElapsedSeconds => adaptiveElapsedSeconds;

        public double StabilizationElapsedSeconds => stabilizationElapsedSeconds;

        public double ActiveSessionElapsedSeconds =>
            acclimatizationElapsedSeconds
            + adaptiveElapsedSeconds
            + stabilizationElapsedSeconds;

        public int DecisionOpportunityCount => decisionSequenceNumber;

        public bool IsTerminal =>
            Phase == VrSessionPhase.Completed || Phase == VrSessionPhase.Aborted;

        public bool Initialize(double monotonicTimeSeconds)
        {
            ValidateMonotonicTime(monotonicTimeSeconds);

            if (Phase != VrSessionPhase.Boot)
            {
                return false;
            }

            hasObservedClock = true;
            lastMonotonicTimeSeconds = monotonicTimeSeconds;
            TransitionTo(
                VrSessionPhase.AwaitingConfig,
                SessionTransitionReason.BootCompleted,
                monotonicTimeSeconds);
            return true;
        }

        public bool AcceptConfiguration(
            SessionTimingConfiguration configuration,
            double monotonicTimeSeconds)
        {
            if (configuration == null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }

            AdvanceTo(monotonicTimeSeconds);
            if (Phase != VrSessionPhase.AwaitingConfig)
            {
                return false;
            }

            timingConfiguration = configuration;
            nextDecisionAtAdaptiveSeconds = configuration.DecisionIntervalSeconds;
            TransitionTo(
                VrSessionPhase.LoadingScene,
                SessionTransitionReason.ConfigurationAccepted,
                monotonicTimeSeconds);
            return true;
        }

        public bool MarkSceneLoaded(double monotonicTimeSeconds)
        {
            AdvanceTo(monotonicTimeSeconds);
            if (Phase != VrSessionPhase.LoadingScene)
            {
                return false;
            }

            TransitionTo(
                VrSessionPhase.Ready,
                SessionTransitionReason.SceneLoaded,
                monotonicTimeSeconds);
            return true;
        }

        public SessionCommandResult ProcessCommand(
            string commandId,
            SessionCommandType commandType,
            double monotonicTimeSeconds,
            bool hasFreshPhysiologyForResume)
        {
            AdvanceTo(monotonicTimeSeconds);
            var previousPhase = Phase;

            if (string.IsNullOrWhiteSpace(commandId))
            {
                return CreateCommandResult(
                    commandId,
                    commandType,
                    SessionCommandResultCode.InvalidCommandId,
                    previousPhase);
            }

            var normalizedCommandId = commandId.Trim();
            if (!processedCommandIds.Add(normalizedCommandId))
            {
                return CreateCommandResult(
                    normalizedCommandId,
                    commandType,
                    SessionCommandResultCode.DuplicateIgnored,
                    previousPhase);
            }

            if (!IsSupportedCommand(commandType))
            {
                return CreateCommandResult(
                    normalizedCommandId,
                    commandType,
                    SessionCommandResultCode.UnsupportedCommand,
                    previousPhase);
            }

            if (IsTerminal)
            {
                return CreateCommandResult(
                    normalizedCommandId,
                    commandType,
                    SessionCommandResultCode.SessionAlreadyTerminal,
                    previousPhase);
            }

            switch (commandType)
            {
                case SessionCommandType.Start:
                    return ProcessStart(normalizedCommandId, monotonicTimeSeconds);
                case SessionCommandType.Pause:
                    return ProcessPause(normalizedCommandId, monotonicTimeSeconds);
                case SessionCommandType.Resume:
                    return ProcessResume(
                        normalizedCommandId,
                        monotonicTimeSeconds,
                        hasFreshPhysiologyForResume);
                case SessionCommandType.Stop:
                    TransitionTo(
                        VrSessionPhase.Aborted,
                        SessionTransitionReason.StopCommand,
                        monotonicTimeSeconds);
                    return CreateCommandResult(
                        normalizedCommandId,
                        commandType,
                        SessionCommandResultCode.Accepted,
                        previousPhase);
                case SessionCommandType.EmergencyStop:
                    TransitionTo(
                        VrSessionPhase.Aborted,
                        SessionTransitionReason.EmergencyStopCommand,
                        monotonicTimeSeconds);
                    return CreateCommandResult(
                        normalizedCommandId,
                        commandType,
                        SessionCommandResultCode.Accepted,
                        previousPhase);
                default:
                    throw new ArgumentOutOfRangeException(nameof(commandType));
            }
        }

        public bool AbortForFatalError(double monotonicTimeSeconds)
        {
            AdvanceTo(monotonicTimeSeconds);
            if (IsTerminal)
            {
                return false;
            }

            TransitionTo(
                VrSessionPhase.Aborted,
                SessionTransitionReason.FatalError,
                monotonicTimeSeconds);
            return true;
        }

        public void AdvanceTo(double monotonicTimeSeconds)
        {
            ValidateMonotonicTime(monotonicTimeSeconds);

            if (!hasObservedClock)
            {
                hasObservedClock = true;
                lastMonotonicTimeSeconds = monotonicTimeSeconds;
                return;
            }

            if (monotonicTimeSeconds < lastMonotonicTimeSeconds)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(monotonicTimeSeconds),
                    monotonicTimeSeconds,
                    "Monotonic time cannot move backwards.");
            }

            var remainingSeconds =
                monotonicTimeSeconds - lastMonotonicTimeSeconds;
            lastMonotonicTimeSeconds = monotonicTimeSeconds;

            while (remainingSeconds > TimeComparisonTolerance
                && IsTimedPhase(Phase))
            {
                var phaseRemainingSeconds = GetPhaseRemainingSeconds();
                if (phaseRemainingSeconds <= TimeComparisonTolerance)
                {
                    CompleteCurrentTimedPhase(
                        monotonicTimeSeconds - remainingSeconds);
                    continue;
                }

                var consumedSeconds = Math.Min(
                    remainingSeconds,
                    phaseRemainingSeconds);
                var intervalStartTimeSeconds =
                    monotonicTimeSeconds - remainingSeconds;

                AddElapsedTime(
                    consumedSeconds,
                    intervalStartTimeSeconds);
                remainingSeconds -= consumedSeconds;

                if (phaseRemainingSeconds - consumedSeconds
                    <= TimeComparisonTolerance)
                {
                    CompleteCurrentTimedPhase(
                        monotonicTimeSeconds - remainingSeconds);
                }
            }
        }

        private SessionCommandResult ProcessStart(
            string commandId,
            double monotonicTimeSeconds)
        {
            var previousPhase = Phase;
            if (Phase != VrSessionPhase.Ready)
            {
                return CreateCommandResult(
                    commandId,
                    SessionCommandType.Start,
                    SessionCommandResultCode.InvalidForCurrentPhase,
                    previousPhase);
            }

            TransitionTo(
                VrSessionPhase.Acclimatization,
                SessionTransitionReason.StartCommand,
                monotonicTimeSeconds);
            return CreateCommandResult(
                commandId,
                SessionCommandType.Start,
                SessionCommandResultCode.Accepted,
                previousPhase);
        }

        private SessionCommandResult ProcessPause(
            string commandId,
            double monotonicTimeSeconds)
        {
            var previousPhase = Phase;
            if (Phase != VrSessionPhase.Adaptive)
            {
                return CreateCommandResult(
                    commandId,
                    SessionCommandType.Pause,
                    SessionCommandResultCode.InvalidForCurrentPhase,
                    previousPhase);
            }

            TransitionTo(
                VrSessionPhase.Paused,
                SessionTransitionReason.PauseCommand,
                monotonicTimeSeconds);
            return CreateCommandResult(
                commandId,
                SessionCommandType.Pause,
                SessionCommandResultCode.Accepted,
                previousPhase);
        }

        private SessionCommandResult ProcessResume(
            string commandId,
            double monotonicTimeSeconds,
            bool hasFreshPhysiologyForResume)
        {
            var previousPhase = Phase;
            if (Phase != VrSessionPhase.Paused)
            {
                return CreateCommandResult(
                    commandId,
                    SessionCommandType.Resume,
                    SessionCommandResultCode.InvalidForCurrentPhase,
                    previousPhase);
            }

            if (!hasFreshPhysiologyForResume)
            {
                return CreateCommandResult(
                    commandId,
                    SessionCommandType.Resume,
                    SessionCommandResultCode.FreshPhysiologyRequired,
                    previousPhase);
            }

            TransitionTo(
                VrSessionPhase.Adaptive,
                SessionTransitionReason.ResumeCommand,
                monotonicTimeSeconds);
            return CreateCommandResult(
                commandId,
                SessionCommandType.Resume,
                SessionCommandResultCode.Accepted,
                previousPhase);
        }

        private SessionCommandResult CreateCommandResult(
            string commandId,
            SessionCommandType commandType,
            SessionCommandResultCode resultCode,
            VrSessionPhase previousPhase)
        {
            return new SessionCommandResult(
                commandId,
                commandType,
                resultCode,
                previousPhase,
                Phase);
        }

        private void AddElapsedTime(
            double elapsedSeconds,
            double intervalStartTimeSeconds)
        {
            switch (Phase)
            {
                case VrSessionPhase.Acclimatization:
                    acclimatizationElapsedSeconds += elapsedSeconds;
                    break;
                case VrSessionPhase.Adaptive:
                    var previousAdaptiveElapsedSeconds = adaptiveElapsedSeconds;
                    adaptiveElapsedSeconds += elapsedSeconds;
                    EmitDecisionOpportunities(
                        previousAdaptiveElapsedSeconds,
                        intervalStartTimeSeconds);
                    break;
                case VrSessionPhase.Stabilization:
                    stabilizationElapsedSeconds += elapsedSeconds;
                    break;
                default:
                    throw new InvalidOperationException(
                        "Elapsed time can only be added to a timed session phase.");
            }
        }

        private void EmitDecisionOpportunities(
            double previousAdaptiveElapsedSeconds,
            double intervalStartTimeSeconds)
        {
            while (nextDecisionAtAdaptiveSeconds
                    <= adaptiveElapsedSeconds + TimeComparisonTolerance
                && nextDecisionAtAdaptiveSeconds
                    < timingConfiguration.AdaptiveDurationSeconds
                        - TimeComparisonTolerance)
            {
                decisionSequenceNumber++;
                var opportunityTimeSeconds = intervalStartTimeSeconds
                    + nextDecisionAtAdaptiveSeconds
                    - previousAdaptiveElapsedSeconds;
                DecisionOpportunityReached?.Invoke(
                    new SessionDecisionOpportunity(
                        decisionSequenceNumber,
                        opportunityTimeSeconds,
                        nextDecisionAtAdaptiveSeconds));
                nextDecisionAtAdaptiveSeconds +=
                    timingConfiguration.DecisionIntervalSeconds;
            }
        }

        private double GetPhaseRemainingSeconds()
        {
            switch (Phase)
            {
                case VrSessionPhase.Acclimatization:
                    return timingConfiguration.AcclimatizationDurationSeconds
                        - acclimatizationElapsedSeconds;
                case VrSessionPhase.Adaptive:
                    return timingConfiguration.AdaptiveDurationSeconds
                        - adaptiveElapsedSeconds;
                case VrSessionPhase.Stabilization:
                    return timingConfiguration.StabilizationDurationSeconds
                        - stabilizationElapsedSeconds;
                default:
                    return 0d;
            }
        }

        private void CompleteCurrentTimedPhase(double monotonicTimeSeconds)
        {
            switch (Phase)
            {
                case VrSessionPhase.Acclimatization:
                    TransitionTo(
                        VrSessionPhase.Adaptive,
                        SessionTransitionReason.AcclimatizationElapsed,
                        monotonicTimeSeconds);
                    break;
                case VrSessionPhase.Adaptive:
                    TransitionTo(
                        VrSessionPhase.Stabilization,
                        SessionTransitionReason.AdaptiveDurationElapsed,
                        monotonicTimeSeconds);
                    break;
                case VrSessionPhase.Stabilization:
                    TransitionTo(
                        VrSessionPhase.Completed,
                        SessionTransitionReason.StabilizationDurationElapsed,
                        monotonicTimeSeconds);
                    break;
                default:
                    throw new InvalidOperationException(
                        "Only a timed session phase can complete automatically.");
            }
        }

        private void TransitionTo(
            VrSessionPhase nextPhase,
            SessionTransitionReason reason,
            double monotonicTimeSeconds)
        {
            var previousPhase = Phase;
            Phase = nextPhase;
            PhaseChanged?.Invoke(
                new SessionPhaseTransition(
                    previousPhase,
                    nextPhase,
                    reason,
                    monotonicTimeSeconds,
                    ActiveSessionElapsedSeconds));
        }

        private static bool IsTimedPhase(VrSessionPhase phase)
        {
            return phase == VrSessionPhase.Acclimatization
                || phase == VrSessionPhase.Adaptive
                || phase == VrSessionPhase.Stabilization;
        }

        private static bool IsSupportedCommand(SessionCommandType commandType)
        {
            var commandValue = (int)commandType;
            return commandValue >= (int)SessionCommandType.Start
                && commandValue <= (int)SessionCommandType.EmergencyStop;
        }

        private static void ValidateMonotonicTime(double monotonicTimeSeconds)
        {
            if (double.IsNaN(monotonicTimeSeconds)
                || double.IsInfinity(monotonicTimeSeconds)
                || monotonicTimeSeconds < 0d)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(monotonicTimeSeconds),
                    monotonicTimeSeconds,
                    "Monotonic time must be finite and non-negative.");
            }
        }
    }
}

