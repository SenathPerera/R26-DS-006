// Only used by the standalone logic test executable, never imported by Unity.
using System;
using System.Collections.Generic;
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
        public GameObject gameObject = new GameObject();
        public T GetComponent<T>() where T : class => gameObject.GetComponent<T>();
    }
    public class GameObject
    {
        private readonly Dictionary<Type, object> components = new Dictionary<Type, object>();
        public T GetComponent<T>() where T : class => components.TryGetValue(typeof(T), out object value) ? value as T : null;
        public T AddComponent<T>() where T : new() { var value = new T(); components[typeof(T)] = value; return value; }
    }
    public static class Time
    {
        public static float time;
        public static double realtimeSinceStartupAsDouble;
    }
    public static class Application { public static string persistentDataPath; }
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
        public static float Abs(float value) => Math.Abs(value);
        public static float Clamp(float value, float min, float max) => Math.Max(min, Math.Min(max, value));
        public static float Clamp01(float value) => Clamp(value, 0, 1);
        public static float Min(params float[] values) { float result = float.PositiveInfinity; foreach (float x in values) result = Math.Min(result, x); return result; }
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
}
