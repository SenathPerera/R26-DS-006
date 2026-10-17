using AdaptiveAudioVR.Core;
using UnityEngine;

namespace AdaptiveAudioVR.Safety
{
    public class SafetyManager : MonoBehaviour
    {
        [SerializeField] private bool emergencyMute;
        [SerializeField] private float signalTimeoutSeconds = 3f;
        [SerializeField] private bool missingProfileFallback = true;
        public event System.Action<bool> EmergencyMuteChanged;

        public bool EmergencyMute
        {
            get => emergencyMute;
            set
            {
                if (emergencyMute == value) return;
                emergencyMute = value;
                EmergencyMuteChanged?.Invoke(value);
            }
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
