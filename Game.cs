using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OpenTK.Graphics.OpenGL;
using OpenTK;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using System.Diagnostics;
using System.Reflection;
using StbImageSharp;
using System.Reflection.Metadata;
using System.IO;
using OpenTK.Windowing.GraphicsLibraryFramework;
using OpenTK.Input;
using static System.Net.Mime.MediaTypeNames;
using OpenTK.Windowing.Common.Input;
using System.Text.RegularExpressions;
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

        public const int balloons_total = 8;
        public int balloons_left = balloons_total;
        public List<BalloonState> m_balloons;

        float totalTime = 0f;
        int frameCount = 0;
        float averageFPS = 0f;
        double currentFPS = 0f;

        private Vector2 _smoothedLook = Vector2.Zero;
        private const float LookSmoothing = 0.515f;   // 0 = raw, 1 = very heavy // 0.015f

        // Fixed timestep
        private float _accumulator = 0f;
        private const float FixedDeltaTime = 1f / 60f;   // 60 Hz physics
        private float smoothedDeltaTime = 0.016f;

        public Game() : base(GameWindowSettings.Default, NativeWindowSettings.Default)
        {
            this.CenterWindow(new Vector2i(1280, 720));
            //this.VSync = VSyncMode.On;
            CursorState = CursorState.Grabbed;
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
        // Setup Sunlight
            sunLight = new DirectionalLight(
            direction: new Vector3(-0.95f, -0.20f, -0.24f),  //-0.45f, -1.0f, -0.35f
            ambient: new Vector3(0.25f, 0.25f, 0.28f),               // 0.25f, 0.25f, 0.28f
            diffuse: new Vector3(0.95f, 0.92f, 0.85f),               // 0.95f, 0.92f, 0.85f
            specular: new Vector3(0.5f, 0.5f, 0.5f)         // 0.2f, 0.2f, 0.2f
);

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
                //Console.WriteLine("Balloon spawned at: " + newballoon.position.ToString());
            }
        }

        protected override void OnUnload()
        {
            timer.Stop();

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
                //Console.WriteLine("Current_FPS " + currentFPS);
                //Console.WriteLine("Plane Y " + plane.position.Y);
            }

            Title = $"GPR2 Balloon Hunt!         Remaining: {balloons_left}    Current FPS: {currentFPS:0}     Average FPS: {averageFPS:0}";

            base.OnUpdateFrame(args);

            if (!IsFocused) // check to see if the window is focused
            {
                return;
            }

            float deltatime = (float)args.Time;

            // ----- Input (variable timestep) -----
            HandleInput(deltatime);

            // Apply rotation every frame so the plane follows the camera more tightly
            // (this softens the lag while still looking slightly delayed)
            float rotY = MathHelper.DegreesToRadians(180.0f + cam.Yaw);
            plane.SetRotation(new Vector3(0f, rotY, 0f));
            plane.UpdateVertices();

            // ----- Fixed timestep – only movement & balloons -----
            _accumulator += deltatime;

            while (_accumulator >= FixedDeltaTime)
            {
                // Constant-speed movement
                plane.position -= plane.Right * plane.FlySpeed * FixedDeltaTime;

                // Lift
                plane.OnFrameUpdate(this.MouseState, viewport[2], viewport[3], FixedDeltaTime);

                // Balloons
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
            if (KeyboardState.IsKeyDown(Keys.Escape))
                Close();

            if (!IsFocused)
                return;

            var mouse = MouseState;

            // Relative movement this frame
            Vector2 rawDelta = new Vector2(mouse.Delta.X, mouse.Delta.Y);

            // Light exponential smoothing (removes micro-stutters)
            _smoothedLook = Vector2.Lerp(_smoothedLook, rawDelta, 1f - MathF.Pow(LookSmoothing, deltaT * 60f));

            float yawDelta = _smoothedLook.X * plane.TurnSpeed * 0.09f * deltaT;
            float pitchDelta = _smoothedLook.Y * plane.LiftSpeed * 0.5f * deltaT;   // optional

            cam.Yaw -= yawDelta;
            plane.Yaw += yawDelta;          // keep plane and camera in sync

            // Optional vertical look / lift
            // plane.Pitch = MathHelper.Clamp(plane.Pitch - pitchDelta, -30f, 30f);
        }

        protected override void OnMouseMove(MouseMoveEventArgs e)
        {
            base.OnMouseMove(e);
        }

        protected override void OnRenderFrame(FrameEventArgs args)
        {
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

            // Fixed camera position looking at the plane from a static vantage point
            //Vector3 fixedCamPos = new Vector3(0f, 150f, 300f);
            //Console.WriteLine("plane.GetPosition() " + plane.GetPosition());
            //Matrix4 view = Matrix4.LookAt(fixedCamPos, new Vector3(-1.1493115f, -0.690963f, 0.20048559f), new Vector3(0f, 1f, 0f));

            // update the uniform color
            double timeValue = timer.Elapsed.TotalSeconds;

            // Setup camera position and rotation
            Vector3 cam_pos = new Vector3(0f, 0f, 0f);
            Vector3 cam_dir = new Vector3(cam.Yaw, 20.0f, 0.0f);
            float distance = 11.0f;
            cam_pos.X = (float)(distance * -Math.Sin(cam_dir.X * (Math.PI / 180)) * Math.Cos((cam_dir.Y) * (Math.PI / 180)));
            cam_pos.Y = (float)(distance * -Math.Sin((cam_dir.Y) * (Math.PI / 180)));
            cam_pos.Z = (float)(-distance * Math.Cos((cam_dir.X) * (Math.PI / 180)) * Math.Cos((cam_dir.Y) * (Math.PI / 180)));
            Matrix4 view = Matrix4.LookAt(plane.GetPosition() - cam_pos, plane.GetPosition(), new Vector3(0f, 1f, 0f));

            Matrix4 proj = cam.GetProjectionMatrix();

            Vector3 camWorldPos = plane.GetPosition() - cam_pos;

            // Draw Skybox
            GL.DepthFunc(DepthFunction.Lequal);
            skybox.Draw(view, proj);
            GL.DepthFunc(DepthFunction.Less);

            // Draw Plane
            plane.Draw(view, proj);

            // Draw Balloons
            for (int i = 0; i < m_balloons.Count; i++)
            {
                balloon.Draw(m_balloons[i].position, m_balloons[i].rotation, m_balloons[i].scale, view, proj);
            }

            // Draw Terrain with Frustum Culling
            terrain.Draw(view, proj, sunLight, camWorldPos);

            this.Context.SwapBuffers();
            base.OnRenderFrame(args);
        }
    }
}
