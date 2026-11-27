using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using OpenTK.Windowing.Common.Input;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace GPR202_Assessment3
{
    public struct GamepadInputState
    {
        public bool Connected;
        public bool HasAnyInput;
        public float Roll;
        public float Pitch;
        public float Strafe;
        public bool LevelButtonDown;
        public bool LevelButtonPressed;
        public float ThrottleDelta;
        public float LookShoulder; // -1 = D-Pad Left (look over left shoulder), +1 = D-Pad Right (look over right shoulder)
        public bool FireGuns;
        public bool FireMissilePressed;
        public bool DropBombPressed;
        public bool RestockAmmoPressed;
    }

    /// <summary>
    /// Separate gamepad flight controls:
    /// - Left Stick X: Roll
    /// - Left Stick Y: Pitch
    /// - Right Stick X: Strafe (lateral left/right)
    /// - Right Stick Y: Pitch
    /// - Right Stick Click (R3): Level the plane
    /// </summary>
    public class GamepadControls
    {
        // Standard GLFW GamepadState axis indices
        private const int AxisLeftX = 0;
        private const int AxisLeftY = 1;
        private const int AxisRightX = 2;
        private const int AxisRightY = 3;
        private const int AxisLeftTrigger = 4;
        private const int AxisRightTrigger = 5;

        // Standard GLFW GamepadState button indices
        private const int ButtonA = 0;
        private const int ButtonB = 1;
        private const int ButtonX = 2;
        private const int ButtonLeftBumper = 4;
        private const int ButtonRightBumper = 5;
        private const int ButtonBack = 6;        // Back / View / Select
        private const int ButtonRightThumb = 10; // R3 (Right stick click)
        private const int ButtonDpadUp = 11;
        private const int ButtonDpadRight = 12;
        private const int ButtonDpadDown = 13;
        private const int ButtonDpadLeft = 14;

        private static readonly int ButtonsOffset = (int)Marshal.OffsetOf<GamepadState>(nameof(GamepadState.Buttons));
        private static readonly int AxesOffset = (int)Marshal.OffsetOf<GamepadState>(nameof(GamepadState.Axes));

        public bool Enabled { get; set; } = true;
        public float Deadzone { get; set; } = 0.14f;

        /// <summary>
        /// Roll speed and responsiveness multiplier for Left Stick X banking.
        /// Higher values roll faster and respond more crisply to small stick inputs.
        /// </summary>
        public float RollSensitivity { get; set; } = 0.5f;

        /// <summary>
        /// When true (standard flight stick), pulling the stick back/down pitches the nose UP
        /// and pushing forward/up pitches the nose DOWN. Set false for non-inverted stick pitch.
        /// </summary>
        public bool PullBackToPitchUp { get; set; } = true;

        /// <summary>
        /// Pitch strength multiplier when BOTH Left Stick Y and Right Stick Y are pulled/pushed
        /// in the same direction (single stick max = 1.0; both sticks combined = this value).
        /// </summary>
        public float DualStickPitchMultiplier { get; set; } = 2.6f;

        /// <summary>
        /// Lateral strafe acceleration (m/s^2) controlled by Right Stick X.
        /// </summary>
        public float StrafeAcceleration { get; set; } = 42f;

        /// <summary>
        /// Throttle change rate per second when using D-Pad Up/Down on the gamepad.
        /// </summary>
        public float ThrottleRate { get; set; } = 0.45f;

        /// <summary>
        /// Angle in degrees to swing the camera when looking over the left/right shoulder with D-Pad Left/Right.
        /// </summary>
        public float ShoulderLookAngleDegrees { get; set; } = 125f;

        /// <summary>
        /// Smoothing speed for swinging into and out of the over-the-shoulder camera view.
        /// </summary>
        public float ShoulderLookSpeed { get; set; } = 10f;

        public bool IsLeveling { get; private set; }

        private bool prevRightThumbDown;
        private bool prevMissileDown;
        private bool prevBombDown;
        private bool prevBackDown;

        public void CancelLeveling()
        {
            IsLeveling = false;
        }

        public GamepadInputState Poll(IReadOnlyList<JoystickState?>? joystickStates)
        {
            var result = new GamepadInputState();
            if (!Enabled)
            {
                ResetEdgeStates();
                return result;
            }

            // 1. Prefer GLFW's standard SDL gamepad mapping (consistent across Xbox, DualSense, Switch Pro, etc.).
            for (int jid = 0; jid < 16; jid++)
            {
                if (!GLFW.JoystickIsGamepad(jid)) continue;
                if (!GLFW.GetGamepadState(jid, out GamepadState state)) continue;

                ReadOnlySpan<byte> raw = MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref state, 1));
                ReadOnlySpan<byte> buttons = raw.Slice(ButtonsOffset, 15);
                ReadOnlySpan<float> axes = MemoryMarshal.Cast<byte, float>(raw.Slice(AxesOffset, 6 * sizeof(float)));

                float lx = ApplyDeadzone(axes[AxisLeftX]);
                float ly = ApplyDeadzone(axes[AxisLeftY]);
                float rx = ApplyDeadzone(axes[AxisRightX]);
                float ry = ApplyDeadzone(axes[AxisRightY]);
                float rt = axes[AxisRightTrigger];

                bool rightThumb = buttons[ButtonRightThumb] == (byte)InputAction.Press;
                bool backDown = buttons[ButtonBack] == (byte)InputAction.Press;
                bool fireGuns = rt > -0.2f || buttons[ButtonA] == (byte)InputAction.Press;
                bool missileDown = buttons[ButtonRightBumper] == (byte)InputAction.Press || buttons[ButtonX] == (byte)InputAction.Press;
                bool bombDown = buttons[ButtonLeftBumper] == (byte)InputAction.Press || buttons[ButtonB] == (byte)InputAction.Press;
                float throttleDelta = (buttons[ButtonDpadUp] == (byte)InputAction.Press ? 1f : 0f)
                    - (buttons[ButtonDpadDown] == (byte)InputAction.Press ? 1f : 0f);
                float lookShoulder = (buttons[ButtonDpadRight] == (byte)InputAction.Press ? 1f : 0f)
                    - (buttons[ButtonDpadLeft] == (byte)InputAction.Press ? 1f : 0f);

                return BuildState(lx, ly, rx, ry, rightThumb, backDown, throttleDelta, lookShoulder, fireGuns, missileDown, bombDown);
            }

            // 2. Fallback for generic unmapped joysticks via OpenTK JoystickStates.
            if (joystickStates != null)
            {
                for (int i = 0; i < joystickStates.Count; i++)
                {
                    var js = joystickStates[i];
                    if (js == null) continue;

                    float lx = js.AxisCount > 0 ? ApplyDeadzone(js.GetAxis(0)) : 0f;
                    float ly = js.AxisCount > 1 ? ApplyDeadzone(js.GetAxis(1)) : 0f;
                    float rx = js.AxisCount > 2 ? ApplyDeadzone(js.GetAxis(2)) : 0f;
                    float ry = js.AxisCount > 3 ? ApplyDeadzone(js.GetAxis(3)) : 0f;

                    bool rightThumb = (js.ButtonCount > 9 && js.IsButtonDown(9))
                        || (js.ButtonCount > 10 && js.IsButtonDown(10));
                    bool backDown = js.ButtonCount > 6 && js.IsButtonDown(6);
                    bool fireGuns = (js.ButtonCount > 0 && js.IsButtonDown(0))
                        || (js.AxisCount > 5 && js.GetAxis(5) > -0.2f);
                    bool missileDown = (js.ButtonCount > 5 && js.IsButtonDown(5))
                        || (js.ButtonCount > 2 && js.IsButtonDown(2));
                    bool bombDown = (js.ButtonCount > 4 && js.IsButtonDown(4))
                        || (js.ButtonCount > 1 && js.IsButtonDown(1));

                    float throttleDelta = 0f;
                    float lookShoulder = 0f;
                    if (js.HatCount > 0)
                    {
                        Hat hat = js.GetHat(0);
                        throttleDelta = (hat.HasFlag(Hat.Up) ? 1f : 0f) - (hat.HasFlag(Hat.Down) ? 1f : 0f);
                        lookShoulder = (hat.HasFlag(Hat.Right) ? 1f : 0f) - (hat.HasFlag(Hat.Left) ? 1f : 0f);
                    }
                    else
                    {
                        throttleDelta = (js.ButtonCount > 11 && js.IsButtonDown(11) ? 1f : 0f)
                            - (js.ButtonCount > 13 && js.IsButtonDown(13) ? 1f : 0f);
                        lookShoulder = (js.ButtonCount > 12 && js.IsButtonDown(12) ? 1f : 0f)
                            - (js.ButtonCount > 14 && js.IsButtonDown(14) ? 1f : 0f);
                    }

                    return BuildState(lx, ly, rx, ry, rightThumb, backDown, throttleDelta, lookShoulder, fireGuns, missileDown, bombDown);
                }
            }

            ResetEdgeStates();
            return result;
        }

        public bool UpdateLeveling(in GamepadInputState input, bool aircraftIsLevel)
        {
            if (input.LevelButtonPressed || input.LevelButtonDown)
            {
                IsLeveling = true;
            }
            else if (MathF.Abs(input.Roll) > 0f || MathF.Abs(input.Pitch) > 0f)
            {
                // Manual stick input overrides auto-leveling immediately.
                IsLeveling = false;
            }
            else if (IsLeveling && aircraftIsLevel)
            {
                IsLeveling = false;
            }
            return IsLeveling;
        }

        private GamepadInputState BuildState(
            float lx, float ly, float rx, float ry,
            bool rightThumbDown, bool backDown, float throttleDelta, float lookShoulder,
            bool fireGuns, bool missileDown, bool bombDown)
        {
            float pitchSign = PullBackToPitchUp ? 1f : -1f;
            float leftPitch = ly * pitchSign;
            float rightPitch = ry * pitchSign;
            float combinedPitch = CombineDualStickPitch(leftPitch, rightPitch);

            bool rightThumbPressed = rightThumbDown && !prevRightThumbDown;
            bool missilePressed = missileDown && !prevMissileDown;
            bool bombPressed = bombDown && !prevBombDown;
            bool backPressed = backDown && !prevBackDown;

            prevRightThumbDown = rightThumbDown;
            prevMissileDown = missileDown;
            prevBombDown = bombDown;
            prevBackDown = backDown;

            bool hasInput = lx != 0f || ly != 0f || rx != 0f || ry != 0f
                || rightThumbDown || backDown || throttleDelta != 0f
                || fireGuns || missileDown || bombDown;

            return new GamepadInputState
            {
                Connected = true,
                HasAnyInput = hasInput,
                Roll = lx,
                Pitch = combinedPitch,
                Strafe = rx,
                LevelButtonDown = rightThumbDown,
                LevelButtonPressed = rightThumbPressed,
                ThrottleDelta = throttleDelta,
                LookShoulder = lookShoulder,
                FireGuns = fireGuns,
                FireMissilePressed = missilePressed,
                DropBombPressed = bombPressed,
                RestockAmmoPressed = backPressed
            };
        }

        /// <summary>
        /// Combines Left Stick Y and Right Stick Y pitch inputs.
        /// Single-stick input stays in [-1, 1]; pulling/pushing both sticks in the same direction
        /// amplifies pitch smoothly up to +/-DualStickPitchMultiplier for aggressive pitch maneuvers.
        /// </summary>
        public float CombineDualStickPitch(float leftPitch, float rightPitch)
        {
            leftPitch = Math.Clamp(leftPitch, -1f, 1f);
            rightPitch = Math.Clamp(rightPitch, -1f, 1f);
            float sum = leftPitch + rightPitch;
            float dualOverlap = (leftPitch * rightPitch > 0f)
                ? MathF.Min(MathF.Abs(leftPitch), MathF.Abs(rightPitch))
                : 0f;
            float maxDual = Math.Max(1.5f, DualStickPitchMultiplier);
            float extraPerOverlap = Math.Max(0f, maxDual - 2f);
            return Math.Clamp(sum + MathF.Sign(sum) * dualOverlap * extraPerOverlap, -maxDual, maxDual);
        }

        private float ApplyDeadzone(float raw)
        {
            float dz = Math.Clamp(Deadzone, 0f, 0.9f);
            float mag = MathF.Abs(raw);
            if (mag <= dz) return 0f;
            float rescaled = (mag - dz) / (1f - dz);
            return MathF.Sign(raw) * Math.Clamp(rescaled, 0f, 1f);
        }

        private void ResetEdgeStates()
        {
            prevRightThumbDown = false;
            prevMissileDown = false;
            prevBombDown = false;
            prevBackDown = false;
        }
    }
}

