using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using LaminarVR.AdaptiveMeditation.Environment;
using LaminarVR.AdaptiveMeditation.Networking;
using LaminarVR.AdaptiveMeditation.Physiology;
using LaminarVR.AdaptiveMeditation.Policy;
using LaminarVR.AdaptiveMeditation.Runtime.Application;
using LaminarVR.AdaptiveMeditation.Runtime.Configuration;
using LaminarVR.AdaptiveMeditation.Session;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace LaminarVR.AdaptiveMeditation.Tests.PlayMode
{
    public sealed class ProductionSessionCoordinatorPlayModeTests
    {
        private readonly List<UnityEngine.Object> createdObjects =
            new List<UnityEngine.Object>();
        private string telemetryFilePath;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            for (var index = 0; index < createdObjects.Count; index++)
            {
                if (createdObjects[index] != null)
                {
                    UnityEngine.Object.Destroy(createdObjects[index]);
                }
            }

            createdObjects.Clear();
            yield return null;
            if (!string.IsNullOrEmpty(telemetryFilePath)
                && File.Exists(telemetryFilePath))
            {
                File.Delete(telemetryFilePath);
            }
        }

        [UnityTest]
        public IEnumerator Coordinator_CollectsBaselineAndRunsDecisionCycle()
        {
            var root = Track(new GameObject("ProductionCoordinatorHarness"));
            var adapter = root.AddComponent<RecordingAdapter>();
            var bootstrap = root.AddComponent<ApplicationBootstrap>();
            bootstrap.Configure(
                CreateProfile<SceneParameterProfile>(SceneProfileJson),
                adapter,
                StudyPolicyMode.StaticPersonalized);

            var coordinator = root.AddComponent<ProductionSessionCoordinator>();
            coordinator.enabled = false;
            coordinator.Configure(
                bootstrap,
                CreateProfile<SessionTimingProfile>(TimingProfileJson),
                CreateProfile<PhysiologyValidationProfile>(
                    PhysiologyProfileJson),
                CreateProfile<RewardPipelineProfile>(RewardProfileJson),
                CreateProfile<StabilizationSelectionProfile>(
                    StabilizationProfileJson),
                CreateProfile<TelemetryLoggingProfile>(TelemetryProfileJson),
                CreateProfile<ProductionCoordinatorProfile>(
                    CoordinatorProfileJson));
            coordinator.ConfigureSessionContext(
                "playmode-production-" + Guid.NewGuid().ToString("N"),
                "P-PLAYMODE-PRODUCTION",
                State(0.5f));

            Assert.That(
                coordinator.TryInitialize(out var validationError),
                Is.True,
                validationError);
            telemetryFilePath = coordinator.TelemetryFilePath;
            coordinator.SetNetworkConnected(true);

            var startTime = Time.realtimeSinceStartupAsDouble;
            var utcNow = UtcNowUnixSeconds();
            coordinator.SubmitCommand("start", SessionCommandType.Start);
            coordinator.Advance(startTime + 0.005d, utcNow);
            Assert.That(coordinator.LastCommandResult.Applied, Is.True);

            coordinator.SubmitPhysiology(CreateWindow(utcNow - 1d, 1.5d));
            coordinator.SubmitPhysiology(CreateWindow(utcNow - 0.5d, 1.4d));
            coordinator.Advance(startTime + 0.01d, utcNow);

            Assert.That(coordinator.AcceptedBaselineWindowCount, Is.EqualTo(2));

            coordinator.Advance(startTime + 0.11d, utcNow + 0.1d);
            Assert.That(coordinator.Phase, Is.EqualTo(VrSessionPhase.Adaptive));
            Assert.That(coordinator.HasPolicyController, Is.True);

            coordinator.Advance(startTime + 0.31d, utcNow + 0.3d);
            for (var frame = 0;
                frame < 20 && !coordinator.LastDecisionResult.HasValue;
                frame++)
            {
                yield return null;
                coordinator.Advance(startTime + 0.31d, utcNow + 0.3d);
            }

            Assert.That(coordinator.LastDecisionResult.HasValue, Is.True);
            Assert.That(
                coordinator.LastDecisionResult.Value.ResultCode,
                Is.EqualTo(
                    PolicyDecisionCycleResultCode.RewardWindowOpened));
            Assert.That(adapter.LastAppliedState, Is.EqualTo(State(0.5f)));
        }

        [UnityTest]
        public IEnumerator VisualBoundary_ForwardsTransportNeutralInputs()
        {
            var root = Track(new GameObject("VisualSessionBoundaryHarness"));
            var adapter = root.AddComponent<RecordingAdapter>();
            var bootstrap = root.AddComponent<ApplicationBootstrap>();
            bootstrap.Configure(
                CreateProfile<SceneParameterProfile>(SceneProfileJson),
                adapter,
                StudyPolicyMode.StaticPersonalized);

            var coordinator = root.AddComponent<ProductionSessionCoordinator>();
            coordinator.enabled = false;
            coordinator.Configure(
                bootstrap,
                CreateProfile<SessionTimingProfile>(TimingProfileJson),
                CreateProfile<PhysiologyValidationProfile>(
                    PhysiologyProfileJson),
                CreateProfile<RewardPipelineProfile>(RewardProfileJson),
                CreateProfile<StabilizationSelectionProfile>(
                    StabilizationProfileJson),
                CreateProfile<TelemetryLoggingProfile>(TelemetryProfileJson),
                CreateProfile<ProductionCoordinatorProfile>(
                    CoordinatorProfileJson));

            var boundary = root.AddComponent<VisualSessionBoundary>();
            boundary.enabled = false;
            boundary.Configure(coordinator);

            var utcNow = UtcNowUnixSeconds();
            boundary.ReceiveSessionContext(
                "playmode-boundary-" + Guid.NewGuid().ToString("N"),
                "P-PLAYMODE-BOUNDARY",
                State(0.5f));
            boundary.ReceiveConnectionState(
                SessionTransportConnectionState.Connected);
            boundary.ReceiveCommand("start", SessionCommandType.Start);

            Assert.That(boundary.PendingMessageCount, Is.EqualTo(3));
            Assert.That(boundary.ProcessPendingMessages(), Is.EqualTo(3));
            Assert.That(boundary.PendingMessageCount, Is.Zero);
            Assert.That(boundary.RejectedMessageCount, Is.Zero);
            Assert.That(boundary.LastDispatchError, Is.Empty);

            Assert.That(
                coordinator.TryInitialize(out var validationError),
                Is.True,
                validationError);
            telemetryFilePath = coordinator.TelemetryFilePath;
            var startTime = Time.realtimeSinceStartupAsDouble;
            coordinator.Advance(
                startTime,
                utcNow);

            Assert.That(coordinator.IsNetworkConnected, Is.True);
            Assert.That(coordinator.LastCommandResult.Applied, Is.True);
            Assert.That(coordinator.Phase, Is.EqualTo(VrSessionPhase.Acclimatization));

            boundary.ReceivePhysiology(CreateWindow(utcNow - 0.5d, 1.4d));
            Assert.That(boundary.ProcessPendingMessages(), Is.EqualTo(1));
            coordinator.Advance(startTime + 0.001d, utcNow + 0.001d);

            Assert.That(coordinator.AcceptedBaselineWindowCount, Is.EqualTo(1));

            yield return null;
        }

        private T CreateProfile<T>(string json)
            where T : ScriptableObject
        {
            var profile = ScriptableObject.CreateInstance<T>();
            JsonUtility.FromJsonOverwrite(json, profile);
            return Track(profile);
        }

        private T Track<T>(T value) where T : UnityEngine.Object
        {
            createdObjects.Add(value);
            return value;
        }

        private static PhysiologyWindow CreateWindow(
            double windowEndUtcUnixSeconds,
            double stressScore)
        {
            return new PhysiologyWindow(
                windowEndUtcUnixSeconds,
                windowEndUtcUnixSeconds - 0.2d,
                windowEndUtcUnixSeconds,
                70d,
                35d,
                40d,
                new StressDecision(
                    StressDecisionMode.Point,
                    1,
                    null,
                    null,
                    "mild",
                    0.8d,
                    false,
                    new StressProbabilityVector(0.1d, 0.7d, 0.1d, 0.1d),
                    stressScore),
                0.95d);
        }

        private static EnvironmentState State(float value)
        {
            return new EnvironmentState(value, value, value, value, value);
        }

        private static double UtcNowUnixSeconds()
        {
            return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000d;
        }

        private const string SceneProfileJson = @"{
            ""sceneId"": ""production-test-scene"",
            ""displayName"": ""Production Test Scene"",
            ""researchConfigurationApproved"": true,
            ""defaultIllumination"": 0.5,
            ""defaultWarmth"": 0.5,
            ""defaultAtmosphericSoftness"": 0.5,
            ""defaultColorRichness"": 0.5,
            ""defaultAmbientMotion"": 0.5,
            ""illuminationRange"": { ""x"": 0.0, ""y"": 1.0 },
            ""warmthRange"": { ""x"": 0.0, ""y"": 1.0 },
            ""atmosphericSoftnessRange"": { ""x"": 0.0, ""y"": 1.0 },
            ""colorRichnessRange"": { ""x"": 0.0, ""y"": 1.0 },
            ""ambientMotionRange"": { ""x"": 0.0, ""y"": 1.0 },
            ""illuminationActionStep"": 0.1,
            ""warmthActionStep"": 0.1,
            ""atmosphericSoftnessActionStep"": 0.1,
            ""colorRichnessActionStep"": 0.1,
            ""ambientMotionActionStep"": 0.1,
            ""transitionDurationSeconds"": 0.05,
            ""minimumSecondsBetweenActions"": 0.0
        }";

        private const string TimingProfileJson = @"{
            ""configurationId"": ""production-timing-test"",
            ""configurationVersion"": 1,
            ""researchConfigurationApproved"": true,
            ""acclimatizationDurationSeconds"": 0.1,
            ""adaptiveDurationSeconds"": 1.0,
            ""stabilizationDurationSeconds"": 0.2,
            ""decisionIntervalSeconds"": 0.2
        }";

        private const string PhysiologyProfileJson = @"{
            ""configurationId"": ""production-physiology-test"",
            ""configurationVersion"": 1,
            ""researchConfigurationApproved"": true,
            ""staleAfterSeconds"": 5.0,
            ""minimumWindowDurationSeconds"": 0.1,
            ""maximumFutureClockSkewSeconds"": 1.0,
            ""sourceTimestampToleranceSeconds"": 1.0,
            ""probabilitySumTolerance"": 0.01,
            ""minimumDecisionSignalQuality"": 0.8,
            ""minimumRewardSignalQuality"": 0.8,
            ""maximumBufferedWindows"": 8
        }";

        private const string RewardProfileJson = @"{
            ""configurationId"": ""production-reward-test"",
            ""configurationVersion"": 1,
            ""researchConfigurationApproved"": true,
            ""baselineStandardDeviationMethod"": 0,
            ""minimumBaselineSamples"": 2,
            ""minimumBaselineStandardDeviation"": 0.01,
            ""trendWindowCount"": 2,
            ""minimumTrendSamples"": 2,
            ""settlingSeconds"": 0.0,
            ""maximumAttributionWaitSeconds"": 1.0,
            ""stressWeight"": 1.0,
            ""rmssdWeight"": 0.0,
            ""heartRateWeight"": 0.0,
            ""changePenaltyWeight"": 0.1,
            ""discomfortPenaltyWeight"": 1.0,
            ""safetyPenaltyWeight"": 1.0
        }";

        private const string StabilizationProfileJson = @"{
            ""configurationId"": ""production-stabilization-test"",
            ""configurationVersion"": 1,
            ""researchConfigurationApproved"": true,
            ""recentOutcomeCount"": 2,
            ""rewardRecencyDecay"": 1.0,
            ""preferenceDistancePenaltyWeight"": 0.0
        }";

        private const string TelemetryProfileJson = @"{
            ""configurationId"": ""production-telemetry-test"",
            ""configurationVersion"": 1,
            ""researchConfigurationApproved"": true,
            ""eventSchemaId"": ""production-test-event"",
            ""eventSchemaVersion"": ""1"",
            ""flushEveryEventCount"": 1
        }";

        private const string CoordinatorProfileJson = @"{
            ""configurationId"": ""production-coordinator-test"",
            ""configurationVersion"": 1,
            ""researchConfigurationApproved"": true,
            ""expectedPhysiologyOutputIntervalSeconds"": 0.1,
            ""maximumConsecutiveSameDirectionActions"": 2,
            ""maximumTotalVariation"": 1.0
        }";

        private sealed class RecordingAdapter : MonoBehaviour,
            ISceneEnvironmentAdapter
        {
            public string SceneId => "production-test-scene";

            public EnvironmentState LastAppliedState { get; private set; }

            public SceneBindingValidation ValidateBindings()
            {
                return SceneBindingValidation.Succeeded();
            }

            public void ApplyState(EnvironmentState state)
            {
                LastAppliedState = state;
            }
        }
    }
}
