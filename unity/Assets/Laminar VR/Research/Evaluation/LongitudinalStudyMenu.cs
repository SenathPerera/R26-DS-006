using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace LaminarVR.AdaptiveMeditation.ResearchEvaluation
{
    public static class LongitudinalStudyMenu
    {
        private const string MenuRoot =
            "Tools/Adaptive Meditation/Research/Longitudinal Study A/";
        private static CancellationTokenSource activeCancellation;
        private static int completedSessions;
        private static int totalSessions;

        [MenuItem(MenuRoot + "Run Smoke (68 sessions)")]
        private static void RunSmoke()
        {
            StartRun(true);
        }

        [MenuItem(MenuRoot + "Run Frozen Full Study (20,400 sessions)")]
        private static void RunFull()
        {
            StartRun(false);
        }

        [MenuItem(MenuRoot + "Cancel Active Run")]
        private static void CancelRun()
        {
            activeCancellation?.Cancel();
        }

        private static async void StartRun(bool smoke)
        {
            if (activeCancellation != null)
            {
                UnityEngine.Debug.LogError(
                    "A longitudinal simulation is already running.");
                return;
            }

            if (!LongitudinalEvaluationProfileLoader.TryLoad(
                    out var bundle, out var mapping, out var error))
            {
                UnityEngine.Debug.LogError(
                    "Cannot start longitudinal simulation: " + error);
                return;
            }

            var replications = smoke ? 1 : LongitudinalStudyRunner.ProfileReplications;
            var sessions = smoke ? 2 : LongitudinalStudyRunner.SessionsPerProfile;
            var probes = smoke ? 4 : LongitudinalStudyRunner.ProbesPerCheckpoint;
            totalSessions = smoke
                ? 68
                : LongitudinalStudyRunner.FullStudySessionCount;
            if (!EditorUtility.DisplayDialog(
                    "Run longitudinal simulation?",
                    "This executes " + totalSessions
                    + " synthetic sessions using Temple Pond v3 and mapping "
                    + mapping.ConfigurationId + ". Protocol v1.0 is investigator-frozen; "
                    + "the full run uses the reserved 410000/420000 seed families.",
                    "Run", "Cancel"))
            {
                return;
            }

            var repositoryRoot = Directory.GetParent(
                Directory.GetParent(UnityEngine.Application.dataPath).FullName).FullName;
            var runId = DateTimeOffset.UtcNow.ToString(
                "yyyyMMdd'T'HHmmss'Z'")
                + (smoke ? "-longitudinal-smoke" : "-longitudinal-full");
            var cancellation = new CancellationTokenSource();
            activeCancellation = cancellation;
            Interlocked.Exchange(ref completedSessions, 0);
            EditorApplication.update += UpdateProgress;
            LongitudinalArtifactWriter writer = null;
            try
            {
                writer = new LongitudinalArtifactWriter(
                    repositoryRoot, runId, UnityEngine.Application.unityVersion,
                    Git(repositoryRoot, "rev-parse HEAD"),
                    !string.IsNullOrEmpty(Git(
                        repositoryRoot, "status --porcelain")),
                    bundle.LinUcb.ConfigurationId,
                    bundle.LinUcb.FeatureSchemaVersion,
                    mapping.ConfigurationId,
                    smoke ? LongitudinalStudyRunner.DevelopmentPrimaryFirstSeed
                        : LongitudinalStudyRunner.PrimaryFirstSeed,
                    smoke ? LongitudinalStudyRunner.DevelopmentChallengeFirstSeed
                        : LongitudinalStudyRunner.ChallengeFirstSeed,
                    replications, sessions, probes);
                var runner = new LongitudinalStudyRunner();
                var completed = await Task.Run(() => runner.RunAsync(
                    bundle,
                    writer,
                    count => Interlocked.Exchange(
                        ref completedSessions, count),
                    cancellation.Token,
                    replications,
                    sessions,
                    probes,
                    smoke), cancellation.Token);
                writer.FinalizeRun("completed", null);
                UnityEngine.Debug.Log(
                    "Longitudinal simulation completed " + completed
                    + " sessions. Artifacts: " + writer.OutputDirectory);
            }
            catch (OperationCanceledException)
            {
                writer?.FinalizeRun("cancelled", "User cancelled the run");
                UnityEngine.Debug.LogWarning(
                    "Longitudinal simulation cancelled. Partial artifacts: "
                    + writer?.OutputDirectory);
            }
            catch (Exception exception)
            {
                writer?.FinalizeRun("failed", exception.ToString());
                UnityEngine.Debug.LogError(
                    "Longitudinal simulation failed: " + exception);
            }
            finally
            {
                writer?.Dispose();
                EditorApplication.update -= UpdateProgress;
                EditorUtility.ClearProgressBar();
                cancellation.Dispose();
                activeCancellation = null;
            }
        }

        private static void UpdateProgress()
        {
            var completed = Interlocked.CompareExchange(
                ref completedSessions, 0, 0);
            if (EditorUtility.DisplayCancelableProgressBar(
                    "Longitudinal Study A",
                    completed + " / " + totalSessions + " sessions",
                    totalSessions > 0
                        ? (float)completed / totalSessions
                        : 0f))
            {
                activeCancellation?.Cancel();
            }
        }

        private static string Git(string repositoryRoot, string arguments)
        {
            try
            {
                using (var process = Process.Start(new ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = arguments,
                    WorkingDirectory = repositoryRoot,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                }))
                {
                    var output = process.StandardOutput.ReadToEnd().Trim();
                    process.WaitForExit();
                    return process.ExitCode == 0 ? output : string.Empty;
                }
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }
    }
}
