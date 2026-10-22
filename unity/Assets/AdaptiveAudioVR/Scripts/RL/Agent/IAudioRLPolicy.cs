namespace AdaptiveAudioVR.RL.Agent
{
    public interface IAudioRLPolicy
    {
        bool IsReady { get; }
        string DisplayName { get; }
        AudioRLAction GetResidualAction(AudioRLState state);
    }
}
