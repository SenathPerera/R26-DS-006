namespace LaminarVR.AdaptiveMeditation.Policy
{
    public interface IEnvironmentPolicy
    {
        string PolicyId { get; }

        string PolicyVersion { get; }

        PolicyDecision SelectAction(PolicyObservation observation);

        void ObserveOutcome(ActionOutcome outcome);

        PolicyStateSnapshot CaptureState();

        void Reset(PolicyResetContext context);
    }
}
