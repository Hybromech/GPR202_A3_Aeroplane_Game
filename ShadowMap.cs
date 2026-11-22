using System;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;

namespace GPR202_Assessment3
{
    public class ShadowMap : IDisposable
    {
        public int Width { get; }
        public int Height { get; }
        public int FboHandle { get; private set; }
        public int DepthTextureHandle { get; private set; }
        public int DepthShaderProgram { get; private set; }
        public Matrix4 LightSpaceMatrix { get; private set; }

        private int _modelLoc;
        private int _lightSpaceLoc;
        private bool _disposed;

        public ShadowMap(int width = 2048, int height = 2048)
        {
            Width = width;
            Height = height;
            LightSpaceMatrix = Matrix4.Identity;

            CreateDepthFramebuffer();
            BuildDepthShader();
        }

        private void CreateDepthFramebuffer()
        {
            FboHandle = GL.GenFramebuffer();
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, FboHandle);

            DepthTextureHandle = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, DepthTextureHandle);
            GL.TexImage2D(
                TextureTarget.Texture2D,
                0,
                PixelInternalFormat.DepthComponent32f,
                Width,
                Height,
                0,
                PixelFormat.DepthComponent,
                PixelType.Float,
                IntPtr.Zero);

            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMinFilter.Nearest);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToBorder);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToBorder);

            float[] border = { 1.0f, 1.0f, 1.0f, 1.0f };
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureBorderColor, border);

            GL.FramebufferTexture2D(
                FramebufferTarget.Framebuffer,
                FramebufferAttachment.DepthAttachment,
                TextureTarget.Texture2D,
                DepthTextureHandle,
                0);

            GL.DrawBuffer(DrawBufferMode.None);
            GL.ReadBuffer(ReadBufferMode.None);

            FramebufferErrorCode status = GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
            if (status != FramebufferErrorCode.FramebufferComplete)
            {
                throw new InvalidOperationException($"Incomplete shadow framebuffer: {status}");
            }

            GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        }

        private void BuildDepthShader()
        {
            const string vertSrc = @"
                #version 330 core
                layout (location = 0) in vec3 aPosition;

                uniform mat4 model;
                uniform mat4 lightSpaceMatrix;

                void main()
                {
                    gl_Position = vec4(aPosition, 1.0) * model * lightSpaceMatrix;
                }";

            const string fragSrc = @"
                #version 330 core
                void main() { }";

            int vs = GL.CreateShader(ShaderType.VertexShader);
            GL.ShaderSource(vs, vertSrc);
            GL.CompileShader(vs);
            ValidateShader(vs, "Depth Vertex Shader");

            int fs = GL.CreateShader(ShaderType.FragmentShader);
            GL.ShaderSource(fs, fragSrc);
            GL.CompileShader(fs);
            ValidateShader(fs, "Depth Fragment Shader");

            DepthShaderProgram = GL.CreateProgram();
            GL.AttachShader(DepthShaderProgram, vs);
            GL.AttachShader(DepthShaderProgram, fs);
            GL.LinkProgram(DepthShaderProgram);

            GL.GetProgram(DepthShaderProgram, GetProgramParameterName.LinkStatus, out int ok);
            if (ok == 0)
            {
                throw new InvalidOperationException($"Depth shader link error: {GL.GetProgramInfoLog(DepthShaderProgram)}");
            }

            GL.DetachShader(DepthShaderProgram, vs);
            GL.DetachShader(DepthShaderProgram, fs);
            GL.DeleteShader(vs);
            GL.DeleteShader(fs);

            _modelLoc = GL.GetUniformLocation(DepthShaderProgram, "model");
            _lightSpaceLoc = GL.GetUniformLocation(DepthShaderProgram, "lightSpaceMatrix");
        }

        private static void ValidateShader(int shaderId, string name)
        {
            GL.GetShader(shaderId, ShaderParameter.CompileStatus, out int compiled);
            if (compiled == 0)
            {
                throw new InvalidOperationException($"{name} compile error: {GL.GetShaderInfoLog(shaderId)}");
            }
        }

        public void BeginPass(
            DirectionalLight sun,
            Vector3 targetCenter,
            float halfExtent = 350f,
            float nearDist = -900f,
            float farDist = 1800f)
        {
            Vector3 dir = (sun != null && sun.Direction.LengthSquared > 0.0001f)
                ? Vector3.Normalize(sun.Direction)
                : Vector3.Normalize(new Vector3(-0.45f, -1.0f, -0.35f));

            Vector3 upAxis = MathF.Abs(Vector3.Dot(dir, Vector3.UnitY)) > 0.99f
                ? Vector3.UnitZ
                : Vector3.UnitY;

            float pullBack = MathF.Max(600f, halfExtent * 1.5f);
            Vector3 initialEye = targetCenter - dir * pullBack;
            Matrix4 viewMat = Matrix4.LookAt(initialEye, targetCenter, upAxis);

            float texelSize = (halfExtent * 2.0f) / Width;
            Vector4 snappedLightSpace = new Vector4(targetCenter, 1.0f) * viewMat;
            snappedLightSpace.X = MathF.Floor(snappedLightSpace.X / texelSize) * texelSize;
            snappedLightSpace.Y = MathF.Floor(snappedLightSpace.Y / texelSize) * texelSize;

            Matrix4 invView = viewMat.Inverted();
            Vector4 worldTarget4 = new Vector4(snappedLightSpace.X, snappedLightSpace.Y, snappedLightSpace.Z, 1.0f) * invView;
            Vector3 worldTarget = worldTarget4.Xyz;
            Vector3 snappedEye = worldTarget - dir * pullBack;
            viewMat = Matrix4.LookAt(snappedEye, worldTarget, upAxis);

            Matrix4 projMat = Matrix4.CreateOrthographicOffCenter(
                -halfExtent, halfExtent,
                -halfExtent, halfExtent,
                nearDist, farDist);

            Matrix4 spaceMat = viewMat * projMat;
            LightSpaceMatrix = spaceMat;

            GL.BindFramebuffer(FramebufferTarget.Framebuffer, FboHandle);
            GL.Viewport(0, 0, Width, Height);
            GL.Clear(ClearBufferMask.DepthBufferBit);

            GL.Enable(EnableCap.PolygonOffsetFill);
            GL.PolygonOffset(2.0f, 4.0f);

            GL.UseProgram(DepthShaderProgram);
            if (_lightSpaceLoc != -1)
            {
                GL.UniformMatrix4(_lightSpaceLoc, true, ref spaceMat);
            }
        }

        public void ApplyDepthModel(Matrix4 modelMatrix)
        {
            if (_modelLoc != -1)
            {
                GL.UniformMatrix4(_modelLoc, true, ref modelMatrix);
            }
        }

        public void EndPass(int viewportWidth, int viewportHeight)
        {
            GL.Disable(EnableCap.PolygonOffsetFill);
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
            GL.Viewport(0, 0, viewportWidth, viewportHeight);
        }

        public void BindDepthMap(TextureUnit unit = TextureUnit.Texture1)
        {
            GL.ActiveTexture(unit);
            GL.BindTexture(TextureTarget.Texture2D, DepthTextureHandle);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            if (DepthShaderProgram != 0) GL.DeleteProgram(DepthShaderProgram);
            if (DepthTextureHandle != 0) GL.DeleteTexture(DepthTextureHandle);
            if (FboHandle != 0) GL.DeleteFramebuffer(FboHandle);
        }
    }
}
