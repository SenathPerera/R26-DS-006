using System;
using AdaptiveAudioVR.Core;
using UnityEngine;

namespace AdaptiveAudioVR.Audio
{
    public class RealtimePcmAudioPlayer : MonoBehaviour
    {
        [Header("Realtime Audio Format")]
        [SerializeField] private int sampleRate = 48000;
        [SerializeField] private int channels = 2;

        [Header("Buffering")]
        [SerializeField] private int bufferCapacitySeconds = 16;
        [SerializeField] private int streamingClipLengthSeconds = 2;

        public float BufferedSeconds
        {
            get
            {
                lock (bufferLock)
                {
                    return sampleRate > 0 && channels > 0
                        ? (float)bufferedSamples / (sampleRate * channels)
                        : 0f;
                }
            }
        }

        public long TotalReceivedSamples { get; private set; }
        public long UnderflowSampleCount { get; private set; }
        public long DroppedSampleCount { get; private set; }
        public bool IsAttachedToMixer { get; private set; }
        public AudioClip StreamingClip => streamingClip;

        private readonly object bufferLock = new object();
        private float[] ringBuffer;
        private int writeIndex;
        private int readIndex;
        private int bufferedSamples;
        private AudioClip streamingClip;

        private void Awake()
        {
            EnsureBuffer();
        }

        private void OnDestroy()
        {
            if (streamingClip != null)
            {
                Destroy(streamingClip);
                streamingClip = null;
            }
        }

        public void ConfigureFormat(int newSampleRate, int newChannels)
        {
            int normalizedSampleRate = Mathf.Max(8000, newSampleRate);
            int normalizedChannels = Mathf.Clamp(newChannels, 1, 2);
            if (normalizedSampleRate == sampleRate && normalizedChannels == channels)
            {
                return;
            }

            sampleRate = normalizedSampleRate;
            channels = normalizedChannels;
            ResetStreamClip();
        }

        public void ClearBuffer()
        {
            lock (bufferLock)
            {
                writeIndex = 0;
                readIndex = 0;
                bufferedSamples = 0;

                if (ringBuffer != null)
                {
                    Array.Clear(ringBuffer, 0, ringBuffer.Length);
                }
            }
        }

        public void AttachToMixer(AudioMixerController mixerController, bool restartPlayback = true)
        {
            if (mixerController == null)
            {
                return;
            }

            EnsureStreamingClip();
            mixerController.SetMeditationPlaybackPaused(false);
            mixerController.ReplaceMeditationClip(streamingClip, restartPlayback);
            IsAttachedToMixer = true;
        }

        public void DetachFromMixer()
        {
            IsAttachedToMixer = false;
        }

        public void EnqueuePcm16(byte[] pcmBytes)
        {
            if (pcmBytes == null || pcmBytes.Length < 2)
            {
                return;
            }

            EnsureBuffer();

            int sampleCount = pcmBytes.Length / 2;
            lock (bufferLock)
            {
                int capacity = ringBuffer.Length;
                for (int byteIndex = 0; byteIndex + 1 < pcmBytes.Length; byteIndex += 2)
                {
                    short sample = (short)(pcmBytes[byteIndex] | (pcmBytes[byteIndex + 1] << 8));
                    float normalized = Mathf.Clamp(sample / 32768f, -1f, 1f);

                    if (bufferedSamples >= capacity)
                    {
                        readIndex = (readIndex + 1) % capacity;
                        bufferedSamples--;
                        DroppedSampleCount++;
                    }

                    ringBuffer[writeIndex] = normalized;
                    writeIndex = (writeIndex + 1) % capacity;
                    bufferedSamples++;
                }
            }

            TotalReceivedSamples += sampleCount;
        }

        private void EnsureBuffer()
        {
            int requiredSamples = Mathf.Max(sampleRate * channels, sampleRate * channels * Mathf.Max(2, bufferCapacitySeconds));
            if (ringBuffer != null && ringBuffer.Length == requiredSamples)
            {
                return;
            }

            ringBuffer = new float[requiredSamples];
            writeIndex = 0;
            readIndex = 0;
            bufferedSamples = 0;
        }

        private void EnsureStreamingClip()
        {
            EnsureBuffer();
            if (streamingClip != null)
            {
                return;
            }

            int clipSamples = Mathf.Max(sampleRate, sampleRate * Mathf.Max(1, streamingClipLengthSeconds));
            streamingClip = AudioClip.Create(
                "LyriaRealtimeStream",
                clipSamples,
                channels,
                sampleRate,
                true,
                OnAudioRead,
                OnAudioSetPosition);
        }

        private void ResetStreamClip()
        {
            ClearBuffer();
            if (streamingClip != null)
            {
                Destroy(streamingClip);
                streamingClip = null;
            }

            EnsureBuffer();
            IsAttachedToMixer = false;
        }

        private void OnAudioRead(float[] data)
        {
            if (data == null || data.Length == 0)
            {
                return;
            }

            lock (bufferLock)
            {
                for (int i = 0; i < data.Length; i++)
                {
                    if (bufferedSamples > 0)
                    {
                        data[i] = ringBuffer[readIndex];
                        readIndex = (readIndex + 1) % ringBuffer.Length;
                        bufferedSamples--;
                    }
                    else
                    {
                        data[i] = 0f;
                        UnderflowSampleCount++;
                    }
                }
            }
        }

        private void OnAudioSetPosition(int newPosition)
        {
            // Streaming queue is time-based, so Unity's clip position resets do not map to buffer position.
        }
    }
}
