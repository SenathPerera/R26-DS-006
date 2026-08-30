using System;
using UnityEngine;

namespace AdaptiveAudioVR.Core
{
    [Serializable]
    public struct SignalPacket
    {
        [Range(0f, 1f)] public float stress;
        [Range(0f, 1f)] public float confidence;
        public float timestamp;

        public SignalPacket(float stress, float confidence, float timestamp)
        {
            this.stress = Mathf.Clamp01(stress);
            this.confidence = Mathf.Clamp01(confidence);
            this.timestamp = timestamp;
        }

        public bool IsRecent(float timeoutSeconds)
        {
            return Time.time - timestamp <= timeoutSeconds;
        }

        public static SignalPacket CreateDefault()
        {
            return new SignalPacket(0.5f, 0.75f, Time.time);
        }
    }
}
