using System;
using LaminarVR.AdaptiveMeditation.Environment;
using LaminarVR.AdaptiveMeditation.Policy;

namespace LaminarVR.AdaptiveMeditation.ResearchEvaluation
{
    public interface ISyntheticSafeOracleModel
    {
        double CalculateUtility(
            EnvironmentState environment,
            double sessionElapsedSeconds);

        double CalculateExpectedStressReduction(
            EnvironmentState environmentBefore,
            EnvironmentState environmentAfter,
            double sessionElapsedSeconds);
    }

    public readonly struct SafeOracleDecisionRecord
    {
        public SafeOracleDecisionRecord(
            EnvironmentAction proposedAction,
            EnvironmentAction evaluatedAction,
            EnvironmentAction oracleAction,
            double selectedExpectedStressReduction,
            double oracleExpectedStressReduction,
            double pseudoRegret,
            double utilityBefore,
            double selectedTargetUtility,
            EnvironmentState environmentBefore,
            EnvironmentState selectedTarget,
            int safeCandidateCount)
        {
            ProposedAction = proposedAction;
            EvaluatedAction = evaluatedAction;
            OracleAction = oracleAction;
            SelectedExpectedStressReduction = RequireFinite(
                selectedExpectedStressReduction,
                nameof(selectedExpectedStressReduction));
            OracleExpectedStressReduction = RequireFinite(
                oracleExpectedStressReduction,
                nameof(oracleExpectedStressReduction));
            PseudoRegret = RequireFiniteNonNegative(
                pseudoRegret,
                nameof(pseudoRegret));
            UtilityBefore = RequireFinite(utilityBefore, nameof(utilityBefore));
            SelectedTargetUtility = RequireFinite(
                selectedTargetUtility,
                nameof(selectedTargetUtility));
            if (!environmentBefore.IsNormalized
                || !selectedTarget.IsNormalized)
            {
                throw new ArgumentException(
                    "Oracle environments must be normalized.");
            }

            EnvironmentBefore = environmentBefore;
            SelectedTarget = selectedTarget;
            if (safeCandidateCount < 1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(safeCandidateCount));
            }

            SafeCandidateCount = safeCandidateCount;
        }

        public EnvironmentAction ProposedAction { get; }
        public EnvironmentAction EvaluatedAction { get; }
        public EnvironmentAction OracleAction { get; }
        public double SelectedExpectedStressReduction { get; }
        public double OracleExpectedStressReduction { get; }
        public double PseudoRegret { get; }
        public double UtilityBefore { get; }
        public double SelectedTargetUtility { get; }
        public EnvironmentState EnvironmentBefore { get; }
        public EnvironmentState SelectedTarget { get; }
        public int SafeCandidateCount { get; }

        private static double RequireFinite(double value, string parameterName)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                throw new ArgumentOutOfRangeException(parameterName);
            }

            return value;
        }

        private static double RequireFiniteNonNegative(
            double value,
            string parameterName)
        {
            if (value < 0d)
            {
                throw new ArgumentOutOfRangeException(parameterName);
            }

            return RequireFinite(value, parameterName);
        }
    }

    public sealed class SafeOracleEvaluator
    {
        private const double RegretTolerance = 1e-12d;
        private readonly SceneEnvironmentProfile sceneProfile;
        private readonly ISyntheticSafeOracleModel responseModel;

        public SafeOracleEvaluator(
            SceneEnvironmentProfile sceneProfile,
            ISyntheticSafeOracleModel responseModel)
        {
            this.sceneProfile = sceneProfile ?? throw new ArgumentNullException(
                nameof(sceneProfile));
            this.responseModel = responseModel ?? throw new ArgumentNullException(
                nameof(responseModel));
        }

        public SafeOracleDecisionRecord Evaluate(
            PolicyObservation observation,
            EnvironmentAction proposedAction,
            EnvironmentAction evaluatedAction,
            double sessionElapsedSeconds)
        {
            if (observation == null)
            {
                throw new ArgumentNullException(nameof(observation));
            }

            var selectedFound = false;
            var selectedExpectedReduction = 0d;
            var selectedTargetUtility = 0d;
            var selectedTarget = observation.CurrentEnvironment;
            var oracleAction = EnvironmentAction.NoChange;
            var oracleExpectedReduction = double.NegativeInfinity;
            var utilityBefore = responseModel.CalculateUtility(
                observation.CurrentEnvironment,
                sessionElapsedSeconds);

            for (var index = 0;
                index < observation.ActionCandidateCount;
                index++)
            {
                var action = observation.GetActionCandidate(index).Action;
                var target = CreateSafeTarget(
                    observation.CurrentEnvironment,
                    action);
                var expectedReduction = responseModel
                    .CalculateExpectedStressReduction(
                        observation.CurrentEnvironment,
                        target,
                        sessionElapsedSeconds);
                if (expectedReduction > oracleExpectedReduction)
                {
                    oracleExpectedReduction = expectedReduction;
                    oracleAction = action;
                }

                if (action == evaluatedAction)
                {
                    selectedFound = true;
                    selectedExpectedReduction = expectedReduction;
                    selectedTargetUtility = responseModel.CalculateUtility(
                        target,
                        sessionElapsedSeconds);
                    selectedTarget = target;
                }
            }

            if (!selectedFound)
            {
                throw new InvalidOperationException(
                    "The evaluated action was absent from the safe candidate set.");
            }

            var regret = oracleExpectedReduction - selectedExpectedReduction;
            if (regret < 0d && regret >= -RegretTolerance)
            {
                regret = 0d;
            }

            if (regret < 0d)
            {
                throw new InvalidOperationException(
                    "Safe-oracle regret cannot be negative.");
            }

            return new SafeOracleDecisionRecord(
                proposedAction,
                evaluatedAction,
                oracleAction,
                selectedExpectedReduction,
                oracleExpectedReduction,
                regret,
                utilityBefore,
                selectedTargetUtility,
                observation.CurrentEnvironment,
                selectedTarget,
                observation.ActionCandidateCount);
        }

        private EnvironmentState CreateSafeTarget(
            EnvironmentState current,
            EnvironmentAction action)
        {
            if (action == EnvironmentAction.NoChange)
            {
                return current;
            }

            return sceneProfile.Limits.Clamp(
                EnvironmentActionApplier.Apply(
                    current,
                    action,
                    sceneProfile.ActionSteps));
        }
    }
}
