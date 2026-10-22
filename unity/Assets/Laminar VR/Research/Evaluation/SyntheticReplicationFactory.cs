using System;
using LaminarVR.AdaptiveMeditation.Environment;

namespace LaminarVR.AdaptiveMeditation.ResearchEvaluation
{
    public sealed class SyntheticReplication
    {
        internal SyntheticReplication(
            int seed,
            SyntheticArchetypeDefinition archetype,
            EnvironmentState preference,
            SyntheticSessionParameters sessionParameters)
        {
            Seed = seed;
            Archetype = archetype;
            Preference = preference;
            SessionParameters = sessionParameters;
        }

        public int Seed { get; }

        public SyntheticArchetypeDefinition Archetype { get; }

        public EnvironmentState Preference { get; }

        public SyntheticSessionParameters SessionParameters { get; }

        public string RandomInitializerVersion =>
            EvaluationRandomStreams.InitializerVersion;

        public ProtocolSyntheticPhysiologyModel CreatePhysiologyModel(
            double noiseMultiplier = 1d)
        {
            return new ProtocolSyntheticPhysiologyModel(
                Archetype,
                Preference,
                SessionParameters,
                EvaluationRandomStreams.Create(
                    Seed,
                    EvaluationRandomStreamId.PhysiologyNoise),
                noiseMultiplier);
        }

        public Pcg32 CreateRandomStream(EvaluationRandomStreamId streamId)
        {
            return EvaluationRandomStreams.Create(Seed, streamId);
        }
    }

    public static class SyntheticReplicationFactory
    {
        public static SyntheticReplication Create(
            int seed,
            SyntheticArchetypeId archetypeId)
        {
            if (seed < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(seed));
            }

            var participantRandom = EvaluationRandomStreams.Create(
                seed,
                EvaluationRandomStreamId.ParticipantAndInitialState);
            var preference = new EnvironmentState(
                (float)participantRandom.NextDouble(0.2d, 0.8d),
                (float)participantRandom.NextDouble(0.2d, 0.8d),
                (float)participantRandom.NextDouble(0.2d, 0.8d),
                (float)participantRandom.NextDouble(0.2d, 0.8d),
                (float)participantRandom.NextDouble(0.2d, 0.8d));
            var parameters = new SyntheticSessionParameters(
                participantRandom.NextDouble(1.7d, 2.7d),
                participantRandom.NextDouble(62d, 82d),
                participantRandom.NextDouble(25d, 65d),
                participantRandom.NextDouble(0.02d, 0.06d));
            return new SyntheticReplication(
                seed,
                SyntheticArchetypeCatalog.Get(archetypeId),
                preference,
                parameters);
        }
    }
}
