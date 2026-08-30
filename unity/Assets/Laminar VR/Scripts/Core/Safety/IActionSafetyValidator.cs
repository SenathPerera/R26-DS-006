using LaminarVR.AdaptiveMeditation.Environment;

namespace LaminarVR.AdaptiveMeditation.Safety
{
    public interface IActionSafetyValidator
    {
        ActionValidationResult Validate(
            EnvironmentAction proposedAction,
            EnvironmentState currentState,
            SceneEnvironmentProfile sceneProfile,
            SafetyRuntimeState runtimeState,
            ActionSafetyLimits safetyLimits);
    }
}
