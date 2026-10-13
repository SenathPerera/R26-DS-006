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
        [Range(0f, 1f)] public float tempo;
        [Range(0f, 1f)] public float fade;
        [Range(0f, 1f)] public float ambientMix;
        [Range(0f, 1f)] public float musicMix;

        public AudioParameters Clamp01()
        {
            intensity = Mathf.Clamp01(intensity);
            density = Mathf.Clamp01(density);
            brightness = Mathf.Clamp01(brightness);
            tempo = Mathf.Clamp01(tempo);
            fade = Mathf.Clamp01(fade);
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
                tempo = Mathf.Lerp(a.tempo, b.tempo, t),
                fade = Mathf.Lerp(a.fade, b.fade, t),
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
                tempo = Mathf.MoveTowards(current.tempo, target.tempo, maxDelta),
                fade = Mathf.MoveTowards(current.fade, target.fade, maxDelta),
                ambientMix = Mathf.MoveTowards(current.ambientMix, target.ambientMix, maxDelta),
                musicMix = Mathf.MoveTowards(current.musicMix, target.musicMix, maxDelta)
            }.Clamp01();
        }

        public float[] ToControlVector()
        {
            return new[]
            {
                intensity,
                density,
                brightness,
                tempo,
                fade,
                musicMix,
                ambientMix
            };
        }

        public static AudioParameters FromControlVector(float[] values)
        {
            if (values == null || values.Length < 7)
            {
                return default;
            }

            AudioParameters parameters = new AudioParameters
            {
                intensity = values[0],
                density = values[1],
                brightness = values[2],
                tempo = values[3],
                fade = values[4],
                musicMix = values[5],
                ambientMix = values[6]
            }.Clamp01();

            parameters.NormalizeMix();
            return parameters;
        }

        public void NormalizeMix()
        {
            float total = musicMix + ambientMix;
            if (total <= 0.001f)
            {
                musicMix = 0.5f;
                ambientMix = 0.5f;
                return;
            }

            musicMix /= total;
            ambientMix /= total;
        }

        public override string ToString()
        {
            return $"Intensity {intensity:F2} | Density {density:F2} | Brightness {brightness:F2} | Tempo {tempo:F2} | Fade {fade:F2} | Ambient {ambientMix:F2} | Music {musicMix:F2}";
        }
    }
}
