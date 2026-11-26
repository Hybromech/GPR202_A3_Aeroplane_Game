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
        public float Airspeed => Velocity.Length;
        public float AngleOfAttack { get; private set; }
        public bool IsStalled => MathF.Abs(AngleOfAttack) > MathHelper.DegreesToRadians(settings.StallAngleDegrees);
        public Vector3 Forward => Vector3.TransformVector(Vector3.UnitZ, Orientation);
        public Vector3 Up => Vector3.TransformVector(Vector3.UnitY, Orientation);
        public Vector3 Right => Vector3.TransformVector(Vector3.UnitX, Orientation);
        public FlightPhysics(FlightSettings settings) => this.settings = settings;
        public void Reset(Vector3 position)
        {
            Position = position;
            Orientation = Matrix4.CreateRotationY(MathF.PI);
            Velocity = Forward * settings.StartSpeed;
            rates = Vector3.Zero;
            AngleOfAttack = 0f;
            Throttle = 0.65f;
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
            float speed = Airspeed;
            Vector3 forward = Forward, up = Up, right = Right;
            float longitudinal = Vector3.Dot(Velocity, forward);
            AngleOfAttack = speed > 0.1f ? MathF.Atan2(-Vector3.Dot(Velocity, up), longitudinal) : 0f;
            float beta = speed > 0.1f ? MathF.Asin(Math.Clamp(Vector3.Dot(Velocity, right)/speed, -1f, 1f)) : 0f;
            float pressure = 0.5f * settings.AirDensity * speed * speed;
            float cl = LiftCoefficient(AngleOfAttack);
            float separated = Math.Max(0f, MathF.Abs(AngleOfAttack) - MathHelper.DegreesToRadians(settings.StallAngleDegrees));
            float cd = settings.ParasiticDrag + settings.InducedDrag * cl * cl
                + 1.2f * MathF.Sin(AngleOfAttack) * MathF.Sin(AngleOfAttack) + separated * 0.8f;
            Vector3 direction = speed > 0.1f ? Velocity/speed : forward;
            Vector3 liftDirection = up - direction * Vector3.Dot(up, direction);
            if (liftDirection.LengthSquared > 0.0001f) liftDirection.Normalize();
            else liftDirection = Vector3.Zero;
            Vector3 force = forward * (settings.MaxThrust * Throttle)
                + pressure * settings.WingArea * (cl * liftDirection - cd * direction - 0.5f * beta * right)
                + new Vector3(0f, -9.81f * settings.Mass, 0f);
            Velocity += force / Math.Max(1f, settings.Mass) * dt;
            Position += Velocity * dt;

            // Pitch and yaw retain aerodynamic torque, damping and restoring moments.
            // Roll input selects a bank angle, approached at a bounded constant rate.
            float authority = Math.Clamp(speed*speed / (70f*70f), 0f, 2f);
            float trimAlpha = (settings.Mass*9.81f / Math.Max(1f, pressure*settings.WingArea)
                - settings.ZeroAngleLift) / settings.LiftSlope;
            trimAlpha = Math.Clamp(trimAlpha, 0f, 0.12f);
            Vector3 acceleration = new Vector3(
                settings.PitchAuthority*pitch - 1.8f*(AngleOfAttack-trimAlpha) - 2.5f*rates.X,
                settings.YawAuthority*yaw + 1.2f*beta - 2.0f*rates.Y,
                0f) * authority;
            rates += acceleration * dt;
            rates = Vector3.Clamp(rates, new Vector3(-2.5f), new Vector3(2.5f));
            Orientation = Matrix4.CreateRotationX(-rates.X*dt)
                * Matrix4.CreateRotationY(rates.Y*dt) * Orientation;
            // Measure bank about the current forward axis relative to the horizon.
            // At vertical flight the horizon bank is undefined; retain the current roll.
            float horizonRight = Right.Y, horizonUp = Up.Y;
            rates.Z = 0f;
            if (horizonRight*horizonRight + horizonUp*horizonUp > 0.0001f)
            {
                float targetBank = roll * MathHelper.DegreesToRadians(
                    Math.Clamp(settings.MaxBankAngleDegrees, 0f, 89f));
                float bank = MathF.Atan2(horizonRight, horizonUp);
                float error = MathF.Atan2(MathF.Sin(targetBank-bank), MathF.Cos(targetBank-bank));
                float maxStep = Math.Max(0f, settings.RollAuthority) * dt;
                float bankStep = Math.Clamp(error, -maxStep, maxStep);
                rates.Z = bankStep / dt;
                Orientation = Matrix4.CreateRotationZ(bankStep) * Orientation;
            }
            // Remove accumulated numerical drift without constraining pitch or bank.
            forward = Vector3.Normalize(Forward);
            right = Vector3.Normalize(Vector3.Cross(Up, forward));
            up = Vector3.Normalize(Vector3.Cross(forward, right));
            Orientation = new Matrix4(new Vector4(right,0), new Vector4(up,0),
                new Vector4(forward,0), new Vector4(0,0,0,1));
        }
    }
}
