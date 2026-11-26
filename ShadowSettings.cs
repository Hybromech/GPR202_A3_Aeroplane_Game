using OpenTK.Mathematics;

namespace GPR202_Assessment3
{
    public class ShadowSettings
    {
        public float PropellerRpm { get; set; } = 900f;

        public bool GpuTimingEnabled { get; set; } = true;
        // Narrower 3x3 terrain kernel; false restores the wider tapered filter.
        public bool TerrainFastShadows { get; set; } = true;

        public bool TerrainLodEnabled { get; set; } = true;
        public float TerrainLodNearDistance { get; set; } = 250f;
        public float TerrainLodMediumDistance { get; set; } = 600f;
        public float TerrainLodFarDistance { get; set; } = 1100f;

        // Bloom: luminance threshold, additive strength and horizontal/vertical blur pairs.
        public bool BloomEnabled { get; set; } = true;
        public float BloomThreshold { get; set; } = 1.0f;
        public float BloomStrength { get; set; } = 0.2f;
        // 1 = full resolution; 2 = half width/height.
        public int BloomDownsample { get; set; } = 2;
        public int BloomBlurIterations { get; set; } = 5;

        // Balloon highlights feeding the shared bloom pass.
        public bool BalloonBloomEnabled { get; set; } = true;
        public float BalloonBloomThreshold { get; set; } = 0.2f;
        public float BalloonBloomIntensity { get; set; } = 5.0f;

        // Plane metallic material: high metalness and moderately polished finish.
        public float PlaneMetalness { get; set; } = 0.9f;
        public float PlaneRoughness { get; set; } = 0.25f;

        // Plane HDR highlights feeding the scene bloom pass.
        public bool PlaneBloomEnabled { get; set; } = true;
        public float PlaneBloomThreshold { get; set; } = 0.6f;
        public float PlaneBloomIntensity { get; set; } = 2.0f;

        // Scene ambient fill. Colour is linear RGB; intensity 0 disables the fill.
        public bool AmbientEnabled { get; set; } = true;
        public Vector3 AmbientColor { get; set; } = new Vector3(1.0f, 1.0f, 1.12f);
        public float AmbientIntensity { get; set; } = 0.55f; //0.25f

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
