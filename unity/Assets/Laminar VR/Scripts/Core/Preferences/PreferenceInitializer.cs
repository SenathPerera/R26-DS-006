using System;
using LaminarVR.AdaptiveMeditation.Environment;

namespace LaminarVR.AdaptiveMeditation.Preferences
{
    public sealed class PreferenceInitializer
    {
        public PreferenceInitializationResult Initialize(
            SceneEnvironmentProfile sceneProfile,
            EnvironmentPreference preference,
            PreferenceInitializationConfiguration configuration)
        {
            if (sceneProfile == null)
            {
                throw new ArgumentNullException(nameof(sceneProfile));
            }

            if (preference == null)
            {
                throw new ArgumentNullException(nameof(preference));
            }

            if (configuration == null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }

            var effectiveLimits = sceneProfile.Limits;
            if (preference.SensitivityLimits.HasValue
                && !sceneProfile.Limits.TryIntersect(
                    preference.SensitivityLimits.Value,
                    out effectiveLimits))
            {
                return new PreferenceInitializationResult(
                    false,
                    PreferenceInitializationFailureReason
                        .SensitivityLimitsDoNotOverlapScene,
                    PreferenceInitializationAdjustment.None,
                    preference.PreferredEnvironment,
                    sceneProfile.SafeDefault,
                    sceneProfile.SafeDefault,
                    sceneProfile.SafeDefault,
                    sceneProfile.Limits);
            }

            var adjustments = PreferenceInitializationAdjustment.None;
            var normalizedPreference =
                preference.PreferredEnvironment.Clamp01();
            if (normalizedPreference != preference.PreferredEnvironment)
            {
                adjustments |=
                    PreferenceInitializationAdjustment.NormalizedDomainClamp;
            }

            var sceneClampedPreference =
                sceneProfile.Limits.Clamp(normalizedPreference);
            if (sceneClampedPreference != normalizedPreference)
            {
                adjustments |=
                    PreferenceInitializationAdjustment.SceneRangeClamp;
            }

            var requestedInitialState = Blend(
                sceneProfile.SafeDefault,
                sceneClampedPreference,
                configuration.PreferenceWeight);
            var safeInitialState = effectiveLimits.Clamp(requestedInitialState);
            if (safeInitialState != requestedInitialState)
            {
                adjustments |=
                    PreferenceInitializationAdjustment.SensitivityRangeClamp;
            }

            safeInitialState = sceneProfile.Limits.Clamp(safeInitialState);
            return new PreferenceInitializationResult(
                true,
                PreferenceInitializationFailureReason.None,
                adjustments,
                preference.PreferredEnvironment,
                sceneClampedPreference,
                requestedInitialState,
                safeInitialState,
                effectiveLimits);
        }

        private static EnvironmentState Blend(
            EnvironmentState safeDefault,
            EnvironmentState preference,
            double preferenceWeight)
        {
            var safeDefaultWeight = 1d - preferenceWeight;
            return new EnvironmentState(
                (float)((safeDefault.Illumination * safeDefaultWeight)
                    + (preference.Illumination * preferenceWeight)),
                (float)((safeDefault.Warmth * safeDefaultWeight)
                    + (preference.Warmth * preferenceWeight)),
                (float)((safeDefault.AtmosphericSoftness * safeDefaultWeight)
                    + (preference.AtmosphericSoftness * preferenceWeight)),
                (float)((safeDefault.ColorRichness * safeDefaultWeight)
                    + (preference.ColorRichness * preferenceWeight)),
                (float)((safeDefault.AmbientMotion * safeDefaultWeight)
                    + (preference.AmbientMotion * preferenceWeight)));
        }
    }
}

