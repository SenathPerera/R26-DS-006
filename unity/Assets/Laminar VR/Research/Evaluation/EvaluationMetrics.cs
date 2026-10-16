using System;
using System.Collections.Generic;
using LaminarVR.AdaptiveMeditation.Environment;
using LaminarVR.AdaptiveMeditation.Physiology;
using LaminarVR.AdaptiveMeditation.Policy;

namespace LaminarVR.AdaptiveMeditation.ResearchEvaluation
{
    public sealed class EvaluationSessionSummary
    {
        private readonly int[] selectedActionCounts;
        private readonly int[] decisionResultCounts;
        private readonly int[] rewardResultCounts;

        public EvaluationSessionSummary(
            string experimentId,
            SyntheticArchetypeId archetype,
            string scenarioCode,
            EvaluationPolicyCondition policyCondition,
            int seed,
            bool baselineAvailable,
            int eligibleDecisionCount,
            double? meanPseudoRegret,
            double cumulativePseudoRegret,
            double? firstToFinalThirdRegretChange,
            double cumulativeProductionReward,
            double? meanProductionReward,
            double? finalMinusBaselineStressScore,
            double? stressAreaUnderCurve,
            double? finalMinusBaselineHeartRateBpm,
            double? finalMinusBaselineRmssdMs,
            int executedChangeCount,
            int validOutcomeCount,
            long modelUpdateCount,
            int noChangeCount,
            double actionEntropyNats,
            double totalNormalizedVariation,
            int? firstOpportunityBelowFirstThirdMean,
            double? bestVisitedUtility,
            double? timeToBestVisitedStateSeconds,
            int? observationsToBestVisitedState,
            int sceneLimitViolationCount,
            int multiDimensionViolationCount,
            int safetyRejectedOrModifiedCount,
            int invalidModelUpdateCount,
            bool allAppliedStatesSafe,
            int[] selectedActionCounts,
            int[] decisionResultCounts,
            int[] rewardResultCounts)
        {
            ExperimentId = RequireText(experimentId, nameof(experimentId));
            Archetype = archetype;
            ScenarioCode = RequireText(scenarioCode, nameof(scenarioCode));
            PolicyCondition = policyCondition;
            Seed = seed;
            BaselineAvailable = baselineAvailable;
            EligibleDecisionCount = eligibleDecisionCount;
            MeanPseudoRegret = meanPseudoRegret;
            CumulativePseudoRegret = cumulativePseudoRegret;
            FirstToFinalThirdRegretChange =
                firstToFinalThirdRegretChange;
            CumulativeProductionReward = cumulativeProductionReward;
            MeanProductionReward = meanProductionReward;
            FinalMinusBaselineStressScore = finalMinusBaselineStressScore;
            StressAreaUnderCurve = stressAreaUnderCurve;
            FinalMinusBaselineHeartRateBpm =
                finalMinusBaselineHeartRateBpm;
            FinalMinusBaselineRmssdMs = finalMinusBaselineRmssdMs;
            ExecutedChangeCount = executedChangeCount;
            ValidOutcomeCount = validOutcomeCount;
            ModelUpdateCount = modelUpdateCount;
            NoChangeCount = noChangeCount;
            ActionEntropyNats = actionEntropyNats;
            TotalNormalizedVariation = totalNormalizedVariation;
            FirstOpportunityBelowFirstThirdMean =
                firstOpportunityBelowFirstThirdMean;
            BestVisitedUtility = bestVisitedUtility;
            TimeToBestVisitedStateSeconds = timeToBestVisitedStateSeconds;
            ObservationsToBestVisitedState = observationsToBestVisitedState;
            SceneLimitViolationCount = sceneLimitViolationCount;
            MultiDimensionViolationCount = multiDimensionViolationCount;
            SafetyRejectedOrModifiedCount = safetyRejectedOrModifiedCount;
            InvalidModelUpdateCount = invalidModelUpdateCount;
            AllAppliedStatesSafe = allAppliedStatesSafe;
            this.selectedActionCounts = CopyCounts(
                selectedActionCounts,
                (int)EnvironmentAction.DecreaseAmbientMotion + 1,
                nameof(selectedActionCounts));
            this.decisionResultCounts = CopyCounts(
                decisionResultCounts,
                Enum.GetValues(typeof(PolicyDecisionCycleResultCode)).Length,
                nameof(decisionResultCounts));
            this.rewardResultCounts = CopyCounts(
                rewardResultCounts,
                Enum.GetValues(typeof(PolicyRewardCycleResultCode)).Length,
                nameof(rewardResultCounts));
        }

        public string ExperimentId { get; }
        public SyntheticArchetypeId Archetype { get; }
        public string ScenarioCode { get; }
        public EvaluationPolicyCondition PolicyCondition { get; }
        public int Seed { get; }
        public bool BaselineAvailable { get; }
        public int EligibleDecisionCount { get; }
        public double? MeanPseudoRegret { get; }
        public double CumulativePseudoRegret { get; }
        public double? FirstToFinalThirdRegretChange { get; }
        public double CumulativeProductionReward { get; }
        public double? MeanProductionReward { get; }
        public double? FinalMinusBaselineStressScore { get; }
        public double? StressAreaUnderCurve { get; }
        public double? FinalMinusBaselineHeartRateBpm { get; }
        public double? FinalMinusBaselineRmssdMs { get; }
        public int ExecutedChangeCount { get; }
        public int ValidOutcomeCount { get; }
        public long ModelUpdateCount { get; }
        public int NoChangeCount { get; }
        public double ActionEntropyNats { get; }
        public double TotalNormalizedVariation { get; }
        public int? FirstOpportunityBelowFirstThirdMean { get; }
        public double? BestVisitedUtility { get; }
        public double? TimeToBestVisitedStateSeconds { get; }
        public int? ObservationsToBestVisitedState { get; }
        public int SceneLimitViolationCount { get; }
        public int MultiDimensionViolationCount { get; }
        public int SafetyRejectedOrModifiedCount { get; }
        public int InvalidModelUpdateCount { get; }
        public bool AllAppliedStatesSafe { get; }

        public int GetSelectedActionCount(EnvironmentAction action)
        {
            return selectedActionCounts[(int)action];
        }

        public int GetDecisionResultCount(
            PolicyDecisionCycleResultCode resultCode)
        {
            return decisionResultCounts[(int)resultCode];
        }

        public int GetRewardResultCount(PolicyRewardCycleResultCode resultCode)
        {
            return rewardResultCounts[(int)resultCode];
        }

        private static string RequireText(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException(
                    "A non-empty identity is required.",
                    parameterName);
            }

            return value.Trim();
        }

        private static int[] CopyCounts(
            int[] source,
            int expectedLength,
            string parameterName)
        {
            if (source == null || source.Length != expectedLength)
            {
                throw new ArgumentException(
                    "The count vector has an unexpected length.",
                    parameterName);
            }

            var copy = new int[source.Length];
            for (var index = 0; index < source.Length; index++)
            {
                if (source[index] < 0)
                {
                    throw new ArgumentOutOfRangeException(parameterName);
                }

                copy[index] = source[index];
            }

            return copy;
        }
    }

    public static class EvaluationMetricCalculator
    {
        public static EvaluationSessionSummary Calculate(
            string experimentId,
            SyntheticArchetypeId archetype,
            string scenarioCode,
            EvaluationPolicyCondition policyCondition,
            int seed,
            EvaluationRuntimeBundle bundle,
            EvaluationSessionResult result)
        {
            if (bundle == null)
            {
                throw new ArgumentNullException(nameof(bundle));
            }

            if (result == null)
            {
                throw new ArgumentNullException(nameof(result));
            }

            var oracleRecords = new List<SafeOracleDecisionRecord>();
            var actionCounts = new int[
                (int)EnvironmentAction.DecreaseAmbientMotion + 1];
            var decisionResultCounts = new int[
                Enum.GetValues(typeof(PolicyDecisionCycleResultCode)).Length];
            var rewardResultCounts = new int[
                Enum.GetValues(typeof(PolicyRewardCycleResultCode)).Length];
            var executedChanges = 0;
            var noChangeCount = 0;
            var safetyInterventions = 0;
            var multiDimensionViolations = 0;
            var totalVariation = 0d;
            for (var index = 0; index < result.Decisions.Length; index++)
            {
                var decisionRecord = result.Decisions[index];
                decisionResultCounts[(int)decisionRecord.Result.ResultCode]++;
                if (decisionRecord.SafeOracle.HasValue)
                {
                    var oracle = decisionRecord.SafeOracle.Value;
                    oracleRecords.Add(oracle);
                    actionCounts[(int)oracle.ProposedAction]++;
                    noChangeCount += oracle.ProposedAction
                            == EnvironmentAction.NoChange
                        ? 1
                        : 0;
                }

                if (!decisionRecord.Result.Validation.HasValue)
                {
                    continue;
                }

                var validation = decisionRecord.Result.Validation.Value;
                if (!validation.Accepted || validation.Modified)
                {
                    safetyInterventions++;
                }

                if (!validation.Accepted
                    || validation.ExecutedAction
                        == EnvironmentAction.NoChange)
                {
                    continue;
                }

                executedChanges++;
                totalVariation += validation.AppliedVariation;
                var environmentBefore = decisionRecord.SafeOracle.HasValue
                    ? decisionRecord.SafeOracle.Value.EnvironmentBefore
                    : result.InitialState;
                if (CountChangedDimensions(
                        validation.RequestedTarget,
                        validation.SafeTarget) > 1
                    || CountChangedDimensions(
                        environmentBefore,
                        validation.SafeTarget) > 1)
                {
                    multiDimensionViolations++;
                }
            }

            var cumulativeRegret = 0d;
            for (var index = 0; index < oracleRecords.Count; index++)
            {
                cumulativeRegret += oracleRecords[index].PseudoRegret;
            }

            var rewardCount = 0;
            var cumulativeReward = 0d;
            for (var index = 0; index < result.Rewards.Length; index++)
            {
                rewardResultCounts[(int)result.Rewards[index]
                    .Result.ResultCode]++;
                if (result.Rewards[index].Result.ResultCode
                    != PolicyRewardCycleResultCode.RewardApplied)
                {
                    continue;
                }

                rewardCount++;
                cumulativeReward += result.Rewards[index]
                    .Result.Calculation.Breakdown.TotalReward;
            }

            var acceptedWindows = GetAcceptedWindows(result);
            CalculatePhysiologyOutcomes(
                acceptedWindows,
                bundle.Timing.AcclimatizationDurationSeconds,
                out var stressChange,
                out var stressArea,
                out var heartRateChange,
                out var rmssdChange);
            if (!result.BaselineAvailable)
            {
                stressChange = null;
                heartRateChange = null;
                rmssdChange = null;
            }
            CalculateBestVisited(
                result.Decisions,
                out var bestUtility,
                out var bestTime,
                out var observationsToBest);

            var invalidModelUpdates = policyCondition
                    == EvaluationPolicyCondition.ContextualBandit
                ? (int)Math.Max(
                    0L,
                    result.PolicyState.ModelUpdateCount - rewardCount)
                : 0;
            var sceneViolations = 0;
            for (var index = 0; index < result.AppliedStates.Length; index++)
            {
                if (!result.AppliedStates[index].IsNormalized
                    || !bundle.Scene.Limits.Contains(
                        result.AppliedStates[index]))
                {
                    sceneViolations++;
                }
            }

            return new EvaluationSessionSummary(
                experimentId,
                archetype,
                scenarioCode,
                policyCondition,
                seed,
                result.BaselineAvailable,
                oracleRecords.Count,
                oracleRecords.Count > 0
                    ? cumulativeRegret / oracleRecords.Count
                    : (double?)null,
                cumulativeRegret,
                CalculateThirdChange(oracleRecords),
                cumulativeReward,
                rewardCount > 0
                    ? cumulativeReward / rewardCount
                    : (double?)null,
                stressChange,
                stressArea,
                heartRateChange,
                rmssdChange,
                executedChanges,
                rewardCount,
                result.PolicyState.ModelUpdateCount,
                noChangeCount,
                CalculateEntropy(actionCounts, oracleRecords.Count),
                totalVariation,
                FindFirstRollingImprovement(oracleRecords),
                bestUtility,
                bestTime,
                observationsToBest,
                sceneViolations,
                multiDimensionViolations,
                safetyInterventions,
                invalidModelUpdates,
                result.AllAppliedStatesSafe,
                actionCounts,
                decisionResultCounts,
                rewardResultCounts);
        }

        private static List<PhysiologyWindow> GetAcceptedWindows(
            EvaluationSessionResult result)
        {
            var windows = new List<PhysiologyWindow>();
            for (var index = 0;
                index < result.PhysiologyRecords.Length;
                index++)
            {
                var record = result.PhysiologyRecords[index];
                if (record.Window != null
                    && record.Ingestion.HasValue
                    && record.Ingestion.Value.Accepted)
                {
                    windows.Add(record.Window);
                }
            }

            windows.Sort((left, right) => left.WindowEndUtcUnixSeconds
                .CompareTo(right.WindowEndUtcUnixSeconds));
            return windows;
        }

        private static void CalculatePhysiologyOutcomes(
            List<PhysiologyWindow> windows,
            double acclimatizationEndSeconds,
            out double? stressChange,
            out double? stressArea,
            out double? heartRateChange,
            out double? rmssdChange)
        {
            stressChange = null;
            stressArea = null;
            heartRateChange = null;
            rmssdChange = null;
            if (windows.Count == 0)
            {
                return;
            }

            var firstEnd = windows[0].WindowEndUtcUnixSeconds;
            var baselineStress = 0d;
            var baselineHeartRate = 0d;
            var baselineRmssd = 0d;
            var baselineRmssdCount = 0;
            var baselineCount = 0;
            var area = 0d;
            for (var index = 0; index < windows.Count; index++)
            {
                var window = windows[index];
                if (window.WindowEndUtcUnixSeconds - firstEnd
                    <= acclimatizationEndSeconds - 60d + 1e-8d)
                {
                    baselineStress += window.Stress.ContinuousScore;
                    baselineHeartRate += window.HeartRateBpm;
                    baselineCount++;
                    if (window.RmssdMs.HasValue)
                    {
                        baselineRmssd += window.RmssdMs.Value;
                        baselineRmssdCount++;
                    }
                }

                if (index > 0)
                {
                    var previous = windows[index - 1];
                    var deltaSeconds = window.WindowEndUtcUnixSeconds
                        - previous.WindowEndUtcUnixSeconds;
                    if (deltaSeconds > 0d)
                    {
                        area += deltaSeconds
                            * (previous.Stress.ContinuousScore
                                + window.Stress.ContinuousScore)
                            * 0.5d;
                    }
                }
            }

            stressArea = windows.Count > 1 ? area : (double?)null;
            if (baselineCount == 0)
            {
                return;
            }

            var final = windows[windows.Count - 1];
            stressChange = final.Stress.ContinuousScore
                - (baselineStress / baselineCount);
            heartRateChange = final.HeartRateBpm
                - (baselineHeartRate / baselineCount);
            if (final.RmssdMs.HasValue && baselineRmssdCount > 0)
            {
                rmssdChange = final.RmssdMs.Value
                    - (baselineRmssd / baselineRmssdCount);
            }
        }

        private static double? CalculateThirdChange(
            List<SafeOracleDecisionRecord> records)
        {
            var thirdSize = records.Count / 3;
            if (thirdSize < 1)
            {
                return null;
            }

            var first = 0d;
            var final = 0d;
            for (var index = 0; index < thirdSize; index++)
            {
                first += records[index].PseudoRegret;
                final += records[records.Count - thirdSize + index]
                    .PseudoRegret;
            }

            return (final - first) / thirdSize;
        }

        private static int? FindFirstRollingImprovement(
            List<SafeOracleDecisionRecord> records)
        {
            var width = records.Count / 3;
            if (width < 1 || records.Count <= width)
            {
                return null;
            }

            var firstMean = 0d;
            for (var index = 0; index < width; index++)
            {
                firstMean += records[index].PseudoRegret;
            }

            firstMean /= width;
            for (var end = width; end < records.Count; end++)
            {
                var rolling = 0d;
                for (var index = end - width + 1; index <= end; index++)
                {
                    rolling += records[index].PseudoRegret;
                }

                if ((rolling / width) < firstMean)
                {
                    return end + 1;
                }
            }

            return null;
        }

        private static double CalculateEntropy(int[] counts, int total)
        {
            if (total < 1)
            {
                return 0d;
            }

            var entropy = 0d;
            for (var index = 0; index < counts.Length; index++)
            {
                if (counts[index] == 0)
                {
                    continue;
                }

                var probability = (double)counts[index] / total;
                entropy -= probability * Math.Log(probability);
            }

            return entropy;
        }

        private static void CalculateBestVisited(
            EvaluationDecisionRecord[] decisions,
            out double? bestUtility,
            out double? timeSeconds,
            out int? observations)
        {
            bestUtility = null;
            timeSeconds = null;
            observations = null;
            var eligibleIndex = 0;
            for (var index = 0; index < decisions.Length; index++)
            {
                if (!decisions[index].SafeOracle.HasValue)
                {
                    continue;
                }

                eligibleIndex++;
                var oracle = decisions[index].SafeOracle.Value;
                if (!bestUtility.HasValue)
                {
                    bestUtility = oracle.UtilityBefore;
                    timeSeconds = 0d;
                    observations = 0;
                }

                if (oracle.SelectedTargetUtility > bestUtility.Value)
                {
                    bestUtility = oracle.SelectedTargetUtility;
                    timeSeconds = decisions[index].EventTimeSeconds;
                    observations = eligibleIndex;
                }
            }
        }

        private static int CountChangedDimensions(
            EnvironmentState before,
            EnvironmentState after)
        {
            var count = 0;
            count += before.Illumination != after.Illumination ? 1 : 0;
            count += before.Warmth != after.Warmth ? 1 : 0;
            count += before.AtmosphericSoftness
                != after.AtmosphericSoftness ? 1 : 0;
            count += before.ColorRichness != after.ColorRichness ? 1 : 0;
            count += before.AmbientMotion != after.AmbientMotion ? 1 : 0;
            return count;
        }
    }
}
