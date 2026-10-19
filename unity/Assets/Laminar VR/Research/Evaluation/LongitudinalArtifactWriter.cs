using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using LaminarVR.AdaptiveMeditation.Policy;

namespace LaminarVR.AdaptiveMeditation.ResearchEvaluation
{
    public sealed class LongitudinalArtifactWriter : ILongitudinalResultSink, IDisposable
    {
        private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
        private readonly string repositoryRoot;
        private readonly string runId;
        private readonly string unityVersion;
        private readonly string gitCommit;
        private readonly bool gitDirty;
        private readonly string linUcbConfigurationId;
        private readonly string featureSchemaVersion;
        private readonly string mappingConfigurationId;
        private readonly int replications;
        private readonly int sessionsPerProfile;
        private readonly int probesPerCheckpoint;
        private readonly int primarySeedStart;
        private readonly int challengeSeedStart;
        private readonly StreamWriter sessions;
        private readonly StreamWriter probes;
        private readonly StreamWriter decisions;
        private readonly StreamWriter rewards;
        private readonly StreamWriter physiology;
        private bool finalized;
        private long sessionRows;
        private long probeRows;
        private long decisionRows;
        private long rewardRows;
        private long physiologyRows;

        public LongitudinalArtifactWriter(
            string repositoryRoot,
            string runId,
            string unityVersion,
            string gitCommit,
            bool gitDirty,
            string linUcbConfigurationId,
            string featureSchemaVersion,
            string mappingConfigurationId,
            int primarySeedStart,
            int challengeSeedStart,
            int replications,
            int sessionsPerProfile,
            int probesPerCheckpoint)
        {
            this.repositoryRoot = Path.GetFullPath(repositoryRoot);
            this.runId = runId ?? throw new ArgumentNullException(nameof(runId));
            this.unityVersion = unityVersion ?? string.Empty;
            this.gitCommit = gitCommit ?? string.Empty;
            this.gitDirty = gitDirty;
            this.linUcbConfigurationId = linUcbConfigurationId;
            this.featureSchemaVersion = featureSchemaVersion;
            this.mappingConfigurationId = mappingConfigurationId;
            this.primarySeedStart = primarySeedStart;
            this.challengeSeedStart = challengeSeedStart;
            this.replications = replications;
            this.sessionsPerProfile = sessionsPerProfile;
            this.probesPerCheckpoint = probesPerCheckpoint;
            OutputDirectory = Path.Combine(
                this.repositoryRoot,
                "analysis", "component-c-bandit-evaluation",
                "longitudinal", "runs", runId);
            if (Directory.Exists(OutputDirectory))
            {
                throw new IOException("Longitudinal run ID already exists: " + runId);
            }

            Directory.CreateDirectory(OutputDirectory);
            sessions = Open("sessions.csv",
                "protocol_id,run_id,scenario,archetype,profile_seed,session_index,session_seed,condition,baseline_available,eligible_decisions,mean_regret,cumulative_regret,cumulative_reward,synthetic_stress_change,executed_changes,valid_outcomes,session_model_updates,total_model_updates,no_change,total_variation,scene_violations,multidimension_violations,invalid_model_updates,snapshot_id");
            probes = Open("probes.csv",
                "protocol_id,run_id,scenario,archetype,profile_seed,condition,checkpoint,probe_index,proposed_action,executed_action,oracle_action,selected_expected_reduction,oracle_expected_reduction,pseudo_regret,model_updates,safe_candidate_count");
            decisions = Open("decisions.csv",
                "protocol_id,run_id,scenario,archetype,profile_seed,session_index,condition,opportunity,event_time_seconds,result,proposed_action,executed_action,safety_reason,oracle_regret");
            rewards = Open("rewards.csv",
                "protocol_id,run_id,scenario,archetype,profile_seed,session_index,condition,event_time_seconds,result,attribution,total_reward");
            physiology = Open("physiology.csv",
                "protocol_id,run_id,scenario,archetype,profile_seed,session_index,condition,emission_sequence,event_time_seconds,dropped,ingestion_result,validation_reason,accepted_sequence,window_start_utc,window_end_utc,heart_rate_bpm,rmssd_ms,stress_score,signal_quality");
        }

        public string OutputDirectory { get; }

        public void OnProbe(
            LongitudinalScenario scenario,
            SyntheticArchetypeId archetype,
            int profileSeed,
            LongitudinalPolicyCondition condition,
            int checkpoint,
            int probeIndex,
            SafeOracleDecisionRecord result,
            long modelUpdateCount)
        {
            EnsureOpen();
            probes.WriteLine(string.Join(",", new[]
            {
                LongitudinalEvaluationProfileLoader.ProtocolId,
                runId,
                scenario.ToString(), archetype.ToString(),
                F(profileSeed), condition.ToString(), F(checkpoint),
                F(probeIndex), result.ProposedAction.ToString(),
                result.EvaluatedAction.ToString(), result.OracleAction.ToString(),
                F(result.SelectedExpectedStressReduction),
                F(result.OracleExpectedStressReduction),
                F(result.PseudoRegret), F(modelUpdateCount),
                F(result.SafeCandidateCount)
            }));
            probeRows++;
        }

        public void OnSession(
            LongitudinalScenario scenario,
            SyntheticArchetypeId archetype,
            int profileSeed,
            int sessionIndex,
            int sessionSeed,
            LongitudinalPolicyCondition condition,
            EvaluationSessionSummary summary,
            EvaluationSessionResult result,
            long priorModelUpdateCount)
        {
            EnsureOpen();
            sessions.WriteLine(string.Join(",", new[]
            {
                LongitudinalEvaluationProfileLoader.ProtocolId,
                runId,
                scenario.ToString(), archetype.ToString(),
                F(profileSeed), F(sessionIndex), F(sessionSeed),
                condition.ToString(), F(summary.BaselineAvailable),
                F(summary.EligibleDecisionCount), F(summary.MeanPseudoRegret),
                F(summary.CumulativePseudoRegret),
                F(summary.CumulativeProductionReward),
                F(summary.FinalMinusBaselineStressScore),
                F(summary.ExecutedChangeCount), F(summary.ValidOutcomeCount),
                F(summary.ModelUpdateCount),
                F(result.PolicyState.ModelUpdateCount),
                F(summary.NoChangeCount), F(summary.TotalNormalizedVariation),
                F(summary.SceneLimitViolationCount),
                F(summary.MultiDimensionViolationCount),
                F(summary.InvalidModelUpdateCount),
                result.ModelSnapshot?.SnapshotId ?? string.Empty
            }));
            sessionRows++;

            foreach (var record in result.Decisions)
            {
                var validation = record.Result.Validation;
                decisions.WriteLine(string.Join(",", new[]
                {
                    LongitudinalEvaluationProfileLoader.ProtocolId,
                    runId,
                    scenario.ToString(), archetype.ToString(),
                    F(profileSeed), F(sessionIndex), condition.ToString(),
                    F(record.OpportunitySequence), F(record.EventTimeSeconds),
                    record.Result.ResultCode.ToString(),
                    record.Result.Decision?.SelectedAction.ToString()
                        ?? string.Empty,
                    validation.HasValue
                        ? validation.Value.ExecutedAction.ToString()
                        : string.Empty,
                    validation.HasValue
                        ? validation.Value.ReasonCode.ToString()
                        : string.Empty,
                    record.SafeOracle.HasValue
                        ? F(record.SafeOracle.Value.PseudoRegret)
                        : string.Empty
                }));
                decisionRows++;
            }

            foreach (var record in result.Rewards)
            {
                rewards.WriteLine(string.Join(",", new[]
                {
                    LongitudinalEvaluationProfileLoader.ProtocolId,
                    runId,
                    scenario.ToString(), archetype.ToString(),
                    F(profileSeed), F(sessionIndex), condition.ToString(),
                    F(record.EventTimeSeconds),
                    record.Result.ResultCode.ToString(),
                    record.Result.AttributionCode.ToString(),
                    record.Result.ResultCode
                        == PolicyRewardCycleResultCode.RewardApplied
                        ? F(record.Result.Calculation.Breakdown.TotalReward)
                        : string.Empty
                }));
                rewardRows++;
            }

            foreach (var record in result.PhysiologyRecords)
            {
                var window = record.Window;
                var ingestion = record.Ingestion;
                physiology.WriteLine(string.Join(",", new[]
                {
                    LongitudinalEvaluationProfileLoader.ProtocolId,
                    runId,
                    scenario.ToString(), archetype.ToString(),
                    F(profileSeed), F(sessionIndex), condition.ToString(),
                    F(record.EmissionSequence), F(record.EventTimeSeconds),
                    F(record.Dropped),
                    ingestion.HasValue
                        ? ingestion.Value.ResultCode.ToString() : string.Empty,
                    ingestion.HasValue
                        ? ingestion.Value.ValidationReasonCode.ToString()
                        : string.Empty,
                    ingestion.HasValue
                        ? F(ingestion.Value.AcceptedSequenceNumber)
                        : string.Empty,
                    window != null ? F(window.WindowStartUtcUnixSeconds)
                        : string.Empty,
                    window != null ? F(window.WindowEndUtcUnixSeconds)
                        : string.Empty,
                    window != null ? F(window.HeartRateBpm) : string.Empty,
                    window != null ? F(window.RmssdMs) : string.Empty,
                    window != null ? F(window.Stress.ContinuousScore)
                        : string.Empty,
                    window != null ? F(window.SignalQuality) : string.Empty
                }));
                physiologyRows++;
            }
        }

        public void FinalizeRun(string status, string failureReason)
        {
            if (finalized) return;
            finalized = true;
            sessions.Dispose();
            probes.Dispose();
            decisions.Dispose();
            rewards.Dispose();
            physiology.Dispose();

            var manifest = new StringBuilder("{\n");
            Property(manifest, "schema_version", "1.0");
            Property(manifest, "protocol_id",
                LongitudinalEvaluationProfileLoader.ProtocolId);
            Property(manifest, "pilot_bundle_id",
                LongitudinalEvaluationProfileLoader.PilotBundleId);
            Property(manifest, "run_id", runId);
            Property(manifest, "status", status);
            Property(manifest, "failure_reason", failureReason ?? string.Empty);
            Property(manifest, "research_status", "investigator_frozen");
            Property(manifest, "generator_version",
                ProtocolSyntheticPhysiologyModel.GeneratorVersion);
            Property(manifest, "random_initializer_version",
                EvaluationRandomStreams.InitializerVersion);
            Property(manifest, "linucb_configuration_id",
                linUcbConfigurationId);
            Property(manifest, "feature_schema_version",
                featureSchemaVersion);
            Property(manifest, "mapping_configuration_id",
                mappingConfigurationId);
            Property(manifest, "primary_seed_range",
                F(primarySeedStart) + "-"
                + F(primarySeedStart + replications - 1));
            Property(manifest, "challenge_seed_range",
                F(challengeSeedStart) + "-"
                + F(challengeSeedStart + replications - 1));
            Property(manifest, "session_seed_formula",
                "profile_seed*100+session_index");
            Property(manifest, "probe_seed_formula",
                "profile_seed*100+99; drift post-switch +98");
            Raw(manifest, "probe_stream_id",
                F((int)EvaluationRandomStreamId.LongitudinalProbeContexts));
            Property(manifest, "unity_version", unityVersion);
            Property(manifest, "git_commit", gitCommit);
            Raw(manifest, "git_dirty", gitDirty ? "true" : "false");
            Raw(manifest, "session_rows", F(sessionRows));
            Raw(manifest, "probe_rows", F(probeRows));
            Raw(manifest, "decision_rows", F(decisionRows));
            Raw(manifest, "reward_rows", F(rewardRows));
            Raw(manifest, "physiology_rows", F(physiologyRows));
            Raw(manifest, "sessions_per_profile",
                F(sessionsPerProfile));
            Raw(manifest, "replications_per_archetype",
                F(replications));
            Raw(manifest, "probes_per_checkpoint",
                F(probesPerCheckpoint));
            HashProperty(manifest, "visual_lock_sha256",
                "analysis/component-c-bandit-evaluation/configs/longitudinal-visual-v1.0.json");
            HashProperty(manifest, "study_config_sha256",
                "analysis/component-c-bandit-evaluation/configs/longitudinal-study-v1.0.json");
            HashProperty(manifest, "protocol_sha256",
                "docs/research/LAMINAR_VR_LONGITUDINAL_SIMULATION_PROTOCOL_v1.0.md");
            HashProperty(manifest, "scene_asset_sha256",
                "unity/Assets/Laminar VR/Configuration/Environment/TemplePondSceneParameterProfile_VisualV3.asset");
            HashProperty(manifest, "mapping_asset_sha256",
                "unity/Assets/Laminar VR/Configuration/Environment/TemplePondEnvironmentMappingProfile_v4.asset");
            HashProperty(manifest, "analysis_script_sha256",
                "analysis/component-c-bandit-evaluation/scripts/analyze_longitudinal_v1.py");
            foreach (var file in new[]
                { "sessions.csv", "probes.csv", "decisions.csv", "rewards.csv", "physiology.csv" })
            {
                Property(manifest, file.Replace('.', '_') + "_sha256",
                    Sha256(Path.Combine(OutputDirectory, file)));
            }

            if (manifest[manifest.Length - 2] == ',')
            {
                manifest.Remove(manifest.Length - 2, 1);
            }

            manifest.Append("}\n");
            File.WriteAllText(
                Path.Combine(OutputDirectory, "manifest.json"),
                manifest.ToString(), new UTF8Encoding(false));
        }

        public void Dispose()
        {
            FinalizeRun("incomplete", "Writer disposed before completion");
        }

        private StreamWriter Open(string fileName, string header)
        {
            var writer = new StreamWriter(
                Path.Combine(OutputDirectory, fileName),
                false,
                new UTF8Encoding(false));
            writer.WriteLine(header);
            return writer;
        }

        private void HashProperty(
            StringBuilder builder, string name, string relativePath)
        {
            Property(builder, name,
                Sha256(Path.Combine(repositoryRoot,
                    relativePath.Replace('/', Path.DirectorySeparatorChar))));
        }

        private static string Sha256(string path)
        {
            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(path))
            {
                return BitConverter.ToString(sha.ComputeHash(stream))
                    .Replace("-", string.Empty).ToLowerInvariant();
            }
        }

        private static void Property(StringBuilder builder, string name, string value)
        {
            builder.Append("  \"").Append(name).Append("\": \"")
                .Append(Escape(value)).Append("\",\n");
        }

        private static void Raw(StringBuilder builder, string name, string value)
        {
            builder.Append("  \"").Append(name).Append("\": ")
                .Append(value).Append(",\n");
        }

        private static string Escape(string value)
        {
            return (value ?? string.Empty).Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\r", "\\r").Replace("\n", "\\n");
        }

        private static string F(bool value) => value ? "true" : "false";
        private static string F(int value) => value.ToString(Invariant);
        private static string F(long value) => value.ToString(Invariant);
        private static string F(double value) => value.ToString("R", Invariant);
        private static string F(double? value) => value.HasValue
            ? F(value.Value) : string.Empty;

        private void EnsureOpen()
        {
            if (finalized) throw new ObjectDisposedException(nameof(LongitudinalArtifactWriter));
        }
    }
}
