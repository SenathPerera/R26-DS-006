using System;
using UnityEngine;

namespace AdaptiveAudioVR.Core
{
    public enum AdaptiveControllerMode
    {
        Initialized,
        LowConfidenceDampened,
        HighStressAdaptive,
        LowStressCalming,
        MidRangeStabilizing
    }

    [Serializable]
    public class SessionState
    {
        public AudioProfile activeProfile;
        public SignalPacket latestSignal;
        public AudioParameters latestParameters;
        public AdaptiveControllerMode controllerMode = AdaptiveControllerMode.Initialized;
        public string safetyMode = "Normal";
        public bool fallbackMode;
        public string personalizationStrategy = "Uninitialized";
        public string currentActionName = "Warmup";
        public string policyStatus = "Idle";
        public float latestReward;
        public LyriaControlFrame currentLyriaFrame;

        public void Reset(AudioProfile profile)
        {
            activeProfile = profile;
            latestSignal = SignalPacket.CreateDefault();
            latestParameters = profile != null ? profile.ToBaselineParameters() : default;
            controllerMode = AdaptiveControllerMode.Initialized;
            safetyMode = "Normal";
            fallbackMode = false;
            personalizationStrategy = "Uninitialized";
            currentActionName = "Warmup";
            policyStatus = "Idle";
            latestReward = 0f;
            currentLyriaFrame = null;
        }
    }
}
