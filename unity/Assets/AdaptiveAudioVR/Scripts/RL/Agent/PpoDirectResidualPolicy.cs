namespace AdaptiveAudioVR.RL.Agent
{
    public sealed class PpoDirectResidualPolicy : IAudioRLPolicy
    {
        private readonly PpoNetworkPolicyRuntime runtime = new PpoNetworkPolicyRuntime();

        public bool IsReady => runtime.IsLoaded;
        public float MaximumDelta => runtime.MaximumDelta;
        public string DisplayName => IsReady
            ? $"Direct PPO residual network, seed {runtime.Seed}"
            : "Direct PPO residual network unavailable";

        public bool TryLoad(string path, out string error)
        {
            return runtime.TryLoad(path, out error);
        }

        public AudioRLAction GetResidualAction(AudioRLState state)
        {
            if (!IsReady || state == null)
            {
                return AudioRLAction.NoChange;
            }

            float[] observation = AudioRLStateEncoder.EncodeForImportedPolicy(state, runtime.MaximumDelta);
            float[] normalized = runtime.QueryAction(observation);
            for (int i = 0; i < normalized.Length; i++)
            {
                normalized[i] *= runtime.MaximumDelta;
            }

            return AudioRLAction.FromArray(normalized).Clamp(runtime.MaximumDelta);
        }
    }
}
