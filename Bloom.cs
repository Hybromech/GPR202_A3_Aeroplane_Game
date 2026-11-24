using System;
using OpenTK.Graphics.OpenGL4;

namespace GPR202_Assessment3
{
    // LearnOpenGL bloom: HDR scene -> bright extraction -> separable blur -> addition.
    // Extraction is a fullscreen pass so all existing scene shaders participate.
    public sealed class Bloom : IDisposable
    {
        private readonly ShadowSettings settings;
        private readonly int[] targets = new int[3];
        private readonly int[] textures = new int[3];
        private int depth, vao, extract, blur, composite;
        private int thresholdLocation, directionLocation, strengthLocation;
        private int width, height;
        private bool disposed;

        private const string Vertex = @"#version 330 core
            out vec2 uv;
            void main() {
                vec2 p = vec2(float((gl_VertexID << 1) & 2), float(gl_VertexID & 2));
                uv = p;
                gl_Position = vec4(p * 2.0 - 1.0, 0.0, 1.0);
            }";
        private const string Extract = @"#version 330 core
            in vec2 uv;
            out vec4 color;
            uniform sampler2D source;
            uniform float threshold;
            void main() {
                vec3 c = texture(source, uv).rgb;
                color = vec4(dot(c, vec3(0.2126, 0.7152, 0.0722)) > threshold ? c : vec3(0), 1);
            }";
        private const string Blur = @"#version 330 core
            in vec2 uv;
            out vec4 color;
            uniform sampler2D source;
            uniform vec2 direction;
            void main() {
                float w[5] = float[](0.2270270270, 0.1945945946, 0.1216216216, 0.0540540541, 0.0162162162);
                vec2 stepSize = direction / vec2(textureSize(source, 0));
                vec3 c = texture(source, uv).rgb * w[0];
                for (int i = 1; i < 5; ++i) {
                    c += (texture(source, uv + stepSize * float(i)).rgb
                        + texture(source, uv - stepSize * float(i)).rgb) * w[i];
                }
                color = vec4(c, 1);
            }";
        private const string Composite = @"#version 330 core
            in vec2 uv;
            out vec4 color;
            uniform sampler2D source;
            uniform sampler2D glow;
            uniform float strength;
            void main() {
                vec4 scene = texture(source, uv);
                color = vec4(scene.rgb + texture(glow, uv).rgb * strength, scene.a);
            }";

        public Bloom(ShadowSettings settings)
        {
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
            try
            {
                vao = GL.GenVertexArray();
                extract = CreateProgram(Extract);
                blur = CreateProgram(Blur);
                composite = CreateProgram(Composite);
                thresholdLocation = GL.GetUniformLocation(extract, "threshold");
                directionLocation = GL.GetUniformLocation(blur, "direction");
                strengthLocation = GL.GetUniformLocation(composite, "strength");
                foreach (int program in new[] { extract, blur, composite })
                {
                    GL.UseProgram(program);
                    GL.Uniform1(GL.GetUniformLocation(program, "source"), 0);
                }
                GL.Uniform1(GL.GetUniformLocation(composite, "glow"), 1);
                GL.UseProgram(0);
            }
            catch { Dispose(); throw; }
        }

        // Called after the shadow pass, before the main scene clear.
        public void BeginScene(int framebufferWidth, int framebufferHeight)
        {
            if (disposed) throw new ObjectDisposedException(nameof(Bloom));
            Resize(Math.Max(1, framebufferWidth), Math.Max(1, framebufferHeight));
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, targets[0]);
            GL.Viewport(0, 0, width, height);
        }

        // Terminal render pass: leaves the window framebuffer bound for swapping.
        public void EndScene()
        {
            if (disposed) throw new ObjectDisposedException(nameof(Bloom));
            if (width == 0) throw new InvalidOperationException("BeginScene must run first.");
            bool depthTest = GL.IsEnabled(EnableCap.DepthTest);
            bool blend = GL.IsEnabled(EnableCap.Blend);
            bool cull = GL.IsEnabled(EnableCap.CullFace);
            GL.Disable(EnableCap.DepthTest);
            GL.Disable(EnableCap.Blend);
            GL.Disable(EnableCap.CullFace);
            GL.BindVertexArray(vao);
            GL.Viewport(0, 0, width, height);
            try
            {
                float strength = settings.BloomEnabled ? Math.Max(0f, settings.BloomStrength) : 0f;
                if (strength > 0f)
                {
                    GL.UseProgram(extract);
                    GL.Uniform1(thresholdLocation, Math.Max(0f, settings.BloomThreshold));
                    Pass(targets[1], textures[0]);
                    GL.UseProgram(blur);
                    int iterations = Math.Clamp(settings.BloomBlurIterations, 1, 20);
                    for (int i = 0; i < iterations; ++i)
                    {
                        GL.Uniform2(directionLocation, 1f, 0f);
                        Pass(targets[2], textures[1]);
                        GL.Uniform2(directionLocation, 0f, 1f);
                        Pass(targets[1], textures[2]);
                    }
                }
                GL.UseProgram(composite);
                GL.Uniform1(strengthLocation, strength);
                GL.ActiveTexture(TextureUnit.Texture1);
                GL.BindTexture(TextureTarget.Texture2D, strength > 0f ? textures[1] : textures[0]);
                Pass(0, textures[0]);
            }
            finally
            {
                GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
                GL.BindVertexArray(0);
                GL.UseProgram(0);
                GL.ActiveTexture(TextureUnit.Texture1);
                GL.BindTexture(TextureTarget.Texture2D, 0);
                GL.ActiveTexture(TextureUnit.Texture0);
                GL.BindTexture(TextureTarget.Texture2D, 0);
                if (depthTest) GL.Enable(EnableCap.DepthTest);
                if (blend) GL.Enable(EnableCap.Blend);
                if (cull) GL.Enable(EnableCap.CullFace);
            }
        }

        private static void Pass(int framebuffer, int texture)
        {
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, framebuffer);
            GL.ActiveTexture(TextureUnit.Texture0);
            GL.BindTexture(TextureTarget.Texture2D, texture);
            GL.DrawArrays(PrimitiveType.Triangles, 0, 3);
        }

        private void Resize(int w, int h)
        {
            if (width == w && height == h) return;
            DeleteTargets();
            GL.ActiveTexture(TextureUnit.Texture0);
            try
            {
                for (int i = 0; i < 3; ++i)
                {
                    targets[i] = GL.GenFramebuffer();
                    textures[i] = GL.GenTexture();
                    GL.BindFramebuffer(FramebufferTarget.Framebuffer, targets[i]);
                    GL.BindTexture(TextureTarget.Texture2D, textures[i]);
                    GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba16f,
                        w, h, 0, PixelFormat.Rgba, PixelType.Float, IntPtr.Zero);
                    GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
                    GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
                    GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
                    GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
                    GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0,
                        TextureTarget.Texture2D, textures[i], 0);
                    GL.DrawBuffer(DrawBufferMode.ColorAttachment0);
                    if (i == 0)
                    {
                        depth = GL.GenRenderbuffer();
                        GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, depth);
                        GL.RenderbufferStorage(RenderbufferTarget.Renderbuffer, RenderbufferStorage.Depth24Stencil8, w, h);
                        GL.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthStencilAttachment,
                            RenderbufferTarget.Renderbuffer, depth);
                    }
                    var status = GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
                    if (status != FramebufferErrorCode.FramebufferComplete)
                        throw new InvalidOperationException($"Bloom framebuffer incomplete: {status}");
                }
                width = w;
                height = h;
            }
            catch { DeleteTargets(); throw; }
            finally
            {
                GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
                GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, 0);
                GL.BindTexture(TextureTarget.Texture2D, 0);
            }
        }

        private static int Compile(ShaderType type, string source)
        {
            int shader = GL.CreateShader(type);
            GL.ShaderSource(shader, source);
            GL.CompileShader(shader);
            GL.GetShader(shader, ShaderParameter.CompileStatus, out int ok);
            if (ok != 0) return shader;
            string log = GL.GetShaderInfoLog(shader);
            GL.DeleteShader(shader);
            throw new InvalidOperationException("Bloom shader compilation: " + log);
        }

        private static int CreateProgram(string source)
        {
            int vertex = 0, fragment = 0, program = 0;
            try
            {
                vertex = Compile(ShaderType.VertexShader, Vertex);
                fragment = Compile(ShaderType.FragmentShader, source);
                program = GL.CreateProgram();
                GL.AttachShader(program, vertex);
                GL.AttachShader(program, fragment);
                GL.LinkProgram(program);
                GL.GetProgram(program, GetProgramParameterName.LinkStatus, out int ok);
                if (ok == 0) throw new InvalidOperationException("Bloom shader link: " + GL.GetProgramInfoLog(program));
                GL.DetachShader(program, vertex);
                GL.DetachShader(program, fragment);
                return program;
            }
            catch { if (program != 0) GL.DeleteProgram(program); throw; }
            finally
            {
                if (vertex != 0) GL.DeleteShader(vertex);
                if (fragment != 0) GL.DeleteShader(fragment);
            }
        }

        private void DeleteTargets()
        {
            for (int i = 0; i < 3; ++i)
            {
                if (textures[i] != 0) GL.DeleteTexture(textures[i]);
                if (targets[i] != 0) GL.DeleteFramebuffer(targets[i]);
                textures[i] = targets[i] = 0;
            }
            if (depth != 0) GL.DeleteRenderbuffer(depth);
            depth = width = height = 0;
        }

        // Must be called while the OpenGL context is current.
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            DeleteTargets();
            if (extract != 0) GL.DeleteProgram(extract);
            if (blur != 0) GL.DeleteProgram(blur);
            if (composite != 0) GL.DeleteProgram(composite);
            if (vao != 0) GL.DeleteVertexArray(vao);
        }
    }
}
