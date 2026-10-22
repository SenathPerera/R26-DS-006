using System;
using UnityEngine;

namespace LaminarVR.AdaptiveMeditation.Runtime.Application
{
    public interface IQuestClientIdentityStore
    {
        string Read(string key);

        void Write(string key, string value);

        void Save();
    }

    public sealed class QuestClientIdentityProvider
    {
        public const string PreferenceKey =
            "adaptive-meditation.quest-client-id.v1";

        private readonly IQuestClientIdentityStore store;

        public QuestClientIdentityProvider(IQuestClientIdentityStore store)
        {
            this.store = store
                ?? throw new ArgumentNullException(nameof(store));
        }

        public string GetOrCreate()
        {
            var existing = store.Read(PreferenceKey)?.Trim();
            if (IsValid(existing))
            {
                return existing;
            }

            var created = "quest-" + Guid.NewGuid().ToString("N");
            store.Write(PreferenceKey, created);
            store.Save();
            return created;
        }

        private static bool IsValid(string value)
        {
            return !string.IsNullOrEmpty(value)
                && value.StartsWith("quest-", StringComparison.Ordinal)
                && value.Length == 38;
        }
    }

    public sealed class PlayerPrefsQuestClientIdentityStore
        : IQuestClientIdentityStore
    {
        public string Read(string key)
        {
            return PlayerPrefs.GetString(key, string.Empty);
        }

        public void Write(string key, string value)
        {
            PlayerPrefs.SetString(key, value);
        }

        public void Save()
        {
            PlayerPrefs.Save();
        }
    }
}
