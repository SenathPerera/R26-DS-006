using AdaptiveAudioVR.Signals;
using LaminarVR.AdaptiveMeditation.Runtime.Application;
using LaminarVR.AdaptiveMeditation.Runtime.Networking;
using UnityEngine;

namespace AdaptiveAudioVR.Integration
{
    [AddComponentMenu("Adaptive Audio/Integration/Component B Stress Signal Receiver")]
    [DisallowMultipleComponent]
    public sealed class ComponentBStressSignalReceiver : MonoBehaviour
    {
        [SerializeField]
        private ComponentBPhysiologyBridge componentBBridge;

        [SerializeField]
        private SignalSimulator signalSimulator;

        private bool subscribed;

        public int ReceivedPayloadCount { get; private set; }

        public string LastRawJson { get; private set; } = string.Empty;

        private void OnEnable()
        {
            Subscribe();
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        public void Configure(
            ComponentBPhysiologyBridge bridge,
            SignalSimulator simulator)
        {
            Unsubscribe();
            componentBBridge = bridge;
            signalSimulator = simulator;
            Subscribe();
        }

        private void Subscribe()
        {
            if (subscribed
                || !isActiveAndEnabled
                || componentBBridge == null)
            {
                return;
            }

            componentBBridge.AcceptedPayloadReceived += HandlePayloadReceived;
            subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!subscribed || componentBBridge == null)
            {
                return;
            }

            componentBBridge.AcceptedPayloadReceived -= HandlePayloadReceived;
            subscribed = false;
        }

        private void HandlePayloadReceived(
            AcceptedComponentBStressPayload payload)
        {
            if (payload == null || signalSimulator == null)
            {
                return;
            }

            LastRawJson = payload.RawJson;
            ReceivedPayloadCount++;
            signalSimulator.SetExternalSignal(
                payload.NormalizedContinuousStress,
                payload.Confidence);

            if (ReceivedPayloadCount == 1)
            {
                Debug.Log(
                    "[ComponentBStressSignalReceiver] Audio is receiving "
                    + "Component B stress payloads.",
                    this);
            }
        }
    }
}
