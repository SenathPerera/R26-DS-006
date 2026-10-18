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
        run("Generation PCM blends stereo tails once without prematurely confirming playback", GenerationBlend);
        run("Equal-power source crossfade overlaps without a midpoint power dip", EqualPowerFade);
        run("Realtime automatic lifecycle respects readiness, manual stop and session changes", AutomaticLifecycle);
        run("Realtime retry delay is bounded and includes fallback crossfade time", RetryDelay);
        run("Stale socket callbacks cannot affect a new realtime connection", StaleConnection);
        run("Realtime waits for startup buffering before acquiring playback ownership", StartupBuffer);
        run("Failed stream attachment can fade back to the still-playing original clip", ReverseCrossfade);
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

    private static byte[] PcmBytes(int samples, short value)
    {
        var data = new byte[samples * 2];
        for (int i = 0; i < samples; i++) { data[i * 2] = (byte)value; data[i * 2 + 1] = (byte)(value >> 8); }
        return data;
    }

    private static void GenerationBlend()
    {
        var pcm = new RealtimePcmAudioPlayer(); Invoke(pcm, "Awake");
        pcm.EnqueuePcm16(PcmBytes(384000, 3276));
        pcm.TrackPlaybackRequest("next", 2);
        pcm.EnqueuePcm16(PcmBytes(192000, 13107), "next");
        Check(pcm.BlendedSampleCount == 96000, "Did not blend one second of future stereo PCM");
        Near(pcm.BufferedSeconds, 5);
        var read = typeof(RealtimePcmAudioPlayer).GetMethod("OnAudioRead", Private);
        var output = new float[480000]; read.Invoke(pcm, new object[] { output });
        Check(output[336000] > 0.35f && output[336000] < 0.36f, "Crossfade midpoint not equal-power");
        Near(output[336000], output[336001]);
        Check(!pcm.HasRenderedRequest("next", out _), "Blend skipped the post-blend playback guard");
        pcm.EnqueuePcm16(PcmBytes(192000, 13107), "next");
        Check(pcm.BlendedSampleCount == 96000, "Every chunk repeated the transition");
        read.Invoke(pcm, new object[] { new float[2] });
        Check(pcm.HasRenderedRequest("next", out _), "Consumed blended request never confirmed");
    }

    private static void EqualPowerFade()
    {
        var mixer = new AudioMixerController();
        Set(mixer, "currentMeditationSource", new AudioSource { clip = new AudioClip(), isPlaying = true });
        Set(mixer, "transitionMeditationSource", new AudioSource());
        var routine = (System.Collections.IEnumerator)typeof(AudioMixerController).GetMethod("CrossfadeMeditationCoroutine", Private)
            .Invoke(mixer, new object[] { new AudioClip(), 8f, true });
        Time.deltaTime = 4; routine.MoveNext();
        float a = (float)typeof(AudioMixerController).GetField("currentMeditationBlend", Private).GetValue(mixer);
        float b = (float)typeof(AudioMixerController).GetField("transitionMeditationBlend", Private).GetValue(mixer);
        Near(a * a + b * b, 1); Check(a > 0.7f && b > 0.7f, "Linear midpoint fade remained");
    }

    private static void AutomaticLifecycle()
    {
        var f = Stream();
        Set(f.Service, "sessionWasRunning", true); Set(f.Service, "observedSessionId", "test-session");
        Set(f.Service, "manuallyStopped", true);
        Invoke(f.Service, "UpdateAutomaticPlayback");
        Check(f.Service.IsConnected, "Manual stop triggered unexpected automatic work");
        f.Bootstrap.CanPlayRealtimeMusic = false; Set(f.Service, "manuallyStopped", false);
        Invoke(f.Service, "UpdateAutomaticPlayback");
        Check(!f.Service.IsConnected && !f.Agent.HasPendingOutcome, "Session pause did not stop streaming and reward attribution");
        Set(f.Service, "manuallyStopped", true); f.Bootstrap.IsPrepared = false;
        f.Bootstrap.CanPlayRealtimeMusic = true; f.Bootstrap.RelaySessionId = "new-session";
        Invoke(f.Service, "UpdateAutomaticPlayback");
        Check(!(bool)typeof(LyriaRealtimeStreamingService).GetField("manuallyStopped", Private).GetValue(f.Service),
            "Prior participant's manual stop suppressed a new session");
    }

    private static void RetryDelay()
    {
        var f = Stream(); Time.realtimeSinceStartupAsDouble = 120;
        for (int i = 0; i < 10; i++) Invoke(f.Service, "ScheduleReconnect");
        double retry = (double)typeof(LyriaRealtimeStreamingService).GetField("nextAutomaticAttemptAt", Private).GetValue(f.Service);
        Check(retry == 150, "Unbounded reconnect delay");
        Set(f.Service, "reconnectFailures", 0); Invoke(f.Service, "ScheduleReconnect");
        retry = (double)typeof(LyriaRealtimeStreamingService).GetField("nextAutomaticAttemptAt", Private).GetValue(f.Service);
        Check(retry >= 123, "Retry clears outgoing PCM before fallback crossfade finishes");
    }

    private static void StaleConnection()
    {
        var f = Stream(); Set(f.Service, "connectionVersion", 2L);
        var dto = typeof(LyriaRealtimeStreamingService).GetNestedType("RealtimeInboundMessage", BindingFlags.NonPublic);
        var message = Activator.CreateInstance(dto, true);
        dto.GetField("type").SetValue(message, "error"); dto.GetField("connectionVersion").SetValue(message, 1L);
        typeof(LyriaRealtimeStreamingService).GetMethod("EnqueueInbound", Private).Invoke(f.Service, new[] { message });
        Invoke(f.Service, "ProcessInboundMessages");
        Check(f.Service.IsConnected && f.Agent.HasPendingOutcome, "Old socket interrupted the current connection");
    }

    private static void StartupBuffer()
    {
        var f = Stream(); Set(f.Service, "<HasBufferedPlaybackStarted>k__BackingField", false);
        Set(f.Service, "pendingBufferedPlaybackStart", true);
        Invoke(f.Service, "TryStartBufferedPlayback");
        Check(!f.Service.HasBufferedPlaybackStarted && f.Agent.Coordination.Status.Owner == AdaptationOwner.Audio,
            "Small buffer interrupted the active RL reservation");
    }

    private static void ReverseCrossfade()
    {
        var mixer = new AudioMixerController(); var fallback = new AudioClip();
        var current = new AudioSource { clip = fallback, isPlaying = true };
        var transition = new AudioSource { clip = new AudioClip(), isPlaying = true };
        Set(mixer, "currentMeditationSource", current); Set(mixer, "transitionMeditationSource", transition);
        Set(mixer, "currentMeditationBlend", 0.8f); Set(mixer, "transitionMeditationBlend", 0.6f);
        Set(mixer, "crossfadeRoutine", new Coroutine());
        Check(mixer.CrossfadeToMeditationClip(fallback, 2.5f), "Fallback to current clip was incorrectly ignored");
        var routine = (System.Collections.IEnumerator)typeof(AudioMixerController).GetMethod("ReturnToCurrentClipCoroutine", Private)
            .Invoke(mixer, new object[] { 2.5f });
        Time.deltaTime = 2.5f; routine.MoveNext(); routine.MoveNext();
        Check(current.clip == fallback && current.isPlaying && !transition.isPlaying && !mixer.IsCrossfading,
            "Interrupted crossfade ended on the failed stream");
    }
}
