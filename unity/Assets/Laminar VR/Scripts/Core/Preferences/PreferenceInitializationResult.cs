using System;
using LaminarVR.AdaptiveMeditation.Environment;

namespace LaminarVR.AdaptiveMeditation.Preferences
{
    [Flags]
    public enum PreferenceInitializationAdjustment
    {
        None = 0,
        NormalizedDomainClamp = 1,
        SceneRangeClamp = 2,
        SensitivityRangeClamp = 4
    }

    public enum PreferenceInitializationFailureReason
    {
        None,
        SensitivityLimitsDoNotOverlapScene
    }

    public readonly struct PreferenceInitializationResult
    {
        public PreferenceInitializationResult(
            bool accepted,
            PreferenceInitializationFailureReason failureReason,
            PreferenceInitializationAdjustment adjustments,
            EnvironmentState declaredPreference,
            EnvironmentState sceneClampedPreference,
            EnvironmentState requestedInitialState,
            EnvironmentState safeInitialState,
            EnvironmentStateLimits effectiveLimits)
        {
            Accepted = accepted;
            FailureReason = failureReason;
            Adjustments = adjustments;
            DeclaredPreference = declaredPreference;
            SceneClampedPreference = sceneClampedPreference;
            RequestedInitialState = requestedInitialState;
            SafeInitialState = safeInitialState;
            EffectiveLimits = effectiveLimits;
        }

        public bool Accepted { get; }

        public PreferenceInitializationFailureReason FailureReason { get; }

        public PreferenceInitializationAdjustment Adjustments { get; }

        public EnvironmentState DeclaredPreference { get; }

        public EnvironmentState SceneClampedPreference { get; }

        public EnvironmentState RequestedInitialState { get; }

        public EnvironmentState SafeInitialState { get; }

        public EnvironmentStateLimits EffectiveLimits { get; }

        public bool WasAdjusted =>
            Adjustments != PreferenceInitializationAdjustment.None;
    }
}

