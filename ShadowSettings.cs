using OpenTK.Mathematics;

namespace GPR202_Assessment3
{
    public class ShadowSettings
    {
        // Directional light directions for different times of day
        public Vector3 DawnDirection { get; set; } = new Vector3(-0.65f, -0.55f, 0.45f);
        public Vector3 SunriseDirection { get; set; } = new Vector3(-0.85f, -0.40f, 0.25f);
        public Vector3 MorningDirection { get; set; } = new Vector3(-0.95f, -0.30f, -0.10f);
        public Vector3 MiddayDirection { get; set; } = new Vector3(-0.20f, -0.95f, -0.15f);
        public Vector3 AfternoonDirection { get; set; } = new Vector3(0.55f, -0.65f, -0.35f);
        public Vector3 SunsetDirection { get; set; } = new Vector3(0.85f, -0.40f, -0.25f);
        public Vector3 DuskDirection { get; set; } = new Vector3(0.65f, -0.55f, 0.45f);
        public Vector3 NightDirection { get; set; } = new Vector3(0.25f, -0.85f, 0.55f);
        public Vector3 MidnightDirection { get; set; } = new Vector3(0.10f, -0.98f, 0.10f);

        // Shadow map resolution and coverage
        public int Resolution { get; set; } = 4096;
        public float HalfExtent { get; set; } = 400f;
        public float NearDist { get; set; } = -900f;
        public float FarDist { get; set; } = 1800f;

        // Depth pass polygon offset
        public float PolygonOffsetFactor { get; set; } = 0.75f;
        public float PolygonOffsetUnits { get; set; } = 1.0f;

        // Shadows received by terrain, including the plane's ground shadow
        public float TerrainSlopeBiasScale { get; set; } = 0.0012f;
        public float TerrainMinBias { get; set; } = 0.00025f;
        public float TerrainPcfSpread { get; set; } = 1.0f;
        public float TerrainSoftEdgeRange { get; set; } = 0.0003f;
        public float TerrainShadowStrength { get; set; } = 0.85f;

        // Shadows received by the plane
        public float PlaneBias { get; set; } = 0.00025f;
        public float PlanePcfSpread { get; set; } = 1.0f;
        public float PlaneSoftEdgeRange { get; set; } = 0.00015f;
        public float PlaneShadowStrength { get; set; } = 0.65f;
    }
}
