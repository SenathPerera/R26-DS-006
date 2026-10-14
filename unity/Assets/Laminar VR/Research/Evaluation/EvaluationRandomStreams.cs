using System;

namespace LaminarVR.AdaptiveMeditation.ResearchEvaluation
{
    public enum EvaluationRandomStreamId : ulong
    {
        ParticipantAndInitialState = 1UL,
        PhysiologyNoise = 2UL,
        MissingWindowSchedule = 3UL,
        RandomSafeActionSelection = 4UL,
        StochasticFaultSchedule = 5UL
    }

    public static class EvaluationRandomStreams
    {
        public const string InitializerVersion = "pcg32-splitmix64-streams/1.0";

        public static Pcg32 Create(
            int sessionSeed,
            EvaluationRandomStreamId streamId)
        {
            if (sessionSeed < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(sessionSeed));
            }

            if (!Enum.IsDefined(typeof(EvaluationRandomStreamId), streamId))
            {
                throw new ArgumentOutOfRangeException(nameof(streamId));
            }

            var root = (ulong)(uint)sessionSeed;
            var stream = (ulong)streamId;
            var initialState = Mix64(
                root ^ 0x853C49E6748FEA9BUL
                ^ unchecked(stream * 0x9E3779B97F4A7C15UL));
            var sequence = Mix64(
                root ^ 0xDA3E39CB94B95BDBUL
                ^ unchecked(stream * 0xD1B54A32D192ED03UL));
            return new Pcg32(initialState, sequence);
        }

        private static ulong Mix64(ulong value)
        {
            value = unchecked(value + 0x9E3779B97F4A7C15UL);
            value = unchecked(
                (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL);
            value = unchecked(
                (value ^ (value >> 27)) * 0x94D049BB133111EBUL);
            return value ^ (value >> 31);
        }
    }
}
