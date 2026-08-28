using LaminarVR.AdaptiveMeditation.Environment;

namespace LaminarVR.AdaptiveMeditation.Preferences
{
    public sealed class EnvironmentPreference
    {
        public EnvironmentPreference(
            EnvironmentState preferredEnvironment,
            EnvironmentStateLimits? sensitivityLimits = null)
        {
            PreferredEnvironment = preferredEnvironment;
            SensitivityLimits = sensitivityLimits;
        }

        public EnvironmentState PreferredEnvironment { get; }

        public EnvironmentStateLimits? SensitivityLimits { get; }
    }
}

