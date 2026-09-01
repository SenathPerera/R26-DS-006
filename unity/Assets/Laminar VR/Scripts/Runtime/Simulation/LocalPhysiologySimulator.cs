#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using LaminarVR.AdaptiveMeditation.Physiology;
using LaminarVR.AdaptiveMeditation.Runtime.Configuration;
using UnityEngine;

namespace LaminarVR.AdaptiveMeditation.Runtime.Simulation
{
    [AddComponentMenu("Adaptive Meditation/Development/Local Physiology Simulator")]
    [DisallowMultipleComponent]
    public sealed class LocalPhysiologySimulator : MonoBehaviour
    {
        [Header("Configuration")]
        [SerializeField]
        private PhysiologyValidationProfile validationProfile = null;

        [SerializeField]
        private LocalPhysiologySimulationProfile simulationProfile = null;

        [Header("Development Controls")]
        [SerializeField, Min(0f)]
        private float simulationSpeedMultiplier = 1f;

        private PhysiologyStateBuffer stateBuffer;
        private double emissionIntervalSeconds;
        private double simulatedMonotonicTimeSeconds;
        private double initialUtcUnixSeconds;
        private double nextEmissionMonotonicTimeSeconds;
        private string statusMessage = string.Empty;

        public bool IsInitialized => stateBuffer != null;

        public long LatestAcceptedSequenceNumber => stateBuffer == null
            ? 0L
            : stateBuffer.LatestAcceptedSequenceNumber;

        public string StatusMessage => statusMessage;

        private void Awake()
        {
            TryInitialize();
        }

        private void Update()
        {
            if (!IsInitialized || !IsValidPositive(simulationSpeedMultiplier))
            {
                return;
            }

            simulatedMonotonicTimeSeconds +=
                Time.unscaledDeltaTime * simulationSpeedMultiplier;
            if (simulatedMonotonicTimeSeconds
                < nextEmissionMonotonicTimeSeconds)
            {
                return;
            }

            EmitAt(nextEmissionMonotonicTimeSeconds);
            nextEmissionMonotonicTimeSeconds =
                simulatedMonotonicTimeSeconds + emissionIntervalSeconds;
        }

        public bool TryInitialize()
        {
            if (IsInitialized)
            {
                return true;
            }

            if (validationProfile == null)
            {
                statusMessage = "Assign a PhysiologyValidationProfile.";
                return false;
            }

            if (simulationProfile == null)
            {
                statusMessage = "Assign a LocalPhysiologySimulationProfile.";
                return false;
            }

            if (!validationProfile.TryCreateRuntimeConfiguration(
                    out var validationConfiguration,
                    out var validationError))
            {
                statusMessage = validationError;
                return false;
            }

            if (!simulationProfile.TryGetEmissionInterval(
                    out emissionIntervalSeconds,
                    out validationError))
            {
                statusMessage = validationError;
                return false;
            }

            stateBuffer = new PhysiologyStateBuffer(validationConfiguration);
            initialUtcUnixSeconds =
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000d;
            nextEmissionMonotonicTimeSeconds = emissionIntervalSeconds;
            statusMessage = "Local physiology simulation initialized.";
            return true;
        }

        public PhysiologyIngestionResult EmitNow()
        {
            if (!IsInitialized)
            {
                statusMessage = "Local physiology simulation is not initialized.";
                return new PhysiologyIngestionResult(
                    PhysiologyIngestionResultCode.InvalidReceiptTime,
                    PhysiologyValidationReasonCode.Accepted,
                    0L);
            }

            return EmitAt(simulatedMonotonicTimeSeconds);
        }

        public bool HasFreshDecisionWindowAfter(long sequenceNumber)
        {
            return stateBuffer != null
                && stateBuffer.HasFreshDecisionWindowAfter(
                    sequenceNumber,
                    simulatedMonotonicTimeSeconds);
        }

        public bool TryGetLatestDecisionWindow(
            long afterSequenceNumberExclusive,
            out PhysiologyWindowSnapshot snapshot,
            out PhysiologyQueryResultCode resultCode)
        {
            if (stateBuffer == null)
            {
                snapshot = default;
                resultCode = PhysiologyQueryResultCode.NoData;
                return false;
            }

            return stateBuffer.TryGetLatestUsable(
                PhysiologyDataUse.Decision,
                simulatedMonotonicTimeSeconds,
                afterSequenceNumberExclusive,
                out snapshot,
                out resultCode);
        }

        private PhysiologyIngestionResult EmitAt(
            double receiptMonotonicTimeSeconds)
        {
            var receivedUtcUnixSeconds =
                initialUtcUnixSeconds + receiptMonotonicTimeSeconds;
            var window = simulationProfile.CreateWindow(receivedUtcUnixSeconds);
            var result = stateBuffer.Ingest(
                window,
                receivedUtcUnixSeconds,
                receiptMonotonicTimeSeconds);
            statusMessage = result.Accepted
                ? "Accepted mock physiology window "
                    + result.AcceptedSequenceNumber
                    + "."
                : "Mock physiology rejected: "
                    + result.ResultCode
                    + "/"
                    + result.ValidationReasonCode;

            Debug.Log(
                "[LocalPhysiologySimulator] physiology_ingestion"
                + " result=" + result.ResultCode
                + " validation=" + result.ValidationReasonCode
                + " sequence=" + result.AcceptedSequenceNumber
                + " monotonic_seconds="
                + receiptMonotonicTimeSeconds.ToString("F3")
                + " window_end_utc_unix_seconds="
                + window.WindowEndUtcUnixSeconds.ToString("F3"),
                this);
            return result;
        }

        private static bool IsValidPositive(float value)
        {
            return !float.IsNaN(value)
                && !float.IsInfinity(value)
                && value > 0f;
        }
    }
}
#endif

