using System;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;

namespace GPR202_Assessment3
{
    // Renders crisp camera-facing 3D billboard health bars above enemy aircraft.
    public sealed class HealthBarRenderer : IDisposable
    {
        private int program;
        private int vao;
        private bool disposed;

        private readonly int viewLocation;
        private readonly int projectionLocation;
        private readonly int anchorLocation;
        private readonly int camRightLocation;
        private readonly int camUpLocation;
        private readonly int sizeLocation;
        private readonly int healthLocation;

        public HealthBarRenderer()
        {
            int vs = 0, fs = 0;
            try
            {
                vs = Compile(ShaderType.VertexShader, @"#version 330 core
                    uniform mat4 view;
                    uniform mat4 projection;
                    uniform vec3 uAnchor;
                    uniform vec3 uCamRight;
                    uniform vec3 uCamUp;
                    uniform vec2 uSize;

                    out vec2 vUv;

                    void main()
                    {
                        const vec2 corners[6] = vec2[6](
                            vec2(0.0, 0.0), vec2(1.0, 0.0), vec2(1.0, 1.0),
                            vec2(0.0, 0.0), vec2(1.0, 1.0), vec2(0.0, 1.0)
                        );
                        vUv = corners[gl_VertexID];
                        vec3 worldPos = uAnchor
                            + uCamRight * ((vUv.x - 0.5) * uSize.x)
                            + uCamUp * ((vUv.y - 0.5) * uSize.y);
                        gl_Position = vec4(worldPos, 1.0) * view * projection;
                    }");

                fs = Compile(ShaderType.FragmentShader, @"#version 330 core
                    in vec2 vUv;
                    out vec4 fragColor;

                    uniform float uHealth;

                    void main()
                    {
                        float h = clamp(uHealth, 0.0, 1.0);
                        vec2 uv = vUv;

                        // Subtle dark frame border
                        float borderX = 0.025;
                        float borderY = 0.14;
                        bool inInner = uv.x >= borderX && uv.x <= (1.0 - borderX)
                                    && uv.y >= borderY && uv.y <= (1.0 - borderY);

                        vec3 frameCol = vec3(0.08, 0.09, 0.11);
                        if (!inInner)
                        {
                            fragColor = vec4(frameCol, 0.88);
                            return;
                        }

                        float innerX = (uv.x - borderX) / (1.0 - 2.0 * borderX);
                        float innerY = (uv.y - borderY) / (1.0 - 2.0 * borderY);

                        vec3 trackCol = vec3(0.14, 0.10, 0.10);
                        vec3 fillCol = h > 0.55
                            ? mix(vec3(0.95, 0.75, 0.14), vec3(0.18, 0.86, 0.34), (h - 0.55) / 0.45)
                            : mix(vec3(0.95, 0.18, 0.14), vec3(0.95, 0.75, 0.14), h / 0.55);

                        // Gentle top highlight along the bar
                        fillCol *= mix(0.82, 1.15, innerY);

                        vec3 col = innerX <= h ? fillCol : trackCol;
                        fragColor = vec4(col, 0.92);
                    }");

                program = GL.CreateProgram();
                GL.AttachShader(program, vs);
                GL.AttachShader(program, fs);
                GL.LinkProgram(program);
                GL.GetProgram(program, GetProgramParameterName.LinkStatus, out int ok);
                if (ok == 0) throw new InvalidOperationException("HealthBarRenderer link error: " + GL.GetProgramInfoLog(program));
                GL.DetachShader(program, vs);
                GL.DetachShader(program, fs);

                vao = GL.GenVertexArray();

                viewLocation = GL.GetUniformLocation(program, "view");
                projectionLocation = GL.GetUniformLocation(program, "projection");
                anchorLocation = GL.GetUniformLocation(program, "uAnchor");
                camRightLocation = GL.GetUniformLocation(program, "uCamRight");
                camUpLocation = GL.GetUniformLocation(program, "uCamUp");
                sizeLocation = GL.GetUniformLocation(program, "uSize");
                healthLocation = GL.GetUniformLocation(program, "uHealth");
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
            }
        }

        private static int Compile(ShaderType type, string source)
        {
            int s = GL.CreateShader(type);
            GL.ShaderSource(s, source);
            GL.CompileShader(s);
            GL.GetShader(s, ShaderParameter.CompileStatus, out int ok);
            if (ok != 0) return s;
            string log = GL.GetShaderInfoLog(s);
            GL.DeleteShader(s);
            throw new InvalidOperationException($"HealthBarRenderer {type}: {log}");
        }

        public void DrawEnemyHealthBars(GameManager gameManager, Matrix4 view, Matrix4 projection, Vector3 cameraPos)
        {
            if (disposed || gameManager == null) return;
            var enemies = gameManager.EnemySpawner.Enemies;
            if (enemies.Count == 0) return;

            // Extract camera right/up basis vectors directly from row-major view matrix columns.
            Vector3 camRight = new Vector3(view.M11, view.M21, view.M31);
            Vector3 camUp = new Vector3(view.M12, view.M22, view.M32);
            if (camRight.LengthSquared > 1e-6f) camRight.Normalize(); else camRight = Vector3.UnitX;
            if (camUp.LengthSquared > 1e-6f) camUp.Normalize(); else camUp = Vector3.UnitY;

            bool blend = GL.IsEnabled(EnableCap.Blend);
            bool cull = GL.IsEnabled(EnableCap.CullFace);
            GL.GetBoolean(GetPName.DepthWritemask, out bool depthWrite);
            GL.GetInteger(GetPName.BlendSrcRgb, out int srcRgb);
            GL.GetInteger(GetPName.BlendDstRgb, out int dstRgb);
            GL.GetInteger(GetPName.BlendSrcAlpha, out int srcAlpha);
            GL.GetInteger(GetPName.BlendDstAlpha, out int dstAlpha);

            GL.Enable(EnableCap.Blend);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
            GL.Disable(EnableCap.CullFace);
            GL.DepthMask(false);

            try
            {
                GL.UseProgram(program);
                GL.UniformMatrix4(viewLocation, true, ref view);
                GL.UniformMatrix4(projectionLocation, true, ref projection);
                GL.Uniform3(camRightLocation, camRight);
                GL.Uniform3(camUpLocation, camUp);
                GL.BindVertexArray(vao);

                for (int i = 0; i < enemies.Count; i++)
                {
                    var e = enemies[i];
                    if (!e.IsAlive || !e.IsSpawned) continue;

                    float dist = Vector3.Distance(cameraPos, e.Position);
                    float distScale = Math.Clamp(dist * 0.025f, 1.0f, 3.8f);
                    Vector3 anchor = e.Position + Vector3.UnitY * (6.5f + distScale * 1.4f);
                    Vector2 size = new Vector2(10.5f * distScale, 1.35f * distScale);

                    GL.Uniform3(anchorLocation, anchor);
                    GL.Uniform2(sizeLocation, size);
                    GL.Uniform1(healthLocation, e.HealthFraction);
                    GL.DrawArrays(PrimitiveType.Triangles, 0, 6);
                }
            }
            finally
            {
                GL.BindVertexArray(0);
                GL.UseProgram(0);
                GL.DepthMask(depthWrite);
                GL.BlendFuncSeparate((BlendingFactorSrc)srcRgb, (BlendingFactorDest)dstRgb, (BlendingFactorSrc)srcAlpha, (BlendingFactorDest)dstAlpha);
                if (!blend) GL.Disable(EnableCap.Blend);
                if (cull) GL.Enable(EnableCap.CullFace);
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (program != 0) GL.DeleteProgram(program);
            if (vao != 0) GL.DeleteVertexArray(vao);
            program = vao = 0;
        }
    }
}

