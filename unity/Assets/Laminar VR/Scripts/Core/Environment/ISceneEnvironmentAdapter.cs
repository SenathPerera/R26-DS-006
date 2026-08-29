namespace LaminarVR.AdaptiveMeditation.Environment
{
    public interface ISceneEnvironmentAdapter
    {
        string SceneId { get; }

        SceneBindingValidation ValidateBindings();

        void ApplyState(EnvironmentState state);
    }
}
