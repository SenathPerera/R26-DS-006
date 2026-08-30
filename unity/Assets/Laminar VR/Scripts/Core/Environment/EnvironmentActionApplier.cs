using System;

namespace LaminarVR.AdaptiveMeditation.Environment
{
    public static class EnvironmentActionApplier
    {
        public static EnvironmentState Apply(
            EnvironmentState current,
            EnvironmentAction action,
            EnvironmentActionStepConfiguration actionSteps)
        {
            ValidateAction(action);
            if (actionSteps == null)
            {
                throw new ArgumentNullException(nameof(actionSteps));
            }

            if (!current.IsNormalized)
            {
                throw new ArgumentException(
                    "The current environment state must be normalized before applying an action.",
                    nameof(current));
            }

            var actionStep = actionSteps.GetForAction(action);
            switch (action)
            {
                case EnvironmentAction.NoChange:
                    return current;
                case EnvironmentAction.IncreaseIllumination:
                    return new EnvironmentState(
                        Clamp01(current.Illumination + actionStep),
                        current.Warmth,
                        current.AtmosphericSoftness,
                        current.ColorRichness,
                        current.AmbientMotion);
                case EnvironmentAction.DecreaseIllumination:
                    return new EnvironmentState(
                        Clamp01(current.Illumination - actionStep),
                        current.Warmth,
                        current.AtmosphericSoftness,
                        current.ColorRichness,
                        current.AmbientMotion);
                case EnvironmentAction.IncreaseWarmth:
                    return new EnvironmentState(
                        current.Illumination,
                        Clamp01(current.Warmth + actionStep),
                        current.AtmosphericSoftness,
                        current.ColorRichness,
                        current.AmbientMotion);
                case EnvironmentAction.DecreaseWarmth:
                    return new EnvironmentState(
                        current.Illumination,
                        Clamp01(current.Warmth - actionStep),
                        current.AtmosphericSoftness,
                        current.ColorRichness,
                        current.AmbientMotion);
                case EnvironmentAction.IncreaseAtmosphericSoftness:
                    return new EnvironmentState(
                        current.Illumination,
                        current.Warmth,
                        Clamp01(current.AtmosphericSoftness + actionStep),
                        current.ColorRichness,
                        current.AmbientMotion);
                case EnvironmentAction.DecreaseAtmosphericSoftness:
                    return new EnvironmentState(
                        current.Illumination,
                        current.Warmth,
                        Clamp01(current.AtmosphericSoftness - actionStep),
                        current.ColorRichness,
                        current.AmbientMotion);
                case EnvironmentAction.IncreaseColorRichness:
                    return new EnvironmentState(
                        current.Illumination,
                        current.Warmth,
                        current.AtmosphericSoftness,
                        Clamp01(current.ColorRichness + actionStep),
                        current.AmbientMotion);
                case EnvironmentAction.DecreaseColorRichness:
                    return new EnvironmentState(
                        current.Illumination,
                        current.Warmth,
                        current.AtmosphericSoftness,
                        Clamp01(current.ColorRichness - actionStep),
                        current.AmbientMotion);
                case EnvironmentAction.IncreaseAmbientMotion:
                    return new EnvironmentState(
                        current.Illumination,
                        current.Warmth,
                        current.AtmosphericSoftness,
                        current.ColorRichness,
                        Clamp01(current.AmbientMotion + actionStep));
                case EnvironmentAction.DecreaseAmbientMotion:
                    return new EnvironmentState(
                        current.Illumination,
                        current.Warmth,
                        current.AtmosphericSoftness,
                        current.ColorRichness,
                        Clamp01(current.AmbientMotion - actionStep));
                default:
                    throw new ArgumentOutOfRangeException(nameof(action), action, null);
            }
        }

        private static void ValidateAction(EnvironmentAction action)
        {
            var actionValue = (int)action;
            if (actionValue < (int)EnvironmentAction.NoChange
                || actionValue > (int)EnvironmentAction.DecreaseAmbientMotion)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(action),
                    action,
                    "The environment action is not supported.");
            }
        }

        private static float Clamp01(float value)
        {
            return Math.Min(1f, Math.Max(0f, value));
        }
    }
}
