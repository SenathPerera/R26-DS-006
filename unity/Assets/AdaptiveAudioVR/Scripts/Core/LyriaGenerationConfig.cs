using System;
using UnityEngine;

namespace AdaptiveAudioVR.Core
{
    public enum LyriaScale
    {
        SCALE_UNSPECIFIED,
        C_MAJOR_A_MINOR,
        D_FLAT_MAJOR_B_FLAT_MINOR,
        D_MAJOR_B_MINOR,
        E_FLAT_MAJOR_C_MINOR,
        E_MAJOR_D_FLAT_MINOR,
        F_MAJOR_D_MINOR,
        G_FLAT_MAJOR_E_FLAT_MINOR,
        G_MAJOR_E_MINOR,
        A_FLAT_MAJOR_F_MINOR,
        A_MAJOR_G_FLAT_MINOR,
        B_FLAT_MAJOR_G_MINOR,
        B_MAJOR_A_FLAT_MINOR
    }

    public enum LyriaGenerationMode
    {
        MUSIC_GENERATION_MODE_UNSPECIFIED,
        QUALITY,
        DIVERSITY,
        VOCALIZATION
    }

    [Serializable]
    public struct LyriaGenerationConfig
    {
        [Range(0f, 3f)] public float temperature;
        [Range(1, 1000)] public int topK;
        public int seed;
        [Range(0f, 6f)] public float guidance;
        [Range(60, 200)] public int bpm;
        [Range(0f, 1f)] public float density;
        [Range(0f, 1f)] public float brightness;
        public LyriaScale scale;
        public bool muteBass;
        public bool muteDrums;
        public bool onlyBassAndDrums;
        public LyriaGenerationMode musicGenerationMode;

        public LyriaGenerationConfig Normalize()
        {
            temperature = Mathf.Clamp(temperature, 0f, 3f);
            topK = Mathf.Clamp(topK, 1, 1000);
            guidance = Mathf.Clamp(guidance, 0f, 6f);
            bpm = Mathf.Clamp(bpm, 60, 200);
            density = Mathf.Clamp01(density);
            brightness = Mathf.Clamp01(brightness);
            if (musicGenerationMode == LyriaGenerationMode.MUSIC_GENERATION_MODE_UNSPECIFIED)
            {
                musicGenerationMode = LyriaGenerationMode.QUALITY;
            }

            return this;
        }

        public string ToDisplayString()
        {
            return $"BPM {bpm} | Density {density:F2} | Brightness {brightness:F2} | Guidance {guidance:F2} | Temp {temperature:F2} | Mode {musicGenerationMode}";
        }
    }
}
