using System;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;

namespace GPR202_Assessment3
{
    public readonly struct CockpitTelemetry
    {
        public float Airspeed { get; init; }          // m/s
        public float PitchRadians { get; init; }      // + = nose up
        public float RollRadians { get; init; }       // + = right bank (right wing down)
        public float AltitudeMsl { get; init; }       // meters above terrain base
        public float AltitudeAgl { get; init; }       // meters above ground directly beneath plane
        public float VerticalSpeed { get; init; }     // m/s (+ = climbing)
        public float Throttle { get; init; }          // 0..1 commanded
        public float EnginePower { get; init; }       // 0..1 actual engine power
        public float StallWarning { get; init; }      // 0..1
        public bool Crashed { get; init; }
        public float Time { get; init; }
        public int GunAmmo { get; init; }
        public int MissilesRemaining { get; init; }
        public int BombsRemaining { get; init; }
        public float HitMarker { get; init; }         // 0..1 flash intensity
        public Vector2 AimCursorOffsetPx { get; init; } // Pixel offset of the unrestricted aim circle from screen center
        public float PlayerHealth { get; init; }
        public float PlayerMaxHealth { get; init; }
        public int EnemiesRemaining { get; init; }

        public static CockpitTelemetry FromFlight(
            FlightPhysics flight,
            float groundHeight,
            float terrainOffsetY,
            bool crashed,
            float time,
            CombatSimulation? combat = null,
            Vector2 aimCursorOffsetPx = default,
            float playerHealth = Plane.DefaultMaxHealth,
            float playerMaxHealth = Plane.DefaultMaxHealth,
            int enemiesRemaining = 0)
        {
            float pitch = MathF.Asin(Math.Clamp(flight.Forward.Y, -1f, 1f));
            float roll = flight.BankAngle;
            float msl = Math.Max(0f, flight.Position.Y - terrainOffsetY);
            float agl = Math.Max(0f, flight.Position.Y - groundHeight);
            return new CockpitTelemetry
            {
                Airspeed = crashed ? 0f : flight.Airspeed,
                PitchRadians = pitch,
                RollRadians = roll,
                AltitudeMsl = msl,
                AltitudeAgl = agl,
                VerticalSpeed = crashed ? 0f : flight.Velocity.Y,
                Throttle = crashed ? 0f : flight.Throttle,
                EnginePower = crashed ? 0f : flight.EnginePower,
                StallWarning = crashed ? 0f : flight.StallWarning,
                Crashed = crashed,
                Time = time,
                GunAmmo = combat?.GunAmmo ?? 0,
                MissilesRemaining = combat?.MissilesRemaining ?? 0,
                BombsRemaining = combat?.BombsRemaining ?? 0,
                HitMarker = combat != null ? Math.Clamp(combat.HitMarkerTimer / 0.22f, 0f, 1f) : 0f,
                AimCursorOffsetPx = aimCursorOffsetPx,
                PlayerHealth = crashed ? 0f : Math.Max(0f, playerHealth),
                PlayerMaxHealth = Math.Max(1f, playerMaxHealth),
                EnemiesRemaining = Math.Max(0, enemiesRemaining)
            };
        }
    }

    // Renders a resolution-independent SDF analog cockpit instrument cluster at the bottom-center
    // of the screen: Airspeed Indicator, Attitude Indicator (Artificial Horizon), Altimeter,
    // plus compact Throttle and Vertical Speed bars.
    public sealed class CockpitHud : IDisposable
    {
        private int program;
        private int crosshairProgram;
        private int vao;
        private bool disposed;

        private readonly int viewportLoc;
        private readonly int scaleLoc;
        private readonly int airspeedLoc;
        private readonly int pitchLoc;
        private readonly int rollLoc;
        private readonly int altitudeLoc;
        private readonly int aglLoc;
        private readonly int verticalSpeedLoc;
        private readonly int throttleLoc;
        private readonly int enginePowerLoc;
        private readonly int stallWarningLoc;
        private readonly int timeLoc;
        private readonly int gunAmmoLoc;
        private readonly int missilesLoc;
        private readonly int bombsLoc;
        private readonly int playerHealthLoc;
        private readonly int playerHealthFracLoc;
        private readonly int enemiesRemainingLoc;

        private readonly int crossViewportLoc;
        private readonly int crossHitLoc;
        private readonly int crossCursorLoc;

        private const string CrosshairVertexShader = @"#version 330 core
            uniform vec2 uViewport;
            out vec2 vPx;
            void main()
            {
                // Fullscreen quad so the mouse-aim cursor circle and tether line can move anywhere on screen.
                const vec2 corners[6] = vec2[6](
                    vec2(-1.0, -1.0), vec2( 1.0, -1.0), vec2( 1.0,  1.0),
                    vec2(-1.0, -1.0), vec2( 1.0,  1.0), vec2(-1.0,  1.0)
                );
                vec2 c = corners[gl_VertexID];
                vPx = c * (uViewport * 0.5);
                gl_Position = vec4(c, 0.0, 1.0);
            }";

        private const string CrosshairFragmentShader = @"#version 330 core
            in vec2 vPx;
            out vec4 fragColor;
            uniform float uHitMarker;
            uniform vec2 uCursorPx;

            float sdSegment(vec2 p, vec2 a, vec2 b)
            {
                vec2 pa = p - a, ba = b - a;
                float h = clamp(dot(pa, ba) / max(dot(ba, ba), 1e-6), 0.0, 1.0);
                return length(pa - ba * h);
            }

            void main()
            {
                vec2 p = vPx;
                float r = length(p);
                float distToCursor = length(p - uCursorPx);
                float cursorLen = length(uCursorPx);

                // Quick early-out for pixels far from center crosshair, cursor circle, and tether line.
                if (r > 32.0 && distToCursor > 22.0 && cursorLen <= 14.0) discard;

                vec3 col = vec3(0.02, 0.04, 0.03);
                float alpha = 0.0;

                // 1. Center aircraft crosshair reticle
                if (r <= 32.0)
                {
                    float dRing = abs(r - 22.0) - 0.85;
                    float dH = sdSegment(vec2(abs(p.x), p.y), vec2(7.0, 0.0), vec2(17.5, 0.0)) - 1.0;
                    float dV = sdSegment(vec2(p.x, abs(p.y)), vec2(0.0, 7.0), vec2(0.0, 17.5)) - 1.0;
                    float dDot = r - 1.8;

                    float dMain = min(min(dH, dV), dDot);
                    float outline = (1.0 - smoothstep(0.4, 2.0, min(dMain, dRing))) * 0.55;
                    float core = 1.0 - smoothstep(-0.6, 0.8, dMain);
                    float ring = (1.0 - smoothstep(-0.6, 0.8, dRing)) * 0.55;

                    vec3 reticleCol = vec3(0.42, 1.0, 0.55);
                    alpha = outline;
                    float bright = max(core, ring);
                    col = mix(col, reticleCol, bright);
                    alpha = max(alpha, bright * 0.9);

                    if (uHitMarker > 0.01)
                    {
                        vec2 ap = abs(p);
                        float dHit = sdSegment(ap, vec2(6.5, 6.5), vec2(15.0, 15.0)) - 1.35;
                        float hitMask = (1.0 - smoothstep(-0.6, 0.9, dHit)) * uHitMarker;
                        col = mix(col, vec3(1.0, 0.36, 0.16), hitMask);
                        alpha = max(alpha, hitMask);
                    }
                }

                // 2. Unrestricted mouse-aim grey circle + tether line from center crosshair
                if (cursorLen > 12.0)
                {
                    vec2 dir = uCursorPx / cursorLen;
                    vec2 lineStart = dir * min(22.0, cursorLen);
                    vec2 lineEnd = uCursorPx - dir * min(10.0, cursorLen);
                    if (cursorLen > 32.0)
                    {
                        float dLine = sdSegment(p, lineStart, lineEnd) - 0.8;
                        float lineMask = (1.0 - smoothstep(-0.5, 1.0, dLine)) * 0.38;
                        col = mix(col, vec3(0.84, 0.89, 0.94), lineMask);
                        alpha = max(alpha, lineMask);
                    }
                }

                if (distToCursor <= 18.0)
                {
                    float dCircle = abs(distToCursor - 9.5) - 1.25;
                    float dCenterPip = distToCursor - 1.8;
                    float dCursor = min(dCircle, dCenterPip);
                    float cOutline = (1.0 - smoothstep(0.4, 2.2, dCursor)) * 0.55;
                    float cCore = (1.0 - smoothstep(-0.6, 0.8, dCursor)) * 0.88;
                    col = mix(col, vec3(0.03, 0.04, 0.05), cOutline);
                    alpha = max(alpha, cOutline);
                    col = mix(col, vec3(0.82, 0.86, 0.90), cCore);
                    alpha = max(alpha, cCore);
                }

                if (alpha <= 0.001) discard;
                fragColor = vec4(col, alpha);
            }";

        private const string VertexShader = @"#version 330 core
            uniform vec2 uViewport;
            uniform float uScale;

            out vec2 vPos;

            void main()
            {
                // Two-triangle quad covering panel local coords x in [-4.15, 4.15], y in [-2.00, 1.38]
                const vec2 corners[6] = vec2[6](
                    vec2(-4.15, -2.00),
                    vec2( 4.15, -2.00),
                    vec2( 4.15,  1.38),
                    vec2(-4.15, -2.00),
                    vec2( 4.15,  1.38),
                    vec2(-4.15,  1.38)
                );

                vec2 local = corners[gl_VertexID];
                vPos = local;

                // Anchor bottom of panel slightly above the bottom edge of the viewport.
                vec2 centerPx = vec2(uViewport.x * 0.5, uScale * 2.00 + 8.0);
                vec2 pixelPos = centerPx + local * uScale;
                vec2 ndc = (pixelPos / uViewport) * 2.0 - 1.0;
                gl_Position = vec4(ndc, 0.0, 1.0);
            }";

        private const string FragmentShader = @"#version 330 core
            in vec2 vPos;
            out vec4 fragColor;

            uniform float uScale;
            uniform float uAirspeed;
            uniform float uPitch;
            uniform float uRoll;
            uniform float uAltitude;
            uniform float uAgl;
            uniform float uVerticalSpeed;
            uniform float uThrottle;
            uniform float uEnginePower;
            uniform float uStallWarning;
            uniform float uTime;
            uniform int uGunAmmo;
            uniform int uMissiles;
            uniform int uBombs;
            uniform int uPlayerHealth;
            uniform float uPlayerHealthFrac;
            uniform int uEnemiesRemaining;

            const float PI = 3.14159265359;
            const float TWO_PI = 6.28318530718;

            // --- Signed Distance Primitives ---
            float sdBox(vec2 p, vec2 b)
            {
                vec2 d = abs(p) - b;
                return length(max(d, 0.0)) + min(max(d.x, d.y), 0.0);
            }

            float sdRoundedBox(vec2 p, vec2 b, float r)
            {
                return sdBox(p, b - vec2(r)) - r;
            }

            float sdSegment(vec2 p, vec2 a, vec2 b)
            {
                vec2 pa = p - a, ba = b - a;
                float h = clamp(dot(pa, ba) / max(dot(ba, ba), 1e-6), 0.0, 1.0);
                return length(pa - ba * h);
            }

            float sdTriangleIsosceles(vec2 p, vec2 q)
            {
                p.x = abs(p.x);
                vec2 a = p - q * clamp(dot(p, q) / dot(q, q), 0.0, 1.0);
                vec2 b = p - q * vec2(clamp(p.x / q.x, 0.0, 1.0), 1.0);
                float s = -sign(q.y);
                vec2 d = min(
                    vec2(dot(a, a), s * (p.x * q.y - p.y * q.x)),
                    vec2(dot(b, b), s * (p.y - q.y)));
                return -sqrt(d.x) * sign(d.y);
            }

            // --- 3x5 Packed Bitmap Glyphs (15 bits, row 0 = top, col 0 = left) ---
            int DigitBits(int d)
            {
                const int bits[10] = int[10](
                    31599, // 0
                    11415, // 1
                    29671, // 2
                    29647, // 3
                    23497, // 4
                    31183, // 5
                    31215, // 6
                    29266, // 7
                    31727, // 8
                    31695  // 9
                );
                return bits[clamp(d, 0, 9)];
            }

            float SampleGlyph(vec2 p, vec2 origin, float charH, int bits)
            {
                float cell = charH / 5.0;
                vec2 rel = (p - origin) / cell;
                if (rel.x < 0.0 || rel.x >= 3.0 || rel.y < 0.0 || rel.y >= 5.0) return 0.0;
                int col = int(floor(rel.x));
                int row = 4 - int(floor(rel.y)); // flip so y-up maps row 0 to top
                int bitIndex = (4 - row) * 3 + (2 - col);
                return float((bits >> bitIndex) & 1);
            }

            float DrawNumber(vec2 p, vec2 center, float charH, int value, int minDigits)
            {
                int v = clamp(value, 0, 99999);
                int digits = max(minDigits, v >= 10000 ? 5 : (v >= 1000 ? 4 : (v >= 100 ? 3 : (v >= 10 ? 2 : 1))));
                float stepX = charH * 0.8;
                float totalW = float(digits - 1) * stepX + charH * 0.6;
                vec2 start = vec2(center.x - totalW * 0.5, center.y - charH * 0.5);

                float mask = 0.0;
                int divisor = 1;
                for (int i = 1; i < digits; i++) divisor *= 10;
                for (int i = 0; i < digits; i++)
                {
                    int d = (v / divisor) % 10;
                    mask = max(mask, SampleGlyph(p, start + vec2(float(i) * stepX, 0.0), charH, DigitBits(d)));
                    divisor = max(1, divisor / 10);
                }
                return mask;
            }

            float DrawLabel2(vec2 p, vec2 center, float charH, int g0, int g1)
            {
                float stepX = charH * 0.8;
                float totalW = stepX + charH * 0.6;
                vec2 start = vec2(center.x - totalW * 0.5, center.y - charH * 0.5);
                float m = SampleGlyph(p, start, charH, g0);
                m = max(m, SampleGlyph(p, start + vec2(stepX, 0.0), charH, g1));
                return m;
            }

            float DrawLabel3(vec2 p, vec2 center, float charH, int g0, int g1, int g2)
            {
                float stepX = charH * 0.8;
                float totalW = 2.0 * stepX + charH * 0.6;
                vec2 start = vec2(center.x - totalW * 0.5, center.y - charH * 0.5);
                float m = SampleGlyph(p, start, charH, g0);
                m = max(m, SampleGlyph(p, start + vec2(stepX, 0.0), charH, g1));
                m = max(m, SampleGlyph(p, start + vec2(stepX * 2.0, 0.0), charH, g2));
                return m;
            }

            float DrawLabel5(vec2 p, vec2 center, float charH, int g0, int g1, int g2, int g3, int g4)
            {
                float stepX = charH * 0.78;
                float totalW = 4.0 * stepX + charH * 0.6;
                vec2 start = vec2(center.x - totalW * 0.5, center.y - charH * 0.5);
                float m = SampleGlyph(p, start, charH, g0);
                m = max(m, SampleGlyph(p, start + vec2(stepX, 0.0), charH, g1));
                m = max(m, SampleGlyph(p, start + vec2(stepX * 2.0, 0.0), charH, g2));
                m = max(m, SampleGlyph(p, start + vec2(stepX * 3.0, 0.0), charH, g3));
                m = max(m, SampleGlyph(p, start + vec2(stepX * 4.0, 0.0), charH, g4));
                return m;
            }

            // Letter glyph constants
            const int G_A = 11245;
            const int G_B = 27566;
            const int G_E = 31143;
            const int G_G = 31087;
            const int G_H = 23533;
            const int G_I = 29847;
            const int G_L = 18727;
            const int G_M = 24429;
            const int G_N = 31597;
            const int G_O = 31599;
            const int G_P = 27556;
            const int G_R = 27565;
            const int G_S = 31183;
            const int G_T = 29842;
            const int G_U = 23407;
            const int G_V = 23402;
            const int G_W = 23413;

            // --- Shared Metallic Bezel & Glass Highlight ---
            vec4 ApplyBezelAndGlass(vec2 lp, float R, vec3 faceCol, float aa)
            {
                float r = length(lp);
                float outerMask = 1.0 - smoothstep(R + 0.08 - aa, R + 0.08 + aa, r);
                if (outerMask <= 0.0) return vec4(0.0);

                // Outer brushed gunmetal bezel with directional sheen from upper-left
                vec2 n = r > 1e-4 ? lp / r : vec2(0.0, 1.0);
                float bevelLight = dot(n, normalize(vec2(-0.55, 0.83))) * 0.5 + 0.5;
                vec3 bezelCol = mix(vec3(0.09, 0.10, 0.12), vec3(0.29, 0.31, 0.35), bevelLight);
                // Inner recessed rim shadow
                float innerLip = smoothstep(R - 0.02, R + 0.015, r);
                vec3 rimDark = mix(vec3(0.03, 0.035, 0.04), bezelCol, smoothstep(R, R + 0.045, r));
                // Subtle outer highlight edge
                float outerEdge = smoothstep(R + 0.055, R + 0.075, r);
                rimDark = mix(rimDark, vec3(0.04, 0.045, 0.05), outerEdge);

                // Subtle curved glass reflection on upper half of face
                float glassArc = smoothstep(0.15, 0.92, lp.y / R - lp.x * 0.25 / R);
                faceCol += vec3(0.06, 0.075, 0.095) * glassArc * smoothstep(R, R * 0.55, r);

                // Vignette at inner edge of glass
                faceCol *= mix(1.0, 0.65, smoothstep(R * 0.86, R, r));

                vec3 col = mix(faceCol, rimDark, innerLip);
                return vec4(col, outerMask);
            }

            // --- Pointer Needle Helper (clockwise angle from 12 o'clock) ---
            vec3 DrawNeedle(vec3 col, vec2 lp, float cwAngle, float len, float tail, float width, vec3 needleCol, float aa)
            {
                vec2 dir = vec2(sin(cwAngle), cos(cwAngle));
                vec2 a = -dir * tail;
                vec2 b = dir * len;
                // Soft drop shadow
                float dShadow = sdSegment(lp - vec2(0.015, -0.02), a, b) - width * 1.1;
                col = mix(col, col * 0.35, (1.0 - smoothstep(-aa * 2.0, aa * 2.5, dShadow)) * 0.7);
                // Tapered needle body
                vec2 pa = lp - a;
                float t = clamp(dot(pa, dir) / max(len + tail, 1e-4), 0.0, 1.0);
                float localW = mix(width, width * 0.35, t);
                float d = sdSegment(lp, a, b) - localW;
                col = mix(col, needleCol, 1.0 - smoothstep(-aa, aa, d));
                return col;
            }

            // --- 1. Airspeed Indicator (Left Dial) ---
            vec4 RenderAirspeed(vec2 p, vec2 center, float R, float aa)
            {
                vec2 lp = p - center;
                if (length(lp) > R + 0.12) return vec4(0.0);

                float r = length(lp);
                vec3 face = vec3(0.065, 0.075, 0.09) * mix(1.08, 0.88, clamp(r / R, 0.0, 1.0));

                // 270 deg sweep: -135 deg (0 m/s) clockwise to +135 deg (160 m/s)
                float cw = atan(lp.x, lp.y); // [-PI, PI], 0 = 12 o'clock, + = CW
                float minA = -0.75 * PI;
                float maxA =  0.75 * PI;
                float frac = (cw - minA) / (maxA - minA);
                float speedAtAngle = frac * 160.0;

                // Colored speed range arcs along r in [0.80, 0.87]
                float inArcBand = smoothstep(R * 0.79 - aa, R * 0.79 + aa, r)
                                * (1.0 - smoothstep(R * 0.87 - aa, R * 0.87 + aa, r));
                if (cw >= minA - 0.02 && cw <= maxA + 0.02)
                {
                    float spanMask = smoothstep(minA - 0.01, minA + 0.01, cw)
                                   * (1.0 - smoothstep(maxA - 0.01, maxA + 0.01, cw));
                    vec3 arcCol = vec3(0.18, 0.72, 0.34); // green cruise 42..125
                    if (speedAtAngle < 42.0) arcCol = vec3(0.85, 0.22, 0.18);       // stall red
                    else if (speedAtAngle > 145.0) arcCol = vec3(0.90, 0.20, 0.18); // overspeed red
                    else if (speedAtAngle > 125.0) arcCol = vec3(0.92, 0.72, 0.16); // caution amber
                    face = mix(face, arcCol, inArcBand * spanMask * 0.85);
                }

                // Ticks: 16 intervals (every 10 m/s), major every 20 m/s
                if (cw >= minA - 0.04 && cw <= maxA + 0.04)
                {
                    float stepA = (maxA - minA) / 16.0;
                    float idx = round((cw - minA) / stepA);
                    if (idx >= 0.0 && idx <= 16.0)
                    {
                        float tickA = minA + idx * stepA;
                        bool major = mod(idx, 2.0) < 0.5;
                        vec2 dir = vec2(sin(tickA), cos(tickA));
                        float r0 = major ? R * 0.67 : R * 0.74;
                        float r1 = R * 0.88;
                        float w = major ? R * 0.022 : R * 0.012;
                        float dTick = sdSegment(lp, dir * r0, dir * r1) - w;
                        vec3 tCol = major ? vec3(0.93, 0.94, 0.96) : vec3(0.68, 0.72, 0.78);
                        face = mix(face, tCol, 1.0 - smoothstep(-aa, aa, dTick));
                    }
                }

                // Label IAS above center
                float lbl = DrawLabel3(lp, vec2(0.0, R * 0.28), R * 0.14, G_I, G_A, G_S);
                face = mix(face, vec3(0.75, 0.79, 0.85), lbl * 0.9);

                // Digital readout inset window in bottom open gap
                vec2 boxCenter = vec2(0.0, -R * 0.48);
                float dBox = sdRoundedBox(lp - boxCenter, vec2(R * 0.34, R * 0.15), R * 0.04);
                face = mix(face, vec3(0.02, 0.025, 0.03), 1.0 - smoothstep(-aa, aa, dBox));
                float dBoxEdge = abs(dBox) - R * 0.012;
                face = mix(face, vec3(0.28, 0.32, 0.38), 1.0 - smoothstep(-aa, aa, dBoxEdge));
                float numMask = DrawNumber(lp, boxCenter, R * 0.16, int(round(uAirspeed)), 3);
                vec3 numCol = uAirspeed < 42.0 ? vec3(1.0, 0.38, 0.32) : vec3(0.95, 0.96, 0.98);
                face = mix(face, numCol, numMask);

                // Sweeping needle
                float spdFrac = clamp(uAirspeed / 160.0, 0.0, 1.0);
                float needleAngle = mix(minA, maxA, spdFrac);
                face = DrawNeedle(face, lp, needleAngle, R * 0.76, R * 0.18, R * 0.026, vec3(0.97, 0.97, 0.95), aa);

                // Center hub cap
                float cap = 1.0 - smoothstep(R * 0.085 - aa, R * 0.085 + aa, r);
                vec3 capCol = mix(vec3(0.18, 0.20, 0.23), vec3(0.45, 0.48, 0.52), clamp((lp.y - lp.x) / (R * 0.16) + 0.5, 0.0, 1.0));
                face = mix(face, capCol, cap);

                return ApplyBezelAndGlass(lp, R, face, aa);
            }

            // --- 2. Attitude Indicator / Artificial Horizon (Center Dial) ---
            vec4 RenderAttitude(vec2 p, vec2 center, float R, float aa)
            {
                vec2 lp = p - center;
                if (length(lp) > R + 0.42) return vec4(0.0);

                float r = length(lp);
                // Rotate horizon coordinates by bank angle (positive roll = right wing down -> horizon tilts CCW)
                float c = cos(uRoll);
                float s = sin(uRoll);
                vec2 hp = vec2(lp.x * c + lp.y * s, -lp.x * s + lp.y * c);

                // Map pitch so 10 degrees = 0.145 * R (shows ~+-60 deg comfortably inside ball)
                float pitchPerRad = R * 0.83;
                float horizonY = hp.y + uPitch * pitchPerRad;

                // Sky & Ground gradients
                float skyT = clamp(horizonY / (R * 0.95), 0.0, 1.0);
                vec3 skyCol = mix(vec3(0.24, 0.56, 0.86), vec3(0.07, 0.22, 0.48), pow(skyT, 0.75));
                float gndT = clamp(-horizonY / (R * 0.95), 0.0, 1.0);
                vec3 gndCol = mix(vec3(0.55, 0.34, 0.17), vec3(0.24, 0.13, 0.06), pow(gndT, 0.75));

                vec3 face = mix(gndCol, skyCol, smoothstep(-aa, aa, horizonY));

                // Horizon line
                float dHorizon = abs(horizonY) - R * 0.014;
                face = mix(face, vec3(0.96, 0.97, 0.98), 1.0 - smoothstep(-aa, aa, dHorizon));

                // Pitch ladder inside inner radius (r < 0.74 * R)
                float ladderClip = 1.0 - smoothstep(R * 0.72 - aa, R * 0.75 + aa, r);
                if (ladderClip > 0.0)
                {
                    float degStep = (5.0 * PI / 180.0) * pitchPerRad; // 5-degree increments
                    float k = round(horizonY / degStep);
                    if (abs(k) >= 1.0 && abs(k) <= 12.0)
                    {
                        float yLine = k * degStep;
                        bool major10 = mod(abs(k), 2.0) < 0.5;
                        float halfW = major10 ? R * 0.24 : R * 0.11;
                        float dBar = sdSegment(vec2(hp.x, horizonY), vec2(-halfW, yLine), vec2(halfW, yLine)) - R * 0.011;
                        float barMask = (1.0 - smoothstep(-aa, aa, dBar)) * ladderClip;
                        face = mix(face, vec3(0.95, 0.96, 0.98), barMask * 0.9);
                    }
                }

                // Upper bank-angle tick scale (fixed to instrument frame at top arc)
                float cw = atan(lp.x, lp.y);
                const float bankDegs[11] = float[11](-60.0, -45.0, -30.0, -20.0, -10.0, 0.0, 10.0, 20.0, 30.0, 45.0, 60.0);
                for (int i = 0; i < 11; i++)
                {
                    float a = bankDegs[i] * PI / 180.0;
                    if (abs(cw - a) < 0.09)
                    {
                        vec2 dir = vec2(sin(a), cos(a));
                        bool major = (abs(bankDegs[i]) == 0.0 || abs(bankDegs[i]) == 30.0 || abs(bankDegs[i]) == 60.0);
                        float r0 = major ? R * 0.80 : R * 0.84;
                        float r1 = R * 0.93;
                        float w = major ? R * 0.018 : R * 0.011;
                        float dTick = sdSegment(lp, dir * r0, dir * r1) - w;
                        vec3 tickCol = (bankDegs[i] == 0.0) ? vec3(1.0, 0.65, 0.15) : vec3(0.94, 0.95, 0.97);
                        face = mix(face, tickCol, 1.0 - smoothstep(-aa, aa, dTick));
                    }
                }

                // Moving roll pointer triangle pointing up to bank scale (rotates with sky top)
                vec2 triP = vec2(hp.x, hp.y - R * 0.78);
                float dPtr = sdTriangleIsosceles(vec2(triP.x, -triP.y), vec2(R * 0.065, R * 0.095));
                face = mix(face, vec3(1.0, 0.72, 0.18), 1.0 - smoothstep(-aa, aa, dPtr));

                // Fixed aircraft reference symbol in the center (orange wings + center dot)
                vec3 refCol = vec3(1.0, 0.62, 0.12);
                vec3 outlineCol = vec3(0.05, 0.05, 0.06);
                float dWingL = min(
                    sdSegment(lp, vec2(-R * 0.42, 0.0), vec2(-R * 0.14, 0.0)),
                    sdSegment(lp, vec2(-R * 0.14, 0.0), vec2(-R * 0.14, -R * 0.075)));
                float dWingR = min(
                    sdSegment(lp, vec2( R * 0.14, 0.0), vec2( R * 0.42, 0.0)),
                    sdSegment(lp, vec2( R * 0.14, 0.0), vec2( R * 0.14, -R * 0.075)));
                float dWings = min(dWingL, dWingR);
                float dDot = length(lp) - R * 0.042;
                float dRef = min(dWings - R * 0.025, dDot);

                face = mix(face, outlineCol, 1.0 - smoothstep(-aa, aa, dRef - R * 0.016));
                face = mix(face, refCol, 1.0 - smoothstep(-aa, aa, dRef));

                vec4 dial = ApplyBezelAndGlass(lp, R, face, aa);

                // Top bezel STALL annunciator pill (lights up & pulses when approaching/in stall)
                if (uStallWarning > 0.05)
                {
                    vec2 pillCenter = vec2(0.0, R + 0.13);
                    float dPill = sdRoundedBox(lp - pillCenter, vec2(0.42, 0.11), 0.045);
                    float pillMask = 1.0 - smoothstep(-aa, aa, dPill);
                    if (pillMask > 0.0)
                    {
                        float pulse = 0.65 + 0.35 * sin(uTime * 18.0);
                        float intensity = mix(0.55, 1.0, uStallWarning) * pulse;
                        vec3 pillBg = mix(vec3(0.32, 0.05, 0.04), vec3(0.95, 0.14, 0.10), intensity);
                        float border = 1.0 - smoothstep(-aa, aa, abs(dPill) - 0.014);
                        vec3 pillCol = mix(pillBg, vec3(1.0, 0.55, 0.45), border * 0.6);
                        float txt = DrawLabel5(lp, pillCenter, 0.13, G_S, G_T, G_A, G_L, G_L);
                        pillCol = mix(pillCol, vec3(1.0, 0.97, 0.94), txt);
                        dial = vec4(mix(dial.rgb, pillCol, pillMask), max(dial.a, pillMask));
                    }
                }

                return dial;
            }

            // --- 3. Altimeter (Right Dial) ---
            vec4 RenderAltimeter(vec2 p, vec2 center, float R, float aa)
            {
                vec2 lp = p - center;
                if (length(lp) > R + 0.42) return vec4(0.0);

                float r = length(lp);
                vec3 face = vec3(0.065, 0.075, 0.09) * mix(1.08, 0.88, clamp(r / R, 0.0, 1.0));

                // 50 ticks around 360 deg (10 major = 0..9, 5 minor per division)
                float cw = mod(atan(lp.x, lp.y) + TWO_PI, TWO_PI);
                float stepA = TWO_PI / 50.0;
                float idx = mod(round(cw / stepA), 50.0);
                float tickA = idx * stepA;
                bool major = mod(idx, 5.0) < 0.5;
                vec2 dir = vec2(sin(tickA), cos(tickA));
                float r0 = major ? R * 0.72 : R * 0.80;
                float r1 = R * 0.90;
                float w = major ? R * 0.022 : R * 0.011;
                float dTick = sdSegment(lp, dir * r0, dir * r1) - w;
                vec3 tCol = major ? vec3(0.93, 0.94, 0.96) : vec3(0.65, 0.70, 0.76);
                face = mix(face, tCol, 1.0 - smoothstep(-aa, aa, dTick));

                // Small 0..9 numerals around major ticks
                float majorIdx = mod(round(cw / (TWO_PI / 10.0)), 10.0);
                float numA = majorIdx * (TWO_PI / 10.0);
                vec2 numPos = vec2(sin(numA), cos(numA)) * (R * 0.58);
                float dNum = DrawNumber(lp, numPos, R * 0.13, int(majorIdx), 1);
                face = mix(face, vec3(0.86, 0.89, 0.94), dNum * 0.92);

                // Label ALT above center
                float lbl = DrawLabel3(lp, vec2(0.0, R * 0.25), R * 0.13, G_A, G_L, G_T);
                face = mix(face, vec3(0.72, 0.76, 0.82), lbl * 0.88);

                // Digital altitude readout inset box
                vec2 boxCenter = vec2(0.0, -R * 0.26);
                float dBox = sdRoundedBox(lp - boxCenter, vec2(R * 0.34, R * 0.13), R * 0.035);
                face = mix(face, vec3(0.02, 0.025, 0.03), 1.0 - smoothstep(-aa, aa, dBox));
                float dBoxEdge = abs(dBox) - R * 0.011;
                face = mix(face, vec3(0.28, 0.32, 0.38), 1.0 - smoothstep(-aa, aa, dBoxEdge));
                float altNum = DrawNumber(lp, boxCenter, R * 0.145, int(round(uAltitude)), 3);
                face = mix(face, vec3(0.95, 0.96, 0.98), altNum);

                // Dual altimeter hands: short = 1000 m/rev, long = 100 m/rev
                float shortAngle = mod(uAltitude / 1000.0, 1.0) * TWO_PI;
                float longAngle = mod(uAltitude / 100.0, 1.0) * TWO_PI;
                face = DrawNeedle(face, lp, shortAngle, R * 0.46, R * 0.12, R * 0.040, vec3(0.86, 0.89, 0.94), aa);
                face = DrawNeedle(face, lp, longAngle,  R * 0.78, R * 0.18, R * 0.022, vec3(0.98, 0.98, 0.96), aa);

                // Center hub cap
                float cap = 1.0 - smoothstep(R * 0.085 - aa, R * 0.085 + aa, r);
                vec3 capCol = mix(vec3(0.18, 0.20, 0.23), vec3(0.45, 0.48, 0.52), clamp((lp.y - lp.x) / (R * 0.16) + 0.5, 0.0, 1.0));
                face = mix(face, capCol, cap);

                vec4 dial = ApplyBezelAndGlass(lp, R, face, aa);

                // Top bezel LOW-AGL terrain warning pill
                if (uAgl < 75.0)
                {
                    vec2 pillCenter = vec2(0.0, R + 0.13);
                    float dPill = sdRoundedBox(lp - pillCenter, vec2(0.32, 0.11), 0.045);
                    float pillMask = 1.0 - smoothstep(-aa, aa, dPill);
                    if (pillMask > 0.0)
                    {
                        bool critical = uAgl < 35.0;
                        float pulse = critical ? (0.60 + 0.40 * sin(uTime * 16.0)) : 0.9;
                        vec3 pillBg = critical ? vec3(0.92, 0.16, 0.12) * pulse : vec3(0.82, 0.52, 0.08) * pulse;
                        float border = 1.0 - smoothstep(-aa, aa, abs(dPill) - 0.014);
                        vec3 pillCol = mix(pillBg, vec3(1.0, 0.85, 0.55), border * 0.55);
                        float txt = DrawLabel3(lp, pillCenter, 0.13, G_L, G_O, G_W);
                        pillCol = mix(pillCol, vec3(1.0, 0.97, 0.94), txt);
                        dial = vec4(mix(dial.rgb, pillCol, pillMask), max(dial.a, pillMask));
                    }
                }

                return dial;
            }

            // --- Compact Side Vertical Bar (Throttle or VSI) ---
            vec4 RenderSideBar(vec2 p, vec2 center, float halfW, float halfH, float fillLow, float fillHigh, float markerY, vec3 fillCol, int g0, int g1, int g2, bool centerTick, float aa)
            {
                vec2 lp = p - center;
                if (abs(lp.x) > halfW + 0.18 || abs(lp.y) > halfH + 0.30) return vec4(0.0);

                // Outer frame
                float dOuter = sdRoundedBox(lp, vec2(halfW + 0.045, halfH + 0.045), 0.06);
                float outerMask = 1.0 - smoothstep(-aa, aa, dOuter);

                // Inner track
                float dInner = sdRoundedBox(lp, vec2(halfW, halfH), 0.035);
                float innerMask = 1.0 - smoothstep(-aa, aa, dInner);

                vec3 col = mix(vec3(0.18, 0.20, 0.23), vec3(0.05, 0.06, 0.075), innerMask);

                // Filled region inside track
                float yNorm = clamp((lp.y + halfH) / (2.0 * halfH), 0.0, 1.0);
                float lo = min(fillLow, fillHigh);
                float hi = max(fillLow, fillHigh);
                float inFill = smoothstep(lo - aa / (2.0 * halfH), lo + aa / (2.0 * halfH), yNorm)
                             * (1.0 - smoothstep(hi - aa / (2.0 * halfH), hi + aa / (2.0 * halfH), yNorm));
                col = mix(col, fillCol, inFill * innerMask * 0.88);

                // Scale ticks (0%, 25%, 50%, 75%, 100%)
                if (innerMask > 0.0)
                {
                    float yTick = round(yNorm * 4.0) * 0.25;
                    float dy = abs(yNorm - yTick) * (2.0 * halfH);
                    bool isMid = centerTick && abs(yTick - 0.5) < 0.01;
                    float tickW = isMid ? halfW : halfW * 0.45;
                    if (abs(lp.x) > halfW - tickW || isMid)
                    {
                        float tMask = 1.0 - smoothstep(-aa, aa, dy - (isMid ? 0.014 : 0.009));
                        col = mix(col, vec3(0.85, 0.88, 0.92), tMask * innerMask * (isMid ? 0.9 : 0.45));
                    }
                }

                // Commanded marker line
                float my = mix(-halfH, halfH, clamp(markerY, 0.0, 1.0));
                float dMark = sdSegment(lp, vec2(-halfW - 0.015, my), vec2(halfW + 0.015, my)) - 0.018;
                col = mix(col, vec3(0.98, 0.98, 0.98), (1.0 - smoothstep(-aa, aa, dMark)) * outerMask);

                // Label beneath bar
                float lbl = DrawLabel3(lp, vec2(0.0, -halfH - 0.16), 0.13, g0, g1, g2);
                vec3 outRgb = mix(col, vec3(0.85, 0.88, 0.92), lbl);
                float outA = max(outerMask, lbl * 0.95);
                return vec4(outRgb, outA);
            }

            // --- Weapon & Ammo Status Strip (Bottom of Dash Cowl) ---
            vec4 RenderWeaponStrip(vec2 p, float aa)
            {
                vec2 center = vec2(0.0, -1.36);
                vec2 lp = p - center;
                float dStrip = sdRoundedBox(lp, vec2(3.65, 0.19), 0.08);
                float stripMask = 1.0 - smoothstep(-aa, aa, dStrip);
                if (stripMask <= 0.0) return vec4(0.0);

                float dBorder = abs(dStrip) - 0.015;
                vec3 col = mix(vec3(0.04, 0.05, 0.065), vec3(0.25, 0.29, 0.35), 1.0 - smoothstep(-aa, aa, dBorder));

                // Subtle bay separators
                float div1 = 1.0 - smoothstep(-aa, aa, sdSegment(lp, vec2(-2.05, -0.11), vec2(-2.05, 0.11)) - 0.008);
                float div2 = 1.0 - smoothstep(-aa, aa, sdSegment(lp, vec2( 0.16, -0.11), vec2( 0.16, 0.11)) - 0.008);
                float div3 = 1.0 - smoothstep(-aa, aa, sdSegment(lp, vec2( 1.94, -0.11), vec2( 1.94, 0.11)) - 0.008);
                col = mix(col, vec3(0.22, 0.26, 0.32), max(div1, max(div2, div3)) * 0.75);

                // Bay 1: Machine gun ammo (GUN 400)
                float gunLbl = DrawLabel3(lp, vec2(-3.16, 0.0), 0.15, G_G, G_U, G_N);
                col = mix(col, vec3(0.74, 0.79, 0.86), gunLbl);
                float gunNum = DrawNumber(lp, vec2(-2.52, 0.0), 0.17, uGunAmmo, 3);
                vec3 gunCol = uGunAmmo == 0 ? vec3(0.95, 0.26, 0.22) : (uGunAmmo <= 80 ? vec3(0.98, 0.72, 0.18) : vec3(0.96, 0.97, 0.99));
                col = mix(col, gunCol, gunNum);

                // Bay 2: 6 Missiles (MSL + 6 pips + count)
                float mslLbl = DrawLabel3(lp, vec2(-1.66, 0.0), 0.15, G_M, G_S, G_L);
                col = mix(col, vec3(0.74, 0.79, 0.86), mslLbl);
                for (int i = 0; i < 6; i++)
                {
                    vec2 pipPos = vec2(-1.24 + float(i) * 0.155, 0.0);
                    float dPip = sdRoundedBox(lp - pipPos, vec2(0.050, 0.092), 0.02);
                    bool loaded = i < uMissiles;
                    vec3 pipCol = loaded ? vec3(1.0, 0.62, 0.16) : vec3(0.14, 0.16, 0.20);
                    col = mix(col, pipCol, 1.0 - smoothstep(-aa, aa, dPip));
                }
                float mslNum = DrawNumber(lp, vec2(-0.18, 0.0), 0.17, uMissiles, 1);
                vec3 mslNumCol = uMissiles == 0 ? vec3(0.95, 0.26, 0.22) : vec3(1.0, 0.80, 0.45);
                col = mix(col, mslNumCol, mslNum);

                // Bay 3: 2 Large Bombs (BMB + 2 pips + count)
                float bmbLbl = DrawLabel3(lp, vec2(0.56, 0.0), 0.15, G_B, G_M, G_B);
                col = mix(col, vec3(0.74, 0.79, 0.86), bmbLbl);
                for (int i = 0; i < 2; i++)
                {
                    vec2 pipPos = vec2(1.02 + float(i) * 0.24, 0.0);
                    float dPip = sdRoundedBox(lp - pipPos, vec2(0.085, 0.090), 0.035);
                    bool loaded = i < uBombs;
                    vec3 pipCol = loaded ? vec3(0.96, 0.34, 0.24) : vec3(0.14, 0.16, 0.20);
                    col = mix(col, pipCol, 1.0 - smoothstep(-aa, aa, dPip));
                }
                float bmbNum = DrawNumber(lp, vec2(1.58, 0.0), 0.17, uBombs, 1);
                vec3 bmbNumCol = uBombs == 0 ? vec3(0.95, 0.26, 0.22) : vec3(1.0, 0.60, 0.48);
                col = mix(col, bmbNumCol, bmbNum);

                // Bay 4: Remaining Enemies (diamond indicator + ENM + count)
                vec2 ep = lp - vec2(2.22, 0.0);
                float dDiamond = (abs(ep.x) + abs(ep.y)) - 0.085;
                vec3 enmIconCol = uEnemiesRemaining > 0 ? vec3(0.98, 0.26, 0.22) : vec3(0.24, 0.88, 0.46);
                col = mix(col, enmIconCol, 1.0 - smoothstep(-aa, aa, dDiamond));
                float enmLbl = DrawLabel3(lp, vec2(2.64, 0.0), 0.15, G_E, G_N, G_M);
                col = mix(col, vec3(0.74, 0.79, 0.86), enmLbl);
                float enmNum = DrawNumber(lp, vec2(3.22, 0.0), 0.17, uEnemiesRemaining, 1);
                vec3 enmNumCol = uEnemiesRemaining > 0 ? vec3(1.0, 0.38, 0.30) : vec3(0.28, 0.92, 0.48);
                col = mix(col, enmNumCol, enmNum);

                return vec4(col, stripMask * 0.92);
            }

            // --- Player Health Bar Strip (Bottom of Dash Cowl) ---
            vec4 RenderHealthStrip(vec2 p, float aa)
            {
                vec2 center = vec2(0.0, -1.74);
                vec2 lp = p - center;
                float dStrip = sdRoundedBox(lp, vec2(3.65, 0.16), 0.07);
                float stripMask = 1.0 - smoothstep(-aa, aa, dStrip);
                if (stripMask <= 0.0) return vec4(0.0);

                float dBorder = abs(dStrip) - 0.015;
                vec3 col = mix(vec3(0.04, 0.05, 0.065), vec3(0.25, 0.29, 0.35), 1.0 - smoothstep(-aa, aa, dBorder));

                // Left label: HP
                float hpLbl = DrawLabel2(lp, vec2(-3.26, 0.0), 0.15, G_H, G_P);
                col = mix(col, vec3(0.76, 0.82, 0.88), hpLbl);

                // Inner health bar track: x in [-2.85, +2.65]
                vec2 barCenter = vec2(-0.10, 0.0);
                vec2 barHalf = vec2(2.75, 0.085);
                float dTrack = sdRoundedBox(lp - barCenter, barHalf, 0.035);
                float trackMask = 1.0 - smoothstep(-aa, aa, dTrack);
                col = mix(col, vec3(0.09, 0.07, 0.08), trackMask);

                float frac = clamp(uPlayerHealthFrac, 0.0, 1.0);
                float barX = clamp((lp.x - (barCenter.x - barHalf.x)) / (2.0 * barHalf.x), 0.0, 1.0);
                float inFill = 1.0 - smoothstep(frac - aa / (2.0 * barHalf.x), frac + aa / (2.0 * barHalf.x), barX);

                vec3 fillCol = frac > 0.55
                    ? mix(vec3(0.95, 0.74, 0.14), vec3(0.18, 0.84, 0.38), (frac - 0.55) / 0.45)
                    : mix(vec3(0.96, 0.18, 0.14), vec3(0.95, 0.74, 0.14), frac / 0.55);
                if (frac < 0.30 && frac > 0.0)
                {
                    fillCol *= 0.72 + 0.28 * sin(uTime * 14.0);
                }
                float yHighlight = clamp((lp.y + barHalf.y) / (2.0 * barHalf.y), 0.0, 1.0);
                fillCol *= mix(0.84, 1.14, yHighlight);
                col = mix(col, fillCol, trackMask * inFill);

                // Right numeric health readout
                float hpNum = DrawNumber(lp, vec2(3.16, 0.0), 0.16, uPlayerHealth, 3);
                vec3 numCol = frac <= 0.30 ? vec3(1.0, 0.36, 0.28) : vec3(0.95, 0.97, 0.99);
                col = mix(col, numCol, hpNum);

                return vec4(col, stripMask * 0.94);
            }

            void main()
            {
                float aa = 1.15 / max(uScale, 1.0);

                // Translucent dark cockpit dash cowl backing
                float dPanel = sdRoundedBox(vPos - vec2(0.0, -0.39), vec2(3.98, 1.56), 0.34);
                float panelMask = 1.0 - smoothstep(-aa, aa, dPanel);
                float panelBorder = 1.0 - smoothstep(-aa, aa, abs(dPanel) - 0.018);

                vec3 color = mix(vec3(0.03, 0.038, 0.05), vec3(0.24, 0.27, 0.32), panelBorder * 0.75);
                float alpha = panelMask * 0.62;

                // 3 primary round flight instruments
                vec4 asi = RenderAirspeed(vPos, vec2(-2.22, -0.02), 0.96, aa);
                vec4 att = RenderAttitude(vPos, vec2( 0.00,  0.02), 1.04, aa);
                vec4 alt = RenderAltimeter(vPos, vec2( 2.22, -0.02), 0.96, aa);

                // Left Throttle bar (0..1)
                vec3 thrCol = mix(vec3(0.22, 0.68, 0.92), vec3(1.0, 0.58, 0.15), smoothstep(0.65, 1.0, uEnginePower));
                vec4 thr = RenderSideBar(vPos, vec2(-3.55, 0.05), 0.125, 0.74, 0.0, clamp(uEnginePower, 0.0, 1.0), clamp(uThrottle, 0.0, 1.0), thrCol, G_T, G_H, G_R, false, aa);

                // Right Vertical Speed Indicator bar (-40..+40 m/s centered at 0.5)
                float vsiNorm = clamp(0.5 + uVerticalSpeed / 80.0, 0.0, 1.0);
                vec3 vsiCol = uVerticalSpeed >= 0.0 ? vec3(0.22, 0.80, 0.46) : vec3(0.95, 0.42, 0.22);
                vec4 vsi = RenderSideBar(vPos, vec2( 3.55, 0.05), 0.125, 0.74, 0.5, vsiNorm, vsiNorm, vsiCol, G_V, G_S, G_I, true, aa);

                // Bottom weapon & health strips
                vec4 weapons = RenderWeaponStrip(vPos, aa);
                vec4 health = RenderHealthStrip(vPos, aa);

                // Composite layers cleanly (standard source-over)
                vec4 layers[7] = vec4[7](health, weapons, thr, vsi, asi, alt, att);
                for (int i = 0; i < 7; i++)
                {
                    float a = layers[i].a;
                    color = mix(color, layers[i].rgb, a);
                    alpha = max(alpha, a);
                }

                if (alpha <= 0.001) discard;
                fragColor = vec4(color, alpha);
            }";

        public CockpitHud()
        {
            int vs = 0, fs = 0, cvs = 0, cfs = 0;
            try
            {
                vs = CompileShader(ShaderType.VertexShader, VertexShader);
                fs = CompileShader(ShaderType.FragmentShader, FragmentShader);
                program = LinkProgram(vs, fs, "CockpitHud");

                cvs = CompileShader(ShaderType.VertexShader, CrosshairVertexShader);
                cfs = CompileShader(ShaderType.FragmentShader, CrosshairFragmentShader);
                crosshairProgram = LinkProgram(cvs, cfs, "Crosshair");

                vao = GL.GenVertexArray();

                viewportLoc = GL.GetUniformLocation(program, "uViewport");
                scaleLoc = GL.GetUniformLocation(program, "uScale");
                airspeedLoc = GL.GetUniformLocation(program, "uAirspeed");
                pitchLoc = GL.GetUniformLocation(program, "uPitch");
                rollLoc = GL.GetUniformLocation(program, "uRoll");
                altitudeLoc = GL.GetUniformLocation(program, "uAltitude");
                aglLoc = GL.GetUniformLocation(program, "uAgl");
                verticalSpeedLoc = GL.GetUniformLocation(program, "uVerticalSpeed");
                throttleLoc = GL.GetUniformLocation(program, "uThrottle");
                enginePowerLoc = GL.GetUniformLocation(program, "uEnginePower");
                stallWarningLoc = GL.GetUniformLocation(program, "uStallWarning");
                timeLoc = GL.GetUniformLocation(program, "uTime");
                gunAmmoLoc = GL.GetUniformLocation(program, "uGunAmmo");
                missilesLoc = GL.GetUniformLocation(program, "uMissiles");
                bombsLoc = GL.GetUniformLocation(program, "uBombs");
                playerHealthLoc = GL.GetUniformLocation(program, "uPlayerHealth");
                playerHealthFracLoc = GL.GetUniformLocation(program, "uPlayerHealthFrac");
                enemiesRemainingLoc = GL.GetUniformLocation(program, "uEnemiesRemaining");

                crossViewportLoc = GL.GetUniformLocation(crosshairProgram, "uViewport");
                crossHitLoc = GL.GetUniformLocation(crosshairProgram, "uHitMarker");
                crossCursorLoc = GL.GetUniformLocation(crosshairProgram, "uCursorPx");
            }
            catch
            {
                Dispose();
                throw;
            }
            finally
            {
                if (vs != 0) GL.DeleteShader(vs);
                if (fs != 0) GL.DeleteShader(fs);
                if (cvs != 0) GL.DeleteShader(cvs);
                if (cfs != 0) GL.DeleteShader(cfs);
            }
        }

        private static int LinkProgram(int vs, int fs, string label)
        {
            int p = GL.CreateProgram();
            GL.AttachShader(p, vs);
            GL.AttachShader(p, fs);
            GL.LinkProgram(p);
            GL.GetProgram(p, GetProgramParameterName.LinkStatus, out int linked);
            if (linked == 0)
            {
                string log = GL.GetProgramInfoLog(p);
                GL.DeleteProgram(p);
                throw new InvalidOperationException($"{label} shader link error: {log}");
            }
            GL.DetachShader(p, vs);
            GL.DetachShader(p, fs);
            return p;
        }

        private static int CompileShader(ShaderType type, string source)
        {
            int shader = GL.CreateShader(type);
            GL.ShaderSource(shader, source);
            GL.CompileShader(shader);
            GL.GetShader(shader, ShaderParameter.CompileStatus, out int ok);
            if (ok != 0) return shader;
            string log = GL.GetShaderInfoLog(shader);
            GL.DeleteShader(shader);
            throw new InvalidOperationException($"CockpitHud {type} compile error: {log}");
        }

        public void Draw(in CockpitTelemetry telemetry, int framebufferWidth, int framebufferHeight, bool showInstruments = true, bool showCrosshair = true)
        {
            if (disposed || framebufferWidth <= 0 || framebufferHeight <= 0) return;
            if (!showInstruments && !showCrosshair) return;

            // Keep instrument cluster compact and proportional across all aspect ratios.
            float scale = Math.Clamp(MathF.Min(framebufferHeight * 0.076f, framebufferWidth * 0.052f), 44f, 94f);

            bool depthTest = GL.IsEnabled(EnableCap.DepthTest);
            bool blend = GL.IsEnabled(EnableCap.Blend);
            bool cull = GL.IsEnabled(EnableCap.CullFace);

            GL.Disable(EnableCap.DepthTest);
            GL.Disable(EnableCap.CullFace);
            GL.Enable(EnableCap.Blend);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

            try
            {
                GL.Viewport(0, 0, framebufferWidth, framebufferHeight);
                GL.BindVertexArray(vao);

                if (showInstruments)
                {
                    GL.UseProgram(program);
                    GL.Uniform2(viewportLoc, (float)framebufferWidth, (float)framebufferHeight);
                    GL.Uniform1(scaleLoc, scale);
                    GL.Uniform1(airspeedLoc, Math.Max(0f, telemetry.Airspeed));
                    GL.Uniform1(pitchLoc, telemetry.PitchRadians);
                    GL.Uniform1(rollLoc, telemetry.RollRadians);
                    GL.Uniform1(altitudeLoc, Math.Max(0f, telemetry.AltitudeMsl));
                    GL.Uniform1(aglLoc, Math.Max(0f, telemetry.AltitudeAgl));
                    GL.Uniform1(verticalSpeedLoc, telemetry.VerticalSpeed);
                    GL.Uniform1(throttleLoc, Math.Clamp(telemetry.Throttle, 0f, 1f));
                    GL.Uniform1(enginePowerLoc, Math.Clamp(telemetry.EnginePower, 0f, 1f));
                    GL.Uniform1(stallWarningLoc, Math.Clamp(telemetry.StallWarning, 0f, 1f));
                    GL.Uniform1(timeLoc, telemetry.Time);
                    GL.Uniform1(gunAmmoLoc, Math.Max(0, telemetry.GunAmmo));
                    GL.Uniform1(missilesLoc, Math.Max(0, telemetry.MissilesRemaining));
                    GL.Uniform1(bombsLoc, Math.Max(0, telemetry.BombsRemaining));
                    GL.Uniform1(playerHealthLoc, (int)MathF.Ceiling(Math.Max(0f, telemetry.PlayerHealth)));
                    GL.Uniform1(playerHealthFracLoc, Math.Clamp(telemetry.PlayerHealth / Math.Max(1f, telemetry.PlayerMaxHealth), 0f, 1f));
                    GL.Uniform1(enemiesRemainingLoc, Math.Max(0, telemetry.EnemiesRemaining));
                    GL.DrawArrays(PrimitiveType.Triangles, 0, 6);
                }

                if (showCrosshair && !telemetry.Crashed)
                {
                    GL.UseProgram(crosshairProgram);
                    GL.Uniform2(crossViewportLoc, (float)framebufferWidth, (float)framebufferHeight);
                    GL.Uniform1(crossHitLoc, Math.Clamp(telemetry.HitMarker, 0f, 1f));
                    GL.Uniform2(crossCursorLoc, telemetry.AimCursorOffsetPx.X, telemetry.AimCursorOffsetPx.Y);
                    GL.DrawArrays(PrimitiveType.Triangles, 0, 6);
                }
            }
            finally
            {
                GL.BindVertexArray(0);
                GL.UseProgram(0);
                if (depthTest) GL.Enable(EnableCap.DepthTest);
                if (!blend) GL.Disable(EnableCap.Blend);
                if (cull) GL.Enable(EnableCap.CullFace);
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (program != 0) GL.DeleteProgram(program);
            if (crosshairProgram != 0) GL.DeleteProgram(crosshairProgram);
            if (vao != 0) GL.DeleteVertexArray(vao);
            program = crosshairProgram = vao = 0;
        }
    }
}
