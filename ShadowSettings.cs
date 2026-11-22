using System;

namespace GPR202_Assessment3
{
    public class ShadowSettings
    {
        // --- Shadow Map Resolution & Frustum ---
        public int Resolution { get; set; } = 4096;
        public float HalfExtent { get; set; } = 400f;
        public float NearDist { get; set; } = -900f;
        public float FarDist { get; set; } = 1800f;

        // --- Depth Pass Polygon Offset ---
        public float PolygonOffsetFactor { get; set; } = 0.75f;
        public float PolygonOffsetUnits { get; set; } = 1.0f;

        // --- Terrain Shadow Sampling ---
        public float TerrainSlopeBiasScale { get; set; } = 0.0012f;
        public float TerrainMinBias { get; set; } = 0.00025f;
        public float TerrainPcfSpread { get; set; } = 1.0f;
        public float TerrainSoftEdgeRange { get; set; } = 0.0008f;
        public float TerrainShadowStrength { get; set; } = 1.0f;

        // --- Plane Shadow Sampling ---
        public float PlaneBias { get; set; } = 0.00025f;
        public float PlanePcfSpread { get; set; } = 1.0f;
        public float PlaneSoftEdgeRange { get; set; } = 0.0008f;
        public float PlaneShadowStrength { get; set; } = 0.75f;
    }
}
