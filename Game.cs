using System;
using System.Collections.Generic;
using System.Diagnostics;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;
using GPR_Command;

namespace GPR202_Assessment3
{
    public class Game : GameWindow
    {
        private Stopwatch timer;
        private readonly AudioSettings audioSettings = new();
        private GameAudio? audio;
        private readonly ParticleSystem explosion = new();
        private readonly VictoryFireworks fireworks = new();
        private readonly CombatSimulation combat = new();
        private readonly List<TargetSphere> combatTargets = new(16);
        private readonly List<ObstacleBox> combatObstacles = new(4);
        private readonly List<int> combatHits = new(16);
        private readonly List<CombatExplosionEvent> combatExplosions = new(8);
        private CombatRenderer? combatRenderer;
        private ParticleRenderer? particleRenderer;
        private bool triggerGuns, requestMissile, requestBomb;
        private bool crashed;
        private float crashTime;
        private Vector3 initialSpawn;
        private Vector3 smoothCamera, smoothTarget;
        private bool cameraReady;
        private double cameraTime;
        private int[] viewport = new int[4];

        Camera cam;
        private readonly FlightSettings flightSettings = new();
        private FlightPhysics flight = null!;
        private float pitchInput, rollInput, yawInput;
        private Vector2 virtualStick;
        private bool mouseLocked = true;
        private bool ignoreNextMouseDelta = true;
        Skybox skybox;
        Plane plane;
        Terrain terrain;
        Balloon balloon;
        DirectionalLight sunLight;
        AmbientLight ambientLight;
        private Material planeMaterial = null!;
        ShadowSettings shadowSettings;
        ShadowMap shadowMap;
        private Bloom bloom = null!;
        private CockpitHud? cockpitHud;
        private readonly GpuTimings gpuTimings = new();
        private BillBoard mountainBillboard = null!;

        public const int balloons_total = 8;
        public int balloons_left = balloons_total;
        public List<BalloonState> m_balloons;

        float totalTime = 0f;
        int frameCount = 0;
        float averageFPS = 0f;
        double currentFPS = 0f;


        // Fixed timestep
        private float _accumulator = 0f;
        private const float FixedDeltaTime = 1f / 120f;

        public Game() : base(GameWindowSettings.Default, NativeWindowSettings.Default)
        {
            this.CenterWindow(new Vector2i(1920, 1080));
            this.VSync = VSyncMode.Off;
            this.WindowState = WindowState.Maximized;
            this.CursorState = CursorState.Grabbed;
        }

        private void SetMouseLock(bool locked)
        {
            mouseLocked = locked;
            CursorState = locked && IsFocused ? CursorState.Grabbed : CursorState.Normal;
            virtualStick = Vector2.Zero;
            ignoreNextMouseDelta = true;
        }

        protected override void OnFocusedChanged(FocusedChangedEventArgs e)
        {
            base.OnFocusedChanged(e);
            CursorState = (e.IsFocused && mouseLocked) ? CursorState.Grabbed : CursorState.Normal;
            virtualStick = Vector2.Zero;
            ignoreNextMouseDelta = true;
        }

        protected override void OnLoad()
        {
            timer = new Stopwatch();
            timer.Start();

            // OpenGL Setup
            GL.Enable(EnableCap.DepthTest);
            GL.ClearColor(new Color4(0.3f, 0.4f, 0.5f, 1f));

            GL.GetInteger(GetPName.Viewport, viewport);
            Console.WriteLine(viewport[0] + ", " + viewport[1] + ", " + viewport[2] + ", " + viewport[3]);

            // Load Game Resources
            cam = new Camera(Vector3.UnitZ * 3, (float)viewport[2], (float)viewport[3]);
            skybox = new Skybox();
            plane = new Plane();
            terrain = new Terrain();
            balloon = new Balloon();

            // Find a mountain summit within the terrain interior.
            Vector3 summit = new Vector3(0f, float.MinValue, 0f);
            for (int z = -900; z <= 900; z += 16)
            for (int x = -900; x <= 900; x += 16)
            {
                float y = Terrain.GetInterpolatedHeightAtWorld(x, z);
                if (y > summit.Y) summit = new Vector3(x, y, z);
            }
            // Clear nearby slopes as the plane rotates to face the camera.
            float baseHeight = summit.Y;
            for (int z = -24; z <= 24; z += 2)
            for (int x = -24; x <= 24; x += 2)
                baseHeight = Math.Max(baseHeight, Terrain.GetInterpolatedHeightAtWorld(summit.X + x, summit.Z + z));
            summit.Y = baseHeight + 1f;
            mountainBillboard = new BillBoard(summit);
            Console.WriteLine($"Mountain billboard at {summit}");

            // Place plane safely above terrain at start
            float groundY = Terrain.GetInterpolatedHeightAtWorld(plane.position.X, plane.position.Z);
            plane.position.Y = groundY + 250f;
            flight = new FlightPhysics(flightSettings);
            initialSpawn = plane.position;
            flight.Reset(plane.position);
            plane.PhysicsOrientation = flight.Orientation;

            shadowSettings = new ShadowSettings();
            ambientLight = new AmbientLight(shadowSettings);
            planeMaterial = new Material(shadowSettings);
            bloom = new Bloom(shadowSettings);

            // Directional Light & Shadow Settings / Shadow Map
            sunLight = new DirectionalLight(
                direction: -Skybox.SunDirection,
                ambient: Vector3.Zero,
                diffuse: new Vector3(0.95f, 0.92f, 0.85f),
                specular: new Vector3(0.25f, 0.25f, 0.25f)
            );
            shadowMap = new ShadowMap(shadowSettings);

            // Start a New Game
            SetupNewGame();
            audio = new GameAudio(audioSettings);
            particleRenderer = new ParticleRenderer();
            combatRenderer = new CombatRenderer();
            cockpitHud = new CockpitHud();

            base.OnLoad();
        }

        private void SetupNewGame()
        {
            fireworks.Clear();
            combat.Reset();
            plane?.ResetOrdnance();
            triggerGuns = requestMissile = requestBomb = false;
            balloons_left = balloons_total;
            planeMaterial.VictoryGold = false;
            audio?.SetMusicTrack(MusicTrack.InGame);
            m_balloons = new List<BalloonState>();
            for (int i = 0; i < balloons_left; i++)
            {
                BalloonState newballoon = new BalloonState();
                newballoon.Spawn();
                m_balloons.Add(newballoon);
            }
        }

        protected override void OnUnload()
        {
            cockpitHud?.Dispose();
            combatRenderer?.Dispose();
            particleRenderer?.Dispose();
            audio?.Dispose();
            timer.Stop();
            mountainBillboard?.Dispose();
            gpuTimings.Dispose();
            bloom?.Dispose();
            shadowMap?.Dispose();
            base.OnUnload();
        }

        protected override void OnMouseWheel(MouseWheelEventArgs e)
        {
            base.OnMouseWheel(e);
            if (!IsFocused || flight is null || crashed) return;
            flight.Throttle = Math.Clamp(flight.Throttle + e.OffsetY * flightSettings.ThrottleWheelStep, 0f, 1f);
        }

        protected override void OnResize(ResizeEventArgs e)
        {
            GL.Viewport(0, 0, e.Width, e.Height);
            viewport[2] = e.Width;
            viewport[3] = e.Height;
            base.OnResize(e);
        }

        protected override void OnUpdateFrame(FrameEventArgs args)
        {
            totalTime += (float)args.Time;
            frameCount++;

            if (totalTime >= 1f)
            {
                averageFPS = frameCount / totalTime;
                frameCount = 0;
                totalTime = 0f;
                currentFPS = 1 / args.Time;
                Title = crashed ? "Crashed! Restarting..." : $"GPR2 Balloon Hunt!         {(balloons_left == 0 ? "VICTORY!" : "Remaining: " + balloons_left)}    {(flightSettings.AssistedControls ? "ASSISTED" : "UNASSISTED")}    Airspeed: {flight.Airspeed:0} m/s    AoA: {MathHelper.RadiansToDegrees(flight.AngleOfAttack):0}    {(flight.IsStalled ? "STALL" : "")}    Throttle: {flight.Throttle:P0}    Current FPS: {currentFPS:0}     Average FPS: {averageFPS:0}";
            }

            base.OnUpdateFrame(args);

            audio?.Update(flight.EnginePower, IsFocused, crashed ? 0f : flight.StallWarning, !crashed);
            if (!IsFocused)
            {
                return;
            }

            float deltatime = (float)args.Time;


            // ----- Input (variable timestep) -----
            if (crashed) { if (KeyboardState.IsKeyDown(Keys.Escape)) Close(); }
            else HandleInput(deltatime);

            // ----- Fixed timestep – only movement & balloons -----
            _accumulator += Math.Min(deltatime, 0.1f);

            while (_accumulator >= FixedDeltaTime)
            {
                if (crashed)
                {
                    explosion.Update(FixedDeltaTime);
                    StepCombat(FixedDeltaTime, allowFiring: false);
                    crashTime += FixedDeltaTime;
                    _accumulator -= FixedDeltaTime;
                    if (crashTime >= 3f) RestartAfterCrash();
                    continue;
                }
                flight.Step(FixedDeltaTime, pitchInput, rollInput, yawInput);
                if (flight.Position.Y < Terrain.GetInterpolatedHeightAtWorld(flight.Position.X, flight.Position.Z) + 2f)
                {
                    fireworks.Clear();
                    crashed = true;
                    crashTime = 0f;
                    plane.position = flight.Position;
                    terrain.AddScorchMark(plane.position);
                    explosion.Explode(plane.position + Vector3.UnitY*2f);
                    audio?.Update(0f, true, 0f, false);
                    audio?.SetMusicTrack(MusicTrack.Death);
                    audio?.PlayExplosion();
                    Title = "Crashed! Restarting...";
                    _accumulator -= FixedDeltaTime;
                    continue;
                }
                plane.position = flight.Position;
                plane.PhysicsOrientation = flight.Orientation;

                StepCombat(FixedDeltaTime, allowFiring: true);

                for (int i = m_balloons.Count - 1; i >= 0; i--)
                {
                    bool wasCollected = m_balloons[i].collected;

                    if (m_balloons[i].Update(plane.position, FixedDeltaTime))
                    {
                        m_balloons.RemoveAt(i);
                    }
                    else if (m_balloons[i].collected != wasCollected)
                    {
                        OnBalloonCollected();
                    }
                }

                fireworks.Update(FixedDeltaTime);
                plane.UpdatePropeller(FixedDeltaTime, shadowSettings.PropellerRpm * flight.PropellerSpeedFraction);
                _accumulator -= FixedDeltaTime;
            }
        }

        private void OnBalloonCollected()
        {
            balloons_left = Math.Max(0, balloons_left - 1);
            audio?.PlayBalloonPop();
            if (balloons_left == 0)
            {
                planeMaterial.VictoryGold = true;
                fireworks.Start(Terrain.width, Terrain.length, Terrain.GetInterpolatedHeightAtWorld);
                audio?.SetVictory(true);
            }
            Console.WriteLine("Balloon Collected! " + balloons_left + " remaining.");
        }

        private Vector3 ComputeCrosshairAimPoint()
        {
            Vector3 rayOrigin = cameraReady ? smoothCamera : plane.position - flight.Forward * Math.Max(1f, flightSettings.CameraDistance) + Vector3.UnitY * 4.5f;
            Vector3 rayTarget = cameraReady ? smoothTarget : plane.position + flight.Forward * 8f;
            Vector3 rayDir = rayTarget - rayOrigin;
            rayDir = rayDir.LengthSquared > 1e-6f ? Vector3.Normalize(rayDir) : flight.Forward;

            // Default convergence point 260m along the center-screen crosshair ray.
            float bestDist = 260f;

            // Snap convergence distance to any uncollected balloon sitting inside the crosshair cone.
            for (int i = 0; i < m_balloons.Count; i++)
            {
                if (m_balloons[i].collected) continue;
                Vector3 toBalloon = m_balloons[i].Center - rayOrigin;
                float proj = Vector3.Dot(toBalloon, rayDir);
                if (proj < 25f || proj > 600f) continue;
                Vector3 closest = rayOrigin + rayDir * proj;
                if (Vector3.DistanceSquared(closest, m_balloons[i].Center) <= 18f * 18f && proj < bestDist)
                {
                    bestDist = proj;
                }
            }

            // Also converge onto terrain if the crosshair aims into a hillside closer than bestDist.
            for (float d = 40f; d < bestDist; d += 20f)
            {
                Vector3 p = rayOrigin + rayDir * d;
                if (p.Y <= Terrain.GetInterpolatedHeightAtWorld(p.X, p.Z))
                {
                    bestDist = d;
                    break;
                }
            }

            return rayOrigin + rayDir * bestDist;
        }

        private void StepCombat(float dt, bool allowFiring)
        {
            if (allowFiring)
            {
                Matrix4 planeModel = plane.ModelMatrix;
                Vector3 aimPoint = ComputeCrosshairAimPoint();

                if (triggerGuns && combat.TryFireGuns(planeModel, flight.Velocity, aimPoint))
                {
                    audio?.PlayGunShot();
                }

                if (requestMissile)
                {
                    requestMissile = false;
                    int slot = combat.TryFireMissile(planeModel, flight.Orientation, flight.Velocity, aimPoint, i => plane.GetMissileSlot(i).LocalCenter);
                    if (slot >= 0)
                    {
                        plane.SetMissileMounted(slot, false);
                        audio?.PlayMissileLaunch();
                    }
                }

                if (requestBomb)
                {
                    requestBomb = false;
                    int slot = combat.TryDropBomb(planeModel, flight.Orientation, flight.Velocity, i => plane.GetBombSlot(i).LocalCenter);
                    if (slot >= 0)
                    {
                        plane.SetBombMounted(slot, false);
                    }
                }
            }

            combatTargets.Clear();
            for (int i = 0; i < m_balloons.Count; i++)
            {
                if (!m_balloons[i].collected)
                {
                    combatTargets.Add(new TargetSphere { Index = i, Center = m_balloons[i].Center, Radius = 12.0f });
                }
            }

            combatObstacles.Clear();
            if (mountainBillboard != null)
            {
                combatObstacles.Add(new ObstacleBox
                {
                    Center = mountainBillboard.Position + new Vector3(0f, mountainBillboard.Size.Y * 0.5f, 0f),
                    HalfExtents = new Vector3(mountainBillboard.Size.X * 0.5f, mountainBillboard.Size.Y * 0.5f, 12f)
                });
            }

            combatHits.Clear();
            combatExplosions.Clear();
            combat.Update(dt, Terrain.GetInterpolatedHeightAtWorld, combatTargets, combatObstacles, combatHits, combatExplosions);

            for (int i = 0; i < combatHits.Count; i++)
            {
                int bIdx = combatHits[i];
                if (bIdx >= 0 && bIdx < m_balloons.Count && m_balloons[bIdx].Pop())
                {
                    OnBalloonCollected();
                }
            }

            for (int i = 0; i < combatExplosions.Count; i++)
            {
                var exp = combatExplosions[i];
                audio?.PlayExplosion();
                if (exp.HitGround)
                {
                    terrain.AddScorchMark(exp.Position, exp.ScorchRadius);
                }
            }
        }
        private void RestartAfterCrash()
        {
            crashed = false;
            crashTime = 0f;
            explosion.Clear();
            flight.Reset(initialSpawn);
            plane.position = flight.Position;
            plane.PhysicsOrientation = flight.Orientation;
            pitchInput = rollInput = yawInput = 0f;
            virtualStick = Vector2.Zero;
            ignoreNextMouseDelta = true;
            _accumulator = 0f;
            cameraReady = false;
            SetupNewGame();
        }

        private void HandleInput(float deltaT)
        {
            if (KeyboardState.IsKeyDown(Keys.Escape)) Close();
            if (KeyboardState.IsKeyPressed(Keys.Tab)) SetMouseLock(!mouseLocked);
            if (KeyboardState.IsKeyPressed(Keys.F2)) flightSettings.AssistedControls = !flightSettings.AssistedControls;
            if (KeyboardState.IsKeyPressed(Keys.H)) shadowSettings.CockpitHudEnabled = !shadowSettings.CockpitHudEnabled;

            // Cursor displacement is a virtual stick; pitch and rudder are inverted below.
            if (CursorState == CursorState.Grabbed)
            {
                if (ignoreNextMouseDelta)
                {
                    ignoreNextMouseDelta = false;
                }
                else
                {
                    float halfW = Math.Max(1f, ClientSize.X * 0.35f);
                    float halfH = Math.Max(1f, ClientSize.Y * 0.35f);
                    virtualStick.X = Math.Clamp(virtualStick.X + MouseState.Delta.X / halfW, -1f, 1f);
                    virtualStick.Y = Math.Clamp(virtualStick.Y - MouseState.Delta.Y / halfH, -1f, 1f);
                }
                rollInput = virtualStick.X;
                pitchInput = virtualStick.Y;
            }
            else
            {
                pitchInput = Math.Clamp(1f - 2f * MouseState.Y / Math.Max(1, ClientSize.Y), -1f, 1f);
                rollInput = Math.Clamp(2f * MouseState.X / Math.Max(1, ClientSize.X) - 1f, -1f, 1f);
                virtualStick = new Vector2(rollInput, pitchInput);
            }
            if (MathF.Abs(pitchInput) < 0.05f) pitchInput = 0f;
            if (MathF.Abs(rollInput) < 0.05f) rollInput = 0f;
            if (KeyboardState.IsKeyDown(Keys.Up)) pitchInput = 1f;
            if (KeyboardState.IsKeyDown(Keys.Down)) pitchInput = -1f;
            if (KeyboardState.IsKeyDown(Keys.Left)) rollInput = -1f;
            if (KeyboardState.IsKeyDown(Keys.Right)) rollInput = 1f;
            yawInput = (KeyboardState.IsKeyDown(Keys.D) ? 1f : 0f) - (KeyboardState.IsKeyDown(Keys.A) ? 1f : 0f);
            pitchInput = -pitchInput;
            yawInput = -yawInput;
            if (flightSettings.AssistedControls)
                pitchInput = 0.35f*pitchInput + 0.65f*pitchInput*pitchInput*pitchInput;
            flight.Throttle = Math.Clamp(flight.Throttle + Math.Min(deltaT, 0.1f) * 0.4f
                * ((KeyboardState.IsKeyDown(Keys.W) ? 1f : 0f) - (KeyboardState.IsKeyDown(Keys.S) ? 1f : 0f)), 0f, 1f);

            // Combat controls: LMB/Space = wing machine guns, RMB/Q/F = missiles, MMB/B/E = large bombs.
            triggerGuns = MouseState.IsButtonDown(MouseButton.Left) || KeyboardState.IsKeyDown(Keys.Space);
            if (MouseState.IsButtonPressed(MouseButton.Right) || KeyboardState.IsKeyPressed(Keys.Q) || KeyboardState.IsKeyPressed(Keys.F))
                requestMissile = true;
            if (MouseState.IsButtonPressed(MouseButton.Middle) || KeyboardState.IsKeyPressed(Keys.B) || KeyboardState.IsKeyPressed(Keys.E))
                requestBomb = true;
        }

        protected override void OnRenderFrame(FrameEventArgs args)
        {
            if (FramebufferSize.X <= 0 || FramebufferSize.Y <= 0) return;

            // Follow heading and pitch without inheriting aircraft bank.
            Vector3 camWorldPos = plane.position - flight.Forward * Math.Max(1f, flightSettings.CameraDistance) + Vector3.UnitY * 4.5f;
            Vector3 cameraTarget = plane.position + flight.Forward * 8f;
            if (crashed) { camWorldPos = plane.position + new Vector3(0f, 18f, 30f); cameraTarget = plane.position + Vector3.UnitY*4f; }
            float cameraBlend = 1f - MathF.Exp(-flightSettings.CameraFollowSpeed * (float)Math.Min(args.Time, 0.1));
            if (!cameraReady) { smoothCamera = camWorldPos; smoothTarget = cameraTarget; cameraReady = true; }
            smoothCamera = Vector3.Lerp(smoothCamera, camWorldPos, cameraBlend);
            smoothTarget = Vector3.Lerp(smoothTarget, cameraTarget, cameraBlend);
            camWorldPos = smoothCamera;
            cameraTarget = smoothTarget;
            cameraTime += args.Time;
            float shake = (crashed ? 0f : flight.StallWarning) * flightSettings.StallShakeAmount;
            camWorldPos += new Vector3((float)Math.Sin(cameraTime*73), (float)Math.Sin(cameraTime*91), 0f) * shake;
            Vector3 viewDirection = Vector3.Normalize(cameraTarget - camWorldPos);
            // Avoid a degenerate LookAt when looking almost straight up or down.
            Vector3 cameraUp = MathF.Abs(Vector3.Dot(viewDirection, Vector3.UnitY)) > 0.999f
                ? Vector3.UnitZ : Vector3.UnitY;
            Matrix4 view = Matrix4.LookAt(camWorldPos, cameraTarget, cameraUp);
            cam.AspectRatio = (float)FramebufferSize.X / FramebufferSize.Y;
            Matrix4 proj = cam.GetProjectionMatrix();

            gpuTimings.Collect(shadowSettings.GpuTimingEnabled);
            terrain.UpdateLod(camWorldPos, shadowSettings);

            // --- PASS 1: Shadow Depth Pass (centered around plane) ---
            gpuTimings.Begin("Shadow depth", shadowSettings.GpuTimingEnabled);
            shadowMap.BeginPass(sunLight, plane.GetPosition());

            terrain.DrawDepth(shadowMap);
            if (!crashed) plane.DrawDepth(shadowMap);
            for (int i = 0; i < combat.ActiveOrdnance.Count; i++)
            {
                var ord = combat.ActiveOrdnance[i];
                plane.DrawOrdnanceDepth(shadowMap, ord.Kind == OrdnanceKind.Bomb, ord.SlotIndex, ord.Position, ord.Orientation);
            }

            shadowMap.EndPass(viewport[2], viewport[3]);
            gpuTimings.End();

            // --- PASS 2: HDR scene for bloom ---
            gpuTimings.Begin("Scene setup + sky", shadowSettings.GpuTimingEnabled);
            bloom.BeginScene(FramebufferSize.X, FramebufferSize.Y);
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

            // Bind shadow depth texture on TextureUnit.Texture1 for lit shaders
            shadowMap.BindDepthMap(TextureUnit.Texture1);

            // Draw Skybox (unlit)
            GL.DepthFunc(DepthFunction.Lequal);
            skybox.Draw(view, proj);
            GL.DepthFunc(DepthFunction.Less);
            gpuTimings.End();

            // Draw Plane with Directional Lighting & Shadows
            gpuTimings.Begin("Plane", shadowSettings.GpuTimingEnabled);
            if (!crashed) plane.Draw(view, proj, sunLight, camWorldPos, ambientLight, planeMaterial, shadowMap);
            for (int i = 0; i < combat.ActiveOrdnance.Count; i++)
            {
                var ord = combat.ActiveOrdnance[i];
                plane.DrawOrdnance(ord.Kind == OrdnanceKind.Bomb, ord.SlotIndex, ord.Position, ord.Orientation, view, proj, sunLight, camWorldPos, ambientLight, planeMaterial, shadowMap);
            }
            gpuTimings.End();

            gpuTimings.Begin("Balloons", shadowSettings.GpuTimingEnabled);
            // Draw Balloons
            for (int i = 0; i < m_balloons.Count; i++)
            {
                balloon.Draw(m_balloons[i].position, m_balloons[i].rotation, m_balloons[i].scale, view, proj, shadowSettings);
            }

            gpuTimings.End();

            // Draw Terrain with Directional Lighting & Shadows
            gpuTimings.Begin("Terrain + billboard", shadowSettings.GpuTimingEnabled);
            terrain.Draw(view, proj, sunLight, camWorldPos, ambientLight, shadowMap);

            mountainBillboard.Draw(view, proj, camWorldPos);
            gpuTimings.End();

            combatRenderer?.Draw(combat, view, proj, camWorldPos);
            particleRenderer?.Draw(combat.ExhaustTrail, view, proj, FramebufferSize.Y, new Vector3(1.1f, 0.95f, 0.75f));
            for (int i = 0; i < combat.ExplosionBursts.Count; i++)
                particleRenderer?.Draw(combat.ExplosionBursts[i], view, proj, FramebufferSize.Y);
            particleRenderer?.Draw(explosion, view, proj, FramebufferSize.Y);
            for (int i=0;i<fireworks.Bursts.Length;i++)
                particleRenderer?.Draw(fireworks.Bursts[i], view, proj, FramebufferSize.Y, fireworks.Colors[i]);

            gpuTimings.Begin("Bloom", shadowSettings.GpuTimingEnabled);
            bloom.EndScene();
            gpuTimings.End();

            if (cockpitHud != null)
            {
                float groundHeight = Terrain.GetInterpolatedHeightAtWorld(flight.Position.X, flight.Position.Z);
                var telemetry = CockpitTelemetry.FromFlight(flight, groundHeight, Terrain.OffsetY, crashed, (float)cameraTime, combat, virtualStick);
                cockpitHud.Draw(telemetry, FramebufferSize.X, FramebufferSize.Y, showInstruments: shadowSettings.CockpitHudEnabled, showCrosshair: !crashed);
            }

            this.Context.SwapBuffers();
            base.OnRenderFrame(args);
        }
    }
}
