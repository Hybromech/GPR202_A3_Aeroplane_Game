# Flight controls and model

Mouse position is a virtual stick: above centre pushes down, below pulls up, left/right banks. Arrow keys override the corresponding axis. In assisted mode, centre the cursor to level the wings. F2 switches assisted/unassisted controls; the title displays the active mode. Up arrow pushes down; Down arrow pulls up. A/D operate inverted rudder (A right, D left); W/S increase/decrease throttle. Mouse wheel up/down also increases/decreases throttle by 5 percentage points per notch, clamped to 0–100%; tune ThrottleWheelStep in FlightSettings.cs. Escape exits. The title shows airspeed (m/s), angle of attack, stall state and throttle.

The simulation runs at 120 Hz with bounded catch-up after stalls in frame delivery. FlightSettings.cs contains mass, wing area, thrust, lift/drag coefficients, stall angle and angular control tuning. Orientation drives both aircraft rendering passes. The chase camera follows heading and pitch with a world-up offset and level horizon, independently of aircraft bank. Propeller animation remains independent.

Lift depends on dynamic pressure and angle of attack. Beyond the critical angle, lift falls and drag increases. Hard pull-ups can stall even above low-speed stall speed. Release or push forward to reduce angle of attack and rebuild airspeed; use throttle as needed. Banking tilts the lift vector and changes the flight path.

This is a simplified aerodynamic game model with empirical angular response and automatic pitch trim, not validated P-51 performance. It omits wind, compressibility, detailed inertia, propwash and landing-gear dynamics. Terrain contact respawns the plane 250 units above the contact point. Camera tracking and mouse sensitivity need playtesting.

Run deterministic physics checks with:

    dotnet run --project tests/FlightChecks/FlightChecks.csproj -c Release

Unassisted mode: mouse horizontal position controls roll acceleration. Holding left/right rolls up to the bank limit, with airspeed-dependent authority and damping. RollAuthority (default 3) tunes acceleration.

In unassisted mode, MaxBankAngleDegrees limits bank to plus/minus 90 degrees by default. At the limit outward roll stops; opposite input rolls back immediately with acceleration. Centring still damps roll without auto-levelling. Horizon-relative limiting pauses near vertical pitch, where bank is undefined.

## Assisted mode (default)

Horizontal mouse position selects a bank angle up to AssistedMaxBankDegrees (60). A damped controller approaches it with bounded acceleration and AssistedRollRate (1 rad/s) target speed. Centre requests level wings. Pitch input uses a gentle centre response and requests up to AssistedPitchRate (0.65 rad/s); elevator torque remains bounded and airspeed-dependent. Automatic rudder helps coordinate turns, while limited elevator assistance reduces altitude loss. This is not an altitude autopilot: steep turns can still lose altitude and speed. Assistance fades near stall; manual rudder overrides automatic coordination.

FlightSettings.cs controls AssistedControls, AssistedMaxBankDegrees, AssistedRollRate, AssistedPitchRate, TurnLiftAssistance and CoordinatedTurnAssistance. F2 switches modes during flight. MaxBankAngleDegrees remains the overall bank limit.

The chase camera stays level and follows smoothly (CameraFollowSpeed). Approaching stall produces a pulsing warning tone and subtle camera shake; StallShakeAmount=0 disables shaking and AudioSettings.StallWarningVolume=0 silences the warning. Gravity, thrust, drag and stall/lift forces remain simulated. These are game assists, not a validated flight-control system.
