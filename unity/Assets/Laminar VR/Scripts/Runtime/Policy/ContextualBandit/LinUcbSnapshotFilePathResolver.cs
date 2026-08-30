using System;
using System.IO;

namespace LaminarVR.AdaptiveMeditation.Runtime.Policy.ContextualBandit
{
    public static class LinUcbSnapshotFilePathResolver
    {
        private const string ModelDirectoryName =
            "AdaptiveMeditationPolicyModels";

        public static string ResolveParticipantSnapshotPath(
            string persistentDataPath,
            string participantPseudonym)
        {
            if (string.IsNullOrWhiteSpace(persistentDataPath))
            {
                throw new ArgumentException(
                    "Persistent data path is required.",
                    nameof(persistentDataPath));
            }

            return Path.Combine(
                persistentDataPath,
                ModelDirectoryName,
                ValidateFileName(participantPseudonym) + ".linucb.json");
        }

        private static string ValidateFileName(string participantPseudonym)
        {
            if (string.IsNullOrWhiteSpace(participantPseudonym))
            {
                throw new ArgumentException(
                    "Participant pseudonym is required.",
                    nameof(participantPseudonym));
            }

            var trimmed = participantPseudonym.Trim();
            if (trimmed == "."
                || trimmed == ".."
                || trimmed.IndexOf('/') >= 0
                || trimmed.IndexOf('\\') >= 0
                || trimmed.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                throw new ArgumentException(
                    "Participant pseudonym cannot be used as a model file name.",
                    nameof(participantPseudonym));
            }

            return trimmed;
        }
    }
}
