namespace AdaptiveAudioVR.RL.Agent
{
    public sealed class PpoSampledResidualPolicy : IAudioRLPolicy
    {
        private readonly ImportedPolicyRuntime runtime = new ImportedPolicyRuntime();
        private readonly int requestedNeighbors;

        public bool IsReady => runtime.IsLoaded;
        public float MaximumDelta => runtime.MaxDelta;
        public string DisplayName => IsReady
            ? $"{runtime.Algorithm.ToUpperInvariant()}-derived sampled residual policy, seed {runtime.Seed} ({runtime.SampleCount} states)"
            : "PPO-derived policy unavailable";

        public PpoSampledResidualPolicy(int requestedNeighbors)
        {
            this.requestedNeighbors = requestedNeighbors;
        }

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

            float[] observation = AudioRLStateEncoder.EncodeForImportedPolicy(state, runtime.MaxDelta);
            float[] normalized = runtime.QueryAction(observation, requestedNeighbors);
            for (int i = 0; i < normalized.Length; i++)
            {
                normalized[i] *= runtime.MaxDelta;
            }

            return AudioRLAction.FromArray(normalized).Clamp(runtime.MaxDelta);
        }
    }
}
