using System;
using System.IO;

namespace LaminarVR.AdaptiveMeditation.Runtime.Telemetry
{
    public static class TelemetryFilePathResolver
    {
        private const string TelemetryDirectoryName =
            "AdaptiveMeditationTelemetry";

        public static string ResolveSessionJsonLinesPath(
            string persistentDataPath,
            string sessionId)
        {
            if (string.IsNullOrWhiteSpace(persistentDataPath))
            {
                throw new ArgumentException(
                    "Persistent data path is required.",
                    nameof(persistentDataPath));
            }

            var safeSessionId = ValidateFileName(sessionId);
            return Path.Combine(
                persistentDataPath,
                TelemetryDirectoryName,
                safeSessionId + ".jsonl");
        }

        private static string ValidateFileName(string sessionId)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                throw new ArgumentException(
                    "Session ID is required.",
                    nameof(sessionId));
            }

            var trimmed = sessionId.Trim();
            if (trimmed == "."
                || trimmed == ".."
                || trimmed.IndexOf('/') >= 0
                || trimmed.IndexOf('\\') >= 0
                || trimmed.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                throw new ArgumentException(
                    "Session ID cannot be used as a telemetry file name.",
                    nameof(sessionId));
            }

            return trimmed;
        }
    }
}
