using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using LaminarVR.AdaptiveMeditation.Environment;
using LaminarVR.AdaptiveMeditation.Runtime.Configuration;
using LaminarVR.AdaptiveMeditation.Runtime.Environment;
using UnityEditor;

namespace LaminarVR.AdaptiveMeditation.ResearchEvaluation
{
    public static class LongitudinalEvaluationProfileLoader
    {
        public const string ProtocolId =
            "laminar-vr-longitudinal-simulation-v1.0";
        public const string PilotBundleId = "temple-pond-pilot-freeze-v3";

        private const string ScenePath =
            "Assets/Laminar VR/Configuration/Environment/"
            + "TemplePondSceneParameterProfile_VisualV3.asset";
        private const string MappingPath =
            "Assets/Laminar VR/Configuration/Environment/"
            + "TemplePondEnvironmentMappingProfile_v4.asset";
        private const double Tolerance = 1e-5d;
        private const string SceneSha256Lf =
            "545f27b0d60540e28968c2f2cd1bfd1acf8c6541f75b80e52de6269e13db6885";
        private const string MappingSha256Lf =
            "85201f85c8ffc20486bf6dc15384426d043a21c46af472cfd2658b3cfd46f697";

        public static bool TryLoad(
            out EvaluationRuntimeBundle bundle,
            out TemplePondEnvironmentMapping mapping,
            out string validationError)
        {
            bundle = null;
            mapping = null;
            if (!FrozenEvaluationProfileLoader.TryLoad(
                    out var historical,
                    out validationError))
            {
                return false;
            }

            var sceneAsset = AssetDatabase.LoadAssetAtPath<SceneParameterProfile>(
                ScenePath);
            var mappingAsset = AssetDatabase.LoadAssetAtPath<
                TemplePondEnvironmentMappingProfile>(MappingPath);
            if (sceneAsset == null || mappingAsset == null)
            {
                validationError = "The v3 scene or v4 mapping asset is missing.";
                return false;
            }

            if (!MatchesLfSha256(ScenePath, SceneSha256Lf)
                || !MatchesLfSha256(MappingPath, MappingSha256Lf))
            {
                validationError =
                    "The v3 visual assets differ from the study lock.";
                return false;
            }

            var sceneGuid = AssetDatabase.AssetPathToGUID(ScenePath);
            var mappingGuid = AssetDatabase.AssetPathToGUID(MappingPath);
            var sceneFile = Path.Combine(
                UnityEngine.Application.dataPath,
                "Laminar VR", "Scenes", "JapaneseTemplePondGarden.unity");
            if (!File.Exists(sceneFile)
                || !SceneReferences(File.ReadAllText(sceneFile),
                    "sceneParameterProfile", sceneGuid)
                || !SceneReferences(File.ReadAllText(sceneFile),
                    "mappingProfile", mappingGuid))
            {
                validationError =
                    "Temple Pond does not bind the v3 scene and v4 mapping assets.";
                return false;
            }

            if (!sceneAsset.TryCreateRuntimeProfile(
                    out var scene,
                    out validationError)
                || !mappingAsset.TryCreateRuntimeMapping(
                    out mapping,
                    out validationError))
            {
                return false;
            }

            if (!string.Equals(scene.SceneId, "temple-pond", StringComparison.Ordinal)
                || !Near(scene.ActionSteps.Illumination, 0.15d)
                || !Near(scene.ActionSteps.Warmth, 0.35d)
                || !Near(scene.ActionSteps.AtmosphericSoftness, 0.4d)
                || !Near(scene.ActionSteps.ColorRichness, 0.3d)
                || !Near(scene.ActionSteps.AmbientMotion, 0.3d)
                || !Near(scene.TransitionDurationSeconds, 6d)
                || !string.Equals(
                    mapping.ConfigurationId,
                    "temple-pond-mapping-pilot-v4",
                    StringComparison.Ordinal)
                || mapping.ConfigurationVersion != 4
                || !Near(mapping.SaturationRange.x, -30d)
                || !Near(mapping.SaturationRange.y, 30d)
                || !Near(mapping.WaterMotionRange.x, 0.1d)
                || !Near(mapping.WaterMotionRange.y, 0.6d))
            {
                bundle = null;
                mapping = null;
                validationError = "The longitudinal visual bundle differs from v3.";
                return false;
            }

            try
            {
                bundle = new EvaluationRuntimeBundle(
                    scene,
                    historical.Timing,
                    historical.Physiology,
                    historical.Reward,
                    historical.Coordinator,
                    historical.Preference,
                    historical.RuleBased,
                    historical.LinUcb,
                    historical.Stabilization,
                    historical.Telemetry,
                    historical.FeatureVectorBuilder);
                validationError = string.Empty;
                return true;
            }
            catch (ArgumentException exception)
            {
                validationError = exception.Message;
                return false;
            }
        }

        private static bool Near(double actual, double expected)
        {
            return Math.Abs(actual - expected) <= Tolerance;
        }

        private static bool MatchesLfSha256(string assetPath, string expected)
        {
            var path = Path.Combine(
                UnityEngine.Application.dataPath,
                assetPath.Substring("Assets/".Length)
                    .Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path)) return false;
            var text = File.ReadAllText(path).Replace("\r\n", "\n");
            using (var sha = SHA256.Create())
            {
                var hash = BitConverter.ToString(
                        sha.ComputeHash(Encoding.UTF8.GetBytes(text)))
                    .Replace("-", string.Empty).ToLowerInvariant();
                return string.Equals(hash, expected, StringComparison.Ordinal);
            }
        }

        private static bool SceneReferences(
            string sceneText, string propertyName, string guid)
        {
            return !string.IsNullOrEmpty(guid)
                && Regex.IsMatch(
                    sceneText,
                    @"\b" + propertyName
                    + @":\s*\{fileID:\s*11400000,\s*guid:\s*"
                    + guid + @",\s*type:\s*2\}");
        }
    }
}
