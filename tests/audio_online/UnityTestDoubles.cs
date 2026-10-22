// Only used by the standalone logic test executable, never imported by Unity.
using System;
using System.Collections.Generic;
using System.Collections;
#if NET8_0_OR_GREATER
using System.Text.Json;
#else
using System.Web.Script.Serialization;
#endif

namespace UnityEngine
{
    public sealed class SerializeField : Attribute { }
    public sealed class DisallowMultipleComponent : Attribute { }
    public sealed class HeaderAttribute : Attribute { public HeaderAttribute(string value) { } }
    public sealed class RangeAttribute : Attribute { public RangeAttribute(float min, float max) { } }
    public sealed class MinAttribute : Attribute { public MinAttribute(float value) { } }
    public sealed class TextAreaAttribute : Attribute { public TextAreaAttribute(int min, int max) { } }
    public class MonoBehaviour
    {
        public bool enabled = true;
        public GameObject gameObject = new GameObject();
        public T GetComponent<T>() where T : class => gameObject.GetComponent<T>();
        public Transform transform => gameObject.transform;
        public Coroutine StartCoroutine(IEnumerator routine) => new Coroutine();
        public void StopCoroutine(Coroutine routine) { }
        public static void Destroy(object value) { }
        public static T FindAnyObjectByType<T>() where T : class => null;
    }
    public class GameObject
    {
        private readonly Dictionary<Type, object> components = new Dictionary<Type, object>();
        public string name;
        public Transform transform = new Transform();
        public GameObject() { }
        public GameObject(string name, params Type[] types) { this.name = name; foreach (var type in types) components[type] = Activator.CreateInstance(type); }
        public static GameObject Find(string name) => null;
        public T GetComponent<T>() where T : class => components.TryGetValue(typeof(T), out object value) ? value as T : null;
        public T AddComponent<T>() where T : new() { var value = new T(); components[typeof(T)] = value; return value; }
    }
    public sealed class Transform { public Transform parent; public void SetParent(Transform value, bool keep) { parent = value; } }
    public sealed class Coroutine { }
    public sealed class AudioClip
    {
        public float length;
        public Action<float[]> Reader;
        public static AudioClip Create(string name, int lengthSamples, int channels, int sampleRate, bool stream,
            Action<float[]> reader, Action<int> position) => new AudioClip { length = (float)lengthSamples / sampleRate, Reader = reader };
    }
    public sealed class AudioSource : MonoBehaviour
    {
        public AudioClip clip;
        public bool isPlaying, loop, playOnAwake, mute, bypassEffects, bypassListenerEffects, bypassReverbZones,
            ignoreListenerPause, ignoreListenerVolume;
        public object outputAudioMixerGroup, rolloffMode, velocityUpdateMode;
        public int priority;
        public float time, volume, pitch = 1, panStereo, spatialBlend, reverbZoneMix, dopplerLevel, spread, minDistance, maxDistance;
        public void Play() { isPlaying = clip != null; }
        public void Stop() { isPlaying = false; }
        public void Pause() { isPlaying = false; }
        public void UnPause() { isPlaying = clip != null; }
        public void GetOutputData(float[] data, int channel) { Array.Clear(data, 0, data.Length); }
    }
    public sealed class AudioLowPassFilter : MonoBehaviour { public float cutoffFrequency, lowpassResonanceQ; }
    public enum AudioReverbPreset { User }
    public sealed class AudioReverbFilter : MonoBehaviour
    {
        public AudioReverbPreset reverbPreset;
        public float dryLevel, room, roomHF, roomLF, decayTime, decayHFRatio, reflectionsLevel, reflectionsDelay,
            reverbLevel, reverbDelay, hfReference, lfReference, diffusion, density;
    }
    public static class Time
    {
        public static float time;
        public static float deltaTime;
        public static float unscaledTime;
        public static double realtimeSinceStartupAsDouble;
    }
    public static class Application { public static string persistentDataPath; }
    public static class AudioSettings
    {
        public static int outputSampleRate = 48000;
        public static void GetDSPBufferSize(out int length, out int count) { length = 256; count = 2; }
    }
    public static class Debug
    {
        public static void Log(object value, object context = null) { }
        public static void LogWarning(object value, object context = null) { }
        public static void LogError(object value, object context = null) { throw new Exception(value.ToString()); }
    }
    public static class JsonUtility
    {
#if NET8_0_OR_GREATER
        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions { IncludeFields = true };
        public static string ToJson(object value, bool pretty = false) => JsonSerializer.Serialize(value, Options);
        public static T FromJson<T>(string value) => JsonSerializer.Deserialize<T>(value, Options);
#else
        public static string ToJson(object value, bool pretty = false) => new JavaScriptSerializer { MaxJsonLength = 4000000 }.Serialize(value);
        public static T FromJson<T>(string value) => new JavaScriptSerializer { MaxJsonLength = 4000000 }.Deserialize<T>(value);
#endif
    }
    public static class Mathf
    {
        public const float PI = (float)Math.PI;
        public static float Cos(float value) => (float)Math.Cos(value);
        public static float Sin(float value) => (float)Math.Sin(value);
        public static float Atan2(float y, float x) => (float)Math.Atan2(y, x);
        public static float Abs(float value) => Math.Abs(value);
        public static float Clamp(float value, float min, float max) => Math.Max(min, Math.Min(max, value));
        public static int Clamp(int value, int min, int max) => Math.Max(min, Math.Min(max, value));
        public static float Clamp01(float value) => Clamp(value, 0, 1);
        public static float Min(params float[] values) { float result = float.PositiveInfinity; foreach (float x in values) result = Math.Min(result, x); return result; }
        public static int Min(int a, int b) => Math.Min(a, b);
        public static float Max(params float[] values) { float result = float.NegativeInfinity; foreach (float x in values) result = Math.Max(result, x); return result; }
        public static int Max(int a, int b) => Math.Max(a, b);
        public static float Lerp(float a, float b, float t) => a + (b - a) * Clamp01(t);
        public static float InverseLerp(float a, float b, float x) => a == b ? 0 : Clamp01((x - a) / (b - a));
        public static float MoveTowards(float current, float target, float delta) => Math.Abs(target - current) <= delta ? target : current + Math.Sign(target - current) * delta;
    }
}

namespace AdaptiveAudioVR.Core
{
    public enum AdaptiveControllerMode { Initialized, LowConfidenceDampened, HighStressAdaptive, LowStressCalming, MidRangeStabilizing }
    // External session boundary double; streaming, PCM queue and audio agent are real sources.
    public sealed class PrototypeBootstrap
    {
        public bool IsSessionRunning = true, IsPrepared = true, PlaybackReady = true;
        public bool CanPlayRealtimeMusic = true;
        public string RelaySessionId = "test-session";
        public AdaptiveAudioVR.RL.Agent.AudioRLAgent MonitorAgent;
        public LaminarVR.AdaptiveMeditation.Coordination.AdaptationCoordinator SharedAdaptation => MonitorAgent.Coordination;
        public SignalPacket CurrentSignal => MonitorAgent.CurrentSignal;
        public AudioParameters CurrentParameters => MonitorAgent.CurrentParameters;
        public string CurrentPromptSummary => "test";
        public LyriaControlFrame CurrentLyriaFrame;
        public void InterruptAdaptation(string reason)
        {
            SharedAdaptation?.Invalidate(1800000000 + UnityEngine.Time.realtimeSinceStartupAsDouble, reason);
            MonitorAgent.InvalidatePendingOutcome(reason);
        }
        public void SetRealtimePlaybackReady(bool ready) { PlaybackReady = ready; }
        public bool TryBeginContentChange(string id, out long reservation) =>
            SharedAdaptation.TryBeginContent(id, UnityEngine.Time.realtimeSinceStartupAsDouble, 90, out reservation);
        public void CompleteContentChange(long reservation, float pipelineDelaySeconds = 0) { }
    }
}

namespace AdaptiveAudioVR.Integration { public sealed class LyriaClipGenerationService : UnityEngine.MonoBehaviour { } }

namespace UnityEngine.Networking
{
    // Network calls are deliberately unsupported in this offline runner.
    public sealed class UnityWebRequest : IDisposable
    {
        public enum Result { Success, ConnectionError }
        public int timeout;
        public Result result = Result.ConnectionError;
        public string error = "Offline test double";
        public DownloadHandler downloadHandler = new DownloadHandler();
        public static string EscapeURL(string value) => Uri.EscapeDataString(value);
        public static UnityWebRequest Get(string url) => new UnityWebRequest();
        public object SendWebRequest() => throw new InvalidOperationException("Network forbidden in offline tests");
        public void Dispose() { }
    }
    public sealed class DownloadHandler { public string text; }
}
