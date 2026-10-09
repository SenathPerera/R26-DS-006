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
                writer.WriteLine("unityTime,sourceTimestamp,windowStart,windowEnd,stress,confidence,signalQuality,heartRate,rmssd,sdnn,intensity,density,brightness,tempo,fade,ambientMix,musicMix,controllerMode,safetyMode,fallbackMode,strategyName,policyStatus,actionName,reward,bpm,guidance,temperature,lyriaPlaybackSource,lyriaGenerationReason,lyriaGenerationOutcome,lyriaCacheState,lyriaClipPath");
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
            string line = string.Join(",", new[]
            {
                Time.time.ToString("F3", CultureInfo.InvariantCulture),
                signal.sourceTimestamp.ToString("F3", CultureInfo.InvariantCulture),
                signal.windowStart.ToString("F3", CultureInfo.InvariantCulture),
                signal.windowEnd.ToString("F3", CultureInfo.InvariantCulture),
                signal.stress.ToString("F3", CultureInfo.InvariantCulture),
                signal.confidence.ToString("F3", CultureInfo.InvariantCulture),
                signal.signalQuality.ToString("F3", CultureInfo.InvariantCulture),
                signal.heartRate.ToString("F3", CultureInfo.InvariantCulture),
                signal.rmssd.ToString("F3", CultureInfo.InvariantCulture),
                signal.sdnn.ToString("F3", CultureInfo.InvariantCulture),
                parameters.intensity.ToString("F3", CultureInfo.InvariantCulture),
                parameters.density.ToString("F3", CultureInfo.InvariantCulture),
                parameters.brightness.ToString("F3", CultureInfo.InvariantCulture),
                parameters.tempo.ToString("F3", CultureInfo.InvariantCulture),
                parameters.fade.ToString("F3", CultureInfo.InvariantCulture),
                parameters.ambientMix.ToString("F3", CultureInfo.InvariantCulture),
                parameters.musicMix.ToString("F3", CultureInfo.InvariantCulture),
                controllerMode.ToString(),
                SanitizeCsv(safetyMode),
                fallbackMode.ToString(),
                SanitizeCsv(strategyName),
                SanitizeCsv(policyStatus),
                SanitizeCsv(actionName),
                reward.ToString("F3", CultureInfo.InvariantCulture),
                generationConfig.bpm.ToString(CultureInfo.InvariantCulture),
                generationConfig.guidance.ToString("F3", CultureInfo.InvariantCulture),
                generationConfig.temperature.ToString("F3", CultureInfo.InvariantCulture),
                SanitizeCsv(lyriaPlaybackSource),
                SanitizeCsv(lyriaGenerationReason),
                SanitizeCsv(lyriaGenerationOutcome),
                SanitizeCsv(lyriaCacheState),
                SanitizeCsv(lyriaClipPath)
            });

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
