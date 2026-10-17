using System;
using System.Threading;
using AdaptiveAudioVR.Audio;
using AdaptiveAudioVR.Core;
using AdaptiveAudioVR.RL.Agent;
using LaminarVR.AdaptiveMeditation.Environment;
using LaminarVR.AdaptiveMeditation.Policy;
using LaminarVR.AdaptiveMeditation.Runtime.Application;
using LaminarVR.AdaptiveMeditation.Runtime.Networking;
using UnityEngine;

namespace AdaptiveAudioVR.Integration
{
    [DisallowMultipleComponent]
    public sealed class ProjectMonitorReporter : MonoBehaviour
    {
        private PrototypeBootstrap audioBootstrap;
        private LyriaClipGenerationService generation;
        private ComponentBStressSignalReceiver physiology;
        private AudioSessionPreferenceReceiver preferences;
        private AudioMixerController mixer;
        private SessionRelayBridge relay;
        private ApplicationBootstrap visual;
        private ProductionSessionCoordinator coordinator;
        private ComponentBPhysiologyBridge physiologyBridge;
        private CancellationTokenSource lifetime;
        private bool sending;
        private float nextSend;
        private float lastPhysiologyAt = -1f;
        private int lastPhysiologyCount;

        public void Configure(PrototypeBootstrap bootstrap, LyriaClipGenerationService clips,
            ComponentBStressSignalReceiver receiver, AudioSessionPreferenceReceiver preferenceReceiver,
            AudioMixerController audioMixer)
        {
            audioBootstrap = bootstrap;
            generation = clips;
            physiology = receiver;
            preferences = preferenceReceiver;
            mixer = audioMixer;
        }

        private void OnEnable() => lifetime = new CancellationTokenSource();

        private void OnDisable()
        {
            lifetime?.Cancel();
            lifetime?.Dispose();
            lifetime = null;
        }

        private void Update()
        {
            if (physiology != null && physiology.ReceivedPayloadCount != lastPhysiologyCount)
            {
                lastPhysiologyCount = physiology.ReceivedPayloadCount;
                lastPhysiologyAt = Time.unscaledTime;
            }
            if (sending || Time.unscaledTime < nextSend || audioBootstrap == null || generation == null) return;
            nextSend = Time.unscaledTime + 5f;
            if (relay == null) relay = FindAnyObjectByType<SessionRelayBridge>();
            if (visual == null) visual = FindAnyObjectByType<ApplicationBootstrap>();
            if (coordinator == null) coordinator = FindAnyObjectByType<ProductionSessionCoordinator>();
            if (physiologyBridge == null) physiologyBridge = FindAnyObjectByType<ComponentBPhysiologyBridge>();
            if (relay == null || relay.CurrentConfiguration == null) return;
            SendReport(lifetime.Token);
        }

        private async void SendReport(CancellationToken token)
        {
            sending = true;
            try
            {
                await relay.PublishDiagnosticsAsync(new SessionRuntimeDiagnostics
                {
                    phase = coordinator != null ? coordinator.Phase.ToString() : "unknown",
                    visualPolicy = visual != null ? visual.StudyPolicyMode.ToString() : "unknown",
                    visualInitialized = visual != null && visual.IsInitialized,
                    audioPolicy = audioBootstrap.CurrentRLMode.ToString(),
                    audioPrepared = audioBootstrap.IsPrepared,
                    audioRunning = audioBootstrap.IsSessionRunning,
                    audioPlaying = mixer != null && mixer.IsMeditationPlaying,
                    audioMuted = mixer != null && mixer.IsMuted,
                    generatedClipReady = generation.IsInitialGeneratedClipReady,
                    generating = generation.IsGenerating,
                    audioBackendReachable = generation.IsBackendReachable,
                    audioBackendConfigured = generation.IsBackendConfigured,
                    audioBackendSdkReady = generation.IsBackendSdkReady,
                    audioGenerationFailures = generation.FailedGenerationCount,
                    audioOnlineUpdates = audioBootstrap.CurrentOnlineUpdateCount,
                    physiologySocket = physiologyBridge != null ? physiologyBridge.ConnectionState.ToString() : "unknown",
                    visualPhysiologyWindows = physiologyBridge != null ? physiologyBridge.ForwardedWindowCount : 0,
                    physiologyPayloads = lastPhysiologyCount,
                    physiologyAgeSeconds = lastPhysiologyAt < 0 ? -1 : Time.unscaledTime - lastPhysiologyAt,
                    audioPreferenceError = preferences != null && !string.IsNullOrEmpty(preferences.LastError),
                    pendingTelemetry = relay.PendingTelemetryEventCount,
                    agents = CaptureAgents(),
                }, token);
            }
            catch (Exception)
            {
                // Best effort. Monitoring failures must never interrupt a participant session.
            }
            finally { sending = false; }
        }

        private AgentMonitorSnapshot CaptureAgents()
        {
            var now = (DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
            return new AgentMonitorSnapshot
            {
                capturedAt = now,
                phase = coordinator != null ? coordinator.Phase.ToString() : "unknown",
                coordination = CaptureCoordination(),
                visual = CaptureVisual(now),
                audio = CaptureAudio(now)
            };
        }

        private AgentMonitorCoordination CaptureCoordination()
        {
            var status = coordinator?.SharedAdaptation?.Status;
            if (status == null) return new AgentMonitorCoordination();
            return new AgentMonitorCoordination
            {
                owner = status.Owner.ToString(), stage = status.Stage, reason = status.Reason,
                eligiblePostStartUtc = status.EligiblePostStartUtc,
                remainingSeconds = System.Math.Max(0, status.DeadlineMonotonic - Time.realtimeSinceStartupAsDouble),
                completedTurns = status.CompletedTurns, cancelledTurns = status.CancelledTurns
            };
        }

        private static AgentMonitorState EmptyState(int dimensions) => new AgentMonitorState
        {
            applied = new float[dimensions], target = new float[dimensions],
            decision = new AgentMonitorDecision
            {
                proposedDelta = new float[dimensions], safeDelta = new float[dimensions]
            }
        };

        private static float[] Vector(EnvironmentState value) => new[]
        {
            value.Illumination, value.Warmth, value.AtmosphericSoftness,
            value.ColorRichness, value.AmbientMotion
        };

        private AgentMonitorState CaptureVisual(double now)
        {
            var result = EmptyState(5);
            var environment = visual != null ? visual.EnvironmentManager : null;
            result.available = visual != null && visual.IsInitialized && environment != null;
            result.mode = visual != null ? visual.StudyPolicyMode.ToString() : "unknown";
            result.state = coordinator != null ? coordinator.Phase.ToString() : "unknown";
            if (!result.available) return result;
            result.applied = Vector(environment.CurrentState);
            result.target = Vector(environment.TargetState);
            result.transitioning = environment.IsTransitionActive;
            if (coordinator == null) return result;
            result.updates = visual.Policy != null ? visual.Policy.CaptureState().ModelUpdateCount : 0;
            var input = coordinator.MonitorPhysiology;
            if (input != null)
            {
                result.input = new AgentMonitorInput
                {
                    available = true, source = "ComponentB",
                    stress = input.Stress.ContinuousScore / 3d,
                    confidence = input.Stress.Confidence, quality = input.SignalQuality,
                    ageSeconds = Math.Max(0d, now - input.WindowEndUtcUnixSeconds)
                };
            }
            var cycle = coordinator.MonitorDecision;
            var decision = cycle?.Decision;
            var observation = cycle?.Observation;
            if (decision != null && observation != null)
            {
                var report = result.decision;
                report.available = true;
                report.sequence = decision.PhysiologySequenceNumber;
                report.at = coordinator.MonitorDecisionAt;
                report.stress = observation.Physiology.Window.Stress.ContinuousScore / 3d;
                report.proposed = decision.SelectedAction.ToString();
                report.result = cycle.Value.ResultCode.ToString();
                if (cycle.Value.Validation.HasValue)
                {
                    var validation = cycle.Value.Validation.Value;
                    report.executed = validation.Accepted ? validation.ExecutedAction.ToString() : "Rejected";
                    report.safety = validation.ReasonCode.ToString();
                    var requested = Vector(validation.RequestedTarget);
                    var safe = Vector(validation.SafeTarget);
                    var before = Vector(observation.CurrentEnvironment);
                    for (int i = 0; i < 5; i++)
                    {
                        report.proposedDelta[i] = requested[i] - before[i];
                        report.safeDelta[i] = validation.Accepted ? safe[i] - before[i] : 0f;
                    }
                }
            }
            var reward = coordinator.MonitorReward;
            if (reward.HasValue)
            {
                var value = reward.Value;
                bool valid = value.Calculation.Valid
                    && (value.ResultCode == PolicyRewardCycleResultCode.RewardApplied
                        || value.ResultCode == PolicyRewardCycleResultCode.PolicyUpdateSkipped);
                result.reward = new AgentMonitorReward
                {
                    available = true, valid = valid, at = coordinator.MonitorRewardAt,
                    status = value.ResultCode.ToString(),
                    sequence = valid ? value.Calculation.Breakdown.PreWindowSequenceNumber : 0,
                    action = valid ? value.Calculation.Breakdown.ExecutedAction.ToString() : "unknown",
                    total = valid ? value.Calculation.Breakdown.TotalReward : 0d
                };
            }
            return result;
        }

        private AgentMonitorState CaptureAudio(double now)
        {
            var result = EmptyState(7);
            var agent = audioBootstrap.MonitorAgent;
            // Do not leak an old audio session into a newly paired visual session.
            result.available = agent != null && agent.IsInitialized && mixer != null
                && audioBootstrap.RelaySessionId == relay.CurrentConfiguration.SessionId;
            if (!result.available) return result;
            result.mode = agent.CurrentPolicyMode.ToString();
            result.state = !audioBootstrap.IsSessionRunning ? "Prepared"
                : mixer.IsMuted ? "Muted" : mixer.IsMeditationPlaybackPaused ? "Paused"
                : !mixer.IsMeditationPlaying ? "NotPlaying" : agent.CurrentSafetyMode.ToString();
            result.applied = mixer.CurrentAppliedParameters.ToControlVector();
            result.target = agent.CurrentTargetParameters.ToControlVector();
            result.transitioning = mixer.IsCrossfading;
            for (int i = 0; i < 7; i++)
                result.transitioning |= Mathf.Abs(result.applied[i] - result.target[i]) > 0.001f;
            result.updates = agent.OnlineUpdateCount;
            var signal = agent.CurrentSignal;
            result.input = new AgentMonitorInput
            {
                available = signal.hasPhysiologyWindow || agent.BaselineObservationCount > 0,
                source = signal.hasPhysiologyWindow ? "ComponentB" : "Simulator",
                stress = signal.stress, confidence = signal.confidence, quality = signal.signalQuality,
                ageSeconds = signal.hasPhysiologyWindow
                    ? Math.Max(0d, now - signal.windowEnd) : Math.Max(0d, Time.time - signal.timestamp)
            };
            var decision = agent.MonitorDecision;
            if (decision != null)
            {
                result.decision = new AgentMonitorDecision
                {
                    available = true, sequence = agent.DecisionCount, at = agent.MonitorDecisionAt,
                    stress = decision.state.signal.stress,
                    proposed = AudioActionCatalog.Name(decision.proposedArm),
                    executed = AudioActionCatalog.Name(decision.executedArm),
                    safety = decision.safetyMode, result = "Selected",
                    proposedDelta = decision.proposedPolicyAction.ToArray(),
                    safeDelta = decision.finalSafeAction.ToArray()
                };
            }
            var outcome = agent.MonitorOutcome;
            if (outcome != null)
            {
                result.reward = new AgentMonitorReward
                {
                    available = true, valid = outcome.rewardValid, at = agent.MonitorOutcomeAt,
                    sequence = agent.MonitorOutcomeDecision,
                    action = AudioActionCatalog.Name(outcome.executedArm),
                    status = !outcome.rewardValid ? "Invalid" : outcome.onlineUpdateApplied ? "Updated" : "EvaluationOnly",
                    total = outcome.rewardValid && outcome.reward != null ? outcome.reward.totalReward : 0d
                };
            }
            return result;
        }
    }
}
