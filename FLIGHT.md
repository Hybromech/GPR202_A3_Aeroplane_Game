# Flight controls and model

Mouse position is a virtual stick: above centre pushes down, below pulls up, left/right banks. Arrow keys override the corresponding axis. Centre the cursor to release roll input; damping slows the roll without automatically levelling the wings. Up arrow pushes down; Down arrow pulls up. A/D operate inverted rudder (A right, D left); W/S increase/decrease throttle. Escape exits. The title shows airspeed (m/s), angle of attack, stall state and throttle.

The simulation runs at 120 Hz with bounded catch-up after stalls in frame delivery. FlightSettings.cs contains mass, wing area, thrust, lift/drag coefficients, stall angle and angular control tuning. Orientation drives both aircraft rendering passes. The chase camera follows heading and pitch with a world-up offset and level horizon, independently of aircraft bank. Propeller animation remains independent.

Lift depends on dynamic pressure and angle of attack. Beyond the critical angle, lift falls and drag increases. Hard pull-ups can stall even above low-speed stall speed. Release or push forward to reduce angle of attack and rebuild airspeed; use throttle as needed. Banking tilts the lift vector and changes the flight path.

This is a simplified aerodynamic game model with empirical angular response and automatic pitch trim, not validated P-51 performance. It omits wind, compressibility, detailed inertia, propwash and landing-gear dynamics. Terrain contact respawns the plane 250 units above the contact point. Camera tracking and mouse sensitivity need playtesting.

Run deterministic physics checks with:

    dotnet run --project tests/FlightChecks/FlightChecks.csproj -c Release

Mouse horizontal position controls roll acceleration. Holding left/right continues rolling, with airspeed-dependent authority and damping. RollAuthority (default 3) tunes acceleration rather than a target angle or fixed roll speed.
