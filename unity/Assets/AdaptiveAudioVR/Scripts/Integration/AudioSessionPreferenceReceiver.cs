using AdaptiveAudioVR.Core;
using AdaptiveAudioVR.Preference;
using LaminarVR.AdaptiveMeditation.Runtime.Application;
using UnityEngine;

namespace AdaptiveAudioVR.Integration
{
    [DisallowMultipleComponent]
    public sealed class AudioSessionPreferenceReceiver : MonoBehaviour
    {
        [SerializeField] private SessionRelayBridge sessionRelayBridge;
        [SerializeField] private PrototypeBootstrap audioBootstrap;

        private string processedMessageId;

        public string LastError { get; private set; } = string.Empty;

        public void Configure(SessionRelayBridge bridge, PrototypeBootstrap bootstrap)
        {
            sessionRelayBridge = bridge;
            audioBootstrap = bootstrap;
        }

        private void Update()
        {
            sessionRelayBridge ??= FindAnyObjectByType<SessionRelayBridge>();
            audioBootstrap ??= FindAnyObjectByType<PrototypeBootstrap>();
            var configuration = sessionRelayBridge != null
                ? sessionRelayBridge.CurrentConfiguration : null;
            if (configuration == null || audioBootstrap == null
                || configuration.MessageId == processedMessageId) return;

            processedMessageId = configuration.MessageId;
            if (!AudioSessionPreferenceMapper.TryMap(configuration,
                out var preferences, out var error))
            {
                LastError = error;
                Debug.LogError("[AudioSessionPreferenceReceiver] " + error, this);
                return;
            }
            if (!audioBootstrap.TryConfigureParticipantSession(preferences))
            {
                LastError = "audio-session-initialization-rejected";
                Debug.LogError("[AudioSessionPreferenceReceiver] " + LastError, this);
                return;
            }
            LastError = string.Empty;
            Debug.Log("[AudioSessionPreferenceReceiver] Audio prepared for session "
                + preferences.SessionId + " with " + preferences.PreferenceMode + " preferences.", this);
        }
    }
}
