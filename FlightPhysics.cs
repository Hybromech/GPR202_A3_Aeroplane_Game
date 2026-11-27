using System;
using OpenTK.Mathematics;
namespace GPR202_Assessment3
{
    public sealed class FlightPhysics
    {
        private readonly FlightSettings settings;
        public Vector3 Position { get; private set; }
        public Vector3 Velocity { get; private set; }
        public Matrix4 Orientation { get; private set; } = Matrix4.CreateRotationY(MathF.PI);
        // Body rates: positive pitch = nose up, positive roll = right bank.
        private Vector3 rates;
        public float Throttle { get; set; } = 0.65f;
        public float EnginePower { get; private set; } = 0.65f;
        public float PropellerSpeedFraction => Math.Clamp(settings.PropellerIdleFraction, 0f, 1f)
            + (1f-Math.Clamp(settings.PropellerIdleFraction, 0f, 1f))*EnginePower;
        public float Airspeed => Velocity.Length;
        public float AngleOfAttack { get; private set; }
        public float BankAngle => MathF.Atan2(Right.Y, Up.Y);
        public float StallWarning => Math.Clamp((MathF.Abs(AngleOfAttack) /
            MathHelper.DegreesToRadians(settings.StallAngleDegrees) - 0.75f) / 0.25f, 0f, 1f);
        public bool IsStalled => MathF.Abs(AngleOfAttack) > MathHelper.DegreesToRadians(settings.StallAngleDegrees);
        public bool IsApproximatelyLevel => MathF.Abs(BankAngle) < 0.035f && MathF.Abs(Forward.Y) < 0.035f && rates.LengthSquared < 0.02f;
        public Vector3 Forward => Vector3.TransformVector(Vector3.UnitZ, Orientation);
        public Vector3 Up => Vector3.TransformVector(Vector3.UnitY, Orientation);
        public Vector3 Right => Vector3.TransformVector(Vector3.UnitX, Orientation);
        public FlightPhysics(FlightSettings settings) => this.settings = settings;
        public void Reset(Vector3 position) => Reset(position, Matrix4.CreateRotationY(MathF.PI));
        public void Reset(Vector3 position, Matrix4 orientation)
        {
            Position = position;
            Orientation = orientation;
            Velocity = Forward * settings.StartSpeed;
            rates = Vector3.Zero;
            AngleOfAttack = 0f;
            Throttle = 0.65f;
            EnginePower = Throttle;
        }
        public float LiftCoefficient(float alpha)
        {
            float stall = MathHelper.DegreesToRadians(settings.StallAngleDegrees);
            float limited = Math.Clamp(alpha, -stall, stall);
            float attached = settings.ZeroAngleLift + settings.LiftSlope * limited;
            float separation = Math.Max(0f, MathF.Abs(alpha) - stall);
            // Continuous loss of attached lift after critical angle of attack.
            float blend = 1f - MathF.Exp(-separation * 8f);
            return attached * (1f-blend) + 0.7f * MathF.Sin(2f*alpha) * blend;
        }
        public void Step(float dt, float pitch, float roll, float yaw)
        {
            if (!(dt > 0f) || dt > 0.02f) throw new ArgumentOutOfRangeException(nameof(dt));
            pitch = Math.Clamp(pitch, -1f, 1f);
            roll = Math.Clamp(roll, -1f, 1f);
            yaw = Math.Clamp(yaw, -1f, 1f);
            Throttle = Math.Clamp(Throttle, 0f, 1f);
            EnginePower += (Throttle-EnginePower) * (1f-MathF.Exp(-Math.Max(0f,settings.EngineResponseRate)*dt));
            float speed = Airspeed;
            Vector3 forward = Forward, up = Up, right = Right;
            float longitudinal = Vector3.Dot(Velocity, forward);
            AngleOfAttack = speed > 0.1f ? MathF.Atan2(-Vector3.Dot(Velocity, up), longitudinal) : 0f;
            float beta = speed > 0.1f ? MathF.Asin(Math.Clamp(Vector3.Dot(Velocity, right)/speed, -1f, 1f)) : 0f;
            float pressure = 0.5f * settings.AirDensity * speed * speed;
            float cl = LiftCoefficient(AngleOfAttack);
            float separated = Math.Max(0f, MathF.Abs(AngleOfAttack) - MathHelper.DegreesToRadians(settings.StallAngleDegrees));
            float cd = settings.ParasiticDrag + settings.IdlePropellerDrag*(1f-EnginePower) + settings.InducedDrag * cl * cl
                + 1.2f * MathF.Sin(AngleOfAttack) * MathF.Sin(AngleOfAttack) + separated * 0.8f;
            Vector3 direction = speed > 0.1f ? Velocity/speed : forward;
            Vector3 liftDirection = up - direction * Vector3.Dot(up, direction);
            if (liftDirection.LengthSquared > 0.0001f) liftDirection.Normalize();
            else liftDirection = Vector3.Zero;

            // High-G combat-flap lift bite when pulling hard at the extreme screen edge in Assisted mode,
            // allowing tight, sharp banked turns without sliding sideways or stalling out.
            float edgeLiftBoost = 1f;
            if (settings.AssistedControls)
            {
                float edgeFactor = Math.Clamp((MathF.Abs(roll) - 0.55f) / 0.40f, 0f, 1f);
                float bankSine = MathF.Abs(MathF.Sin(BankAngle));
                float aoaAlign = Math.Clamp(AngleOfAttack / MathHelper.DegreesToRadians(Math.Max(6f, settings.StallAngleDegrees * 0.8f)), 0f, 1f);
                edgeLiftBoost = 1f + edgeFactor * edgeFactor * bankSine * aoaAlign * 0.85f * (1f - 0.5f * StallWarning);
            }

            Vector3 force = forward * (settings.MaxThrust * EnginePower)
                + pressure * settings.WingArea * (cl * edgeLiftBoost * liftDirection - cd * direction - 0.5f * beta * right)
                + new Vector3(0f, -9.81f * settings.Mass, 0f);
            Velocity += force / Math.Max(1f, settings.Mass) * dt;
            Position += Velocity * dt;

            // Aerodynamic control torque scales with dynamic pressure. Damping and
            // restoring moments allow banking, pitch inertia, sideslip and stall recovery.
            float authority = Math.Clamp(speed*speed / (70f*70f), 0f, 2f);
            float trimAlpha = (settings.Mass*9.81f / Math.Max(1f, pressure*settings.WingArea)
                - settings.ZeroAngleLift) / settings.LiftSlope;
            trimAlpha = Math.Clamp(trimAlpha, 0f, 0.12f);
            Vector3 acceleration = new Vector3(
                settings.PitchAuthority*pitch - 1.8f*(AngleOfAttack-trimAlpha) - 2.5f*rates.X,
                settings.YawAuthority*yaw + 1.2f*beta - 2.0f*rates.Y,
                settings.RollAuthority*roll - 2.0f*rates.Z) * authority;
            if (!settings.AssistedControls)
            {
                // Natural bank-induced coordinated turn: when banked, tilted wing lift curves the flight path
                // horizontally into the turn and naturally guides nose pitch/yaw around the turn.
                float bank = BankAngle;
                float sinBank = MathF.Sin(bank);
                if (MathF.Abs(sinBank) > 0.02f && Right.Y * Right.Y + Up.Y * Up.Y > 0.01f)
                {
                    float cosBank = Math.Max(0.45f, MathF.Cos(bank));
                    float tanBank = Math.Clamp(sinBank / cosBank, -1.5f, 1.5f);
                    float safety = 1f - StallWarning;
                    float naturalRightTurnRate = tanBank * Math.Max(0.25f, settings.AssistedTurnRate * 0.45f)
                        * settings.CoordinatedTurnAssistance * (0.4f + 0.6f * safety);

                    Vector3 horizonLeft = Vector3.Cross(Vector3.UnitY, forward);
                    if (horizonLeft.LengthSquared > 1e-4f) horizonLeft.Normalize();
                    else horizonLeft = right;

                    Vector3 bankTurnDFwd = horizonLeft * (-naturalRightTurnRate);
                    float bankPitchRate = Vector3.Dot(bankTurnDFwd, up);
                    float bankYawRate = Vector3.Dot(bankTurnDFwd, right);

                    acceleration.X += bankPitchRate * 3.2f * authority;
                    acceleration.Y += bankYawRate * 3.2f * authority;
                }
            }
            if (settings.AssistedControls)
            {
                float bank = BankAngle;
                float safety = 1f - StallWarning;
                float absRoll = MathF.Abs(roll);
                float absPitch = MathF.Abs(pitch);

                // Extreme-edge boost: when the cursor is pushed near the outer screen edge (> 55%),
                // bank steeply (~78 deg) and pull heavy high-G elevator right up to peak wing lift
                // (~86% of stall AoA) to carve maximum-G sharp turns without snapping into a stall.
                float turnEdge = Math.Clamp((absRoll - 0.55f) / 0.40f, 0f, 1f);
                float pitchEdge = Math.Clamp((absPitch - 0.55f) / 0.40f, 0f, 1f);

                float maxAllowedBankDeg = settings.EnableMaxBankAngle ? settings.MaxBankAngleDegrees : 180f;
                float baseMaxBank = Math.Clamp(settings.AssistedMaxBankDegrees, 0f, maxAllowedBankDeg);
                // Keep a small vertical lift margin (~78 deg max) during level hard turns so heavy elevator pull
                // sustains altitude while curving sharply; ease bank further when actively climbing so wing lift climbs.
                float climbBankEase = Math.Clamp(pitch, 0f, 1f) * 34f;
                float edgeBankCapDeg = Math.Min(maxAllowedBankDeg, Math.Min(78f, baseMaxBank + turnEdge * 10f) - climbBankEase);
                edgeBankCapDeg = Math.Max(35f, edgeBankCapDeg);
                float baseMaxBankRad = MathHelper.DegreesToRadians(baseMaxBank);
                float edgeBankCapRad = MathHelper.DegreesToRadians(edgeBankCapDeg);

                // 1. Bank smoothly into the requested left/right turn, leveling wings when centered.
                float targetBank = Math.Clamp(roll * baseMaxBankRad, -edgeBankCapRad, edgeBankCapRad);
                float bankError = MathF.Atan2(MathF.Sin(targetBank - bank), MathF.Cos(targetBank - bank));
                float effectiveRollRate = settings.AssistedRollRate * (1f + turnEdge * 0.75f);
                float rollRate = Math.Clamp(bankError * 4.5f, -effectiveRollRate, effectiveRollRate);
                // Near vertical, horizon bank is undefined: damp roll rather than chase it.
                if (Right.Y * Right.Y + Up.Y * Up.Y < 0.01f) rollRate = 0f;
                float rollTorqueLimit = Math.Max(settings.RollAuthority, 6.0f);
                acceleration.Z = Math.Clamp((rollRate - rates.Z) * 7.0f, -rollTorqueLimit, rollTorqueLimit) * authority;

                // 2. Build horizon-aligned basis so cursor up/down climbs/dives relative to the ground
                //    and cursor left/right carves a coordinated horizontal turn using both pitch and yaw.
                Vector3 horizonLeft = Vector3.Cross(Vector3.UnitY, forward);
                if (horizonLeft.LengthSquared > 1e-4f)
                {
                    horizonLeft.Normalize();
                }
                else
                {
                    horizonLeft = right;
                }
                Vector3 worldPitchUp = Vector3.Cross(forward, horizonLeft);
                if (worldPitchUp.LengthSquared > 1e-4f) worldPitchUp.Normalize();
                else worldPitchUp = up;

                // World-horizontal turn rate ramps aggressively near the extreme left/right screen edges.
                float turnScale = Math.Max(0f, settings.AssistedTurnRate) * (1f + turnEdge * 1.15f) * (0.35f + 0.65f * safety);
                float desiredRightTurnRate = roll * turnScale * settings.CoordinatedTurnAssistance
                    - yaw * settings.YawAuthority * 0.65f;

                // World-vertical climb/dive rate with extra pull near top/bottom edges.
                float turnLoad = 1f / Math.Max(0.35f, MathF.Cos(bank)) - 1f;
                float levelHold = Math.Clamp(turnLoad * 0.25f, 0f, 0.45f)
                    * settings.TurnLiftAssistance * safety * (1f - absPitch);
                float climbLimit = forward.Y > 0.75f && absRoll > 0.2f ? Math.Clamp((0.92f - forward.Y) / 0.17f, 0f, 1f) : 1f;
                float desiredClimbRate = pitch * settings.AssistedPitchRate * (1f + pitchEdge * 0.35f) * (pitch > 0f ? climbLimit : 1f) + levelHold;

                // Project desired world nose rotation onto the aircraft's local Up (elevator) and Right (rudder) axes:
                // d(Forward)/dt = rates.X * Up + rates.Y * Right.
                Vector3 desiredDFwd = worldPitchUp * desiredClimbRate + horizonLeft * (-desiredRightTurnRate);
                float targetPitchRate = Vector3.Dot(desiredDFwd, up);
                float targetYawRate = Vector3.Dot(desiredDFwd, right);

                // Heavy high-G elevator pull-up when banked at the extreme screen edges:
                // drives AoA right up to ~90% of stall angle so wings generate maximum turning lift without stalling.
                float stallRad = MathHelper.DegreesToRadians(settings.StallAngleDegrees);
                float bankAlign = Math.Clamp(MathF.Abs(MathF.Sin(bank)), 0f, 1f);
                float diveReduce = 1f - Math.Clamp(-pitch, 0f, 1f) * 0.6f;
                float desiredTurnAoA = stallRad * (0.90f * turnEdge * turnEdge * bankAlign * diveReduce);
                float aoaPullBoost = Math.Max(0f, desiredTurnAoA - AngleOfAttack) * 6.0f;
                targetPitchRate += aoaPullBoost;

                // During banked turns, prevent excessive pitch-up from snapping AoA past stall into a tail-slide.
                if (absRoll > 0.2f && AngleOfAttack > stallRad * 0.84f)
                {
                    float aoaExcess = AngleOfAttack - stallRad * 0.84f;
                    float maxSafePitch = Math.Max(-0.3f, 0.95f - aoaExcess * 12f);
                    targetPitchRate = Math.Min(targetPitchRate, maxSafePitch);
                }

                float pitchTorqueLimit = Math.Max(settings.PitchAuthority * 3.0f, 4.5f);
                float yawTorqueLimit = Math.Max(settings.YawAuthority * 3.5f, 2.6f);
                acceleration.X = (Math.Clamp((targetPitchRate - rates.X) * 5.8f, -pitchTorqueLimit, pitchTorqueLimit)
                    - 1.0f * (AngleOfAttack - trimAlpha)) * authority;
                acceleration.Y = (Math.Clamp((targetYawRate - rates.Y) * 5.5f, -yawTorqueLimit, yawTorqueLimit)
                    + 1.5f * beta) * authority;
            }
            rates += acceleration * dt;
            rates = Vector3.Clamp(rates, new Vector3(-2.5f), new Vector3(2.5f));
            Orientation = Matrix4.CreateRotationX(-rates.X*dt)
                * Matrix4.CreateRotationY(rates.Y*dt)
                * Matrix4.CreateRotationZ(rates.Z*dt) * Orientation;
            // Clamp horizon-relative bank while preserving acceleration inside the limit.
            // Bank is undefined at vertical pitch, so skip correction there.
            float horizonRight = Right.Y, horizonUp = Up.Y;
            if (settings.EnableMaxBankAngle && horizonRight * horizonRight + horizonUp * horizonUp > 0.0001f)
            {
                float bank = MathF.Atan2(horizonRight, horizonUp);
                float assistedLimitDeg = Math.Min(settings.MaxBankAngleDegrees, Math.Max(settings.AssistedMaxBankDegrees, 84f));
                float limit = MathHelper.DegreesToRadians(Math.Clamp(settings.AssistedControls ? assistedLimitDeg : settings.MaxBankAngleDegrees, 0f, 90f));
                float boundedBank = Math.Clamp(bank, -limit, limit);
                if (bank != boundedBank)
                {
                    Orientation = Matrix4.CreateRotationZ(boundedBank - bank) * Orientation;
                    if (rates.Z * bank > 0f) rates.Z = 0f;
                }
            }
            // Remove accumulated numerical drift.
            forward = Vector3.Normalize(Forward);
            right = Vector3.Normalize(Vector3.Cross(Up, forward));
            up = Vector3.Normalize(Vector3.Cross(forward, right));
            Orientation = new Matrix4(new Vector4(right,0), new Vector4(up,0),
                new Vector4(forward,0), new Vector4(0,0,0,1));
        }

        /// <summary>
        /// Separate direct gamepad flight physics step:
        /// - <paramref name="roll"/> (Left Stick X): direct roll control (holds bank when released)
        /// - <paramref name="pitch"/> (Left Stick Y + Right Stick Y): direct pitch control
        /// - <paramref name="strafe"/> (Right Stick X): lateral starboard/port strafe thrust
        /// - <paramref name="levelPlane"/> (Right Stick Click): smoothly levels bank and pitch to the horizon
        /// </summary>
        public void StepGamepad(float dt, float pitch, float roll, float strafe, bool levelPlane, float strafeAcceleration = 42f, float rollSensitivity = 2.6f)
        {
            if (!(dt > 0f) || dt > 0.02f) throw new ArgumentOutOfRangeException(nameof(dt));
            // Single-stick pitch is in [-1, 1]; combined dual-stick pitch can exceed 1.0 (up to +/-3.0)
            // for aggressive pitch maneuvers when both sticks pull/push together.
            pitch = Math.Clamp(pitch, -3f, 3f);
            roll = Math.Clamp(roll, -1f, 1f);
            strafe = Math.Clamp(strafe, -1f, 1f);
            float dualPitchBoost = Math.Clamp(MathF.Abs(pitch) - 1f, 0f, 2f);
            Throttle = Math.Clamp(Throttle, 0f, 1f);
            EnginePower += (Throttle - EnginePower) * (1f - MathF.Exp(-Math.Max(0f, settings.EngineResponseRate) * dt));

            float speed = Airspeed;
            Vector3 forward = Forward, up = Up, right = Right;
            float longitudinal = Vector3.Dot(Velocity, forward);
            AngleOfAttack = speed > 0.1f ? MathF.Atan2(-Vector3.Dot(Velocity, up), longitudinal) : 0f;
            float beta = speed > 0.1f ? MathF.Asin(Math.Clamp(Vector3.Dot(Velocity, right) / speed, -1f, 1f)) : 0f;
            float pressure = 0.5f * settings.AirDensity * speed * speed;
            float cl = LiftCoefficient(AngleOfAttack);
            float separated = Math.Max(0f, MathF.Abs(AngleOfAttack) - MathHelper.DegreesToRadians(settings.StallAngleDegrees));
            float cd = settings.ParasiticDrag + settings.IdlePropellerDrag * (1f - EnginePower) + settings.InducedDrag * cl * cl
                + 1.2f * MathF.Sin(AngleOfAttack) * MathF.Sin(AngleOfAttack) + separated * 0.8f;
            Vector3 direction = speed > 0.1f ? Velocity / speed : forward;
            Vector3 liftDirection = up - direction * Vector3.Dot(up, direction);
            if (liftDirection.LengthSquared > 0.0001f) liftDirection.Normalize();
            else liftDirection = Vector3.Zero;

            // Extra high-G elevator lift bite when both gamepad sticks pull/push together,
            // plus gentle banked-turn lift support so banking naturally curves the flight path.
            float bankNow = BankAngle;
            float absSinBank = MathF.Abs(MathF.Sin(bankNow));
            float bankLiftSupport = 1f + absSinBank * 0.35f * (1f - 0.5f * StallWarning);
            float dualLiftBoost = (1f + dualPitchBoost * 0.55f * (1f - 0.35f * StallWarning)) * bankLiftSupport;

            // Starboard is -right (pilot's right); positive strafe pushes right, negative pushes left.
            float mass = Math.Max(1f, settings.Mass);
            Vector3 starboard = -right;
            float lateralSpeed = Vector3.Dot(Velocity, starboard);
            Vector3 strafeForce = MathF.Abs(strafe) > 0.01f
                ? starboard * (strafe * mass * Math.Max(0f, strafeAcceleration))
                : -starboard * (lateralSpeed * mass * 2.2f);

            Vector3 force = forward * (settings.MaxThrust * EnginePower)
                + pressure * settings.WingArea * (cl * dualLiftBoost * liftDirection - cd * direction - 0.5f * beta * right * (1f - MathF.Abs(strafe)))
                + strafeForce
                + new Vector3(0f, -9.81f * mass, 0f);
            Velocity += force / mass * dt;
            Position += Velocity * dt;

            float authority = Math.Clamp(speed * speed / (70f * 70f), 0.35f, 2f);
            float trimAlpha = (mass * 9.81f / Math.Max(1f, pressure * settings.WingArea)
                - settings.ZeroAngleLift) / settings.LiftSlope;
            trimAlpha = Math.Clamp(trimAlpha, 0f, 0.12f);

            Vector3 acceleration;
            if (levelPlane)
            {
                // Smoothly roll wings level (BankAngle -> 0) and bring nose to horizon (Forward.Y -> 0).
                float bank = BankAngle;
                float targetRollRate = Math.Clamp(-bank * 5.0f, -3.5f, 3.5f);
                if (Right.Y * Right.Y + Up.Y * Up.Y < 0.01f) targetRollRate = 0f;

                Vector3 horizonLeft = Vector3.Cross(Vector3.UnitY, forward);
                if (horizonLeft.LengthSquared > 1e-4f) horizonLeft.Normalize();
                else horizonLeft = right;
                Vector3 worldPitchUp = Vector3.Cross(forward, horizonLeft);
                if (worldPitchUp.LengthSquared > 1e-4f) worldPitchUp.Normalize();
                else worldPitchUp = up;

                float pitchAngle = MathF.Asin(Math.Clamp(forward.Y, -1f, 1f));
                float desiredClimbRate = Math.Clamp(-pitchAngle * 3.5f, -2.0f, 2.0f);
                Vector3 desiredDFwd = worldPitchUp * desiredClimbRate;
                float targetPitchRate = Vector3.Dot(desiredDFwd, up);
                float targetYawRate = Vector3.Dot(desiredDFwd, right);

                float rollLimit = Math.Max(settings.RollAuthority * 2.0f, 6.5f);
                float pitchLimit = Math.Max(settings.PitchAuthority * 2.5f, 4.0f);
                float yawLimit = Math.Max(settings.YawAuthority * 3.0f, 2.5f);

                acceleration = new Vector3(
                    Math.Clamp((targetPitchRate - rates.X) * 6.0f, -pitchLimit, pitchLimit) - 1.2f * (AngleOfAttack - trimAlpha),
                    Math.Clamp((targetYawRate - rates.Y) * 6.0f, -yawLimit, yawLimit) + 1.0f * beta,
                    Math.Clamp((targetRollRate - rates.Z) * 7.5f, -rollLimit, rollLimit)
                ) * authority;
            }
            else
            {
                // Direct gamepad stick response: crisp, fast aileron roll that holds bank when centered;
                // suppresses weathercock yaw while actively strafing so aim stays steady.
                // Combined dual-stick pitch (|pitch| > 1) reduces pitch damping & restoring drag
                // for noticeably stronger, more aggressive pitch maneuvers.
                float weathercock = (1f - MathF.Abs(strafe)) * 1.2f * beta;
                float pitchDamping = 2.6f / (1f + dualPitchBoost * 0.35f);
                float pitchRestore = 1.6f / (1f + dualPitchBoost * 0.45f);
                float maxGamepadRollRate = 4.2f * Math.Clamp(rollSensitivity / 2.6f, 0.4f, 2.5f);
                float targetRollRate = roll * maxGamepadRollRate;
                float rollAccel = Math.Clamp((targetRollRate - rates.Z) * 14.0f, -24f, 24f);
                acceleration = new Vector3(
                    (settings.PitchAuthority * 1.25f * pitch - pitchRestore * (AngleOfAttack - trimAlpha) - pitchDamping * rates.X) * authority,
                    (weathercock - 2.4f * rates.Y) * authority,
                    rollAccel * Math.Clamp(authority, 0.75f, 1.5f)
                );

                // Natural bank-induced coordinated turn: when the wings are banked, tilted wing lift
                // naturally turns the plane horizontally in the direction of the bank.
                float sinBank = MathF.Sin(bankNow);
                if (MathF.Abs(sinBank) > 0.02f && Right.Y * Right.Y + Up.Y * Up.Y > 0.01f)
                {
                    float cosBank = Math.Max(0.45f, MathF.Cos(bankNow));
                    float tanBank = Math.Clamp(sinBank / cosBank, -1.6f, 1.6f);
                    float safety = 1f - StallWarning;
                    float naturalRightTurnRate = tanBank * Math.Max(0.30f, settings.AssistedTurnRate * 0.50f)
                        * settings.CoordinatedTurnAssistance * (0.4f + 0.6f * safety);

                    Vector3 horizonLeft = Vector3.Cross(Vector3.UnitY, forward);
                    if (horizonLeft.LengthSquared > 1e-4f) horizonLeft.Normalize();
                    else horizonLeft = right;

                    Vector3 bankTurnDFwd = horizonLeft * (-naturalRightTurnRate);
                    float bankPitchRate = Vector3.Dot(bankTurnDFwd, up);
                    float bankYawRate = Vector3.Dot(bankTurnDFwd, right);

                    acceleration.X += bankPitchRate * 3.5f * authority;
                    acceleration.Y += bankYawRate * 3.5f * authority;
                }
            }

            rates += acceleration * dt;
            float maxPitchRate = 2.5f + dualPitchBoost * 0.9f;
            float maxRollRateClamp = Math.Max(4.5f, 4.2f * Math.Clamp(rollSensitivity / 2.6f, 0.4f, 2.5f) + 0.5f);
            rates = Vector3.Clamp(rates, new Vector3(-maxPitchRate, -2.5f, -maxRollRateClamp), new Vector3(maxPitchRate, 2.5f, maxRollRateClamp));
            Orientation = Matrix4.CreateRotationX(-rates.X * dt)
                * Matrix4.CreateRotationY(rates.Y * dt)
                * Matrix4.CreateRotationZ(rates.Z * dt) * Orientation;

            float horizonRight = Right.Y, horizonUp = Up.Y;
            if (settings.EnableMaxBankAngle && horizonRight * horizonRight + horizonUp * horizonUp > 0.0001f)
            {
                float bank = MathF.Atan2(horizonRight, horizonUp);
                float limit = MathHelper.DegreesToRadians(Math.Clamp(settings.MaxBankAngleDegrees, 0f, 90f));
                float boundedBank = Math.Clamp(bank, -limit, limit);
                if (bank != boundedBank)
                {
                    Orientation = Matrix4.CreateRotationZ(boundedBank - bank) * Orientation;
                    if (rates.Z * bank > 0f) rates.Z = 0f;
                }
            }

            forward = Vector3.Normalize(Forward);
            right = Vector3.Normalize(Vector3.Cross(Up, forward));
            up = Vector3.Normalize(Vector3.Cross(forward, right));
            Orientation = new Matrix4(new Vector4(right, 0), new Vector4(up, 0),
                new Vector4(forward, 0), new Vector4(0, 0, 0, 1));
        }
    }
}
