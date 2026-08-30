#if UNITY_EDITOR || DEVELOPMENT_BUILD
using LaminarVR.AdaptiveMeditation.Physiology;
using LaminarVR.AdaptiveMeditation.Runtime.Simulation;
using NUnit.Framework;
using UnityEngine;

namespace LaminarVR.AdaptiveMeditation.Tests.EditMode.Simulation
{
    public sealed class LocalPhysiologySimulationProfileTests
    {
        [Test]
        public void NewProfile_IsDisabledAndCannotScheduleEmission()
        {
            var asset = ScriptableObject.CreateInstance<LocalPhysiologySimulationProfile>();

            try
            {
                var created = asset.TryGetEmissionInterval(
                    out _,
                    out var validationError);

                Assert.That(created, Is.False);
                Assert.That(validationError, Does.Contain("not enabled"));
            }
            finally
            {
                Object.DestroyImmediate(asset);
            }
        }

        [Test]
        public void EnabledProfile_CreatesComponentBCompatiblePointWindow()
        {
            const string json = @"{
                ""developmentSimulationEnabled"": true,
                ""emissionIntervalSeconds"": 5.0,
                ""windowDurationSeconds"": 60.0,
                ""sourceTimestampOffsetSeconds"": 0.0,
                ""heartRateBpm"": 78.0,
                ""includeRmssd"": true,
                ""rmssdMs"": 34.0,
                ""includeSdnn"": true,
                ""sdnnMs"": 42.0,
                ""pointStressLevel"": 2,
                ""stressLabel"": ""moderate"",
                ""stressConfidence"": 0.5,
                ""level0Probability"": 0.1,
                ""level1Probability"": 0.2,
                ""level2Probability"": 0.6,
                ""level3Probability"": 0.1,
                ""continuousStressScore"": 1.7,
                ""signalQuality"": 0.95
            }";
            var asset = ScriptableObject.CreateInstance<LocalPhysiologySimulationProfile>();

            try
            {
                JsonUtility.FromJsonOverwrite(json, asset);

                var schedulable = asset.TryGetEmissionInterval(
                    out var intervalSeconds,
                    out var validationError);
                var window = asset.CreateWindow(1000d);
                var validation = new PhysiologyWindowValidator(
                    CreateValidationConfiguration()).Validate(window, 1000d);

                Assert.That(schedulable, Is.True, validationError);
                Assert.That(intervalSeconds, Is.EqualTo(5d));
                Assert.That(validation.Accepted, Is.True);
                Assert.That(window.WindowDurationSeconds, Is.EqualTo(60d));
                Assert.That(window.Stress.Mode, Is.EqualTo(StressDecisionMode.Point));
                Assert.That(window.Stress.PointLevel, Is.EqualTo(2));
                Assert.That(window.Stress.Label, Is.EqualTo("moderate"));
                Assert.That(window.RmssdMs, Is.EqualTo(34d));
                Assert.That(window.SdnnMs, Is.EqualTo(42d));
            }
            finally
            {
                Object.DestroyImmediate(asset);
            }
        }

        private static PhysiologyValidationConfiguration
            CreateValidationConfiguration()
        {
            return new PhysiologyValidationConfiguration(
                "simulation-test",
                1,
                90d,
                30d,
                2d,
                0.001d,
                0.005d,
                0.8d,
                0.9d,
                4);
        }
    }
}
#endif

