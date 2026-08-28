using System;
using LaminarVR.AdaptiveMeditation.Environment;

namespace LaminarVR.AdaptiveMeditation.Policy
{
    public sealed class ActionOutcome
    {
        public ActionOutcome(
            string decisionId,
            PolicyDecision decision,
            EnvironmentAction executedAction,
            double reward,
            long preWindowSequenceNumber,
            long postWindowSequenceNumber)
        {
            if (string.IsNullOrWhiteSpace(decisionId))
            {
                throw new ArgumentException(
                    "A decision ID is required for outcome correlation.",
                    nameof(decisionId));
            }

            Decision = decision
                ?? throw new ArgumentNullException(nameof(decision));
            if (!Enum.IsDefined(typeof(EnvironmentAction), executedAction))
            {
                throw new ArgumentOutOfRangeException(nameof(executedAction));
            }

            if (double.IsNaN(reward) || double.IsInfinity(reward))
            {
                throw new ArgumentOutOfRangeException(nameof(reward));
            }

            if (preWindowSequenceNumber
                != decision.PhysiologySequenceNumber)
            {
                throw new ArgumentException(
                    "The outcome must reference the decision physiology window.",
                    nameof(preWindowSequenceNumber));
            }

            if (postWindowSequenceNumber <= preWindowSequenceNumber)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(postWindowSequenceNumber));
            }

            DecisionId = decisionId.Trim();
            ExecutedAction = executedAction;
            Reward = reward;
            PreWindowSequenceNumber = preWindowSequenceNumber;
            PostWindowSequenceNumber = postWindowSequenceNumber;
        }

        public string DecisionId { get; }

        public PolicyDecision Decision { get; }

        public EnvironmentAction ExecutedAction { get; }

        public double Reward { get; }

        public long PreWindowSequenceNumber { get; }

        public long PostWindowSequenceNumber { get; }
    }
}
