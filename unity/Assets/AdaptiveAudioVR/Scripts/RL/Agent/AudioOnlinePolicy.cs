using System;
using System.Collections.Generic;
using UnityEngine;

namespace AdaptiveAudioVR.RL.Agent
{
    public enum AudioRLPolicyMode
    {
        // Preserve serialized 0/1 values, migrating existing scenes to whole policies.
        RuleBasedAdaptive = 0,
        PersonalizedBandit = 1,
        StaticPersonalized = 2
    }

    public static class AudioActionCatalog
    {
        public const string Schema = "audio-micro-actions-v1";
        public const int Count = 13;
        private static readonly string[] Names =
        {
            "Hold stable", "Increase intensity", "Reduce intensity", "Increase density", "Reduce density",
            "Increase brightness", "Darken", "Increase tempo", "Reduce tempo", "Lengthen fade", "Shorten fade",
            "Increase music / reduce ambience", "Increase ambience / reduce music"
        };

        public static string Name(int arm) => arm >= 0 && arm < Count ? Names[arm] : "Safety replacement";

        public static AudioRLAction Action(int arm, float delta)
        {
            if (arm < 0 || arm >= Count) throw new ArgumentOutOfRangeException(nameof(arm));
            if (arm == 0) return AudioRLAction.NoChange;
            var values = new float[7];
            int dimension = (arm - 1) / 2;
            values[dimension] = arm % 2 == 1 ? delta : -delta;
            if (dimension == 5) values[6] = -values[5];
            return AudioRLAction.FromArray(values);
        }

        public static int Match(AudioRLAction action, float delta)
        {
            for (int arm = 0; arm < Count; arm++)
                if (Same(action, Action(arm, delta))) return arm;
            return -1;
        }

        public static bool Same(AudioRLAction a, AudioRLAction b, float tolerance = 0.00001f)
        {
            float[] left = a.ToArray(), right = b.ToArray();
            for (int i = 0; i < left.Length; i++)
                if (!AudioLinUcbModel.IsFinite(left[i]) || !AudioLinUcbModel.IsFinite(right[i])
                    || Mathf.Abs(left[i] - right[i]) > tolerance) return false;
            return true;
        }
    }

    public static class AudioBanditFeatures
    {
        public const string Schema = "audio-context-v1-40";
        public const int Count = 40;

        public static double[] Encode(AudioRLState state, float maximumDelta)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            var values = new List<double>(Count) { 1d };
            float[] preferences = state.preferenceEncoding ?? Array.Empty<float>();
            for (int i = 0; i < 13; i++) values.Add(i < preferences.Length ? preferences[i] : 0f);
            float[] current = state.currentParameters.ToControlVector();
            float[] baseline = state.personalizedBaseline.ToControlVector();
            for (int i = 0; i < 7; i++) values.Add(current[i]);
            for (int i = 0; i < 7; i++) values.Add(current[i] - baseline[i]);
            values.Add(state.signal.stress);
            values.Add(state.signal.confidence);
            values.Add(state.signal.signalQuality);
            values.Add(state.stressTrend);
            values.Add(state.confidenceTrend);
            values.Add(state.signal.hasHeartRate ? state.signal.heartRate / 200f : 0f);
            values.Add(state.signal.hasRmssd ? state.signal.rmssd / 200f : 0f);
            values.Add(state.signal.hasHeartRate ? 1d : 0d);
            values.Add(state.signal.hasRmssd ? 1d : 0d);
            values.Add(state.sessionProgress);
            values.Add(state.noveltyCount / 20f);
            values.Add(state.recentSafeAction.MaximumAbsoluteMagnitude / Mathf.Max(0.001f, maximumDelta));
            if (values.Count != Count) throw new InvalidOperationException("Audio feature schema changed without a version change.");
            for (int i = 0; i < values.Count; i++)
            {
                if (!AudioLinUcbModel.IsFinite(values[i])) throw new ArgumentException("Non-finite audio context.");
                values[i] = Math.Max(-1, Math.Min(1, values[i]));
            }
            return values.ToArray();
        }
    }

    public sealed class AudioOnlinePolicy
    {
        public const string SelectionSchema = "audio-linucb-balanced-ties-v2";
        public AudioLinUcbModel Model { get; }
        private readonly float actionDelta;
        private readonly AudioRuleBaselinePolicy rules;
        private readonly int[] selectionAttempts = new int[AudioActionCatalog.Count];

        public AudioOnlinePolicy(float actionDelta, double alpha, double ridge)
        {
            this.actionDelta = actionDelta;
            Model = new AudioLinUcbModel(AudioBanditFeatures.Count, AudioActionCatalog.Count, alpha, ridge);
            rules = new AudioRuleBaselinePolicy(0.45f, 0.65f, 0.35f);
        }

        public AudioBanditSelection Select(AudioRLPolicyMode mode, AudioRLState state, double[] features, IReadOnlyList<int> eligible, bool explore = true)
        {
            if (mode == AudioRLPolicyMode.PersonalizedBandit)
            {
                var selection = Model.Select(features, eligible, explore, selectionAttempts);
                if (explore && Model.Alpha > 0 && selectionAttempts[selection.Arm] < int.MaxValue)
                    selectionAttempts[selection.Arm]++;
                return selection;
            }
            int best = 0;
            double bestError = double.PositiveInfinity;
            AudioRLAction desired = mode == AudioRLPolicyMode.StaticPersonalized
                ? AudioRLAction.NoChange : rules.GetAction(state, actionDelta);
            float[] target = desired.ToArray();
            foreach (int arm in eligible)
            {
                float[] candidate = AudioActionCatalog.Action(arm, actionDelta).ToArray();
                double error = 0;
                for (int i = 0; i < candidate.Length; i++) error += Math.Pow(candidate[i] - target[i], 2);
                if (error < bestError - 1e-12 || (Math.Abs(error - bestError) <= 1e-12 && arm < best))
                { best = arm; bestError = error; }
            }
            return new AudioBanditSelection(best, 0, 0, 0);
        }
    }
}
