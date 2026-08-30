using System;
using UnityEngine;

namespace AdaptiveAudioVR.Core
{
    [Serializable]
    public struct AudioParameters
    {
        [Range(0f, 1f)] public float intensity;
        [Range(0f, 1f)] public float density;
        [Range(0f, 1f)] public float brightness;
        [Range(0f, 1f)] public float ambientMix;
        [Range(0f, 1f)] public float musicMix;

        public AudioParameters Clamp01()
        {
            intensity = Mathf.Clamp01(intensity);
            density = Mathf.Clamp01(density);
            brightness = Mathf.Clamp01(brightness);
            ambientMix = Mathf.Clamp01(ambientMix);
            musicMix = Mathf.Clamp01(musicMix);
            return this;
        }

        public static AudioParameters Lerp(AudioParameters a, AudioParameters b, float t)
        {
            t = Mathf.Clamp01(t);
            return new AudioParameters
            {
                intensity = Mathf.Lerp(a.intensity, b.intensity, t),
                density = Mathf.Lerp(a.density, b.density, t),
                brightness = Mathf.Lerp(a.brightness, b.brightness, t),
                ambientMix = Mathf.Lerp(a.ambientMix, b.ambientMix, t),
                musicMix = Mathf.Lerp(a.musicMix, b.musicMix, t)
            }.Clamp01();
        }

        public static AudioParameters MoveTowards(AudioParameters current, AudioParameters target, float maxDelta)
        {
            return new AudioParameters
            {
                intensity = Mathf.MoveTowards(current.intensity, target.intensity, maxDelta),
                density = Mathf.MoveTowards(current.density, target.density, maxDelta),
                brightness = Mathf.MoveTowards(current.brightness, target.brightness, maxDelta),
                ambientMix = Mathf.MoveTowards(current.ambientMix, target.ambientMix, maxDelta),
                musicMix = Mathf.MoveTowards(current.musicMix, target.musicMix, maxDelta)
            }.Clamp01();
        }

        public override string ToString()
        {
            return $"Intensity {intensity:F2} | Density {density:F2} | Brightness {brightness:F2} | Ambient {ambientMix:F2} | Music {musicMix:F2}";
        }
    }
}
