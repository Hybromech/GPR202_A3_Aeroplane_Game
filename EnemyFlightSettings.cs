using System;

namespace GPR202_Assessment3
{
    // Enemy-specific flight tuning inheriting all aerodynamic parameters from FlightSettings,
    // allowing enemy speed, agility, and guidance limits to be tuned independently of the player.
    public class EnemyFlightSettings : FlightSettings
    {
        // Enemy AI guidance & safety envelope defaults.
        public float MinTerrainClearance { get; set; } = 55f;
        public float BomberTerrainClearance { get; set; } = 85f;
        public float MaxAltitude { get; set; } = 680f;
        public float StallRecoveryAirspeed { get; set; } = 52f;
        public float MaxPitchUpRadians { get; set; } = 0.48f;
        public float MaxPitchDownRadians { get; set; } = 0.45f;
        public float MinThrottle { get; set; } = 0.25f;
        public float MaxThrottle { get; set; } = 1.0f;

        public EnemyFlightSettings()
        {
            AssistedControls = true;
            AssistedMaxBankDegrees = 70f;
        }

        public EnemyFlightSettings Clone()
        {
            return new EnemyFlightSettings
            {
                AssistedControls = AssistedControls,
                AssistedMaxBankDegrees = AssistedMaxBankDegrees,
                AssistedRollRate = AssistedRollRate,
                AssistedPitchRate = AssistedPitchRate,
                AssistedTurnRate = AssistedTurnRate,
                TurnLiftAssistance = TurnLiftAssistance,
                CoordinatedTurnAssistance = CoordinatedTurnAssistance,
                CameraDistance = CameraDistance,
                CameraFollowSpeed = CameraFollowSpeed,
                StallShakeAmount = StallShakeAmount,
                Mass = Mass,
                WingArea = WingArea,
                AirDensity = AirDensity,
                MaxThrust = 18000f,
                EngineResponseRate = EngineResponseRate,
                IdlePropellerDrag = IdlePropellerDrag,
                PropellerIdleFraction = PropellerIdleFraction,
                LiftSlope = LiftSlope,
                ZeroAngleLift = ZeroAngleLift,
                StallAngleDegrees = StallAngleDegrees,
                ParasiticDrag = ParasiticDrag,
                InducedDrag = InducedDrag,
                PitchAuthority = PitchAuthority,
                RollAuthority = RollAuthority,
                MaxBankAngleDegrees = MaxBankAngleDegrees,
                YawAuthority = YawAuthority,
                ThrottleWheelStep = ThrottleWheelStep,
                StartSpeed = StartSpeed,
                MinTerrainClearance = MinTerrainClearance,
                BomberTerrainClearance = BomberTerrainClearance,
                MaxAltitude = MaxAltitude,
                StallRecoveryAirspeed = StallRecoveryAirspeed,
                MaxPitchUpRadians = MaxPitchUpRadians,
                MaxPitchDownRadians = MaxPitchDownRadians,
                MinThrottle = MinThrottle,
                MaxThrottle = MaxThrottle
            };
        }
    }
}

