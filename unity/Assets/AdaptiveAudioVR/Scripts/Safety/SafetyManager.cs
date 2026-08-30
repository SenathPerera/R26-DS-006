using AdaptiveAudioVR.Core;
using UnityEngine;

namespace AdaptiveAudioVR.Safety
{
    public class SafetyManager : MonoBehaviour
    {
        [SerializeField] private bool emergencyMute;
        [SerializeField] private float signalTimeoutSeconds = 3f;
        [SerializeField] private bool missingProfileFallback = true;

        public bool EmergencyMute
        {
            get => emergencyMute;
            set => emergencyMute = value;
        }

        public bool MissingProfileFallback => missingProfileFallback;

        public bool IsSafeToRun()
        {
            return !emergencyMute;
        }

        public string GetSafetyMode(bool hasProfile, SignalPacket latestSignal)
        {
            if (emergencyMute)
            {
                return "EmergencyMuted";
            }

            if (!hasProfile && missingProfileFallback)
            {
                return "MissingProfileFallback";
            }

            if (!latestSignal.IsRecent(signalTimeoutSeconds))
            {
                return "SignalTimeoutFallback";
            }

            return "Normal";
        }
    }
}
