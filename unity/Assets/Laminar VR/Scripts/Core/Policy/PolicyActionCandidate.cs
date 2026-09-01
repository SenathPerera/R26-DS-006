using System;
using LaminarVR.AdaptiveMeditation.Environment;

namespace LaminarVR.AdaptiveMeditation.Policy
{
    public readonly struct PolicyActionCandidate
    {
        public PolicyActionCandidate(
            EnvironmentAction action,
            double actionMagnitude)
        {
            var actionValue = (int)action;
            if (actionValue < (int)EnvironmentAction.NoChange
                || actionValue
                    > (int)EnvironmentAction.DecreaseAmbientMotion)
            {
                throw new ArgumentOutOfRangeException(nameof(action));
            }

            if (double.IsNaN(actionMagnitude)
                || double.IsInfinity(actionMagnitude)
                || actionMagnitude < 0d
                || actionMagnitude > 1d)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(actionMagnitude));
            }

            if (action == EnvironmentAction.NoChange
                ? actionMagnitude != 0d
                : actionMagnitude <= 0d)
            {
                throw new ArgumentException(
                    "NoChange must have zero magnitude and changing actions "
                    + "must have positive normalized magnitude.",
                    nameof(actionMagnitude));
            }

            Action = action;
            ActionMagnitude = actionMagnitude;
        }

        public EnvironmentAction Action { get; }

        public double ActionMagnitude { get; }
    }
}
