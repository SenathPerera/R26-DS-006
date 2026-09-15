using System;
using UnityEngine;

namespace AdaptiveAudioVR.Core
{
    [Serializable]
    public struct SignalPacket
    {
        [Range(0f, 1f)] public float stress;
        [Range(0f, 1f)] public float confidence;
        [Range(0f, 1f)] public float signalQuality;
        public float heartRate;
        public float rmssd;
        public float sdnn;
        public float timestamp;
        public double sourceTimestamp;
        public double windowStart;
        public double windowEnd;
        public long sequenceId;
        public bool hasPhysiologyWindow;

        public SignalPacket(float stress, float confidence, float timestamp)
        {
            this.stress = Mathf.Clamp01(stress);
            this.confidence = Mathf.Clamp01(confidence);
            signalQuality = 1f;
            heartRate = 0f;
            rmssd = 0f;
            sdnn = 0f;
            this.timestamp = timestamp;
            sourceTimestamp = 0d;
            windowStart = 0d;
            windowEnd = 0d;
            sequenceId = 0L;
            hasPhysiologyWindow = false;
        }

        public SignalPacket(
            float stress,
            float confidence,
            float signalQuality,
            float heartRate,
            float rmssd,
            float sdnn,
            double sourceTimestamp,
            double windowStart,
            double windowEnd,
            long sequenceId,
            float receivedAtUnityTime)
        {
            this.stress = Mathf.Clamp01(stress);
            this.confidence = Mathf.Clamp01(confidence);
            this.signalQuality = Mathf.Clamp01(signalQuality);
            this.heartRate = Mathf.Max(0f, heartRate);
            this.rmssd = Mathf.Max(0f, rmssd);
            this.sdnn = Mathf.Max(0f, sdnn);
            timestamp = receivedAtUnityTime;
            this.sourceTimestamp = sourceTimestamp;
            this.windowStart = windowStart;
            this.windowEnd = windowEnd;
            this.sequenceId = sequenceId;
            hasPhysiologyWindow = windowEnd > windowStart && sourceTimestamp > 0d;
        }

        public bool IsRecent(float timeoutSeconds)
        {
            return Time.time - timestamp <= timeoutSeconds;
        }

        public bool IsNonOverlappingAfter(SignalPacket previous, double toleranceSeconds = 0.5d)
        {
            if (!hasPhysiologyWindow || !previous.hasPhysiologyWindow)
            {
                return true;
            }

            return windowStart + toleranceSeconds >= previous.windowEnd;
        }

        public static SignalPacket CreateDefault()
        {
            return new SignalPacket(0.5f, 0.75f, Time.time);
        }
    }
}
