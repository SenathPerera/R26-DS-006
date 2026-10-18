using System;
using LaminarVR.AdaptiveMeditation.Runtime.Application;
using LaminarVR.AdaptiveMeditation.Session;
using UnityEngine;
using UnityEngine.Rendering;

namespace LaminarVR.AdaptiveMeditation.Runtime.Environment
{
    [DisallowMultipleComponent]
    [AddComponentMenu("Adaptive Meditation/Environment/Temple Pond Sky Motion")]
    public sealed class TemplePondSkyMotion : MonoBehaviour
    {
        [SerializeField] private TemplePondSkyMotionProfile profile;
        [SerializeField] private Material birdMaterial;
        [SerializeField] private ProductionSessionCoordinator sessionCoordinator;

        private static readonly int RotationId = Shader.PropertyToID("_Rotation");
        private Material originalSkybox;
        private Material runtimeSkybox;
        private GameObject flock;
        private Mesh mesh;
        private Vector3[] vertices;
        private float ambientMotion = 0.5f;
        private float skyRotation;
        private float orbitRadians;
        private float wingbeatRadians;

        public void Configure(TemplePondSkyMotionProfile motionProfile,
            Material silhouetteMaterial, ProductionSessionCoordinator coordinator = null)
        {
            if (mesh != null)
                throw new InvalidOperationException("Disable sky motion before configuring it.");
            profile = motionProfile;
            birdMaterial = silhouetteMaterial;
            sessionCoordinator = coordinator;
        }

        public void ApplyAmbientMotion(float normalizedMotion)
        {
            if (float.IsNaN(normalizedMotion) || float.IsInfinity(normalizedMotion)
                || normalizedMotion < 0f || normalizedMotion > 1f)
                throw new ArgumentOutOfRangeException(nameof(normalizedMotion));
            ambientMotion = normalizedMotion;
        }

        private void OnEnable()
        {
            if (profile == null || !profile.IsValid || birdMaterial == null)
            {
                Debug.LogError("Temple sky motion requires a valid profile and bird material.", this);
                enabled = false;
                return;
            }

            originalSkybox = RenderSettings.skybox;
            if (originalSkybox != null && originalSkybox.HasProperty(RotationId))
            {
                runtimeSkybox = new Material(originalSkybox) { name = "Temple Sky (Runtime)" };
                skyRotation = originalSkybox.GetFloat(RotationId);
                RenderSettings.skybox = runtimeSkybox;
            }

            // One small mesh, one material, no physics, shadows or per-bird Updates.
            flock = new GameObject("Distant Bird Flock");
            flock.transform.SetParent(transform, false);
            mesh = new Mesh { name = "Temple Bird Silhouettes" };
            mesh.MarkDynamic();
            vertices = new Vector3[profile.BirdCount * 10];
            var triangles = new int[profile.BirdCount * 24];
            var face = new[] { 0, 1, 2, 0, 2, 3, 4, 5, 6, 7, 8, 9 };
            for (var bird = 0; bird < profile.BirdCount; bird++)
            {
                for (var index = 0; index < face.Length; index += 3)
                {
                    var target = bird * 24 + index * 2;
                    var offset = bird * 10;
                    triangles[target] = triangles[target + 3] = offset + face[index];
                    triangles[target + 1] = triangles[target + 5] = offset + face[index + 1];
                    triangles[target + 2] = triangles[target + 4] = offset + face[index + 2];
                }
            }
            UpdateFlock();
            mesh.triangles = triangles;
            flock.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = flock.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = birdMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        private void Update()
        {
            if (sessionCoordinator != null
                && (sessionCoordinator.IsHeadsetLifecycleSuspended
                    || sessionCoordinator.Phase == VrSessionPhase.Paused
                    || sessionCoordinator.Phase == VrSessionPhase.Completed
                    || sessionCoordinator.Phase == VrSessionPhase.Aborted))
                return;

            // Bound long-frame steps so resuming a suspended app cannot jump the sky.
            var deltaSeconds = Mathf.Min(Time.deltaTime, 0.1f);
            skyRotation = Mathf.Repeat(skyRotation + profile.SkySpeed(ambientMotion) * deltaSeconds, 360f);
            if (runtimeSkybox != null && RenderSettings.skybox == runtimeSkybox)
                runtimeSkybox.SetFloat(RotationId, skyRotation);

            orbitRadians = Mathf.Repeat(orbitRadians
                + profile.FlightSpeed(ambientMotion) / profile.OrbitRadiusMeters * deltaSeconds,
                Mathf.PI * 2f);
            wingbeatRadians = Mathf.Repeat(wingbeatRadians
                + profile.WingbeatHz * Mathf.PI * 2f * deltaSeconds, Mathf.PI * 2f);
            UpdateFlock();
        }

        private void UpdateFlock()
        {
            var radial = new Vector3(Mathf.Sin(orbitRadians), 0f, Mathf.Cos(orbitRadians));
            var forward = new Vector3(radial.z, 0f, -radial.x);
            var halfSpan = profile.WingspanMeters * 0.5f;
            for (var bird = 0; bird < vertices.Length / 10; bird++)
            {
                var row = (bird + 1) / 2;
                var side = bird == 0 ? 0 : (bird % 2 == 0 ? 1 : -1);
                var center = radial * (profile.OrbitRadiusMeters + side * row * profile.FormationSpacingMeters)
                    - forward * (row * profile.FormationSpacingMeters);
                var lift = Vector3.up * (Mathf.Sin(wingbeatRadians + bird * 0.8f) * halfSpan * 0.25f);
                var index = bird * 10;
                vertices[index] = center + forward * halfSpan * 0.65f;
                vertices[index + 1] = center + radial * halfSpan * 0.12f;
                vertices[index + 2] = center - forward * halfSpan * 0.55f;
                vertices[index + 3] = center - radial * halfSpan * 0.12f;
                vertices[index + 4] = center + forward * halfSpan * 0.2f;
                vertices[index + 5] = center + radial * halfSpan - forward * halfSpan * 0.2f + lift;
                vertices[index + 6] = center - forward * halfSpan * 0.35f;
                vertices[index + 7] = vertices[index + 4];
                vertices[index + 8] = center - radial * halfSpan - forward * halfSpan * 0.2f + lift;
                vertices[index + 9] = vertices[index + 6];
            }
            mesh.vertices = vertices;
            mesh.RecalculateBounds();
        }

        private void OnDisable()
        {
            // Never edit the imported skybox material or overwrite a replacement sky.
            if (runtimeSkybox != null && RenderSettings.skybox == runtimeSkybox)
                RenderSettings.skybox = originalSkybox;
            if (runtimeSkybox != null) Destroy(runtimeSkybox);
            if (flock != null) Destroy(flock);
            if (mesh != null) Destroy(mesh);
            runtimeSkybox = null;
            flock = null;
            mesh = null;
        }
    }
}
