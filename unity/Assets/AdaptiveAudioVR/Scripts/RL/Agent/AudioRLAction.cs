using System;
using AdaptiveAudioVR.Core;
using UnityEngine;

namespace AdaptiveAudioVR.RL.Agent
{
    [Serializable]
    public struct AudioRLAction
    {
        public float deltaIntensity;
        public float deltaDensity;
        public float deltaBrightness;
        public float deltaTempo;
        public float deltaFade;
        public float deltaMusicMix;
        public float deltaAmbientMix;

        public static AudioRLAction NoChange => default;

        public float MeanAbsoluteMagnitude =>
            (Mathf.Abs(deltaIntensity)
             + Mathf.Abs(deltaDensity)
             + Mathf.Abs(deltaBrightness)
             + Mathf.Abs(deltaTempo)
             + Mathf.Abs(deltaFade)
             + Mathf.Abs(deltaMusicMix)
             + Mathf.Abs(deltaAmbientMix)) / 7f;

        public float MaximumAbsoluteMagnitude => Mathf.Max(
            Mathf.Abs(deltaIntensity),
            Mathf.Abs(deltaDensity),
            Mathf.Abs(deltaBrightness),
            Mathf.Abs(deltaTempo),
            Mathf.Abs(deltaFade),
            Mathf.Abs(deltaMusicMix),
            Mathf.Abs(deltaAmbientMix));

        public float[] ToArray()
        {
            return new[]
            {
                deltaIntensity,
                deltaDensity,
                deltaBrightness,
                deltaTempo,
                deltaFade,
                deltaMusicMix,
                deltaAmbientMix
            };
        }

        public static AudioRLAction FromArray(float[] values)
        {
            if (values == null || values.Length < 7)
            {
                return NoChange;
            }

            return new AudioRLAction
            {
                deltaIntensity = values[0],
                deltaDensity = values[1],
                deltaBrightness = values[2],
                deltaTempo = values[3],
                deltaFade = values[4],
                deltaMusicMix = values[5],
                deltaAmbientMix = values[6]
            };
        }

        public AudioRLAction Clamp(float maximumAbsoluteDelta)
        {
            maximumAbsoluteDelta = Mathf.Max(0f, maximumAbsoluteDelta);
            return FromArray(Array.ConvertAll(ToArray(), value => Mathf.Clamp(value, -maximumAbsoluteDelta, maximumAbsoluteDelta)));
        }

        public AudioRLAction Scale(float scale)
        {
            return FromArray(Array.ConvertAll(ToArray(), value => value * scale));
        }

        public AudioParameters ApplyTo(AudioParameters current)
        {
            float[] state = current.ToControlVector();
            float[] action = ToArray();
            for (int i = 0; i < state.Length; i++)
            {
                state[i] = Mathf.Clamp01(state[i] + action[i]);
            }

            return AudioParameters.FromControlVector(state);
        }

        public static AudioRLAction Between(AudioParameters from, AudioParameters to)
        {
            float[] fromValues = from.ToControlVector();
            float[] toValues = to.ToControlVector();
            float[] delta = new float[7];
            for (int i = 0; i < delta.Length; i++)
            {
                delta[i] = toValues[i] - fromValues[i];
            }

            return FromArray(delta);
        }

        public static AudioRLAction operator +(AudioRLAction left, AudioRLAction right)
        {
            float[] a = left.ToArray();
            float[] b = right.ToArray();
            for (int i = 0; i < a.Length; i++)
            {
                a[i] += b[i];
            }

            return FromArray(a);
        }

        public override string ToString()
        {
            return $"I {deltaIntensity:+0.000;-0.000;0.000}, D {deltaDensity:+0.000;-0.000;0.000}, B {deltaBrightness:+0.000;-0.000;0.000}, T {deltaTempo:+0.000;-0.000;0.000}, F {deltaFade:+0.000;-0.000;0.000}, M {deltaMusicMix:+0.000;-0.000;0.000}, A {deltaAmbientMix:+0.000;-0.000;0.000}";
        }
    }
}
