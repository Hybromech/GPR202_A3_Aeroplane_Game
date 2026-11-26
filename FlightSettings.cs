namespace GPR202_Assessment3
{
    // SI units; a tunable simplified aircraft, not a certified P-51 flight model.
    public sealed class FlightSettings
    {
        public bool AssistedControls { get; set; } = true;
        public float AssistedMaxBankDegrees { get; set; } = 60f;
        public float AssistedRollRate { get; set; } = 1.0f;
        public float AssistedPitchRate { get; set; } = 0.65f;
        public float TurnLiftAssistance { get; set; } = 0.65f;
        public float CoordinatedTurnAssistance { get; set; } = 1f;
        // Distance behind the plane in world units; rendering enforces a minimum of 1.
        public float CameraDistance { get; set; } = 15f;
        public float CameraFollowSpeed { get; set; } = 7f;
        public float StallShakeAmount { get; set; } = 0.06f;
        public float Mass { get; set; } = 3500f;
        public float WingArea { get; set; } = 22f;
        public float AirDensity { get; set; } = 1.225f;
        public float MaxThrust { get; set; } = 18000f;
        public float EngineResponseRate { get; set; } = 2f;
        // Extra drag from the windmilling propeller at closed throttle.
        public float IdlePropellerDrag { get; set; } = 0.015f;
        public float PropellerIdleFraction { get; set; } = 0.2f;
        public float LiftSlope { get; set; } = 5.2f;
        public float ZeroAngleLift { get; set; } = 0.2f;
        public float StallAngleDegrees { get; set; } = 16f;
        public float ParasiticDrag { get; set; } = 0.025f;
        public float InducedDrag { get; set; } = 0.065f;
        public float PitchAuthority { get; set; } = 1.5f;
        // Roll angular acceleration coefficient; scaled by airspeed and opposed by damping.
        public float RollAuthority { get; set; } = 3.0f;
        // Maximum absolute bank relative to the horizon.
        public float MaxBankAngleDegrees { get; set; } = 90f;
        public float YawAuthority { get; set; } = 0.6f;
        // Fraction of full throttle per wheel notch (0.05 = 5%).
        public float ThrottleWheelStep { get; set; } = 0.05f;
        public float StartSpeed { get; set; } = 85f;
    }
}
