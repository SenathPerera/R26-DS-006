using UnityEngine;

namespace LaminarVR.AdaptiveMeditation.Runtime.Environment
{
    [CreateAssetMenu(menuName = "Adaptive Meditation/Environment/Temple Sky Motion")]
    public sealed class TemplePondSkyMotionProfile : ScriptableObject
    {
        // TODO(RESEARCH_DECISION): These are visual preview settings. Calibrate
        // sky rotation, flight speed and flock geometry on Quest before study use.
        [SerializeField] private Vector2 skyDegreesPerSecond = new Vector2(0.02f, 0.08f);
        [SerializeField] private Vector2 flightMetersPerSecond = new Vector2(0.6f, 1.2f);
        [SerializeField, Range(1, 12)] private int birdCount = 7;
        [SerializeField, Min(20f)] private float orbitRadiusMeters = 55f;
        [SerializeField, Min(0.1f)] private float wingspanMeters = 1.6f;
        [SerializeField, Min(0.1f)] private float formationSpacingMeters = 2.5f;
        [SerializeField, Range(0.05f, 0.5f)] private float wingbeatHz = 0.3f;

        public int BirdCount => Mathf.Clamp(birdCount, 1, 12);
        public float OrbitRadiusMeters => Mathf.Max(20f, orbitRadiusMeters);
        public float WingspanMeters => Mathf.Max(0.1f, wingspanMeters);
        public float FormationSpacingMeters => Mathf.Max(0.1f, formationSpacingMeters);
        public float WingbeatHz => Mathf.Clamp(wingbeatHz, 0.05f, 0.5f);
        public float SkySpeed(float motion) => Mathf.Lerp(skyDegreesPerSecond.x, skyDegreesPerSecond.y, motion);
        public float FlightSpeed(float motion) => Mathf.Lerp(flightMetersPerSecond.x, flightMetersPerSecond.y, motion);

        public bool IsValid => IsValidRange(skyDegreesPerSecond)
            && IsValidRange(flightMetersPerSecond)
            && birdCount >= 1 && birdCount <= 12
            && IsFinite(orbitRadiusMeters) && orbitRadiusMeters >= 20f
            && IsFinite(wingspanMeters) && wingspanMeters >= 0.1f
            && IsFinite(formationSpacingMeters) && formationSpacingMeters >= 0.1f
            && IsFinite(wingbeatHz) && wingbeatHz >= 0.05f && wingbeatHz <= 0.5f;

        private static bool IsValidRange(Vector2 range) => IsFinite(range.x)
            && IsFinite(range.y) && range.x >= 0f && range.y >= range.x;

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
