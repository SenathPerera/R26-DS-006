using System;
using AdaptiveAudioVR.Core;
using UnityEngine;

namespace AdaptiveAudioVR.RL.Agent
{
    public enum AudioRLSafetyMode
    {
        Normal,
        LowConfidenceDampened,
        ConfidenceFreeze,
        LowSignalQualityDampened,
        SignalQualityFreeze,
        StaleSignalRecovery,
        BaselineRecovery,
        EmergencyMuted
    }

    [Serializable]
    public sealed class AudioRLSafetyResult
    {
        public AudioRLAction proposedAction;
        public AudioRLAction finalSafeAction;
        public AudioParameters safeTarget;
        public AudioRLSafetyMode safetyMode;
        public bool interventionApplied;
        public string reason;
    }

    public sealed class AudioRLSafetyFilter
    {
        private readonly float maximumActionDelta;
        private readonly float confidenceFreezeThreshold;
        private readonly float lowConfidenceThreshold;
        private readonly float signalQualityFreezeThreshold;
        private readonly float lowSignalQualityThreshold;
        private readonly float maximumBaselineDistance;
        private readonly float maximumActionAcceleration;
        private readonly float minimumAmbientMix;
        private readonly float maximumMusicMix;

        public AudioRLSafetyFilter(
            float maximumActionDelta,
            float confidenceFreezeThreshold,
            float lowConfidenceThreshold,
            float signalQualityFreezeThreshold,
            float lowSignalQualityThreshold,
            float maximumBaselineDistance,
            float maximumActionAcceleration,
            float minimumAmbientMix,
            float maximumMusicMix)
        {
            this.maximumActionDelta = Mathf.Max(0.001f, maximumActionDelta);
            this.confidenceFreezeThreshold = Mathf.Clamp01(confidenceFreezeThreshold);
            this.lowConfidenceThreshold = Mathf.Max(this.confidenceFreezeThreshold, Mathf.Clamp01(lowConfidenceThreshold));
            this.signalQualityFreezeThreshold = Mathf.Clamp01(signalQualityFreezeThreshold);
            this.lowSignalQualityThreshold = Mathf.Max(this.signalQualityFreezeThreshold, Mathf.Clamp01(lowSignalQualityThreshold));
            this.maximumBaselineDistance = Mathf.Clamp01(maximumBaselineDistance);
            this.maximumActionAcceleration = Mathf.Max(0.001f, maximumActionAcceleration);
            this.minimumAmbientMix = Mathf.Clamp01(minimumAmbientMix);
            this.maximumMusicMix = Mathf.Clamp01(maximumMusicMix);
        }

        public AudioRLSafetyResult Apply(
            AudioRLAction proposedAction,
            AudioRLAction previousSafeAction,
            AudioRLState state,
            bool safeToRun,
            bool signalIsRecent)
        {
            if (!safeToRun)
            {
                return BuildResult(
                    proposedAction,
                    AudioRLAction.NoChange,
                    state.currentParameters,
                    AudioRLSafetyMode.EmergencyMuted,
                    true,
                    "Emergency mute is active; the controller action was cancelled.");
            }

            if (!signalIsRecent)
            {
                AudioRLAction recovery = AudioRLAction.Between(state.currentParameters, state.personalizedBaseline)
                    .Clamp(maximumActionDelta * 0.35f);
                AudioParameters recoveryTarget = recovery.ApplyTo(state.currentParameters);
                return BuildResult(
                    proposedAction,
                    recovery,
                    recoveryTarget,
                    AudioRLSafetyMode.StaleSignalRecovery,
                    true,
                    "The input signal is stale; moving conservatively toward the personalized baseline.");
            }

            AudioRLAction safeAction = proposedAction.Clamp(maximumActionDelta);
            AudioRLSafetyMode mode = AudioRLSafetyMode.Normal;
            string reason = "Action was inside normal confidence, signal-quality, and magnitude limits.";
            bool adaptationFrozen = false;

            float confidence = Mathf.Clamp01(state.signal.confidence);
            float signalQuality = Mathf.Clamp01(state.signal.signalQuality);
            if (signalQuality <= signalQualityFreezeThreshold)
            {
                safeAction = AudioRLAction.NoChange;
                mode = AudioRLSafetyMode.SignalQualityFreeze;
                reason = "Signal quality was below the freeze threshold.";
                adaptationFrozen = true;
            }
            else if (confidence <= confidenceFreezeThreshold)
            {
                safeAction = AudioRLAction.NoChange;
                mode = AudioRLSafetyMode.ConfidenceFreeze;
                reason = "Physiological confidence was below the freeze threshold.";
                adaptationFrozen = true;
            }
            else if (signalQuality < lowSignalQualityThreshold)
            {
                float scale = Mathf.Lerp(0.15f, 0.55f, Mathf.InverseLerp(signalQualityFreezeThreshold, lowSignalQualityThreshold, signalQuality));
                safeAction = safeAction.Scale(scale);
                mode = AudioRLSafetyMode.LowSignalQualityDampened;
                reason = $"Signal quality dampened the proposed action to {scale:F2} of its magnitude.";
            }
            else if (confidence < lowConfidenceThreshold)
            {
                float scale = Mathf.Lerp(0.15f, 0.50f, Mathf.InverseLerp(confidenceFreezeThreshold, lowConfidenceThreshold, confidence));
                safeAction = safeAction.Scale(scale);
                mode = AudioRLSafetyMode.LowConfidenceDampened;
                reason = $"Confidence dampened the proposed action to {scale:F2} of its magnitude.";
            }

            // A freeze is an absolute hold. Acceleration smoothing must not reintroduce
            // part of the previous action after confidence or quality has failed.
            if (!adaptationFrozen)
            {
                safeAction = LimitActionAcceleration(previousSafeAction, safeAction);
            }
            AudioParameters safeTarget = safeAction.ApplyTo(state.currentParameters);
            safeTarget.brightness = Mathf.Min(safeTarget.brightness, 0.90f);
            safeTarget.ambientMix = Mathf.Max(safeTarget.ambientMix, minimumAmbientMix);
            safeTarget.musicMix = Mathf.Min(safeTarget.musicMix, maximumMusicMix);
            safeTarget.NormalizeMix();

            if (MeanDistance(safeTarget, state.personalizedBaseline) > maximumBaselineDistance)
            {
                safeTarget = AudioParameters.Lerp(state.personalizedBaseline, safeTarget, 0.5f);
                safeTarget.NormalizeMix();
                mode = AudioRLSafetyMode.BaselineRecovery;
                reason = "The target drifted too far from the personalized baseline and was pulled back halfway.";
            }

            safeTarget = ClampTargetDelta(state.currentParameters, safeTarget, maximumActionDelta);
            safeAction = AudioRLAction.Between(state.currentParameters, safeTarget);

            bool intervened = mode != AudioRLSafetyMode.Normal
                              || MeanActionDifference(proposedAction, safeAction) > 0.0005f;
            return BuildResult(proposedAction, safeAction, safeTarget.Clamp01(), mode, intervened, reason);
        }

        private AudioRLAction LimitActionAcceleration(AudioRLAction previous, AudioRLAction candidate)
        {
            float[] previousValues = previous.ToArray();
            float[] candidateValues = candidate.ToArray();
            for (int i = 0; i < candidateValues.Length; i++)
            {
                candidateValues[i] = Mathf.MoveTowards(previousValues[i], candidateValues[i], maximumActionAcceleration);
            }

            return AudioRLAction.FromArray(candidateValues).Clamp(maximumActionDelta);
        }

        private static AudioParameters ClampTargetDelta(AudioParameters current, AudioParameters target, float maximumDelta)
        {
            float[] currentValues = current.ToControlVector();
            float[] targetValues = target.ToControlVector();
            for (int i = 0; i < 5; i++)
            {
                targetValues[i] = Mathf.MoveTowards(currentValues[i], targetValues[i], maximumDelta);
            }

            targetValues[5] = Mathf.MoveTowards(currentValues[5], targetValues[5], maximumDelta);
            targetValues[6] = 1f - targetValues[5];
            return AudioParameters.FromControlVector(targetValues);
        }

        private static AudioRLSafetyResult BuildResult(
            AudioRLAction proposed,
            AudioRLAction safe,
            AudioParameters target,
            AudioRLSafetyMode mode,
            bool intervention,
            string reason)
        {
            return new AudioRLSafetyResult
            {
                proposedAction = proposed,
                finalSafeAction = safe,
                safeTarget = target,
                safetyMode = mode,
                interventionApplied = intervention,
                reason = reason
            };
        }

        private static float MeanDistance(AudioParameters left, AudioParameters right)
        {
            float[] a = left.ToControlVector();
            float[] b = right.ToControlVector();
            float sum = 0f;
            for (int i = 0; i < a.Length; i++)
            {
                sum += Mathf.Abs(a[i] - b[i]);
            }

            return sum / a.Length;
        }

        private static float MeanActionDifference(AudioRLAction left, AudioRLAction right)
        {
            float[] a = left.ToArray();
            float[] b = right.ToArray();
            float sum = 0f;
            for (int i = 0; i < a.Length; i++)
            {
                sum += Mathf.Abs(a[i] - b[i]);
            }

            return sum / a.Length;
        }
    }
}
