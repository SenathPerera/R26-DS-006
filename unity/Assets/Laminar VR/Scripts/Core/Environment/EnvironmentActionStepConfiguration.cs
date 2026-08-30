using System;

namespace LaminarVR.AdaptiveMeditation.Environment
{
    public sealed class EnvironmentActionStepConfiguration
    {
        public EnvironmentActionStepConfiguration(
            float illumination,
            float warmth,
            float atmosphericSoftness,
            float colorRichness,
            float ambientMotion)
        {
            Illumination = ValidateStep(illumination, nameof(illumination));
            Warmth = ValidateStep(warmth, nameof(warmth));
            AtmosphericSoftness = ValidateStep(
                atmosphericSoftness,
                nameof(atmosphericSoftness));
            ColorRichness = ValidateStep(
                colorRichness,
                nameof(colorRichness));
            AmbientMotion = ValidateStep(
                ambientMotion,
                nameof(ambientMotion));
        }

        public float Illumination { get; }

        public float Warmth { get; }

        public float AtmosphericSoftness { get; }

        public float ColorRichness { get; }

        public float AmbientMotion { get; }

        public float GetForAction(EnvironmentAction action)
        {
            switch (action)
            {
                case EnvironmentAction.NoChange:
                    return 0f;
                case EnvironmentAction.IncreaseIllumination:
                case EnvironmentAction.DecreaseIllumination:
                    return Illumination;
                case EnvironmentAction.IncreaseWarmth:
                case EnvironmentAction.DecreaseWarmth:
                    return Warmth;
                case EnvironmentAction.IncreaseAtmosphericSoftness:
                case EnvironmentAction.DecreaseAtmosphericSoftness:
                    return AtmosphericSoftness;
                case EnvironmentAction.IncreaseColorRichness:
                case EnvironmentAction.DecreaseColorRichness:
                    return ColorRichness;
                case EnvironmentAction.IncreaseAmbientMotion:
                case EnvironmentAction.DecreaseAmbientMotion:
                    return AmbientMotion;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(action),
                        action,
                        "The environment action is not supported.");
            }
        }

        private static float ValidateStep(float step, string parameterName)
        {
            if (float.IsNaN(step)
                || float.IsInfinity(step)
                || step <= 0f
                || step > 1f)
            {
                throw new ArgumentOutOfRangeException(
                    parameterName,
                    step,
                    "Action step must be finite, greater than 0, and no greater than 1.");
            }

            return step;
        }
    }
}
