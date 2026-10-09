using System;
using LaminarVR.AdaptiveMeditation.Physiology;

namespace LaminarVR.AdaptiveMeditation.Runtime.Networking
{
    public sealed class AcceptedComponentBStressPayload
    {
        private const double MaximumContinuousStressScore = 3d;

        public AcceptedComponentBStressPayload(
            string rawJson,
            PhysiologyWindow window)
        {
            if (string.IsNullOrWhiteSpace(rawJson))
            {
                throw new ArgumentException(
                    "An accepted Component B payload requires its raw JSON.",
                    nameof(rawJson));
            }

            RawJson = rawJson;
            Window = window
                ?? throw new ArgumentNullException(nameof(window));
        }

        public string RawJson { get; }

        public PhysiologyWindow Window { get; }

        public float NormalizedContinuousStress =>
            (float)Math.Max(
                0d,
                Math.Min(
                    1d,
                    Window.Stress.ContinuousScore
                    / MaximumContinuousStressScore));

        public float Confidence =>
            (float)Math.Max(
                0d,
                Math.Min(1d, Window.Stress.Confidence));
    }
}
