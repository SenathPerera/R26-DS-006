using AdaptiveAudioVR.Core;
using UnityEngine;

namespace AdaptiveAudioVR.Signals
{
    public class SignalSimulator : MonoBehaviour
    {
        public enum SimulationMode
        {
            Manual,
            Oscillation,
            RandomWalk,
            External
        }

        [Header("Mode")]
        [SerializeField] private SimulationMode mode = SimulationMode.Oscillation;

        [Header("Manual Controls")]
        [Range(0f, 1f)] [SerializeField] private float manualStress = 0.5f;
        [Range(0f, 1f)] [SerializeField] private float manualConfidence = 0.8f;

        [Header("Oscillation")]
        [SerializeField] private float oscillationSpeed = 0.3f;
        [SerializeField] private float stressOscillationAmplitude = 0.25f;
        [SerializeField] private float confidenceOscillationAmplitude = 0.18f;

        [Header("Random Walk")]
        [SerializeField] private float randomWalkChangeRate = 0.18f;
        [SerializeField] private float randomWalkUpdateInterval = 0.75f;

        [Header("Smoothing")]
        [SerializeField] private float outputSmoothSpeed = 3f;

        public SignalPacket CurrentSignal { get; private set; }
        public SimulationMode CurrentMode => mode;

        private float targetStress = 0.5f;
        private float targetConfidence = 0.8f;
        private float nextRandomWalkTime;

        private void Start()
        {
            CurrentSignal = SignalPacket.CreateDefault();
            targetStress = CurrentSignal.stress;
            targetConfidence = CurrentSignal.confidence;
        }

        private void Update()
        {
            if (mode == SimulationMode.External)
            {
                return;
            }

            UpdateTargets();

            float stress = Mathf.Lerp(CurrentSignal.stress, Mathf.Clamp01(targetStress), Time.deltaTime * outputSmoothSpeed);
            float confidence = Mathf.Lerp(CurrentSignal.confidence, Mathf.Clamp01(targetConfidence), Time.deltaTime * outputSmoothSpeed);
            CurrentSignal = new SignalPacket(stress, confidence, Time.time);
        }

        public void SetManualStress(float value)
        {
            manualStress = Mathf.Clamp01(value);
            if (mode == SimulationMode.Manual)
            {
                targetStress = manualStress;
            }
        }

        public void SetManualConfidence(float value)
        {
            manualConfidence = Mathf.Clamp01(value);
            if (mode == SimulationMode.Manual)
            {
                targetConfidence = manualConfidence;
            }
        }

        public void SetModeManual()
        {
            mode = SimulationMode.Manual;
            targetStress = manualStress;
            targetConfidence = manualConfidence;
        }

        public void SetModeOscillation()
        {
            mode = SimulationMode.Oscillation;
        }

        public void SetModeRandomWalk()
        {
            mode = SimulationMode.RandomWalk;
            nextRandomWalkTime = 0f;
        }

        public void SetExternalSignal(float stress, float confidence)
        {
            SetExternalSignal(new SignalPacket(stress, confidence, Time.time));
        }

        public void SetExternalSignal(SignalPacket signal)
        {
            mode = SimulationMode.External;
            targetStress = Mathf.Clamp01(signal.stress);
            targetConfidence = Mathf.Clamp01(signal.confidence);
            CurrentSignal = signal;
        }

        private void UpdateTargets()
        {
            switch (mode)
            {
                case SimulationMode.Manual:
                    targetStress = manualStress;
                    targetConfidence = manualConfidence;
                    break;
                case SimulationMode.Oscillation:
                    float time = Time.time * oscillationSpeed;
                    targetStress = 0.5f + (Mathf.Sin(time) * stressOscillationAmplitude);
                    targetConfidence = 0.7f + (Mathf.Cos(time * 0.65f) * confidenceOscillationAmplitude);
                    break;
                case SimulationMode.RandomWalk:
                    if (Time.time >= nextRandomWalkTime)
                    {
                        targetStress = Mathf.Clamp01(targetStress + Random.Range(-randomWalkChangeRate, randomWalkChangeRate));
                        targetConfidence = Mathf.Clamp01(targetConfidence + Random.Range(-randomWalkChangeRate, randomWalkChangeRate));
                        nextRandomWalkTime = Time.time + Mathf.Max(0.1f, randomWalkUpdateInterval);
                    }
                    break;
            }
        }
    }
}
