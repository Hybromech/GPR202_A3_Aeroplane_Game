using OpenTK.Windowing.GraphicsLibraryFramework;

namespace GPR202_Assessment3
{
    public class Controls
    {
        /// <summary>
        /// When true, the assisted-flight aim cursor tracks a world-space aim direction
        /// and naturally converges back to the center crosshair as the plane turns toward it.
        /// When false, the cursor stays in screen space wherever the player places it.
        /// </summary>
        public bool CursorConvergesBack { get; set; } = false;

        /// <summary>
        /// Runtime hotkey to toggle <see cref="CursorConvergesBack"/> on or off.
        /// </summary>
        public Keys ToggleCursorConvergenceKey { get; set; } = Keys.F3;

        /// <summary>
        /// Separate gamepad flight controls (Left Stick X=Roll, Left/Right Stick Y=Pitch,
        /// Right Stick X=Strafe, Right Stick Click=Level Plane).
        /// </summary>
        public GamepadControls Gamepad { get; } = new();

        public bool ToggleCursorConvergence()
        {
            CursorConvergesBack = !CursorConvergesBack;
            return CursorConvergesBack;
        }
    }
}

