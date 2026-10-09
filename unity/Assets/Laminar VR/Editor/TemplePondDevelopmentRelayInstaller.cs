using System;
using System.IO;
using AdaptiveAudioVR.Integration;
using LaminarVR.AdaptiveMeditation.Runtime.Application;
using LaminarVR.AdaptiveMeditation.Runtime.Configuration;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using Object = UnityEngine.Object;

namespace LaminarVR.AdaptiveMeditation.Editor
{
    public static class TemplePondDevelopmentRelayInstaller
    {
        private const string ProfilePath =
            "Assets/Laminar VR/Configuration/Networking/"
            + "TemplePondDevelopmentSessionRelayProfile.asset";

        private const string ComponentBProfilePath =
            "Assets/Laminar VR/Configuration/Networking/"
            + "ComponentBQuestStreamConnectionProfile.asset";

        private const string DevelopmentNetworkProfilePath =
            "Assets/Laminar VR/Configuration/Networking/"
            + "LocalDevelopmentNetworkProfile.asset";

        private const string DevelopmentHostEnvironmentVariable =
            "MINDSYNC_DEVELOPMENT_HOST";

        private const string InputActionsPath =
            "Assets/Samples/XR Interaction Toolkit/3.0.11/Starter Assets/"
            + "XRI Default Input Actions.inputactions";

        [MenuItem(
            "Adaptive Meditation/Configure Temple Pond Development Relay")]
        public static void Configure()
        {
            var developmentNetworkProfile =
                LoadOrCreateDevelopmentNetworkProfile();
            if (!TrySyncDevelopmentHost(
                    developmentNetworkProfile,
                    out string syncMessage)
                && !developmentNetworkProfile.TryGetLyriaHttpBaseUrl(
                    out _,
                    out _))
            {
                EditorUtility.DisplayDialog(
                    "Temple Pond Relay",
                    syncMessage,
                    "OK");
                return;
            }

            var root = GameObject.Find("AdaptiveEnvironment");
            if (root == null)
            {
                EditorUtility.DisplayDialog(
                    "Temple Pond Relay",
                    "Open JapaneseTemplePondGarden and ensure the "
                    + "AdaptiveEnvironment object exists.",
                    "OK");
                return;
            }

            var bootstrap = root.GetComponent<ApplicationBootstrap>();
            var coordinator = root.GetComponent<
                ProductionSessionCoordinator>();
            var boundary = root.GetComponent<VisualSessionBoundary>();
            if (bootstrap == null || coordinator == null || boundary == null)
            {
                EditorUtility.DisplayDialog(
                    "Temple Pond Relay",
                    "AdaptiveEnvironment is missing an existing production "
                    + "bootstrap, coordinator, or visual boundary.",
                    "OK");
                return;
            }

            var profile = LoadOrCreateProfile(developmentNetworkProfile);
            ConfigureComponentBProfile(developmentNetworkProfile);
            var inputActions = AssetDatabase.LoadAssetAtPath<InputActionAsset>(
                InputActionsPath);
            if (inputActions == null)
            {
                EditorUtility.DisplayDialog(
                    "Temple Pond Relay",
                    "The XRI Default Input Actions asset could not be found. "
                    + "Reimport the XR Interaction Toolkit Starter Assets "
                    + "sample before configuring the relay.",
                    "OK");
                return;
            }

            var bridge = GetOrAdd<SessionRelayBridge>(root);
            var pairing = GetOrAdd<SessionRelayPairingController>(root);
            var panel = GetOrAdd<QuestPairingRuntimePanel>(root);
            var audioInstaller = Object.FindAnyObjectByType<
                AdaptiveAudioVrSceneInstaller>();

            SetReference(bridge, "applicationBootstrap", bootstrap);
            SetReference(bridge, "productionCoordinator", coordinator);
            SetReference(bridge, "visualSessionBoundary", boundary);
            SetReference(pairing, "connectionProfile", profile);
            SetReference(pairing, "sessionRelayBridge", bridge);
            SetReference(panel, "pairingController", pairing);
            SetReference(panel, "inputActions", inputActions);
            if (audioInstaller != null)
            {
                SetReference(
                    audioInstaller,
                    "developmentNetworkProfile",
                    developmentNetworkProfile);
            }

            PlayerSettings.SetApplicationIdentifier(
                NamedBuildTarget.Android,
                "com.mindsyncvr.templepond");
            PlayerSettings.insecureHttpOption =
                InsecureHttpOption.DevelopmentOnly;

            EditorUtility.SetDirty(root);
            EditorSceneManager.MarkSceneDirty(root.scene);
            Selection.activeGameObject = root;
            EditorUtility.DisplayDialog(
                "Temple Pond Relay",
                "Development relay profile and Quest pairing components are "
                + "configured. Save the scene, start the relay on port 8080, "
                + "and enter Play Mode.",
                "OK");
        }

        [MenuItem(
            "Adaptive Meditation/Sync Local Development Host From Environment")]
        public static void SyncLocalDevelopmentHostFromEnvironment()
        {
            var profile = LoadOrCreateDevelopmentNetworkProfile();
            bool synchronized = TrySyncDevelopmentHost(
                profile,
                out string message);
            EditorUtility.DisplayDialog(
                "Local Development Host",
                message,
                "OK");
            if (synchronized)
            {
                Selection.activeObject = profile;
            }
        }

        [InitializeOnLoadMethod]
        private static void SyncLocalDevelopmentHostAfterScriptReload()
        {
            EditorApplication.delayCall += () =>
            {
                var profile = AssetDatabase.LoadAssetAtPath<
                    LocalDevelopmentNetworkProfile>(
                    DevelopmentNetworkProfilePath);
                if (profile != null)
                {
                    TrySyncDevelopmentHost(profile, out _);
                }
            };
        }

        internal static void SynchronizeLocalDevelopmentHostForBuild()
        {
            var profile = LoadOrCreateDevelopmentNetworkProfile();
            if (!TrySyncDevelopmentHost(profile, out string message)
                && !profile.TryGetLyriaHttpBaseUrl(out _, out _))
            {
                throw new BuildFailedException(message);
            }
        }

        private static SessionRelayConnectionProfile LoadOrCreateProfile(
            LocalDevelopmentNetworkProfile developmentNetworkProfile)
        {
            var profile = AssetDatabase.LoadAssetAtPath<
                SessionRelayConnectionProfile>(ProfilePath);
            if (profile == null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ProfilePath));
                profile = ScriptableObject.CreateInstance<
                    SessionRelayConnectionProfile>();
                AssetDatabase.CreateAsset(profile, ProfilePath);
            }

            var serialized = new SerializedObject(profile);
            serialized.FindProperty("configurationId").stringValue =
                "temple-pond-session-relay-dev-v1";
            serialized.FindProperty("configurationVersion").intValue = 1;
            serialized.FindProperty("deploymentConfigurationApproved")
                .boolValue = true;
            serialized.FindProperty("developmentNetworkProfile")
                .objectReferenceValue = developmentNetworkProfile;
            serialized.FindProperty("relayEndpoint").stringValue = string.Empty;
            serialized.FindProperty("schemaVersion").stringValue =
                "mindsync-session-v1";
            serialized.FindProperty("maximumMessageBytes").intValue = 65536;
            serialized.FindProperty("maximumTelemetryEventsPerBatch")
                .intValue = 32;
            serialized.FindProperty("allowInsecureDevelopmentEndpoint")
                .boolValue = true;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            return profile;
        }

        private static void ConfigureComponentBProfile(
            LocalDevelopmentNetworkProfile developmentNetworkProfile)
        {
            var profile = AssetDatabase.LoadAssetAtPath<
                ComponentBStreamConnectionProfile>(ComponentBProfilePath);
            if (profile == null)
            {
                Directory.CreateDirectory(
                    Path.GetDirectoryName(ComponentBProfilePath));
                profile = ScriptableObject.CreateInstance<
                    ComponentBStreamConnectionProfile>();
                AssetDatabase.CreateAsset(profile, ComponentBProfilePath);
            }

            var serialized = new SerializedObject(profile);
            serialized.FindProperty("configurationId").stringValue =
                "component-b-quest-stream-dev-v1";
            serialized.FindProperty("configurationVersion").intValue = 1;
            serialized.FindProperty("deploymentConfigurationApproved")
                .boolValue = true;
            serialized.FindProperty("developmentNetworkProfile")
                .objectReferenceValue = developmentNetworkProfile;
            serialized.FindProperty("streamEndpoint").stringValue = string.Empty;
            serialized.FindProperty("keepaliveIntervalSeconds").floatValue = 20f;
            serialized.FindProperty("maximumMessageBytes").intValue = 65536;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
        }

        private static LocalDevelopmentNetworkProfile
            LoadOrCreateDevelopmentNetworkProfile()
        {
            var profile = AssetDatabase.LoadAssetAtPath<
                LocalDevelopmentNetworkProfile>(
                DevelopmentNetworkProfilePath);
            if (profile != null)
            {
                return profile;
            }

            Directory.CreateDirectory(
                Path.GetDirectoryName(DevelopmentNetworkProfilePath));
            profile = ScriptableObject.CreateInstance<
                LocalDevelopmentNetworkProfile>();
            AssetDatabase.CreateAsset(
                profile,
                DevelopmentNetworkProfilePath);
            AssetDatabase.SaveAssets();
            return profile;
        }

        private static bool TrySyncDevelopmentHost(
            LocalDevelopmentNetworkProfile profile,
            out string message)
        {
            string host = System.Environment.GetEnvironmentVariable(
                DevelopmentHostEnvironmentVariable);
            string source = "the process environment";
            if (string.IsNullOrWhiteSpace(host))
            {
                string repositoryRoot = Path.GetFullPath(
                    Path.Combine(UnityEngine.Application.dataPath, "..", ".."));
                string rootEnvironmentPath = Path.Combine(
                    repositoryRoot,
                    ".env");
                string backendEnvironmentPath = Path.Combine(
                    repositoryRoot,
                    "services",
                    "lyria_backend",
                    ".env");

                if (TryReadEnvironmentValue(
                        rootEnvironmentPath,
                        DevelopmentHostEnvironmentVariable,
                        out host))
                {
                    source = "the repository .env file";
                }
                else if (TryReadEnvironmentValue(
                             backendEnvironmentPath,
                             DevelopmentHostEnvironmentVariable,
                             out host))
                {
                    source = "the Lyria backend .env file";
                }
            }

            if (string.IsNullOrWhiteSpace(host))
            {
                message =
                    $"Set {DevelopmentHostEnvironmentVariable}=<your-PC-LAN-IP> "
                    + "in services/lyria_backend/.env, then run this command again.";
                return false;
            }

            var serialized = new SerializedObject(profile);
            serialized.FindProperty("host").stringValue = host.Trim();
            serialized.ApplyModifiedPropertiesWithoutUndo();

            if (!profile.TryGetLyriaHttpBaseUrl(
                    out _,
                    out string validationError))
            {
                message = validationError;
                return false;
            }

            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            message =
                $"Local development host synchronized from {source}: "
                + profile.Host;
            return true;
        }

        private static bool TryReadEnvironmentValue(
            string filePath,
            string variableName,
            out string value)
        {
            value = string.Empty;
            if (!File.Exists(filePath))
            {
                return false;
            }

            foreach (string rawLine in File.ReadLines(filePath))
            {
                string line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith("#"))
                {
                    continue;
                }

                int separatorIndex = line.IndexOf('=');
                if (separatorIndex <= 0
                    || !string.Equals(
                        line.Substring(0, separatorIndex).Trim(),
                        variableName,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                value = line.Substring(separatorIndex + 1).Trim().Trim('"', '\'');
                return !string.IsNullOrWhiteSpace(value);
            }

            return false;
        }

        private static T GetOrAdd<T>(GameObject root)
            where T : Component
        {
            return root.GetComponent<T>() ?? Undo.AddComponent<T>(root);
        }

        private static void SetReference(
            Object owner,
            string propertyName,
            Object value)
        {
            var serialized = new SerializedObject(owner);
            serialized.FindProperty(propertyName).objectReferenceValue = value;
            serialized.ApplyModifiedProperties();
            EditorUtility.SetDirty(owner);
        }
    }

    public sealed class LocalDevelopmentNetworkBuildProcessor
        : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            TemplePondDevelopmentRelayInstaller
                .SynchronizeLocalDevelopmentHostForBuild();
        }
    }
}
