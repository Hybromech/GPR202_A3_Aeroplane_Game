using System;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;

namespace GPR202_Assessment3
{
    // Upright camera-facing plane, anchored at its bottom centre.
    public sealed class BillBoard : IDisposable
    {
        public Vector3 Position { get; set; }
        public Vector2 Size { get; set; } = new Vector2(48f, 32f);
        private int program, vao;
        private readonly int viewLocation, projectionLocation, positionLocation, rightLocation, sizeLocation;
        private bool disposed;

        public BillBoard(Vector3 position)
        {
            Position = position;
            int vertex = 0, fragment = 0;
            try
            {
                vertex = Compile(ShaderType.VertexShader, @"#version 330 core
                    uniform mat4 view;
                    uniform mat4 projection;
                    uniform vec3 anchor;
                    uniform vec3 right;
                    uniform vec2 size;
                    out vec2 uv;
                    void main() {
                        vec2 corners[6] = vec2[](vec2(0,0),vec2(1,0),vec2(1,1),
                            vec2(0,0),vec2(1,1),vec2(0,1));
                        uv = corners[gl_VertexID];
                        vec3 world = anchor + right * ((uv.x - 0.5) * size.x)
                            + vec3(0, uv.y * size.y, 0);
                        gl_Position = vec4(world, 1) * view * projection;
                    }");
                fragment = Compile(ShaderType.FragmentShader, @"#version 330 core
                    in vec2 uv;
                    out vec4 color;
                    void main() {
                        // Procedural mountain marker, requiring no external texture asset.
                        vec3 ink = vec3(0.035, 0.12, 0.18);
                        float border = min(min(uv.x, 1.0-uv.x), min(uv.y, 1.0-uv.y));
                        if (border < 0.04) ink = vec3(1.0, 0.65, 0.12);
                        float peak = 0.78 - abs(uv.x - 0.5) * 1.25;
                        if (uv.y > 0.2 && uv.y < peak)
                            ink = uv.y > 0.58 ? vec3(1.0) : vec3(0.18, 0.65, 0.62);
                        color = vec4(ink, 1);
                    }");
                program = GL.CreateProgram();
                GL.AttachShader(program, vertex);
                GL.AttachShader(program, fragment);
                GL.LinkProgram(program);
                GL.GetProgram(program, GetProgramParameterName.LinkStatus, out int ok);
                if (ok == 0) throw new InvalidOperationException(GL.GetProgramInfoLog(program));
                GL.DetachShader(program, vertex);
                GL.DetachShader(program, fragment);
                vao = GL.GenVertexArray();
                viewLocation = GL.GetUniformLocation(program, "view");
                projectionLocation = GL.GetUniformLocation(program, "projection");
                positionLocation = GL.GetUniformLocation(program, "anchor");
                rightLocation = GL.GetUniformLocation(program, "right");
                sizeLocation = GL.GetUniformLocation(program, "size");
            }
            catch { Dispose(); throw; }
            finally
            {
                if (vertex != 0) GL.DeleteShader(vertex);
                if (fragment != 0) GL.DeleteShader(fragment);
            }
        }

        public void Draw(Matrix4 view, Matrix4 projection, Vector3 cameraPosition)
        {
            if (disposed) throw new ObjectDisposedException(nameof(BillBoard));
            Vector3 facing = cameraPosition - Position;
            facing.Y = 0f;
            Vector3 right = facing.LengthSquared > 0.0001f
                ? Vector3.Normalize(Vector3.Cross(Vector3.UnitY, facing)) : Vector3.UnitX;
            bool culling = GL.IsEnabled(EnableCap.CullFace);
            GL.Disable(EnableCap.CullFace);
            GL.UseProgram(program);
            GL.UniformMatrix4(viewLocation, true, ref view);
            GL.UniformMatrix4(projectionLocation, true, ref projection);
            GL.Uniform3(positionLocation, Position);
            GL.Uniform3(rightLocation, right);
            GL.Uniform2(sizeLocation, Size);
            GL.BindVertexArray(vao);
            GL.DrawArrays(PrimitiveType.Triangles, 0, 6);
            GL.BindVertexArray(0);
            if (culling) GL.Enable(EnableCap.CullFace);
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
            throw new InvalidOperationException("Billboard shader: " + log);
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (vao != 0) GL.DeleteVertexArray(vao);
            if (program != 0) GL.DeleteProgram(program);
        }
    }
}
