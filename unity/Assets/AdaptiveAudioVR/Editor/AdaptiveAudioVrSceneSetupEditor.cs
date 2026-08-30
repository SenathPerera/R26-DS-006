using System;
using System.Linq;
using System.Reflection;
using AdaptiveAudioVR.Integration;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AdaptiveAudioVR.Editor
{
    public static class AdaptiveAudioVrSceneSetupEditor
    {
        private const string InstallerObjectName = "AdaptiveAudioIntegration";
        private const string MeditationSearchFolder = "Assets/AdaptiveAudioVR/Audio/Meditation";

        [MenuItem("AdaptiveAudioVR/Install In Current Scene", priority = 10)]
        public static void InstallInCurrentScene()
        {
            Scene activeScene = SceneManager.GetActiveScene();
            if (!activeScene.IsValid() || string.IsNullOrWhiteSpace(activeScene.path))
            {
                EditorUtility.DisplayDialog(
                    "Adaptive Audio VR",
                    "Open a saved Unity scene before running the installer.",
                    "OK");
                return;
            }

            GameObject installerObject = GameObject.Find(InstallerObjectName);
            if (installerObject == null)
            {
                installerObject = new GameObject(InstallerObjectName);
                Undo.RegisterCreatedObjectUndo(installerObject, "Create Adaptive Audio Integration");
            }

            AdaptiveAudioVrSceneInstaller installer = installerObject.GetComponent<AdaptiveAudioVrSceneInstaller>();
            bool addedInstaller = installer == null;
            if (installer == null)
            {
                installer = Undo.AddComponent<AdaptiveAudioVrSceneInstaller>(installerObject);
            }

            if (addedInstaller)
            {
                string sceneDisplayName = activeScene.name;
                SetField(installer, "environmentId", ToEnvironmentId(activeScene.name));
                SetField(installer, "environmentDisplayName", sceneDisplayName);
                SetField(
                    installer,
                    "meditationMusicReference",
                    $"serene instrumental meditation music that fits {sceneDisplayName}; music only, no environmental sound effects");
            }

            if (GetFieldValue<AudioClip>(installer, "fixedAmbientClip") == null)
            {
                Debug.LogWarning(
                    "[AdaptiveAudioVrSceneSetupEditor] Assign this scene's fixed ambient clip on AdaptiveAudioIntegration before testing.",
                    installerObject);
            }

            if (GetFieldValue<AudioClip>(installer, "fallbackMeditationClip") == null)
            {
                SetField(installer, "fallbackMeditationClip", FindFirstAudioClip(MeditationSearchFolder));
            }

            SetField(installer, "includeValidationDashboard", false);
            SetField(installer, "includeRealtimeBridge", true);
            SetField(installer, "installOnAwake", true);
            SetField(installer, "autoGenerateMeditationClipOnStart", false);
            SetField(installer, "autoGenerateOnActionChange", false);
            SetField(installer, "autoCheckRealtimeCapabilityOnStart", true);

            installer.InstallAdaptiveAudioRuntime();

            EditorUtility.SetDirty(installerObject);
            EditorSceneManager.MarkSceneDirty(activeScene);
            EditorSceneManager.SaveOpenScenes();

            Debug.Log("[AdaptiveAudioVrSceneSetupEditor] Adaptive audio runtime installed into the current VR scene.", installerObject);
        }

        [MenuItem("AdaptiveAudioVR/Install In Current Scene", true)]
        private static bool ValidateInstallInCurrentScene()
        {
            Scene activeScene = SceneManager.GetActiveScene();
            return activeScene.IsValid() && !string.IsNullOrWhiteSpace(activeScene.path);
        }

        private static AudioClip FindFirstAudioClip(string folder)
        {
            string[] guids = AssetDatabase.FindAssets("t:AudioClip", new[] { folder });
            if (guids == null || guids.Length == 0)
            {
                Debug.LogWarning($"[AdaptiveAudioVrSceneSetupEditor] No audio clips found in {folder}.");
                return null;
            }

            string selectedPath = guids
                .Select(AssetDatabase.GUIDToAssetPath)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();

            if (string.IsNullOrWhiteSpace(selectedPath))
            {
                return null;
            }

            return AssetDatabase.LoadAssetAtPath<AudioClip>(selectedPath);
        }

        private static string ToEnvironmentId(string sceneName)
        {
            if (string.IsNullOrWhiteSpace(sceneName))
            {
                return "default";
            }

            var characters = sceneName
                .Trim()
                .Select(character => char.IsLetterOrDigit(character) ? char.ToLowerInvariant(character) : '_')
                .ToArray();

            return new string(characters).Trim('_');
        }

        private static T GetFieldValue<T>(Component target, string fieldName) where T : class
        {
            if (target == null)
            {
                return null;
            }

            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            return field != null ? field.GetValue(target) as T : null;
        }

        private static void SetField(Component target, string fieldName, object value)
        {
            if (target == null)
            {
                return;
            }

            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null)
            {
                return;
            }

            field.SetValue(target, value);
            EditorUtility.SetDirty(target);
        }
    }
}
