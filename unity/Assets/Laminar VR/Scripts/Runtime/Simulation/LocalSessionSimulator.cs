#if UNITY_EDITOR || DEVELOPMENT_BUILD
using LaminarVR.AdaptiveMeditation.Physiology;
using LaminarVR.AdaptiveMeditation.Runtime.Configuration;
using LaminarVR.AdaptiveMeditation.Session;
using UnityEngine;

namespace LaminarVR.AdaptiveMeditation.Runtime.Simulation
{
    [AddComponentMenu("Adaptive Meditation/Development/Local Session Simulator")]
    [DisallowMultipleComponent]
    public sealed class LocalSessionSimulator : MonoBehaviour
    {
        [Header("Research Configuration")]
        [SerializeField]
        private SessionTimingProfile timingProfile = null;

        [SerializeField]
        private LocalPhysiologySimulator physiologySimulator = null;

        [Header("Development Controls")]
        [SerializeField, Min(0f)]
        private float simulationSpeedMultiplier = 1f;

        [SerializeField]
        private bool showDebugPanel = true;

        private SessionStateMachine stateMachine;
        private double simulatedMonotonicTimeSeconds;
        private int localCommandSequence;
        private long pausePhysiologySequenceNumber;
        private string statusMessage = string.Empty;

        public VrSessionPhase Phase => stateMachine == null
            ? VrSessionPhase.Boot
            : stateMachine.Phase;

        private void Awake()
        {
            stateMachine = new SessionStateMachine();
            stateMachine.PhaseChanged += HandlePhaseChanged;
            stateMachine.DecisionOpportunityReached += HandleDecisionOpportunity;
            stateMachine.Initialize(simulatedMonotonicTimeSeconds);
            statusMessage = "Awaiting an approved timing configuration.";
        }

        private void Update()
        {
            if (stateMachine == null
                || simulationSpeedMultiplier <= 0f
                || float.IsNaN(simulationSpeedMultiplier)
                || float.IsInfinity(simulationSpeedMultiplier))
            {
                return;
            }

            simulatedMonotonicTimeSeconds +=
                Time.unscaledDeltaTime * simulationSpeedMultiplier;
            stateMachine.AdvanceTo(simulatedMonotonicTimeSeconds);
        }

        private void OnDestroy()
        {
            if (stateMachine == null)
            {
                return;
            }

            stateMachine.PhaseChanged -= HandlePhaseChanged;
            stateMachine.DecisionOpportunityReached -= HandleDecisionOpportunity;
        }

        private void OnGUI()
        {
            if (!showDebugPanel || stateMachine == null)
            {
                return;
            }

            GUILayout.BeginArea(
                new Rect(16f, 16f, 380f, 450f),
                "Adaptive Meditation - Local Session",
                GUI.skin.window);
            GUILayout.Label("Phase: " + stateMachine.Phase);
            GUILayout.Label(
                "Simulated clock: "
                + simulatedMonotonicTimeSeconds.ToString("F1")
                + " s");
            GUILayout.Label(
                "Active session: "
                + stateMachine.ActiveSessionElapsedSeconds.ToString("F1")
                + " s");
            GUILayout.Label(
                "Decision opportunities: "
                + stateMachine.DecisionOpportunityCount);
            GUILayout.Label(
                "Physiology sequence: "
                + (physiologySimulator == null
                    ? "not assigned"
                    : physiologySimulator.LatestAcceptedSequenceNumber.ToString()));
            GUILayout.Space(6f);

            DrawPhaseButton(
                "Load Approved Configuration",
                stateMachine.Phase == VrSessionPhase.AwaitingConfig,
                LoadApprovedConfiguration);
            DrawPhaseButton(
                "Mark Scene Loaded",
                stateMachine.Phase == VrSessionPhase.LoadingScene,
                MarkSceneLoaded);
            DrawPhaseButton(
                "Start Session",
                stateMachine.Phase == VrSessionPhase.Ready,
                () => ProcessLocalCommand(SessionCommandType.Start, false));
            DrawPhaseButton(
                "Pause",
                stateMachine.Phase == VrSessionPhase.Adaptive,
                Pause);
            DrawPhaseButton(
                "Resume",
                stateMachine.Phase == VrSessionPhase.Paused,
                Resume);
            DrawPhaseButton(
                "Emit Mock Physiology Now",
                physiologySimulator != null && physiologySimulator.IsInitialized,
                EmitMockPhysiology);
            DrawPhaseButton(
                "Stop",
                !stateMachine.IsTerminal,
                () => ProcessLocalCommand(SessionCommandType.Stop, false));
            DrawPhaseButton(
                "Emergency Stop",
                !stateMachine.IsTerminal,
                () => ProcessLocalCommand(SessionCommandType.EmergencyStop, false));

            GUILayout.Space(6f);
            GUILayout.Label("Status: " + statusMessage);
            GUILayout.EndArea();
        }

        private void LoadApprovedConfiguration()
        {
            if (timingProfile == null)
            {
                statusMessage = "Assign a SessionTimingProfile.";
                return;
            }

            if (!timingProfile.TryCreateRuntimeConfiguration(
                    out var configuration,
                    out var validationError))
            {
                statusMessage = validationError;
                return;
            }

            var accepted = stateMachine.AcceptConfiguration(
                configuration,
                simulatedMonotonicTimeSeconds);
            statusMessage = accepted
                ? "Configuration accepted. Mark the scene as loaded when ready."
                : "Configuration was not valid for the current phase.";
        }

        private void MarkSceneLoaded()
        {
            var accepted = stateMachine.MarkSceneLoaded(
                simulatedMonotonicTimeSeconds);
            statusMessage = accepted
                ? "Scene ready."
                : "Scene-loaded signal was not valid for the current phase.";
        }

        private void Resume()
        {
            var hasFreshPhysiology = physiologySimulator != null
                && physiologySimulator.HasFreshDecisionWindowAfter(
                    pausePhysiologySequenceNumber);
            var result = ProcessLocalCommand(
                SessionCommandType.Resume,
                hasFreshPhysiology);
            if (!result.Applied
                && result.ResultCode
                    == SessionCommandResultCode.FreshPhysiologyRequired)
            {
                statusMessage =
                    "Resume requires a new, fresh, decision-quality physiology window.";
            }
        }

        private void Pause()
        {
            var result = ProcessLocalCommand(SessionCommandType.Pause, false);
            if (result.Applied)
            {
                pausePhysiologySequenceNumber = physiologySimulator == null
                    ? 0L
                    : physiologySimulator.LatestAcceptedSequenceNumber;
            }
        }

        private void EmitMockPhysiology()
        {
            var result = physiologySimulator.EmitNow();
            statusMessage = result.Accepted
                ? "Mock physiology window accepted."
                : "Mock physiology rejected: "
                    + result.ResultCode
                    + "/"
                    + result.ValidationReasonCode;
        }

        private SessionCommandResult ProcessLocalCommand(
            SessionCommandType commandType,
            bool hasFreshPhysiologyForResume)
        {
            localCommandSequence++;
            var commandId = "local-simulator-" + localCommandSequence;
            var result = stateMachine.ProcessCommand(
                commandId,
                commandType,
                simulatedMonotonicTimeSeconds,
                hasFreshPhysiologyForResume);
            statusMessage = commandType + ": " + result.ResultCode;
            Debug.Log(
                "[LocalSessionSimulator] command"
                + " id=" + commandId
                + " type=" + commandType
                + " result=" + result.ResultCode
                + " phase=" + result.CurrentPhase,
                this);
            return result;
        }

        private void HandlePhaseChanged(SessionPhaseTransition transition)
        {
            Debug.Log(
                "[LocalSessionSimulator] phase_transition"
                + " previous=" + transition.PreviousPhase
                + " current=" + transition.CurrentPhase
                + " reason=" + transition.Reason
                + " monotonic_seconds="
                + transition.MonotonicTimeSeconds.ToString("F3")
                + " active_seconds="
                + transition.ActiveSessionElapsedSeconds.ToString("F3"),
                this);
        }

        private void HandleDecisionOpportunity(
            SessionDecisionOpportunity opportunity)
        {
            var physiologyResult = PhysiologyQueryResultCode.NoData;
            var physiologySequence = 0L;
            if (physiologySimulator != null
                && physiologySimulator.TryGetLatestDecisionWindow(
                    0L,
                    out var snapshot,
                    out physiologyResult))
            {
                physiologySequence = snapshot.SequenceNumber;
            }

            Debug.Log(
                "[LocalSessionSimulator] decision_opportunity"
                + " sequence=" + opportunity.SequenceNumber
                + " monotonic_seconds="
                + opportunity.MonotonicTimeSeconds.ToString("F3")
                + " adaptive_seconds="
                + opportunity.AdaptiveElapsedSeconds.ToString("F3")
                + " physiology_result=" + physiologyResult
                + " physiology_sequence=" + physiologySequence,
                this);
        }

        private static void DrawPhaseButton(
            string label,
            bool enabled,
            System.Action action)
        {
            var previousEnabled = GUI.enabled;
            GUI.enabled = enabled;
            if (GUILayout.Button(label))
            {
                action();
            }

            GUI.enabled = previousEnabled;
        }
    }
}
#endif
