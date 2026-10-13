using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace AdaptiveAudioVR.RL.Agent
{
    public sealed class AudioBanditPersistence
    {
        private readonly string ownerKey;
        private readonly string configurationKey;
        public string FilePath { get; }

        public AudioBanditPersistence(string directory, string userId, string environmentId, bool simulated, string configuration)
        {
            ownerKey = Hash(userId + "|" + environmentId + "|" + (simulated ? "simulated" : "physiology"));
            configurationKey = Hash(configuration);
            FilePath = Path.Combine(directory, ownerKey + "_" + configurationKey + ".json");
        }

        public bool TryLoad(AudioLinUcbModel model, out string status)
        {
            status = "New personalized model";
            if (!File.Exists(FilePath)) return false;
            try
            {
                if (new FileInfo(FilePath).Length > 2 * 1024 * 1024) throw new InvalidDataException("Snapshot is oversized.");
                var snapshot = JsonUtility.FromJson<AudioBanditSnapshot>(File.ReadAllText(FilePath));
                if (snapshot == null || snapshot.ownerKey != ownerKey || snapshot.configurationKey != configurationKey
                    || snapshot.featureSchema != AudioBanditFeatures.Schema || snapshot.actionSchema != AudioActionCatalog.Schema)
                    throw new InvalidDataException("Snapshot identity, schema or reward configuration mismatch.");
                if (!model.TryRestore(snapshot, out status)) return false;
                status = "Restored personalized model";
                return true;
            }
            catch (Exception ex) when (ex is IOException || ex is ArgumentException || ex is UnauthorizedAccessException)
            { status = "Snapshot rejected: " + ex.Message; return false; }
        }

        public bool TrySave(AudioLinUcbModel model, out string status)
        {
            string temporaryPath = FilePath + ".tmp";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                var snapshot = model.Capture();
                snapshot.ownerKey = ownerKey;
                snapshot.configurationKey = configurationKey;
                snapshot.featureSchema = AudioBanditFeatures.Schema;
                snapshot.actionSchema = AudioActionCatalog.Schema;
                File.WriteAllText(temporaryPath, JsonUtility.ToJson(snapshot), new UTF8Encoding(false));
                if (File.Exists(FilePath)) File.Replace(temporaryPath, FilePath, null);
                else File.Move(temporaryPath, FilePath);
                status = "Personalized model saved";
                return true;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is NotSupportedException)
            { status = "Model save failed: " + ex.Message; return false; }
        }

        private static string Hash(string value)
        {
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-", "").ToLowerInvariant();
        }
    }
}
