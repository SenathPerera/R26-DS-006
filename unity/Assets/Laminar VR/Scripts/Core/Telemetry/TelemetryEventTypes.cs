namespace LaminarVR.AdaptiveMeditation.Telemetry
{
    // TODO(RESEARCH_DECISION): These are draft local event names. Freeze and
    // version the shared telemetry vocabulary before the pilot export schema.
    public static class TelemetryEventTypes
    {
        public const string ApplicationStarted = "application.started";
        public const string ApplicationVersion = "application.version";
        public const string DeviceInformation = "device.information";
        public const string SessionConfigReceived = "session.config_received";
        public const string SceneLoadStarted = "scene.load_started";
        public const string SceneLoadCompleted = "scene.load_completed";
        public const string SceneBindingValidation = "scene.binding_validation";
        public const string SessionPhaseChanged = "session.phase_changed";
        public const string PhysiologyReceived = "physiology.received";
        public const string PhysiologyRejected = "physiology.rejected";
        public const string DecisionRequested = "decision.requested";
        public const string PolicyCandidateScore = "policy.candidate_score";
        public const string ActionProposed = "action.proposed";
        public const string ActionValidated = "action.validated";
        public const string TransitionStarted = "transition.started";
        public const string TransitionCompleted = "transition.completed";
        public const string TransitionCancelled = "transition.cancelled";
        public const string RewardWindowOpened = "reward_window.opened";
        public const string RewardWindowClosed = "reward_window.closed";
        public const string RewardCalculated = "reward.calculated";
        public const string RewardInvalidated = "reward.invalidated";
        public const string BanditUpdated = "bandit.updated";
        public const string NetworkConnected = "network.connected";
        public const string NetworkDisconnected = "network.disconnected";
        public const string SessionPaused = "session.paused";
        public const string SessionResumed = "session.resumed";
        public const string EmergencyStop = "session.emergency_stop";
        public const string ApplicationFocusLost = "application.focus_lost";
        public const string ApplicationFocusRestored = "application.focus_restored";
        public const string PerformanceSample = "performance.sample";
        public const string SessionCompleted = "session.completed";
        public const string SessionAborted = "session.aborted";
        public const string LocalLogUploadSucceeded = "local_log.upload_succeeded";
        public const string LocalLogUploadFailed = "local_log.upload_failed";
    }
}
