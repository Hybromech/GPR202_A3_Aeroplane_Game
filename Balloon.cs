using System;
using System.Collections.Generic;
using System.IO;
using Assimp;
using Assimp.Configs;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using StbImageSharp;

namespace GPR202_Assessment3
{
    public class Balloon : GameObject
    {
        private List<VertexData> vertexdata;
        private List<uint> indices;

        private int balloonShaderProgram;

        // PERFORMANCE OPTIMIZATION: Cached uniform locations.
        // Since multiple balloons are rendered every frame, querying uniform locations 
        // per-balloon per-frame causes severe performance bottlenecks. Caching them eliminates this.
        private int modelLocation;
        private int viewLocation;
        private int projectionLocation;

        private readonly string vertexShaderCode =
            @"
                #version 330 core

                layout (location = 0) in vec3 aPosition;
                layout (location = 1) in vec2 aTexCoord;

                uniform mat4 model;
                uniform mat4 view;
                uniform mat4 projection;

                out vec2 texCoord;

                void main()
                {
                    texCoord = aTexCoord;
                    gl_Position = vec4(aPosition, 1.0f) * model * view * projection;
                }";

        private readonly string fragShaderCode =
        @"
                #version 330 core

                out vec4 pixelColor;
                in vec2 texCoord;

                uniform sampler2D texture0;

                void main()
                {
                    vec4 texColor = texture(texture0, texCoord);
                    pixelColor = texColor;
                }
            ";

        public Balloon()
        {
            ImportModelFile();
            Setup();
        }

        public void Draw(Vector3 pos, Vector3 rot, float scale, Matrix4 view, Matrix4 proj)
        {
            this.position = pos;
            this.rotation = rot;
            this.scale = scale;

            model = Matrix4.Identity * Matrix4.CreateScale(scale) * Matrix4.CreateRotationX(rotation.X) * Matrix4.CreateRotationY(rotation.Y) * Matrix4.CreateTranslation(position);

            GL.UseProgram(balloonShaderProgram);

            // PERFORMANCE OPTIMIZATION: Use cached uniform locations instead of GL.GetUniformLocation.
            GL.UniformMatrix4(modelLocation, true, ref model);
            GL.UniformMatrix4(viewLocation, true, ref view);
            GL.UniformMatrix4(projectionLocation, true, ref proj);

            GL.BindVertexArray(vao);
            GL.ActiveTexture(TextureUnit.Texture0);
            GL.BindTexture(TextureTarget.Texture2D, textureHandle);

            GL.DrawElements(OpenTK.Graphics.OpenGL4.PrimitiveType.Triangles, indices.Count, DrawElementsType.UnsignedInt, 0);
        }

        private void Setup()
        {
            vbo = GL.GenBuffer();
            vao = GL.GenVertexArray();
            GL.BindVertexArray(vao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, vbo);

            List<float> vbodata = new List<float>();
            for (int i = 0; i < vertexdata.Count; i++)
            {
                vbodata.Add(vertexdata[i].x);
                vbodata.Add(vertexdata[i].y);
                vbodata.Add(vertexdata[i].z);
                vbodata.Add(vertexdata[i].u);
                vbodata.Add(vertexdata[i].v);
            }
            float[] _vbo = vbodata.ToArray();

            GL.BufferData(BufferTarget.ArrayBuffer, sizeof(float) * _vbo.Length, _vbo, BufferUsageHint.StaticDraw);
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 5 * sizeof(float), 0);
            GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, 5 * sizeof(float), 3 * sizeof(float));
            GL.EnableVertexAttribArray(1);

            ebo = GL.GenBuffer();
            uint[] _ebo = indices.ToArray();
            GL.BindBuffer(BufferTarget.ElementArrayBuffer, ebo);
            GL.BufferData(BufferTarget.ElementArrayBuffer, _ebo.Length * sizeof(uint), _ebo, BufferUsageHint.StaticDraw);

            int vertexShaderObject = GL.CreateShader(ShaderType.VertexShader);
            GL.ShaderSource(vertexShaderObject, vertexShaderCode);
            GL.CompileShader(vertexShaderObject);

            int fragmentShaderObject = GL.CreateShader(ShaderType.FragmentShader);
            GL.ShaderSource(fragmentShaderObject, fragShaderCode);
            GL.CompileShader(fragmentShaderObject);

            balloonShaderProgram = GL.CreateProgram();
            GL.AttachShader(balloonShaderProgram, vertexShaderObject);
            GL.AttachShader(balloonShaderProgram, fragmentShaderObject);
            GL.LinkProgram(balloonShaderProgram);

            GL.DetachShader(balloonShaderProgram, vertexShaderObject);
            GL.DetachShader(balloonShaderProgram, fragmentShaderObject);
            GL.DeleteShader(vertexShaderObject);
            GL.DeleteShader(fragmentShaderObject);

            // PERFORMANCE OPTIMIZATION: Cache uniform locations once after shader linking.
            modelLocation = GL.GetUniformLocation(balloonShaderProgram, "model");
            viewLocation = GL.GetUniformLocation(balloonShaderProgram, "view");
            projectionLocation = GL.GetUniformLocation(balloonShaderProgram, "projection");

            GL.UseProgram(balloonShaderProgram);
        }

        private void ImportModelFile()
        {
            string modelName = "balloon.obj";
            string fileName = "Resource/Model/" + modelName;

            AssimpContext importer = new AssimpContext();
            NormalSmoothingAngleConfig config = new NormalSmoothingAngleConfig(66.0f);
            importer.SetConfig(config);

            Scene model = importer.ImportFile(fileName, PostProcessSteps.Triangulate);

            vertexdata = new List<VertexData>();
            for (int i = 0; i < model.Meshes[0].VertexCount; i++)
            {
                VertexData vertex =
                    new VertexData(model.Meshes[0].Vertices[i].X, model.Meshes[0].Vertices[i].Y, model.Meshes[0].Vertices[i].Z,
                                       model.Meshes[0].TextureCoordinateChannels[0][i].X, model.Meshes[0].TextureCoordinateChannels[0][i].Y);
                vertexdata.Add(vertex);
            }

            indices = new List<uint>();
            for (int i = 0; i < model.Meshes[0].FaceCount; i++)
            {
                Assimp.Face face = model.Meshes[0].Faces[i];
                for (int j = 0; j < face.IndexCount; j++)
                    indices.Add((uint)face.Indices[j]);
            }

            textureHandle = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, textureHandle);

            StbImage.stbi_set_flip_vertically_on_load(1);
            string fullpath = "Resource/Model/balloon.png";
            if (!File.Exists(fullpath))
            {
                throw new FileNotFoundException("Texture not found at '" + fullpath + "'");
            }
            using (var stream = File.OpenRead(fullpath))
            {
                ImageResult image = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);
                GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, image.Width, image.Height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, image.Data);
            }
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMinFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)OpenTK.Graphics.OpenGL4.TextureWrapMode.ClampToEdge);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)OpenTK.Graphics.OpenGL4.TextureWrapMode.ClampToEdge);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapR, (int)OpenTK.Graphics.OpenGL4.TextureWrapMode.ClampToEdge);

            importer.Dispose();
        }
    }
}