using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace LaminarVR.AdaptiveMeditation.ResearchEvaluation.Tests
{
    public sealed class EvaluationArtifactWriterTests
    {
        [Test]
        public async Task Writer_ProducesInvariantChecksummedArtifacts()
        {
            Assert.That(
                FrozenEvaluationProfileLoader.TryLoad(
                    out var bundle,
                    out var validationError),
                Is.True,
                validationError);
            var root = Path.Combine(
                Path.GetTempPath(),
                "component-c-evaluation-artifact-test-"
                    + Guid.NewGuid().ToString("N"));
            try
            {
                var cell = await new EvaluationExperimentRunner()
                    .RunCellDetailedAsync(
                        bundle,
                        "artifact-test",
                        SyntheticArchetypeId.A1PreferenceAlignedBalanced,
                        SyntheticFaultScenarioId.R0Nominal,
                        EvaluationPolicyCondition.ContextualBandit,
                        100000);
                var provenance = new EvaluationRunProvenance(
                    new string('a', 64),
                    "test-commit",
                    true,
                    "test-unity",
                    "test-application",
                    "test-os",
                    "test-runtime",
                    DateTimeOffset.Parse("2026-10-14T00:00:00Z"));
                EvaluationArtifactReceipt receipt;
                using (var writer = new EvaluationArtifactWriter(
                    root,
                    "artifact-test-run",
                    bundle,
                    provenance))
                {
                    await writer.OnSessionCompletedAsync(
                        cell,
                        CancellationToken.None);
                    receipt = writer.Complete(
                        new EvaluationStudyResult(
                            EvaluationStudyId.StudyAPrimaryPolicyComparison,
                            new[] { cell.Summary }),
                        DateTimeOffset.Parse("2026-10-14T00:01:00Z"));
                }

                Assert.That(receipt.SessionCount, Is.EqualTo(1));
                Assert.That(receipt.TransitionLineCount, Is.GreaterThan(0));
                Assert.That(File.Exists(receipt.ManifestPath), Is.True);
                Assert.That(File.ReadAllLines(receipt.SummaryPath).Length,
                    Is.EqualTo(2));
                Assert.That(File.ReadAllLines(receipt.TransitionPath).Length,
                    Is.EqualTo(receipt.TransitionLineCount));
                Assert.That(File.ReadAllText(receipt.ManifestPath),
                    Does.Contain(EvaluationRuntimeBundle.ProtocolId));
                Assert.That(File.ReadAllText(receipt.ManifestPath),
                    Does.Contain("\"status\":\"completed\""));
                AssertChecksums(receipt.Sha256ByPath, receipt);
            }
            finally
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, true);
                }
            }
        }

        [Test]
        public async Task Abort_RetainsChecksummedPartialRunWithFailureStatus()
        {
            Assert.That(
                FrozenEvaluationProfileLoader.TryLoad(
                    out var bundle,
                    out var validationError),
                Is.True,
                validationError);
            var root = Path.Combine(
                Path.GetTempPath(),
                "component-c-evaluation-abort-test-"
                    + Guid.NewGuid().ToString("N"));
            try
            {
                var cell = await new EvaluationExperimentRunner()
                    .RunCellDetailedAsync(
                        bundle,
                        "artifact-abort-test",
                        SyntheticArchetypeId.A1PreferenceAlignedBalanced,
                        SyntheticFaultScenarioId.R0Nominal,
                        EvaluationPolicyCondition.ContextualBandit,
                        100000);
                var provenance = new EvaluationRunProvenance(
                    new string('b', 64),
                    "test-commit",
                    true,
                    "test-unity",
                    "test-application",
                    "test-os",
                    "test-runtime",
                    DateTimeOffset.Parse("2026-10-14T00:00:00Z"));
                EvaluationArtifactReceipt receipt;
                using (var writer = new EvaluationArtifactWriter(
                    root,
                    "artifact-abort-run",
                    bundle,
                    provenance))
                {
                    await writer.OnSessionCompletedAsync(
                        cell,
                        CancellationToken.None);
                    receipt = writer.Abort(
                        EvaluationStudyId.StudyAPrimaryPolicyComparison,
                        "failed",
                        "Synthetic validation failure.",
                        DateTimeOffset.Parse("2026-10-14T00:01:00Z"));
                }

                var manifest = File.ReadAllText(receipt.ManifestPath);
                Assert.That(manifest, Does.Contain("\"status\":\"failed\""));
                Assert.That(manifest,
                    Does.Contain("Synthetic validation failure."));
                Assert.That(receipt.Sha256ByPath.Count, Is.EqualTo(5));
                Assert.That(File.ReadAllLines(receipt.SummaryPath).Length,
                    Is.EqualTo(2));
            }
            finally
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, true);
                }
            }
        }

        private static void AssertChecksums(
            IReadOnlyDictionary<string, string> checksums,
            EvaluationArtifactReceipt receipt)
        {
            foreach (var path in new[]
            {
                receipt.TransitionPath,
                receipt.SummaryPath,
                receipt.AggregatePath,
                receipt.InferencePath,
                receipt.ConvergencePath
            })
            {
                string recorded = null;
                foreach (var pair in checksums)
                {
                    if (pair.Key.EndsWith(
                            "/" + Path.GetFileName(path),
                            StringComparison.Ordinal))
                    {
                        recorded = pair.Value;
                        break;
                    }
                }

                Assert.That(recorded,
                    Is.EqualTo(EvaluationArtifactWriter.ComputeSha256(path)));
            }
        }
    }
}
