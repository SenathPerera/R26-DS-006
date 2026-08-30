using System;
using System.IO;
using AdaptiveAudioVR.Core;
using UnityEngine;

namespace AdaptiveAudioVR.Preference
{
    public class PreferenceManager : MonoBehaviour
    {
        private const string DefaultConfigFolder = "AdaptiveAudioVR/Configs";

        [SerializeField] private string configFileName = "sample_user_preferences.json";
        [SerializeField] private string configFolder = DefaultConfigFolder;
        [SerializeField] private bool loadOnAwake = false;
        [SerializeField] private bool logToConsole = true;

        public UserPreferences CurrentPreferences { get; private set; }
        public bool IsUsingFallback { get; private set; }

        public event Action<UserPreferences> PreferencesLoaded;

        private void Awake()
        {
            if (loadOnAwake)
            {
                LoadPreferences();
            }
        }

        public UserPreferences LoadPreferences()
        {
            string path = ResolveConfigPath();
            IsUsingFallback = false;

            try
            {
                if (!File.Exists(path))
                {
                    Log($"Preference file missing at {path}. Using safe defaults.");
                    UseFallback();
                    return CurrentPreferences;
                }

                string json = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(json))
                {
                    Log("Preference file was empty. Using safe defaults.");
                    UseFallback();
                    return CurrentPreferences;
                }

                var loaded = JsonUtility.FromJson<UserPreferences>(json);
                if (loaded == null)
                {
                    Log("Preference file could not be parsed. Using safe defaults.");
                    UseFallback();
                    return CurrentPreferences;
                }

                loaded.Normalize();
                CurrentPreferences = loaded;
                Log($"Preferences loaded for user {CurrentPreferences.userId} from {path}.");
                PreferencesLoaded?.Invoke(CurrentPreferences);
                return CurrentPreferences;
            }
            catch (Exception ex)
            {
                Log($"Preference loading failed: {ex.Message}. Using safe defaults.");
                UseFallback();
                return CurrentPreferences;
            }
        }

        public UserPreferences EnsureLoaded()
        {
            if (CurrentPreferences == null)
            {
                LoadPreferences();
            }

            return CurrentPreferences;
        }

        private void UseFallback()
        {
            IsUsingFallback = true;
            CurrentPreferences = UserPreferences.CreateSafeDefaults();
            PreferencesLoaded?.Invoke(CurrentPreferences);
        }

        private string ResolveConfigPath()
        {
            string primaryFolder = Path.Combine(Application.streamingAssetsPath, configFolder);
            string primaryPath = Path.Combine(primaryFolder, configFileName);
            if (File.Exists(primaryPath))
            {
                return primaryPath;
            }

            string legacyFolder = Path.Combine(Application.streamingAssetsPath, "Configs");
            return Path.Combine(legacyFolder, configFileName);
        }

        private void Log(string message)
        {
            if (logToConsole)
            {
                Debug.Log($"[PreferenceManager] {message}", this);
            }
        }
    }
}
