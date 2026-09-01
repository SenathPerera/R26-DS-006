using System;

namespace LaminarVR.AdaptiveMeditation.Policy
{
    public enum PolicyResetReason
    {
        NewSession,
        SessionRestart,
        Manual
    }

    public sealed class PolicyResetContext
    {
        public PolicyResetContext(PolicyResetReason reason)
        {
            if (!Enum.IsDefined(typeof(PolicyResetReason), reason))
            {
                throw new ArgumentOutOfRangeException(nameof(reason));
            }

            Reason = reason;
        }

        public PolicyResetReason Reason { get; }
    }

    public class PolicyStateSnapshot
    {
        public PolicyStateSnapshot(
            string policyId,
            string policyVersion,
            string stateSchemaVersion,
            long decisionCount,
            long observedOutcomeCount,
            long modelUpdateCount)
        {
            PolicyId = RequireIdentity(policyId, nameof(policyId));
            PolicyVersion = RequireIdentity(
                policyVersion,
                nameof(policyVersion));
            StateSchemaVersion = RequireIdentity(
                stateSchemaVersion,
                nameof(stateSchemaVersion));
            ValidateNonNegative(decisionCount, nameof(decisionCount));
            ValidateNonNegative(
                observedOutcomeCount,
                nameof(observedOutcomeCount));
            ValidateNonNegative(modelUpdateCount, nameof(modelUpdateCount));

            DecisionCount = decisionCount;
            ObservedOutcomeCount = observedOutcomeCount;
            ModelUpdateCount = modelUpdateCount;
        }

        public string PolicyId { get; }

        public string PolicyVersion { get; }

        public string StateSchemaVersion { get; }

        public long DecisionCount { get; }

        public long ObservedOutcomeCount { get; }

        public long ModelUpdateCount { get; }

        private static string RequireIdentity(
            string value,
            string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException(
                    "Policy state identity values are required.",
                    parameterName);
            }

            return value.Trim();
        }

        private static void ValidateNonNegative(
            long value,
            string parameterName)
        {
            if (value < 0L)
            {
                throw new ArgumentOutOfRangeException(parameterName);
            }
        }
    }
}
