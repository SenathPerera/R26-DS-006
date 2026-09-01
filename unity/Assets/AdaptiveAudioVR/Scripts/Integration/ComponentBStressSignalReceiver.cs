using System;
using AdaptiveAudioVR.Core;
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

        [SerializeField, Min(0.25f)]
        private float bridgeDiscoveryIntervalSeconds = 1f;

        private bool subscribed;
        private double lastAcceptedWindowEnd = double.MinValue;
        private long sequenceId;
        private float nextBridgeDiscoveryTime;

        public int ReceivedPayloadCount { get; private set; }

        public int DuplicateOrOutOfOrderPayloadCount { get; private set; }

        public string LastRawJson { get; private set; } = string.Empty;

        public SignalPacket CurrentSignal { get; private set; }

        public bool HasLiveSignal => ReceivedPayloadCount > 0;

        public bool IsConnectedToBridge => subscribed && componentBBridge != null;

        private void OnEnable()
        {
            Subscribe();
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        private void Update()
        {
            if (subscribed || Time.unscaledTime < nextBridgeDiscoveryTime)
            {
                return;
            }

            nextBridgeDiscoveryTime = Time.unscaledTime
                                      + Mathf.Max(0.25f, bridgeDiscoveryIntervalSeconds);
            ComponentBPhysiologyBridge discoveredBridge =
                FindAnyObjectByType<ComponentBPhysiologyBridge>();
            if (discoveredBridge != null)
            {
                Configure(discoveredBridge, signalSimulator);
            }
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
            if (!subscribed)
            {
                return;
            }

            if (componentBBridge != null)
            {
                componentBBridge.AcceptedPayloadReceived -= HandlePayloadReceived;
            }

            subscribed = false;
        }

        private void HandlePayloadReceived(
            AcceptedComponentBStressPayload payload)
        {
            TryProcessPayload(payload);
        }

        public bool TryProcessPayload(AcceptedComponentBStressPayload payload)
        {
            if (payload == null || payload.Window == null || signalSimulator == null)
            {
                return false;
            }

            double windowEnd = payload.Window.WindowEndUtcUnixSeconds;
            if (!IsFinite(windowEnd) || windowEnd <= lastAcceptedWindowEnd)
            {
                DuplicateOrOutOfOrderPayloadCount++;
                return false;
            }

            sequenceId++;
            CurrentSignal = CreateSignalPacket(payload, sequenceId, Time.time);
            lastAcceptedWindowEnd = windowEnd;
            LastRawJson = payload.RawJson;
            ReceivedPayloadCount++;
            signalSimulator.SetExternalSignal(CurrentSignal);

            if (ReceivedPayloadCount == 1)
            {
                Debug.Log(
                    "[ComponentBStressSignalReceiver] Audio is receiving "
                    + "Component B stress payloads.",
                    this);
            }

            return true;
        }

        public static SignalPacket CreateSignalPacket(
            AcceptedComponentBStressPayload payload,
            long packetSequenceId,
            float receivedAtUnityTime)
        {
            if (payload == null || payload.Window == null)
            {
                throw new ArgumentNullException(nameof(payload));
            }

            var window = payload.Window;
            return new SignalPacket(
                payload.NormalizedContinuousStress,
                payload.Confidence,
                (float)window.SignalQuality,
                (float)window.HeartRateBpm,
                (float)(window.RmssdMs ?? 0d),
                (float)(window.SdnnMs ?? 0d),
                window.SourceTimestampUtcUnixSeconds,
                window.WindowStartUtcUnixSeconds,
                window.WindowEndUtcUnixSeconds,
                packetSequenceId,
                receivedAtUnityTime);
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
