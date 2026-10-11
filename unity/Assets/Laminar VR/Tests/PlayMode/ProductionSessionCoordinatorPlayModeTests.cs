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
using LaminarVR.AdaptiveMeditation.Telemetry;
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
                    CoordinatorProfileJson),
                CreateProfile<PreferenceInitializationProfile>(
                    PreferenceProfileJson));
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
        public IEnumerator Coordinator_AppliesApprovedPreferenceBlendAtInitialization()
        {
            var root = Track(new GameObject("PreferenceInitializationHarness"));
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
                    CoordinatorProfileJson),
                CreateProfile<PreferenceInitializationProfile>(
                    PreferenceProfileJson));
            var preference = new EnvironmentState(
                0.7f,
                0.3f,
                0.6f,
                0.2f,
                0.8f);
            var expectedInitial = new EnvironmentState(
                0.63f,
                0.37f,
                0.565f,
                0.305f,
                0.695f);
            coordinator.ConfigureSessionContext(
                "playmode-preference-" + Guid.NewGuid().ToString("N"),
                "P-PLAYMODE-PREFERENCE",
                preference);

            Assert.That(
                coordinator.TryInitialize(out var validationError),
                Is.True,
                validationError);
            telemetryFilePath = coordinator.TelemetryFilePath;
            Assert.That(
                bootstrap.EnvironmentManager.IsTransitionActive,
                Is.True);
            AssertStateEqual(
                bootstrap.EnvironmentManager.TargetState,
                expectedInitial);

            var now = Time.realtimeSinceStartupAsDouble;
            coordinator.Advance(now + 0.1d, UtcNowUnixSeconds() + 0.1d);

            Assert.That(
                bootstrap.EnvironmentManager.IsTransitionActive,
                Is.False);
            AssertStateEqual(
                bootstrap.EnvironmentManager.CurrentState,
                expectedInitial);
            AssertStateEqual(adapter.LastAppliedState, expectedInitial);

            yield return null;
        }

        [UnityTest]
        public IEnumerator Coordinator_EmitsCompleteStartupAndPhysiologyTelemetry()
        {
            var root = Track(new GameObject("TelemetryCoverageHarness"));
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
                    CoordinatorProfileJson),
                CreateProfile<PreferenceInitializationProfile>(
                    PreferenceProfileJson));
            coordinator.ConfigureSessionContext(
                "playmode-telemetry-" + Guid.NewGuid().ToString("N"),
                "P-PLAYMODE-TELEMETRY",
                State(0.7f));

            Assert.That(
                coordinator.TryInitialize(out var validationError),
                Is.True,
                validationError);
            telemetryFilePath = coordinator.TelemetryFilePath;

            var startTime = Time.realtimeSinceStartupAsDouble;
            var utcNow = UtcNowUnixSeconds();
            coordinator.SubmitPhysiology(
                CreateWindow(utcNow - 0.5d, 1.4d));
            coordinator.SubmitPhysiology(
                CreateInvalidWindow(utcNow - 0.25d));
            Assert.That(
                coordinator.ReportTelemetryTransferSucceeded(
                    "ack-telemetry-coverage",
                    4,
                    10L,
                    13L),
                Is.True);
            Assert.That(
                coordinator.ReportTelemetryTransferFailed(
                    "telemetry-publish-failed:Test",
                    2,
                    14L,
                    15L),
                Is.True);

            var events = new List<TelemetryEvent>();
            for (var frame = 0; frame < 160; frame++)
            {
                yield return null;
                var offset = 0.1d + (frame * 0.001d);
                coordinator.Advance(startTime + offset, utcNow + offset);
                while (coordinator.TryDequeue(out var telemetryEvent))
                {
                    events.Add(telemetryEvent);
                }
            }

            AssertEventPresent(events, TelemetryEventTypes.ApplicationStarted);
            AssertEventPresent(events, TelemetryEventTypes.ApplicationVersion);
            AssertEventPresent(events, TelemetryEventTypes.DeviceInformation);
            AssertEventPresent(events, TelemetryEventTypes.SceneLoadStarted);
            var bindingEvent = AssertEventPresent(
                events,
                TelemetryEventTypes.SceneBindingValidation);
            Assert.That(
                FindField(bindingEvent, "valid").BooleanValue,
                Is.True);
            AssertEventPresent(events, TelemetryEventTypes.SceneLoadCompleted);
            AssertEventPresent(events, TelemetryEventTypes.PerformanceSample);

            var accepted = AssertEventPresent(
                events,
                TelemetryEventTypes.PhysiologyReceived);
            Assert.That(
                FindField(accepted, "heart_rate_bpm").NumberValue,
                Is.EqualTo(70d));
            Assert.That(
                FindField(accepted, "continuous_stress_score").NumberValue,
                Is.EqualTo(1.4d));

            var rejected = AssertEventPresent(
                events,
                TelemetryEventTypes.PhysiologyRejected);
            Assert.That(
                FindField(rejected, "heart_rate_bpm").ValueType,
                Is.EqualTo(TelemetryFieldValueType.Null));
            Assert.That(
                FindField(rejected, "validation_reason").StringValue,
                Is.EqualTo("HeartRateInvalid"));

            var transitionCompleted = AssertEventPresent(
                events,
                TelemetryEventTypes.TransitionCompleted);
            Assert.That(
                FindField(
                    transitionCompleted,
                    "transition_reason").StringValue,
                Is.EqualTo("participant_preference_initialization"));

            Assert.That(
                events.Exists(
                    telemetryEvent => telemetryEvent.EventType
                        == TelemetryEventTypes.LocalLogUploadSucceeded),
                Is.False);
            Assert.That(
                events.Exists(
                    telemetryEvent => telemetryEvent.EventType
                        == TelemetryEventTypes.LocalLogUploadFailed),
                Is.False);
            var durableJsonLines = ReadTextFromActiveTelemetryFile(
                telemetryFilePath);
            Assert.That(
                durableJsonLines,
                Does.Contain(
                    "\"eventType\":\"local_log.upload_succeeded\"")
                    .And.Contain("ack-telemetry-coverage")
                    .And.Contain(
                        "\"eventType\":\"local_log.upload_failed\"")
                    .And.Contain("telemetry-publish-failed:Test"));
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
                    CoordinatorProfileJson),
                CreateProfile<PreferenceInitializationProfile>(
                    PreferenceProfileJson));

            var boundary = root.AddComponent<VisualSessionBoundary>();
            boundary.enabled = false;
            boundary.Configure(coordinator);

            var utcNow = UtcNowUnixSeconds();
            boundary.ReceiveSessionContext(
                "playmode-boundary-" + Guid.NewGuid().ToString("N"),
                "P-PLAYMODE-BOUNDARY",
                State(0.5f),
                CreateSessionContext());
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

        [UnityTest]
        public IEnumerator LocalEmergencyStop_AbortsWithoutNetworkTransport()
        {
            var root = Track(new GameObject("LocalEmergencyStopHarness"));
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
                    CoordinatorProfileJson),
                CreateProfile<PreferenceInitializationProfile>(
                    PreferenceProfileJson));

            var boundary = root.AddComponent<VisualSessionBoundary>();
            boundary.enabled = false;
            boundary.Configure(coordinator);
            boundary.ReceiveSessionContext(
                "playmode-local-emergency-" + Guid.NewGuid().ToString("N"),
                "P-PLAYMODE-LOCAL-EMERGENCY",
                State(0.5f),
                CreateSessionContext());
            Assert.That(boundary.ProcessPendingMessages(), Is.EqualTo(1));
            Assert.That(
                coordinator.TryInitialize(out var validationError),
                Is.True,
                validationError);
            telemetryFilePath = coordinator.TelemetryFilePath;

            var startTime = Time.realtimeSinceStartupAsDouble;
            var utcNow = UtcNowUnixSeconds();
            coordinator.SubmitCommand("start", SessionCommandType.Start);
            coordinator.Advance(startTime, utcNow);
            Assert.That(
                coordinator.Phase,
                Is.EqualTo(VrSessionPhase.Acclimatization));

            var localEmergency = root.AddComponent<
                LocalEmergencyStopController>();
            localEmergency.enabled = false;
            localEmergency.Configure(boundary);

            Assert.That(localEmergency.TryTriggerEmergencyStop(), Is.True);
            Assert.That(localEmergency.HasTriggered, Is.True);
            Assert.That(localEmergency.LastCommandId,
                Does.StartWith("local-emergency-stop-"));
            Assert.That(localEmergency.TryTriggerEmergencyStop(), Is.False);
            Assert.That(boundary.PendingMessageCount, Is.EqualTo(1));
            Assert.That(boundary.ProcessPendingMessages(), Is.EqualTo(1));

            coordinator.Advance(startTime + 0.001d, utcNow + 0.001d);

            Assert.That(coordinator.LastCommandResult.Applied, Is.True);
            Assert.That(
                coordinator.LastCommandResult.CommandType,
                Is.EqualTo(SessionCommandType.EmergencyStop));
            Assert.That(coordinator.Phase, Is.EqualTo(VrSessionPhase.Aborted));

            var events = new List<TelemetryEvent>();
            for (var frame = 0; frame < 80; frame++)
            {
                yield return null;
                coordinator.Advance(
                    startTime + 0.002d + (frame * 0.001d),
                    utcNow + 0.002d + (frame * 0.001d));
                while (coordinator.TryDequeue(out var telemetryEvent))
                {
                    events.Add(telemetryEvent);
                }
            }

            var emergencyEvent = AssertEventPresent(
                events,
                TelemetryEventTypes.EmergencyStop);
            Assert.That(
                FindField(emergencyEvent, "terminal_phase").StringValue,
                Is.EqualTo("Aborted"));
            Assert.That(
                FindField(emergencyEvent, "reason").StringValue,
                Is.EqualTo("EmergencyStopCommand"));
            Assert.That(
                FindField(emergencyEvent, "final_state_illumination")
                    .NumberValue,
                Is.EqualTo(0.5d).Within(0.0001d));

            yield return null;
        }

        [UnityTest]
        public IEnumerator HeadsetLifecycleLoss_PausesAdaptiveSessionAndRequiresFreshResume()
        {
            var root = Track(new GameObject("HeadsetLifecycleAdaptiveHarness"));
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
                    CoordinatorProfileJson),
                CreateProfile<PreferenceInitializationProfile>(
                    PreferenceProfileJson));
            coordinator.ConfigureSessionContext(
                "playmode-headset-lifecycle-" + Guid.NewGuid().ToString("N"),
                "P-PLAYMODE-HEADSET-LIFECYCLE",
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
            coordinator.SubmitPhysiology(CreateWindow(utcNow - 1d, 1.5d));
            coordinator.SubmitPhysiology(CreateWindow(utcNow - 0.5d, 1.4d));
            coordinator.Advance(startTime + 0.01d, utcNow);
            coordinator.Advance(startTime + 0.11d, utcNow + 0.1d);
            Assert.That(coordinator.Phase, Is.EqualTo(VrSessionPhase.Adaptive));

            Assert.That(
                coordinator.SuspendForHeadsetLifecycle(
                    HeadsetLifecycleInterruptionReason.ApplicationFocusLost,
                    startTime + 0.12d,
                    utcNow + 0.11d),
                Is.True);
            Assert.That(coordinator.IsHeadsetLifecycleSuspended, Is.True);
            Assert.That(coordinator.Phase, Is.EqualTo(VrSessionPhase.Paused));
            Assert.That(
                coordinator.LastCommandResult.CommandType,
                Is.EqualTo(SessionCommandType.Pause));

            coordinator.Advance(startTime + 1d, utcNow + 0.99d);
            Assert.That(coordinator.Phase, Is.EqualTo(VrSessionPhase.Paused));

            Assert.That(
                coordinator.RestoreHeadsetLifecycle(
                    startTime + 1d,
                    utcNow + 0.99d),
                Is.True);
            Assert.That(coordinator.IsHeadsetLifecycleSuspended, Is.False);
            Assert.That(coordinator.Phase, Is.EqualTo(VrSessionPhase.Paused));

            coordinator.SubmitCommand(
                "resume-without-fresh-window",
                SessionCommandType.Resume);
            coordinator.Advance(startTime + 1.001d, utcNow + 0.991d);
            Assert.That(
                coordinator.LastCommandResult.ResultCode,
                Is.EqualTo(SessionCommandResultCode.FreshPhysiologyRequired));
            Assert.That(coordinator.Phase, Is.EqualTo(VrSessionPhase.Paused));

            var freshUtc = UtcNowUnixSeconds();
            coordinator.SubmitPhysiology(
                CreateWindow(freshUtc - 0.05d, 1.3d));
            coordinator.Advance(startTime + 1.002d, freshUtc);
            coordinator.SubmitCommand(
                "resume-with-fresh-window",
                SessionCommandType.Resume);
            coordinator.Advance(startTime + 1.003d, freshUtc + 0.001d);

            Assert.That(coordinator.LastCommandResult.Applied, Is.True);
            Assert.That(coordinator.Phase, Is.EqualTo(VrSessionPhase.Adaptive));

            for (var frame = 0; frame < 40; frame++)
            {
                yield return null;
                coordinator.Advance(
                    startTime + 1.003d,
                    freshUtc + 0.001d);
            }

            var recordedEventTypes = new List<string>();
            while (coordinator.TryDequeue(out var telemetryEvent))
            {
                recordedEventTypes.Add(telemetryEvent.EventType);
            }

            CollectionAssert.Contains(
                recordedEventTypes,
                "application.focus_lost");
            CollectionAssert.Contains(
                recordedEventTypes,
                "application.focus_restored");
        }

        [UnityTest]
        public IEnumerator HeadsetLifecycleLoss_FreezesAcclimatizationClock()
        {
            var root = Track(new GameObject("HeadsetLifecycleClockHarness"));
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
                    CoordinatorProfileJson),
                CreateProfile<PreferenceInitializationProfile>(
                    PreferenceProfileJson));
            coordinator.ConfigureSessionContext(
                "playmode-headset-clock-" + Guid.NewGuid().ToString("N"),
                "P-PLAYMODE-HEADSET-CLOCK",
                State(0.5f));

            Assert.That(
                coordinator.TryInitialize(out var validationError),
                Is.True,
                validationError);
            telemetryFilePath = coordinator.TelemetryFilePath;

            var startTime = Time.realtimeSinceStartupAsDouble;
            var utcNow = UtcNowUnixSeconds();
            coordinator.SubmitCommand("start", SessionCommandType.Start);
            coordinator.Advance(startTime, utcNow);
            coordinator.SuspendForHeadsetLifecycle(
                HeadsetLifecycleInterruptionReason.ApplicationPaused,
                startTime + 0.01d,
                utcNow + 0.01d);

            coordinator.Advance(startTime + 10d, utcNow + 10d);
            Assert.That(
                coordinator.Phase,
                Is.EqualTo(VrSessionPhase.Acclimatization));

            coordinator.RestoreHeadsetLifecycle(
                startTime + 10d,
                utcNow + 10d);
            coordinator.Advance(startTime + 10.05d, utcNow + 10.05d);

            Assert.That(
                coordinator.Phase,
                Is.EqualTo(VrSessionPhase.Acclimatization));
            yield return null;
        }

        [Test]
        public void HeadsetLifecycleController_CoalescesFocusAndPauseCallbacks()
        {
            var root = Track(new GameObject("HeadsetLifecycleControllerHarness"));
            var coordinator = root.AddComponent<ProductionSessionCoordinator>();
            coordinator.enabled = false;
            var controller = root.AddComponent<HeadsetLifecycleSafetyController>();
            controller.enabled = false;
            controller.Configure(coordinator);

            controller.SetApplicationFocus(false);
            Assert.That(controller.IsLifecycleUnavailable, Is.True);
            Assert.That(coordinator.IsHeadsetLifecycleSuspended, Is.True);

            controller.SetApplicationPaused(true);
            controller.SetApplicationFocus(true);
            Assert.That(controller.IsLifecycleUnavailable, Is.True);
            Assert.That(coordinator.IsHeadsetLifecycleSuspended, Is.True);

            controller.SetApplicationPaused(false);
            Assert.That(controller.IsLifecycleUnavailable, Is.False);
            Assert.That(coordinator.IsHeadsetLifecycleSuspended, Is.False);
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

        private static PhysiologyWindow CreateInvalidWindow(
            double windowEndUtcUnixSeconds)
        {
            return new PhysiologyWindow(
                windowEndUtcUnixSeconds,
                windowEndUtcUnixSeconds - 0.2d,
                windowEndUtcUnixSeconds,
                double.NaN,
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
                    new StressProbabilityVector(
                        0.1d,
                        0.7d,
                        0.1d,
                        0.1d),
                    1.4d),
                0.95d);
        }

        private static TelemetryEvent AssertEventPresent(
            IReadOnlyList<TelemetryEvent> events,
            string eventType)
        {
            for (var index = 0; index < events.Count; index++)
            {
                if (events[index].EventType == eventType)
                {
                    return events[index];
                }
            }

            Assert.Fail("Missing telemetry event: " + eventType);
            return null;
        }

        private static TelemetryField FindField(
            TelemetryEvent telemetryEvent,
            string fieldName)
        {
            for (var index = 0;
                index < telemetryEvent.FieldCount;
                index++)
            {
                var field = telemetryEvent.GetField(index);
                if (field.Name == fieldName)
                {
                    return field;
                }
            }

            Assert.Fail(
                "Missing telemetry field '"
                + fieldName
                + "' on "
                + telemetryEvent.EventType
                + ".");
            return default;
        }

        private static string ReadTextFromActiveTelemetryFile(
            string filePath)
        {
            using (var stream = new FileStream(
                       filePath,
                       FileMode.Open,
                       FileAccess.Read,
                       FileShare.ReadWrite | FileShare.Delete))
            using (var reader = new StreamReader(stream))
            {
                return reader.ReadToEnd();
            }
        }

        private static EnvironmentState State(float value)
        {
            return new EnvironmentState(value, value, value, value, value);
        }

        private static void AssertStateEqual(
            EnvironmentState actual,
            EnvironmentState expected)
        {
            Assert.That(
                actual.Illumination,
                Is.EqualTo(expected.Illumination).Within(1e-6f));
            Assert.That(
                actual.Warmth,
                Is.EqualTo(expected.Warmth).Within(1e-6f));
            Assert.That(
                actual.AtmosphericSoftness,
                Is.EqualTo(expected.AtmosphericSoftness).Within(1e-6f));
            Assert.That(
                actual.ColorRichness,
                Is.EqualTo(expected.ColorRichness).Within(1e-6f));
            Assert.That(
                actual.AmbientMotion,
                Is.EqualTo(expected.AmbientMotion).Within(1e-6f));
        }

        private static SessionContextSnapshot CreateSessionContext()
        {
            return new SessionContextSnapshot(
                SessionContextSnapshot.SupportedSchemaVersion,
                "2026-10-10T08:30:00.000Z",
                6d,
                -0.2d,
                0.7d,
                0.4d,
                false,
                510,
                2,
                3);
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
            ""discomfortPenaltyWeight"": 0.0,
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

        private const string PreferenceProfileJson = @"{
            ""configurationId"": ""production-preference-test"",
            ""configurationVersion"": 1,
            ""researchConfigurationApproved"": true,
            ""preferenceWeight"": 0.65
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
