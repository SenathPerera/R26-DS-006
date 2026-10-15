using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using Process = System.Diagnostics.Process;
using ProcessStartInfo = System.Diagnostics.ProcessStartInfo;
using ProcessWindowStyle = System.Diagnostics.ProcessWindowStyle;

namespace LaminarVR.AdaptiveMeditation.ResearchEvaluation
{
    public static class ComponentCEvaluationRunMenu
    {
        private const string MenuRoot =
            "Tools/Adaptive Meditation/Research/Full Evaluation/";

        private static CancellationTokenSource activeCancellation;
        private static AtomicEvaluationProgress activeProgress;
        private static string activeRunId;

        [MenuItem(MenuRoot + "Run Study A (7,200 sessions)")]
        private static void RunStudyA()
        {
            StartStudy(
                EvaluationStudyId.StudyAPrimaryPolicyComparison,
                "study-a",
                EvaluationExperimentRunner.StudyASessionCount);
        }

        [MenuItem(MenuRoot + "Run Study B (10,400 sessions)")]
        private static void RunStudyB()
        {
            StartStudy(
                EvaluationStudyId.StudyBRobustness,
                "study-b",
                EvaluationExperimentRunner.StudyBSessionCount);
        }

        [MenuItem(MenuRoot + "Run Study C (4,800 sessions)")]
        private static void RunStudyC()
        {
            StartStudy(
                EvaluationStudyId.StudyCLinUcbSensitivity,
                "study-c",
                EvaluationExperimentRunner.StudyCSessionCount);
        }

        [MenuItem(MenuRoot + "Run Study D (206 invariant cases)")]
        private static void RunStudyD()
        {
            StartInvariantStudy();
        }

        [MenuItem(MenuRoot + "Cancel Active Run")]
        private static void CancelActiveRun()
        {
            activeCancellation?.Cancel();
        }

        private static async void StartStudy(
            EvaluationStudyId study,
            string studyCode,
            int sessionCount)
        {
            if (activeCancellation != null)
            {
                Debug.LogError(
                    "A Component C evaluation run is already active: "
                    + activeRunId);
                return;
            }

            if (!EditorUtility.DisplayDialog(
                    "Run Component C simulation study?",
                    "This will execute "
                        + sessionCount
                        + " frozen simulated sessions and write research "
                        + "artifacts under analysis/component-c-bandit-evaluation. "
                        + "Existing run IDs are never overwritten.",
                    "Run " + studyCode,
                    "Cancel"))
            {
                return;
            }

            if (!FrozenEvaluationProfileLoader.TryLoad(
                    out var bundle,
                    out var validationError))
            {
                Debug.LogError(
                    "Cannot start the evaluation: " + validationError);
                return;
            }

            var repositoryRoot = GetRepositoryRoot();
            var evaluationRoot = Path.Combine(
                repositoryRoot,
                "analysis",
                "component-c-bandit-evaluation");
            var runId = DateTimeOffset.UtcNow.ToString(
                    "yyyyMMdd'T'HHmmss'Z'")
                + "-"
                + studyCode;
            var provenance = CreateProvenance(repositoryRoot);
            var cancellation = new CancellationTokenSource();
            var progress = new AtomicEvaluationProgress(study, sessionCount);
            activeCancellation = cancellation;
            activeProgress = progress;
            activeRunId = runId;
            EditorApplication.update += UpdateProgressBar;

            try
            {
                var coordinator = new EvaluationStudyExecutionCoordinator();
                var receipt = await Task.Run(
                    () => coordinator.RunAsync(
                        study,
                        bundle,
                        evaluationRoot,
                        runId,
                        provenance,
                        progress,
                        cancellation.Token),
                    cancellation.Token);
                Debug.Log(
                    "Component C evaluation completed: "
                    + runId
                    + ". Sessions="
                    + receipt.SessionCount
                    + ", transitionLines="
                    + receipt.TransitionLineCount
                    + ", manifest="
                    + receipt.ManifestPath
                    + ". Run the repository artifact validator before "
                    + "using these outputs in the paper.");
            }
            catch (OperationCanceledException)
            {
                Debug.LogWarning(
                    "Component C evaluation was cancelled. A partial "
                    + "cancelled manifest was retained for audit: "
                    + runId);
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    "Component C evaluation failed. A partial failed "
                    + "manifest was retained for audit: "
                    + runId);
                Debug.LogException(exception);
            }
            finally
            {
                EditorApplication.update -= UpdateProgressBar;
                EditorUtility.ClearProgressBar();
                cancellation.Dispose();
                activeCancellation = null;
                activeProgress = null;
                activeRunId = null;
            }
        }

        private static async void StartInvariantStudy()
        {
            if (activeCancellation != null)
            {
                Debug.LogError(
                    "A Component C evaluation run is already active: "
                    + activeRunId);
                return;
            }

            if (!EditorUtility.DisplayDialog(
                    "Run Component C deterministic invariant suite?",
                    "This will execute "
                        + DeterministicInvariantSuiteRunner.ExpectedCaseCount
                        + " recorded safety, physiology, attribution, session, "
                        + "and transport cases and write checksummed artifacts "
                        + "under analysis/component-c-bandit-evaluation.",
                    "Run study-d",
                    "Cancel"))
            {
                return;
            }

            if (!FrozenEvaluationProfileLoader.TryLoad(
                    out var bundle,
                    out var validationError))
            {
                Debug.LogError(
                    "Cannot start the invariant suite: " + validationError);
                return;
            }

            var repositoryRoot = GetRepositoryRoot();
            var evaluationRoot = Path.Combine(
                repositoryRoot,
                "analysis",
                "component-c-bandit-evaluation");
            var runId = DateTimeOffset.UtcNow.ToString(
                    "yyyyMMdd'T'HHmmss'Z'")
                + "-study-d";
            var provenance = CreateProvenance(repositoryRoot);
            var cancellation = new CancellationTokenSource();
            var progress = new AtomicEvaluationProgress(
                EvaluationStudyId.StudyDDeterministicInvariants,
                DeterministicInvariantSuiteRunner.ExpectedCaseCount);
            activeCancellation = cancellation;
            activeProgress = progress;
            activeRunId = runId;
            EditorApplication.update += UpdateProgressBar;

            try
            {
                var result = await Task.Run(
                    () => new DeterministicInvariantSuiteRunner().Run(
                        bundle,
                        progress,
                        cancellation.Token),
                    cancellation.Token);
                cancellation.Token.ThrowIfCancellationRequested();
                var receipt = new DeterministicInvariantArtifactWriter().Write(
                    evaluationRoot,
                    runId,
                    bundle,
                    provenance,
                    result,
                    DateTimeOffset.UtcNow);
                if (result.AllPassed)
                {
                    Debug.Log(
                        "Component C invariant suite completed: "
                        + runId
                        + ". Passed="
                        + receipt.PassedCaseCount
                        + "/"
                        + receipt.CaseCount
                        + ", manifest="
                        + receipt.ManifestPath
                        + ". Run the repository artifact validator before "
                        + "using these outputs in the paper.");
                }
                else
                {
                    Debug.LogError(
                        "Component C invariant suite completed with failures: "
                        + receipt.PassedCaseCount
                        + "/"
                        + receipt.CaseCount
                        + " cases passed. Inspect "
                        + receipt.CasesPath);
                }
            }
            catch (OperationCanceledException)
            {
                Debug.LogWarning(
                    "Component C invariant suite was cancelled before "
                    + "artifacts were finalized: "
                    + runId);
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    "Component C invariant suite failed before completion: "
                    + runId);
                Debug.LogException(exception);
            }
            finally
            {
                EditorApplication.update -= UpdateProgressBar;
                EditorUtility.ClearProgressBar();
                cancellation.Dispose();
                activeCancellation = null;
                activeProgress = null;
                activeRunId = null;
            }
        }

        private static void UpdateProgressBar()
        {
            var progress = activeProgress;
            var cancellation = activeCancellation;
            if (progress == null || cancellation == null)
            {
                return;
            }

            var completed = progress.CompletedSessions;
            var total = Math.Max(1, progress.TotalSessions);
            var cancelled = EditorUtility.DisplayCancelableProgressBar(
                "Component C simulation evaluation",
                activeRunId + ": " + completed + " / " + total,
                Math.Min(1f, (float)completed / total));
            if (cancelled)
            {
                cancellation.Cancel();
            }
        }

        private static EvaluationRunProvenance CreateProvenance(
            string repositoryRoot)
        {
            var protocolPath = Path.Combine(
                repositoryRoot,
                "docs",
                "research",
                "COMPONENT_C_SIMULATION_EVALUATION_PROTOCOL.md");
            var gitCommit = TryRunGit(
                repositoryRoot,
                "rev-parse HEAD",
                out var commitOutput)
                ? commitOutput
                : "unavailable";
            var gitStatusAvailable = TryRunGit(
                repositoryRoot,
                "status --porcelain --untracked-files=normal",
                out var statusOutput);
            var applicationVersion = UnityEngine.Application.version;
            if (string.IsNullOrWhiteSpace(applicationVersion))
            {
                applicationVersion = "unset-editor-application-version";
            }

            return new EvaluationRunProvenance(
                EvaluationArtifactWriter.ComputeSha256(protocolPath),
                gitCommit,
                !gitStatusAvailable || statusOutput.Length > 0,
                UnityEngine.Application.unityVersion,
                applicationVersion,
                SystemInfo.operatingSystem,
                System.Environment.Version.ToString(),
                DateTimeOffset.UtcNow);
        }

        private static bool TryRunGit(
            string repositoryRoot,
            string arguments,
            out string output)
        {
            output = string.Empty;
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = arguments,
                    WorkingDirectory = repositoryRoot,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                using (var process = Process.Start(startInfo))
                {
                    if (process == null)
                    {
                        return false;
                    }

                    output = process.StandardOutput.ReadToEnd().Trim();
                    process.WaitForExit();
                    return process.ExitCode == 0;
                }
            }
            catch (Exception)
            {
                output = string.Empty;
                return false;
            }
        }

        private static string GetRepositoryRoot()
        {
            var unityRoot = Directory.GetParent(
                UnityEngine.Application.dataPath).FullName;
            return Directory.GetParent(unityRoot).FullName;
        }

        private sealed class AtomicEvaluationProgress
            : IProgress<EvaluationRunProgress>
        {
            private int completedSessions;

            public AtomicEvaluationProgress(
                EvaluationStudyId study,
                int totalSessions)
            {
                Study = study;
                TotalSessions = totalSessions;
            }

            public EvaluationStudyId Study { get; }
            public int TotalSessions { get; }
            public int CompletedSessions => Volatile.Read(
                ref completedSessions);

            public void Report(EvaluationRunProgress value)
            {
                if (value.Study != Study
                    || value.TotalSessions != TotalSessions)
                {
                    return;
                }

                Volatile.Write(
                    ref completedSessions,
                    value.CompletedSessions);
            }
        }
    }
}
