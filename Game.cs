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
        private int[] viewport = new int[4];

        Camera cam;
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
        private BillBoard mountainBillboard = null!;

        public const int balloons_total = 8;
        public int balloons_left = balloons_total;
        public List<BalloonState> m_balloons;

        float totalTime = 0f;
        int frameCount = 0;
        float averageFPS = 0f;
        double currentFPS = 0f;

        private Vector2 _smoothedLook = Vector2.Zero;
        private const float LookSmoothing = 0.515f;

        // Fixed timestep
        private float _accumulator = 0f;
        private const float FixedDeltaTime = 1f / 60f;

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
            plane.position.Y = groundY + 40f;

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
            timer.Stop();
            mountainBillboard?.Dispose();
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
                Title = $"GPR2 Balloon Hunt!         Remaining: {balloons_left}    Current FPS: {currentFPS:0}     Average FPS: {averageFPS:0}";
            }

            base.OnUpdateFrame(args);

            if (!IsFocused)
            {
                return;
            }

            float deltatime = (float)args.Time;

            // ----- Input (variable timestep) -----
            HandleInput(deltatime);

            float rotY = MathHelper.DegreesToRadians(180.0f + cam.Yaw);
            plane.SetRotation(new Vector3(0f, rotY, 0f));
            plane.UpdateVertices();

            // ----- Fixed timestep – only movement & balloons -----
            _accumulator += deltatime;

            while (_accumulator >= FixedDeltaTime)
            {
                plane.position -= plane.Right * plane.FlySpeed * FixedDeltaTime;
                plane.OnFrameUpdate(this.MouseState, viewport[2], viewport[3], FixedDeltaTime);

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
                        Console.WriteLine("Balloon Collected! " + balloons_left + " remaining.");
                    }
                }

                _accumulator -= FixedDeltaTime;
            }
        }
        private bool firstMove = true;
        private Vector2 lastPos;
        private void HandleInput(float deltaT)
        {
            // Keyboard
            KeyboardState keyboard = KeyboardState;
            if (keyboard.IsKeyDown(Keys.Escape))
                Close();

            // Mouse
            MouseState mouse = this.MouseState;
            if (firstMove) // this bool variable is initially set to true
            {
                lastPos = new Vector2(mouse.X, mouse.Y);
                firstMove = false;
            }
            else
            {
                float TurnFactor = ((1.0f / viewport[2]) * mouse.X);
                float TurnFactorN;
                float midPoint = 0.5f;
                if (TurnFactor > 0.5f)
                {
                    TurnFactor = 1 - TurnFactor;
                    TurnFactorN = (1 - ((TurnFactor - ((1 - TurnFactor) * TurnFactor)) * (1 / (1 - midPoint))));
                }
                else
                {
                    TurnFactorN = (TurnFactor - ((1 - TurnFactor) * TurnFactor)) * (1 / midPoint);
                }

                float deltaX = ((TurnFactorN - 0.5f) * 2f) * 0.1f;

                //Apply yaw rotation to camera and plane together
                cam.Yaw -= deltaX * 1f * plane.TurnSpeed * deltaT;
                plane.Yaw -= deltaX * -1f * plane.TurnSpeed * deltaT;
            }
        }

        protected override void OnRenderFrame(FrameEventArgs args)
        {
            if (FramebufferSize.X <= 0 || FramebufferSize.Y <= 0) return;

            // Compute camera position from plane follow offset
            Vector3 cam_pos = new Vector3(0f, 0f, 0f);
            Vector3 cam_dir = new Vector3(cam.Yaw, 20.0f, 0.0f);
            float distance = 11.0f;
            cam_pos.X = (float)(distance * -Math.Sin(cam_dir.X * (Math.PI / 180)) * Math.Cos((cam_dir.Y) * (Math.PI / 180)));
            cam_pos.Y = (float)(distance * -Math.Sin((cam_dir.Y) * (Math.PI / 180)));
            cam_pos.Z = (float)(-distance * Math.Cos((cam_dir.X) * (Math.PI / 180)) * Math.Cos((cam_dir.Y) * (Math.PI / 180)));

            Vector3 camWorldPos = plane.GetPosition() - cam_pos;
            Matrix4 view = Matrix4.LookAt(camWorldPos, plane.GetPosition(), new Vector3(0f, 1f, 0f));
            Matrix4 proj = cam.GetProjectionMatrix();

            terrain.UpdateLod(camWorldPos, shadowSettings);

            // --- PASS 1: Shadow Depth Pass (centered around plane) ---
            shadowMap.BeginPass(sunLight, plane.GetPosition());

            terrain.DrawDepth(shadowMap);
            plane.DrawDepth(shadowMap);

            shadowMap.EndPass(viewport[2], viewport[3]);

            // --- PASS 2: HDR scene for bloom ---
            bloom.BeginScene(FramebufferSize.X, FramebufferSize.Y);
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

            // Bind shadow depth texture on TextureUnit.Texture1 for lit shaders
            shadowMap.BindDepthMap(TextureUnit.Texture1);

            // Draw Skybox (unlit)
            GL.DepthFunc(DepthFunction.Lequal);
            skybox.Draw(view, proj);
            GL.DepthFunc(DepthFunction.Less);

            // Draw Plane with Directional Lighting & Shadows
            plane.Draw(view, proj, sunLight, camWorldPos, ambientLight, planeMaterial, shadowMap);

            // Draw Balloons
            for (int i = 0; i < m_balloons.Count; i++)
            {
                balloon.Draw(m_balloons[i].position, m_balloons[i].rotation, m_balloons[i].scale, view, proj, shadowSettings);
            }

            // Draw Terrain with Directional Lighting & Shadows
            terrain.Draw(view, proj, sunLight, camWorldPos, ambientLight, shadowMap);

            mountainBillboard.Draw(view, proj, camWorldPos);

            bloom.EndScene();
            this.Context.SwapBuffers();
            base.OnRenderFrame(args);
        }
    }
}
