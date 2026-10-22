using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace LaminarVR.AdaptiveMeditation.ResearchEvaluation
{
    public sealed class InvariantArtifactReceipt
    {
        public InvariantArtifactReceipt(
            string manifestPath,
            string casesPath,
            int caseCount,
            int passedCaseCount,
            string casesSha256)
        {
            ManifestPath = manifestPath;
            CasesPath = casesPath;
            CaseCount = caseCount;
            PassedCaseCount = passedCaseCount;
            CasesSha256 = casesSha256;
        }

        public string ManifestPath { get; }
        public string CasesPath { get; }
        public int CaseCount { get; }
        public int PassedCaseCount { get; }
        public string CasesSha256 { get; }
    }

    public sealed class DeterministicInvariantArtifactWriter
    {
        public const string SchemaVersion = "1.0.0";

        private static readonly CultureInfo Invariant =
            CultureInfo.InvariantCulture;

        public InvariantArtifactReceipt Write(
            string evaluationRoot,
            string runId,
            EvaluationRuntimeBundle bundle,
            EvaluationRunProvenance provenance,
            DeterministicInvariantSuiteResult result,
            DateTimeOffset completedUtc)
        {
            if (string.IsNullOrWhiteSpace(evaluationRoot))
            {
                throw new ArgumentException(
                    "An evaluation root is required.",
                    nameof(evaluationRoot));
            }

            if (string.IsNullOrWhiteSpace(runId))
            {
                throw new ArgumentException("A run ID is required.", nameof(runId));
            }

            if (bundle == null)
            {
                throw new ArgumentNullException(nameof(bundle));
            }

            if (provenance == null)
            {
                throw new ArgumentNullException(nameof(provenance));
            }

            if (result == null)
            {
                throw new ArgumentNullException(nameof(result));
            }

            var root = Path.GetFullPath(evaluationRoot);
            var casesDirectory = Path.Combine(root, "results", "invariant-cases");
            var manifestDirectory = Path.Combine(root, "manifests");
            Directory.CreateDirectory(casesDirectory);
            Directory.CreateDirectory(manifestDirectory);
            var casesPath = Path.Combine(casesDirectory, runId + ".jsonl");
            var manifestPath = Path.Combine(manifestDirectory, runId + ".json");
            EnsureNew(casesPath);
            EnsureNew(manifestPath);

            var encoding = new UTF8Encoding(false);
            using (var writer = new StreamWriter(casesPath, false, encoding))
            {
                for (var index = 0; index < result.Cases.Length; index++)
                {
                    writer.WriteLine(BuildCaseJson(result.Cases[index]));
                }
            }

            var casesSha = EvaluationArtifactWriter.ComputeSha256(casesPath);
            var relativeCases = "results/invariant-cases/" + runId + ".jsonl";
            File.WriteAllText(
                manifestPath,
                BuildManifestJson(
                    runId,
                    bundle,
                    provenance,
                    result,
                    completedUtc,
                    relativeCases,
                    casesSha),
                encoding);
            return new InvariantArtifactReceipt(
                manifestPath,
                casesPath,
                result.Cases.Length,
                result.PassedCaseCount,
                casesSha);
        }

        private static string BuildCaseJson(InvariantCaseRecord record)
        {
            var builder = new StringBuilder("{");
            AddString(builder, "schema_version", SchemaVersion);
            AddString(builder, "protocol_id", EvaluationRuntimeBundle.ProtocolId);
            AddString(builder, "study", "study-d");
            AddString(builder, "case_id", record.CaseId);
            AddString(builder, "category", record.Category);
            AddRaw(builder, "exact_input", record.ExactInputJson);
            AddNullableString(builder, "proposed_action", record.ProposedAction);
            AddString(builder, "validation_outcome", record.ValidationOutcome);
            if (record.ExecutedState.HasValue)
            {
                AddRaw(
                    builder,
                    "executed_state",
                    DeterministicInvariantSuiteRunner.StateJson(
                        record.ExecutedState.Value));
            }
            else
            {
                AddNull(builder, "executed_state");
            }

            AddString(builder, "reward_disposition", record.RewardDisposition);
            AddNumber(builder, "model_update_count", record.ModelUpdateCount);
            AddString(builder, "expected", record.Expected);
            AddString(builder, "actual", record.Actual);
            AddBoolean(builder, "passed", record.Passed);
            return End(builder);
        }

        private static string BuildManifestJson(
            string runId,
            EvaluationRuntimeBundle bundle,
            EvaluationRunProvenance provenance,
            DeterministicInvariantSuiteResult result,
            DateTimeOffset completedUtc,
            string relativeCases,
            string casesSha)
        {
            var builder = new StringBuilder("{");
            AddString(builder, "schema_version", SchemaVersion);
            AddString(builder, "artifact_type", "deterministic-invariant-suite");
            AddString(builder, "protocol_id", EvaluationRuntimeBundle.ProtocolId);
            AddString(builder, "protocol_sha256", provenance.ProtocolSha256);
            AddString(builder, "run_id", runId);
            AddString(builder, "study", "StudyDDeterministicInvariants");
            AddString(builder, "status", "completed");
            AddString(builder, "git_commit", provenance.GitCommit);
            AddBoolean(builder, "git_dirty", provenance.GitDirty);
            AddString(builder, "unity_version", provenance.UnityVersion);
            AddString(builder, "application_version", provenance.ApplicationVersion);
            AddString(builder, "profile_bundle_id", EvaluationRuntimeBundle.PilotBundleId);
            AddString(builder, "scene_id", bundle.Scene.SceneId);
            AddString(
                builder,
                "linucb_configuration",
                bundle.LinUcb.ConfigurationId + "/"
                    + bundle.LinUcb.ConfigurationVersion.ToString(Invariant));
            AddString(
                builder,
                "physiology_configuration",
                bundle.Physiology.ConfigurationId + "/"
                    + bundle.Physiology.ConfigurationVersion.ToString(Invariant));
            AddString(
                builder,
                "reward_configuration",
                bundle.Reward.ConfigurationId + "/"
                    + bundle.Reward.ConfigurationVersion.ToString(Invariant));
            AddString(
                builder,
                "coordinator_configuration",
                bundle.Coordinator.ConfigurationId + "/"
                    + bundle.Coordinator.ConfigurationVersion.ToString(Invariant));
            AddString(
                builder,
                "started_utc",
                provenance.StartedUtc.ToString("O", Invariant));
            AddString(
                builder,
                "completed_utc",
                completedUtc.ToString("O", Invariant));
            AddNumber(builder, "case_count", result.Cases.Length);
            AddNumber(builder, "passed_case_count", result.PassedCaseCount);
            AddNumber(
                builder,
                "failed_case_count",
                result.Cases.Length - result.PassedCaseCount);
            AddBoolean(builder, "all_cases_passed", result.AllPassed);
            AddString(builder, "operating_system", provenance.OperatingSystem);
            AddString(builder, "runtime_version", provenance.RuntimeVersion);
            var checksums = new StringBuilder("{");
            AddString(checksums, relativeCases, casesSha);
            AddRaw(builder, "output_sha256", End(checksums));
            return End(builder) + System.Environment.NewLine;
        }

        private static void EnsureNew(string path)
        {
            if (File.Exists(path))
            {
                throw new IOException(
                    "Invariant artifacts are append-protected: " + path);
            }
        }

        private static void AddString(
            StringBuilder builder,
            string name,
            string value)
        {
            Prefix(builder, name);
            builder.Append('"').Append(Escape(value)).Append('"');
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
            long value)
        {
            Prefix(builder, name);
            builder.Append(value.ToString(Invariant));
        }

        private static void AddBoolean(
            StringBuilder builder,
            string name,
            bool value)
        {
            Prefix(builder, name);
            builder.Append(value ? "true" : "false");
        }

        private static void AddRaw(
            StringBuilder builder,
            string name,
            string value)
        {
            Prefix(builder, name);
            builder.Append(value);
        }

        private static void AddNull(StringBuilder builder, string name)
        {
            Prefix(builder, name);
            builder.Append("null");
        }

        private static void Prefix(StringBuilder builder, string name)
        {
            if (builder.Length > 1 && builder[builder.Length - 1] != '{')
            {
                builder.Append(',');
            }

            builder.Append('"').Append(name).Append("\":");
        }

        private static string End(StringBuilder builder)
        {
            return builder.Append('}').ToString();
        }

        private static string Escape(string value)
        {
            if (value == null)
            {
                return string.Empty;
            }

            return value.Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\r", "\\r")
                .Replace("\n", "\\n")
                .Replace("\t", "\\t");
        }
    }
}
