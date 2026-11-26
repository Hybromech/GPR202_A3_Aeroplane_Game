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
        private int[] viewport = new int[4];

        Camera cam;
        private readonly FlightSettings flightSettings = new();
        private FlightPhysics flight = null!;
        private float pitchInput, rollInput, yawInput;
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
            //this.CursorState = CursorState.Grabbed;
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
            flight.Reset(plane.position);
            plane.PhysicsOrientation = flight.Orientation;

            shadowSettings = new ShadowSettings();
            ambientLight = new AmbientLight(shadowSettings);
            planeMaterial = new Material(shadowSettings);
            bloom = new Bloom(shadowSettings);

            // Directional Light & Shadow Settings / Shadow Map
            sunLight = new DirectionalLight(
                direction: new Vector3(-0.92f, -0.24f, -0.30f),
                ambient: Vector3.Zero,
                diffuse: new Vector3(0.95f, 0.92f, 0.85f),
                specular: new Vector3(0.25f, 0.25f, 0.25f)
            );
            shadowMap = new ShadowMap(shadowSettings);

            // Start a New Game
            SetupNewGame();
            audio = new GameAudio(audioSettings);

            base.OnLoad();
        }

        private void SetupNewGame()
        {
            balloons_left = balloons_total;
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
            audio?.Dispose();
            timer.Stop();
            mountainBillboard?.Dispose();
            gpuTimings.Dispose();
            bloom?.Dispose();
            shadowMap?.Dispose();
            base.OnUnload();
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
                Title = $"GPR2 Balloon Hunt!         Remaining: {balloons_left}    Airspeed: {flight.Airspeed:0} m/s    AoA: {MathHelper.RadiansToDegrees(flight.AngleOfAttack):0}    {(flight.IsStalled ? "STALL" : "")}    Throttle: {flight.Throttle:P0}    Current FPS: {currentFPS:0}     Average FPS: {averageFPS:0}";
            }

            base.OnUpdateFrame(args);

            audio?.Update(flight.Throttle, IsFocused);
            if (!IsFocused)
            {
                return;
            }

            float deltatime = (float)args.Time;
            plane.UpdatePropeller(deltatime, shadowSettings.PropellerRpm);

            // ----- Input (variable timestep) -----
            HandleInput(deltatime);

            // ----- Fixed timestep – only movement & balloons -----
            _accumulator += Math.Min(deltatime, 0.1f);

            while (_accumulator >= FixedDeltaTime)
            {
                flight.Step(FixedDeltaTime, pitchInput, rollInput, yawInput);
                if (flight.Position.Y < Terrain.GetInterpolatedHeightAtWorld(flight.Position.X, flight.Position.Z) + 2f)
                {
                    // Simple crash/respawn rather than a ground-contact simulation.
                    Vector3 restart = flight.Position;
                    restart.Y = Terrain.GetInterpolatedHeightAtWorld(restart.X, restart.Z) + 250f;
                    flight.Reset(restart);
                }
                plane.position = flight.Position;
                plane.PhysicsOrientation = flight.Orientation;

                for (int i = m_balloons.Count - 1; i >= 0; i--)
                {
                    bool wasCollected = m_balloons[i].collected;

                    if (m_balloons[i].Update(plane.position, FixedDeltaTime))
                    {
                        m_balloons.RemoveAt(i);
                    }
                    else if (m_balloons[i].collected != wasCollected)
                    {
                        balloons_left--;
                        audio?.PlayBalloonPop();
                        Console.WriteLine("Balloon Collected! " + balloons_left + " remaining.");
                    }
                }

                _accumulator -= FixedDeltaTime;
            }
        }
        private void HandleInput(float deltaT)
        {
            if (KeyboardState.IsKeyDown(Keys.Escape)) Close();
            // Cursor displacement is a virtual stick; pitch and rudder are inverted below.
            pitchInput = Math.Clamp(1f - 2f * MouseState.Y / Math.Max(1, ClientSize.Y), -1f, 1f);
            rollInput = Math.Clamp(2f * MouseState.X / Math.Max(1, ClientSize.X) - 1f, -1f, 1f);
            if (MathF.Abs(pitchInput) < 0.05f) pitchInput = 0f;
            if (MathF.Abs(rollInput) < 0.05f) rollInput = 0f;
            if (KeyboardState.IsKeyDown(Keys.Up)) pitchInput = 1f;
            if (KeyboardState.IsKeyDown(Keys.Down)) pitchInput = -1f;
            if (KeyboardState.IsKeyDown(Keys.Left)) rollInput = -1f;
            if (KeyboardState.IsKeyDown(Keys.Right)) rollInput = 1f;
            yawInput = (KeyboardState.IsKeyDown(Keys.D) ? 1f : 0f) - (KeyboardState.IsKeyDown(Keys.A) ? 1f : 0f);
            pitchInput = -pitchInput;
            yawInput = -yawInput;
            flight.Throttle = Math.Clamp(flight.Throttle + Math.Min(deltaT, 0.1f) * 0.4f
                * ((KeyboardState.IsKeyDown(Keys.W) ? 1f : 0f) - (KeyboardState.IsKeyDown(Keys.S) ? 1f : 0f)), 0f, 1f);
        }

        protected override void OnRenderFrame(FrameEventArgs args)
        {
            if (FramebufferSize.X <= 0 || FramebufferSize.Y <= 0) return;

            // Follow heading and pitch without inheriting aircraft bank.
            Vector3 camWorldPos = plane.position - flight.Forward * 15f + Vector3.UnitY * 4.5f;
            Vector3 cameraTarget = plane.position + flight.Forward * 8f;
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
            plane.DrawDepth(shadowMap);

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
            plane.Draw(view, proj, sunLight, camWorldPos, ambientLight, planeMaterial, shadowMap);
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

            gpuTimings.Begin("Bloom", shadowSettings.GpuTimingEnabled);
            bloom.EndScene();
            gpuTimings.End();
            this.Context.SwapBuffers();
            base.OnRenderFrame(args);
        }
    }
}
