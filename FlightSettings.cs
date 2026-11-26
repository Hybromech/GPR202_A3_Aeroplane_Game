namespace GPR202_Assessment3
{
    // SI units; a tunable simplified aircraft, not a certified P-51 flight model.
    public sealed class FlightSettings
    {
        public float Mass { get; set; } = 3500f;
        public float WingArea { get; set; } = 22f;
        public float AirDensity { get; set; } = 1.225f;
        public float MaxThrust { get; set; } = 12000f;
        public float LiftSlope { get; set; } = 5.2f;
        public float ZeroAngleLift { get; set; } = 0.2f;
        public float StallAngleDegrees { get; set; } = 16f;
        public float ParasiticDrag { get; set; } = 0.025f;
        public float InducedDrag { get; set; } = 0.065f;
        public float PitchAuthority { get; set; } = 1.5f;
        // Maximum roll rate in radians/second; input scales this directly, independent of airspeed.
        public float RollAuthority { get; set; } = 3.0f;
        public float YawAuthority { get; set; } = 0.6f;
        public float StartSpeed { get; set; } = 85f;
    }
}
