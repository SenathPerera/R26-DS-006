using System;
using System.Globalization;
using System.IO;
using System.Text;
using AdaptiveAudioVR.Core;
using AdaptiveAudioVR.Integration;
using UnityEngine;

namespace AdaptiveAudioVR.Logging
{
    public class SessionLogger : MonoBehaviour
    {
        [SerializeField] private bool logToConsole = false;
        [SerializeField] private float logIntervalSeconds = 1f;
        [SerializeField] private LyriaClipGenerationService lyriaClipGenerationService;

        public string CurrentLogPath { get; private set; }

        private float lastLogTime = float.MinValue;
        private StreamWriter writer;
        private bool sessionStarted;

        private void OnDestroy()
        {
            CloseWriter();
        }

        private void OnApplicationQuit()
        {
            CloseWriter();
        }

        public void StartSession()
        {
            if (sessionStarted)
            {
                return;
            }

            try
            {
                string logDirectory = Path.Combine(Application.persistentDataPath, "Logs");
                Directory.CreateDirectory(logDirectory);
                string fileName = $"adaptive_audio_session_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
                CurrentLogPath = Path.Combine(logDirectory, fileName);
                writer = new StreamWriter(CurrentLogPath, false, Encoding.UTF8);
                writer.WriteLine("unityTime,stress,confidence,intensity,density,brightness,ambientMix,musicMix,controllerMode,safetyMode,fallbackMode,strategyName,policyStatus,actionName,reward,bpm,guidance,temperature,lyriaPlaybackSource,lyriaGenerationReason,lyriaGenerationOutcome,lyriaCacheState,lyriaClipPath");
                writer.Flush();
                sessionStarted = true;
                Debug.Log($"[SessionLogger] Logging session to {CurrentLogPath}", this);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SessionLogger] Failed to start session log: {ex.Message}", this);
                sessionStarted = false;
            }
        }

        public void LogFrame(
            SignalPacket signal,
            AudioParameters parameters,
            AdaptiveControllerMode controllerMode,
            string safetyMode,
            bool fallbackMode,
            string strategyName,
            string policyStatus,
            string actionName,
            float reward,
            LyriaGenerationConfig generationConfig)
        {
            if (!sessionStarted)
            {
                StartSession();
            }

            if (!sessionStarted || Time.time - lastLogTime < logIntervalSeconds)
            {
                return;
            }

            lyriaClipGenerationService ??= FindAnyObjectByType<LyriaClipGenerationService>();
            string lyriaPlaybackSource = lyriaClipGenerationService != null ? lyriaClipGenerationService.CurrentPlaybackSourceLabel : "Unavailable";
            string lyriaGenerationReason = lyriaClipGenerationService != null ? lyriaClipGenerationService.LastGenerationReason : "Unavailable";
            string lyriaGenerationOutcome = lyriaClipGenerationService != null ? lyriaClipGenerationService.LastGenerationOutcome : "Unavailable";
            string lyriaCacheState = lyriaClipGenerationService != null ? lyriaClipGenerationService.LastCacheState : "Unavailable";
            string lyriaClipPath = lyriaClipGenerationService != null ? lyriaClipGenerationService.LastGeneratedClipPath : "Unavailable";

            lastLogTime = Time.time;
            string line = string.Format(
                CultureInfo.InvariantCulture,
                "{0:F3},{1:F3},{2:F3},{3:F3},{4:F3},{5:F3},{6:F3},{7:F3},{8},{9},{10},{11},{12},{13},{14:F3},{15},{16:F3},{17:F3},{18},{19},{20},{21},{22}",
                Time.time,
                signal.stress,
                signal.confidence,
                parameters.intensity,
                parameters.density,
                parameters.brightness,
                parameters.ambientMix,
                parameters.musicMix,
                controllerMode,
                SanitizeCsv(safetyMode),
                fallbackMode,
                SanitizeCsv(strategyName),
                SanitizeCsv(policyStatus),
                SanitizeCsv(actionName),
                reward,
                generationConfig.bpm,
                generationConfig.guidance,
                generationConfig.temperature,
                SanitizeCsv(lyriaPlaybackSource),
                SanitizeCsv(lyriaGenerationReason),
                SanitizeCsv(lyriaGenerationOutcome),
                SanitizeCsv(lyriaCacheState),
                SanitizeCsv(lyriaClipPath));

            try
            {
                writer?.WriteLine(line);
                writer?.Flush();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SessionLogger] Failed to write log line: {ex.Message}", this);
            }

            if (logToConsole)
            {
                Debug.Log($"[SessionLogger] {line}", this);
            }
        }

        private static string SanitizeCsv(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "Unknown";
            }

            return value.Replace(",", "_");
        }

        private void CloseWriter()
        {
            if (writer == null)
            {
                return;
            }

            try
            {
                writer.Flush();
                writer.Dispose();
            }
            catch
            {
                // Ignore shutdown IO errors.
            }
            finally
            {
                writer = null;
            }
        }
    }
}
