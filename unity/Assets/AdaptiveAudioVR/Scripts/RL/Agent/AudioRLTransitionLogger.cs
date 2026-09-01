using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace AdaptiveAudioVR.RL.Agent
{
    [DisallowMultipleComponent]
    public sealed class AudioRLTransitionLogger : MonoBehaviour
    {
        [SerializeField] private bool logToConsole;
        [SerializeField] private bool flushEveryTransition = true;

        public string CurrentLogPath { get; private set; }
        public int TransitionCount { get; private set; }

        private StreamWriter writer;

        public void StartSession(string sessionId)
        {
            Close();
            try
            {
                string directory = Path.Combine(Application.persistentDataPath, "RLTransitions");
                Directory.CreateDirectory(directory);
                string safeSessionId = string.IsNullOrWhiteSpace(sessionId) ? DateTime.UtcNow.ToString("yyyyMMdd_HHmmss") : sessionId;
                CurrentLogPath = Path.Combine(directory, $"audio_rl_transitions_{safeSessionId}.jsonl");
                writer = new StreamWriter(CurrentLogPath, false, new UTF8Encoding(false));
                TransitionCount = 0;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AudioRLTransitionLogger] Could not start transition log: {ex.Message}", this);
                Close();
            }
        }

        public void Log(AudioRLTransition transition)
        {
            if (transition == null)
            {
                return;
            }

            if (writer == null)
            {
                StartSession(transition.sessionId);
            }

            try
            {
                string json = JsonUtility.ToJson(transition, false);
                writer?.WriteLine(json);
                if (flushEveryTransition)
                {
                    writer?.Flush();
                }

                TransitionCount++;
                if (logToConsole)
                {
                    Debug.Log($"[AudioRLTransitionLogger] {json}", this);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AudioRLTransitionLogger] Could not write transition: {ex.Message}", this);
            }
        }

        private void OnDestroy()
        {
            Close();
        }

        private void OnApplicationQuit()
        {
            Close();
        }

        private void Close()
        {
            try
            {
                writer?.Flush();
                writer?.Dispose();
            }
            catch
            {
                // Shutdown should not fail because a log file could not flush.
            }
            finally
            {
                writer = null;
            }
        }
    }
}
