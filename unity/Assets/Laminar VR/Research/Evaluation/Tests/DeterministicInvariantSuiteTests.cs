using System;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace LaminarVR.AdaptiveMeditation.ResearchEvaluation.Tests
{
    public sealed class DeterministicInvariantSuiteTests
    {
        [Test]
        public void Run_FrozenCatalogRecordsAllPassingCases()
        {
            Assert.That(
                FrozenEvaluationProfileLoader.TryLoad(
                    out var bundle,
                    out var validationError),
                Is.True,
                validationError);

            var result = new DeterministicInvariantSuiteRunner().Run(bundle);

            Assert.That(result.Cases,
                Has.Length.EqualTo(
                    DeterministicInvariantSuiteRunner.ExpectedCaseCount));
            Assert.That(result.AllPassed, Is.True,
                string.Join(", ", result.Cases
                    .Where(item => !item.Passed)
                    .Select(item => item.CaseId + ":" + item.Actual)));
            Assert.That(result.Cases.Select(item => item.CaseId).Distinct().Count(),
                Is.EqualTo(result.Cases.Length));
            Assert.That(result.Cases.Any(
                item => item.Category == "safety.gate"), Is.True);
            Assert.That(result.Cases.Any(
                item => item.Category == "attribution.window_reuse"), Is.True);
            Assert.That(result.Cases.Any(
                item => item.Category == "transport.session_gate"), Is.True);
        }

        [Test]
        public void Writer_ProducesChecksummedCaseArtifactAndManifest()
        {
            Assert.That(
                FrozenEvaluationProfileLoader.TryLoad(
                    out var bundle,
                    out var validationError),
                Is.True,
                validationError);
            var root = Path.Combine(
                Path.GetTempPath(),
                "component-c-invariant-artifact-test-"
                    + Guid.NewGuid().ToString("N"));
            try
            {
                var result = new DeterministicInvariantSuiteRunner().Run(bundle);
                var provenance = new EvaluationRunProvenance(
                    new string('d', 64),
                    "test-commit",
                    false,
                    "test-unity",
                    "test-application",
                    "test-os",
                    "test-runtime",
                    DateTimeOffset.Parse("2026-10-15T00:00:00Z"));

                var receipt = new DeterministicInvariantArtifactWriter().Write(
                    root,
                    "study-d-test",
                    bundle,
                    provenance,
                    result,
                    DateTimeOffset.Parse("2026-10-15T00:01:00Z"));

                Assert.That(File.ReadAllLines(receipt.CasesPath),
                    Has.Length.EqualTo(result.Cases.Length));
                Assert.That(receipt.CasesSha256,
                    Is.EqualTo(EvaluationArtifactWriter.ComputeSha256(
                        receipt.CasesPath)));
                var manifest = File.ReadAllText(receipt.ManifestPath);
                Assert.That(manifest,
                    Does.Contain("\"all_cases_passed\":true")
                        .And.Contain("\"case_count\":206")
                        .And.Contain(receipt.CasesSha256));
            }
            finally
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, true);
                }
            }
        }
    }
}
