using System;
using System.Collections;
using LaminarVR.AdaptiveMeditation.Runtime.Environment;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace LaminarVR.AdaptiveMeditation.Tests.PlayMode
{
    public sealed class TemplePondSkyMotionPlayModeTests
    {
        private GameObject root;
        private Material previousSky;
        private Material sky;
        private Material birds;
        private TemplePondSkyMotionProfile profile;
        private TemplePondSkyMotion motion;

        [SetUp]
        public void SetUp()
        {
            previousSky = RenderSettings.skybox;
            sky = new Material(Shader.Find("Skybox/6 Sided"));
            sky.SetFloat("_Rotation", 12f);
            RenderSettings.skybox = sky;
            birds = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            profile = ScriptableObject.CreateInstance<TemplePondSkyMotionProfile>();
            root = new GameObject("Sky motion test");
            root.SetActive(false);
            motion = root.AddComponent<TemplePondSkyMotion>();
            motion.Configure(profile, birds);
            root.SetActive(true);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Object.Destroy(root);
            yield return null;
            RenderSettings.skybox = previousSky;
            Object.Destroy(sky);
            Object.Destroy(birds);
            Object.Destroy(profile);
        }

        [UnityTest]
        public IEnumerator AnimatesFlockAndPrivateSkyWithoutChangingSourceMaterial()
        {
            var mesh = root.GetComponentInChildren<MeshFilter>().sharedMesh;
            var firstPosition = mesh.vertices[0];
            Assert.That(mesh.vertexCount, Is.EqualTo(profile.BirdCount * 10));
            Assert.That(root.GetComponentsInChildren<MeshRenderer>().Length, Is.EqualTo(1));
            Assert.That(RenderSettings.skybox, Is.Not.SameAs(sky));
            yield return new WaitForSeconds(0.2f);
            Assert.That(RenderSettings.skybox.GetFloat("_Rotation"), Is.GreaterThan(12f));
            Assert.That(sky.GetFloat("_Rotation"), Is.EqualTo(12f));
            Assert.That(mesh.vertices[0], Is.Not.EqualTo(firstPosition));
        }

        [UnityTest]
        public IEnumerator DisableRestoresSkyAndReenableDoesNotDuplicateFlock()
        {
            motion.enabled = false;
            Assert.That(RenderSettings.skybox, Is.SameAs(sky));
            yield return null;
            Assert.That(root.GetComponentsInChildren<MeshRenderer>().Length, Is.Zero);
            motion.enabled = true;
            yield return null;
            Assert.That(root.GetComponentsInChildren<MeshRenderer>().Length, Is.EqualTo(1));
            // A new scene/system's sky must not be overwritten during cleanup.
            RenderSettings.skybox = previousSky;
            motion.enabled = false;
            Assert.That(RenderSettings.skybox, Is.SameAs(previousSky));
        }

        [TestCase(-0.1f)]
        [TestCase(1.1f)]
        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        public void RejectsInvalidNormalizedMotion(float value)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => motion.ApplyAmbientMotion(value));
        }

        [TestCase("{\"skyDegreesPerSecond\":{\"x\":-1,\"y\":1}}")]
        [TestCase("{\"flightMetersPerSecond\":{\"x\":2,\"y\":1}}")]
        [TestCase("{\"birdCount\":100}")]
        [TestCase("{\"orbitRadiusMeters\":0}")]
        public void RejectsMalformedMotionProfile(string json)
        {
            Assert.That(profile.IsValid, Is.True);
            JsonUtility.FromJsonOverwrite(json, profile);
            Assert.That(profile.IsValid, Is.False);
        }
    }
}
