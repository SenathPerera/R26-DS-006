using System.IO;
using LaminarVR.AdaptiveMeditation.Runtime.Application;
using LaminarVR.AdaptiveMeditation.Runtime.Configuration;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace LaminarVR.AdaptiveMeditation.Editor
{
    public static class TemplePondDevelopmentRelayInstaller
    {
        private const string ProfilePath =
            "Assets/Laminar VR/Configuration/Networking/"
            + "TemplePondDevelopmentSessionRelayProfile.asset";

        [MenuItem(
            "Adaptive Meditation/Configure Temple Pond Development Relay")]
        public static void Configure()
        {
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

            var profile = LoadOrCreateProfile();
            var bridge = GetOrAdd<SessionRelayBridge>(root);
            var pairing = GetOrAdd<SessionRelayPairingController>(root);
            var panel = GetOrAdd<QuestPairingRuntimePanel>(root);

            SetReference(bridge, "applicationBootstrap", bootstrap);
            SetReference(bridge, "productionCoordinator", coordinator);
            SetReference(bridge, "visualSessionBoundary", boundary);
            SetReference(pairing, "connectionProfile", profile);
            SetReference(pairing, "sessionRelayBridge", bridge);
            SetReference(panel, "pairingController", pairing);

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

        private static SessionRelayConnectionProfile LoadOrCreateProfile()
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
            serialized.FindProperty("relayEndpoint").stringValue =
                "ws://172.20.10.4:8080/realtime?role=quest";
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
}
