using System;
using System.IO;
using System.Threading;
using LaminarVR.AdaptiveMeditation.Environment;
using LaminarVR.AdaptiveMeditation.Policy;
using UnityEditor;
using UnityEngine;

namespace LaminarVR.AdaptiveMeditation.ResearchEvaluation
{
    public static class ComponentCEvaluationMenu
    {
        [MenuItem(
            "Tools/Adaptive Meditation/Research/Run Phase 2 Smoke Session")]
        private static async void RunPhase2SmokeSession()
        {
            try
            {
                if (!FrozenEvaluationProfileLoader.TryLoad(
                        out var bundle,
                        out var validationError))
                {
                    Debug.LogError(
                        "Phase 2 evaluation profiles are invalid: "
                        + validationError);
                    return;
                }

                var result = await new EvaluationSessionRunner().RunAsync(
                    new EvaluationSessionRequest(
                        bundle,
                        "phase-2-smoke-session",
                        "synthetic-smoke",
                        StudyPolicyMode.ContextualBandit,
                        new EnvironmentState(
                            0.45f,
                            0.65f,
                            0.45f,
                            0.55f,
                            0.35f),
                        new Phase2SmokePhysiologyModel(),
                        1700000000d),
                    CancellationToken.None);

                Debug.Log(
                    "Phase 2 smoke session completed. Decisions="
                    + result.Decisions.Length
                    + ", rewards="
                    + result.RewardAppliedCount
                    + ", modelUpdates="
                    + result.PolicyState.ModelUpdateCount
                    + ", safeStates="
                    + result.AllAppliedStatesSafe
                    + ". Results were kept in memory and are not research data.");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        [MenuItem(
            "Tools/Adaptive Meditation/Research/Run Phase 3 Archetype Smoke Suite")]
        private static async void RunPhase3ArchetypeSmokeSuite()
        {
            try
            {
                if (!FrozenEvaluationProfileLoader.TryLoad(
                        out var bundle,
                        out var validationError))
                {
                    Debug.LogError(
                        "Phase 3 evaluation profiles are invalid: "
                        + validationError);
                    return;
                }

                for (var index = 0;
                    index < SyntheticArchetypeCatalog.Count;
                    index++)
                {
                    var archetype = SyntheticArchetypeCatalog.GetAt(index);
                    var replication = SyntheticReplicationFactory.Create(
                        100000,
                        archetype.Id);
                    var result = await new EvaluationSessionRunner().RunAsync(
                        new EvaluationSessionRequest(
                            bundle,
                            "phase-3-smoke-" + archetype.Code,
                            "synthetic-smoke",
                            StudyPolicyMode.ContextualBandit,
                            replication.Preference,
                            replication.CreatePhysiologyModel(),
                            1700000000d),
                        CancellationToken.None);
                    Debug.Log(
                        archetype.Code
                        + " completed: decisions="
                        + result.Decisions.Length
                        + ", rewards="
                        + result.RewardAppliedCount
                        + ", updates="
                        + result.PolicyState.ModelUpdateCount
                        + ", safeStates="
                        + result.AllAppliedStatesSafe
                        + ".");
                }

                Debug.Log(
                    "Phase 3 archetype smoke suite completed. Results were "
                    + "kept in memory and are not research data.");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        [MenuItem(
            "Tools/Adaptive Meditation/Research/Run Phase 4 Fault Smoke Suite")]
        private static async void RunPhase4FaultSmokeSuite()
        {
            try
            {
                if (!FrozenEvaluationProfileLoader.TryLoad(
                        out var bundle,
                        out var validationError))
                {
                    Debug.LogError(
                        "Phase 4 evaluation profiles are invalid: "
                        + validationError);
                    return;
                }

                for (var index = 0;
                    index < SyntheticFaultScenarioCatalog.Count;
                    index++)
                {
                    var scenario = SyntheticFaultScenarioCatalog.GetAt(index);
                    var replication = SyntheticReplicationFactory.Create(
                        200000,
                        SyntheticArchetypeId.A1PreferenceAlignedBalanced);
                    var result = await new EvaluationSessionRunner().RunAsync(
                        new EvaluationSessionRequest(
                            bundle,
                            "phase-4-smoke-" + scenario.Code,
                            "synthetic-smoke",
                            StudyPolicyMode.ContextualBandit,
                            replication.Preference,
                            SyntheticFaultSessionFactory.CreatePhysiologyModel(
                                replication,
                                scenario.Id),
                            1700000000d,
                            scenario.CreateNetworkSchedule()),
                        CancellationToken.None);
                    Debug.Log(
                        scenario.Code
                        + " completed: baseline="
                        + result.BaselineAvailable
                        + ", decisions="
                        + result.Decisions.Length
                        + ", rewards="
                        + result.RewardAppliedCount
                        + ", updates="
                        + result.PolicyState.ModelUpdateCount
                        + ", safeStates="
                        + result.AllAppliedStatesSafe
                        + ".");
                }

                Debug.Log(
                    "Phase 4 fault smoke suite completed. Results were kept "
                    + "in memory and are not research data.");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        [MenuItem(
            "Tools/Adaptive Meditation/Research/Run Phase 5 Policy Smoke Suite")]
        private static async void RunPhase5PolicySmokeSuite()
        {
            try
            {
                if (!FrozenEvaluationProfileLoader.TryLoad(
                        out var bundle,
                        out var validationError))
                {
                    Debug.LogError(
                        "Phase 5 evaluation profiles are invalid: "
                        + validationError);
                    return;
                }

                var runner = new EvaluationExperimentRunner();
                for (var policyValue =
                        (int)EvaluationPolicyCondition.StaticPersonalized;
                    policyValue <= (int)EvaluationPolicyCondition.RandomSafe;
                    policyValue++)
                {
                    var policy = (EvaluationPolicyCondition)policyValue;
                    var summary = await runner.RunCellAsync(
                        bundle,
                        "phase-5-smoke",
                        SyntheticArchetypeId.A1PreferenceAlignedBalanced,
                        SyntheticFaultScenarioId.R0Nominal,
                        policy,
                        EvaluationExperimentRunner.StudyAFirstSeed,
                        CancellationToken.None);
                    Debug.Log(
                        policy
                        + " completed: eligibleDecisions="
                        + summary.EligibleDecisionCount
                        + ", meanRegret="
                        + (summary.MeanPseudoRegret.HasValue
                            ? summary.MeanPseudoRegret.Value.ToString("R")
                            : "missing")
                        + ", validOutcomes="
                        + summary.ValidOutcomeCount
                        + ", modelUpdates="
                        + summary.ModelUpdateCount
                        + ", safeStates="
                        + summary.AllAppliedStatesSafe
                        + ".");
                }

                Debug.Log(
                    "Phase 5 policy smoke suite completed. Results were kept "
                    + "in memory and are not research data.");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        [MenuItem(
            "Tools/Adaptive Meditation/Research/Run Phase 6 Artifact Smoke")]
        private static async void RunPhase6ArtifactSmoke()
        {
            var outputRoot = Path.Combine(
                Path.GetTempPath(),
                "component-c-phase-6-smoke-" + Guid.NewGuid().ToString("N"));
            try
            {
                if (!FrozenEvaluationProfileLoader.TryLoad(
                        out var bundle,
                        out var validationError))
                {
                    Debug.LogError(
                        "Phase 6 evaluation profiles are invalid: "
                        + validationError);
                    return;
                }

                var unityRoot = Directory.GetParent(UnityEngine.Application.dataPath)
                    .FullName;
                var repositoryRoot = Directory.GetParent(unityRoot).FullName;
                var protocolPath = Path.Combine(
                    repositoryRoot,
                    "docs",
                    "research",
                    "COMPONENT_C_SIMULATION_EVALUATION_PROTOCOL.md");
                var provenance = new EvaluationRunProvenance(
                    EvaluationArtifactWriter.ComputeSha256(protocolPath),
                    "phase-6-smoke-not-a-research-run",
                    true,
                    UnityEngine.Application.unityVersion,
                    UnityEngine.Application.version,
                    SystemInfo.operatingSystem,
                    System.Environment.Version.ToString(),
                    DateTimeOffset.UtcNow);
                var cell = await new EvaluationExperimentRunner()
                    .RunCellDetailedAsync(
                        bundle,
                        "phase-6-smoke",
                        SyntheticArchetypeId.A1PreferenceAlignedBalanced,
                        SyntheticFaultScenarioId.R0Nominal,
                        EvaluationPolicyCondition.ContextualBandit,
                        EvaluationExperimentRunner.StudyAFirstSeed,
                        CancellationToken.None);
                EvaluationArtifactReceipt receipt;
                using (var writer = new EvaluationArtifactWriter(
                    outputRoot,
                    "phase-6-smoke",
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
                        DateTimeOffset.UtcNow);
                }

                Debug.Log(
                    "Phase 6 artifact smoke completed. Sessions="
                    + receipt.SessionCount
                    + ", transitionLines="
                    + receipt.TransitionLineCount
                    + ", checksummedOutputs="
                    + receipt.Sha256ByPath.Count
                    + ". Temporary artifacts were validated and removed; "
                    + "they are not research data.");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
            finally
            {
                if (Directory.Exists(outputRoot))
                {
                    Directory.Delete(outputRoot, true);
                }
            }
        }
    }
}
