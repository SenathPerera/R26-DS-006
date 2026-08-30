namespace LaminarVR.AdaptiveMeditation.Environment
{
    public interface IEnvironmentParameterManager
    {
        EnvironmentState CurrentState { get; }

        EnvironmentState TargetState { get; }

        bool IsTransitionActive { get; }

        string ActiveTransitionId { get; }

        void BeginTransition(
            string transitionId,
            EnvironmentState targetState,
            double startMonotonicTimeSeconds,
            double durationSeconds);

        EnvironmentTransitionProgress AdvanceTransition(
            double currentMonotonicTimeSeconds);

        bool CancelTransition(out string cancelledTransitionId);
    }
}
