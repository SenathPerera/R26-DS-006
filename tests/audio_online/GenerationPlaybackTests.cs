using System;
using System.Reflection;
using AdaptiveAudioVR.Audio;
using AdaptiveAudioVR.Core;
using AdaptiveAudioVR.Integration;
using AdaptiveAudioVR.RL.Agent;
using LaminarVR.AdaptiveMeditation.Coordination;
using UnityEngine;

internal static class GenerationPlaybackTests
{
    private const double Epoch = 1800000000;
    private static readonly BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
    public static void Run(Action<string, Action> run)
    {
        run("PCM marker waits for matching consumed samples and guard, not enqueue", Marker);
        run("Dropped or cleared action PCM cannot confirm playback", LostMarker);
        run("Real PCM queue tracks backlog and invalidates cleared requests", PcmQueue);
        run("Local mix and brightness change without pitch or fabricated density/tempo", Mixer);
        run("Tempo maps to generated BPM with a distinct model configuration", BpmAndIdentity);
        run("Generation-only actions are masked without live generation", OfflineMask);
        run("Generation holds the shared turn until playback, settling and clean outcome", DelayedGeneration);
        run("Cancelled and late generation confirmations cannot unlock a new turn", CancelledGeneration);
        run("Realtime bridge waits for ack, PCM guard and streaming look-ahead", RealtimePlayback);
        run("Cancelled realtime generation reconciles at playback without reward", RealtimeCancellation);
        run("Realtime PCM gaps cancel pending audio rewards", RealtimeGap);
        run("Lost realtime PCM markers freeze learning rather than guessing playback", RealtimeLoss);
        run("Older realtime bridges keep generation-only actions disabled", RealtimeCompatibility);
    }

    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Near(float a, float b) => Check(Math.Abs(a - b) < 0.00001f, a + " != " + b);
    private static void Set(object obj, string field, object value) => obj.GetType().GetField(field, Private).SetValue(obj, value);
    private static void Invoke(object obj, string method) => obj.GetType().GetMethod(method, Private).Invoke(obj, null);

    private static void Marker()
    {
        var marker = new AudioPlaybackMarker();
        marker.Begin("new", 20);
        marker.Enqueued("old", 0); marker.Read(0, 100);
        Check(!marker.Rendered, "Old revision counted");
        marker.Enqueued("new", 100);
        Check(!marker.Rendered, "Queueing counted as playback");
        marker.Read(100, 20); Check(!marker.Rendered, "Guard boundary not consumed");
        marker.Read(120, 1); Check(marker.Rendered && !marker.Lost, "Consumed guard not confirmed");
        marker.Begin("next", 0); marker.Enqueued("new", 121); marker.Read(121, 20);
        Check(!marker.Rendered, "Prior request confirmed next request");
    }

    private static void LostMarker()
    {
        var marker = new AudioPlaybackMarker(); marker.Begin("a", 5); marker.Enqueued("a", 100);
        marker.Dropped(100, 6); marker.Read(106, 100);
        Check(marker.Lost && !marker.Rendered, "Dropped marker counted");
        marker.Begin("b", 0); marker.Enqueued("b", 200); marker.Invalidate(); marker.Read(200, 1);
        Check(marker.Lost && !marker.Rendered, "Buffer clear counted");
    }

    private static void PcmQueue()
    {
        var player = new RealtimePcmAudioPlayer(); Invoke(player, "Awake");
        player.EnqueuePcm16(new byte[20]);
        player.TrackPlaybackRequest("a", 0);
        player.EnqueuePcm16(new byte[20], "a");
        var read = typeof(RealtimePcmAudioPlayer).GetMethod("OnAudioRead", Private);
        read.Invoke(player, new object[] { new float[10] });
        Check(!player.HasRenderedRequest("a", out _), "Existing backlog credited to action");
        read.Invoke(player, new object[] { new float[1] });
        Check(player.HasRenderedRequest("a", out bool lost) && !lost, "Actual queue read not counted");
        player.TrackPlaybackRequest("b", 0); player.EnqueuePcm16(new byte[20], "b"); player.ClearBuffer();
        Check(!player.HasRenderedRequest("b", out lost) && lost, "Clear did not invalidate queue marker");
    }

    private static void Mixer()
    {
        var mixer = new AudioMixerController(); var music = new AudioSource(); var ambience = new AudioSource();
        var filter = new AudioLowPassFilter();
        Set(mixer, "currentMeditationSource", music); Set(mixer, "ambientSource", ambience);
        Set(mixer, "currentMeditationLowPass", filter);
        mixer.SetGeneratedControls(new AudioParameters { density = 0.3f, tempo = 0.2f });
        mixer.SetTargetParameters(new AudioParameters { intensity = 0.8f, brightness = 0.7f, density = 0.9f,
            tempo = 0.9f, musicMix = 0.6f, ambientMix = 0.4f });
        Time.deltaTime = 1; Invoke(mixer, "Update");
        Near(music.pitch, 1); Near(ambience.pitch, 1);
        Near(mixer.CurrentAppliedParameters.tempo, 0.2f); Near(mixer.CurrentAppliedParameters.density, 0.3f);
        Check(music.volume > 0 && ambience.volume > 0 && filter.cutoffFrequency > 1200, "Local controls stopped working");
        mixer.SetGeneratedControls(new AudioParameters { density = 0.6f, tempo = 0.5f }); Invoke(mixer, "Update");
        Near(mixer.CurrentAppliedParameters.tempo, 0.5f); Near(mixer.CurrentAppliedParameters.density, 0.6f);
        Near(music.pitch, 1); Near(ambience.pitch, 1);
    }

    private static SignalPacket Window(double end, long seq) => new SignalPacket(0.7f, 0.9f, 0.95f, 78, 35, 40,
        Epoch + end, Epoch + end - 60, Epoch + end, seq, (float)end);
    private static void Tick(AudioRLAgent agent, double now, double end, long seq)
    {
        Time.time = (float)now; Time.realtimeSinceStartupAsDouble = now;
        agent.EvaluateAt(Window(end, seq), 1, true, now, Epoch + now);
    }
    private static AudioRLAgent Prepared(bool online, out AdaptationCoordinator coordinator)
    {
        var agent = new AudioRLAgent(); Set(agent, "persistPersonalizedModel", false);
        agent.SetPolicyMode(AudioRLPolicyMode.PersonalizedBandit);
        agent.Initialize(new AudioProfile { userId = "generation-test", baseIntensity = 0.4f, baseDensity = 0.4f,
            baseBrightness = 0.4f, baseTempo = 0.3f, baseFade = 0.7f, baseMusicMix = 0.45f, baseAmbientMix = 0.55f },
            PersonalizationStrategy.CreateNeutral());
        coordinator = new AdaptationCoordinator(); agent.BindCoordination(coordinator);
        agent.SetGenerationPlaybackAvailable(online); Tick(agent, 60, 60, 1);
        var policy = (AudioOnlinePolicy)typeof(AudioRLAgent).GetField("policy", Private).GetValue(agent);
        var state = (AudioRLState)typeof(AudioRLAgent).GetMethod("BuildState", Private).Invoke(agent, new object[] { Window(120, 2) });
        // Synthetic fixture strongly prefers density. Runtime must still enforce execution eligibility.
        policy.Model.Observe(3, AudioBanditFeatures.Encode(state, 0.08f), 1);
        Tick(agent, 120, 120, 2);
        return agent;
    }

    private static void BpmAndIdentity()
    {
        Check(AudioMusicalMapping.Bpm(0) == 60 && AudioMusicalMapping.Bpm(1) == 112, "Wrong BPM endpoints");
        Check(AudioMusicalMapping.Bpm(0.2f) != AudioMusicalMapping.Bpm(0.23f), "Real tempo action did not change BPM");
        Check(AudioMusicalMapping.Bpm(0, -50) == 60 && AudioMusicalMapping.Bpm(1, 200) == 200, "BPM out of provider range");
        var a = Prepared(false, out _); string offline = a.ModelPath;
        a.SetGenerationPlaybackAvailable(true); Tick(a, 121, 120, 2);
        Check(a.ModelPath != offline, "Pitch-era or offline weights entered generation model");
    }

    private static void OfflineMask()
    {
        var a = Prepared(false, out _);
        Check(a.DecisionCount == 1 && !a.TryGetGenerationAction(out _, out _), "Generation requested offline");
        Near(a.CurrentProposedAction.deltaDensity, 0); Near(a.CurrentProposedAction.deltaTempo, 0);
    }

    private static void DelayedGeneration()
    {
        var a = Prepared(true, out var c); long count = a.OnlineUpdateCount;
        Check(a.TryGetGenerationAction(out string id, out var target), "Fixture did not select density");
        Near(a.CurrentParameters.density, 0.4f);
        Tick(a, 180, 180, 3);
        Check(a.OnlineUpdateCount == count && c.Status.Stage == "Transitioning", "Learned before playback");
        Check(!c.TryBeginContent("other-generation", 180, 90, out _), "Separate content bypassed audio turn");
        Check(a.ConfirmGenerationPlayback(id), "Matching played revision rejected");
        Near(a.CurrentParameters.density, target.density);
        Tick(a, 181, 180, 3); Tick(a, 240, 240, 4);
        Check(a.OnlineUpdateCount == count && c.Status.Owner == AdaptationOwner.Audio, "Settling/overlapping window credited");
        Tick(a, 300, 300, 5);
        Check(a.OnlineUpdateCount == count + 1 && c.Status.CompletedTurns == 1, "Clean generated outcome did not learn once");
        Check(!a.ConfirmGenerationPlayback(id), "Completed revision reused");
    }

    private static void CancelledGeneration()
    {
        var a = Prepared(true, out var c); long count = a.OnlineUpdateCount;
        Check(a.TryGetGenerationAction(out string old, out var target), "No generation action");
        a.FailGenerationAction(old, "Transport failed");
        Near(a.CurrentTargetParameters.density, a.CurrentParameters.density);
        Check(!a.ConfirmGenerationPlayback(old) && a.OnlineUpdateCount == count, "Cancelled action trained");
        Tick(a, 240, 240, 4);
        Check(a.TryGetGenerationAction(out string current, out _) && current != old, "No new action after cancellation");
        Check(!a.ConfirmGenerationPlayback(old) && c.Status.Owner == AdaptationOwner.Audio, "Late confirmation unlocked newer turn");
        a.FailGenerationAction(old, "Late failure"); Check(a.IsPendingGenerationAction(current), "Late failure cancelled newer turn");
        a.FailGenerationAction(current, "Cancelled during playback");
        a.ReconcileGeneratedPlayback(target);
        Near(a.CurrentParameters.density, target.density); Near(a.CurrentTargetParameters.density, target.density);
        Check(a.OnlineUpdateCount == count, "Reconciled cancelled action trained");
    }

    private sealed class StreamFixture
    {
        public AudioRLAgent Agent;
        public LyriaRealtimeStreamingService Service;
        public RealtimePcmAudioPlayer Pcm;
        public PrototypeBootstrap Bootstrap;
        public string Id;
        public AudioParameters Target;
    }

    private static StreamFixture Stream()
    {
        var agent = Prepared(true, out _);
        Check(agent.TryGetGenerationAction(out string id, out var target), "No streaming fixture action");
        var pcm = new RealtimePcmAudioPlayer(); Invoke(pcm, "Awake");
        Set(pcm, "streamingClip", new AudioClip { length = 2 });
        var mixer = new AudioMixerController();
        Set(mixer, "currentMeditationSource", new AudioSource { clip = pcm.StreamingClip, isPlaying = true });
        mixer.SetGeneratedControls(agent.CurrentParameters);
        var bootstrap = new PrototypeBootstrap { MonitorAgent = agent };
        var service = new LyriaRealtimeStreamingService();
        Set(service, "bootstrap", bootstrap); Set(service, "audioMixerController", mixer); Set(service, "pcmAudioPlayer", pcm);
        Set(service, "<IsConnected>k__BackingField", true); Set(service, "<IsStreaming>k__BackingField", true);
        Set(service, "<HasBufferedPlaybackStarted>k__BackingField", true);
        var dtoType = typeof(LyriaRealtimeStreamingService).GetNestedType("RealtimeClientMessage", BindingFlags.NonPublic);
        Set(service, "activeGenerationMessage", Activator.CreateInstance(dtoType, true));
        Set(service, "backendTracksActionPlayback", true); Set(service, "submittedGenerationId", id);
        Set(service, "generationTarget", target);
        ((System.Collections.Generic.HashSet<string>)typeof(LyriaRealtimeStreamingService)
            .GetField("issuedGenerationRequests", Private).GetValue(service)).Add(id);
        pcm.TrackPlaybackRequest(id, 2);
        pcm.EnqueuePcm16(new byte[48000 * 2 * 3 * 2], id);
        return new StreamFixture { Agent = agent, Service = service, Pcm = pcm, Bootstrap = bootstrap, Id = id, Target = target };
    }

    private static void Receive(StreamFixture f, string type = "state", string state = "config_updated", string protocol = "")
    {
        var dtoType = typeof(LyriaRealtimeStreamingService).GetNestedType("RealtimeInboundMessage", BindingFlags.NonPublic);
        var message = Activator.CreateInstance(dtoType, true);
        dtoType.GetField("type").SetValue(message, type); dtoType.GetField("state").SetValue(message, state);
        dtoType.GetField("requestId").SetValue(message, f.Id); dtoType.GetField("actionPlaybackProtocol").SetValue(message, protocol);
        typeof(LyriaRealtimeStreamingService).GetMethod("HandleInboundMessage", Private).Invoke(f.Service, new[] { message });
    }

    private static void Read(StreamFixture f, int samples) => typeof(RealtimePcmAudioPlayer).GetMethod("OnAudioRead", Private)
        .Invoke(f.Pcm, new object[] { new float[samples] });
    private static void Update(StreamFixture f, double time)
    {
        Time.realtimeSinceStartupAsDouble = time;
        Invoke(f.Service, "UpdateGenerationPlayback");
    }

    private static void RealtimePlayback()
    {
        var f = Stream(); long count = f.Agent.OnlineUpdateCount;
        Read(f, 192001); Update(f, 120);
        Check(f.Agent.TryGetGenerationAction(out _, out _), "PCM without ack confirmed action");
        Receive(f); Update(f, 120); Update(f, 122);
        Check(f.Agent.TryGetGenerationAction(out _, out _), "Speaker look-ahead was skipped");
        Update(f, 122.2);
        Near(f.Agent.CurrentParameters.density, f.Target.density);
        Check(!f.Agent.TryGetGenerationAction(out _, out _) && f.Agent.HasPendingOutcome, "Playback did not lead to delayed measurement");
        Check(f.Agent.OnlineUpdateCount == count, "Playback confirmation itself trained policy");
        Receive(f); Update(f, 123);
        Check(f.Agent.HasPendingOutcome, "Duplicate ack invalidated a valid measurement");
    }

    private static void RealtimeCancellation()
    {
        var f = Stream(); long count = f.Agent.OnlineUpdateCount;
        f.Agent.FailGenerationAction(f.Id, "Signal dropped");
        Receive(f); Update(f, 120);
        Check(!f.Bootstrap.PlaybackReady, "Cancelled action allowed peer attribution before playback");
        Read(f, 192001); Update(f, 121); Update(f, 123.2);
        Check(f.Bootstrap.PlaybackReady && !f.Agent.HasPendingOutcome, "Cancelled action did not reconcile cleanly");
        Near(f.Agent.CurrentParameters.density, f.Target.density);
        Near(f.Agent.CurrentTargetParameters.density, f.Target.density);
        Check(f.Agent.OnlineUpdateCount == count, "Cancelled request assigned reward");
    }

    private static void RealtimeGap()
    {
        var f = Stream(); Receive(f);
        Read(f, 288010); Update(f, 120);
        Check(f.Pcm.UnderflowSampleCount == 10 && !f.Agent.HasPendingOutcome && !f.Bootstrap.PlaybackReady,
            "PCM gap left reward measurement running");
    }

    private static void RealtimeLoss()
    {
        var f = Stream(); Receive(f); f.Pcm.ClearBuffer(); Update(f, 120);
        Check(!f.Agent.HasPendingOutcome && !f.Bootstrap.PlaybackReady, "Lost marker was attributed");
        Check((bool)typeof(LyriaRealtimeStreamingService).GetField("generationFaulted", Private).GetValue(f.Service),
            "Irrecoverable marker failure did not require restart");
    }

    private static void RealtimeCompatibility()
    {
        var f = Stream(); Set(f.Service, "submittedGenerationId", null);
        Receive(f, type: "connected"); Update(f, 120);
        Check(!f.Agent.GenerationPlaybackAvailable && f.Service.LastWarningMessage.Contains("density/tempo actions disabled"),
            "Old bridge admitted unconfirmable generation action");
    }
}
