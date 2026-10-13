using System;
using System.Collections.Generic;
using AdaptiveAudioVR.Core;
using UnityEngine;

namespace AdaptiveAudioVR.RL.Agent
{
    public static class AudioOutcomeGate
    {
        public static bool IsValidWindow(SignalPacket signal, double utcNow, float minimumQuality,
            float minimumConfidence, out string reason)
        {
            reason = string.Empty;
            if (!AudioLinUcbModel.IsFinite(signal.timestamp) || !AudioLinUcbModel.IsFinite(signal.stress) || !AudioLinUcbModel.IsFinite(signal.confidence)
                || !AudioLinUcbModel.IsFinite(signal.signalQuality) || signal.stress < 0 || signal.stress > 1
                || signal.confidence < minimumConfidence || signal.confidence > 1
                || signal.signalQuality < minimumQuality || signal.signalQuality > 1)
                reason = "Invalid or unreliable stress/confidence/quality.";
            else if (signal.hasPhysiologyWindow && (!AudioLinUcbModel.IsFinite(signal.windowStart)
                || !AudioLinUcbModel.IsFinite(signal.windowEnd) || !AudioLinUcbModel.IsFinite(signal.sourceTimestamp)
                || signal.windowEnd - signal.windowStart < 30 || signal.windowEnd - signal.windowStart > 120
                || signal.windowEnd > utcNow + 2 || utcNow - signal.windowEnd > 90
                || signal.sourceTimestamp < signal.windowEnd - 2 || signal.sourceTimestamp > utcNow + 2))
                reason = "Malformed, stale, or future physiology window.";
            else if ((signal.hasHeartRate && (!AudioLinUcbModel.IsFinite(signal.heartRate) || signal.heartRate <= 0))
                || (signal.hasRmssd && (!AudioLinUcbModel.IsFinite(signal.rmssd) || signal.rmssd < 0)))
                reason = "Invalid supplied heart rate or RMSSD.";
            return reason.Length == 0;
        }

        public static bool CanAttribute(SignalPacket before, SignalPacket after, double eligiblePostStartUtc,
            double lastUsedPostEnd, out string reason)
        {
            reason = string.Empty;
            if (before.hasPhysiologyWindow != after.hasPhysiologyWindow)
                reason = "Signal source changed.";
            else if (after.hasPhysiologyWindow && (after.sequenceId <= before.sequenceId
                || after.windowEnd <= lastUsedPostEnd || after.windowStart < before.windowEnd
                || after.windowStart < eligiblePostStartUtc))
                reason = "Post window is reused, overlapping, or includes transition/settling.";
            return reason.Length == 0;
        }
    }

    public sealed class AudioSessionBaseline
    {
        private readonly List<SignalPacket> samples = new List<SignalPacket>();
        public int Count => samples.Count;
        public float StressScale => StandardDeviation(s => s.stress, s => true, 0.05f);
        public float RmssdScale => StandardDeviation(s => s.rmssd, s => s.hasRmssd, 5f);
        public float HeartRateScale => StandardDeviation(s => s.heartRate, s => s.hasHeartRate, 5f);

        public void Add(SignalPacket signal)
        {
            if (signal.hasPhysiologyWindow && samples.Count > 0
                && signal.windowStart < samples[samples.Count - 1].windowEnd) return;
            if (samples.Count < 60) samples.Add(signal);
        }

        private float StandardDeviation(Func<SignalPacket, float> value, Func<SignalPacket, bool> include, float floor)
        {
            double sum = 0, squared = 0;
            int count = 0;
            foreach (SignalPacket signal in samples)
            {
                if (!include(signal)) continue;
                double x = value(signal);
                sum += x; squared += x * x; count++;
            }
            return count > 1 ? Mathf.Max(floor, (float)Math.Sqrt(Math.Max(0, (squared - sum * sum / count) / (count - 1)))) : floor;
        }
    }
}
