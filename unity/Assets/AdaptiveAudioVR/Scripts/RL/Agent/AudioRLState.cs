using System;
using AdaptiveAudioVR.Core;
using UnityEngine;

namespace AdaptiveAudioVR.RL.Agent
{
    [Serializable]
    public sealed class AudioRLState
    {
        public string userId;
        public SignalPacket signal;
        public float stressTrend;
        public float confidenceTrend;
        public float heartRateTrend;
        public float rmssdTrend;
        public AudioParameters currentParameters;
        public AudioParameters personalizedBaseline;
        public float[] preferenceEncoding;
        public AudioRLAction recentMeanResidualAction;
        public float sessionProgress;
        public float timeSinceLastActionSeconds;
        public int noveltyCount;
        public int decisionIndex;
        public bool isProductionWindow;

        public AudioRLState Snapshot()
        {
            return new AudioRLState
            {
                userId = userId,
                signal = signal,
                stressTrend = stressTrend,
                confidenceTrend = confidenceTrend,
                heartRateTrend = heartRateTrend,
                rmssdTrend = rmssdTrend,
                currentParameters = currentParameters,
                personalizedBaseline = personalizedBaseline,
                preferenceEncoding = preferenceEncoding != null ? (float[])preferenceEncoding.Clone() : Array.Empty<float>(),
                recentMeanResidualAction = recentMeanResidualAction,
                sessionProgress = sessionProgress,
                timeSinceLastActionSeconds = timeSinceLastActionSeconds,
                noveltyCount = noveltyCount,
                decisionIndex = decisionIndex,
                isProductionWindow = isProductionWindow
            };
        }

        public string ToSummary()
        {
            string source = isProductionWindow ? $"window {signal.sequenceId}" : "simulation";
            return $"{source}; stress {signal.stress:F2} ({stressTrend:+0.00;-0.00;0.00}), confidence {signal.confidence:F2}, quality {signal.signalQuality:F2}, decision {decisionIndex}, progress {Mathf.Clamp01(sessionProgress):P0}";
        }
    }
}
