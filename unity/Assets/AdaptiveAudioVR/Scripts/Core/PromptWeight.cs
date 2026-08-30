using System;
using UnityEngine;

namespace AdaptiveAudioVR.Core
{
    [Serializable]
    public struct PromptWeight
    {
        public string text;
        [Range(0f, 2.5f)] public float weight;

        public PromptWeight(string text, float weight)
        {
            this.text = string.IsNullOrWhiteSpace(text) ? "instrumental meditation" : text.Trim();
            this.weight = Mathf.Clamp(weight, 0.01f, 2.5f);
        }

        public PromptWeight Normalize()
        {
            text = string.IsNullOrWhiteSpace(text) ? "instrumental meditation" : text.Trim();
            weight = Mathf.Clamp(weight, 0.01f, 2.5f);
            return this;
        }
    }
}
