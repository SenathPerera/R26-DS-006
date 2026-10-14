using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LaminarVR.AdaptiveMeditation.Environment;
using LaminarVR.AdaptiveMeditation.Physiology;
using LaminarVR.AdaptiveMeditation.Policy;

namespace LaminarVR.AdaptiveMeditation.ResearchEvaluation
{
    public sealed class EvaluationRunProvenance
    {
        public EvaluationRunProvenance(
            string protocolSha256,
            string gitCommit,
            bool gitDirty,
            string unityVersion,
            string applicationVersion,
            string operatingSystem,
            string runtimeVersion,
            DateTimeOffset startedUtc)
        {
            ProtocolSha256 = RequireText(protocolSha256, nameof(protocolSha256));
            GitCommit = RequireText(gitCommit, nameof(gitCommit));
            GitDirty = gitDirty;
            UnityVersion = RequireText(unityVersion, nameof(unityVersion));
            ApplicationVersion = RequireText(
                applicationVersion,
                nameof(applicationVersion));
            OperatingSystem = RequireText(operatingSystem, nameof(operatingSystem));
            RuntimeVersion = RequireText(runtimeVersion, nameof(runtimeVersion));
            StartedUtc = startedUtc;
        }

        public string ProtocolSha256 { get; }
        public string GitCommit { get; }
        public bool GitDirty { get; }
        public string UnityVersion { get; }
        public string ApplicationVersion { get; }
        public string OperatingSystem { get; }
        public string RuntimeVersion { get; }
        public DateTimeOffset StartedUtc { get; }

        private static string RequireText(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException(
                    "A non-empty provenance value is required.",
                    parameterName);
            }

            return value.Trim();
        }
    }

    public sealed class EvaluationArtifactReceipt
    {
        public EvaluationArtifactReceipt(
            string manifestPath,
            string transitionPath,
            string summaryPath,
            string aggregatePath,
            string inferencePath,
            string convergencePath,
            int sessionCount,
            int transitionLineCount,
            IReadOnlyDictionary<string, string> sha256ByPath)
        {
            ManifestPath = manifestPath;
            TransitionPath = transitionPath;
            SummaryPath = summaryPath;
            AggregatePath = aggregatePath;
            InferencePath = inferencePath;
            ConvergencePath = convergencePath;
            SessionCount = sessionCount;
            TransitionLineCount = transitionLineCount;
            Sha256ByPath = sha256ByPath;
        }

        public string ManifestPath { get; }
        public string TransitionPath { get; }
        public string SummaryPath { get; }
        public string AggregatePath { get; }
        public string InferencePath { get; }
        public string ConvergencePath { get; }
        public int SessionCount { get; }
        public int TransitionLineCount { get; }
        public IReadOnlyDictionary<string, string> Sha256ByPath { get; }
    }

    public sealed class EvaluationArtifactWriter
        : IEvaluationSessionSink, IDisposable
    {
        public const string ArtifactSchemaVersion = "1.0.0";

        private static readonly CultureInfo Invariant =
            CultureInfo.InvariantCulture;

        private readonly EvaluationRuntimeBundle bundle;
        private readonly EvaluationRunProvenance provenance;
        private readonly string runId;
        private readonly string evaluationRoot;
        private readonly string manifestPath;
        private readonly string transitionPath;
        private readonly string summaryPath;
        private readonly string aggregatePath;
        private readonly string inferencePath;
        private readonly string convergencePath;
        private readonly StreamWriter transitionWriter;
        private readonly StreamWriter summaryWriter;
        private readonly List<EvaluationSessionSummary> streamedSummaries =
            new List<EvaluationSessionSummary>();
        private int sessionCount;
        private int transitionLineCount;
        private bool finalized;

        public EvaluationArtifactWriter(
            string evaluationRoot,
            string runId,
            EvaluationRuntimeBundle bundle,
            EvaluationRunProvenance provenance)
        {
            if (string.IsNullOrWhiteSpace(evaluationRoot))
            {
                throw new ArgumentException(
                    "An evaluation output root is required.",
                    nameof(evaluationRoot));
            }

            this.runId = RequireRunId(runId);
            this.bundle = bundle ?? throw new ArgumentNullException(nameof(bundle));
            this.provenance = provenance
                ?? throw new ArgumentNullException(nameof(provenance));

            var root = Path.GetFullPath(evaluationRoot);
            this.evaluationRoot = root;
            manifestPath = PreparePath(root, "manifests", runId + ".json");
            transitionPath = PreparePath(
                root,
                Path.Combine("results", "transitions"),
                runId + ".jsonl");
            summaryPath = PreparePath(
                root,
                Path.Combine("results", "session-summaries"),
                runId + ".csv");
            aggregatePath = PreparePath(
                root,
                Path.Combine("results", "aggregate-tables"),
                runId + "-aggregates.csv");
            inferencePath = PreparePath(
                root,
                Path.Combine("results", "aggregate-tables"),
                runId + "-inference.csv");
            convergencePath = PreparePath(
                root,
                Path.Combine("results", "aggregate-tables"),
                runId + "-convergence.csv");
            EnsureNewFile(manifestPath);
            EnsureNewFile(transitionPath);
            EnsureNewFile(summaryPath);
            EnsureNewFile(aggregatePath);
            EnsureNewFile(inferencePath);
            EnsureNewFile(convergencePath);

            var encoding = new UTF8Encoding(false);
            transitionWriter = new StreamWriter(transitionPath, false, encoding);
            summaryWriter = new StreamWriter(summaryPath, false, encoding);
            summaryWriter.WriteLine(BuildSummaryHeader());
        }

        public Task OnSessionCompletedAsync(
            EvaluationCellResult cell,
            CancellationToken cancellationToken)
        {
            if (cell == null)
            {
                throw new ArgumentNullException(nameof(cell));
            }

            EnsureOpen();
            cancellationToken.ThrowIfCancellationRequested();
            WriteTransitions(cell);
            summaryWriter.WriteLine(BuildSummaryRow(cell.Summary));
            transitionWriter.Flush();
            summaryWriter.Flush();
            streamedSummaries.Add(cell.Summary);
            sessionCount++;
            return Task.CompletedTask;
        }

        public EvaluationArtifactReceipt Complete(
            EvaluationStudyResult studyResult,
            DateTimeOffset completedUtc)
        {
            if (studyResult == null)
            {
                throw new ArgumentNullException(nameof(studyResult));
            }

            EnsureOpen();
            if (studyResult.Sessions.Length != sessionCount)
            {
                throw new InvalidOperationException(
                    "The study result count does not match streamed sessions.");
            }

            return FinalizeArtifacts(
                studyResult,
                "completed",
                null,
                completedUtc);
        }

        public EvaluationArtifactReceipt Abort(
            EvaluationStudyId study,
            string status,
            string failureReason,
            DateTimeOffset completedUtc)
        {
            EnsureOpen();
            if (status != "cancelled" && status != "failed")
            {
                throw new ArgumentException(
                    "An aborted run status must be 'cancelled' or 'failed'.",
                    nameof(status));
            }

            if (string.IsNullOrWhiteSpace(failureReason))
            {
                throw new ArgumentException(
                    "An aborted run requires a reason.",
                    nameof(failureReason));
            }

            return FinalizeArtifacts(
                new EvaluationStudyResult(
                    study,
                    streamedSummaries.ToArray()),
                status,
                failureReason.Trim(),
                completedUtc);
        }

        private EvaluationArtifactReceipt FinalizeArtifacts(
            EvaluationStudyResult studyResult,
            string status,
            string failureReason,
            DateTimeOffset completedUtc)
        {
            transitionWriter.Dispose();
            summaryWriter.Dispose();
            WriteAggregateCsv(studyResult.Sessions);
            var inferenceComplete = WriteInferenceCsv(studyResult);
            var convergenceComplete = WriteConvergenceCsv(studyResult);

            var checksums = new Dictionary<string, string>(
                StringComparer.Ordinal)
            {
                [RelativeArtifactName(transitionPath)] = ComputeSha256(transitionPath),
                [RelativeArtifactName(summaryPath)] = ComputeSha256(summaryPath),
                [RelativeArtifactName(aggregatePath)] = ComputeSha256(aggregatePath),
                [RelativeArtifactName(inferencePath)] = ComputeSha256(inferencePath),
                [RelativeArtifactName(convergencePath)] = ComputeSha256(convergencePath)
            };
            File.WriteAllText(
                manifestPath,
                BuildManifestJson(
                    studyResult,
                    status,
                    failureReason,
                    completedUtc,
                    inferenceComplete,
                    convergenceComplete,
                    checksums),
                new UTF8Encoding(false));
            finalized = true;
            return new EvaluationArtifactReceipt(
                manifestPath,
                transitionPath,
                summaryPath,
                aggregatePath,
                inferencePath,
                convergencePath,
                sessionCount,
                transitionLineCount,
                checksums);
        }

        public void Dispose()
        {
            if (finalized)
            {
                return;
            }

            transitionWriter.Dispose();
            summaryWriter.Dispose();
            finalized = true;
        }

        public static string ComputeSha256(string path)
        {
            using (var stream = File.OpenRead(path))
            using (var sha = SHA256.Create())
            {
                var hash = sha.ComputeHash(stream);
                var builder = new StringBuilder(hash.Length * 2);
                for (var index = 0; index < hash.Length; index++)
                {
                    builder.Append(hash[index].ToString("x2", Invariant));
                }

                return builder.ToString();
            }
        }

        private void WriteTransitions(EvaluationCellResult cell)
        {
            var summary = cell.Summary;
            var session = cell.Session;
            for (var index = 0; index < session.PhysiologyRecords.Length; index++)
            {
                WriteJsonLine(BuildPhysiologyJson(
                    summary,
                    session.PhysiologyRecords[index]));
            }

            for (var index = 0; index < session.Decisions.Length; index++)
            {
                WriteJsonLine(BuildDecisionJson(
                    summary,
                    session.Decisions[index]));
            }

            for (var index = 0; index < session.Rewards.Length; index++)
            {
                WriteJsonLine(BuildRewardJson(
                    summary,
                    session.Rewards[index]));
            }
        }

        private void WriteJsonLine(string json)
        {
            transitionWriter.WriteLine(json);
            transitionLineCount++;
        }

        private static string BuildPhysiologyJson(
            EvaluationSessionSummary summary,
            EvaluationPhysiologyRecord record)
        {
            var builder = StartEvent(summary, "physiology");
            AddNumber(builder, "emission_sequence", record.EmissionSequence);
            AddNumber(builder, "event_time_seconds", record.EventTimeSeconds);
            AddBoolean(builder, "dropped", record.Dropped);
            if (record.Ingestion.HasValue)
            {
                var ingestion = record.Ingestion.Value;
                AddString(builder, "ingestion_result", ingestion.ResultCode.ToString());
                AddString(
                    builder,
                    "validation_reason",
                    ingestion.ValidationReasonCode.ToString());
                AddNumber(
                    builder,
                    "accepted_sequence_number",
                    ingestion.AcceptedSequenceNumber);
            }
            else
            {
                AddNull(builder, "ingestion_result");
                AddNull(builder, "validation_reason");
                AddNull(builder, "accepted_sequence_number");
            }

            var window = record.Window;
            if (window == null)
            {
                AddNull(builder, "window");
            }
            else
            {
                AddRaw(builder, "window", BuildWindowJson(window));
            }

            return EndObject(builder);
        }

        private static string BuildDecisionJson(
            EvaluationSessionSummary summary,
            EvaluationDecisionRecord record)
        {
            var builder = StartEvent(summary, "decision");
            AddNumber(builder, "opportunity_sequence", record.OpportunitySequence);
            AddNumber(builder, "event_time_seconds", record.EventTimeSeconds);
            AddString(builder, "result_code", record.Result.ResultCode.ToString());
            AddNullableString(
                builder,
                "physiology_query_result",
                record.Result.PhysiologyQueryResult.HasValue
                    ? record.Result.PhysiologyQueryResult.Value.ToString()
                    : null);
            var decision = record.Result.Decision;
            if (decision == null)
            {
                AddNull(builder, "policy_decision");
            }
            else
            {
                var decisionJson = new StringBuilder("{");
                AddString(decisionJson, "policy_id", decision.PolicyId);
                AddString(decisionJson, "policy_version", decision.PolicyVersion);
                AddString(
                    decisionJson,
                    "selected_action",
                    decision.SelectedAction.ToString());
                AddNumber(
                    decisionJson,
                    "physiology_sequence_number",
                    decision.PhysiologySequenceNumber);
                AddString(decisionJson, "reason_code", decision.ReasonCode);
                AddNullableNumber(
                    decisionJson,
                    "expected_reward",
                    decision.ExpectedReward);
                AddNullableNumber(
                    decisionJson,
                    "uncertainty",
                    decision.Uncertainty);
                AddBoolean(
                    decisionJson,
                    "exploration_used",
                    decision.ExplorationUsed);
                AddRaw(
                    decisionJson,
                    "candidate_scores",
                    BuildCandidateScoresJson(decision));
                AddRaw(
                    builder,
                    "policy_decision",
                    EndObject(decisionJson));
            }

            if (record.Result.Validation.HasValue)
            {
                AddRaw(
                    builder,
                    "safety_validation",
                    BuildValidationJson(record.Result.Validation.Value));
            }
            else
            {
                AddNull(builder, "safety_validation");
            }

            if (record.SafeOracle.HasValue)
            {
                AddRaw(
                    builder,
                    "safe_oracle",
                    BuildOracleJson(record.SafeOracle.Value));
            }
            else
            {
                AddNull(builder, "safe_oracle");
            }

            return EndObject(builder);
        }

        private static string BuildRewardJson(
            EvaluationSessionSummary summary,
            EvaluationRewardRecord record)
        {
            var builder = StartEvent(summary, "reward");
            AddNumber(builder, "event_time_seconds", record.EventTimeSeconds);
            AddString(builder, "result_code", record.Result.ResultCode.ToString());
            AddString(
                builder,
                "attribution_code",
                record.Result.AttributionCode.ToString());
            AddString(
                builder,
                "calculation_code",
                record.Result.Calculation.ResultCode.ToString());
            if (record.Result.Calculation.Valid)
            {
                var breakdown = record.Result.Calculation.Breakdown;
                var detail = new StringBuilder("{");
                AddString(
                    detail,
                    "executed_action",
                    breakdown.ExecutedAction.ToString());
                AddNumber(
                    detail,
                    "pre_window_sequence_number",
                    breakdown.PreWindowSequenceNumber);
                AddNumber(
                    detail,
                    "post_window_sequence_number",
                    breakdown.PostWindowSequenceNumber);
                AddNumber(detail, "stress_component", breakdown.StressComponent);
                AddNumber(detail, "rmssd_component", breakdown.RmssdComponent);
                AddNumber(
                    detail,
                    "heart_rate_component",
                    breakdown.HeartRateComponent);
                AddNumber(
                    detail,
                    "change_penalty_component",
                    breakdown.ChangePenaltyComponent);
                AddNumber(
                    detail,
                    "discomfort_penalty_component",
                    breakdown.DiscomfortPenaltyComponent);
                AddNumber(
                    detail,
                    "safety_penalty_component",
                    breakdown.SafetyPenaltyComponent);
                AddNumber(detail, "total_reward", breakdown.TotalReward);
                AddRaw(builder, "breakdown", EndObject(detail));
            }
            else
            {
                AddNull(builder, "breakdown");
            }

            return EndObject(builder);
        }

        private static StringBuilder StartEvent(
            EvaluationSessionSummary summary,
            string eventType)
        {
            var builder = new StringBuilder("{");
            AddString(builder, "schema_version", ArtifactSchemaVersion);
            AddString(builder, "protocol_id", EvaluationRuntimeBundle.ProtocolId);
            AddString(builder, "event_type", eventType);
            AddString(builder, "experiment_id", summary.ExperimentId);
            AddString(builder, "archetype", summary.Archetype.ToString());
            AddString(builder, "scenario", summary.ScenarioCode);
            AddString(builder, "policy", summary.PolicyCondition.ToString());
            AddNumber(builder, "seed", summary.Seed);
            return builder;
        }

        private static string BuildWindowJson(PhysiologyWindow window)
        {
            var builder = new StringBuilder("{");
            AddNumber(
                builder,
                "source_timestamp_utc_unix_seconds",
                window.SourceTimestampUtcUnixSeconds);
            AddNumber(
                builder,
                "window_start_utc_unix_seconds",
                window.WindowStartUtcUnixSeconds);
            AddNumber(
                builder,
                "window_end_utc_unix_seconds",
                window.WindowEndUtcUnixSeconds);
            AddNumber(builder, "heart_rate_bpm", window.HeartRateBpm);
            AddNullableNumber(builder, "rmssd_ms", window.RmssdMs);
            AddNullableNumber(builder, "sdnn_ms", window.SdnnMs);
            AddNumber(builder, "signal_quality", window.SignalQuality);
            AddString(builder, "stress_label", window.Stress.Label);
            AddNumber(
                builder,
                "continuous_stress_score",
                window.Stress.ContinuousScore);
            return EndObject(builder);
        }

        private static string BuildCandidateScoresJson(PolicyDecision decision)
        {
            var builder = new StringBuilder("[");
            for (var index = 0; index < decision.CandidateScoreCount; index++)
            {
                if (index > 0)
                {
                    builder.Append(',');
                }

                var score = decision.GetCandidateScore(index);
                var item = new StringBuilder("{");
                AddString(item, "action", score.Action.ToString());
                AddNumber(item, "score", score.Score);
                AddNumber(item, "uncertainty", score.Uncertainty);
                AddNullableNumber(item, "expected_reward", score.ExpectedReward);
                AddNullableNumber(
                    item,
                    "exploration_bonus",
                    score.ExplorationBonus);
                builder.Append(EndObject(item));
            }

            return builder.Append(']').ToString();
        }

        private static string BuildValidationJson(
            Safety.ActionValidationResult validation)
        {
            var builder = new StringBuilder("{");
            AddString(
                builder,
                "proposed_action",
                validation.ProposedAction.ToString());
            AddString(
                builder,
                "executed_action",
                validation.ExecutedAction.ToString());
            AddBoolean(builder, "accepted", validation.Accepted);
            AddBoolean(builder, "modified", validation.Modified);
            AddString(builder, "reason_code", validation.ReasonCode.ToString());
            AddNumber(builder, "applied_variation", validation.AppliedVariation);
            AddRaw(
                builder,
                "requested_target",
                BuildEnvironmentJson(validation.RequestedTarget));
            AddRaw(
                builder,
                "safe_target",
                BuildEnvironmentJson(validation.SafeTarget));
            return EndObject(builder);
        }

        private static string BuildOracleJson(SafeOracleDecisionRecord oracle)
        {
            var builder = new StringBuilder("{");
            AddString(builder, "proposed_action", oracle.ProposedAction.ToString());
            AddString(builder, "evaluated_action", oracle.EvaluatedAction.ToString());
            AddString(builder, "oracle_action", oracle.OracleAction.ToString());
            AddNumber(
                builder,
                "selected_expected_stress_reduction",
                oracle.SelectedExpectedStressReduction);
            AddNumber(
                builder,
                "oracle_expected_stress_reduction",
                oracle.OracleExpectedStressReduction);
            AddNumber(builder, "pseudo_regret", oracle.PseudoRegret);
            AddNumber(builder, "utility_before", oracle.UtilityBefore);
            AddNumber(
                builder,
                "selected_target_utility",
                oracle.SelectedTargetUtility);
            AddNumber(builder, "safe_candidate_count", oracle.SafeCandidateCount);
            AddRaw(
                builder,
                "environment_before",
                BuildEnvironmentJson(oracle.EnvironmentBefore));
            AddRaw(
                builder,
                "selected_target",
                BuildEnvironmentJson(oracle.SelectedTarget));
            return EndObject(builder);
        }

        private static string BuildEnvironmentJson(EnvironmentState state)
        {
            var builder = new StringBuilder("{");
            AddNumber(builder, "illumination", state.Illumination);
            AddNumber(builder, "warmth", state.Warmth);
            AddNumber(
                builder,
                "atmospheric_softness",
                state.AtmosphericSoftness);
            AddNumber(builder, "color_richness", state.ColorRichness);
            AddNumber(builder, "ambient_motion", state.AmbientMotion);
            return EndObject(builder);
        }

        private static string BuildSummaryHeader()
        {
            var fields = new List<string>
            {
                "schema_version", "protocol_id", "experiment_id", "archetype",
                "scenario", "policy", "seed", "baseline_available",
                "eligible_decision_count", "mean_pseudo_regret",
                "cumulative_pseudo_regret", "first_to_final_third_regret_change",
                "cumulative_production_reward", "mean_production_reward",
                "final_minus_baseline_stress_score", "stress_area_under_curve",
                "final_minus_baseline_heart_rate_bpm",
                "final_minus_baseline_rmssd_ms", "executed_change_count",
                "valid_outcome_count", "model_update_count", "no_change_count",
                "action_entropy_nats", "total_normalized_variation",
                "first_opportunity_below_first_third_mean", "best_visited_utility",
                "time_to_best_visited_state_seconds",
                "observations_to_best_visited_state", "scene_limit_violation_count",
                "multi_dimension_violation_count",
                "safety_rejected_or_modified_count", "invalid_model_update_count",
                "all_applied_states_safe"
            };
            foreach (EnvironmentAction action in Enum.GetValues(
                typeof(EnvironmentAction)))
            {
                fields.Add("action_count_" + action);
            }

            foreach (PolicyDecisionCycleResultCode code in Enum.GetValues(
                typeof(PolicyDecisionCycleResultCode)))
            {
                fields.Add("decision_result_count_" + code);
            }

            foreach (PolicyRewardCycleResultCode code in Enum.GetValues(
                typeof(PolicyRewardCycleResultCode)))
            {
                fields.Add("reward_result_count_" + code);
            }

            return string.Join(",", fields);
        }

        private static string BuildSummaryRow(EvaluationSessionSummary summary)
        {
            var fields = new List<string>
            {
                ArtifactSchemaVersion,
                EvaluationRuntimeBundle.ProtocolId,
                Csv(summary.ExperimentId),
                summary.Archetype.ToString(),
                Csv(summary.ScenarioCode),
                summary.PolicyCondition.ToString(),
                Format(summary.Seed),
                Format(summary.BaselineAvailable),
                Format(summary.EligibleDecisionCount),
                Format(summary.MeanPseudoRegret),
                Format(summary.CumulativePseudoRegret),
                Format(summary.FirstToFinalThirdRegretChange),
                Format(summary.CumulativeProductionReward),
                Format(summary.MeanProductionReward),
                Format(summary.FinalMinusBaselineStressScore),
                Format(summary.StressAreaUnderCurve),
                Format(summary.FinalMinusBaselineHeartRateBpm),
                Format(summary.FinalMinusBaselineRmssdMs),
                Format(summary.ExecutedChangeCount),
                Format(summary.ValidOutcomeCount),
                Format(summary.ModelUpdateCount),
                Format(summary.NoChangeCount),
                Format(summary.ActionEntropyNats),
                Format(summary.TotalNormalizedVariation),
                Format(summary.FirstOpportunityBelowFirstThirdMean),
                Format(summary.BestVisitedUtility),
                Format(summary.TimeToBestVisitedStateSeconds),
                Format(summary.ObservationsToBestVisitedState),
                Format(summary.SceneLimitViolationCount),
                Format(summary.MultiDimensionViolationCount),
                Format(summary.SafetyRejectedOrModifiedCount),
                Format(summary.InvalidModelUpdateCount),
                Format(summary.AllAppliedStatesSafe)
            };
            foreach (EnvironmentAction action in Enum.GetValues(
                typeof(EnvironmentAction)))
            {
                fields.Add(Format(summary.GetSelectedActionCount(action)));
            }

            foreach (PolicyDecisionCycleResultCode code in Enum.GetValues(
                typeof(PolicyDecisionCycleResultCode)))
            {
                fields.Add(Format(summary.GetDecisionResultCount(code)));
            }

            foreach (PolicyRewardCycleResultCode code in Enum.GetValues(
                typeof(PolicyRewardCycleResultCode)))
            {
                fields.Add(Format(summary.GetRewardResultCount(code)));
            }

            return string.Join(",", fields);
        }

        private void WriteAggregateCsv(EvaluationSessionSummary[] sessions)
        {
            var rows = EvaluationAggregateBuilder.Build(sessions);
            using (var writer = new StreamWriter(
                aggregatePath,
                false,
                new UTF8Encoding(false)))
            {
                writer.WriteLine(
                    "schema_version,protocol_id,experiment_id,archetype,scenario,"
                    + "policy,attempted_sessions,metric_available_sessions,mean,"
                    + "standard_deviation,median,first_quartile,third_quartile,"
                    + "interquartile_range,scene_limit_violations,"
                    + "multi_dimension_violations,invalid_model_updates");
                for (var index = 0; index < rows.Length; index++)
                {
                    var row = rows[index];
                    var statistics = row.MeanRegretStatistics;
                    writer.WriteLine(string.Join(",", new[]
                    {
                        ArtifactSchemaVersion,
                        EvaluationRuntimeBundle.ProtocolId,
                        Csv(row.Key.ExperimentId),
                        row.Key.Archetype.ToString(),
                        Csv(row.Key.ScenarioCode),
                        row.Key.Policy.ToString(),
                        Format(row.AttemptedSessions),
                        Format(row.MetricAvailableSessions),
                        statistics.HasValue ? Format(statistics.Value.Mean) : string.Empty,
                        statistics.HasValue
                            ? Format(statistics.Value.StandardDeviation)
                            : string.Empty,
                        statistics.HasValue ? Format(statistics.Value.Median) : string.Empty,
                        statistics.HasValue
                            ? Format(statistics.Value.FirstQuartile)
                            : string.Empty,
                        statistics.HasValue
                            ? Format(statistics.Value.ThirdQuartile)
                            : string.Empty,
                        statistics.HasValue
                            ? Format(statistics.Value.InterquartileRange)
                            : string.Empty,
                        Format(row.SceneLimitViolations),
                        Format(row.MultiDimensionViolations),
                        Format(row.InvalidModelUpdates)
                    }));
                }
            }
        }

        private bool WriteInferenceCsv(EvaluationStudyResult studyResult)
        {
            using (var writer = new StreamWriter(
                inferencePath,
                false,
                new UTF8Encoding(false)))
            {
                writer.WriteLine(
                    "schema_version,protocol_id,hypothesis,pair_count,"
                    + "mean_difference,median_difference,ci95_lower,ci95_upper,"
                    + "probability_left_lower,permutation_resamples,"
                    + "extreme_resamples,two_sided_p_value,complete");
                if (studyResult.Study
                    != EvaluationStudyId.StudyAPrimaryPolicyComparison)
                {
                    return false;
                }

                var h3 = EvaluationAggregateBuilder.BuildH3PrimarySamples(
                    studyResult.Sessions);
                var h4 = EvaluationAggregateBuilder
                    .BuildH4NonStationaritySamples(studyResult.Sessions);
                var h3Complete = WriteInferenceRow(writer, "H3", h3, 200);
                var h4Complete = WriteInferenceRow(writer, "H4", h4, 200);
                return h3Complete && h4Complete;
            }
        }

        private bool WriteConvergenceCsv(EvaluationStudyResult studyResult)
        {
            using (var writer = new StreamWriter(
                convergencePath,
                false,
                new UTF8Encoding(false)))
            {
                writer.WriteLine(
                    "schema_version,protocol_id,contrast,cumulative_replications,"
                    + "mean_difference,median_difference,ci95_lower,ci95_upper,"
                    + "probability_left_lower,complete");
                if (studyResult.Study
                    != EvaluationStudyId.StudyAPrimaryPolicyComparison)
                {
                    return false;
                }

                var samples = EvaluationAggregateBuilder.BuildH3PrimarySamples(
                    studyResult.Sessions);
                var requestedCounts = new[] { 25, 50, 100, 200 };
                var allComplete = true;
                for (var index = 0; index < requestedCounts.Length; index++)
                {
                    var count = requestedCounts[index];
                    if (samples.Count < count)
                    {
                        allComplete = false;
                        continue;
                    }

                    var left = new double[count];
                    var right = new double[count];
                    Array.Copy(samples.Left, left, count);
                    Array.Copy(samples.Right, right, count);
                    var bootstrap = EvaluationStatistics.PairedMeanDifference(
                        left,
                        right,
                        EvaluationStatistics.ProtocolBootstrapResamples,
                        EvaluationStatistics.ProtocolBootstrapSeed);
                    writer.WriteLine(string.Join(",", new[]
                    {
                        ArtifactSchemaVersion,
                        EvaluationRuntimeBundle.ProtocolId,
                        "H3",
                        Format(count),
                        Format(bootstrap.MeanDifference),
                        Format(bootstrap.MedianDifference),
                        Format(bootstrap.Lower95),
                        Format(bootstrap.Upper95),
                        Format(bootstrap.ProbabilityLeftLower),
                        "true"
                    }));
                }

                return allComplete;
            }
        }

        private static bool WriteInferenceRow(
            TextWriter writer,
            string hypothesis,
            EvaluationPairedSamples samples,
            int expectedPairCount)
        {
            if (samples.Count < 2)
            {
                return false;
            }

            var bootstrap = EvaluationStatistics.PairedMeanDifference(
                samples.Left,
                samples.Right);
            var permutation = EvaluationStatistics
                .PairedSignFlipMeanDifference(samples.Left, samples.Right);
            var complete = samples.Count == expectedPairCount;
            writer.WriteLine(string.Join(",", new[]
            {
                ArtifactSchemaVersion,
                EvaluationRuntimeBundle.ProtocolId,
                hypothesis,
                Format(samples.Count),
                Format(bootstrap.MeanDifference),
                Format(bootstrap.MedianDifference),
                Format(bootstrap.Lower95),
                Format(bootstrap.Upper95),
                Format(bootstrap.ProbabilityLeftLower),
                Format(permutation.ResampleCount),
                Format(permutation.ExtremeResampleCount),
                Format(permutation.TwoSidedPValue),
                Format(complete)
            }));
            return complete;
        }

        private string BuildManifestJson(
            EvaluationStudyResult studyResult,
            string status,
            string failureReason,
            DateTimeOffset completedUtc,
            bool inferenceComplete,
            bool convergenceComplete,
            IReadOnlyDictionary<string, string> checksums)
        {
            var builder = new StringBuilder("{");
            AddString(builder, "schema_version", ArtifactSchemaVersion);
            AddString(builder, "protocol_id", EvaluationRuntimeBundle.ProtocolId);
            AddString(builder, "protocol_sha256", provenance.ProtocolSha256);
            AddString(builder, "run_id", runId);
            AddString(builder, "study", studyResult.Study.ToString());
            AddRaw(
                builder,
                "experiment_matrix",
                BuildExperimentMatrixJson(studyResult.Sessions));
            AddString(builder, "status", status);
            AddNullableString(builder, "failure_reason", failureReason);
            AddString(builder, "git_commit", provenance.GitCommit);
            AddBoolean(builder, "git_dirty", provenance.GitDirty);
            AddString(builder, "unity_version", provenance.UnityVersion);
            AddString(
                builder,
                "application_version",
                provenance.ApplicationVersion);
            AddString(builder, "feature_schema", bundle.LinUcb.FeatureSchemaVersion);
            AddString(
                builder,
                "telemetry_schema",
                bundle.Telemetry.EventSchemaId
                    + "/"
                    + bundle.Telemetry.EventSchemaVersion);
            AddString(builder, "profile_bundle_id", EvaluationRuntimeBundle.PilotBundleId);
            AddString(builder, "scene_id", bundle.Scene.SceneId);
            AddString(builder, "timing_configuration", Identity(bundle.Timing.ConfigurationId, bundle.Timing.ConfigurationVersion));
            AddString(builder, "physiology_configuration", Identity(bundle.Physiology.ConfigurationId, bundle.Physiology.ConfigurationVersion));
            AddString(builder, "reward_configuration", Identity(bundle.Reward.ConfigurationId, bundle.Reward.ConfigurationVersion));
            AddString(builder, "coordinator_configuration", Identity(bundle.Coordinator.ConfigurationId, bundle.Coordinator.ConfigurationVersion));
            AddString(builder, "preference_configuration", Identity(bundle.Preference.ConfigurationId, bundle.Preference.ConfigurationVersion));
            AddString(builder, "rule_based_configuration", Identity(bundle.RuleBased.ConfigurationId, bundle.RuleBased.ConfigurationVersion));
            AddString(builder, "linucb_configuration", Identity(bundle.LinUcb.ConfigurationId, bundle.LinUcb.ConfigurationVersion));
            AddString(builder, "stabilization_configuration", Identity(bundle.Stabilization.ConfigurationId, bundle.Stabilization.ConfigurationVersion));
            AddString(builder, "telemetry_configuration", Identity(bundle.Telemetry.ConfigurationId, bundle.Telemetry.ConfigurationVersion));
            AddString(builder, "prng", "pcg-xsh-rr-32/v1");
            AddString(builder, "random_stream_registry", "component-c-evaluation-streams/v1");
            AddNumber(builder, "bootstrap_seed", EvaluationStatistics.ProtocolBootstrapSeed);
            AddNumber(builder, "bootstrap_resamples", EvaluationStatistics.ProtocolBootstrapResamples);
            AddNumber(builder, "permutation_seed", EvaluationStatistics.ProtocolPermutationSeed);
            AddNumber(builder, "permutation_resamples", EvaluationStatistics.ProtocolPermutationResamples);
            AddString(builder, "started_utc", provenance.StartedUtc.ToString("O", Invariant));
            AddString(builder, "completed_utc", completedUtc.ToString("O", Invariant));
            AddNumber(builder, "session_count", sessionCount);
            AddNumber(builder, "transition_line_count", transitionLineCount);
            AddBoolean(builder, "confirmatory_inference_complete", inferenceComplete);
            AddBoolean(
                builder,
                "convergence_diagnostic_complete",
                convergenceComplete);
            AddString(builder, "operating_system", provenance.OperatingSystem);
            AddString(builder, "runtime_version", provenance.RuntimeVersion);
            var checksumJson = new StringBuilder("{");
            foreach (var pair in checksums)
            {
                AddString(checksumJson, pair.Key, pair.Value);
            }

            AddRaw(builder, "output_sha256", EndObject(checksumJson));
            return EndObject(builder) + System.Environment.NewLine;
        }

        private static string BuildExperimentMatrixJson(
            EvaluationSessionSummary[] sessions)
        {
            var experiments = new SortedSet<string>(StringComparer.Ordinal);
            var archetypes = new SortedSet<string>(StringComparer.Ordinal);
            var scenarios = new SortedSet<string>(StringComparer.Ordinal);
            var policies = new SortedSet<string>(StringComparer.Ordinal);
            var minimumSeed = int.MaxValue;
            var maximumSeed = int.MinValue;
            for (var index = 0; index < sessions.Length; index++)
            {
                var session = sessions[index];
                experiments.Add(session.ExperimentId);
                archetypes.Add(session.Archetype.ToString());
                scenarios.Add(session.ScenarioCode);
                policies.Add(session.PolicyCondition.ToString());
                minimumSeed = Math.Min(minimumSeed, session.Seed);
                maximumSeed = Math.Max(maximumSeed, session.Seed);
            }

            var builder = new StringBuilder("{");
            AddRaw(builder, "experiments", BuildStringArray(experiments));
            AddRaw(builder, "archetypes", BuildStringArray(archetypes));
            AddRaw(builder, "scenarios", BuildStringArray(scenarios));
            AddRaw(builder, "policies", BuildStringArray(policies));
            if (sessions.Length > 0)
            {
                AddNumber(builder, "minimum_seed", minimumSeed);
                AddNumber(builder, "maximum_seed", maximumSeed);
            }
            else
            {
                AddNull(builder, "minimum_seed");
                AddNull(builder, "maximum_seed");
            }

            return EndObject(builder);
        }

        private static string BuildStringArray(IEnumerable<string> values)
        {
            var builder = new StringBuilder("[");
            var first = true;
            foreach (var value in values)
            {
                if (!first)
                {
                    builder.Append(',');
                }

                builder.Append('"').Append(JsonEscape(value)).Append('"');
                first = false;
            }

            return builder.Append(']').ToString();
        }

        private static string Identity(string id, int version)
        {
            return id + "/" + version.ToString(Invariant);
        }

        private static string PreparePath(
            string root,
            string relativeDirectory,
            string fileName)
        {
            var directory = Path.Combine(root, relativeDirectory);
            Directory.CreateDirectory(directory);
            return Path.Combine(directory, fileName);
        }

        private static void EnsureNewFile(string path)
        {
            if (File.Exists(path))
            {
                throw new IOException(
                    "Evaluation artifacts are append-protected: " + path);
            }
        }

        private static string RequireRunId(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("A run ID is required.", nameof(value));
            }

            var trimmed = value.Trim();
            for (var index = 0; index < trimmed.Length; index++)
            {
                var character = trimmed[index];
                if (!char.IsLetterOrDigit(character)
                    && character != '-'
                    && character != '_'
                    && character != '.')
                {
                    throw new ArgumentException(
                        "Run IDs may contain only letters, digits, '.', '-', and '_'.",
                        nameof(value));
                }
            }

            return trimmed;
        }

        private void EnsureOpen()
        {
            if (finalized)
            {
                throw new ObjectDisposedException(nameof(EvaluationArtifactWriter));
            }
        }

        private string RelativeArtifactName(string path)
        {
            var prefix = evaluationRoot.TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "Artifact path is outside the evaluation root.");
            }

            return path.Substring(prefix.Length).Replace('\\', '/');
        }

        private static string Csv(string value)
        {
            if (value == null)
            {
                return string.Empty;
            }

            if (value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) < 0)
            {
                return value;
            }

            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        private static string Format(bool value)
        {
            return value ? "true" : "false";
        }

        private static string Format(int value)
        {
            return value.ToString(Invariant);
        }

        private static string Format(long value)
        {
            return value.ToString(Invariant);
        }

        private static string Format(double value)
        {
            return value.ToString("R", Invariant);
        }

        private static string Format(double? value)
        {
            return value.HasValue ? Format(value.Value) : string.Empty;
        }

        private static string Format(int? value)
        {
            return value.HasValue ? Format(value.Value) : string.Empty;
        }

        private static void AddString(
            StringBuilder builder,
            string name,
            string value)
        {
            AddPropertyPrefix(builder, name);
            builder.Append('"').Append(JsonEscape(value)).Append('"');
        }

        private static void AddNullableString(
            StringBuilder builder,
            string name,
            string value)
        {
            if (value == null)
            {
                AddNull(builder, name);
            }
            else
            {
                AddString(builder, name, value);
            }
        }

        private static void AddNumber(
            StringBuilder builder,
            string name,
            double value)
        {
            AddPropertyPrefix(builder, name);
            builder.Append(Format(value));
        }

        private static void AddNumber(
            StringBuilder builder,
            string name,
            long value)
        {
            AddPropertyPrefix(builder, name);
            builder.Append(Format(value));
        }

        private static void AddNullableNumber(
            StringBuilder builder,
            string name,
            double? value)
        {
            if (value.HasValue)
            {
                AddNumber(builder, name, value.Value);
            }
            else
            {
                AddNull(builder, name);
            }
        }

        private static void AddBoolean(
            StringBuilder builder,
            string name,
            bool value)
        {
            AddPropertyPrefix(builder, name);
            builder.Append(value ? "true" : "false");
        }

        private static void AddNull(StringBuilder builder, string name)
        {
            AddPropertyPrefix(builder, name);
            builder.Append("null");
        }

        private static void AddRaw(
            StringBuilder builder,
            string name,
            string json)
        {
            AddPropertyPrefix(builder, name);
            builder.Append(json);
        }

        private static void AddPropertyPrefix(
            StringBuilder builder,
            string name)
        {
            if (builder.Length > 1)
            {
                builder.Append(',');
            }

            builder.Append('"').Append(JsonEscape(name)).Append("\":");
        }

        private static string EndObject(StringBuilder builder)
        {
            return builder.Append('}').ToString();
        }

        private static string JsonEscape(string value)
        {
            if (value == null)
            {
                return string.Empty;
            }

            var builder = new StringBuilder(value.Length + 8);
            for (var index = 0; index < value.Length; index++)
            {
                var character = value[index];
                switch (character)
                {
                    case '\\':
                        builder.Append("\\\\");
                        break;
                    case '"':
                        builder.Append("\\\"");
                        break;
                    case '\n':
                        builder.Append("\\n");
                        break;
                    case '\r':
                        builder.Append("\\r");
                        break;
                    case '\t':
                        builder.Append("\\t");
                        break;
                    default:
                        if (character < 0x20)
                        {
                            builder.Append("\\u")
                                .Append(((int)character).ToString("x4", Invariant));
                        }
                        else
                        {
                            builder.Append(character);
                        }

                        break;
                }
            }

            return builder.ToString();
        }
    }
}
